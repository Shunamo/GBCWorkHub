using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.DevSession;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Models;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// Remote session lifecycle orchestration (RDP/CMC/RC, occupancy, poll, stale recovery).
    /// Does not own ViewModel; updates UI via <see cref="RemoteSessionUi"/>.
    /// </summary>
    public sealed class RemoteSessionController : IDisposable
    {
        private const string TrackedComputerName = "KEB-7VY98V3";

        private readonly RdpStatusBiz _rdpStatus = new RdpStatusBiz();
        private readonly RemotePcBiz _remotePcBiz = new RemotePcBiz();
        private readonly RemotePcShareBiz _share = new RemotePcShareBiz();
        private readonly RdpSessionTrackingService _tracker = new RdpSessionTrackingService();
        private readonly CmcPmpSessionMonitor _cmcMonitor = new CmcPmpSessionMonitor();
        private readonly DevSessionFileBiz _devSessionFiles = new DevSessionFileBiz();
        private readonly Dictionary<string, LastProcessedRdpState> _lastByComputer =
            new Dictionary<string, LastProcessedRdpState>(StringComparer.OrdinalIgnoreCase);

        private RemoteSessionUi _ui;
        private bool _disposed;
        private bool _eventsWired;

        private int _cmcReserveInFlight;
        private int _cmcSessionLostInFlight;
        private int _activeSessionGeneration;
        /// <summary>PMP 브라우저를 연 시점의 토큰. 원격 SessionAgent가 WorkHub 접속으로 인식하게 클립보드에 미리 올린다.</summary>
        private string _pendingCmcSessionToken;
        private const int CmcSessionTokenHoldMs = 120000;
        private string _activeSessionToken;
        private string _activeComputerName;
        private string _activeIpAddress;
        private DispatcherTimer _pollTimer;
        private bool _pollInFlight;
        private DateTime _userSessionStartedAt;
        private int _userSessionReleaseGate;

        public bool IsTracking { get { return _tracker != null && _tracker.IsTracking; } }
        public string ActiveIpAddress { get { return _activeIpAddress; } }
        public string ActiveComputerName { get { return _activeComputerName; } }
        public string ActiveSessionToken { get { return _activeSessionToken; } }
        public bool IsShareConfigured { get { return _share.IsConfigured; } }
        public string ShareLastError { get { return _share.LastConnectionError; } }

        /// <summary>Shell TfsSyncCoordinator wiring only.</summary>
        public RdpSessionTrackingService TrackerForShell { get { return _tracker; } }

        public void AttachUi(RemoteSessionUi ui)
        {
            if (ui == null) throw new ArgumentNullException("ui");
            _ui = ui;
            if (_eventsWired) return;
            _tracker.RdpStarted += OnRdpStarted;
            _tracker.RdpConnectionConfirmed += OnRdpConnectionConfirmed;
            _tracker.RdpEnded += OnRdpEnded;
            _tracker.RdpTrackingError += OnRdpTrackingError;
            _cmcMonitor.SessionAppeared += OnCmcSessionAppeared;
            _cmcMonitor.SessionMissing += OnCmcSessionMissing;
            _cmcMonitor.SessionLost += OnCmcSessionLost;
            _cmcMonitor.MismatchedSessionDetected += OnCmcMismatchedSession;
            _eventsWired = true;
        }

        public async Task InitializeCentralShareAsync()
        {
            if (!_share.IsConfigured)
            {
                _ui.IsCentralDbConnected = false;
                _ui.IsCentralShareEnabled = false;
                _ui.CentralDbStatus = "DB 미설정 — App.config GbcWorkHubDb (Golden Oracle) 입력 필요";
                DiagnosticLogger.Error("DB_CONNECTION_FAILED", "connection string 미설정 또는 placeholder");
                StartPollTimer();
                return;
            }

            bool ok = false;
            try
            {
                ok = await _share.TestConnectionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ok = false;
                DiagnosticLogger.Error("DB_CONNECTION_FAILED", ex.GetType().Name + ": " + (ex.Message ?? ""));
            }

            _ui.IsCentralDbConnected = ok;
            _ui.IsCentralShareEnabled = ok;
            _ui.CentralDbStatus = ok
                ? "DB 연결됨 — XSUP.MSDWHTKD 공유 활성"
                : "DB 연결 실패 — (" + (_share.LastConnectionError ?? "오류") + ")";

            StartPollTimer();
            if (ok && _ui.IsGalleryVisible)
                await PollSharedStatusAsync(forceUi: true).ConfigureAwait(true);

            if (ok)
            {
                await TryRecoverStaleSessionsAsync().ConfigureAwait(true);
                await TryAdoptLiveCmcSessionsAsync().ConfigureAwait(true);
            }
        }

        /// <summary>
        /// 접속: DB 선점 성공 시에만 mstsc / 게시 .rdp 실행
        /// </summary>
        public Task<string> StartRemoteSessionAsync(RemotePcDto pc)
        {
            return StartRemoteSessionAsync(pc, null);
        }

        /// <param name="publishedRdpPath">있으면 게시 .rdp 실행+추적, 없으면 mstsc /v:IP</param>
        public async Task<string> StartRemoteSessionAsync(RemotePcDto pc, string publishedRdpPath)
        {
            if (pc == null)
                return "선택된 PC가 없습니다.";

            // 점유 키: IP 우선, 없으면 PC명(RC 등)
            string shareKey = !string.IsNullOrWhiteSpace(pc.IpAddress)
                ? pc.IpAddress.Trim()
                : (pc.PcName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(shareKey))
                return "PC IP/PC명이 등록되어 있지 않습니다.";

            if (!_share.IsConfigured || (!_ui.IsCentralShareEnabled))
                return "DB 연결 실패 — 원격 접속을 시작할 수 없습니다.\nApp.config의 GbcWorkHubDb(Golden Oracle)를 설정하세요.";

            string computerName = string.IsNullOrWhiteSpace(pc.PcName) ? TrackedComputerName : pc.PcName.Trim();
            string remoteIp = shareKey;
            string sessionToken = RemotePcShareBiz.NewSessionToken();
            string userAccount = RemotePcShareBiz.LocalUserAccount;
            string clientPc = RemotePcShareBiz.LocalClientPc;
            string accessIp = RemotePcShareBiz.LocalAccessIp;
            bool usePublishedRdp = !string.IsNullOrWhiteSpace(publishedRdpPath);

            bool reserved;
            try
            {
                reserved = await _share.TryReserveAsync(remoteIp, sessionToken, computerName).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("RESERVE_REJECTED", ex.Message);
                return "DB 선점 중 오류: " + ex.Message
                    + (usePublishedRdp
                        ? "\n\nRC는 MSDWHTKD에 REMOTE_ACCS_IP_ADDR='" + remoteIp + "' 행이 필요합니다."
                        : string.Empty);
            }

            if (!reserved)
            {
                RemotePcStatus current = null;
                try
                {
                    current = await _share.GetByRemoteIpAsync(remoteIp).ConfigureAwait(true);
                }
                catch
                {
                    // ignore
                }

                if (current != null)
                {
                    _ui.ApplySharedStatusToUi(current, !_tracker.IsTracking, _tracker.IsTracking);
                    _ui.UpdateGalleryFromStatus(current);
                }

                // 행 없음(RC 미등록) vs 실제 점유 구분
                if (current == null)
                {
                    return "DB에 이 PC 행이 없습니다.\n"
                        + "XSUP.MSDWHTKD 에 REMOTE_ACCS_IP_ADDR='" + remoteIp + "' 를 추가하세요.\n"
                        + "(ACCS_STS_CD=AVAILABLE)";
                }

                string who = (current.AccessUserId ?? "?") + " / " + (current.AccessPcName ?? "?");
                string localUser = RemotePcShareBiz.LocalUserAccount;
                if (!string.IsNullOrWhiteSpace(current.AccessUserId)
                    && string.Equals(current.AccessUserId.Trim(), localUser, StringComparison.OrdinalIgnoreCase)
                    && !_tracker.IsTracking)
                {
                    return "이전에 종료되지 않은 내 점유가 DB에 남아 있습니다.\n"
                        + "(" + who + ")\n"
                        + "앱을 다시 시작하거나 상태 복구 후 접속하세요.";
                }

                return "현재 다른 사용자가 이 원격 PC를 사용 중입니다.\n(" + who + ")";
            }

            _activeSessionToken = sessionToken;
            _activeComputerName = computerName;
            _activeIpAddress = remoteIp;
            _userSessionStartedAt = DateTime.Now;
            Interlocked.Exchange(ref _userSessionReleaseGate, 0);
            _ui.SetSessionTokenDisplay(sessionToken);
            _ui.SetLocalAccessIp(accessIp ?? "-");

            _ui.UpdateGalleryLocalInUse(remoteIp, userAccount, clientPc, sessionToken);

            // 이전 TFS 가져오기 SYNC_REQUEST가 남아 있으면 원격이 일반 접속을
            // 임시 재접속으로 오인함 → 일반 UserSession 시작 전 제거
            TfsClipboardAckService.TryClearSyncRequestIfPresent();

            // Dev-session two-point tracking: announce SESSION_TOKEN to the remote SessionAgent
            // before mstsc launches, so its connect-time invocation can capture a baseline
            // snapshot keyed by this token. Separate message from SYNC_REQUEST above — does not
            // affect the TFS-sync-reconnect trigger.
            TfsClipboardAckService.TryAnnounceSessionToken(sessionToken, null, 1500, computerName);

            _ui.RunOnUi(() =>
            {
                string before = "?";
                _ui.SetRemoteComputerName(computerName);
                _ui.SetWorkHubUserAccount(userAccount);
                _ui.SetWorkHubClientPc(clientPc);
                _ui.SetCurrentStatus("사용 중");
                _ui.SetStatusSource("LOCAL_RESERVE_IN_USE");
                _ui.SetLastReceiveMessage("DB 선점 완료 — 사용 중");
                _ui.SetConnectionEndedAt("-");
                _ui.SetConnectionRequestedAt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _ui.SetConnectionConfirmedAt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _ui.SetRdpSessionConfirmed(true);
                DiagnosticLogger.Info("ViewModel", "Status " + before + " -> 사용 중 (Reserve) remoteIp=" + remoteIp + " token=" + sessionToken);
            });

            string message;
            bool ok = usePublishedRdp
                ? _tracker.TryStartPublishedRdp(publishedRdpPath.Trim(), remoteIp, computerName, out message)
                : _tracker.TryStart(remoteIp, computerName, out message);
            if (!ok)
            {
                await _share.ReleaseAsync(remoteIp, sessionToken, "MSTSC_START_FAILED").ConfigureAwait(true);
                DiagnosticLogger.Error("MSTSC_START_FAILED", "RemoteIp=" + remoteIp + " SessionToken=" + sessionToken + " " + (message ?? ""));
                _activeSessionToken = null;
                _ui.UpdateGalleryLocalAvailable(remoteIp);
                _ui.RunOnUi(() =>
                {
                    _ui.SetCurrentStatus("사용 가능");
                    _ui.SetStatusSource("MSTSC_START_FAILED");
                    _ui.SetLastReceiveMessage(message ?? "mstsc 실행 실패 — DB 선점 해제");
                });
                return message ?? "mstsc 실행 실패";
            }

            _activeSessionGeneration = _tracker.SessionGeneration;
            DiagnosticLogger.Info("MSTSC_STARTED", "RemoteIp=" + remoteIp + " SessionToken=" + sessionToken
                + " Gen=" + _activeSessionGeneration
                + (usePublishedRdp ? " Mode=PublishedRdp" : " Mode=MstscIp"));

            _ui.RunOnUi(new Action(PushTrackingUiFields));
            return null;
        }

        private void OnRdpStarted(int generation)
        {
            _ui.RunOnUi(() =>
            {
                if (generation != _activeSessionGeneration && generation != _tracker.SessionGeneration)
                {
                    DiagnosticLogger.Warn("ViewModel", "Ignore stale RdpStarted gen=" + generation + " active=" + _activeSessionGeneration);
                    return;
                }
                _activeSessionGeneration = generation;
                PushTrackingUiFields();
            });
        }

        private async void OnRdpConnectionConfirmed(RdpStatusPayload payload, int generation)
        {
            if (generation != _activeSessionGeneration)
            {
                DiagnosticLogger.Warn("ViewModel", "Ignore stale RdpConnectionConfirmed gen=" + generation + " active=" + _activeSessionGeneration);
                return;
            }

            if (_tracker.LaunchPurpose == RdpLaunchPurpose.TfsSyncReconnect
                || (_ui.TfsSync != null && _ui.TfsSync.IsSyncInFlight))
            {
                DiagnosticLogger.Info("ViewModel", "Skip DB IN_USE confirm during TfsSyncReconnect");
                return;
            }

            string token = _activeSessionToken;
            string remoteIp = _activeIpAddress;

            // UI는 Reserve 시점에 이미 사용 중 — 원격 확인은 메타/REMOTE_ACCS_DTM 갱신
            _ui.RunOnUi(() =>
            {
                _ui.SetStatusSource("REMOTE_CLIPBOARD_EVENT");
                _ui.SetLastReceiveMessage("원격 연결 성공 확인");
                _ui.SetRdpSessionConfirmed(true);
                _ui.SetConnectionConfirmedAt( _tracker.ConnectionConfirmedAt.HasValue
                    ? _tracker.ConnectionConfirmedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                if (payload != null)
                    _ui.MergeEvents(payload);

                _ui.RefreshEventListBinding();
                PushTrackingUiFields();
                _ui.UpdateGalleryStatusCode(remoteIp, RemotePcDbStatuses.InUse);
                DiagnosticLogger.Info("ViewModel", "원격 연결 확인 Gen=" + generation);
            });

            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(remoteIp) && _ui.IsCentralShareEnabled)
            {
                try
                {
                    bool dbOk = await _share.ConfirmConnectionAsync(remoteIp, token).ConfigureAwait(true);
                    DiagnosticLogger.Info("REMOTE_CONNECTION_CONFIRMED", "RemoteIp=" + remoteIp + " SessionToken=" + token + " ok=" + dbOk);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("REMOTE_CONNECTION_CONFIRMED", ex.Message);
                }
            }
        }

        private void OnRdpEnded(RdpSessionTrackingService.RdpSessionEndInfo info)
        {
            if (info == null)
                return;

            // Process.Exited는 백그라운드 스레드일 수 있음 → UI Dispatcher로 마샬
            var app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => OnRdpEnded(info)));
                return;
            }

