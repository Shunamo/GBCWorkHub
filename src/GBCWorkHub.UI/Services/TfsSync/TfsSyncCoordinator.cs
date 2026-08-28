using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Models;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Services.TfsSync
{
    public enum TfsSyncFailReason
    {
        None = 0,
        RdpStartFailed,
        Timeout,
        Cancelled,
        ParseFailed,
        ValidationFailed,
        SaveFailed,
        ClipboardEmpty,
        Unknown
    }

    public enum TfsSyncState
    {
        Idle = 0,
        RequestCreated,
        RdpStarting,
        WaitingForConnection,
        WaitingForPayload,
        PayloadReceived,
        Saving,
        AckWritten,
        ClosingRdp,
        Completed,
        Failed,
        TimedOut
    }

    public sealed class TfsSyncRequest
    {
        public string RemoteIp { get; set; }
        public string RemoteComputerName { get; set; }
        public string SessionToken { get; set; }
        public string RequestId { get; set; }
        public DateTime SessionStartedAt { get; set; }
        public DateTime SessionEndedAt { get; set; }
        public string ClipboardHashBefore { get; set; }
        /// <summary>CMC 등: mstsc 재접속 없이 클립보드만으로 TFS 수신</summary>
        public bool ClipboardOnly { get; set; }
    }

    public sealed class TfsSyncResult
    {
        public bool Success { get; set; }
        public TfsSyncFailReason FailReason { get; set; }
        public int ChangesetCount { get; set; }
        public string Message { get; set; }
        public TfsRecentChangesetsPayload Payload { get; set; }
    }

    /// <summary>
    /// TFS 가져오기: 요청 토큰 기록 → 일반과 동일한 mstsc /v: 재접속 → Payload 대기
    /// → 저장/UI → ACK → 1초 대기 → 로컬이 mstsc 종료.
    /// Process.Exited에서는 클립보드 파싱하지 않는다.
    /// </summary>
    public sealed class TfsSyncCoordinator
    {
        public const string DeliveryModePendingReconnect = TfsPayloadIngestService.DeliveryModePendingReconnect;
        // RC는 connect 후 pending TFS를 두 번째로 클립보드에 올리므로 여유 필요
        private const int SyncTimeoutMs = 60000;
        private const int ConnectTimeoutMs = 60000;
        private const int AckSettleMs = 1000;
        /// <summary>SYNC_REQUEST 클립보드 기록 후 mstsc 전 대기 (원격 rdpclip 준비).</summary>
        private const int SyncRequestClipboardSettleMs = 2000;

        private readonly IPopupService _popup;
        private readonly RdpSessionTrackingService _rdp;
        private readonly TfsWorkLogViewModel _tfsVm;
        private readonly Action<string> _selectTfsTab;
        private readonly Action<string> _setGalleryAvailable;
        private readonly Func<int> _getPendingBadgeRefresh;
        private readonly Func<ISet<int>> _getImportedChangesetIds;

        private readonly object _sync = new object();
        private bool _syncInFlight;
        private bool _userCancelled;
        private TfsSyncRequest _activeRequest;
        private TfsSyncState _state = TfsSyncState.Idle;
        private TaskCompletionSource<AcceptedPayload> _payloadWaiter;
        private TaskCompletionSource<bool> _rdpClosedWaiter;
        private int _mstscPid = -1;
        private string _lastDeliveryId;

        private sealed class AcceptedPayload
        {
            public TfsRecentChangesetsPayload Payload { get; set; }
            public string RawClipboard { get; set; }
        }

        public TfsSyncCoordinator(
            IPopupService popup,
            RdpSessionTrackingService rdp,
            TfsWorkLogViewModel tfsVm,
            Action<string> selectTfsTab,
            Action<string> setGalleryAvailable,
            Func<int> getPendingBadgeRefresh,
            Func<ISet<int>> getImportedChangesetIds = null)
        {
            _popup = popup;
            _rdp = rdp;
            _tfsVm = tfsVm;
            _selectTfsTab = selectTfsTab;
            _setGalleryAvailable = setGalleryAvailable;
            _getPendingBadgeRefresh = getPendingBadgeRefresh;
            _getImportedChangesetIds = getImportedChangesetIds;
            if (_popup != null)
                _popup.ProgressCancelled += OnProgressCancelled;
        }

        private void OnProgressCancelled()
        {
            TaskCompletionSource<AcceptedPayload> waiter;
            lock (_sync)
            {
                if (!_syncInFlight)
                    return;
                _userCancelled = true;
                waiter = _payloadWaiter;
            }

            DiagnosticLogger.Info("TFS_SYNC", "User cancelled progress — aborting fetch");
            if (waiter != null)
                waiter.TrySetResult(null);
            CloseSyncRdpSafe("user_cancel");
        }

        private bool IsUserCancelled
        {
            get { lock (_sync) return _userCancelled; }
        }

        public bool IsSyncInFlight
        {
            get { lock (_sync) return _syncInFlight; }
        }

        public TfsSyncState State
        {
            get { lock (_sync) return _state; }
        }

        public bool IsWaitingForPayload
        {
            get
            {
                lock (_sync)
                {
                    return _syncInFlight
                        && (_state == TfsSyncState.WaitingForPayload
                            || _state == TfsSyncState.WaitingForConnection
                            || _state == TfsSyncState.RdpStarting);
                }
            }
        }

        public int PendingCount
        {
            get { return TfsPendingSessionStore.Count; }
        }

        public async Task HandleUserSessionEndedAsync(TfsSyncRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RemoteIp))
                return;

            if (TfsSyncPromptGate.ShouldSkipPrompt(request.RemoteIp, request.SessionStartedAt))
            {
                DiagnosticLogger.Info("TFS_PROMPT_SKIPPED",
                    FormatLog(request, null, "reason=already_handled_this_session"));
                return;
            }

            var popupRequest = new PopupRequest
            {
                Kind = PopupKind.Confirm,
                Icon = PopupIconKind.Question,
                Title = "원격 작업이 종료되었습니다",
                Message = "이번 원격 작업에서 생성된 TFS 체크인 내역을 가져오시겠습니까?",
                Detail = BuildDetail(request),
                DedupKey = "TfsSyncPrompt:" + request.RemoteIp,
                Buttons = new[]
                {
                    new PopupButtonDefinition("가져오기", PopupResultType.Primary, isDefault: true),
                    new PopupButtonDefinition("나중에", PopupResultType.Secondary),
                    new PopupButtonDefinition("이번에는 안 함", PopupResultType.Tertiary, isCancel: true)
                }
            };

            PopupResult result = await _popup.ShowConfirmAsync(popupRequest).ConfigureAwait(true);

            if (result == null || result.ResultType == PopupResultType.None)
                return;

            if (result.IsPrimary)
            {
                DiagnosticLogger.Info("TFS_SYNC_BUTTON_CLICKED", FormatLog(request, null, "button=가져오기"));
                await RunFetchAsync(request).ConfigureAwait(true);
                return;
            }

            if (result.IsSecondary)
            {
                TfsPendingSessionStore.Upsert(new TfsPendingSession
                {
                    RemoteIp = request.RemoteIp,
                    RemoteComputerName = request.RemoteComputerName,
                    SessionToken = request.SessionToken,
                    SessionStartedAt = request.SessionStartedAt,
                    SessionEndedAt = request.SessionEndedAt,
                    Status = "LATER"
                });
                if (_getPendingBadgeRefresh != null)
                    _getPendingBadgeRefresh();
                return;
            }

            // 이번에는 안 함 → 같은 세션에서 팝업 재표시 방지 (CMC 웹 종료 중복 감지)
            TfsSyncPromptGate.MarkHandled(request.RemoteIp, request.SessionStartedAt, "declined");
            TfsPendingSessionStore.Remove(request.RemoteIp);
            if (_getPendingBadgeRefresh != null)
                _getPendingBadgeRefresh();
        }

        public async Task RetryPendingAsync(TfsPendingSession pending)
        {
            if (pending == null)
                return;
            var request = new TfsSyncRequest
            {
                RemoteIp = pending.RemoteIp,
                RemoteComputerName = pending.RemoteComputerName,
                SessionToken = pending.SessionToken,
                SessionStartedAt = pending.SessionStartedAt,
                SessionEndedAt = pending.SessionEndedAt
            };
            DiagnosticLogger.Info("TFS_SYNC_BUTTON_CLICKED", FormatLog(request, null, "button=다시시도"));
            await RunFetchAsync(request).ConfigureAwait(true);
        }

        public bool TryAcceptTfsPayload(TfsRecentChangesetsPayload payload, string rawClipboard)
        {
            TaskCompletionSource<AcceptedPayload> waiter;
            TfsSyncRequest req;
            TfsSyncState state;
            lock (_sync)
            {
                waiter = _payloadWaiter;
                req = _activeRequest;
                state = _state;
            }

            if (waiter == null || req == null || payload == null)
                return false;

            if (state != TfsSyncState.WaitingForPayload
                && state != TfsSyncState.WaitingForConnection
                && state != TfsSyncState.RdpStarting)
            {
                DiagnosticLogger.Warn("TFS_PAYLOAD_REJECTED", FormatLog(req, payload,
                    "reason=not_waiting state=" + state));
                return false;
            }

            string reject;
            if (!TryValidateIncoming(payload, req, out reject))
            {
                DiagnosticLogger.Warn("TFS_PAYLOAD_REJECTED", FormatLog(req, payload,
                    "reason=" + (reject ?? "validation")));
                return false;
            }

            DiagnosticLogger.Info("TFS_PAYLOAD_PARSED", FormatLog(req, payload, "ok=True"));
            DiagnosticLogger.Info("TFS_REQUEST_ID_MATCHED", FormatLog(req, payload, null));

            SetState(TfsSyncState.PayloadReceived, req, payload);
            return waiter.TrySetResult(new AcceptedPayload
            {
                Payload = payload,
                RawClipboard = rawClipboard
            });
        }

        public TfsSyncRequest GetActiveRequestSnapshot()
        {
            lock (_sync)
                return _activeRequest;
        }

        public bool ShouldIgnoreRdpStatusForDb(RdpLaunchPurpose purpose)
        {
            return purpose == RdpLaunchPurpose.TfsSyncReconnect || IsSyncInFlight;
        }

        /// <summary>Process.Exited — 클립보드 파싱 금지, 종료 대기만 해제.</summary>
        public void NotifyReconnectEnded(string reason)
        {
            TfsSyncState state;
            TfsSyncRequest req;
            string deliveryId;
            int pid;
            TaskCompletionSource<bool> closed;
            lock (_sync)
            {
                if (!_syncInFlight)
                    return;
                state = _state;
                req = _activeRequest;
                deliveryId = _lastDeliveryId;
                pid = _mstscPid;
                closed = _rdpClosedWaiter;
            }

            DiagnosticLogger.Info("TFS_TEMP_RDP_EXITED", FormatLog(req, null,
                "deliveryId=" + (deliveryId ?? "-")
                + " mstscPid=" + pid
                + " reason=" + (reason ?? "-")
                + " state=" + state));

            if (closed != null
                && (state == TfsSyncState.ClosingRdp
                    || state == TfsSyncState.AckWritten
                    || state == TfsSyncState.Completed
                    || state == TfsSyncState.TimedOut
                    || state == TfsSyncState.Failed))
            {
                closed.TrySetResult(true);
            }
        }

        /// <summary>
        /// CMC: SYNC_REQUEST를 클립보드에 쓰고, 원격 Agent 응답(GBCWORKHUB_TFS::)을 대기.
        /// mstsc/.rdp 재접속은 하지 않음.
        /// </summary>
        private async Task WaitClipboardOnlyFetchAsync(TfsSyncRequest request)
        {
            await _popup.UpdateProgressAsync("2. TFS 동기화 요청 기록 중 (CMC 클립보드)").ConfigureAwait(true);

            string requestedAtUtc = DateTimeOffset.UtcNow.ToString("o");
            var clipboardDto = new TfsSyncRequestClipboardDto
            {
                Type = TfsSyncRequestClipboardDto.ExpectedType,
                RequestId = request.RequestId,
                TargetComputerName = request.RemoteComputerName,
                RemoteIp = request.RemoteIp,
                RequestedAtUtc = requestedAtUtc,
                SessionStartedAtUtc = ToUtcRoundTrip(request.SessionStartedAt),
                SessionEndedAtUtc = ToUtcRoundTrip(request.SessionEndedAt),
                SessionToken = request.SessionToken
            };

            string clipboardText;
            bool written = TfsClipboardAckService.TryWriteSyncRequest(clipboardDto, out clipboardText);
            DiagnosticLogger.Info("TFS_REQUEST_CLIPBOARD_WRITTEN",
                "mode=CMC_CLIPBOARD_ONLY requestId=" + (request.RequestId ?? "-")
                + " ok=" + written
                + " length=" + (clipboardText != null ? clipboardText.Length : 0));

            if (!written)
            {
                SetState(TfsSyncState.Failed, request, null);
                await FailAndShowAsync(request, new TfsSyncResult
                {
                    FailReason = TfsSyncFailReason.Unknown,
                    Message = "TFS 동기화 요청을 클립보드에 기록하지 못했습니다."
                }).ConfigureAwait(true);
                return;
            }

            SetState(TfsSyncState.WaitingForPayload, request, null);

            // disconnect 때는 클립보드가 이미 끊긴 경우가 많음 → PMP 재오픈으로 connect 유도
            try
            {
                CmcPmpBrowserLauncher.TryOpenAutoLogon();
                DiagnosticLogger.Info("TFS_SYNC", "CMC: reopened AutoLogon for pending TFS delivery");
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_SYNC", "CMC AutoLogon reopen failed: " + ex.Message);
            }

            await _popup.UpdateProgressAsync(
                "3. PMP 재접속 후 TFS 대기 중\n"
                + "(SYNC_REQUEST를 원격에 계속 전달 중 — Agent가 읽으면 Pending TFS를 보냅니다)").ConfigureAwait(true);

            var waiter = new TaskCompletionSource<AcceptedPayload>();
            lock (_sync)
                _payloadWaiter = waiter;

            // CMC: 재접속 시간 포함해 더 길게 대기 + SYNC_REQUEST 주기적 재기록
            int waitMs = Math.Max(SyncTimeoutMs, 120000);
            for (int i = 0; i < waitMs / 500; i++)
            {
                if (IsUserCancelled)
                {
                    await FinishCancelledAsync(request).ConfigureAwait(true);
                    return;
                }

                // 2초마다 SYNC_REQUEST 재기록 — 원격 Agent가 connect 후 읽을 수 있게
                if (i % 4 == 0)
                {
                    string rewrite;
                    bool ok = TfsClipboardAckService.TryWriteSyncRequest(clipboardDto, out rewrite);
                    if (i == 0 || i % 20 == 0)
                    {
                        DiagnosticLogger.Info("TFS_REQUEST_CLIPBOARD_REWRITTEN",
                            "mode=CMC_CLIPBOARD_ONLY requestId=" + (request.RequestId ?? "-")
                            + " ok=" + ok
                            + " tick=" + i);
                    }
                }

                if (waiter.Task.IsCompleted)
                {
                    var acceptedEarly = await waiter.Task.ConfigureAwait(true);
                    if (acceptedEarly != null && acceptedEarly.Payload != null)
                    {
                        await ApplyFetchedPayloadAsync(request, acceptedEarly.Payload, acceptedEarly.RawClipboard, closeRdp: false)
                            .ConfigureAwait(true);
                        return;
                    }
                }

                string raw;
                var payload = TryReadClipboardTfsPayload(request, out raw);
                if (payload != null)
                {
                    await ApplyFetchedPayloadAsync(request, payload, raw, closeRdp: false).ConfigureAwait(true);
                    return;
                }

                await Task.Delay(500).ConfigureAwait(true);
            }

            SetState(TfsSyncState.TimedOut, request, null);
            await FailAndShowAsync(request, new TfsSyncResult
            {
                FailReason = TfsSyncFailReason.Timeout,
                Message = "CMC TFS 클립보드를 받지 못했습니다.\n"
                    + "1) 원격 Logs에 DISCONNECT_TFS_OK / pendingSaved=True 확인\n"
                    + "2) PMP로 다시 접속(connect)해 Pending 전송\n"
                    + "3) C:\\GBCWorkHub\\PendingTfsRecent.json 존재 여부 확인"
            }).ConfigureAwait(true);
        }

        private async Task RunFetchAsync(TfsSyncRequest request)
        {
            lock (_sync)
            {
                if (_syncInFlight)
                    return;
                _syncInFlight = true;
                _userCancelled = false;
                _activeRequest = request;
                _lastDeliveryId = null;
                _mstscPid = -1;
                if (string.IsNullOrWhiteSpace(request.RequestId))
                    request.RequestId = Guid.NewGuid().ToString("N");
                _state = TfsSyncState.RequestCreated;
            }

            AcceptedPayload accepted = null;

            try
            {
                await _popup.ShowProgressAsync(new PopupRequest
                {
                    Title = "TFS 체크인 내역 가져오기",
                    ProgressStepText = "1. 로컬 클립보드 확인 중",
                    ShowCancelOnProgress = true,
                    Icon = PopupIconKind.Info,
                    Kind = PopupKind.Progress
                }).ConfigureAwait(true);

                string existingRaw;
                TfsRecentChangesetsPayload existing = TryReadClipboardTfsPayload(request, out existingRaw);
                if (existing != null)
                {
                    DiagnosticLogger.Info("TFS_SYNC", "Using existing clipboard TFS payload — skip reconnect"
                        + " requestId=" + (existing.RequestId ?? "-"));
                    await ApplyFetchedPayloadAsync(request, existing, existingRaw, closeRdp: false).ConfigureAwait(true);
                    return;
                }

                // CMC: 웹 RDP라 mstsc 재접속 불가. disconnect Agent가 올린 클립보드를 조금 더 기다림.
                if (request.ClipboardOnly)
                {
                    await WaitClipboardOnlyFetchAsync(request).ConfigureAwait(true);
                    return;
                }

                await _popup.UpdateProgressAsync("2. TFS 동기화 요청 기록 중").ConfigureAwait(true);

                string requestedAtUtc = DateTimeOffset.UtcNow.ToString("o");
                var clipboardDto = new TfsSyncRequestClipboardDto
                {
                    Type = TfsSyncRequestClipboardDto.ExpectedType,
                    RequestId = request.RequestId,
                    TargetComputerName = request.RemoteComputerName,
                    RemoteIp = request.RemoteIp,
                    RequestedAtUtc = requestedAtUtc,
                    SessionStartedAtUtc = ToUtcRoundTrip(request.SessionStartedAt),
                    SessionEndedAtUtc = ToUtcRoundTrip(request.SessionEndedAt),
                    SessionToken = request.SessionToken
                };

                string clipboardText;
                bool written = TfsClipboardAckService.TryWriteSyncRequest(clipboardDto, out clipboardText);
                DiagnosticLogger.Info("TFS_REQUEST_CLIPBOARD_WRITTEN",
                    "requestId=" + (request.RequestId ?? "-")
                    + " targetComputerName=" + (clipboardDto.TargetComputerName ?? "-")
                    + " requestedAtUtc=" + requestedAtUtc
                    + " Prefix=" + TfsClipboardAckService.SyncRequestPrefix
                    + " length=" + (clipboardText != null ? clipboardText.Length : 0)
                    + " ok=" + written);

                if (!written)
                {
                    SetState(TfsSyncState.Failed, request, null);
                    await FailAndShowAsync(request, new TfsSyncResult
                    {
                        FailReason = TfsSyncFailReason.Unknown,
                        Message = "TFS 동기화 요청을 클립보드에 기록하지 못했습니다."
                    }).ConfigureAwait(true);
                    return;
                }

                // 원격 Handle-RdpConnect가 SYNC_REQUEST를 일반 접속과 구분하려면
                // rdpclip에 요청이 먼저 보여야 함 → mstsc 직전에 짧게 대기
                await _popup.UpdateProgressAsync("2.5. 동기화 요청 전달 대기 중").ConfigureAwait(true);
                await Task.Delay(SyncRequestClipboardSettleMs).ConfigureAwait(true);

                SetState(TfsSyncState.RdpStarting, request, null);
                await _popup.UpdateProgressAsync("3. 원격 PC 재접속 중").ConfigureAwait(true);

                // AURORA: mstsc /v:IP / RC: 게시 .rdp (점유키가 IP가 아니면)
                string startMsg;
                string launchMode;
                bool started = TryStartTfsReconnectRdp(request, out startMsg, out launchMode);

                if (!started)
                {
                    SetState(TfsSyncState.Failed, request, null);
                    await FailAndShowAsync(request, new TfsSyncResult
                    {
                        FailReason = TfsSyncFailReason.RdpStartFailed,
                        Message = startMsg ?? "원격 재접속 실행 실패"
                    }).ConfigureAwait(true);
                    return;
                }

                lock (_sync)
                {
                    var ids = _rdp.TrackedProcessIds;
                    _mstscPid = ids != null && ids.Count > 0 ? ids[0] : -1;
                }

                DiagnosticLogger.Info("TFS_TEMP_RDP_STARTED", FormatLog(request, null, "launch=" + launchMode));

                // Payload waiter를 먼저 걸어 연결 대기 중 도착분도 받는다
                var waiter = new TaskCompletionSource<AcceptedPayload>();
                lock (_sync)
                    _payloadWaiter = waiter;

                SetState(TfsSyncState.WaitingForConnection, request, null);
                DiagnosticLogger.Info("TFS_WAITING_FOR_CONNECTION", FormatLog(request, null,
                    "timeoutMs=" + ConnectTimeoutMs));
                await _popup.UpdateProgressAsync("4. 원격 접속 확인 대기 중 (최대 60초)").ConfigureAwait(true);

                bool connectedOrPayload = await WaitForConnectionOrPayloadAsync(waiter, ConnectTimeoutMs)
                    .ConfigureAwait(true);

                if (IsUserCancelled)
                {
                    await FinishCancelledAsync(request).ConfigureAwait(true);
                    return;
                }

                if (waiter.Task.IsCompleted)
                {
                    accepted = await waiter.Task.ConfigureAwait(true);
                    if (accepted != null && accepted.Payload != null)
                    {
                        DiagnosticLogger.Info("TFS_PAYLOAD_BEFORE_OR_AT_CONNECT", FormatLog(request, accepted.Payload, null));
                        await ApplyFetchedPayloadAsync(request, accepted.Payload, accepted.RawClipboard, closeRdp: true)
                            .ConfigureAwait(true);
                        return;
                    }
                }

                if (!connectedOrPayload || !_rdp.IsConnectionConfirmed)
                {
                    if (IsUserCancelled)
                    {
                        await FinishCancelledAsync(request).ConfigureAwait(true);
                        return;
                    }
                    SetState(TfsSyncState.TimedOut, request, null);
                    DiagnosticLogger.Warn("TFS_SYNC_TIMEOUT", FormatLog(request, null, "phase=connection"));
                    CloseSyncRdpSafe("connect_timeout");
                    await FailAndShowAsync(request, new TfsSyncResult
                    {
                        FailReason = TfsSyncFailReason.Timeout,
                        Message = "원격 접속 확인에 실패했습니다.\n"
                            + "RDP 로그인/클립보드 공유 후 다시 시도하세요."
                    }).ConfigureAwait(true);
                    return;
                }

                DiagnosticLogger.Info("TFS_RDP_CONNECTED", FormatLog(request, null, null));

                // 원격 rdpclip이 늦게 붙는 경우 대비: 접속 확인 후 SYNC_REQUEST 재기록
                string rewriteText;
                bool rewritten = TfsClipboardAckService.TryWriteSyncRequest(clipboardDto, out rewriteText);
                DiagnosticLogger.Info("TFS_REQUEST_CLIPBOARD_REWRITTEN",
                    "requestId=" + (request.RequestId ?? "-")
                    + " ok=" + rewritten
                    + " length=" + (rewriteText != null ? rewriteText.Length : 0));
                await Task.Delay(1500).ConfigureAwait(true);

                SetState(TfsSyncState.WaitingForPayload, request, null);
                DiagnosticLogger.Info("TFS_WAITING_FOR_PAYLOAD", FormatLog(request, null,
                    "timeoutMs=" + SyncTimeoutMs + " after=connected"));
                await _popup.UpdateProgressAsync("5. TFS Payload 대기 중 (접속 후 최대 60초)").ConfigureAwait(true);

                accepted = await WaitForPayloadAsync(waiter, SyncTimeoutMs).ConfigureAwait(true);

                if (IsUserCancelled)
                {
                    await FinishCancelledAsync(request).ConfigureAwait(true);
                    return;
                }

                if (accepted == null || accepted.Payload == null)
                {
                    SetState(TfsSyncState.TimedOut, request, null);
                    DiagnosticLogger.Warn("TFS_SYNC_TIMEOUT", FormatLog(request, null, "phase=payload"));
                    CloseSyncRdpSafe("timeout");
                    await FailAndShowAsync(request, new TfsSyncResult
                    {
                        FailReason = TfsSyncFailReason.Timeout,
                        Message = "원격 접속 후 60초 안에 TFS Payload를 받지 못했습니다.\n"
                            + "원격 SessionAgent connect가 PendingTfsRecent / SYNC_REQUEST를 처리하는지,\n"
                            + "RDP 클립보드 공유를 확인하세요."
                    }).ConfigureAwait(true);
                    return;
                }

                await ApplyFetchedPayloadAsync(request, accepted.Payload, accepted.RawClipboard, closeRdp: true)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetState(TfsSyncState.Failed, request, accepted != null ? accepted.Payload : null);
                DiagnosticLogger.Error("TFS_SYNC", FormatLog(request, null, "ex=" + ex.Message));
                CloseSyncRdpSafe("exception");
                await FailAndShowAsync(request, new TfsSyncResult
                {
                    FailReason = TfsSyncFailReason.Unknown,
                    Message = ex.Message
                }).ConfigureAwait(true);
            }
            finally
            {
                // 실패/타임아웃/취소 후에도 SYNC_REQUEST가 남으면 다음 일반 접속이
                // 원격에서 임시 TFS 재접속으로 오인됨 → 항상 정리
                TfsClipboardAckService.TryClearSyncRequestIfPresent();

                lock (_sync)
                {
                    _syncInFlight = false;
                    _activeRequest = null;
                    _payloadWaiter = null;
                    _rdpClosedWaiter = null;
                }
                if (_getPendingBadgeRefresh != null)
                    _getPendingBadgeRefresh();
            }
        }

        private async Task ApplyFetchedPayloadAsync(
            TfsSyncRequest request,
            TfsRecentChangesetsPayload payload,
            string raw,
            bool closeRdp)
        {
            if (IsUserCancelled)
            {
                await FinishCancelledAsync(request).ConfigureAwait(true);
                return;
            }

            SetState(TfsSyncState.Saving, request, payload);
            await _popup.UpdateProgressAsync("체크인 내역 저장 중").ConfigureAwait(true);

            int before = _tfsVm != null ? _tfsVm.Candidates.Count : 0;
            string ingestRaw = !string.IsNullOrWhiteSpace(raw) ? raw : BuildRawFromPayload(payload);

            ISet<int> importedIds = null;
            try
            {
                if (_getImportedChangesetIds != null)
                    importedIds = _getImportedChangesetIds();
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_SYNC", "Imported changeset lookup failed: " + ex.Message);
            }

            var ingest = TfsPayloadIngestService.Ingest(
                ingestRaw,
                _tfsVm,
                request,
                writeAck: false,
                excludeImportedChangesetIds: importedIds);

            if (ingest == null || !ingest.Applied)
            {
                if (IsUserCancelled)
                {
                    await FinishCancelledAsync(request).ConfigureAwait(true);
                    return;
                }
                SetState(TfsSyncState.Failed, request, payload);
                if (closeRdp)
                    CloseSyncRdpSafe("save_failed");
                await FailAndShowAsync(request, new TfsSyncResult
                {
                    FailReason = TfsSyncFailReason.SaveFailed,
                    Message = ingest != null && !string.IsNullOrEmpty(ingest.ErrorMessage)
                        ? ingest.ErrorMessage
                        : "TFS Payload 저장에 실패했습니다."
                }).ConfigureAwait(true);
                return;
            }

            DiagnosticLogger.Info("TFS_PAYLOAD_SAVED", FormatLog(request, payload,
                TfsPayloadIngestService.FormatContext(ingest)));
            DiagnosticLogger.Info("TFS_UI_UPDATED", FormatLog(request, payload,
                "finalCandidateCount=" + ingest.FinalCandidateCount + " before=" + before));

            string deliveryId = payload.DeliveryId;
            lock (_sync)
                _lastDeliveryId = deliveryId;

            bool ackOk = false;
            if (!string.IsNullOrWhiteSpace(deliveryId))
                ackOk = TfsClipboardAckService.TryWriteAck(deliveryId);

            ingest.AckWritten = ackOk;
            SetState(TfsSyncState.AckWritten, request, payload);
            DiagnosticLogger.Info("TFS_ACK_WRITTEN", FormatLog(request, payload, "ok=" + ackOk));

            // 기대 순서: mstsc 종료 → 진행 모달 닫기 → "가져왔습니다" → (버튼) 불러오기
            if (closeRdp)
            {
                await _popup.UpdateProgressAsync("원격 창 종료 중").ConfigureAwait(true);
                await Task.Delay(AckSettleMs).ConfigureAwait(true);

                SetState(TfsSyncState.ClosingRdp, request, payload);
                DiagnosticLogger.Info("TFS_TEMP_RDP_CLOSE_STARTED", FormatLog(request, payload, null));

                var closedWaiter = new TaskCompletionSource<bool>();
                lock (_sync)
                    _rdpClosedWaiter = closedWaiter;

                CloseSyncRdpSafe("after_ack");
                await Task.WhenAny(closedWaiter.Task, Task.Delay(3000)).ConfigureAwait(true);
            }

            await _popup.CloseProgressAsync().ConfigureAwait(true);

            if (IsUserCancelled)
            {
                SetState(TfsSyncState.Completed, request, payload);
                TfsSyncPromptGate.MarkHandled(request.RemoteIp, request.SessionStartedAt, "sync_saved_then_cancel");
                TfsPendingSessionStore.Remove(request.RemoteIp);
                MarkGalleryAvailableLocal(request.RemoteIp);
                DiagnosticLogger.Info("TFS_SYNC", "Cancelled after save — skip success popup");
                return;
            }

            SetState(TfsSyncState.Completed, request, payload);
            DiagnosticLogger.Info("TFS_SYNC_COMPLETED", FormatLog(request, payload,
                TfsPayloadIngestService.FormatContext(ingest)));

            TfsSyncPromptGate.MarkHandled(request.RemoteIp, request.SessionStartedAt, "sync_completed");
            TfsPendingSessionStore.Remove(request.RemoteIp);
            MarkGalleryAvailableLocal(request.RemoteIp);
            await ShowSuccessAsync(ingest, before).ConfigureAwait(true);
        }

        private async Task FinishCancelledAsync(TfsSyncRequest request)
        {
            SetState(TfsSyncState.Failed, request, null);
            CloseSyncRdpSafe("user_cancel");
            await _popup.CloseProgressAsync().ConfigureAwait(true);
            DiagnosticLogger.Info("TFS_SYNC", FormatLog(request, null, "reason=Cancelled"));
            // 취소 시 성공/실패 팝업 모두 띄우지 않음
            await Task.CompletedTask.ConfigureAwait(true);
        }

        private async Task ShowSuccessAsync(TfsPayloadIngestResult ingest, int before)
        {
            int finalCount = ingest != null ? ingest.FinalCandidateCount : (_tfsVm != null ? _tfsVm.Candidates.Count : 0);
            int newUnimported = ingest != null ? ingest.NewUnimportedCount : finalCount;
            bool authorEmpty = ingest != null
                && !ingest.AuthorFilterSkipped
                && ingest.ReturnedItemCount > 0
                && ingest.AuthorMatchedCount == 0;
            bool recentFallback = ingest != null && ingest.IsRecentFallback;

            string title;
            string message;
            PopupIconKind icon;
            string primaryLabel = "업무기록에서 선택";
            bool offerSelect = true;

            if (authorEmpty)
            {
                title = "TFS 내역 확인";
                message = "현재 연결된 TFS 사용자가 작성한 체크인이 없습니다.";
                icon = PopupIconKind.Info;
                offerSelect = false;
            }
            else if (recentFallback && newUnimported > 0)
            {
                title = "오늘 체크인 확인";
                message = "접속 시작~종료 구간에 체크인이 없어,"
                    + Environment.NewLine
                    + "오늘 동일 계정 체크인 중 업무기록에 없는 "
                    + newUnimported + "건을 찾았습니다."
                    + Environment.NewLine
                    + "업무기록에 가져오시겠습니까?";
                icon = PopupIconKind.Info;
                primaryLabel = "가져오기";
            }
            else if (recentFallback)
            {
                title = "TFS 내역 확인";
                message = ingest != null && !string.IsNullOrEmpty(ingest.StatusMessage)
                    ? ingest.StatusMessage
                    : "접속 구간에 체크인이 없고, 오늘 미등록 체크인도 없습니다.";
                icon = PopupIconKind.Info;
                offerSelect = false;
            }
            else if (finalCount > 0 || (ingest != null && ingest.AuthorMatchedCount > 0))
            {
                title = "TFS 내역을 불러왔습니다";
                message = "체크인 후보 " + finalCount + "건을 확인했습니다."
                    + (before > 0 ? " (기존 포함)" : "");
                icon = PopupIconKind.Success;
            }
            else
            {
                title = "TFS 내역 확인";
                message = ingest != null && !string.IsNullOrEmpty(ingest.StatusMessage)
                    ? ingest.StatusMessage
                    : "수신된 체크인이 없습니다.";
                icon = PopupIconKind.Info;
                offerSelect = false;
            }

            PopupButtonDefinition[] buttons = offerSelect
                ? new[]
                {
                    new PopupButtonDefinition(primaryLabel, PopupResultType.Primary, isDefault: true),
                    new PopupButtonDefinition("닫기", PopupResultType.Secondary, isCancel: true)
                }
                : new[]
                {
                    new PopupButtonDefinition("닫기", PopupResultType.Primary, isDefault: true, isCancel: true)
                };

            var resultPopup = new PopupRequest
            {
                Kind = PopupKind.Result,
                Icon = icon,
                Title = title,
                Message = message,
                Buttons = buttons
            };

            var go = await _popup.ShowConfirmAsync(resultPopup).ConfigureAwait(true);
            if (offerSelect && go != null && go.IsPrimary && _selectTfsTab != null)
                _selectTfsTab("WorkLog");
        }

        private async Task<bool> WaitForConnectionOrPayloadAsync(
            TaskCompletionSource<AcceptedPayload> waiter,
            int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (waiter != null && waiter.Task.IsCompleted)
                    return true;
                if (_rdp != null && _rdp.IsConnectionConfirmed)
                    return true;
                if (_rdp == null || !_rdp.IsTracking)
                    return false;
                await Task.Delay(200).ConfigureAwait(true);
            }

            return (waiter != null && waiter.Task.IsCompleted)
                || (_rdp != null && _rdp.IsConnectionConfirmed);
        }

        private async Task<AcceptedPayload> WaitForPayloadAsync(
            TaskCompletionSource<AcceptedPayload> waiter,
            int timeoutMs)
        {
            if (waiter == null)
                return null;
            if (waiter.Task.IsCompleted)
                return await waiter.Task.ConfigureAwait(true);

            var timeout = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(waiter.Task, timeout).ConfigureAwait(true);
            if (completed == waiter.Task)
                return await waiter.Task.ConfigureAwait(true);
            return null;
        }

        private void CloseSyncRdpSafe(string reason)
        {
            try
            {
                if (_rdp != null
                    && _rdp.IsTracking
                    && _rdp.LaunchPurpose == RdpLaunchPurpose.TfsSyncReconnect)
                {
                    _rdp.ForceEnd(reason ?? "tfs_sync_close");
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_TEMP_RDP_CLOSE_STARTED", "ForceEnd failed: " + ex.Message);
            }
        }

        private TfsRecentChangesetsPayload TryReadClipboardTfsPayload(TfsSyncRequest request, out string raw)
        {
            raw = null;
            try
            {
                var app = Application.Current;
                if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
                {
                    string captured = null;
                    app.Dispatcher.Invoke(new Action(() =>
                    {
                        if (Clipboard.ContainsText())
                            captured = Clipboard.GetText();
                    }));
                    raw = captured;
                }
                else
                {
                    if (Clipboard.ContainsText())
                        raw = Clipboard.GetText();
                }

                if (string.IsNullOrWhiteSpace(raw)
                    || !raw.TrimStart().StartsWith(TfsClipboardPayloadService.ClipboardPrefix, StringComparison.Ordinal))
                    return null;

                var parse = new TfsClipboardPayloadService().TryHandleClipboardText(raw);
                if (parse.Result != TfsClipboardPayloadService.HandleResult.Success || parse.Payload == null)
                    return null;

                if (!parse.Payload.Success)
                    return null;
                if (!string.Equals(parse.Payload.Type, TfsClipboardPayloadService.ExpectedType, StringComparison.OrdinalIgnoreCase))
                    return null;

                string pc = parse.Payload.ResolveComputerName();
                if (request != null
                    && !string.IsNullOrWhiteSpace(request.RemoteComputerName)
                    && !string.IsNullOrWhiteSpace(pc)
                    && !string.Equals(pc, request.RemoteComputerName, StringComparison.OrdinalIgnoreCase))
                    return null;

                return parse.Payload;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_SYNC", "Clipboard peek failed: " + ex.Message);
                return null;
            }
        }

        private async Task FailAndShowAsync(TfsSyncRequest request, TfsSyncResult syncResult)
        {
            if (IsUserCancelled)
            {
                await FinishCancelledAsync(request).ConfigureAwait(true);
                return;
            }

            await _popup.CloseProgressAsync().ConfigureAwait(true);
            MarkGalleryAvailableLocal(request != null ? request.RemoteIp : null);

            DiagnosticLogger.Warn("TFS_SYNC", "FailAndShow reason="
                + (syncResult != null ? syncResult.FailReason.ToString() : "?")
                + " " + FormatLog(request, null, "msg=" + (syncResult != null ? syncResult.Message : null)));

            var failPopup = new PopupRequest
            {
                Kind = PopupKind.Result,
                Icon = PopupIconKind.Warning,
                Title = "TFS 내역을 가져오지 못했습니다",
                Message = syncResult.Message ?? syncResult.FailReason.ToString(),
                Buttons = new[]
                {
                    new PopupButtonDefinition("다시 시도", PopupResultType.Primary, isDefault: true),
                    new PopupButtonDefinition("나중에", PopupResultType.Secondary),
                    new PopupButtonDefinition("닫기", PopupResultType.Tertiary, isCancel: true)
                }
            };

            var r = await _popup.ShowConfirmAsync(failPopup).ConfigureAwait(true);
            if (r != null && r.IsPrimary)
            {
                await RunFetchAsync(request).ConfigureAwait(true);
                return;
            }
            if (r != null && r.IsSecondary && request != null)
            {
                TfsPendingSessionStore.Upsert(new TfsPendingSession
                {
                    RemoteIp = request.RemoteIp,
                    RemoteComputerName = request.RemoteComputerName,
                    SessionToken = request.SessionToken,
                    SessionStartedAt = request.SessionStartedAt,
                    SessionEndedAt = request.SessionEndedAt,
                    Status = "LATER"
                });
            }
        }

        private void MarkGalleryAvailableLocal(string remoteIp)
        {
            if (_setGalleryAvailable != null && !string.IsNullOrEmpty(remoteIp))
                _setGalleryAvailable(remoteIp);
        }

        private void SetState(TfsSyncState state, TfsSyncRequest request, TfsRecentChangesetsPayload payload)
        {
            lock (_sync)
                _state = state;
            DiagnosticLogger.Info("TFS_SYNC_STATE", FormatLog(request, payload, "state=" + state));
        }

        private static bool TryValidateIncoming(
            TfsRecentChangesetsPayload payload,
            TfsSyncRequest req,
            out string rejectReason)
        {
            rejectReason = null;
            if (!payload.Success)
            {
                rejectReason = "success=false";
                return false;
            }

            // RC pending 재전송 시 requestId가 비어 있을 수 있음 → PC명만 맞으면 허용
            if (!string.IsNullOrWhiteSpace(payload.RequestId)
                && !string.IsNullOrWhiteSpace(req.RequestId)
                && !string.Equals(req.RequestId.Trim(), payload.RequestId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                rejectReason = "requestId mismatch";
                return false;
            }

            string pc = payload.ResolveComputerName();
            if (!string.IsNullOrWhiteSpace(req.RemoteComputerName)
                && !string.IsNullOrWhiteSpace(pc)
                && !string.Equals(pc, req.RemoteComputerName, StringComparison.OrdinalIgnoreCase))
            {
                rejectReason = "computerName mismatch";
                return false;
            }

            // PENDING_RECONNECT 권장. RC disconnect 직후 DISCONNECT 모드도 허용.
            if (!string.IsNullOrWhiteSpace(payload.DeliveryMode)
                && !string.Equals(payload.DeliveryMode, DeliveryModePendingReconnect, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(payload.DeliveryMode, "DISCONNECT", StringComparison.OrdinalIgnoreCase))
            {
                rejectReason = "deliveryMode unsupported: " + payload.DeliveryMode;
                return false;
            }

            return true;
        }

        private string FormatLog(TfsSyncRequest request, TfsRecentChangesetsPayload payload, string extra)
        {
            int pid;
            TfsSyncState state;
            lock (_sync)
            {
                pid = _mstscPid;
                state = _state;
            }

            string msg = "requestId=" + (request != null && !string.IsNullOrEmpty(request.RequestId)
                    ? request.RequestId
                    : (payload != null ? payload.RequestId : null) ?? "-")
                + " deliveryId=" + (payload != null && !string.IsNullOrEmpty(payload.DeliveryId)
                    ? payload.DeliveryId
                    : (_lastDeliveryId ?? "-"))
                + " mstscPid=" + pid
                + " state=" + state
                + " computerName=" + (payload != null
                    ? (payload.ResolveComputerName() ?? "-")
                    : (request != null ? request.RemoteComputerName : null) ?? "-");

            if (!string.IsNullOrEmpty(extra))
                msg += " " + extra;
            return msg;
        }

        private static string ToUtcRoundTrip(DateTime value)
        {
            DateTimeOffset offset;
            if (value.Kind == DateTimeKind.Utc)
                offset = new DateTimeOffset(value, TimeSpan.Zero);
            else if (value.Kind == DateTimeKind.Local)
                offset = new DateTimeOffset(value);
            else
                offset = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local));

            return offset.ToUniversalTime().ToString("o");
        }

        private static string BuildRawFromPayload(TfsRecentChangesetsPayload payload)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            return TfsClipboardPayloadService.ClipboardPrefix + json;
        }

        /// <summary>
        /// AURORA(IP): mstsc /v: / RC(PC명 점유키): 게시 .rdp
        /// </summary>
        private bool TryStartTfsReconnectRdp(TfsSyncRequest request, out string message, out string launchMode)
        {
            message = null;
            launchMode = "none";
            if (request == null)
            {
                message = "요청이 없습니다.";
                return false;
            }

            string shareKey = (request.RemoteIp ?? string.Empty).Trim();
            string pcName = (request.RemoteComputerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(pcName))
                pcName = shareKey;

            if (!LooksLikeIpv4(shareKey))
            {
                var found = PublishedRdpLauncher.TryFindForPc("RC", pcName);
                if (!found.Succeeded && !string.Equals(pcName, shareKey, StringComparison.OrdinalIgnoreCase))
                    found = PublishedRdpLauncher.TryFindForPc("RC", shareKey);

                if (!found.Succeeded)
                {
                    message = found.Message
                        ?? "RC 재접속용 게시 RDP 파일을 찾지 못했습니다.";
                    return false;
                }

                launchMode = "published_rdp";
                return _rdp.TryStartPublishedRdp(
                    found.Path,
                    shareKey,
                    pcName,
                    RdpLaunchPurpose.TfsSyncReconnect,
                    out message);
            }

            launchMode = "mstsc_/v";
            return _rdp.TryStart(
                shareKey,
                pcName,
                RdpLaunchPurpose.TfsSyncReconnect,
                startMinimized: false,
                out message);
        }

        private static bool LooksLikeIpv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            for (int i = 0; i < 4; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0 || n > 255)
                    return false;
            }
            return true;
        }

        private static string BuildDetail(TfsSyncRequest request)
        {
            return "원격 PC: " + (request.RemoteComputerName ?? "-")
                + "\nIP: " + (request.RemoteIp ?? "-")
                + "\n시작: " + request.SessionStartedAt.ToString("yyyy-MM-dd HH:mm:ss")
                + "\n종료: " + request.SessionEndedAt.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
