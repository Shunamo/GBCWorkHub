using System;
using System.Security.Cryptography;
using System.Text;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>
    /// GBCWORKHUB_ACK:: / GBCWORKHUB_TFS_SYNC_REQUEST:: 클립보드 프로토콜의 상태 기계.
    /// 인스턴스 기반이며 <see cref="IClipboardAccessor"/>를 주입받으므로 실제 Clipboard 없이
    /// 결정론적 단위 테스트가 가능하다. Thread.Sleep/타이머를 직접 사용하지 않는다 —
    /// ACK의 "hold 경과"는 <see cref="CompleteAckLease"/>를 호출자가 원하는 시점에 부르는 것으로 표현한다
    /// (운영 코드에서는 Task.Delay 이후 호출).
    ///
    /// 핵심 규칙(prefix):
    /// 클립보드가 GBCWORKHUB 로 시작하면 우리 프로토콜이다 → 사용자 백업 복원, 백업이 없으면 프로토콜만 정리.
    /// prefix가 없으면 사용자가 복사한 값이다 → 절대 건드리지 않는다.
    /// 빈 클립보드(RDP 종료 후 비움)는 prefix가 아니지만, 백업이 있으면 복원한다.
    /// 예외: 아직 수집하지 않은 GBCWORKHUB_TFS:: / SESSION_RESULT 는 로컬이 쓴 잔여물이 아니므로 덮지 않는다.
    /// </summary>
    public sealed class ClipboardProtocolLeaseManager
    {
        private readonly IClipboardAccessor _accessor;
        private readonly Func<string, bool> _looksLikeOwnProtocolNoise;
        private readonly Action<string> _log;

        private readonly object _sync = new object();
        private ClipboardProtocolLease _activeLease;
        private int _generation;
        /// <summary>프로토콜 쓰기 전에 잡아 둔 마지막 사용자 텍스트. RDP 종료 후 rdpclip이 비워도 로컬에서 되돌린다.</summary>
        private string _lastGoodUserBackup;

        /// <param name="accessor">클립보드 IO. 테스트에서는 fake 주입.</param>
        /// <param name="looksLikeOwnProtocolNoise">
        /// "GBCWORKHUB"로 시작하는 우리 프로토콜 문구인지 판별 — 사용자 백업 캡처 시 이런 값은 백업하지 않는다.
        /// </param>
        /// <param name="log">진단 로그 싱크(선택). 원문 전체를 넘기지 말 것 — 이 클래스는 항상 해시/길이만 넘긴다.</param>
        public ClipboardProtocolLeaseManager(
            IClipboardAccessor accessor,
            Func<string, bool> looksLikeOwnProtocolNoise,
            Action<string> log = null)
        {
            if (accessor == null)
                throw new ArgumentNullException("accessor");
            if (looksLikeOwnProtocolNoise == null)
                throw new ArgumentNullException("looksLikeOwnProtocolNoise");

            _accessor = accessor;
            _looksLikeOwnProtocolNoise = looksLikeOwnProtocolNoise;
            _log = log ?? delegate { };
        }

        public ClipboardProtocolLease GetActiveLeaseSnapshot()
        {
            lock (_sync)
                return _activeLease;
        }

        // ---------------------------------------------------------------
        // SYNC_REQUEST (sticky — 완료/취소 전까지 자동 복원 없음)
        // ---------------------------------------------------------------

        /// <summary>최초 SYNC_REQUEST 기록. 현재 사용자 클립보드를 백업하고 새 lease를 시작한다.</summary>
        public ClipboardWriteResult BeginSyncRequest(
            string requestId,
            string text,
            Func<string, bool> isBlockingForeignPayload)
        {
            if (string.IsNullOrEmpty(text))
                return Fail();

            string current;
            bool got = _accessor.TryGetText(out current);

            if (got && string.Equals(current, text, StringComparison.Ordinal))
            {
                var already = AdoptExistingAsLease(ClipboardLeaseKind.SyncRequest, requestId, text, backup: null);
                Log("SYNC_BEGIN_ALREADY_CURRENT", requestId, already);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.AlreadyCurrent, Lease = already };
            }

            if (got && isBlockingForeignPayload != null && isBlockingForeignPayload(current))
            {
                Log("SYNC_BEGIN_SKIPPED_FOREIGN_PAYLOAD", requestId, null);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.SkippedForeignInboundPresent };
            }

            string backupText = ResolveBackup(got ? current : null);

            try
            {
                _accessor.SetText(text);
            }
            catch (Exception ex)
            {
                _log("SYNC_BEGIN_EXCEPTION requestId=" + (requestId ?? "-") + " ex=" + ex.GetType().Name);
                return Fail();
            }

            var lease = NewLease(ClipboardLeaseKind.SyncRequest, requestId, text, backupText);
            SetActiveLease(lease);
            Log("SYNC_BEGIN_WRITTEN", requestId, lease);
            return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Written, Lease = lease };
        }

        /// <summary>
        /// 재전송(retry tick). 현재 활성 lease가 없거나 다른 requestId면 BeginSyncRequest로 위임한다
        /// (첫 호출과 재전송 호출부를 통일해 기존 호출부 변경을 없앤다).
        /// 활성 lease가 있고 이미 displaced 상태면 절대 다시 쓰지 않는다.
        /// </summary>
        public ClipboardWriteResult TryRefreshSyncRequest(
            string requestId,
            string text,
            Func<string, bool> isBlockingForeignPayload)
        {
            ClipboardProtocolLease lease;
            lock (_sync)
                lease = _activeLease;

            bool sameLease = lease != null
                && lease.Kind == ClipboardLeaseKind.SyncRequest
                && string.Equals(lease.RequestId ?? string.Empty, requestId ?? string.Empty, StringComparison.Ordinal);

            if (!sameLease)
                return BeginSyncRequest(requestId, text, isBlockingForeignPayload);

            if (lease.IsDisplaced)
            {
                string displacedCurrent;
                bool gotDisplaced = _accessor.TryGetText(out displacedCurrent);
                string displacedText = gotDisplaced ? (displacedCurrent ?? string.Empty) : string.Empty;
                bool canReclaim = !gotDisplaced
                    || string.IsNullOrEmpty(displacedText)
                    || (_looksLikeOwnProtocolNoise(displacedText)
                        && (isBlockingForeignPayload == null || !isBlockingForeignPayload(displacedText)));
                if (!canReclaim)
                {
                    Log("SYNC_REFRESH_SKIPPED_DISPLACED", requestId, lease);
                    return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Displaced, Lease = lease };
                }
                // GBC status 등으로 displaced된 경우 아래에서 회수
            }

            string current;
            bool got = _accessor.TryGetText(out current);
            string currentForCompare = got ? (current ?? string.Empty) : string.Empty;
            string writtenForCompare = lease.WrittenText ?? string.Empty;

            if (string.Equals(currentForCompare, writtenForCompare, StringComparison.Ordinal))
            {
                if (string.Equals(writtenForCompare, text ?? string.Empty, StringComparison.Ordinal))
                {
                    // Exact CAS: 지금도 내가 쓴 값 그대로다. 불필요한 SetText 생략.
                    Log("SYNC_REFRESH_ALREADY_CURRENT", requestId, lease);
                    return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.AlreadyCurrent, Lease = lease };
                }

                // 여전히 내 소유 구간이므로 최신 내용으로 갱신 가능 (예: payload 세부값 변경).
                try
                {
                    _accessor.SetText(text);
                }
                catch (Exception ex)
                {
                    _log("SYNC_REFRESH_EXCEPTION requestId=" + (requestId ?? "-") + " ex=" + ex.GetType().Name);
                    return Fail();
                }

                lease.WrittenText = text;
                lease.WrittenHash = ComputeHash(text);
                Log("SYNC_REFRESH_REWRITTEN", requestId, lease);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Written, Lease = lease };
            }

            if (got && isBlockingForeignPayload != null && isBlockingForeignPayload(current))
            {
                Log("SYNC_REFRESH_SKIPPED_FOREIGN_PAYLOAD", requestId, lease);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.SkippedForeignInboundPresent, Lease = lease };
            }

            // 원격 접속 확인(GBC status 등)이 SYNC_REQUEST를 덮은 경우 — 사용자 일반 텍스트가 아니면 회수
            if (got
                && !string.IsNullOrEmpty(currentForCompare)
                && _looksLikeOwnProtocolNoise(currentForCompare))
            {
                try
                {
                    _accessor.SetText(text);
                }
                catch (Exception ex)
                {
                    _log("SYNC_REFRESH_EXCEPTION requestId=" + (requestId ?? "-") + " ex=" + ex.GetType().Name);
                    return Fail();
                }

                lease.WrittenText = text;
                lease.WrittenHash = ComputeHash(text);
                lease.IsDisplaced = false;
                Log("SYNC_REFRESH_RECLAIMED_PROTOCOL", requestId, lease);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Written, Lease = lease };
            }

            // 사용자가 새 내용을 복사했거나 알 수 없는 다른 값이 올라와 있다 — 절대 덮지 않는다.
            lease.IsDisplaced = true;
            Log("SYNC_REFRESH_DISPLACED", requestId, lease);
            return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Displaced, Lease = lease };
        }

        /// <summary>
        /// 접속 직후 SYNC_REQUEST 재기록. GBC status 등 프로토콜/빈 클립보드는 덮고,
        /// 미수집 TFS 페이로드·일반 사용자 텍스트는 덮지 않는다. displaced 플래그도 해제 가능.
        /// </summary>
        public ClipboardWriteResult TryForceRefreshSyncRequest(
            string requestId,
            string text,
            Func<string, bool> isBlockingForeignPayload)
        {
            if (string.IsNullOrEmpty(text))
                return Fail();

            ClipboardProtocolLease lease;
            lock (_sync)
                lease = _activeLease;

            bool sameLease = lease != null
                && lease.Kind == ClipboardLeaseKind.SyncRequest
                && string.Equals(lease.RequestId ?? string.Empty, requestId ?? string.Empty, StringComparison.Ordinal);

            if (!sameLease)
                return BeginSyncRequest(requestId, text, isBlockingForeignPayload);

            string current;
            bool got = _accessor.TryGetText(out current);
            string currentText = got ? (current ?? string.Empty) : string.Empty;

            if (got && isBlockingForeignPayload != null && isBlockingForeignPayload(currentText))
            {
                Log("SYNC_FORCE_SKIPPED_FOREIGN_PAYLOAD", requestId, lease);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.SkippedForeignInboundPresent, Lease = lease };
            }

            if (got
                && !string.IsNullOrEmpty(currentText)
                && !_looksLikeOwnProtocolNoise(currentText)
                && !string.Equals(currentText, lease.WrittenText ?? string.Empty, StringComparison.Ordinal))
            {
                lease.IsDisplaced = true;
                Log("SYNC_FORCE_SKIPPED_USER_TEXT", requestId, lease);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Displaced, Lease = lease };
            }

            try
            {
                _accessor.SetText(text);
            }
            catch (Exception ex)
            {
                _log("SYNC_FORCE_EXCEPTION requestId=" + (requestId ?? "-") + " ex=" + ex.GetType().Name);
                return Fail();
            }

            lease.WrittenText = text;
            lease.WrittenHash = ComputeHash(text);
            lease.IsDisplaced = false;
            Log("SYNC_FORCE_REWRITTEN", requestId, lease);
            return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Written, Lease = lease };
        }

        /// <summary>
        /// 사용자가 명시적으로 다시 시도할 때만. 남은 GBCWORKHUB* 프로토콜(미수집 TFS 포함)을
        /// 치워서 새 SYNC_REQUEST를 쓸 수 있게 한다. prefix 없는 사용자 텍스트는 건드리지 않는다.
        /// </summary>
        public bool TryDiscardInboundPayloadForRetry()
        {
            string current;
            bool got = _accessor.TryGetText(out current);
            string currentText = got ? (current ?? string.Empty) : string.Empty;

            if (!HasWorkHubPrefix(currentText))
                return false;

            string restore;
            lock (_sync)
            {
                restore = _activeLease != null && !string.IsNullOrEmpty(_activeLease.UserBackup)
                    ? _activeLease.UserBackup
                    : _lastGoodUserBackup;
                _activeLease = null;
            }

            try
            {
                if (!string.IsNullOrEmpty(restore) && !HasWorkHubPrefix(restore))
                    _accessor.SetText(restore);
                else
                    _accessor.Clear();
                Log("SYNC_RETRY_DISCARDED_INBOUND", null, null);
                return true;
            }
            catch (Exception ex)
            {
                _log("SYNC_RETRY_DISCARD_EX ex=" + ex.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// SYNC_REQUEST 종료(성공/실패/취소/타임아웃/예외 공통 경로).
        /// 현재 클립보드가 GBCWORKHUB* 이거나 비어 있으면 복원/정리. 일반 텍스트는 유지.
        /// </summary>
        public bool TryEndActiveSyncRequestLease()
        {
            ClipboardProtocolLease lease;
            lock (_sync)
                lease = _activeLease;

            if (lease == null || lease.Kind != ClipboardLeaseKind.SyncRequest)
                return false;

            return TryEndLease(lease);
        }

        // ---------------------------------------------------------------
        // ACK (transient — hold 후 자동 복원)
        // ---------------------------------------------------------------

        /// <summary>ACK 문구를 즉시 기록하고 lease를 반환한다. hold 경과 후 <see cref="CompleteAckLease"/>를 호출해야 한다.</summary>
        public ClipboardWriteResult WriteTransientAck(string deliveryId, string text)
        {
            if (string.IsNullOrEmpty(text))
                return Fail();

            string current;
            bool got = _accessor.TryGetText(out current);
            string backupText = ResolveBackup(got ? current : null);

            try
            {
                _accessor.SetText(text);
            }
            catch (Exception ex)
            {
                _log("ACK_WRITE_EXCEPTION deliveryId=" + (deliveryId ?? "-") + " ex=" + ex.GetType().Name);
                return Fail();
            }

            var lease = NewLease(ClipboardLeaseKind.Ack, deliveryId, text, backupText);
            SetActiveLease(lease);
            Log("ACK_WRITTEN", deliveryId, lease);
            return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Written, Lease = lease };
        }

        /// <summary>
        /// ACK hold 경과 시점에 호출. prefix가 있으면 복원/정리, 없으면 사용자 복사를 유지.
        /// </summary>
        public bool CompleteAckLease(ClipboardProtocolLease lease)
        {
            if (lease == null || lease.Kind != ClipboardLeaseKind.Ack)
                return false;
            return TryEndLease(lease);
        }

        // ---------------------------------------------------------------
        // 공통
        // ---------------------------------------------------------------

        /// <summary>
        /// 클립보드 처리 기준은 prefix 하나다.
        /// GBCWORKHUB* 이면 사용자 백업 복원, 백업이 없으면 프로토콜만 정리(Clear).
        /// prefix가 없는 일반 텍스트는 절대 건드리지 않는다.
        /// </summary>
        public bool TryEndLease(ClipboardProtocolLease lease)
        {
            if (lease == null)
                return false;

            lock (_sync)
            {
                if (!ReferenceEquals(_activeLease, lease))
                    return false;
            }

            string current;
            bool got = _accessor.TryGetText(out current);
            string currentText = got ? (current ?? string.Empty) : string.Empty;
            bool hasPrefix = HasWorkHubPrefix(currentText);
            string restore = !string.IsNullOrEmpty(lease.UserBackup)
                ? lease.UserBackup
                : _lastGoodUserBackup;

            if (IsUnreadInboundPayload(currentText))
            {
                lease.IsDisplaced = true;
                Log("LEASE_END_SKIPPED_INBOUND_PAYLOAD", lease.RequestId, lease);
                ClearIfStillActive(lease);
                return false;
            }

            if (!hasPrefix && !string.IsNullOrEmpty(currentText))
            {
                lease.IsDisplaced = true;
                Log("LEASE_END_SKIPPED_NO_PREFIX", lease.RequestId, lease);
                ClearIfStillActive(lease);
                return false;
            }

            bool acted = false;
            try
            {
                if (!string.IsNullOrEmpty(restore))
                {
                    _accessor.SetText(restore);
                    acted = true;
                    Log(hasPrefix ? "LEASE_END_RESTORED_PREFIX" : "LEASE_END_RESTORED_EMPTY",
                        lease.RequestId, lease);
                }
                else if (hasPrefix)
                {
                    _accessor.Clear();
                    acted = true;
                    Log("LEASE_END_CLEARED_PREFIX_NO_BACKUP", lease.RequestId, lease);
                }
            }
            catch (Exception ex)
            {
                _log("LEASE_END_EXCEPTION requestId=" + (lease.RequestId ?? "-") + " ex=" + ex.GetType().Name);
                acted = false;
            }

            ClearIfStillActive(lease);
            return acted;
        }

        private void ClearIfStillActive(ClipboardProtocolLease lease)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeLease, lease))
                    _activeLease = null;
            }
        }

        /// <summary>
        /// 비밀번호 붙여넣기 전에 우리가 올린 아웃바운드 프로토콜만 치운다.
        /// 사용자 텍스트와 미수집 TFS/SESSION_RESULT 는 유지한다.
        /// </summary>
        public bool TryReleaseClipboardForUserCredentials()
        {
            ClipboardProtocolLease lease;
            lock (_sync)
                lease = _activeLease;
            bool acted = false;
            if (lease != null)
                acted = TryEndLease(lease);

            string current;
            bool got = _accessor.TryGetText(out current);
            string currentText = got ? (current ?? string.Empty) : string.Empty;

            if (IsUnreadInboundPayload(currentText))
                return acted;
            if (!string.IsNullOrEmpty(currentText) && !HasWorkHubPrefix(currentText))
                return acted;

            string restore;
            lock (_sync)
                restore = _lastGoodUserBackup;

            try
            {
                if (!string.IsNullOrEmpty(restore) && !HasWorkHubPrefix(restore))
                {
                    _accessor.SetText(restore);
                    Log("CREDENTIALS_RESTORED_BACKUP", null, null);
                    return true;
                }
                if (HasWorkHubPrefix(currentText))
                {
                    _accessor.Clear();
                    Log("CREDENTIALS_CLEARED_PROTOCOL", null, null);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log("CREDENTIALS_RELEASE_EX ex=" + ex.GetType().Name);
                return acted;
            }

            return acted;
        }

        /// <summary>
        /// rdpclip이 비우기 전에, 지금 클립보드가 사용자 텍스트면 그 값을 기억한다.
        /// </summary>
        public void CaptureCurrentAsUserBackupIfReal()
        {
            string current;
            if (!_accessor.TryGetText(out current))
                return;
            if (string.IsNullOrWhiteSpace(current) || _looksLikeOwnProtocolNoise(current))
                return;
            lock (_sync)
                _lastGoodUserBackup = current;
            Log("USER_BACKUP_CAPTURED", null, _activeLease);
        }

        /// <summary>
        /// 클립보드가 비었거나 우리 프로토콜만 남아 있으면, 세션 중 기억한 사용자 텍스트를 로컬에 되돌린다.
        /// mstsc 종료 후 rdpclip이 비운 뒤에 호출한다.
        /// </summary>
        public bool TryRestoreRememberedBackupIfEmptyOrProtocol()
        {
            string backup;
            lock (_sync)
            {
                backup = _lastGoodUserBackup;
                if (string.IsNullOrEmpty(backup) && _activeLease != null)
                    backup = _activeLease.UserBackup;
            }
            if (string.IsNullOrEmpty(backup))
                return false;

            string current;
            bool got = _accessor.TryGetText(out current);
            if (got && IsUnreadInboundPayload(current))
                return false;
            if (got && !string.IsNullOrEmpty(current)
                && !HasWorkHubPrefix(current)
                && !string.Equals(current, backup, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                _accessor.SetText(backup);
                Log("REMEMBERED_BACKUP_RESTORED", null, _activeLease);
                return true;
            }
            catch (Exception ex)
            {
                _log("REMEMBERED_BACKUP_RESTORE_EX ex=" + ex.GetType().Name);
                return false;
            }
        }

        private static bool HasWorkHubPrefix(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.StartsWith("GBCWORKHUB", StringComparison.Ordinal);
        }

        /// <summary>
        /// 원격이 올린 응답. 로컬 lease 정리로 덮으면 수집이 실패한다.
        /// GBCWORKHUB_TFS_SYNC_REQUEST:: 는 여기에 포함되지 않는다(StartsWith GBCWORKHUB_TFS:: 가 아님).
        /// </summary>
        private static bool IsUnreadInboundPayload(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return text.StartsWith("GBCWORKHUB_TFS::", StringComparison.Ordinal)
                || text.StartsWith("GBCWORKHUB_SESSION_RESULT::", StringComparison.Ordinal);
        }

        private string ResolveBackup(string current)
        {
            if (!string.IsNullOrEmpty(current) && !_looksLikeOwnProtocolNoise(current))
            {
                lock (_sync)
                    _lastGoodUserBackup = current;
                return current;
            }

            lock (_sync)
            {
                if (_activeLease != null && !string.IsNullOrEmpty(_activeLease.UserBackup))
                    return _activeLease.UserBackup;
                return _lastGoodUserBackup;
            }
        }

        private ClipboardProtocolLease AdoptExistingAsLease(
            ClipboardLeaseKind kind, string requestId, string text, string backup)
        {
            var lease = NewLease(kind, requestId, text, backup);
            SetActiveLease(lease);
            return lease;
        }

        private ClipboardProtocolLease NewLease(ClipboardLeaseKind kind, string requestId, string text, string backup)
        {
            int gen = System.Threading.Interlocked.Increment(ref _generation);
            return new ClipboardProtocolLease
            {
                Generation = gen,
                Kind = kind,
                RequestId = requestId,
                WrittenText = text,
                WrittenHash = ComputeHash(text),
                UserBackup = backup,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.MaxValue,
                IsDisplaced = false
            };
        }

        private void SetActiveLease(ClipboardProtocolLease lease)
        {
            lock (_sync)
                _activeLease = lease;
        }

        private static ClipboardWriteResult Fail()
        {
            return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Failed };
        }

        private void Log(string evt, string requestId, ClipboardProtocolLease lease)
        {
            _log(evt
                + " requestId=" + (requestId ?? "-")
                + " kind=" + (lease != null ? lease.Kind.ToString() : "-")
                + " gen=" + (lease != null ? lease.Generation.ToString() : "-")
                + " hash=" + (lease != null ? Shorten(lease.WrittenHash) : "-")
                + " len=" + (lease != null && lease.WrittenText != null ? lease.WrittenText.Length.ToString() : "-"));
        }

        private static string Shorten(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "-";
            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        public static string ComputeHash(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
