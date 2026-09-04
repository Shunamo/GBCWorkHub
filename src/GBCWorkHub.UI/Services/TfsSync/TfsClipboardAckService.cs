using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Services;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>
    /// GBCWORKHUB_ACK:: / GBCWORKHUB_TFS_SYNC_REQUEST:: 클립보드 프로토콜의 공개 진입점.
    /// 실제 상태 기계는 <see cref="ClipboardProtocolLeaseManager"/>에 있다 — 여기 있는 메서드
    /// 시그니처는 기존 호출부(TfsSyncCoordinator, TfsPayloadIngestService, RemoteSessionController,
    /// ClipboardMonitorService, RemoteWorkspaceViewModel)를 하나도 바꾸지 않기 위해 그대로 유지한다.
    /// wire format(prefix, JSON 필드)도 전부 동일하다.
    /// </summary>
    public static class TfsClipboardAckService
    {
        public const string AckPrefix = "GBCWORKHUB_ACK::";
        public const string SyncRequestPrefix = "GBCWORKHUB_TFS_SYNC_REQUEST::";

        /// <summary>
        /// Deliberately separate from SyncRequestPrefix: GBCWORKHUB_TFS_SYNC_REQUEST:: presence
        /// is how the remote side (Aurora_SessionAgent.ps1's Invoke-Connect) distinguishes an
        /// ordinary connect from a TFS-sync-reconnect fetch. Sending it on every connect would
        /// make every ordinary connect look like a TFS-fetch reconnect to that already-fielded
        /// script. This prefix carries only SESSION_TOKEN, for the two-point dev-session file
        /// tracking baseline (see GBCWorkHub.SessionAgent/DevSession/SyncRequest.cs on the
        /// remote side) — unrelated to, and non-interfering with, the TFS-sync-reconnect flow.
        /// </summary>
        public const string SessionTokenAnnouncePrefix = "GBCWORKHUB_SESSION_TOKEN::";

        /// <summary>프로토콜 문구를 잠시 올린 뒤 사용자 클립보드 복원 (상태/ACK).</summary>
        private const int TransientHoldMs = 1500;
        /// <summary>원격 세션이 붙은 뒤 TOKEN을 유지하는 시간. 로그인/비번 입력보다 뒤라 짧게.</summary>
        public const int SessionTokenHoldAfterReadyMs = 5000;

        private static readonly ClipboardProtocolLeaseManager Manager = new ClipboardProtocolLeaseManager(
            new WpfClipboardAccessor(),
            ShouldNotBackupAsUserText,
            msg => DiagnosticLogger.Info("CLIPBOARD_LEASE", msg));

        public static bool TryWriteAck(string deliveryId)
        {
            if (string.IsNullOrWhiteSpace(deliveryId))
                return false;

            string text = AckPrefix + deliveryId.Trim();
            var result = Manager.WriteTransientAck(deliveryId.Trim(), text);
            if (result.Outcome == ClipboardWriteOutcome.Failed)
            {
                DiagnosticLogger.Error("TFS_CLIPBOARD_WRITE", "ACK write failed deliveryId=" + deliveryId);
                return false;
            }

            var lease = result.Lease;
            if (lease != null)
                ScheduleAckCompletion(lease, TransientHoldMs);
            return true;
        }

        /// <summary>
        /// GBCWORKHUB_SESSION_TOKEN::{compact JSON} — 원격 세션이 붙은 뒤에만 올린다.
        /// 로그인/비밀번호 붙여넣기 중에는 올리지 않는다.
        /// </summary>
        public static bool TryAnnounceSessionToken(string sessionToken, string requestId)
        {
            return TryAnnounceSessionToken(sessionToken, requestId, TransientHoldMs, null);
        }

        public static bool TryAnnounceSessionToken(string sessionToken, string requestId, int holdMs)
        {
            return TryAnnounceSessionToken(sessionToken, requestId, holdMs, null);
        }

        /// <param name="holdMs">
        /// 원격 세션이 이미 붙은 뒤에만 사용. 로그인 화면에서 길게 들고 있으면
        /// 비밀번호 붙여넣기가 프로토콜 문구로 바뀐다.
        /// </param>
        /// <param name="targetComputerName">
        /// 갤러리/원격 PC명. 원격 SessionAgent는 Environment.MachineName 과 같을 때만 WorkHub 세션으로 본다.
        /// </param>
        public static bool TryAnnounceSessionToken(
            string sessionToken, string requestId, int holdMs, string targetComputerName)
        {
            return TryAnnounceSessionToken(sessionToken, requestId, holdMs, targetComputerName, null);
        }

        /// <param name="remoteIp">
        /// 갤러리 원격 IP. 원격 Agent는 PC명이 달라도 이 IP가 로컬 NIC와 같으면 WorkHub 세션으로 본다.
        /// </param>
        public static bool TryAnnounceSessionToken(
            string sessionToken, string requestId, int holdMs, string targetComputerName, string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(sessionToken))
                return false;

            string body = JsonConvert.SerializeObject(new
            {
                requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId,
                sessionToken = sessionToken.Trim(),
                targetComputerName = string.IsNullOrWhiteSpace(targetComputerName)
                    ? null
                    : targetComputerName.Trim(),
                remoteIp = string.IsNullOrWhiteSpace(remoteIp) ? null : remoteIp.Trim()
            });
            string text = SessionTokenAnnouncePrefix + body;

            var result = Manager.WriteTransientAck(sessionToken.Trim(), text);
            if (result.Outcome == ClipboardWriteOutcome.Failed)
            {
                DiagnosticLogger.Error("TFS_CLIPBOARD_WRITE", "SessionToken announce failed sessionToken=" + sessionToken);
                return false;
            }

            var lease = result.Lease;
            if (lease != null)
                ScheduleAckCompletion(lease, holdMs > 0 ? holdMs : TransientHoldMs);
            return true;
        }

        /// <summary>
        /// mstsc/PMP 비밀번호 붙여넣기 전에 아웃바운드 프로토콜을 치우고
        /// 사용자가 복사해 둔 텍스트(비밀번호 등)를 되돌린다.
        /// </summary>
        public static void PrepareClipboardForCredentials()
        {
            try
            {
                Manager.CaptureCurrentAsUserBackupIfReal();
                bool acted = Manager.TryReleaseClipboardForUserCredentials();
                DiagnosticLogger.Info("CLIPBOARD_CREDENTIALS_PREPARE", "acted=" + acted);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("CLIPBOARD_CREDENTIALS_PREPARE", ex.Message);
            }
        }

        /// <summary>
        /// GBCWORKHUB_TFS_SYNC_REQUEST::{compact JSON} 기록.
        /// 최초 호출과 재전송(retry tick) 호출을 구분하지 않고 그대로 받되, 내부적으로
        /// ClipboardProtocolLeaseManager가 Begin/Refresh를 알아서 판단한다(exact CAS 보호).
        /// </summary>
        public static bool TryWriteSyncRequest(TfsSyncRequestClipboardDto dto, out string clipboardText)
        {
            clipboardText = null;
            if (dto == null)
                return false;

            if (string.IsNullOrWhiteSpace(dto.Type))
                dto.Type = TfsSyncRequestClipboardDto.ExpectedType;

            string json = JsonConvert.SerializeObject(dto, Formatting.None);
            clipboardText = SyncRequestPrefix + json;

            var result = Manager.TryRefreshSyncRequest(dto.RequestId, clipboardText, IsBlockingForeignPayload);

            if (result.Outcome == ClipboardWriteOutcome.SkippedForeignInboundPresent)
            {
                DiagnosticLogger.Info("TFS_SYNC_REQUEST_SKIPPED", "reason=tfs_payload_present");
            }
            else if (result.Outcome == ClipboardWriteOutcome.Displaced)
            {
                DiagnosticLogger.Info("TFS_SYNC_REQUEST_DISPLACED",
                    "requestId=" + (dto.RequestId ?? "-") + " reason=user_or_other_content_present");
            }
            else if (result.Outcome == ClipboardWriteOutcome.Failed)
            {
                DiagnosticLogger.Error("TFS_CLIPBOARD_WRITE", "SYNC_REQUEST write failed requestId=" + (dto.RequestId ?? "-"));
            }

            return result.IsSuccessLike;
        }

        /// <summary>
        /// 원격 접속 확인 직후 SYNC_REQUEST 강제 재기록.
        /// GBC status가 클립보드를 덮어도 다시 올린다(미수집 TFS 페이로드·일반 텍스트는 제외).
        /// </summary>
        public static bool TryForceWriteSyncRequest(TfsSyncRequestClipboardDto dto, out string clipboardText)
        {
            clipboardText = null;
            if (dto == null)
                return false;

            if (string.IsNullOrWhiteSpace(dto.Type))
                dto.Type = TfsSyncRequestClipboardDto.ExpectedType;

            string json = JsonConvert.SerializeObject(dto, Formatting.None);
            clipboardText = SyncRequestPrefix + json;

            var result = Manager.TryForceRefreshSyncRequest(dto.RequestId, clipboardText, IsBlockingForeignPayload);

            if (result.Outcome == ClipboardWriteOutcome.SkippedForeignInboundPresent)
            {
                DiagnosticLogger.Info("TFS_SYNC_REQUEST_SKIPPED", "reason=tfs_payload_present mode=force");
            }
            else if (result.Outcome == ClipboardWriteOutcome.Displaced)
            {
                DiagnosticLogger.Info("TFS_SYNC_REQUEST_DISPLACED",
                    "requestId=" + (dto.RequestId ?? "-") + " reason=user_text_present mode=force");
            }
            else if (result.Outcome == ClipboardWriteOutcome.Failed)
            {
                DiagnosticLogger.Error("TFS_CLIPBOARD_WRITE",
                    "SYNC_REQUEST force write failed requestId=" + (dto.RequestId ?? "-"));
            }
            else
            {
                DiagnosticLogger.Info("TFS_REQUEST_CLIPBOARD_FORCE_WRITTEN",
                    "requestId=" + (dto.RequestId ?? "-")
                    + " outcome=" + result.Outcome
                    + " length=" + clipboardText.Length);
            }

            return result.IsSuccessLike;
        }

        /// <summary>
        /// 다시 시도 전에 남은 TFS/SYNC 프로토콜을 치운다. 이후 TryWriteSyncRequest가 새 prefix를 쓸 수 있다.
        /// </summary>
        public static bool PrepareFreshSyncRequest()
        {
            try
            {
                bool discarded = Manager.TryDiscardInboundPayloadForRetry();
                DiagnosticLogger.Info("TFS_SYNC_RETRY_PREPARE", "discardedInbound=" + discarded);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_SYNC_RETRY_PREPARE", "failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 클립보드에 남은 SYNC_REQUEST를 지우고, 가능하면 사용자 클립보드를 복원한다.
        /// GBCWORKHUB* 이거나 비어 있으면 복원/정리. 일반 텍스트와 미수집 TFS 응답은 유지.
        /// </summary>
        public static bool TryClearSyncRequestIfPresent()
        {
            try
            {
                bool acted = Manager.TryEndActiveSyncRequestLease();
                DiagnosticLogger.Info("TFS_SYNC_REQUEST_CLEARED", "acted=" + acted);
                return acted;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_SYNC_REQUEST_CLEARED", "failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// RDP가 끊긴 뒤 로컬 클립보드가 비었거나 GBCWORKHUB* 만 남아 있으면
        /// 접속 전 사용자 텍스트를 다시 넣는다. prefix가 없는 일반 텍스트는 건드리지 않는다.
        /// </summary>
        private static readonly object RdpclipRestoreSync = new object();
        private static DateTime _rdpclipRestoreArmUntilUtc = DateTime.MinValue;

        public static bool TryRestoreRememberedClipboardAfterRemote()
        {
            try
            {
                bool acted = Manager.TryRestoreRememberedBackupIfEmptyOrProtocol();
                DiagnosticLogger.Info("CLIPBOARD_REMEMBERED_RESTORE", "acted=" + acted);
                return acted;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("CLIPBOARD_REMEMBERED_RESTORE", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// rdpclip이 클립보드를 비우는 그 이벤트를 복원으로 바꾼다.
        /// rdpclip.exe는 못 고치므로, 비움 통지(WM_CLIPBOARDUPDATE empty)에서 사용자 글을 다시 넣는다.
        /// </summary>
        public static void ScheduleRestoreAfterRemoteDisconnect()
        {
            try
            {
                Manager.CaptureCurrentAsUserBackupIfReal();
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("CLIPBOARD_CAPTURE_BEFORE_RDP_END", ex.Message);
            }

            TryClearSyncRequestIfPresent();

            lock (RdpclipRestoreSync)
                _rdpclipRestoreArmUntilUtc = DateTime.UtcNow.AddSeconds(15);

            TryHandleClipboardEmptiedByRdpclip();
        }

        /// <summary>
        /// 클립보드가 비었거나 프로토콜만 남을 때 호출. armed 구간에만 사용자 백업을 넣는다.
        /// </summary>
        public static bool TryHandleClipboardEmptiedByRdpclip()
        {
            lock (RdpclipRestoreSync)
            {
                if (_rdpclipRestoreArmUntilUtc == DateTime.MinValue
                    || DateTime.UtcNow > _rdpclipRestoreArmUntilUtc)
                    return false;
            }

            bool acted = TryRestoreRememberedClipboardAfterRemote();
            if (acted)
            {
                lock (RdpclipRestoreSync)
                    _rdpclipRestoreArmUntilUtc = DateTime.MinValue;
            }
            return acted;
        }

        public static bool TryRestoreUserClipboard()
        {
            var lease = Manager.GetActiveLeaseSnapshot();
            if (lease == null)
                return false;

            try
            {
                bool acted = Manager.TryEndLease(lease);
                DiagnosticLogger.Info("CLIPBOARD_USER_RESTORE", "acted=" + acted);
                return acted;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("CLIPBOARD_USER_RESTORE", ex.Message);
                return false;
            }
        }

        public static bool IsProtocolText(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.StartsWith("GBCWORKHUB", StringComparison.Ordinal);
        }

        /// <summary>
        /// 사용자 복사본이 아님: 프로토콜, 빈 값, 스케줄러 명령 붙여넣기.
        /// 이런 값은 백업하지 않고, 이전에 잡아 둔 사용자 텍스트를 유지한다.
        /// </summary>
        private static bool ShouldNotBackupAsUserText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || IsProtocolText(text))
                return true;
            string t = text.TrimStart();
            return t.StartsWith("powershell.exe -STA", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("powershell -STA", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlockingForeignPayload(string current)
        {
            return !string.IsNullOrEmpty(current)
                && current.StartsWith(TfsClipboardPayloadService.ClipboardPrefix, StringComparison.Ordinal);
        }

        private static void ScheduleAckCompletion(ClipboardProtocolLease lease, int holdMs)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(holdMs).ConfigureAwait(false);
                    Manager.CompleteAckLease(lease);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Warn("CLIPBOARD_TRANSIENT_RESTORE", ex.Message);
                }
            });
        }

        public static string ComputeClipboardHash(string text)
        {
            return ClipboardProtocolLeaseManager.ComputeHash(text);
        }
    }

    public sealed class TfsPendingSession
    {
        public string RemoteIp { get; set; }
        public string RemoteComputerName { get; set; }
        public string SessionToken { get; set; }
        public DateTime SessionStartedAt { get; set; }
        public DateTime SessionEndedAt { get; set; }
        public string Status { get; set; }
        public bool ClipboardOnly { get; set; }
    }

    public static class TfsPendingSessionStore
    {
        private static readonly object Sync = new object();
        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "TfsPendingSessions.json");
            }
        }

        public static System.Collections.Generic.List<TfsPendingSession> Load()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(StorePath))
                        return new System.Collections.Generic.List<TfsPendingSession>();
                    string json = File.ReadAllText(StorePath, Encoding.UTF8);
                    var list = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<TfsPendingSession>>(json);
                    return list ?? new System.Collections.Generic.List<TfsPendingSession>();
                }
                catch
                {
                    return new System.Collections.Generic.List<TfsPendingSession>();
                }
            }
        }

        public static void Save(System.Collections.Generic.List<TfsPendingSession> sessions)
        {
            lock (Sync)
            {
                try
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(sessions ?? new System.Collections.Generic.List<TfsPendingSession>(), Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(StorePath, json, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("TFS_PENDING_STORE", ex.Message);
                }
            }
        }

        public static void Upsert(TfsPendingSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.RemoteIp))
                return;
            var list = Load();
            list.RemoveAll(x => x != null && string.Equals(x.RemoteIp, session.RemoteIp, StringComparison.OrdinalIgnoreCase));
            list.Add(session);
            Save(list);
        }

        public static void Remove(string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
                return;
            var list = Load();
            list.RemoveAll(x => x != null && string.Equals(x.RemoteIp, remoteIp, StringComparison.OrdinalIgnoreCase));
            Save(list);
        }

        public static int Count
        {
            get { return Load().Count; }
        }
    }

    /// <summary>
    /// 같은 점유 세션(PC + 접속 시작 시각)에서 가져오기/나중에를 이미 눌렀으면
    /// 종료 팝업을 다시 띄우지 않는다. CMC 웹 RDP 중복 SessionLost 대비.
    /// 재접속(시작 시각이 새로 찍힘)은 다시 묻는다.
    /// </summary>
    public static class TfsSyncPromptGate
    {
        private static readonly object Sync = new object();

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "TfsSyncPromptHandled.json");
            }
        }

        private sealed class HandledEntry
        {
            public string RemoteIp { get; set; }
            public DateTime SessionStartedAt { get; set; }
            public DateTime HandledAt { get; set; }
            public string Reason { get; set; }
        }

        public static void MarkHandled(string remoteIp, DateTime sessionStartedAt, string reason)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
                return;

            lock (Sync)
            {
                var list = LoadUnlocked();
                list.RemoveAll(x => x != null
                    && string.Equals(x.RemoteIp, remoteIp, StringComparison.OrdinalIgnoreCase));
                list.Add(new HandledEntry
                {
                    RemoteIp = remoteIp.Trim(),
                    SessionStartedAt = sessionStartedAt == default(DateTime)
                        ? DateTime.Now
                        : sessionStartedAt,
                    HandledAt = DateTime.Now,
                    Reason = reason ?? "handled"
                });
                DateTime cutoff = DateTime.Now.AddDays(-7);
                list.RemoveAll(x => x == null || x.HandledAt < cutoff);
                SaveUnlocked(list);
            }

            DiagnosticLogger.Info("TFS_PROMPT_GATE",
                "MarkHandled ip=" + remoteIp
                + " sessionStart=" + sessionStartedAt.ToString("yyyy-MM-dd HH:mm:ss")
                + " reason=" + (reason ?? "-"));
        }

        public static bool ShouldSkipPrompt(string remoteIp, DateTime sessionStartedAt)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
                return false;

            lock (Sync)
            {
                var list = LoadUnlocked();
                HandledEntry hit = null;
                foreach (var x in list)
                {
                    if (x == null)
                        continue;
                    if (!string.Equals(x.RemoteIp, remoteIp, StringComparison.OrdinalIgnoreCase))
                        continue;
                    hit = x;
                    break;
                }

                if (hit == null)
                    return false;

                // 시작 시각이 같으면 같은 점유. (종료 직후 중복 SessionLost)
                bool sameSession = sessionStartedAt != default(DateTime)
                    && hit.SessionStartedAt != default(DateTime)
                    && Math.Abs((hit.SessionStartedAt - sessionStartedAt).TotalSeconds) < 120;

                // 이 점유가 시작된 뒤에 이미 답했으면 스킵.
                // (이전에는 -1분이라, 나중에 직후 재접속하면 새 세션 전체가 막혔음)
                bool alreadyAnsweredThisSession = sessionStartedAt != default(DateTime)
                    && hit.HandledAt >= sessionStartedAt;

                bool skip = sameSession || alreadyAnsweredThisSession;
                if (skip)
                {
                    DiagnosticLogger.Info("TFS_PROMPT_GATE",
                        "SkipPrompt ip=" + remoteIp
                        + " reason=" + (hit.Reason ?? "-")
                        + " handledAt=" + hit.HandledAt.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                return skip;
            }
        }

        private static System.Collections.Generic.List<HandledEntry> LoadUnlocked()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new System.Collections.Generic.List<HandledEntry>();
                string json = File.ReadAllText(StorePath, Encoding.UTF8);
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<HandledEntry>>(json);
                return list ?? new System.Collections.Generic.List<HandledEntry>();
            }
            catch
            {
                return new System.Collections.Generic.List<HandledEntry>();
            }
        }

        private static void SaveUnlocked(System.Collections.Generic.List<HandledEntry> list)
        {
            try
            {
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                    list ?? new System.Collections.Generic.List<HandledEntry>(),
                    Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(StorePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("TFS_PROMPT_GATE", ex.Message);
            }
        }
    }
}