#pragma warning disable CS4014
            HandleRdpEndedAsync(info);
#pragma warning restore CS4014
        }

        private async Task HandleRdpEndedAsync(RdpSessionTrackingService.RdpSessionEndInfo info)
        {
            if (info.SessionGeneration != _activeSessionGeneration)
            {
                DiagnosticLogger.Warn("ViewModel", "Ignore stale RdpEnded gen=" + info.SessionGeneration
                    + " active=" + _activeSessionGeneration
                    + " reason=" + (info.Reason ?? ""));
                return;
            }

            string token = _activeSessionToken;
            string remoteIp = _activeIpAddress;
            string computerName = _activeComputerName;
            int processId = info.PrimaryProcessId;

            DiagnosticLogger.Info("LOCAL_MSTSC_EXIT_DETECTED",
                "RemoteIp=" + (remoteIp ?? "-")
                + " ProcessId=" + processId
                + " Purpose=" + info.LaunchPurpose
                + " SessionToken=" + TokenPrefix(token)
                + " reason=" + (info.Reason ?? "")
                + " confirmed=" + info.WasConnectionConfirmed);

            // TFS 재접속: Oracle/TFS 재팝업 금지. Process.Exited에서 클립보드 파싱 안 함.
            if (info.LaunchPurpose == RdpLaunchPurpose.TfsSyncReconnect)
            {
                if (_ui.TfsSync != null)
                    _ui.TfsSync.NotifyReconnectEnded(info.Reason);
                _ui.RunOnUi(() =>
                {
                    _ui.SetLastReceiveMessage("TFS 동기화 재접속 종료");
                    PushTrackingUiFields();
                });
                return;
            }

            if (_ui.TfsSync != null && _ui.TfsSync.IsSyncInFlight)
            {
                _ui.RunOnUi(() =>
                {
                    _ui.SetLastReceiveMessage("TFS 동기화 진행 중 — 종료 이벤트 무시");
                    PushTrackingUiFields();
                });
                return;
            }

            double sessionSeconds = 0;
            if (_userSessionStartedAt != default(DateTime))
                sessionSeconds = (info.EndedAt - _userSessionStartedAt).TotalSeconds;
            bool hadReservedSession = !string.IsNullOrEmpty(token);

            // Dev-session file classification is not collected here: the remote SessionAgent
            // computes it (two-point snapshot diff + TFVC) at its own disconnect trigger and
            // delivers it asynchronously via GBCWORKHUB_SESSION_RESULT:: — handled by
            // HandleSessionChangeResultReceived below, independently of this mstsc-exit path.

            // 1) Oracle 점유 해제 (Popup보다 먼저, 중복 안전)
            await ReleaseUserSessionOccupancyOnceAsync(remoteIp, token, processId, info.LaunchPurpose)
                .ConfigureAwait(true);

            // 2) UI: DB 재조회로 카드 상태 반영 (거짓 IN_USE 금지)
            await RefreshRemotePcAfterReleaseAsync(remoteIp).ConfigureAwait(true);
            DiagnosticLogger.Info("REMOTE_PC_REFRESHED",
                "RemoteIp=" + (remoteIp ?? "-")
                + " ProcessId=" + processId
                + " Purpose=" + info.LaunchPurpose
                + " SessionToken=" + TokenPrefix(token));

            _ui.RunOnUi(() =>
            {
                _ui.ClearSessionRaw();
                _ui.SetRdpSessionConfirmed(false);
                _ui.SetConnectionEndedAt(info.EndedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                // RC 등은 원격 클립보드 confirm 없이도 정상 종료일 수 있음
                string endMsg;
                if (info.WasConnectionConfirmed || sessionSeconds >= 10.0 || hadReservedSession)
                    endMsg = "원격 연결 종료 — 점유 해제됨";
                else
                    endMsg = "원격 접속 취소 또는 실패";
                _ui.SetLastReceiveMessage(endMsg);
                _ui.SetStatusSource("LOCAL_MSTSC_EXIT");
                _ui.SetCurrentStatus("사용 가능");
                _ui.AddLocalEndEvent(info);
                _ui.RefreshEventListBinding();
                PushTrackingUiFields();
                _ui.UpdateGalleryLocalAvailable(remoteIp);
                _ui.RecalculateStatusCounts();
                _ui.RefreshGalleryFilter();
            });

            // Dev-session file change report is logged from HandleSessionChangeResultReceived,
            // whenever the remote SessionAgent's payload actually arrives (independent timing
            // from this method — the remote disconnect trigger and its TFVC queries take longer
            // than the local mstsc-exit handling above).

            // 3) TFS 확인 Popup (AURORA / RC 동일 — SessionAgent가 GBCWORKHUB_TFS:: 전달)
            bool shouldPromptTfs = info.LaunchPurpose == RdpLaunchPurpose.UserSession
                && !string.IsNullOrEmpty(remoteIp)
                && (info.WasConnectionConfirmed || hadReservedSession || sessionSeconds >= 5.0);

            if (shouldPromptTfs && _ui.TfsSync != null)
            {
                DiagnosticLogger.Info("TFS_PROMPT_OPENED",
                    "RemoteIp=" + remoteIp
                    + " ProcessId=" + processId
                    + " Purpose=" + info.LaunchPurpose
                    + " SessionToken=" + TokenPrefix(token)
                    + " confirmed=" + info.WasConnectionConfirmed
                    + " reserved=" + hadReservedSession
                    + " sessionSec=" + sessionSeconds.ToString("0.0"));

                var request = new TfsSyncRequest
                {
                    RemoteIp = remoteIp,
                    RemoteComputerName = computerName,
                    SessionToken = null,
                    SessionStartedAt = _userSessionStartedAt == default(DateTime) ? info.EndedAt.AddHours(-1) : _userSessionStartedAt,
                    SessionEndedAt = info.EndedAt
                };

                if (_ui.TfsWorkLog != null)
                {
                    _ui.TfsWorkLog.UpdateSessionContext(new GBCWorkHub.DTO.WorkLog.WorkSessionContext
                    {
                        RemoteIp = remoteIp,
                        RemoteComputerName = computerName,
                        ClientLocalIp = RemotePcShareBiz.LocalAccessIp,
                        CurrentUserId = Environment.UserDomainName + "\\" + Environment.UserName,
                        CurrentUserName = Environment.UserName,
                        SessionStartedAt = request.SessionStartedAt,
                        SessionEndedAt = request.SessionEndedAt
                    });
                }

                await _ui.TfsSync.HandleUserSessionEndedAsync(request).ConfigureAwait(true);
                _ui.RefreshPendingTfsBadge();
            }
            else
            {
                DiagnosticLogger.Info("TFS_PROMPT_SKIPPED",
                    "RemoteIp=" + (remoteIp ?? "-")
                    + " confirmed=" + info.WasConnectionConfirmed
                    + " reserved=" + hadReservedSession
                    + " sessionSec=" + sessionSeconds.ToString("0.0")
                    + " purpose=" + info.LaunchPurpose);
            }
        }

        /// <summary>
        /// 일반 사용자 세션 mstsc 종료 시 Oracle 점유 해제 (동일 종료에 대해 1회만).
        /// </summary>
        private async Task ReleaseUserSessionOccupancyOnceAsync(
            string remoteIp,
            string sessionToken,
            int processId,
            RdpLaunchPurpose purpose)
        {
            if (purpose != RdpLaunchPurpose.UserSession)
                return;

            if (Interlocked.CompareExchange(ref _userSessionReleaseGate, 1, 0) != 0)
            {
                DiagnosticLogger.Info("RELEASE_STARTED",
                    "skipped_duplicate RemoteIp=" + (remoteIp ?? "-")
                    + " ProcessId=" + processId
                    + " Purpose=" + purpose
                    + " SessionToken=" + TokenPrefix(sessionToken));
                return;
            }

            DiagnosticLogger.Info("RELEASE_STARTED",
                "RemoteIp=" + (remoteIp ?? "-")
                + " ProcessId=" + processId
                + " Purpose=" + purpose
                + " SessionToken=" + TokenPrefix(sessionToken)
                + " LocalUser=" + RemotePcShareBiz.LocalUserAccount
                + " LocalPc=" + RemotePcShareBiz.LocalClientPc);

            bool released = false;
            if (!string.IsNullOrEmpty(sessionToken) && !string.IsNullOrEmpty(remoteIp) && _ui.IsCentralShareEnabled)
            {
                try
                {
                    released = await _share.ReleaseAsync(remoteIp, sessionToken, "MSTSC_EXIT").ConfigureAwait(true);
                    DiagnosticLogger.Info(released ? "RELEASE_SUCCEEDED" : "RELEASE_FAILED",
                        "RemoteIp=" + remoteIp
                        + " ProcessId=" + processId
                        + " Purpose=" + purpose
                        + " SessionToken=" + TokenPrefix(sessionToken)
                        + " ok=" + released);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("RELEASE_FAILED",
                        "RemoteIp=" + remoteIp
                        + " ProcessId=" + processId
                        + " Purpose=" + purpose
                        + " SessionToken=" + TokenPrefix(sessionToken)
                        + " " + ex.Message);
                }
            }
            else
            {
                DiagnosticLogger.Info("RELEASE_SUCCEEDED",
                    "RemoteIp=" + (remoteIp ?? "-")
                    + " ProcessId=" + processId
                    + " Purpose=" + purpose
                    + " SessionToken=" + TokenPrefix(sessionToken)
                    + " mode=local_or_no_token");
            }

            _activeSessionToken = null;
        }

        private async Task RefreshRemotePcAfterReleaseAsync(string remoteIp)
        {
            if (string.IsNullOrEmpty(remoteIp))
                return;

            // DB 왕복 전에 카드/헤더를 즉시 AVAILABLE로 (체감 지연 제거)
            _ui.UpdateGalleryLocalAvailable(remoteIp);
            _ui.RunOnUi(() =>
            {
                _ui.SetCurrentStatus("사용 가능");
                _ui.SetStatusSource("LOCAL_MSTSC_EXIT");
                _ui.SetWorkHubUserAccount(RemotePcShareBiz.LocalUserAccount);
                _ui.SetWorkHubClientPc(RemotePcShareBiz.LocalClientPc);
            });

            if ((!_ui.IsCentralShareEnabled))
                return;

            try
            {
                var current = await _share.GetByRemoteIpAsync(remoteIp).ConfigureAwait(true);
                if (current != null)
                {
                    DiagnosticLogger.Info("REMOTE_PC_REFRESHED",
                        "RemoteIp=" + remoteIp
                        + " DbStatus=" + (current.AccessStatusCode ?? "-")
                        + " User=" + (current.AccessUserId ?? "-")
                        + " Pc=" + (current.AccessPcName ?? "-"));
                    _ui.ApplySharedStatusToUi(current, true, _tracker.IsTracking);
                    _ui.UpdateGalleryFromStatus(current);
                }
                else
                {
                    DiagnosticLogger.Warn("REMOTE_PC_REFRESHED", "RemoteIp=" + remoteIp + " row=null after release");
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("REMOTE_PC_REFRESHED", "requery failed: " + ex.Message);
            }

            if (_ui.SelectedRemoteComputer != null
                && string.Equals(_ui.SelectedRemoteComputer.IpAddress, remoteIp, StringComparison.OrdinalIgnoreCase))
            {
                await _ui.LoadRecentUsageLogsAsync(_ui.SelectedRemoteComputer).ConfigureAwait(true);
            }
        }

        private static string TokenPrefix(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "-";
            return token.Length <= 8 ? token : token.Substring(0, 8);
        }

        /// <summary>
        /// 앱 시작 시: DB상 현재 사용자/PC가 IN_USE|CONNECTING 소유자인데 mstsc가 없으면 복구 안내
        /// </summary>
        public async Task TryRecoverStaleSessionsAsync()
        {
            if ((!_ui.IsCentralShareEnabled) || _ui.Popup == null)
                return;

            IList<RemotePcStatus> all;
            try
            {
                all = await _share.GetAllAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("STALE_SESSION", "GetAll failed: " + ex.Message);
                return;
            }

            if (all == null || all.Count == 0)
                return;

            string user = RemotePcShareBiz.LocalUserAccount;
            string localPc = RemotePcShareBiz.LocalClientPc;
            string trackingIp = _tracker.IsTracking ? _tracker.TargetIp : null;

            var owned = all.Where(s =>
            {
                if (s == null || string.IsNullOrWhiteSpace(s.RemoteAccessIpAddress))
                    return false;
                if (!string.Equals(s.AccessUserId, user, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(s.AccessPcName, localPc, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(s.AccessStatusCode, RemotePcDbStatuses.InUse, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(s.AccessStatusCode, RemotePcDbStatuses.Connecting, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.IsNullOrEmpty(trackingIp)
                    && string.Equals(s.RemoteAccessIpAddress, trackingIp, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            }).ToList();

            foreach (var s in owned)
            {
                // 원격이 아직 살아 있으면 점유 유지 + 로컬 세션 재연결 (해제 묻지 않음)
                if (IsOwnedRemoteStillAlive(s))
                {
                    RehydrateActiveSessionFromDb(s);
                    DiagnosticLogger.Info("STALE_SESSION",
                        "Rehydrated live remote Ip=" + s.RemoteAccessIpAddress
                        + " Site=" + (s.SiteCode ?? "-")
                        + " Token=" + TokenPrefix(s.SessionToken));
                    continue;
                }

                var result = await _ui.Popup.ShowConfirmAsync(new PopupRequest
                {
                    Kind = PopupKind.Confirm,
                    Icon = PopupIconKind.Warning,
                    Title = "원격 접속 상태 복구",
                    Message = "이전에 종료되지 않은 원격 접속 상태가 있습니다",
                    Detail = (s.RemotePcName ?? "-") + " (" + s.RemoteAccessIpAddress + ")\n상태: "
                        + (s.DisplayStatus ?? s.AccessStatusCode)
                        + "\n소유: " + (s.AccessUserId ?? "-") + " / " + (s.AccessPcName ?? "-"),
                    DedupKey = "StaleSession:" + s.RemoteAccessIpAddress,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition("상태 복구", PopupResultType.Primary, isDefault: true),
                        new PopupButtonDefinition("나중에", PopupResultType.Secondary, isCancel: true)
                    }
                }).ConfigureAwait(true);

                if (result == null || !result.IsPrimary)
                    continue;

                if (string.IsNullOrEmpty(s.SessionToken))
                {
                    DiagnosticLogger.Warn("STALE_SESSION", "SessionToken 없음 — RemoteIp=" + s.RemoteAccessIpAddress);
                    continue;
                }

                DiagnosticLogger.Info("RELEASE_STARTED",
                    "RemoteIp=" + s.RemoteAccessIpAddress
                    + " ProcessId=-1"
                    + " Purpose=StaleRecovery"
                    + " SessionToken=" + TokenPrefix(s.SessionToken));

                try
                {
                    bool ok = await _share.ReleaseAsync(s.RemoteAccessIpAddress, s.SessionToken, "STALE_RECOVERY").ConfigureAwait(true);
                    DiagnosticLogger.Info(ok ? "RELEASE_SUCCEEDED" : "RELEASE_FAILED",
                        "RemoteIp=" + s.RemoteAccessIpAddress
                        + " ProcessId=-1"
                        + " Purpose=StaleRecovery"
                        + " SessionToken=" + TokenPrefix(s.SessionToken));
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("RELEASE_FAILED",
                        "RemoteIp=" + s.RemoteAccessIpAddress
                        + " Purpose=StaleRecovery "
                        + ex.Message);
                }

                await RefreshRemotePcAfterReleaseAsync(s.RemoteAccessIpAddress).ConfigureAwait(true);
                DiagnosticLogger.Info("REMOTE_PC_REFRESHED",
                    "RemoteIp=" + s.RemoteAccessIpAddress
                    + " ProcessId=-1"
                    + " Purpose=StaleRecovery"
                    + " SessionToken=" + TokenPrefix(s.SessionToken));
            }
        }

        private bool IsOwnedRemoteStillAlive(RemotePcStatus s)
        {
            if (s == null)
                return false;

            string ip = s.RemoteAccessIpAddress;
            string pc = s.RemotePcName;
            bool isCmc = string.Equals(s.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);
            if (isCmc)
                return IsCmcRemoteStillAliveLocally(ip, pc);

            if (!string.IsNullOrWhiteSpace(ip) && _tracker.IsMstscRunningForIp(ip))
                return true;
            if (!string.IsNullOrWhiteSpace(pc) && _tracker.IsMstscRunningForIp(pc))
                return true;
            return false;
        }

        public bool IsCmcRemoteStillAlive(string shareKey, string pcName)
        {
            return IsCmcRemoteStillAliveLocally(shareKey, pcName);
        }

        private static bool IsCmcRemoteStillAliveLocally(string shareKey, string pcName)
        {
            if (!string.IsNullOrWhiteSpace(pcName) && CmcPmpSessionMonitor.ProbeSessionVisible(pcName))
                return true;
            if (!string.IsNullOrWhiteSpace(shareKey) && CmcPmpSessionMonitor.ProbeSessionVisible(shareKey))
                return true;
            return false;
        }

        /// <summary>
        /// Work Hub 재시작 시: rdp.ma 가 이미 떠 있는데 DB가 AVAILABLE 이면 다시 점유.
        /// (종료 시 점유가 풀렸거나, 창은 살아 있는데 모니터가 끊긴 경우)
        /// </summary>
        public async Task TryAdoptLiveCmcSessionsAsync()
        {
            if ((!_ui.IsCentralShareEnabled) || !_share.IsConfigured)
                return;

            List<RemotePcDto> pcs = null;
            try
            {
                pcs = _remotePcBiz.GetRemotePcListBySite("CMC");
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("CMC_ADOPT", "GetRemotePcListBySite failed: " + ex.Message);
                return;
            }

            if (pcs == null || pcs.Count == 0)
                return;

            string signal = CmcPmpSessionMonitor.TryGetLiveRdpSignalText();
            if (string.IsNullOrWhiteSpace(signal))
            {
                DiagnosticLogger.Info("CMC_ADOPT", "No live rdp.ma / PMP RDP SESSION found");
                return;
            }

            DiagnosticLogger.Info("CMC_ADOPT", "Live signal=" + signal);

            // 신호 텍스트에 PC명이 있으면 그 PC만, 없으면 CMC PC가 1대일 때만 채택
            var targets = new List<RemotePcDto>();
            foreach (var dto in pcs)
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.IpAddress))
                    continue;
                string pcName = string.IsNullOrWhiteSpace(dto.PcName) ? null : dto.PcName.Trim();
                if (!string.IsNullOrWhiteSpace(pcName)
                    && signal.IndexOf(pcName, StringComparison.OrdinalIgnoreCase) >= 0)
                    targets.Add(dto);
            }

            if (targets.Count == 0 && pcs.Count == 1 && pcs[0] != null)
                targets.Add(pcs[0]);

            if (targets.Count == 0)
            {
                DiagnosticLogger.Warn("CMC_ADOPT",
                    "rdp.ma live but could not map to a CMC PC — signal=" + signal);
                return;
            }

            string user = RemotePcShareBiz.LocalUserAccount;
            string localPc = RemotePcShareBiz.LocalClientPc;

            foreach (var dto in targets)
            {
                string shareKey = dto.IpAddress.Trim();
                string pcName = string.IsNullOrWhiteSpace(dto.PcName) ? shareKey : dto.PcName.Trim();

                RemotePcStatus status = null;
                try
                {
                    status = await _share.GetByRemoteIpAsync(shareKey).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Warn("CMC_ADOPT", "GetByRemoteIp failed " + shareKey + ": " + ex.Message);
                    continue;
                }

                bool inUse = status != null
                    && string.Equals(status.AccessStatusCode, RemotePcDbStatuses.InUse, StringComparison.OrdinalIgnoreCase);
                bool owned = status != null
                    && string.Equals(status.AccessUserId, user, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(status.AccessPcName, localPc, StringComparison.OrdinalIgnoreCase);

                if (inUse && owned)
                {
                    RehydrateActiveSessionFromDb(status);
                    DiagnosticLogger.Info("CMC_ADOPT", "Rehydrated owned IN_USE " + shareKey);
                    continue;
                }

                if (inUse && !owned)
                {
                    DiagnosticLogger.Info("CMC_ADOPT",
                        "Live rdp but owned by other — skip " + shareKey
                        + " who=" + (status.AccessUserId ?? "-"));
                    continue;
                }

                _cmcMonitor.StartWatching(shareKey, pcName);
                await TryReserveCmcOccupancyAsync(shareKey, pcName).ConfigureAwait(true);
                DiagnosticLogger.Info("CMC_ADOPT", "Reserved live CMC session " + shareKey + " Pc=" + pcName);
            }
        }

        /// <summary>DB 점유 + 살아 있는 원격에 로컬 세션/모니터를 다시 붙인다.</summary>
        public void RehydrateActiveSessionFromDb(RemotePcStatus s)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.RemoteAccessIpAddress))
                return;
            if (string.IsNullOrWhiteSpace(s.SessionToken))
                return;

            string shareKey = s.RemoteAccessIpAddress.Trim();
            string pcName = string.IsNullOrWhiteSpace(s.RemotePcName) ? shareKey : s.RemotePcName.Trim();
            bool isCmc = string.Equals(s.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);

            _activeSessionToken = s.SessionToken;
            _activeIpAddress = shareKey;
            _activeComputerName = pcName;
            _userSessionStartedAt = s.AccessStartDateTime
                ?? s.RemoteAccessDateTime
                ?? DateTime.Now;
            Interlocked.Exchange(ref _userSessionReleaseGate, 0);
            _ui.SetSessionTokenDisplay(s.SessionToken);

            _ui.UpdateGalleryLocalInUse(
                shareKey,
                RemotePcShareBiz.LocalUserAccount,
                RemotePcShareBiz.LocalClientPc,
                s.SessionToken);

            if (isCmc)
            {
                _cmcMonitor.StartWatching(shareKey, pcName);
            }
            else if (!_tracker.IsTracking)
            {
                string needle = shareKey;
                if (!string.IsNullOrWhiteSpace(pcName) && _tracker.IsMstscRunningForIp(pcName))
                    needle = pcName;
                string adoptMsg;
                if (!_tracker.TryAdoptExisting(shareKey, pcName, needle, out adoptMsg))
                {
                    DiagnosticLogger.Warn("STALE_SESSION",
                        "Adopt mstsc failed Ip=" + shareKey + " msg=" + (adoptMsg ?? "-"));
                }
                else
                {
                    _activeSessionGeneration = _tracker.SessionGeneration;
                    DiagnosticLogger.Info("STALE_SESSION",
                        "Adopted mstsc Ip=" + shareKey
                        + " Gen=" + _activeSessionGeneration);
                }
            }
            else
            {
                _activeSessionGeneration = _tracker.SessionGeneration;
            }

            _ui.RunOnUi(() =>
            {
                _ui.SetRemoteComputerName(pcName);
                _ui.SetCurrentStatus("사용 중");
                _ui.SetStatusSource(isCmc ? "CMC_REHYDRATE" : "RDP_REHYDRATE");
                _ui.SetLastReceiveMessage("원격이 계속 실행 중 — 점유 유지 (" + pcName + ")");
                _ui.SetRdpSessionConfirmed(true);
            });
        }


        private void PushTrackingUiFields()
        {
            if (_ui == null) return;
            var ids = _tracker.TrackedProcessIds;
            _ui.SetTrackedProcessIds(ids != null && ids.Count > 0 ? string.Join(", ", ids) : "-");
            _ui.SetRdpLaunchTime(_tracker.LaunchTime.HasValue
                ? _tracker.LaunchTime.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : "-");
            _ui.SetRdpSessionConfirmed(_tracker.IsConnectionConfirmed);
            if (_tracker.ConnectionConfirmedAt.HasValue)
                _ui.SetConnectionConfirmedAt(_tracker.ConnectionConfirmedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (_tracker.EndedAt.HasValue)
                _ui.SetConnectionEndedAt(_tracker.EndedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!string.IsNullOrEmpty(_tracker.StatusSource))
                _ui.SetStatusSource(_tracker.StatusSource);
        }
        private void OnRdpTrackingError(string message)
        {
            _ui.RunOnUi(() =>
            {
                _ui.SetLastReceiveMessage(message ?? "RDP 추적 오류");
            });
        }

        private void StartPollTimer()
        {
            if (_pollTimer != null)
                return;

            int seconds = ReadIntSetting("RemotePc.DbPollSeconds", 2);
            if (seconds < 1)
                seconds = 1;
            if (seconds > 30)
                seconds = 30;

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };
            _pollTimer.Tick += async (s, e) =>
            {
                await PollSharedStatusAsync(forceUi: false).ConfigureAwait(true);
            };
            _pollTimer.Start();
        }

        public async Task PollSharedStatusAsync(bool forceUi)
        {
            if (_pollInFlight)
                return;
            if (!_share.IsConfigured)
                return;

            _pollInFlight = true;
            try
            {
                IList<RemotePcStatus> list = await _share.GetAllAsync().ConfigureAwait(true);
                bool ok = _share.LastConnectionOk;
                _ui.IsCentralDbConnected = ok;
                _ui.IsCentralShareEnabled = ok;
                if (!ok)
                {
                    _ui.CentralDbStatus = "DB 연결 실패 — 팀 공유 비활성";
                    return;
                }

                _ui.CentralDbStatus = "DB 연결됨 — XSUP.MSDWHTKD 공유 활성";

                if (!_ui.IsGalleryVisible)
                {
                    _ui.SetLastRefreshedAt(DateTime.Now);
                    return;
                }

                // 폴링마다 사이트 시드 재로드하지 않음 — DB 상태만 병합 (UI 지연/깜빡임 완화)
                _ui.MergeRemoteComputersFromDb(list, ip => _tracker.IsTracking && string.Equals(ip, _activeIpAddress, StringComparison.OrdinalIgnoreCase));

                if (string.Equals(_ui.SelectedSiteCode, "RC", StringComparison.OrdinalIgnoreCase))
                    _ui.RefreshRcVpnBadges();

                // 사이트 독립: 현재 활성 세션 / 선택 PC만 헤더 반영. AURORA IP 하드코딩 금지.
                string focusIp = _activeIpAddress;
                if (string.IsNullOrWhiteSpace(focusIp) && _ui.SelectedRemoteComputer != null)
                    focusIp = _ui.SelectedRemoteComputer.IpAddress;

                RemotePcStatus match = null;
                if (!string.IsNullOrWhiteSpace(focusIp) && list != null)
                {
                    foreach (var item in list)
                    {
                        if (item != null && string.Equals(item.RemoteAccessIpAddress, focusIp, StringComparison.OrdinalIgnoreCase))
                            match = item;
                    }
                }

                if (match != null)
                {
                    // 로컬 추적 중이어도 forceUi면 헤더 갱신. 평소에는 추적 중 덮어쓰지 않음.
                    bool overwrite = forceUi || !_tracker.IsTracking;
                    _ui.ApplySharedStatusToUi(match, overwrite, _tracker.IsTracking);
                }

                _ui.SetLastRefreshedAt(DateTime.Now);

                if (_ui.RefreshPcListRequested != null && forceUi)
                    await _ui.RefreshPcListRequested().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _ui.IsCentralDbConnected = false;
                _ui.IsCentralShareEnabled = false;
                _ui.CentralDbStatus = "DB 연결 실패 — 팀 공유 비활성";
                DiagnosticLogger.Error("DB_POLL_FAILED", ex.Message);
            }
            finally
            {
                _pollInFlight = false;
            }
        }

        public async Task ApplySharedStatusToPcListAsync(IList<RemotePcDto> pcs)
        {
            if (pcs == null || pcs.Count == 0 || !_share.IsConfigured)
                return;

            IList<RemotePcStatus> shared;
            try
            {
                shared = await _share.GetAllAsync().ConfigureAwait(true);
            }
            catch
            {
                return;
            }

            if (shared == null)
                return;

            foreach (var pc in pcs)
            {
                if (pc == null || string.IsNullOrWhiteSpace(pc.IpAddress))
                    continue;

                var match = shared.FirstOrDefault(x =>
                    x != null && string.Equals(x.RemoteAccessIpAddress, pc.IpAddress.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    pc.SharedStatusText = _ui.IsCentralShareEnabled ? "DB 미등록" : "DB 미연결";
                    continue;
                }

                pc.SharedStatusText = match.DisplayStatus;
                pc.SharedUserAccount = match.AccessUserId;
                pc.SharedClientPc = match.AccessPcName;
            }

            _ui.MergeRemoteComputersFromDb(shared, ip => _tracker.IsTracking && string.Equals(ip, _activeIpAddress, StringComparison.OrdinalIgnoreCase));
        }

        public async Task ConnectCmcWithPmpAsync(RemoteComputerItemViewModel item)
        {
            string pcLabel = string.IsNullOrWhiteSpace(item.PcName) ? "PC" : item.PcName.Trim();
            string shareKey = string.IsNullOrWhiteSpace(item.IpAddress) ? pcLabel : item.IpAddress.Trim();

            if (!_share.IsConfigured || (!_ui.IsCentralShareEnabled))
            {
                await _ui.PopupShowInfoAsync("원격 접속",
                    "DB 연결 실패 — 원격 접속을 시작할 수 없습니다.\nApp.config의 GbcWorkHubDb(Golden Oracle)를 설정하세요.")
                    .ConfigureAwait(true);
                return;
            }

            var open = CmcPmpBrowserLauncher.TryOpenAutoLogon();
            if (!open.Succeeded)
            {
                await _ui.PopupShowInfoAsync("CMC / PMP", open.Message).ConfigureAwait(true);
                return;
            }

            _pendingCmcSessionToken = RemotePcShareBiz.NewSessionToken();
            TfsClipboardAckService.TryAnnounceSessionToken(
                _pendingCmcSessionToken, null, CmcSessionTokenHoldMs, pcLabel);
            DiagnosticLogger.Info("CMC_CONNECT",
                "SESSION_TOKEN announced for remote agent gate Token=" + TokenPrefix(_pendingCmcSessionToken));

            _cmcMonitor.StartWatching(shareKey, pcLabel);

            // 이미 웹 RDP가 떠 있으면 즉시 점유 시도
            if (_cmcMonitor.IsSessionVisible || CmcPmpSessionMonitor.ProbeSessionVisible(pcLabel))
                await TryReserveCmcOccupancyAsync(shareKey, pcLabel).ConfigureAwait(true);

            _ui.RunOnUi(() =>
            {
                _ui.SetLastReceiveMessage("CMC: 브라우저에서 " + pcLabel + " 웹 RDP 접속 시 점유됩니다. (목록/로그인만으로는 점유 안 함)");
            });

            DiagnosticLogger.Info("CMC_CONNECT",
                "Browser opened ShareKey=" + shareKey + " Pc=" + pcLabel
                + " Url=" + CmcPmpBrowserLauncher.GetAutoLogonUrl());

            await _ui.PopupShowInfoAsync(
                "CMC / PMP",
                "PMP Auto Logon 페이지를 열었습니다.\n\n"
                + "1) 로그인 후 " + pcLabel + " 에 접속하세요.\n"
                + "2) 웹 RDP(rdp.ma) 창이 보이면 Work Hub가 점유합니다.\n"
                + "3) 해당 창을 닫으면 점유가 해제됩니다.\n\n"
                + "(VPN/목록만 연 상태에서는 점유하지 않습니다.)").ConfigureAwait(true);
        }

        private void OnCmcMismatchedSession(string shareKey, string expectedPc, string windowTitle)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => OnCmcMismatchedSession(shareKey, expectedPc, windowTitle)));
                return;
            }

            DiagnosticLogger.Warn("CMC_PMP",
                "Wrong PC web RDP expected=" + (expectedPc ?? "-")
                + " shareKey=" + (shareKey ?? "-")
                + " title=" + (windowTitle ?? "-"));

            string preview = windowTitle ?? "-";
            if (preview.Length > 80)
                preview = preview.Substring(0, 80);

