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
    /// 핵심 불변식(Exact CAS, 설계 원칙 A):
    /// 어떤 lease의 복원/삭제도 "현재 클립보드 == lease.WrittenText" 일 때만 수행한다.
    /// 값이 다르면 사용자 복사본이거나 다른 세션의 메시지이므로 절대 손대지 않는다.
    /// 이 불변식 하나로 ACK/SYNC_REQUEST/다른 세션 메시지 간 상호 훼손이 구조적으로 불가능해진다 —
    /// lease가 교체되면 새 lease가 쓴 텍스트로 클립보드가 바뀌므로, 이전 lease의 CAS는 자동으로 실패한다.
    /// </summary>
    public sealed class ClipboardProtocolLeaseManager
    {
        private readonly IClipboardAccessor _accessor;
        private readonly Func<string, bool> _looksLikeOwnProtocolNoise;
        private readonly Action<string> _log;

        private readonly object _sync = new object();
        private ClipboardProtocolLease _activeLease;
        private int _generation;

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
                Log("SYNC_REFRESH_SKIPPED_DISPLACED", requestId, lease);
                return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Displaced, Lease = lease };
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

            // 사용자가 새 내용을 복사했거나 알 수 없는 다른 값이 올라와 있다 — 절대 덮지 않는다.
            lease.IsDisplaced = true;
            Log("SYNC_REFRESH_DISPLACED", requestId, lease);
            return new ClipboardWriteResult { Outcome = ClipboardWriteOutcome.Displaced, Lease = lease };
        }

        /// <summary>
        /// SYNC_REQUEST 종료(성공/실패/취소/타임아웃/예외 공통 경로). 활성 SyncRequest lease가
        /// 있고 현재 클립보드가 정확히 그 값일 때만 사용자 백업 복원 또는 Clear.
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
        /// ACK hold 경과 시점에 호출. 현재 클립보드가 정확히 이 lease가 쓴 ACK 문구일 때만
        /// 사용자 백업을 복원(또는 백업이 없었으면 Clear)한다. 그 사이 사용자가 새로 복사했거나
        /// 다른 Payload/ACK/SYNC_REQUEST가 도착했으면 아무것도 하지 않는다.
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
        /// 임의의 lease를 exact CAS로 종료한다(복원 또는 Clear). 이미 다른 lease로 교체되었거나
        /// 현재 클립보드가 더 이상 lease.WrittenText가 아니면 아무것도 하지 않고 false.
        /// </summary>
        public bool TryEndLease(ClipboardProtocolLease lease)
        {
            if (lease == null)
                return false;

            lock (_sync)
            {
                if (!ReferenceEquals(_activeLease, lease))
                {
                    // 이미 다른 lease가 이어받음 — 이 lease는 자기 몫이 없다.
                    return false;
                }
            }

            string current;
            bool got = _accessor.TryGetText(out current);
            string currentForCompare = got ? (current ?? string.Empty) : string.Empty;
            string writtenForCompare = lease.WrittenText ?? string.Empty;
            bool exact = string.Equals(currentForCompare, writtenForCompare, StringComparison.Ordinal);

            if (!exact)
            {
                lease.IsDisplaced = true;
                Log("LEASE_END_SKIPPED_NOT_EXACT", lease.RequestId, lease);
                ClearIfStillActive(lease);
                return false;
            }

            bool acted = false;
            try
            {
                if (!string.IsNullOrEmpty(lease.UserBackup))
                {
                    _accessor.SetText(lease.UserBackup);
                    acted = true;
                    Log("LEASE_END_RESTORED_BACKUP", lease.RequestId, lease);
                }
                else
                {
                    _accessor.Clear();
                    acted = true;
                    Log("LEASE_END_CLEARED_NO_BACKUP", lease.RequestId, lease);
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
        /// 백업 후보를 정한다. 현재 값이 진짜 사용자 텍스트면 그것을 백업한다.
        /// 비어 있거나 우리 자신의 프로토콜 잔재(예: 직전 ACK/SYNC_REQUEST)라면, 그 프로토콜
        /// 잔재를 남긴 이전 lease가 들고 있던 UserBackup을 그대로 이어받는다 — 그렇지 않으면
        /// ACK → SYNC_REQUEST처럼 프로토콜 쓰기가 연쇄될 때 맨 처음 사용자 텍스트를 영영 잃는다.
        /// </summary>
        private string ResolveBackup(string current)
        {
            if (!string.IsNullOrEmpty(current) && !_looksLikeOwnProtocolNoise(current))
                return current;

            lock (_sync)
                return _activeLease != null ? _activeLease.UserBackup : null;
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
