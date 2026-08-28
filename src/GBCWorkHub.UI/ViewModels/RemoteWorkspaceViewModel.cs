using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Models;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.ViewModels
{
    public class RemoteWorkspaceViewModel : ViewModelBase, IDisposable
    {
        private readonly RemotePcBiz _remotePcBiz = new RemotePcBiz();
        private readonly RemoteSessionController _session = new RemoteSessionController();
        private TfsWorkLogViewModel _tfsWorkLog;
        private WorkLog.WorkLogListViewModel _workLogList;
        private Action _refreshPendingTfsBadge;
        private readonly Action _onSiteContextChanged;

        private ObservableCollection<RemoteComputerItemViewModel> _remoteComputers;
        private ICollectionView _filteredRemoteComputers;
        private RemoteComputerItemViewModel _selectedRemoteComputer;
        private string _searchText = string.Empty;
        private string _selectedStatusFilter = "ALL";
        private RemoteComputerViewMode _selectedViewMode = RemoteComputerViewMode.Card;
        private int _totalCount;
        private int _availableCount;
        private int _connectingCount;
        private int _inUseCount;
        private int _checkRequiredCount;
        private DateTime? _lastRefreshedAt;
        private bool _isRefreshing;
        private bool _isGalleryLoading;
        private string _selectedSiteCode;
        private ObservableCollection<RemoteSiteDto> _sites;

        private ObservableCollection<RdpEventViewModel> _recentEvents;
        private int _recentEventCount;
        private ObservableCollection<RemotePcUsageLogItemViewModel> _recentUsageLogs;
        private int _usageLogLoadGeneration;

        private const int MaxRecentEvents = 20;
        private const int RecentUsageLogTake = 3;
        private const string TrackedComputerName = "KEB-7VY98V3";

        private string _remoteComputerName = "-";
        private string _remoteWindowsUser = "-";
        private string _clientComputerName = "-";
        private string _collectedAt = "-";
        private string _latestEventId = "-";
        private string _currentStatus = "-";
        private string _sessionRaw;
        private string _lastReceiveMessage = "GBC Work Hub 데이터를 기다리는 중";
        private string _statusSource = "-";
        private bool _isRdpSessionConfirmed;
        private string _trackedProcessIds = "-";
        private string _rdpLaunchTime = "-";
        private string _connectionConfirmedAt = "-";
        private string _connectionEndedAt = "-";
        private string _connectionRequestedAt = "-";
        private string _updatedDateTime = "-";
        private string _workHubUserAccount = "-";
        private string _workHubClientPc = "-";
        private string _localAccessIp = "-";
        private string _sessionTokenDisplay = "-";
        private string _centralDbStatus = "중앙 DB: 확인 중…";
        private bool _isCentralDbConnected;
        private bool _isCentralShareEnabled;
        private bool _disposed;

        private IPopupService _popup;
        private TfsSyncCoordinator _tfsSync;

        public RemoteWorkspaceViewModel()
            : this(null)
        {
        }

        public RemoteWorkspaceViewModel(Action onSiteContextChanged)
        {
            _onSiteContextChanged = onSiteContextChanged;
            _recentEvents = new ObservableCollection<RdpEventViewModel>();
            _recentUsageLogs = new ObservableCollection<RemotePcUsageLogItemViewModel>();
            _remoteComputers = new ObservableCollection<RemoteComputerItemViewModel>();
            _sites = new ObservableCollection<RemoteSiteDto>(_remotePcBiz.GetSiteList() ?? new List<RemoteSiteDto>());
            _filteredRemoteComputers = CollectionViewSource.GetDefaultView(_remoteComputers);
            _filteredRemoteComputers.Filter = FilterRemoteComputer;

            WorkHubUserAccount = RemotePcShareBiz.LocalUserAccount;
            WorkHubClientPc = RemotePcShareBiz.LocalClientPc;
            LocalAccessIp = RemotePcShareBiz.LocalAccessIp ?? "-";

            RefreshCommand = new RelayCommand(() => { var _ = RefreshFromDbAsync(); }, () => !IsRefreshing);
            SelectStatusFilterCommand = new RelayCommand<string>(SelectStatusFilter);
            ConnectRemoteComputerCommand = new RelayCommand<RemoteComputerItemViewModel>(item => { var _ = ConnectRemoteComputerAsync(item); });
            CheckRemoteComputerCommand = new RelayCommand<RemoteComputerItemViewModel>(item => { var _ = CheckRemoteComputerAsync(item); });
            ChangeViewModeCommand = new RelayCommand<string>(SetViewMode);
            SelectRemoteComputerCommand = new RelayCommand<RemoteComputerItemViewModel>(SelectRemoteComputer);
            PrimaryActionCommand = new RelayCommand<RemoteComputerItemViewModel>(item => { var _ = ExecutePrimaryActionAsync(item); });
            OpenSiteCommand = new RelayCommand<string>(code => { var _ = OpenSiteAsync(code); });
            BackToSitesCommand = new RelayCommand(BackToSites);

            ApplyWorkHubUserToSession();
        }

        /// <summary>Shell에서 popup / TFS / WorkLog 참조를 주입한다.</summary>
        public void AttachShellServices(
            IPopupService popup,
            TfsWorkLogViewModel tfsWorkLog,
            WorkLog.WorkLogListViewModel workLogList,
            TfsSyncCoordinator tfsSync,
            Action<string> requestSelectMainTab,
            Action refreshPendingTfsBadge)
        {
            _popup = popup;
            _tfsWorkLog = tfsWorkLog;
            _workLogList = workLogList;
            _tfsSync = tfsSync;
            // requestSelectMainTab: Shell(MainViewModel) owns tab navigation; unused here after phase 1 split.
            _refreshPendingTfsBadge = refreshPendingTfsBadge;
            _session.AttachUi(new SessionUiBridge(this));
        }

        public string RemoteComputerName
        {
            get { return _remoteComputerName; }
            set { SetProperty(ref _remoteComputerName, value); }
        }

        public string RemoteWindowsUser
        {
            get { return _remoteWindowsUser; }
            set { SetProperty(ref _remoteWindowsUser, value); }
        }

        public string ClientComputerName
        {
            get { return _clientComputerName; }
            set { SetProperty(ref _clientComputerName, value); }
        }

        public string CollectedAt
        {
            get { return _collectedAt; }
            set { SetProperty(ref _collectedAt, value); }
        }

        public string LatestEventId
        {
            get { return _latestEventId; }
            set { SetProperty(ref _latestEventId, value); }
        }

        public string CurrentStatus
        {
            get { return _currentStatus; }
            set { SetProperty(ref _currentStatus, value); }
        }

        public string SessionRaw
        {
            get { return _sessionRaw; }
            set { SetProperty(ref _sessionRaw, value); }
        }

        public string LastReceiveMessage
        {
            get { return _lastReceiveMessage; }
            set { SetProperty(ref _lastReceiveMessage, value); }
        }

        public string StatusSource
        {
            get { return _statusSource; }
            set { SetProperty(ref _statusSource, value); }
        }

        public bool IsRdpSessionConfirmed
        {
            get { return _isRdpSessionConfirmed; }
            set { SetProperty(ref _isRdpSessionConfirmed, value); }
        }

        public string TrackedProcessIds
        {
            get { return _trackedProcessIds; }
            set { SetProperty(ref _trackedProcessIds, value); }
        }

        public string RdpLaunchTime
        {
            get { return _rdpLaunchTime; }
            set { SetProperty(ref _rdpLaunchTime, value); }
        }

        public string ConnectionConfirmedAt
        {
            get { return _connectionConfirmedAt; }
            set { SetProperty(ref _connectionConfirmedAt, value); }
        }

        public string ConnectionEndedAt
        {
            get { return _connectionEndedAt; }
            set { SetProperty(ref _connectionEndedAt, value); }
        }

        public string ConnectionRequestedAt
        {
            get { return _connectionRequestedAt; }
            set { SetProperty(ref _connectionRequestedAt, value); }
        }

        public string UpdatedDateTime
        {
            get { return _updatedDateTime; }
            set { SetProperty(ref _updatedDateTime, value); }
        }

        public string LocalAccessIp
        {
            get { return _localAccessIp; }
            set { SetProperty(ref _localAccessIp, value); }
        }

        public string WorkHubUserAccount
        {
            get { return _workHubUserAccount; }
            set { SetProperty(ref _workHubUserAccount, value); }
        }

        public string WorkHubClientPc
        {
            get { return _workHubClientPc; }
            set { SetProperty(ref _workHubClientPc, value); }
        }

        public string SessionTokenDisplay
        {
            get { return _sessionTokenDisplay; }
            set { SetProperty(ref _sessionTokenDisplay, value); }
        }

        public string CentralDbStatus
        {
            get { return _centralDbStatus; }
            set { SetProperty(ref _centralDbStatus, value); }
        }

        public bool IsCentralDbConnected
        {
            get { return _isCentralDbConnected; }
            set { SetProperty(ref _isCentralDbConnected, value); }
        }

        public bool IsCentralShareEnabled
        {
            get { return _isCentralShareEnabled; }
            set { SetProperty(ref _isCentralShareEnabled, value); }
        }

        public ObservableCollection<RdpEventViewModel> RecentEvents
        {
            get { return _recentEvents; }
            private set { SetProperty(ref _recentEvents, value); }
        }

        public int RecentEventCount
        {
            get { return _recentEventCount; }
            private set { SetProperty(ref _recentEventCount, value); }
        }

        public ObservableCollection<RemotePcUsageLogItemViewModel> RecentUsageLogs
        {
            get { return _recentUsageLogs; }
            private set { SetProperty(ref _recentUsageLogs, value); }
        }

        public TfsWorkLogViewModel TfsWorkLog
        {
            get { return _tfsWorkLog; }
        }

        public WorkLog.WorkLogListViewModel WorkLogList
        {
            get { return _workLogList; }
        }

        public ObservableCollection<RemoteComputerItemViewModel> RemoteComputers
        {
            get { return _remoteComputers; }
        }

        public ICollectionView FilteredRemoteComputers
        {
            get { return _filteredRemoteComputers; }
        }

        public RemoteComputerItemViewModel SelectedRemoteComputer
        {
            get { return _selectedRemoteComputer; }
            set
            {
                if (_selectedRemoteComputer == value)
                    return;

                if (_selectedRemoteComputer != null)
                    _selectedRemoteComputer.IsSelected = false;

                _selectedRemoteComputer = value;

                if (_selectedRemoteComputer != null)
                    _selectedRemoteComputer.IsSelected = true;

                RaisePropertyChanged("SelectedRemoteComputer");
                SyncDetailPanelFromSelected();
                var _ = LoadRecentUsageLogsAsync(_selectedRemoteComputer);
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    RefreshGalleryFilter();
            }
        }

        public string SelectedStatusFilter
        {
            get { return _selectedStatusFilter; }
            set
            {
                if (SetProperty(ref _selectedStatusFilter, string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant()))
                    RefreshGalleryFilter();
            }
        }

        public RemoteComputerViewMode SelectedViewMode
        {
            get { return _selectedViewMode; }
            set
            {
                if (SetProperty(ref _selectedViewMode, value))
                {
                    RaisePropertyChanged("IsCardViewMode");
                    RaisePropertyChanged("IsListViewMode");
                }
            }
        }

        public bool IsCardViewMode
        {
            get { return SelectedViewMode == RemoteComputerViewMode.Card; }
        }

        public bool IsListViewMode
        {
            get { return SelectedViewMode == RemoteComputerViewMode.List; }
        }

        public int TotalCount
        {
            get { return _totalCount; }
            private set { SetProperty(ref _totalCount, value); }
        }

        public int AvailableCount
        {
            get { return _availableCount; }
            private set { SetProperty(ref _availableCount, value); }
        }

        public int ConnectingCount
        {
            get { return _connectingCount; }
            private set { SetProperty(ref _connectingCount, value); }
        }

        public int InUseCount
        {
            get { return _inUseCount; }
            private set { SetProperty(ref _inUseCount, value); }
        }

        public int CheckRequiredCount
        {
            get { return _checkRequiredCount; }
            private set { SetProperty(ref _checkRequiredCount, value); }
        }

        public DateTime? LastRefreshedAt
        {
            get { return _lastRefreshedAt; }
            private set
            {
                if (SetProperty(ref _lastRefreshedAt, value))
                    RaisePropertyChanged("LastRefreshedAtDisplay");
            }
        }

        public string LastRefreshedAtDisplay
        {
            get
            {
                return LastRefreshedAt.HasValue
                    ? LastRefreshedAt.Value.ToString("HH:mm:ss")
                    : "-";
            }
        }

        public string TotalCountDisplay
        {
            get { return "전체 " + TotalCount + "대"; }
        }

        public bool IsRefreshing
        {
            get { return _isRefreshing; }
            private set
            {
                if (SetProperty(ref _isRefreshing, value))
                {
                    var refresh = RefreshCommand as RelayCommand;
                    if (refresh != null)
                        refresh.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>사이트 PC 갤러리 로딩 중 — 스켈레톤 UI 표시.</summary>
        public bool IsGalleryLoading
        {
            get { return _isGalleryLoading; }
            private set { SetProperty(ref _isGalleryLoading, value); }
        }

        public ICommand RefreshCommand { get; private set; }
        public ICommand SelectStatusFilterCommand { get; private set; }
        public ICommand ConnectRemoteComputerCommand { get; private set; }
        public ICommand CheckRemoteComputerCommand { get; private set; }
        public ICommand ChangeViewModeCommand { get; private set; }
        public ICommand SelectRemoteComputerCommand { get; private set; }
        public ICommand PrimaryActionCommand { get; private set; }
        public ICommand OpenSiteCommand { get; private set; }
        public ICommand BackToSitesCommand { get; private set; }

        /// <summary>TfsSyncCoordinator 등 Shell wiring용.</summary>
        public RemoteSessionController Session { get { return _session; } }
        public RdpSessionTrackingService RdpTracker { get { return _session.TrackerForShell; } }
        public Action<string> UpdateGalleryAvailableHandler { get { return UpdateGalleryItemLocalAvailable; } }

        public ObservableCollection<RemoteSiteDto> Sites
        {
            get { return _sites; }
        }

        public bool IsAuroraSiteEnabled { get { return IsSiteEnabled("AURORA"); } }
        public bool IsCmcSiteEnabled { get { return IsSiteEnabled("CMC"); } }
        public bool IsRcSiteEnabled { get { return IsSiteEnabled("RC"); } }
        public bool IsMNGHASiteEnabled { get { return IsSiteEnabled("MNGHA"); } }

        private bool IsSiteEnabled(string siteCode)
        {
            if (_sites == null || string.IsNullOrWhiteSpace(siteCode))
                return false;
            var site = _sites.FirstOrDefault(x =>
                x != null && string.Equals(x.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase));
            return site != null && site.IsEnabled;
        }

        public string SelectedSiteCode
        {
            get { return _selectedSiteCode; }
            private set
            {
                if (SetProperty(ref _selectedSiteCode, value))
                {
                    RaisePropertyChanged("IsSitePickerVisible");
                    RaisePropertyChanged("IsGalleryVisible");
                    RaisePropertyChanged("HeaderTitle");
                    if (_onSiteContextChanged != null)
                        _onSiteContextChanged();
                }
            }
        }

        public bool IsSitePickerVisible
        {
            get { return string.IsNullOrWhiteSpace(SelectedSiteCode); }
        }

        public bool IsGalleryVisible
        {
            get { return !string.IsNullOrWhiteSpace(SelectedSiteCode); }
        }

        public string HeaderTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SelectedSiteCode))
                    return SelectedSiteCode;
                return string.Empty;
            }
        }

        /// <summary>PC 목록 새로고침용 콜백 (MainWindow 등록, 하위 호환)</summary>
        public Func<Task> RefreshPcListRequested { get; set; }

        public Task InitializeCentralShareAsync()
        {
            return _session.InitializeCentralShareAsync();
        }

        public async Task RefreshFromDbAsync()
        {
            IsRefreshing = true;
            if (IsGalleryVisible)
                IsGalleryLoading = true;
            try
            {
                // 스켈레톤은 PC 목록 구성까지만. DB 상태/업무기록 조회는 목록 표시 후 진행.
                if (IsGalleryVisible)
                {
                    await LoadSelectedSiteGalleryAsync().ConfigureAwait(true);
                    IsGalleryLoading = false;
                }

                await PollSharedStatusAsync(forceUi: true).ConfigureAwait(true);
                if (RefreshPcListRequested != null)
                    await RefreshPcListRequested().ConfigureAwait(true);
                if (WorkLogList != null)
                    await WorkLogList.ReloadFromDbAsync(force: true).ConfigureAwait(true);
            }
            finally
            {
                IsGalleryLoading = false;
                IsRefreshing = false;
            }
        }

        public async Task OpenSiteAsync(string siteCode)
        {
            if (string.IsNullOrWhiteSpace(siteCode))
                return;

            var site = Sites.FirstOrDefault(x =>
                x != null && string.Equals(x.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase));
            if (site != null && !site.IsEnabled)
            {
                await ShowInfoPopupAsync("알림",
                    siteCode + " 사이트는 아직 준비 중입니다.\nApp.config에 " + siteCode + ".Host 를 설정하세요.").ConfigureAwait(true);
                return;
            }

            string siteKey = siteCode.Trim().ToUpperInvariant();

            var pcs = _remotePcBiz.GetRemotePcListBySite(siteCode);
            if (pcs == null || pcs.Count == 0)
            {
                await ShowInfoPopupAsync("알림",
                    siteCode + " 사이트에 등록된 PC가 없습니다.\nApp.config " + siteCode + ".Host / " + siteCode + ".PcNames 를 확인하세요.").ConfigureAwait(true);
                return;
            }

            IsGalleryLoading = true;
            try
            {
                SelectedSiteCode = siteKey;
                SearchText = string.Empty;
                SelectedStatusFilter = "ALL";
                SelectedRemoteComputer = null;
                if (WorkLogList != null)
                    WorkLogList.SetSiteContext(siteKey);

                RunOnUi(() =>
                {
                    RemoteComputers.Clear();
                    RecalculateStatusCounts();
                });

                await LoadSelectedSiteGalleryAsync().ConfigureAwait(true);
                if (string.Equals(siteKey, "RC", StringComparison.OrdinalIgnoreCase))
                    RefreshRcVpnBadges();
            }
            finally
            {
                // PC 카드는 로컬 목록만으로 바로 표시. 공유 DB 폴링은 백그라운드.
                IsGalleryLoading = false;
            }

            if (IsCentralShareEnabled)
                await PollSharedStatusAsync(forceUi: true).ConfigureAwait(true);
            if (string.Equals(siteKey, "CMC", StringComparison.OrdinalIgnoreCase) && IsCentralShareEnabled)
                await _session.TryAdoptLiveCmcSessionsAsync().ConfigureAwait(true);
        }

        public void BackToSites()
        {
            SelectedSiteCode = null;
            SearchText = string.Empty;
            SelectedStatusFilter = "ALL";
            SelectedRemoteComputer = null;
            if (WorkLogList != null)
                WorkLogList.SetSiteContext(null);

            RunOnUi(() =>
            {
                RemoteComputers.Clear();
                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        public Task<string> StartRemoteSessionAsync(RemotePcDto pc)
        {
            return _session.StartRemoteSessionAsync(pc);
        }

        public Task<string> StartRemoteSessionAsync(RemotePcDto pc, string publishedRdpPath)
        {
            return _session.StartRemoteSessionAsync(pc, publishedRdpPath);
        }

        public void HandleClipboardText(string clipboardText, bool usedDispatcher)
        {
            DiagnosticLogger.Info("ViewModel", "HandleClipboardText 시작 (UI Dispatcher 사용=" + usedDispatcher + ")");

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                DiagnosticLogger.Info("Decision", "IGNORE_NON_GBC_CLIPBOARD (empty)");
                return;
            }

            string text = clipboardText.TrimStart();

            // Prefix: 긴 것부터 (Monitor와 동일)
            if (text.StartsWith(TfsClipboardPayloadService.ClipboardPrefix, StringComparison.Ordinal))
            {
                HandleTfsClipboardText(text);
                return;
            }

            if (text.StartsWith(TfsClipboardAckService.AckPrefix, StringComparison.Ordinal)
                || text.StartsWith(TfsClipboardAckService.SyncRequestPrefix, StringComparison.Ordinal)
                || text.StartsWith(TfsClipboardAckService.SessionTokenAnnouncePrefix, StringComparison.Ordinal))
            {
                return;
            }

            if (!text.StartsWith(RdpStatusBiz.ClipboardPrefix, StringComparison.Ordinal)
                && !RdpStatusBiz.LooksLikeRcStatusLine(text))
            {
                DiagnosticLogger.Info("Decision", "IGNORE_NON_GBC_CLIPBOARD");
                return;
            }

            var parse = _session.TryHandleRdpClipboardText(text);
            switch (parse.Result)
            {
                case RdpStatusBiz.ClipboardHandleResult.Ignored:
                    DiagnosticLogger.Info("Decision", "IGNORE_NON_GBC_CLIPBOARD");
                    return;
                case RdpStatusBiz.ClipboardHandleResult.UnsupportedQuiet:
                    DiagnosticLogger.Warn("Decision", "IGNORE_NON_GBC_CLIPBOARD (unsupported type, UI unchanged)");
                    return;
                case RdpStatusBiz.ClipboardHandleResult.ParseFailed:
                    DiagnosticLogger.Error("Decision", "IGNORE_NON_GBC_CLIPBOARD (JSON parse failed, UI unchanged)");
                    return;
                case RdpStatusBiz.ClipboardHandleResult.Success:
                    TryAcceptPayload(text, parse, usedDispatcher);
                    return;
            }
        }

        /// <summary>GBCWORKHUB_SESSION_RESULT:: — remote SessionAgent's dev-session classification.</summary>
        public void HandleSessionChangeResultText(string clipboardText)
        {
            _session.HandleSessionChangeResultReceived(clipboardText);
        }

        public void HandleTfsClipboardText(string clipboardText)
        {
            if (TfsWorkLog == null)
                return;

            // 재접속 WaitingForPayload: 수신만 coordinator에 넘김 (저장/ACK/종료 순서는 RunFetch)
            if (_tfsSync != null && _tfsSync.IsWaitingForPayload)
            {
                var parseWait = new TfsClipboardPayloadService().TryHandleClipboardText(clipboardText);
                if (parseWait.Result == TfsClipboardPayloadService.HandleResult.Success
                    && parseWait.Payload != null)
                {
                    _tfsSync.TryAcceptTfsPayload(parseWait.Payload, clipboardText);
                }
                return;
            }

            if (_tfsSync != null && _tfsSync.IsSyncInFlight)
                return;

            TfsSyncRequest active = null;
            if (_tfsSync != null)
                active = _tfsSync.GetActiveRequestSnapshot();

            TfsPayloadIngestService.Ingest(
                clipboardText,
                TfsWorkLog,
                active,
                writeAck: true);
        }

        private void TryAcceptPayload(string fullClipboardText, RdpStatusBiz.ParseResult parse, bool usedDispatcher)
        {
            var payload = parse.Payload;
            if (payload == null)
                return;

            string computerKey = string.IsNullOrWhiteSpace(payload.ComputerName)
                ? "_"
                : payload.ComputerName.Trim();

            string payloadHash = ComputeSha256(fullClipboardText);

            string freshnessReason;
            var decision = _session.EvaluateClipboardFreshness(
                payloadHash,
                payload,
                computerKey,
                out freshnessReason);

            if (decision == RdpStatusBiz.FreshnessDecision.Duplicate)
            {
                DiagnosticLogger.Info("Decision", "IGNORE_DUPLICATE_PAYLOAD computerName=" + computerKey + " | " + freshnessReason);
                return;
            }

            if (decision == RdpStatusBiz.FreshnessDecision.Older)
            {
                DiagnosticLogger.Info("Decision", "IGNORE_OLDER_PAYLOAD computerName=" + computerKey + " | " + freshnessReason);
                return;
            }

            string status = _session.DetermineClipboardStatus(payload, parse.DeterminedStatus);

            bool tracking = _session.IsTrackingLocally;
            string trackedName = _session.TrackerRemoteComputerName;
            bool nameMatches = string.Equals(computerKey, trackedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(computerKey, _session.TrackerTargetIp, StringComparison.OrdinalIgnoreCase);
            bool isConfirm = RdpSessionTrackingService.IsConnectionConfirmPayload(payload) && nameMatches;

            if (tracking && !isConfirm && string.Equals(status, "사용 가능", StringComparison.Ordinal))
            {
                DiagnosticLogger.Info("Decision", "ACCEPT_NEW_GBC_PAYLOAD (events only, status kept while local tracking)"
                    + " ComputerName=" + computerKey
                    + " LatestEventId=" + (payload.LatestEventId.HasValue ? payload.LatestEventId.Value.ToString() : "null"));
                ApplyAcceptedPayload(payload, null, payloadHash, computerKey, usedDispatcher, keepCurrentStatus: true);
                return;
            }

            DiagnosticLogger.Info(
                "Decision",
                "ACCEPT_NEW_GBC_PAYLOAD"
                + " ComputerName=" + computerKey
                + " CollectedAt=" + (payload.CollectedAt ?? "null")
                + " LatestEventId=" + (payload.LatestEventId.HasValue ? payload.LatestEventId.Value.ToString() : "null")
                + " LatestRecordId=" + (payload.LatestRecordId.HasValue ? payload.LatestRecordId.Value.ToString() : "null")
                + " TriggerType=" + (payload.TriggerType ?? "null")
                + " IsVerifiedDisconnect=" + (payload.IsVerifiedDisconnect.HasValue ? payload.IsVerifiedDisconnect.Value.ToString() : "null")
                + " CurrentStatus=" + status
                + " | " + freshnessReason);

            ApplyAcceptedPayload(payload, status, payloadHash, computerKey, usedDispatcher, keepCurrentStatus: false);

            if (isConfirm && tracking)
            {
                _session.TryConfirmConnectionFromClipboard(payload, computerKey);
                if (_session.IsTfsSyncReconnectOrInFlight())
                {
                    DiagnosticLogger.Info("Decision", "RDP_CONFIRM_TRACKER_ONLY_DURING_TFS_SYNC");
                }
            }
        }

        private void ApplyAcceptedPayload(
            RdpStatusPayload payload,
            string status,
            string payloadHash,
            string computerKey,
            bool usedDispatcher,
            bool keepCurrentStatus)
        {
            RunOnUi(() => ApplyAcceptedPayloadCore(payload, status, payloadHash, computerKey, usedDispatcher, keepCurrentStatus));
        }

        private void ApplyAcceptedPayloadCore(
            RdpStatusPayload payload,
            string status,
            string payloadHash,
            string computerKey,
            bool usedDispatcher,
            bool keepCurrentStatus)
        {
            if (!keepCurrentStatus && status != null)
            {
                string statusBefore = _currentStatus;
                bool raised;
                SetPropertyAlways(ref _currentStatus, status, out raised, "CurrentStatus");
                DiagnosticLogger.Info("ViewModel", "CurrentStatus " + statusBefore + " -> " + _currentStatus
                    + " PropertyChanged=" + raised + " Dispatcher=" + usedDispatcher);
            }

            AssignAlways(ref _remoteComputerName, string.IsNullOrWhiteSpace(payload.ComputerName) ? "-" : payload.ComputerName, "RemoteComputerName");
            AssignAlways(ref _remoteWindowsUser, string.IsNullOrWhiteSpace(payload.WindowsUser) ? "-" : payload.WindowsUser, "RemoteWindowsUser");
            AssignAlways(ref _clientComputerName, _session.ResolveClientComputerName(payload.ClientName), "ClientComputerName");
            AssignAlways(ref _collectedAt, string.IsNullOrWhiteSpace(payload.CollectedAt) ? "-" : payload.CollectedAt, "CollectedAt");
            AssignAlways(ref _latestEventId, payload.LatestEventId.HasValue ? payload.LatestEventId.Value.ToString() : "-", "LatestEventId");
            AssignAlways(ref _sessionRaw, payload.SessionRaw, "SessionRaw");
            AssignAlways(ref _lastReceiveMessage, "원격 PC 상태 수신 완료", "LastReceiveMessage");

            if (!keepCurrentStatus && !string.IsNullOrEmpty(status))
            {
                if (string.Equals(status, "사용 중", StringComparison.Ordinal))
                    AssignAlways(ref _statusSource, "REMOTE_CLIPBOARD_EVENT", "StatusSource");
            }

            int added = MergeEvents(payload);
            RefreshEventListBinding();
            DiagnosticLogger.Info("ViewModel", "RecentEvents 갱신 count=" + RecentEvents.Count + " merged=" + added);

            _session.RememberLastProcessed(computerKey, payload, payloadHash);
        }

        private int MergeEvents(RdpStatusPayload payload)
        {
            var parsedEvents = _session.ParseClipboardEvents(payload);
            if (parsedEvents.Count == 0 && payload != null && payload.LatestEventId.HasValue)
            {
                long syntheticId = payload.LatestRecordId.HasValue && payload.LatestRecordId.Value > 0
                    ? payload.LatestRecordId.Value
                    : (payload.CollectedAt ?? string.Empty).GetHashCode() & 0x7FFFFFFF;

                parsedEvents.Add(new RdpStatusBiz.ParsedEventItem
                {
                    RecordId = syntheticId == 0 ? DateTime.Now.Ticks : syntheticId,
                    EventId = payload.LatestEventId.Value,
                    EventTime = payload.CollectedAt,
                    User = payload.WindowsUser,
                    SessionId = null,
                    SourceIp = null,
                    EventDescription = _session.GetEventDescription(payload.LatestEventId.Value)
                });
            }

            int merged = 0;
            foreach (var item in parsedEvents
                .OrderByDescending(x => x.RecordId)
                .ThenByDescending(x => x.EventTime ?? string.Empty))
            {
                var existingEvent = RecentEvents.FirstOrDefault(x => x.RecordId == item.RecordId);
                if (existingEvent != null)
                    RecentEvents.Remove(existingEvent);

                RecentEvents.Insert(0, new RdpEventViewModel
                {
                    RecordId = item.RecordId,
                    EventId = item.EventId,
                    EventTime = item.EventTime ?? string.Empty,
                    User = item.User ?? string.Empty,
                    SessionId = item.SessionId ?? string.Empty,
                    SourceIp = item.SourceIp ?? string.Empty,
                    EventDescription = item.EventDescription ?? string.Empty
                });
                merged++;
            }

            while (RecentEvents.Count > MaxRecentEvents)
                RecentEvents.RemoveAt(RecentEvents.Count - 1);

            return merged;
        }













        private async Task LoadRecentUsageLogsAsync(RemoteComputerItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.IpAddress) || !_session.IsShareConfigured)
                return;
            int generation = ++_usageLogLoadGeneration;
            try
            {
                var logs = await _session.GetRecentUsageLogsAsync(item.IpAddress, RecentUsageLogTake).ConfigureAwait(true);
                ApplyRecentUsageLogs(generation, logs);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("USAGE_LOG", ex.Message);
            }
        }

        private void ApplyRecentUsageLogs(int generation, IList<RemotePcUsageLogDto> logs)
        {
            if (generation != _usageLogLoadGeneration)
                return;

            var next = new ObservableCollection<RemotePcUsageLogItemViewModel>();
            if (logs != null)
            {
                foreach (var dto in logs)
                {
                    var vm = RemotePcUsageLogItemViewModel.FromDto(dto);
                    if (vm != null)
                        next.Add(vm);
                }
            }

            RecentUsageLogs = next;
            RaisePropertyChanged("RecentUsageLogs");
        }

        private static string TokenPrefix(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "-";
            return token.Length <= 8 ? token : token.Substring(0, 8);
        }











        private void AddLocalEndEvent(RdpSessionTrackingService.RdpSessionEndInfo info)
        {
            long localRecordId = -Math.Abs(info.EndedAt.Ticks % 1000000000L);
            if (localRecordId == 0)
                localRecordId = -DateTime.Now.Ticks;

            var oldLocal = RecentEvents.Where(x => x.RecordId < 0).ToList();
            foreach (var o in oldLocal)
                RecentEvents.Remove(o);

            RecentEvents.Insert(0, new RdpEventViewModel
            {
                RecordId = localRecordId,
                EventId = info.WasConnectionConfirmed ? 24 : 0,
                EventTime = info.EndedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                User = string.IsNullOrWhiteSpace(RemoteWindowsUser) || RemoteWindowsUser == "-"
                    ? Environment.UserName
                    : RemoteWindowsUser,
                SessionId = string.Empty,
                SourceIp = string.Empty,
                EventDescription = info.WasConnectionConfirmed ? "연결 해제(로컬)" : "접속 취소(로컬)"
            });

            while (RecentEvents.Count > MaxRecentEvents)
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }

        private void RefreshEventListBinding()
        {
            var snapshot = new ObservableCollection<RdpEventViewModel>(_recentEvents.ToList());
            RecentEvents = snapshot;
            RecentEventCount = snapshot.Count;
            RaisePropertyChanged("RecentEvents");
            RaisePropertyChanged("RecentEventCount");
        }



        private void RefreshTrackingUiFields()
        {
            // Tracking display fields are pushed by RemoteSessionController.
        }



        private Task PollSharedStatusAsync(bool forceUi)
        {
            return _session.PollSharedStatusAsync(forceUi);
        }

        public Task ApplySharedStatusToPcListAsync(IList<RemotePcDto> pcs)
        {
            return _session.ApplySharedStatusToPcListAsync(pcs);
        }

        private async Task LoadSelectedSiteGalleryAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedSiteCode))
                return;

            await Task.Yield();

            string localUser = RemotePcShareBiz.LocalUserAccount;
            string site = SelectedSiteCode;
            var sitePcs = _remotePcBiz.GetRemotePcListBySite(site) ?? new List<RemotePcDto>();

            RunOnUi(() =>
            {
                foreach (var dto in sitePcs)
                {
                    if (dto == null)
                        continue;

                    // Host 미설정(RC 가명 등): IP 없어도 PcName으로 카드 표시. 접속은 Host 설정 후.
                    bool hasIp = !string.IsNullOrWhiteSpace(dto.IpAddress);
                    bool hasName = !string.IsNullOrWhiteSpace(dto.PcName);
                    if (!hasIp && !hasName)
                        continue;

                    RemoteComputerItemViewModel existing = null;
                    if (hasIp)
                        existing = FindGalleryItemByIp(dto.IpAddress.Trim());
                    if (existing == null && hasName)
                    {
                        existing = RemoteComputers.FirstOrDefault(x =>
                            x != null
                            && string.Equals(x.PcName, dto.PcName.Trim(), StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.SiteCode ?? site, dto.HospitalCode ?? site, StringComparison.OrdinalIgnoreCase));
                    }

                    if (existing != null)
                    {
                        if (hasName)
                            existing.PcName = dto.PcName.Trim();
                        if (hasIp)
                            existing.IpAddress = dto.IpAddress.Trim();
                        existing.SiteCode = dto.HospitalCode ?? site;
                        existing.CurrentLocalUser = localUser;
                        continue;
                    }

                    RemoteComputers.Add(RemoteComputerItemViewModel.FromDto(dto, localUser));
                }

                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        private void MergeRemoteComputersFromDb(IList<RemotePcStatus> list, Func<string, bool> isLocalActiveFn)
        {
            if (list == null || string.IsNullOrWhiteSpace(SelectedSiteCode))
                return;

            string localUser = RemotePcShareBiz.LocalUserAccount;
            string site = SelectedSiteCode;

            RunOnUi(() =>
            {
                foreach (var status in list)
                {
                    if (status == null || string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress))
                        continue;

                    // DB SITE_CD 가 있으면 현재 사이트와 다를 때 무시
                    if (!string.IsNullOrWhiteSpace(status.SiteCode)
                        && !string.Equals(status.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string ip = status.RemoteAccessIpAddress.Trim();
                    var item = FindGalleryItemByIp(ip);
                    if (item == null)
                    {
                        // 사이트 시드에 없는 IP는 현재 사이트 화면에 강제 추가하지 않음
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(item.SiteCode)
                        && !string.Equals(item.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrWhiteSpace(item.SiteCode) && !string.IsNullOrWhiteSpace(status.SiteCode))
                        item.SiteCode = status.SiteCode.Trim().ToUpperInvariant();

                    bool isLocalActive = isLocalActiveFn != null && isLocalActiveFn(ip);

                    // 로컬이 접속 추적 중일 때 DB가 아직 AVAILABLE이면 카드 덮어쓰지 않음(레이스).
                    // 그 외(CONNECTING/IN_USE/AVAILABLE 확정)는 항상 DB 반영.
                    if (isLocalActive
                        && string.Equals(status.AccessStatusCode, RemotePcDbStatuses.Available, StringComparison.OrdinalIgnoreCase)
                        && (item.IsConnecting || item.IsInUse))
                    {
                        item.CurrentLocalUser = localUser;
                    }
                    else
                    {
                        item.ApplyFromDb(status, localUser);
                    }
                }

                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        private RemoteComputerItemViewModel FindGalleryItemByIp(string ip)
        {
            return FindGalleryItemByShareKey(ip);
        }

        /// <summary>점유 키로 갤러리 항목 찾기 (IP 또는 RC PC명).</summary>
        private RemoteComputerItemViewModel FindGalleryItemByShareKey(string shareKey)
        {
            if (string.IsNullOrWhiteSpace(shareKey) || RemoteComputers == null)
                return null;

            string key = shareKey.Trim();
            return RemoteComputers.FirstOrDefault(x =>
                x != null
                && (string.Equals(x.IpAddress, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.PcName, key, StringComparison.OrdinalIgnoreCase)));
        }

        private void UpdateGalleryItemFromStatus(RemotePcStatus status)
        {
            if (status == null || string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress))
                return;

            RunOnUi(() =>
            {
                var item = FindGalleryItemByIp(status.RemoteAccessIpAddress);
                if (item == null)
                    return;
                item.ApplyFromDb(status, RemotePcShareBiz.LocalUserAccount);
                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        private void UpdateGalleryItemLocalInUse(string ip, string userId, string clientPc, string sessionToken)
        {
            RunOnUi(() =>
            {
                var item = FindGalleryItemByIp(ip);
                if (item == null)
                    return;
                item.ApplyLocalInUse(userId, clientPc, sessionToken);
                SelectedRemoteComputer = item;
                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        private void UpdateGalleryItemLocalAvailable(string ip)
        {
            RunOnUi(() =>
            {
                var item = FindGalleryItemByShareKey(ip);
                if (item == null)
                {
                    DiagnosticLogger.Warn("GALLERY", "AVAILABLE 갱신 실패 — 항목 없음 key=" + (ip ?? "-"));
                    return;
                }
                item.ApplyLocalAvailable();
                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        private void UpdateGalleryItemStatusCode(string ip, string statusCode)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            RunOnUi(() =>
            {
                var item = FindGalleryItemByIp(ip);
                if (item == null)
                    return;
                item.StatusCode = statusCode;
                RecalculateStatusCounts();
                RefreshGalleryFilter();
            });
        }

        private void RecalculateStatusCounts()
        {
            int total = 0;
            int available = 0;
            int inUse = 0;
            int check = 0;

            foreach (var item in RemoteComputers)
            {
                if (item == null)
                    continue;
                total++;
                if (item.IsAvailable)
                    available++;
                else if (item.IsInUse || item.IsConnecting)
                    inUse++;
                else if (item.IsCheckRequired)
                    check++;
            }

            TotalCount = total;
            AvailableCount = available;
            ConnectingCount = 0;
            InUseCount = inUse;
            CheckRequiredCount = check;
            RaisePropertyChanged("TotalCountDisplay");
        }

        private bool FilterRemoteComputer(object obj)
        {
            var item = obj as RemoteComputerItemViewModel;
            if (item == null)
                return false;

            if (!string.Equals(SelectedStatusFilter, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(SelectedStatusFilter, "IN_USE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!item.IsInUse && !item.IsConnecting)
                        return false;
                }
                else if (!string.Equals(item.StatusCode, SelectedStatusFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            string q = (SearchText ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;

            return ContainsIgnoreCase(item.PcName, q)
                || ContainsIgnoreCase(item.IpAddress, q)
                || ContainsIgnoreCase(item.AccessUserId, q)
                || ContainsIgnoreCase(item.AccessPcName, q);
        }

        private static bool ContainsIgnoreCase(string source, string query)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
                return false;
            return source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshGalleryFilter()
        {
            if (_filteredRemoteComputers != null)
                _filteredRemoteComputers.Refresh();
        }

        private void SelectStatusFilter(string filter)
        {
            SelectedStatusFilter = string.IsNullOrWhiteSpace(filter) ? "ALL" : filter;
        }

        private void SetViewMode(string mode)
        {
            if (string.Equals(mode, "List", StringComparison.OrdinalIgnoreCase))
                SelectedViewMode = RemoteComputerViewMode.List;
            else
                SelectedViewMode = RemoteComputerViewMode.Card;
        }

        private void SelectRemoteComputer(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return;
            SelectedRemoteComputer = item;
        }

        private void SyncDetailPanelFromSelected()
        {
            var item = SelectedRemoteComputer;
            if (item == null)
                return;

            AssignAlways(ref _remoteComputerName, item.DisplayTitle, "RemoteComputerName");
            AssignAlways(ref _currentStatus, item.StatusDisplayName, "CurrentStatus");
            if (!string.IsNullOrWhiteSpace(item.AccessUserId))
                AssignAlways(ref _workHubUserAccount, item.AccessUserId, "WorkHubUserAccount");
            if (!string.IsNullOrWhiteSpace(item.AccessPcName))
                AssignAlways(ref _workHubClientPc, item.AccessPcName, "WorkHubClientPc");
            ConnectionRequestedAt = FormatTs(item.AccessStartAt);
            UpdatedDateTime = FormatTs(item.UpdatedAt);
        }

        private async Task ExecutePrimaryActionAsync(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return;

            SelectedRemoteComputer = item;

            if (item.IsCheckRequired)
            {
                await CheckRemoteComputerAsync(item).ConfigureAwait(true);
                return;
            }

            await ConnectRemoteComputerAsync(item).ConfigureAwait(true);
        }

        private async Task ConnectRemoteComputerAsync(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return;

            SelectedRemoteComputer = item;

            if (item.IsOwnedByOtherUser)
            {
                await ShowInfoPopupAsync("원격 접속",
                    "현재 다른 사용자가 이 원격 PC를 사용 중입니다.\n사용자: "
                        + RemoteComputerItemViewModel.FormatUserId(item.AccessUserId)
                        + "\n접속 PC: " + (item.AccessPcName ?? "-")).ConfigureAwait(true);
                return;
            }

            if (item.IsConnecting && item.IsOwnedByCurrentUser)
                return;

            if (item.IsInUse && item.IsOwnedByCurrentUser)
            {
                bool isCmc = string.Equals(item.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);
                bool stillAlive = _session.IsLocalSessionAliveFor(item)
                    || (isCmc && _session.IsCmcRemoteStillAlive(item.IpAddress, item.PcName));

                if (stillAlive)
                {
                    if (string.IsNullOrWhiteSpace(_session.ActiveSessionToken)
                        && !string.IsNullOrWhiteSpace(item.SessionToken))
                    {
                        _session.RehydrateActiveSessionFromDb(new RemotePcStatus
                        {
                            RemoteAccessIpAddress = item.IpAddress,
                            RemotePcName = item.PcName,
                            SiteCode = item.SiteCode,
                            SessionToken = item.SessionToken,
                            AccessStatusCode = RemotePcDbStatuses.InUse
                        });
                    }

                    await ShowInfoPopupAsync("원격 접속",
                        isCmc
                            ? "이미 이 원격 PC를 사용 중입니다.\n브라우저의 PMP 웹 RDP 창을 확인하세요."
                            : "이미 이 원격 PC를 사용 중입니다.\n원격 데스크톱(mstsc) 창을 확인하세요.").ConfigureAwait(true);
                    return;
                }

                bool recovered = await TryReleaseStaleOwnedPcAsync(item).ConfigureAwait(true);
                if (!recovered)
                    return;
            }

            if (string.Equals(item.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase))
            {
                await _session.ConnectCmcWithPmpAsync(item).ConfigureAwait(true);
                return;
            }

            if (string.Equals(item.SiteCode, "RC", StringComparison.OrdinalIgnoreCase))
            {
                await _session.ConnectRcWithPublishedRdpAsync(item).ConfigureAwait(true);
                return;
            }

            string message = await StartRemoteSessionAsync(item.ToDto()).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(message))
                await ShowInfoPopupAsync("원격 접속", message).ConfigureAwait(true);
        }











        private Task<bool> TryReleaseStaleOwnedPcAsync(RemoteComputerItemViewModel item)
        {
            return _session.TryReleaseStaleOwnedPcAsync(item);
        }



        private void RefreshRcVpnBadges()
        {
            bool vpn = Services.FortiVpnStatus.IsConnected();
            RunOnUi(() =>
            {
                if (RemoteComputers == null)
                    return;
                foreach (var pc in RemoteComputers)
                {
                    if (pc == null)
                        continue;
                    if (string.Equals(pc.SiteCode, "RC", StringComparison.OrdinalIgnoreCase))
                        pc.IsVpnConnected = vpn;
                    else
                        pc.IsVpnConnected = false;
                }
            });
        }

        private async Task CheckRemoteComputerAsync(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return;

            SelectedRemoteComputer = item;

            if (!_session.IsShareConfigured || string.IsNullOrWhiteSpace(item.IpAddress))
            {
                await ShowInfoPopupAsync("원격 접속", "DB 미연결 또는 IP 없음").ConfigureAwait(true);
                return;
            }

            try
            {
                var status = await _session.GetStatusByIpAsync(item.IpAddress.Trim()).ConfigureAwait(true);
                if (status != null)
                {
                    ApplySharedStatusToUi(status, overwriteLocalStatus: !_session.IsTrackingLocally, _session.IsTrackingLocally);
                    UpdateGalleryItemFromStatus(status);
                }
                else
                {
                    await ShowInfoPopupAsync("원격 접속", "공유 DB에 해당 PC 상태가 없습니다.").ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                await ShowInfoPopupAsync("원격 접속", "상태 조회 실패: " + ex.Message).ConfigureAwait(true);
            }
        }

        private async Task ShowInfoPopupAsync(string title, string message, PopupIconKind icon = PopupIconKind.Info)
        {
            if (_popup == null)
            {
                DiagnosticLogger.Warn("Popup", title + " | " + message);
                return;
            }

            await _popup.ShowResultAsync(new PopupRequest
            {
                Title = title,
                Message = message,
                Icon = icon,
                Kind = PopupKind.Result,
                Buttons = new List<PopupButtonDefinition>
                {
                    new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true)
                }
            }).ConfigureAwait(true);
        }

        private void ApplySharedStatusToUi(RemotePcStatus status, bool overwriteLocalStatus, bool isTracking)
        {
            if (status == null)
                return;

            if (overwriteLocalStatus || !isTracking)
            {
                if (!string.IsNullOrEmpty(status.DisplayStatus))
                    CurrentStatus = status.DisplayStatus;
            }
            else if (!isTracking && !string.IsNullOrEmpty(status.DisplayStatus))
            {
                CurrentStatus = status.DisplayStatus;
            }

            if (!string.IsNullOrWhiteSpace(status.AccessUserId))
                WorkHubUserAccount = status.AccessUserId;
            if (!string.IsNullOrWhiteSpace(status.AccessPcName))
                WorkHubClientPc = status.AccessPcName;
            if (!string.IsNullOrWhiteSpace(status.RemotePcName))
                RemoteComputerName = status.RemotePcName;
            if (!string.IsNullOrWhiteSpace(status.SessionToken))
                SessionTokenDisplay = status.SessionToken;
        }

        public string GetAppCloseWarning()
        {
            return _session.GetAppCloseWarning();
        }

        public Task HandleAppClosingAsync()
        {
            return _session.HandleAppClosingAsync();
        }

        private void RunOnUi(Action action)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher == null)
            {
                action();
                return;
            }

            if (app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }

        private void AssignAlways(ref string field, string value, string propertyName)
        {
            bool raised;
            SetPropertyAlways(ref field, value, out raised, propertyName);
        }

        private string ResolveWorkHubOwnerKey()
        {
            return WorkHubUserProfile.LocalIp;
        }

        public void ApplyWorkHubUserToSessionPublic()
        {
            ApplyWorkHubUserToSession();
        }

        private void ApplyWorkHubUserToSession()
        {
            string localIp = ResolveWorkHubOwnerKey();
            var ctx = _tfsWorkLog != null ? _tfsWorkLog.CurrentSessionContext : null;
            if (ctx == null)
            {
                ctx = new GBCWorkHub.DTO.WorkLog.WorkSessionContext();
                if (_tfsWorkLog != null)
                    _tfsWorkLog.UpdateSessionContext(ctx);
            }

            ctx.ClientLocalIp = localIp;
            if (string.IsNullOrWhiteSpace(ctx.CurrentUserId))
                ctx.CurrentUserId = Environment.UserDomainName + "\\" + Environment.UserName;
            if (string.IsNullOrWhiteSpace(ctx.CurrentUserName))
                ctx.CurrentUserName = Environment.UserName;
        }

        private static string FormatTs(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-";
        }

        private static int ReadIntSetting(string key, int defaultValue)
        {
            try
            {
                string raw = ConfigurationManager.AppSettings[key];
                int n;
                if (int.TryParse(raw, out n))
                    return n;
            }
            catch
            {
            }
            return defaultValue;
        }

        private static string ComputeSha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_session != null)
                _session.Dispose();
        }

        private sealed class SessionUiBridge : RemoteSessionUi
        {
            private readonly RemoteWorkspaceViewModel _vm;
            public SessionUiBridge(RemoteWorkspaceViewModel vm) { _vm = vm; }

            public override void RunOnUi(Action action) { _vm.RunOnUi(action); }
            public override bool IsGalleryVisible { get { return _vm.IsGalleryVisible; } }
            public override bool IsCentralShareEnabled
            {
                get { return _vm.IsCentralShareEnabled; }
                set { _vm.IsCentralShareEnabled = value; }
            }
            public override bool IsCentralDbConnected
            {
                get { return _vm.IsCentralDbConnected; }
                set { _vm.IsCentralDbConnected = value; }
            }
            public override string CentralDbStatus
            {
                get { return _vm.CentralDbStatus; }
                set { _vm.CentralDbStatus = value; }
            }
            public override string SelectedSiteCode { get { return _vm.SelectedSiteCode; } }
            public override RemoteComputerItemViewModel SelectedRemoteComputer { get { return _vm.SelectedRemoteComputer; } }

            public override void UpdateGalleryLocalInUse(string ip, string userId, string clientPc, string sessionToken)
            { _vm.UpdateGalleryItemLocalInUse(ip, userId, clientPc, sessionToken); }
            public override void UpdateGalleryLocalAvailable(string ip) { _vm.UpdateGalleryItemLocalAvailable(ip); }
            public override void UpdateGalleryFromStatus(RemotePcStatus status) { _vm.UpdateGalleryItemFromStatus(status); }
            public override void UpdateGalleryStatusCode(string ip, string statusCode) { _vm.UpdateGalleryItemStatusCode(ip, statusCode); }
            public override void RecalculateStatusCounts() { _vm.RecalculateStatusCounts(); }
            public override void RefreshGalleryFilter() { _vm.RefreshGalleryFilter(); }
            public override void MergeRemoteComputersFromDb(IList<RemotePcStatus> list, Func<string, bool> isLocalActive)
            { _vm.MergeRemoteComputersFromDb(list, isLocalActive); }
            public override RemoteComputerItemViewModel FindGalleryItemByShareKey(string shareKey)
            { return _vm.FindGalleryItemByShareKey(shareKey); }
            public override RemoteComputerItemViewModel FindGalleryItemByIp(string ip)
            { return _vm.FindGalleryItemByIp(ip); }

            public override void ApplySharedStatusToUi(RemotePcStatus status, bool overwriteLocalStatus, bool isTracking)
            { _vm.ApplySharedStatusToUi(status, overwriteLocalStatus, isTracking); }
            public override void AssignDetailField(string propertyName, string value)
            {
                switch (propertyName)
                {
                    case "RemoteComputerName": _vm.RemoteComputerName = value; break;
                    case "CurrentStatus": _vm.CurrentStatus = value; break;
                    case "LastReceiveMessage": _vm.LastReceiveMessage = value; break;
                    case "StatusSource": _vm.StatusSource = value; break;
                    case "SessionRaw": _vm.SessionRaw = value; break;
                    case "WorkHubUserAccount": _vm.WorkHubUserAccount = value; break;
                    case "WorkHubClientPc": _vm.WorkHubClientPc = value; break;
                    case "SessionTokenDisplay": _vm.SessionTokenDisplay = value; break;
                    case "LocalAccessIp": _vm.LocalAccessIp = value; break;
                    case "ConnectionConfirmedAt": _vm.ConnectionConfirmedAt = value; break;
                    case "ConnectionEndedAt": _vm.ConnectionEndedAt = value; break;
                    case "ConnectionRequestedAt": _vm.ConnectionRequestedAt = value; break;
                }
            }
            public override void SetRdpSessionConfirmed(bool value) { _vm.IsRdpSessionConfirmed = value; }
            public override void SetConnectionConfirmedAt(string value) { _vm.ConnectionConfirmedAt = value; }
            public override void SetConnectionEndedAt(string value) { _vm.ConnectionEndedAt = value; }
            public override void SetConnectionRequestedAt(string value) { _vm.ConnectionRequestedAt = value; }
            public override void SetSessionTokenDisplay(string value) { _vm.SessionTokenDisplay = value; }
            public override void SetLocalAccessIp(string value) { _vm.LocalAccessIp = value; }
            public override void SetCurrentStatus(string value) { _vm.CurrentStatus = value; }
            public override void SetStatusSource(string value) { _vm.StatusSource = value; }
            public override void SetLastReceiveMessage(string value) { _vm.LastReceiveMessage = value; }
            public override void ClearSessionRaw() { _vm.SessionRaw = string.Empty; }
            public override void SetWorkHubUserAccount(string value) { _vm.WorkHubUserAccount = value; }
            public override void SetWorkHubClientPc(string value) { _vm.WorkHubClientPc = value; }
            public override void SetRemoteComputerName(string value) { _vm.RemoteComputerName = value; }
            public override void SetTrackedProcessIds(string value) { _vm.TrackedProcessIds = value; }
            public override void SetRdpLaunchTime(string value) { _vm.RdpLaunchTime = value; }
            public override void SetLastRefreshedAt(DateTime value) { _vm.LastRefreshedAt = value; }

            public override int MergeEvents(RdpStatusPayload payload) { return _vm.MergeEvents(payload); }
            public override void RefreshEventListBinding() { _vm.RefreshEventListBinding(); }
            public override void AddLocalEndEvent(RdpSessionTrackingService.RdpSessionEndInfo info) { _vm.AddLocalEndEvent(info); }

            public override Task LoadRecentUsageLogsAsync(RemoteComputerItemViewModel item)
            { return _vm.LoadRecentUsageLogsAsync(item); }
            public override void RefreshRcVpnBadges() { _vm.RefreshRcVpnBadges(); }
            public override Func<Task> RefreshPcListRequested { get { return _vm.RefreshPcListRequested; } }

            public override IPopupService Popup { get { return _vm._popup; } }
            public override TfsSyncCoordinator TfsSync { get { return _vm._tfsSync; } }
            public override TfsWorkLogViewModel TfsWorkLog { get { return _vm.TfsWorkLog; } }
            public override void RefreshPendingTfsBadge()
            {
                if (_vm._refreshPendingTfsBadge != null) _vm._refreshPendingTfsBadge();
            }
            public override Task PopupShowInfoAsync(string title, string message)
            { return _vm.ShowInfoPopupAsync(title, message); }
        }
    }
}