#pragma warning disable CS4014
            _ui.PopupShowInfoAsync(
                "CMC / PMP",
                "Work Hub에서 선택한 PC는 '" + (expectedPc ?? shareKey) + "' 입니다.\n\n"
                + "지금 열린 웹 RDP는 다른 PC로 보입니다.\n(" + preview + ")\n\n"
                + "선택한 PC로 접속해야 점유됩니다. 다른 PC 접속은 점유하지 않습니다.");
#pragma warning restore CS4014
        }

        private void OnCmcSessionAppeared(string shareKey)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => OnCmcSessionAppeared(shareKey)));
                return;
            }

            string pcName = shareKey;
            var item = _ui.FindGalleryItemByShareKey(shareKey);
            if (item != null && !string.IsNullOrWhiteSpace(item.PcName))
                pcName = item.PcName.Trim();

#pragma warning disable CS4014
            TryReserveCmcOccupancyAsync(shareKey, pcName);
#pragma warning restore CS4014
        }

        private void OnCmcSessionMissing(string shareKey)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => OnCmcSessionMissing(shareKey)));
                return;
            }

            if (!string.Equals(_activeIpAddress, shareKey, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(_activeSessionToken))
                return;

            _ui.RunOnUi(() =>
            {
                _ui.SetLastReceiveMessage("CMC 웹 RDP 창 종료 감지 — 점유 해제");
            });
        }

        private void OnCmcSessionLost(string shareKey)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => OnCmcSessionLost(shareKey)));
                return;
            }

            if (_ui.TfsSync != null && _ui.TfsSync.IsSyncInFlight)
            {
                DiagnosticLogger.Info("CMC_PMP",
                    "Session lost during TFS fetch — occupancy kept ShareKey=" + shareKey);
                _ui.RunOnUi(() =>
                {
                    _ui.SetLastReceiveMessage("TFS 가져오기 중 — 점유 유지");
                });
                return;
            }

#pragma warning disable CS4014
            HandleCmcSessionLostAsync(shareKey);
#pragma warning restore CS4014
        }

        private async Task HandleCmcSessionLostAsync(string shareKey)
        {
            if (string.IsNullOrWhiteSpace(shareKey))
                return;

            if (!string.Equals(_activeIpAddress, shareKey, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(_activeSessionToken))
            {
                DiagnosticLogger.Info("CMC_PMP", "Session lost but no local CMC reserve ShareKey=" + shareKey);
                return;
            }

            if (Interlocked.CompareExchange(ref _cmcSessionLostInFlight, 1, 0) != 0)
            {
                DiagnosticLogger.Info("CMC_PMP", "Session lost ignored — already handling ShareKey=" + shareKey);
                return;
            }

            try
            {
                string token = _activeSessionToken;
                string remoteIp = _activeIpAddress;
                string computerName = string.IsNullOrWhiteSpace(_activeComputerName) ? remoteIp : _activeComputerName;
                DateTime endedAt = DateTime.Now;
                DateTime startedAt = _userSessionStartedAt == default(DateTime) ? endedAt.AddHours(-1) : _userSessionStartedAt;
                bool hadReservedSession = !string.IsNullOrEmpty(token);
                double sessionSeconds = (endedAt - startedAt).TotalSeconds;

                bool shouldPromptTfs = hadReservedSession || sessionSeconds >= 5.0;
                if (shouldPromptTfs && _ui.TfsSync != null)
                {
                    DiagnosticLogger.Info("CMC_PMP",
                        "Web RDP closed — occupancy held until TFS prompt/fetch ShareKey=" + remoteIp);

                    _ui.RunOnUi(() =>
                    {
                        _ui.SetRdpSessionConfirmed(false);
                        _ui.SetConnectionEndedAt(endedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                        _ui.SetLastReceiveMessage("웹 RDP 종료 — TFS 가져오기 동안 점유 유지");
                        _ui.SetStatusSource("CMC_TFS_HOLD");
                        _ui.SetCurrentStatus("사용 중");
                        PushTrackingUiFields();
                    });

                    DiagnosticLogger.Info("TFS_PROMPT_OPENED",
                        "RemoteIp=" + remoteIp
                        + " Purpose=CMC_UserSession"
                        + " SessionToken=" + TokenPrefix(token)
                        + " reserved=" + hadReservedSession
                        + " sessionSec=" + sessionSeconds.ToString("0.0")
                        + " clipboardOnly=True"
                        + " occupancy=held");

                    var request = new TfsSyncRequest
                    {
                        RemoteIp = remoteIp,
                        RemoteComputerName = computerName,
                        SessionToken = token,
                        SessionStartedAt = startedAt,
                        SessionEndedAt = endedAt,
                        ClipboardOnly = true
                    };

                    if (_ui.TfsWorkLog != null)
                    {
                        _ui.TfsWorkLog.UpdateSessionContext(new GBCWorkHub.DTO.WorkLog.WorkSessionContext
                        {
                            RemoteIp = remoteIp,
                            RemoteComputerName = computerName,
                            ClientLocalIp = RemotePcShareBiz.LocalAccessIp,
                            CurrentUserId = Environment.UserDomainName + "\\" + Environment.UserName,
                            CurrentUserName = Environment.UserName,
                            SessionStartedAt = request.SessionStartedAt,
                            SessionEndedAt = request.SessionEndedAt
                        });
                    }

                    await _ui.TfsSync.HandleUserSessionEndedAsync(request).ConfigureAwait(true);
                    _ui.RefreshPendingTfsBadge();

                    bool stillInWebRdp = _cmcMonitor.IsWatching
                        && string.Equals(_cmcMonitor.WatchedShareKey, remoteIp, StringComparison.OrdinalIgnoreCase)
                        && _cmcMonitor.IsSessionVisible;
                    if (stillInWebRdp)
                    {
                        DiagnosticLogger.Info("CMC_PMP",
                            "TFS fetch done — web RDP still open, occupancy kept ShareKey=" + remoteIp);
                        _ui.RunOnUi(() =>
                        {
                            _ui.SetRdpSessionConfirmed(true);
                            _ui.SetLastReceiveMessage("TFS 가져오기 종료 — 웹 RDP가 열려 있어 점유 유지");
                            _ui.SetStatusSource("CMC_PMP_SESSION");
                            _ui.SetCurrentStatus("사용 중");
                            PushTrackingUiFields();
                        });
                        return;
                    }
                }
                else
                {
                    DiagnosticLogger.Info("TFS_PROMPT_SKIPPED",
                        "RemoteIp=" + (remoteIp ?? "-")
                        + " reserved=" + hadReservedSession
                        + " sessionSec=" + sessionSeconds.ToString("0.0")
                        + " purpose=CMC");
                }

                DiagnosticLogger.Info("CMC_PMP", "Releasing occupancy ShareKey=" + remoteIp);
                await ReleaseUserSessionOccupancyOnceAsync(remoteIp, token, 0, RdpLaunchPurpose.UserSession)
                    .ConfigureAwait(true);
                await RefreshRemotePcAfterReleaseAsync(remoteIp).ConfigureAwait(true);

                _ui.RunOnUi(() =>
                {
                    _ui.SetRdpSessionConfirmed(false);
                    _ui.SetConnectionEndedAt(endedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    _ui.SetLastReceiveMessage("CMC 웹 RDP 종료 — 점유 해제됨");
                    _ui.SetStatusSource("CMC_PMP_SESSION_LOST");
                    _ui.SetCurrentStatus("사용 가능");
                    _ui.UpdateGalleryLocalAvailable(remoteIp);
                    _ui.RecalculateStatusCounts();
                    PushTrackingUiFields();
                });
            }
            finally
            {
                Interlocked.Exchange(ref _cmcSessionLostInFlight, 0);
            }
        }

        private async Task TryReserveCmcOccupancyAsync(string shareKey, string pcName)
        {
            if (string.IsNullOrWhiteSpace(shareKey))
                return;

            if (Interlocked.CompareExchange(ref _cmcReserveInFlight, 1, 0) != 0)
                return;

            try
            {
                if (string.Equals(_activeIpAddress, shareKey, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(_activeSessionToken))
                {
                    _ui.RunOnUi(() =>
                    {
                        _ui.SetLastReceiveMessage("CMC 웹 RDP 감지 — 이미 점유 중");
                        _ui.SetRdpSessionConfirmed(true);
                    });
                    return;
                }

                string sessionToken = !string.IsNullOrWhiteSpace(_pendingCmcSessionToken)
                    ? _pendingCmcSessionToken
                    : RemotePcShareBiz.NewSessionToken();
                string computerName = string.IsNullOrWhiteSpace(pcName) ? shareKey : pcName.Trim();
                string userAccount = RemotePcShareBiz.LocalUserAccount;
                string clientPc = RemotePcShareBiz.LocalClientPc;

                bool reserved;
                try
                {
                    reserved = await _share.TryReserveAsync(shareKey, sessionToken, computerName)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("CMC_RESERVE", ex.Message);
                    await _ui.PopupShowInfoAsync("CMC 점유", "DB 선점 중 오류: " + ex.Message
                        + "\n\nMSDWHTKD에 REMOTE_ACCS_IP_ADDR='" + shareKey + "' 행이 필요합니다.")
                        .ConfigureAwait(true);
                    return;
                }

                if (!reserved)
                {
                    RemotePcStatus current = null;
                    try { current = await _share.GetByRemoteIpAsync(shareKey).ConfigureAwait(true); }
                    catch { /* ignore */ }

                    if (current != null)
                        _ui.UpdateGalleryFromStatus(current);

                    string who = current == null
                        ? "(DB 행 없음 — sql/08_CMC_MSDWHTKD_P-BCTECH1.sql 실행)"
                        : ((current.AccessUserId ?? "?") + " / " + (current.AccessPcName ?? "?"));
                    await _ui.PopupShowInfoAsync("CMC 점유",
                        "웹 RDP는 감지됐지만 점유할 수 없습니다.\n" + who).ConfigureAwait(true);
                    return;
                }

                _activeSessionToken = sessionToken;
                _activeComputerName = computerName;
                _activeIpAddress = shareKey;
                _userSessionStartedAt = DateTime.Now;
                Interlocked.Exchange(ref _userSessionReleaseGate, 0);
                _ui.SetSessionTokenDisplay(sessionToken);

                _ui.UpdateGalleryLocalInUse(shareKey, userAccount, clientPc, sessionToken);

                _ui.RunOnUi(() =>
                {
                    _ui.SetRemoteComputerName(computerName);
                    _ui.SetWorkHubUserAccount(userAccount);
                    _ui.SetWorkHubClientPc(clientPc);
                    _ui.SetCurrentStatus("사용 중");
                    _ui.SetStatusSource("CMC_PMP_SESSION");
                    _ui.SetLastReceiveMessage("CMC 웹 RDP 감지 — DB 점유");
                    _ui.SetConnectionEndedAt("-");
                    _ui.SetConnectionRequestedAt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    _ui.SetConnectionConfirmedAt(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    _ui.SetRdpSessionConfirmed(true);
                });

                TfsClipboardAckService.TryAnnounceSessionToken(
                    sessionToken, null, CmcSessionTokenHoldMs, computerName);
                DiagnosticLogger.Info("CMC_RESERVE",
                    "ShareKey=" + shareKey + " Token=" + sessionToken);
            }
            finally
            {
                Interlocked.Exchange(ref _cmcReserveInFlight, 0);
            }
        }

        /// <summary>내가 점유한 채 mstsc만 사라진 경우 DB 점유 해제.</summary>
        public async Task<bool> TryReleaseStaleOwnedPcAsync(RemoteComputerItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.IpAddress))
                return false;

            string ip = item.IpAddress.Trim();
            RemotePcStatus status = null;
            try
            {
                status = await _share.GetByRemoteIpAsync(ip).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await _ui.PopupShowInfoAsync("원격 접속", "상태 조회 실패: " + ex.Message).ConfigureAwait(true);
                return false;
            }

            if (status == null || string.IsNullOrWhiteSpace(status.SessionToken))
            {
                await _ui.PopupShowInfoAsync("원격 접속",
                    "잔여 점유를 해제할 수 없습니다. (세션 토큰 없음)\n앱을 다시 시작해 상태 복구를 시도하세요.")
                    .ConfigureAwait(true);
                return false;
            }

            if (_ui.Popup != null)
            {
                var result = await _ui.Popup.ShowConfirmAsync(new PopupRequest
                {
                    Kind = PopupKind.Confirm,
                    Icon = PopupIconKind.Warning,
                    Title = "잔여 점유 복구",
                    Message = "실제 접속은 없는데 DB만 사용 중으로 남아 있습니다.\n점유를 해제한 뒤 다시 접속할까요?",
                    Detail = (item.PcName ?? "-") + " (" + ip + ")",
                    DedupKey = "StaleOwnedConnect:" + ip,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition("점유 해제 후 접속", PopupResultType.Primary, isDefault: true),
                        new PopupButtonDefinition("취소", PopupResultType.Secondary, isCancel: true)
                    }
                }).ConfigureAwait(true);

                if (result == null || !result.IsPrimary)
                    return false;
            }

            try
            {
                bool ok = await _share.ReleaseAsync(ip, status.SessionToken, "STALE_OWNED_RECONNECT").ConfigureAwait(true);
                if (!ok)
                {
                    await _ui.PopupShowInfoAsync("원격 접속", "점유 해제에 실패했습니다. 앱을 다시 시작해 보세요.")
                        .ConfigureAwait(true);
                    return false;
                }

                _ui.UpdateGalleryLocalAvailable(ip);
                DiagnosticLogger.Info("STALE_OWNED_RELEASE", "RemoteIp=" + ip);
                return true;
            }
            catch (Exception ex)
            {
                await _ui.PopupShowInfoAsync("원격 접속", "점유 해제 오류: " + ex.Message).ConfigureAwait(true);
                return false;
            }
        }

        /// <summary>
        /// RC: VPN 미연결이면 Forti → 창 닫힘 대기. 이미 VPN이면 바로 .rdp + DB 점유.
        /// </summary>
        public async Task ConnectRcWithPublishedRdpAsync(RemoteComputerItemViewModel item)
        {
            string pcLabel = string.IsNullOrWhiteSpace(item.PcName) ? "PC" : item.PcName.Trim();
            _ui.RefreshRcVpnBadges();

            bool vpnReady = Services.FortiVpnStatus.IsConnected();
            if (!vpnReady)
            {
                var forti = Services.FortiClientLauncher.TryLaunch();
                if (!forti.Succeeded)
                {
                    await _ui.PopupShowInfoAsync("FortiClient", forti.Message).ConfigureAwait(true);
                    return;
                }

                _ui.RunOnUi(() =>
                {
                    _ui.SetLastReceiveMessage("FortiClient 대기 중 — VPN 연결 후 창을 닫으면 원격 접속합니다.");
                });

                var wait = await Services.FortiClientLauncher
                    .WaitUntilMainWindowClosedAsync()
                    .ConfigureAwait(true);

                if (!wait.Succeeded)
                {
                    await _ui.PopupShowInfoAsync("FortiClient", wait.Message).ConfigureAwait(true);
                    return;
                }

                // 창만 닫히고 VPN 미연결이면 다음 단계(.rdp)로 가지 않음 (대기 없이 즉시 판정)
                _ui.RefreshRcVpnBadges();
                if (!Services.FortiVpnStatus.IsConnected())
                {
                    await _ui.PopupShowInfoAsync(
                        "FortiClient",
                        "VPN이 연결되지 않았습니다.\n"
                        + "FortiClient에서 VPN 연결을 완료한 뒤 다시 접속해 주세요.").ConfigureAwait(true);
                    DiagnosticLogger.Warn("RC_VPN", "Forti window closed but VPN not connected — RDP aborted");
                    return;
                }
            }
            else if (!Services.FortiVpnStatus.IsConnected())
            {
                // 배지는 연결로 보였더라도 접속 직전 한 번 더 확인
                _ui.RefreshRcVpnBadges();
                await _ui.PopupShowInfoAsync(
                    "FortiClient",
                    "VPN이 연결되지 않았습니다.\nFortiClient에서 VPN 연결 후 다시 시도해 주세요.")
                    .ConfigureAwait(true);
                return;
            }

            var rdp = Services.PublishedRdpLauncher.TryFindForPc(item.SiteCode, item.PcName);
            if (!rdp.Succeeded)
            {
                await _ui.PopupShowInfoAsync("원격 접속", rdp.Message).ConfigureAwait(true);
                return;
            }

            var dto = item.ToDto();
            if (string.IsNullOrWhiteSpace(dto.IpAddress))
                dto.IpAddress = pcLabel;

            string message = await StartRemoteSessionAsync(dto, rdp.Path).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(message))
                await _ui.PopupShowInfoAsync("원격 접속", message).ConfigureAwait(true);
            else
                DiagnosticLogger.Info("RC_RDP_STARTED", "Pc=" + pcLabel + " File=" + rdp.Path
                    + " ShareKey=" + dto.IpAddress
                    + " VpnWasReady=" + vpnReady);
        }



        public async Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsAsync(string ip, int take)
        {
            return await _share.GetRecentUsageLogsAsync(ip, take).ConfigureAwait(true);
        }

        public async Task<RemotePcStatus> GetStatusByIpAsync(string ip)
        {
            return await _share.GetByRemoteIpAsync(ip).ConfigureAwait(true);
        }

        public RdpStatusBiz.ParseResult TryHandleRdpClipboardText(string text)
        {
            return _rdpStatus.TryHandleClipboardText(text);
        }

        public RdpStatusBiz.FreshnessDecision EvaluateClipboardFreshness(
            string payloadHash,
            RdpStatusPayload payload,
            string computerKey,
            out string freshnessReason)
        {
            LastProcessedRdpState existing;
            _lastByComputer.TryGetValue(computerKey, out existing);
            return _rdpStatus.EvaluateFreshness(
                payloadHash,
                payload,
                existing != null ? existing.LatestRecordId : null,
                existing != null ? existing.CollectedAt : null,
                existing != null ? existing.PayloadHash : null,
                out freshnessReason);
        }

        public string DetermineClipboardStatus(RdpStatusPayload payload, string determinedStatus)
        {
            return determinedStatus ?? _rdpStatus.DetermineStatus(payload);
        }

        public string ResolveClientComputerName(string clientName)
        {
            return _rdpStatus.ResolveClientComputerName(clientName);
        }

        public DateTime? TryParseCollectedAt(string collectedAt)
        {
            return _rdpStatus.TryParseCollectedAt(collectedAt);
        }

        public System.Collections.Generic.List<RdpStatusBiz.ParsedEventItem> ParseClipboardEvents(RdpStatusPayload payload)
        {
            return _rdpStatus.ParseEvents(payload);
        }

        public string GetEventDescription(int eventId)
        {
            return _rdpStatus.GetEventDescription(eventId);
        }

        public void RememberLastProcessed(
            string computerKey,
            RdpStatusPayload payload,
            string payloadHash)
        {
            _lastByComputer[computerKey] = new LastProcessedRdpState
            {
                ComputerName = computerKey,
                LatestRecordId = payload != null ? payload.LatestRecordId : null,
                CollectedAt = payload != null ? _rdpStatus.TryParseCollectedAt(payload.CollectedAt) : null,
                PayloadHash = payloadHash,
                Payload = payload
            };
        }

        public bool TryConfirmConnectionFromClipboard(RdpStatusPayload payload, string computerKey)
        {
            bool tracking = _tracker.IsTracking;
            string trackedName = _tracker.RemoteComputerName ?? TrackedComputerName;
            bool nameMatches = string.Equals(computerKey, trackedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(computerKey, _tracker.TargetIp, StringComparison.OrdinalIgnoreCase);
            bool isConfirm = RdpSessionTrackingService.IsConnectionConfirmPayload(payload) && nameMatches;
            if (isConfirm && tracking)
            {
                _tracker.ConfirmConnection(payload);
                return true;
            }
            return false;
        }

        public bool IsTrackingLocally { get { return _tracker.IsTracking; } }

        public string TrackerRemoteComputerName
        {
            get { return _tracker.RemoteComputerName ?? TrackedComputerName; }
        }

        public string TrackerTargetIp { get { return _tracker.TargetIp; } }

        public RdpLaunchPurpose TrackerLaunchPurpose { get { return _tracker.LaunchPurpose; } }

        public bool IsLocalSessionActiveForIp(string ip)
        {
            return _tracker.IsTracking
                && string.Equals(ip, _activeIpAddress, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsLocalSessionAliveFor(RemoteComputerItemViewModel item)
        {
            if (item == null) return false;
            bool stillTracked = _tracker.IsTracking
                && string.Equals(_tracker.TargetIp, item.IpAddress, StringComparison.OrdinalIgnoreCase);
            bool mstscIp = !string.IsNullOrWhiteSpace(item.IpAddress)
                && _tracker.IsMstscRunningForIp(item.IpAddress);
            bool mstscPc = !string.IsNullOrWhiteSpace(item.PcName)
                && _tracker.IsMstscRunningForIp(item.PcName);
            return stillTracked || mstscIp || mstscPc;
        }

        public bool IsTfsSyncReconnectOrInFlight()
        {
            return _tracker.LaunchPurpose == RdpLaunchPurpose.TfsSyncReconnect
                || (_ui != null && _ui.TfsSync != null && _ui.TfsSync.IsSyncInFlight);
        }
        public string GetAppCloseWarning()
        {
            return null;
        }

        public async Task HandleAppClosingAsync()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }

            // 원격이 살아 있으면 점유 유지 (RC mstsc / CMC 웹 RDP)
            bool remoteAlive = _tracker.IsTracking
                || IsCmcRemoteStillAliveLocally(_activeIpAddress, _activeComputerName);

            if (!remoteAlive
                && !string.IsNullOrEmpty(_activeSessionToken)
                && !string.IsNullOrEmpty(_activeIpAddress)
                && _ui.IsCentralShareEnabled)
            {
                await _share.ReleaseAsync(_activeIpAddress, _activeSessionToken).ConfigureAwait(false);
            }
            else if (remoteAlive)
            {
                DiagnosticLogger.Info("APP_CLOSE",
                    "Keep occupancy — remote still alive Ip=" + (_activeIpAddress ?? "-")
                    + " Token=" + TokenPrefix(_activeSessionToken));
            }
        }


        private static int ReadIntSetting(string key, int defaultValue)
        {
            try
            {
                string raw = ConfigurationManager.AppSettings[key];
                int value;
                if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out value))
                    return value;
            }
            catch { }
            return defaultValue;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
            if (_eventsWired)
            {
                _tracker.RdpStarted -= OnRdpStarted;
                _tracker.RdpConnectionConfirmed -= OnRdpConnectionConfirmed;
                _tracker.RdpEnded -= OnRdpEnded;
                _tracker.RdpTrackingError -= OnRdpTrackingError;
                _cmcMonitor.SessionAppeared -= OnCmcSessionAppeared;
                _cmcMonitor.SessionMissing -= OnCmcSessionMissing;
                _cmcMonitor.SessionLost -= OnCmcSessionLost;
                _cmcMonitor.MismatchedSessionDetected -= OnCmcMismatchedSession;
                _eventsWired = false;
            }
            if (_tracker != null) _tracker.Dispose();
            if (_cmcMonitor != null) _cmcMonitor.Dispose();
        }

        /// <summary>
        /// Handles one GBCWORKHUB_SESSION_RESULT:: clipboard payload from the remote
        /// SessionAgent — parses it, persists the classified files to XSUP.MSDWHTKH_FILE
        /// (keyed by the token embedded in the payload, independent of whatever session is
        /// currently active locally), and logs the temporary developer/debug report.
        /// </summary>
        public async void HandleSessionChangeResultReceived(string clipboardText)
        {
            try
            {
                var report = await _devSessionFiles.HandleReceivedResultAsync(clipboardText).ConfigureAwait(true);
                if (report == null)
                {
                    DiagnosticLogger.Warn("DEV_SESSION_RESULT", "Received payload could not be parsed or had no SESSION_TOKEN.");
                    return;
                }

                DiagnosticLogger.Info("DEV_SESSION_RESULT",
                    "token=" + TokenPrefix(report.SessionToken)
                    + " files=" + report.Files.Count
                    + " changesets=" + report.Changesets.Count
                    + " truncated=" + report.Truncated);
                DiagnosticLogger.Info("DEV_SESSION_REPORT", DevSessionReportFormatter.Format(report));
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("DEV_SESSION_RESULT", ex.Message);
            }
        }
    }
}