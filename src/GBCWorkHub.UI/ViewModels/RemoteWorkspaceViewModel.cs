using System;
using System.Collections;
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
        private readonly DirectoryBiz _directoryBiz = new DirectoryBiz();
        private readonly RemoteSessionController _session = new RemoteSessionController();
        private TfsWorkLogViewModel _tfsWorkLog;
        private WorkLog.WorkLogListViewModel _workLogList;
        private Action _refreshPendingTfsBadge;
        private Action _notifyIdentityChanged;
        private Action _onAuthSucceeded;
        private readonly Action _onSiteContextChanged;

        private ObservableCollection<RemoteComputerItemViewModel> _remoteComputers;
        private ObservableCollection<RemotePcTeamGroupViewModel> _galleryGroups;
        private ObservableCollection<SiteShortcutItemViewModel> _siteShortcuts;
        private ObservableCollection<PcAccessSectionViewModel> _selectedPcAccessSections;
        private string _selectedPcComment = string.Empty;
        private string _selectedPcCommentBaseline = string.Empty;
        private bool _isPcAccessEditing;
        private bool _isPcAccessSaving;
        private string _adminEditPcIp = string.Empty;
        private string _adminEditTeamName = string.Empty;
        private readonly Dictionary<string, List<RemotePcDto>> _sitePcCache =
            new Dictionary<string, List<RemotePcDto>>(StringComparer.OrdinalIgnoreCase);
        private IList<RemotePcStatus> _lastOccupancy;
        private ICollectionView _filteredRemoteComputers;
        private RemoteComputerItemViewModel _selectedRemoteComputer;
        private string _searchText = string.Empty;
        private string _selectedStatusFilter = "ALL";
        private string _selectedGroupFilter = "전체";
        private ObservableCollection<string> _groupFilterOptions;
        private bool _didAutoApplyGroupFilter;
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
        private const int RecentUsageLogTake = 20;
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
            _galleryGroups = new ObservableCollection<RemotePcTeamGroupViewModel>();
            _groupFilterOptions = new ObservableCollection<string> { "전체" };
            _sites = new ObservableCollection<RemoteSiteDto>(_remotePcBiz.GetSiteList() ?? new List<RemoteSiteDto>());
            _siteShortcuts = new ObservableCollection<SiteShortcutItemViewModel>();
            foreach (var site in _sites)
            {
                if (site == null || string.IsNullOrWhiteSpace(site.SiteCode))
                    continue;
                _siteShortcuts.Add(new SiteShortcutItemViewModel(site.SiteCode.Trim().ToUpperInvariant(), site.IsEnabled));
            }
            _filteredRemoteComputers = CollectionViewSource.GetDefaultView(_remoteComputers);
            _filteredRemoteComputers.Filter = FilterRemoteComputer;
            var view = _filteredRemoteComputers as ListCollectionView;
            if (view != null)
                view.CustomSort = new RemotePcGalleryComparer();

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
            ActivateRemoteComputerCommand = new RelayCommand<RemoteComputerItemViewModel>(item => { var _ = ActivateRemoteComputerAsync(item); });
            OpenSiteCommand = new RelayCommand<string>(code => { var _ = OpenSiteAsync(code); });
            BackToSitesCommand = new RelayCommand(BackToSites);
            CopySelectedPcDomainCommand = new RelayCommand(CopySelectedPcDomain, () => HasSelectedPcDomain);
            CopyPcCredentialCommand = new RelayCommand<PcCredentialLineViewModel>(CopyPcCredentialLine);
            BeginEditPcAccessCommand = new RelayCommand(BeginEditPcAccess, () => HasSelectedRemoteComputer && !IsPcAccessEditing && !IsPcAccessSaving);
            SavePcAccessCommand = new RelayCommand(() => { var _ = SavePcAccessAsync(); }, () => IsPcAccessEditing && !IsPcAccessSaving);
            CancelEditPcAccessCommand = new RelayCommand(CancelEditPcAccess, () => IsPcAccessEditing && !IsPcAccessSaving);
            AddRemotePcCommand = new RelayCommand(() => { var _ = AddRemotePcAsync(); }, () => IsAdmin && IsGalleryVisible && !string.IsNullOrWhiteSpace(SelectedSiteCode));
            DeleteRemotePcCommand = new RelayCommand(() => { var _ = DeleteRemotePcAsync(); }, () => ShowAdminDeletePcButton);

            _selectedPcAccessSections = new ObservableCollection<PcAccessSectionViewModel>();
            ApplyWorkHubUserToSession();
        }

        /// <summary>Shell에서 popup / TFS / WorkLog 참조를 주입한다.</summary>
        public void AttachShellServices(
            IPopupService popup,
            TfsWorkLogViewModel tfsWorkLog,
            WorkLog.WorkLogListViewModel workLogList,
            TfsSyncCoordinator tfsSync,
            Action<string> requestSelectMainTab,
            Action refreshPendingTfsBadge,
            Action notifyIdentityChanged = null,
            Action onAuthSucceeded = null)
        {
            _popup = popup;
            _tfsWorkLog = tfsWorkLog;
            _workLogList = workLogList;
            _tfsSync = tfsSync;
            // requestSelectMainTab: Shell(MainViewModel) owns tab navigation; unused here after phase 1 split.
            _refreshPendingTfsBadge = refreshPendingTfsBadge;
            _notifyIdentityChanged = notifyIdentityChanged;
            _onAuthSucceeded = onAuthSucceeded;
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

        public bool HasSelectedRemoteComputer
        {
            get { return _selectedRemoteComputer != null; }
        }

        public bool HasRecentUsageLogs
        {
            get { return _recentUsageLogs != null && _recentUsageLogs.Count > 0; }
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

        public ObservableCollection<RemotePcTeamGroupViewModel> GalleryGroups
        {
            get { return _galleryGroups; }
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
                RaisePropertyChanged("HasSelectedRemoteComputer");
                RaisePropertyChanged("SelectedPcDomain");
                RaisePropertyChanged("HasSelectedPcDomain");
                RaisePropertyChanged("HasSelectedPcAccessSections");
                RaisePropertyChanged("ShowPcAccessCommentColumn");
                RaisePropertyChanged("PcAccessSectionColumns");
                var copy = CopySelectedPcDomainCommand as RelayCommand;
                if (copy != null)
                    copy.RaiseCanExecuteChanged();
                RaisePcAccessEditCommands();
                RebuildSelectedPcAccessPanel();
                SyncDetailPanelFromSelected();
                RaisePropertyChanged("ShowAdminDeletePcButton");
                var add = AddRemotePcCommand as RelayCommand;
                if (add != null)
                    add.RaiseCanExecuteChanged();
                var del = DeleteRemotePcCommand as RelayCommand;
                if (del != null)
                    del.RaiseCanExecuteChanged();
                if (_selectedRemoteComputer == null)
                    ApplyRecentUsageLogs(++_usageLogLoadGeneration, null);
                else
                {
                    var _ = LoadRecentUsageLogsAsync(_selectedRemoteComputer);
                }
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

        /// <summary>PC의 소속 그룹(진료지원/진료간호/원무 등, PcMap.TeamNm 기반) 필터. "전체"면 미적용.</summary>
        public string SelectedGroupFilter
        {
            get { return _selectedGroupFilter; }
            set
            {
                if (SetProperty(ref _selectedGroupFilter, string.IsNullOrWhiteSpace(value) ? "전체" : value.Trim()))
                    RefreshGalleryFilter();
            }
        }

        public ObservableCollection<string> GroupFilterOptions
        {
            get { return _groupFilterOptions; }
        }

        public bool HasGroupFilterOptions
        {
            get { return _groupFilterOptions != null && _groupFilterOptions.Count > 1; }
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
        public ICommand ActivateRemoteComputerCommand { get; private set; }
        public ICommand OpenSiteCommand { get; private set; }
        public ICommand BackToSitesCommand { get; private set; }
        public ICommand CopySelectedPcDomainCommand { get; private set; }
        public ICommand CopyPcCredentialCommand { get; private set; }
        public ICommand BeginEditPcAccessCommand { get; private set; }
        public ICommand SavePcAccessCommand { get; private set; }
        public ICommand CancelEditPcAccessCommand { get; private set; }
        public ICommand AddRemotePcCommand { get; private set; }
        public ICommand DeleteRemotePcCommand { get; private set; }

        public bool IsAdmin
        {
            get { return OccupancyNameStore.IsAdmin; }
        }

        public bool ShowAdminDeletePcButton
        {
            get
            {
                return IsAdmin
                    && HasSelectedRemoteComputer
                    && !IsPcAccessEditing
                    && !IsPcAccessSaving;
            }
        }

        public string AdminEditPcIp
        {
            get { return _adminEditPcIp; }
            set { SetProperty(ref _adminEditPcIp, value ?? string.Empty); }
        }

        public string AdminEditTeamName
        {
            get { return _adminEditTeamName; }
            set { SetProperty(ref _adminEditTeamName, value ?? string.Empty); }
        }

        /// <summary>선택 PC의 원격 Windows 로그인(엑셀 ID).</summary>
        public string SelectedPcDomain
        {
            get
            {
                return SelectedRemoteComputer != null ? SelectedRemoteComputer.PcDomain : null;
            }
        }

        public bool HasSelectedPcDomain
        {
            get { return !string.IsNullOrWhiteSpace(SelectedPcDomain); }
        }

        public ObservableCollection<PcAccessSectionViewModel> SelectedPcAccessSections
        {
            get { return _selectedPcAccessSections; }
        }

        /// <summary>COMMENT 칸. 내용 있거나 수정 모드일 때만.</summary>
        public bool ShowPcAccessCommentColumn
        {
            get
            {
                if (IsPcAccessEditing)
                    return true;
                return !string.IsNullOrWhiteSpace(_selectedPcComment);
            }
        }

        /// <summary>Domain/VPN 가로 배치: 코멘트 없고 섹션 2개일 때 2열.</summary>
        public int PcAccessSectionColumns
        {
            get
            {
                if (ShowPcAccessCommentColumn)
                    return 1;
                int n = _selectedPcAccessSections != null ? _selectedPcAccessSections.Count : 0;
                return n >= 2 ? 2 : 1;
            }
        }

        public bool HasSelectedPcAccessSections
        {
            get { return _selectedPcAccessSections != null && _selectedPcAccessSections.Count > 0; }
        }

        public bool IsPcAccessEditing
        {
            get { return _isPcAccessEditing; }
            private set
            {
                if (SetProperty(ref _isPcAccessEditing, value))
                {
                    RaisePropertyChanged("IsPcAccessReadOnly");
                    RaisePropertyChanged("ShowPcAccessEditButton");
                    RaisePropertyChanged("ShowPcAccessSaveButton");
                    RaisePropertyChanged("ShowPcAccessCommentColumn");
                    RaisePropertyChanged("PcAccessSectionColumns");
                    ApplyCredentialEditFlags();
                    RaisePcAccessEditCommands();
                }
            }
        }

        public bool IsPcAccessSaving
        {
            get { return _isPcAccessSaving; }
            private set
            {
                if (SetProperty(ref _isPcAccessSaving, value))
                    RaisePcAccessEditCommands();
            }
        }

        public bool IsPcAccessReadOnly
        {
            get { return !IsPcAccessEditing; }
        }

        public bool ShowPcAccessEditButton
        {
            get { return HasSelectedRemoteComputer && !IsPcAccessEditing; }
        }

        public bool ShowPcAccessSaveButton
        {
            get { return IsPcAccessEditing; }
        }

        public string SelectedPcComment
        {
            get { return _selectedPcComment; }
            set
            {
                string next = value ?? string.Empty;
                if (SetProperty(ref _selectedPcComment, next))
                {
                    RaisePropertyChanged("ShowPcAccessCommentColumn");
                    RaisePropertyChanged("PcAccessSectionColumns");
                }
            }
        }

        private void CopySelectedPcDomain()
        {
            if (!HasSelectedPcDomain)
                return;
            CopyTextToClipboard(SelectedPcDomain);
        }

        private void CopyPcCredentialLine(PcCredentialLineViewModel line)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.Value))
                return;
            if (!CopyTextToClipboard(line.Value))
                return;
            line.MarkCopied();
        }

        private static bool CopyTextToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            try
            {
                Clipboard.SetText(text.Trim());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RaisePcAccessEditCommands()
        {
            var begin = BeginEditPcAccessCommand as RelayCommand;
            if (begin != null)
                begin.RaiseCanExecuteChanged();
            var save = SavePcAccessCommand as RelayCommand;
            if (save != null)
                save.RaiseCanExecuteChanged();
            var cancel = CancelEditPcAccessCommand as RelayCommand;
            if (cancel != null)
                cancel.RaiseCanExecuteChanged();
            RaisePropertyChanged("ShowPcAccessEditButton");
            RaisePropertyChanged("ShowPcAccessSaveButton");
            RaisePropertyChanged("ShowAdminDeletePcButton");
            var del = DeleteRemotePcCommand as RelayCommand;
            if (del != null)
                del.RaiseCanExecuteChanged();
        }

        private void ApplyCredentialEditFlags()
        {
            if (_selectedPcAccessSections == null)
                return;
            for (int i = 0; i < _selectedPcAccessSections.Count; i++)
            {
                var section = _selectedPcAccessSections[i];
                if (section != null)
                    section.SetEditing(IsPcAccessEditing);
            }
        }

        private void BeginEditPcAccess()
        {
            if (!HasSelectedRemoteComputer || IsPcAccessEditing)
                return;
            EnsureEditableAccessSections();
            IsPcAccessEditing = true;
        }

        /// <summary>편집 시 Domain + (CMC Auth / RC·MNGHA VPN) 빈 칸이 항상 보이게.</summary>
        private void EnsureEditableAccessSections()
        {
            var item = SelectedRemoteComputer;
            if (item == null || _selectedPcAccessSections == null)
                return;

            EnsureSectionHasEditableFields(FindAccessSection("Domain"), "Domain");

            bool cmc = string.Equals(item.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);
            bool vpnSite = string.Equals(item.SiteCode, "RC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.SiteCode, "MNGHA", StringComparison.OrdinalIgnoreCase);

            if (cmc)
            {
                // Legacy CMC VPN rows already mapped to Auth in RebuildSelectedPcAccessPanel.
                if (FindAccessSection("Auth") == null && FindAccessSection("VPN") != null)
                {
                    // leave VPN as-is if somehow still present; prefer Auth slot
                }
                if (FindAccessSection("Auth") == null)
                    _selectedPcAccessSections.Add(CreateAccessSection("Auth", new List<string> { string.Empty }, new List<string> { string.Empty }));
                else
                    EnsureSectionHasEditableFields(FindAccessSection("Auth"), "Auth");
            }
            else if (vpnSite)
            {
                if (FindAccessSection("VPN") == null)
                    _selectedPcAccessSections.Add(CreateAccessSection("VPN", new List<string> { string.Empty }, new List<string> { string.Empty }));
                else
                    EnsureSectionHasEditableFields(FindAccessSection("VPN"), "VPN");
            }

            if (FindAccessSection("Domain") == null)
                _selectedPcAccessSections.Insert(0, CreateAccessSection("Domain", new List<string> { string.Empty }, new List<string> { string.Empty }));

            RaisePropertyChanged("HasSelectedPcAccessSections");
            RaisePropertyChanged("PcAccessSectionColumns");
        }

        private PcAccessSectionViewModel FindAccessSection(string title)
        {
            if (_selectedPcAccessSections == null)
                return null;
            for (int i = 0; i < _selectedPcAccessSections.Count; i++)
            {
                var s = _selectedPcAccessSections[i];
                if (s != null && string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        private void EnsureSectionHasEditableFields(PcAccessSectionViewModel section, string title)
        {
            if (section == null)
                return;
            EnsureFieldHasLine(section, "ID", "ID");
            EnsureFieldHasLine(section, "PW", "PW");
        }

        private void EnsureFieldHasLine(PcAccessSectionViewModel section, string kind, string label)
        {
            if (section == null || section.Fields == null)
                return;
            PcCredentialFieldViewModel field = null;
            for (int i = 0; i < section.Fields.Count; i++)
            {
                if (section.Fields[i] != null
                    && string.Equals(section.Fields[i].Kind, kind, StringComparison.OrdinalIgnoreCase))
                {
                    field = section.Fields[i];
                    break;
                }
            }
            if (field == null)
            {
                field = new PcCredentialFieldViewModel(kind, label);
                section.Fields.Add(field);
            }
            if (field.Lines == null)
                return;
            if (field.Lines.Count == 0)
                field.Lines.Add(new PcCredentialLineViewModel(string.Empty, CopyPcCredentialCommand));
        }

        private void CancelEditPcAccess()
        {
            if (!IsPcAccessEditing)
                return;
            IsPcAccessEditing = false;
            RebuildSelectedPcAccessPanel();
        }

        private async Task SavePcAccessAsync()
        {
            var item = SelectedRemoteComputer;
            if (item == null || !IsPcAccessEditing || IsPcAccessSaving)
                return;
            if (string.IsNullOrWhiteSpace(item.SiteCode) || string.IsNullOrWhiteSpace(item.PcName))
                return;

            string note = BuildPcNoteFromSections(_selectedPcAccessSections);
            string comment = SelectedPcComment ?? string.Empty;
            string domain = BuildPcDomainFromSections(_selectedPcAccessSections);
            string site = item.SiteCode;
            string pc = item.PcName;

            IsPcAccessSaving = true;
            try
            {
                if (IsAdmin)
                {
                    var map = new PcMapDto
                    {
                        SiteCode = site,
                        PcName = pc,
                        PcIp = AdminEditPcIp,
                        ShareKey = string.IsNullOrWhiteSpace(item.IpAddress) ? pc : item.IpAddress,
                        TeamName = AdminEditTeamName,
                        PcDomain = domain,
                        PcNote = note,
                        PcComment = comment
                    };
                    // AURORA는 IP를 점유키로
                    if (string.Equals(site, "AURORA", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(AdminEditPcIp))
                        map.ShareKey = AdminEditPcIp.Trim();

                    string err = await Task.Run(() => _directoryBiz.UpsertPcMapForAdmin(map, pc)).ConfigureAwait(true);
                    if (!string.IsNullOrWhiteSpace(err))
                    {
                        await ShowInfoPopupAsync("PC 저장", err, PopupIconKind.Warning).ConfigureAwait(true);
                        return;
                    }
                }
                else
                {
                    int n = await _directoryBiz.UpdatePcAccessAsync(site, pc, note, comment, domain).ConfigureAwait(true);
                    if (n < 0)
                        return;
                }

                if (SelectedRemoteComputer != null
                    && string.Equals(SelectedRemoteComputer.SiteCode, site, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(SelectedRemoteComputer.PcName, pc, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedRemoteComputer.PcNote = string.IsNullOrWhiteSpace(note) ? null : note;
                    SelectedRemoteComputer.PcComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
                    SelectedRemoteComputer.PcDomain = string.IsNullOrWhiteSpace(domain) ? null : domain;
                    if (IsAdmin)
                    {
                        if (!string.IsNullOrWhiteSpace(AdminEditPcIp))
                            SelectedRemoteComputer.HostAddress = AdminEditPcIp.Trim();
                        SelectedRemoteComputer.GroupName = string.IsNullOrWhiteSpace(AdminEditTeamName)
                            ? null
                            : AdminEditTeamName.Trim();
                    }
                    _selectedPcCommentBaseline = comment;
                    IsPcAccessEditing = false;
                    RebuildSelectedPcAccessPanel();
                    if (IsAdmin)
                        await LoadSelectedSiteGalleryAsync().ConfigureAwait(true);
                }
            }
            finally
            {
                IsPcAccessSaving = false;
            }
        }

        private async Task AddRemotePcAsync()
        {
            if (!IsAdmin || _popup == null || string.IsNullOrWhiteSpace(SelectedSiteCode))
                return;

            string site = SelectedSiteCode.Trim();
            var result = await _popup.ShowPromptAsync(new PopupRequest
            {
                Title = "PC 추가",
                Message = "사이트: " + site
                    + "\n· 사용자명 칸 → PC 이름\n· 소속 칸 → 팀(예: 진료지원)\n· IP는 추가 후 하단 수정에서 입력",
                Icon = PopupIconKind.Info,
                ShowAffiliationInput = true,
                RequireAffiliation = false,
                AffiliationText = string.Empty,
                InputText = string.Empty,
                Buttons = new[]
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("추가", PopupResultType.Primary, isDefault: true)
                }
            }).ConfigureAwait(true);

            if (result == null || !result.IsPrimary || string.IsNullOrWhiteSpace(result.InputText))
                return;

            string pcName = result.InputText.Trim();
            string team = string.IsNullOrWhiteSpace(result.AffiliationText) ? null : result.AffiliationText.Trim();
            var map = new PcMapDto
            {
                SiteCode = site,
                PcName = pcName,
                TeamName = team,
                ShareKey = pcName
            };

            string err = await Task.Run(() => _directoryBiz.UpsertPcMapForAdmin(map, null)).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(err))
            {
                await ShowInfoPopupAsync("PC 추가", err, PopupIconKind.Warning).ConfigureAwait(true);
                return;
            }

            await LoadSelectedSiteGalleryAsync().ConfigureAwait(true);
            if (RemoteComputers == null)
                return;
            for (int i = 0; i < RemoteComputers.Count; i++)
            {
                var row = RemoteComputers[i];
                if (row != null && string.Equals(row.PcName, pcName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedRemoteComputer = row;
                    break;
                }
            }
        }

        private async Task DeleteRemotePcAsync()
        {
            if (!IsAdmin || _popup == null)
                return;
            var item = SelectedRemoteComputer;
            if (item == null || string.IsNullOrWhiteSpace(item.SiteCode) || string.IsNullOrWhiteSpace(item.PcName))
                return;

            var confirm = await _popup.ShowConfirmAsync(new PopupRequest
            {
                Title = "PC 삭제",
                Message = item.SiteCode + " / " + item.PcName + " 을(를) 목록에서 삭제할까요?",
                Icon = PopupIconKind.Warning,
                Buttons = new[]
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("삭제", PopupResultType.Primary, isDefault: true)
                }
            }).ConfigureAwait(true);

            if (confirm == null || !confirm.IsPrimary)
                return;

            string err = await Task.Run(() => _directoryBiz.DeletePcMapForAdmin(item.SiteCode, item.PcName))
                .ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(err))
            {
                await ShowInfoPopupAsync("PC 삭제", err, PopupIconKind.Warning).ConfigureAwait(true);
                return;
            }

            SelectedRemoteComputer = null;
            await LoadSelectedSiteGalleryAsync().ConfigureAwait(true);
        }

        private static string BuildPcNoteFromSections(IEnumerable<PcAccessSectionViewModel> sections)
        {
            var ids = new List<string>();
            var pws = new List<string>();
            var vpnIds = new List<string>();
            var vpnPws = new List<string>();
            var authIds = new List<string>();
            var authPws = new List<string>();
            CollectSectionLines(sections, "Domain", "ID", ids);
            CollectSectionLines(sections, "Domain", "PW", pws);
            CollectSectionLines(sections, "VPN", "ID", vpnIds);
            CollectSectionLines(sections, "VPN", "PW", vpnPws);
            CollectSectionLines(sections, "Auth", "ID", authIds);
            CollectSectionLines(sections, "Auth", "PW", authPws);

            var parts = new List<string>();
            if (ids.Count > 0)
                parts.Add("[ID]\n" + string.Join("\n", ids.ToArray()));
            if (pws.Count > 0)
                parts.Add("[Password]\n" + string.Join("\n", pws.ToArray()));
            if (vpnIds.Count > 0)
                parts.Add("[VPN ID]\n" + string.Join("\n", vpnIds.ToArray()));
            if (vpnPws.Count > 0)
                parts.Add("[VPN Password]\n" + string.Join("\n", vpnPws.ToArray()));
            if (authIds.Count > 0)
                parts.Add("[Auth ID]\n" + string.Join("\n", authIds.ToArray()));
            if (authPws.Count > 0)
                parts.Add("[Auth Password]\n" + string.Join("\n", authPws.ToArray()));
            return parts.Count == 0 ? null : string.Join("\n\n", parts.ToArray());
        }

        private static string BuildPcDomainFromSections(IEnumerable<PcAccessSectionViewModel> sections)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectSectionLines(sections, "Domain", "ID", ids);
            CollectSectionLines(sections, "VPN", "ID", ids);
            var unique = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (seen.Add(ids[i]))
                    unique.Add(ids[i]);
            }
            return unique.Count == 0 ? null : string.Join("\n", unique.ToArray());
        }

        private static void CollectSectionLines(
            IEnumerable<PcAccessSectionViewModel> sections,
            string sectionTitle,
            string fieldKind,
            IList<string> target)
        {
            if (sections == null || target == null)
                return;
            foreach (var section in sections)
            {
                if (section == null || section.Fields == null)
                    continue;
                if (!string.Equals(section.Title, sectionTitle, StringComparison.OrdinalIgnoreCase))
                    continue;
                for (int i = 0; i < section.Fields.Count; i++)
                {
                    var field = section.Fields[i];
                    if (field == null || field.Lines == null)
                        continue;
                    if (!string.Equals(field.Kind, fieldKind, StringComparison.OrdinalIgnoreCase))
                        continue;
                    for (int j = 0; j < field.Lines.Count; j++)
                    {
                        var line = field.Lines[j];
                        if (line == null || string.IsNullOrWhiteSpace(line.Value))
                            continue;
                        target.Add(line.Value.Trim());
                    }
                }
            }
        }

        private void RebuildSelectedPcAccessPanel()
        {
            IsPcAccessEditing = false;
            _selectedPcAccessSections.Clear();

            var item = SelectedRemoteComputer;
            if (item == null)
            {
                SetProperty(ref _selectedPcComment, string.Empty, "SelectedPcComment");
                _selectedPcCommentBaseline = string.Empty;
                AdminEditPcIp = string.Empty;
                AdminEditTeamName = string.Empty;
                RaisePropertyChanged("HasSelectedPcAccessSections");
                RaisePropertyChanged("ShowPcAccessCommentColumn");
                RaisePropertyChanged("PcAccessSectionColumns");
                RaisePcAccessEditCommands();
                return;
            }

            AdminEditPcIp = item.HostAddress ?? item.IpAddress ?? string.Empty;
            AdminEditTeamName = item.GroupName ?? string.Empty;

            var ids = new List<string>();
            var pws = new List<string>();
            var vpnIds = new List<string>();
            var vpnPws = new List<string>();
            var authIds = new List<string>();
            var authPws = new List<string>();

            IList<PcAccessCredential> creds = PcAccessNoteParser.ParseCredentials(item.PcNote);
            if ((creds == null || creds.Count == 0) && !string.IsNullOrWhiteSpace(item.PcDomain))
            {
                string[] parts = item.PcDomain.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string t = parts[i].Trim();
                    if (t.Length > 0)
                        ids.Add(t);
                }
            }
            else if (creds != null)
            {
                bool cmc = string.Equals(item.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);
                for (int i = 0; i < creds.Count; i++)
                {
                    PcAccessCredential c = creds[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.Value))
                        continue;
                    string v = c.Value.Trim();
                    if (string.Equals(c.Kind, "PW", StringComparison.OrdinalIgnoreCase))
                        pws.Add(v);
                    else if (string.Equals(c.Kind, "AUTH_ID", StringComparison.OrdinalIgnoreCase)
                        || (cmc && string.Equals(c.Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase)))
                        authIds.Add(v);
                    else if (string.Equals(c.Kind, "AUTH_PW", StringComparison.OrdinalIgnoreCase)
                        || (cmc && string.Equals(c.Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase)))
                        authPws.Add(v);
                    else if (string.Equals(c.Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase))
                        vpnIds.Add(v);
                    else if (string.Equals(c.Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase))
                        vpnPws.Add(v);
                    else
                        ids.Add(v);
                }
            }

            if (ids.Count > 0 || pws.Count > 0)
                _selectedPcAccessSections.Add(CreateAccessSection("Domain", ids, pws));
            if (vpnIds.Count > 0 || vpnPws.Count > 0)
                _selectedPcAccessSections.Add(CreateAccessSection("VPN", vpnIds, vpnPws));
            if (authIds.Count > 0 || authPws.Count > 0)
                _selectedPcAccessSections.Add(CreateAccessSection("Auth", authIds, authPws));

            string comment = item.PcComment;
            if (string.IsNullOrWhiteSpace(comment))
                comment = PcAccessNoteParser.ParseCommentFromNote(item.PcNote) ?? string.Empty;

            SetProperty(ref _selectedPcComment, comment ?? string.Empty, "SelectedPcComment");
            _selectedPcCommentBaseline = _selectedPcComment;

            RaisePropertyChanged("HasSelectedPcAccessSections");
            RaisePropertyChanged("ShowPcAccessCommentColumn");
            RaisePropertyChanged("PcAccessSectionColumns");
            RaisePcAccessEditCommands();
        }

        private PcAccessSectionViewModel CreateAccessSection(string title, IList<string> idValues, IList<string> pwValues)
        {
            var section = new PcAccessSectionViewModel(title);
            var idField = new PcCredentialFieldViewModel("ID", "ID");
            if (idValues != null)
            {
                for (int i = 0; i < idValues.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(idValues[i]))
                        continue;
                    idField.Lines.Add(new PcCredentialLineViewModel(idValues[i].Trim(), CopyPcCredentialCommand));
                }
            }
            if (idField.Lines.Count == 0)
                idField.Lines.Add(new PcCredentialLineViewModel(string.Empty, CopyPcCredentialCommand));
            section.Fields.Add(idField);

            var pwField = new PcCredentialFieldViewModel("PW", "PW");
            if (pwValues != null)
            {
                for (int i = 0; i < pwValues.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(pwValues[i]))
                        continue;
                    pwField.Lines.Add(new PcCredentialLineViewModel(pwValues[i].Trim(), CopyPcCredentialCommand));
                }
            }
            if (pwField.Lines.Count == 0)
                pwField.Lines.Add(new PcCredentialLineViewModel(string.Empty, CopyPcCredentialCommand));
            section.Fields.Add(pwField);
            return section;
        }

        /// <summary>TfsSyncCoordinator 등 Shell wiring용.</summary>
        public RemoteSessionController Session { get { return _session; } }
        public RdpSessionTrackingService RdpTracker { get { return _session.TrackerForShell; } }
        public Action<string> UpdateGalleryAvailableHandler { get { return UpdateGalleryItemLocalAvailable; } }

        public ObservableCollection<RemoteSiteDto> Sites
        {
            get { return _sites; }
        }

        /// <summary>갤러리 사이드바: 사이트별 사용 가능 PC 수 + 바로 이동.</summary>
        public ObservableCollection<SiteShortcutItemViewModel> SiteShortcuts
        {
            get { return _siteShortcuts; }
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
                    SyncSiteShortcutSelection();
                    var add = AddRemotePcCommand as RelayCommand;
                    if (add != null)
                        add.RaiseCanExecuteChanged();
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
                _sitePcCache.Clear();
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
            if (string.Equals(SelectedSiteCode, siteKey, StringComparison.OrdinalIgnoreCase))
                return;

            var pcs = _remotePcBiz.GetRemotePcListBySite(siteCode);
            if (pcs == null || pcs.Count == 0)
            {
                await ShowInfoPopupAsync("알림",
                    siteCode + " 사이트에 등록된 PC가 없습니다.\nApp.config " + siteCode + ".Host / " + siteCode + ".PcNames 를 확인하세요.").ConfigureAwait(true);
                return;
            }

            _sitePcCache[siteKey] = pcs;

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
            bool nameMatches = RdpStatusBiz.ComputerNamesLooselyMatch(computerKey, trackedName)
                || string.Equals(computerKey, _session.TrackerTargetIp, StringComparison.OrdinalIgnoreCase);
            bool isConfirm = RdpSessionTrackingService.IsConnectionConfirmPayload(payload) && nameMatches;

            // TFS 가져오기 재접속: 이벤트의 "사용 중"으로 점유 UI를 다시 올리지 않음
            if (_session.IsTfsSyncReconnectOrInFlight())
            {
                DiagnosticLogger.Info("Decision", "ACCEPT_NEW_GBC_PAYLOAD (events only during TFS sync)"
                    + " ComputerName=" + computerKey
                    + " LatestEventId=" + (payload.LatestEventId.HasValue ? payload.LatestEventId.Value.ToString() : "null"));
                ApplyAcceptedPayload(payload, null, payloadHash, computerKey, usedDispatcher, keepCurrentStatus: true);
                if (isConfirm && tracking)
                    _session.TryConfirmConnectionFromClipboard(payload, computerKey);
                return;
            }

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
                _session.TryConfirmConnectionFromClipboard(payload, computerKey);
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
            if (item == null || !_session.IsShareConfigured)
                return;

            var keys = ExclusiveUsageLogKeys(item);
            if (keys.Count == 0)
                return;

            int generation = ++_usageLogLoadGeneration;
            try
            {
                var merged = new List<RemotePcUsageLogDto>();
                var seen = new HashSet<long>();
                for (int i = 0; i < keys.Count; i++)
                {
                    var logs = await _session.GetRecentUsageLogsAsync(keys[i], RecentUsageLogTake).ConfigureAwait(true);
                    if (logs == null)
                        continue;
                    for (int j = 0; j < logs.Count; j++)
                    {
                        var dto = logs[j];
                        if (dto == null || seen.Contains(dto.LogId))
                            continue;
                        if (!UsageLogBelongsToItem(item, dto))
                            continue;
                        seen.Add(dto.LogId);
                        merged.Add(dto);
                    }
                }

                merged.Sort((a, b) =>
                {
                    DateTime? at = a != null ? a.RequestedAt : null;
                    DateTime? bt = b != null ? b.RequestedAt : null;
                    int c = Nullable.Compare(bt, at);
                    if (c != 0)
                        return c;
                    long aid = a != null ? a.LogId : 0;
                    long bid = b != null ? b.LogId : 0;
                    return bid.CompareTo(aid);
                });

                EnsureOpenOccupancyHistory(item, merged);

                if (merged.Count > RecentUsageLogTake)
                    merged = merged.GetRange(0, RecentUsageLogTake);

                ApplyRecentUsageLogs(generation, merged);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("USAGE_LOG", ex.Message);
            }
        }

        private static void EnsureOpenOccupancyHistory(
            RemoteComputerItemViewModel item,
            List<RemotePcUsageLogDto> merged)
        {
            if (item == null || merged == null)
                return;
            if (!item.IsInUse && !item.IsConnecting && !item.IsCheckRequired)
                return;

            for (int i = 0; i < merged.Count; i++)
            {
                var log = merged[i];
                if (log == null || log.EndedAt.HasValue)
                    continue;
                if (string.Equals(log.SessionStatus, "ENDED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(log.SessionStatus, RemotePcDbStatuses.SessionEnded, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(log.SessionStatus, RemotePcDbStatuses.InUse, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(log.SessionStatus, RemotePcDbStatuses.Connecting, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(log.SessionStatus, RemotePcDbStatuses.CheckRequired, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            merged.Insert(0, new RemotePcUsageLogDto
            {
                LogId = 0,
                SessionToken = item.SessionToken,
                RemoteAccessIpAddress = item.IpAddress,
                RemotePcName = item.PcName,
                AccessUserId = item.AccessUserId,
                AccessPcName = item.AccessPcName,
                SessionStatus = string.IsNullOrWhiteSpace(item.StatusCode)
                    ? RemotePcDbStatuses.InUse
                    : item.StatusCode,
                RequestedAt = item.AccessStartAt,
                EndedAt = null
            });
        }

        private List<string> ExclusiveUsageLogKeys(RemoteComputerItemViewModel item)
        {
            var keys = new List<string>();
            AddExclusiveKey(keys, item, item != null ? item.IpAddress : null);
            AddExclusiveKey(keys, item, item != null ? item.PcName : null);
            AddExclusiveKey(keys, item, item != null ? item.HostAddress : null);
            return keys;
        }

        private void AddExclusiveKey(List<string> keys, RemoteComputerItemViewModel item, string key)
        {
            if (item == null || string.IsNullOrWhiteSpace(key) || keys == null)
                return;
            string t = key.Trim();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], t, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            if (!ShareKeyUniqueToItem(item, t))
                return;
            keys.Add(t);
        }

        private bool ShareKeyUniqueToItem(RemoteComputerItemViewModel item, string key)
        {
            if (item == null || string.IsNullOrWhiteSpace(key) || RemoteComputers == null)
                return false;

            int hits = 0;
            for (int i = 0; i < RemoteComputers.Count; i++)
            {
                var x = RemoteComputers[i];
                if (x == null)
                    continue;
                if (GalleryItemHasKey(x, key))
                    hits++;
                if (hits > 1)
                    return false;
            }
            return hits == 1 && GalleryItemHasKey(item, key);
        }

        private static bool GalleryItemHasKey(RemoteComputerItemViewModel item, string key)
        {
            if (item == null || string.IsNullOrWhiteSpace(key))
                return false;
            return RemotePcStatusMapper.SameKey(item.IpAddress, key)
                || RemotePcStatusMapper.SameKey(item.PcName, key)
                || RemotePcStatusMapper.SameKey(item.HostAddress, key);
        }

        private static bool UsageLogBelongsToItem(RemoteComputerItemViewModel item, RemotePcUsageLogDto dto)
        {
            if (item == null || dto == null)
                return false;
            if (!string.IsNullOrWhiteSpace(dto.RemotePcName)
                && !RemotePcStatusMapper.SameKey(dto.RemotePcName, item.PcName)
                && !RemotePcStatusMapper.SameKey(dto.RemotePcName, item.IpAddress))
                return false;
            return RemotePcStatusMapper.SameKey(dto.RemoteAccessIpAddress, item.IpAddress)
                || RemotePcStatusMapper.SameKey(dto.RemoteAccessIpAddress, item.PcName)
                || RemotePcStatusMapper.SameKey(dto.RemoteAccessIpAddress, item.HostAddress)
                || RemotePcStatusMapper.SameKey(dto.RemotePcName, item.PcName);
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
            RaisePropertyChanged("HasRecentUsageLogs");
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
                User = OccupancyNameStore.TryGet()
                    ?? (string.IsNullOrWhiteSpace(RemoteWindowsUser) || RemoteWindowsUser == "-"
                        ? Environment.UserName
                        : RemoteWindowsUser),
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
                    if (hasName)
                    {
                        existing = RemoteComputers.FirstOrDefault(x =>
                            x != null
                            && string.Equals(x.PcName, dto.PcName.Trim(), StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.SiteCode ?? site, dto.HospitalCode ?? site, StringComparison.OrdinalIgnoreCase));
                    }
                    if (existing == null && hasIp)
                    {
                        string ip = dto.IpAddress.Trim();
                        var byIp = RemoteComputers.Where(x =>
                            x != null && string.Equals(x.IpAddress, ip, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (byIp.Count == 1)
                            existing = byIp[0];
                    }

                    if (existing != null)
                    {
                        if (hasName)
                            existing.PcName = dto.PcName.Trim();
                        if (hasIp)
                            existing.IpAddress = dto.IpAddress.Trim();
                        if (!string.IsNullOrWhiteSpace(dto.HostAddress))
                            existing.HostAddress = dto.HostAddress.Trim();
                        existing.GroupName = dto.GroupName;
                        existing.PcDomain = dto.PcDomain;
                        existing.PcNote = dto.PcNote;
                        existing.PcComment = dto.PcComment;
                        existing.AgentInstalled = dto.AgentInstalled;
                        existing.SiteCode = dto.HospitalCode ?? site;
                        existing.CurrentLocalUser = localUser;
                        continue;
                    }

                    RemoteComputers.Add(RemoteComputerItemViewModel.FromDto(dto, localUser));
                }

                RecalculateStatusCounts();
                RebuildGroupFilterOptions();
                RefreshGalleryFilter();
                RefreshSiteShortcutCounts(_lastOccupancy);
                if (SelectedRemoteComputer != null)
                    RebuildSelectedPcAccessPanel();
            });
        }

        private void MergeRemoteComputersFromDb(IList<RemotePcStatus> list, Func<string, bool> isLocalActiveFn)
        {
            if (list == null || string.IsNullOrWhiteSpace(SelectedSiteCode))
                return;

            string localUser = RemotePcShareBiz.LocalUserAccount;
            string site = SelectedSiteCode;
            _lastOccupancy = list;

            RunOnUi(() =>
            {
                foreach (var status in list)
                {
                    if (IsOccupancyAvailable(status))
                        ApplyOccupancyStatusToGallery(status, site, localUser, isLocalActiveFn);
                }
                foreach (var status in list)
                {
                    if (!IsOccupancyAvailable(status))
                        ApplyOccupancyStatusToGallery(status, site, localUser, isLocalActiveFn);
                }

                RecalculateStatusCounts();
                RefreshGalleryFilter();
                RefreshSiteShortcutCounts(list);
            });
        }

        private static bool IsOccupancyAvailable(RemotePcStatus status)
        {
            return status == null
                || string.Equals(status.AccessStatusCode, RemotePcDbStatuses.Available, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyOccupancyStatusToGallery(
            RemotePcStatus status,
            string site,
            string localUser,
            Func<string, bool> isLocalActiveFn)
        {
            if (status == null || string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress))
                return;

            if (!string.IsNullOrWhiteSpace(status.SiteCode)
                && !string.Equals(status.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                return;

            string ip = status.RemoteAccessIpAddress.Trim();
            var item = FindGalleryItemForOccupancy(status);
            if (item == null)
                return;

            if (!string.IsNullOrWhiteSpace(item.SiteCode)
                && !string.Equals(item.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(item.SiteCode) && !string.IsNullOrWhiteSpace(status.SiteCode))
                item.SiteCode = status.SiteCode.Trim().ToUpperInvariant();

            bool isLocalActive = isLocalActiveFn != null && isLocalActiveFn(ip);

            if (isLocalActive
                && string.Equals(status.AccessStatusCode, RemotePcDbStatuses.Available, StringComparison.OrdinalIgnoreCase)
                && (item.IsConnecting || item.IsInUse))
            {
                item.CurrentLocalUser = localUser;
                return;
            }

            item.ApplyFromDb(status, localUser);
        }

        private RemoteComputerItemViewModel FindGalleryItemByIp(string ip)
        {
            return FindGalleryItemByShareKey(ip);
        }

        /// <summary>점유 키로 갤러리 항목 찾기 (IP 또는 RC PC명). 키가 여러 카드에 겹치면 붙이지 않음.</summary>
        private RemoteComputerItemViewModel FindGalleryItemByShareKey(string shareKey)
        {
            if (string.IsNullOrWhiteSpace(shareKey) || RemoteComputers == null)
                return null;

            string key = shareKey.Trim();
            return UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.PcName, key))
                ?? UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.IpAddress, key))
                ?? UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.HostAddress, key));
        }

        private RemoteComputerItemViewModel FindGalleryItemForOccupancy(RemotePcStatus status)
        {
            if (status == null || RemoteComputers == null)
                return null;

            string name = string.IsNullOrWhiteSpace(status.RemotePcName) ? null : status.RemotePcName.Trim();
            string key = string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress)
                ? null
                : status.RemoteAccessIpAddress.Trim();

            RemoteComputerItemViewModel byName = null;
            if (!string.IsNullOrWhiteSpace(name))
                byName = UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.PcName, name));
            if (byName != null)
                return byName;

            if (!string.IsNullOrWhiteSpace(key))
            {
                var byKeyAsName = UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.PcName, key));
                if (byKeyAsName != null)
                    return byKeyAsName;

                var byShare = UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.IpAddress, key));
                if (byShare != null)
                {
                    if (string.IsNullOrWhiteSpace(name) || RemotePcStatusMapper.SameKey(byShare.PcName, name))
                        return byShare;
                    return null;
                }

                var byHost = UniqueGalleryMatch(x => RemotePcStatusMapper.SameKey(x.HostAddress, key));
                if (byHost != null
                    && (string.IsNullOrWhiteSpace(name) || RemotePcStatusMapper.SameKey(byHost.PcName, name)))
                    return byHost;
            }

            return null;
        }

        private RemoteComputerItemViewModel UniqueGalleryMatch(Func<RemoteComputerItemViewModel, bool> predicate)
        {
            if (RemoteComputers == null || predicate == null)
                return null;

            RemoteComputerItemViewModel found = null;
            for (int i = 0; i < RemoteComputers.Count; i++)
            {
                var x = RemoteComputers[i];
                if (x == null || !predicate(x))
                    continue;
                if (found != null)
                    return null;
                found = x;
            }
            return found;
        }

        private void UpdateGalleryItemFromStatus(RemotePcStatus status)
        {
            if (status == null || string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress))
                return;

            RunOnUi(() =>
            {
                var item = FindGalleryItemForOccupancy(status);
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
            UpdateCurrentSiteShortcutAvailableCount();
        }

        private void SyncSiteShortcutSelection()
        {
            if (_siteShortcuts == null)
                return;
            foreach (var row in _siteShortcuts)
            {
                if (row == null)
                    continue;
                row.IsCurrent = !string.IsNullOrWhiteSpace(SelectedSiteCode)
                    && string.Equals(row.SiteCode, SelectedSiteCode, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void UpdateCurrentSiteShortcutAvailableCount()
        {
            if (_siteShortcuts == null || string.IsNullOrWhiteSpace(SelectedSiteCode))
                return;
            foreach (var row in _siteShortcuts)
            {
                if (row == null)
                    continue;
                if (string.Equals(row.SiteCode, SelectedSiteCode, StringComparison.OrdinalIgnoreCase))
                {
                    row.AvailableCount = AvailableCount;
                    row.IsCurrent = true;
                }
            }
        }

        private void RefreshSiteShortcutCounts(IList<RemotePcStatus> occupancy)
        {
            if (_siteShortcuts == null)
                return;

            foreach (var row in _siteShortcuts)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.SiteCode))
                    continue;

                bool isCurrent = !string.IsNullOrWhiteSpace(SelectedSiteCode)
                    && string.Equals(row.SiteCode, SelectedSiteCode, StringComparison.OrdinalIgnoreCase);
                row.IsCurrent = isCurrent;

                if (isCurrent)
                {
                    row.AvailableCount = AvailableCount;
                    continue;
                }

                if (!row.IsEnabled)
                {
                    row.AvailableCount = 0;
                    continue;
                }

                var pcs = GetCachedSitePcs(row.SiteCode);
                int available = 0;
                if (pcs != null)
                {
                    foreach (var pc in pcs)
                    {
                        if (IsPcAvailableInOccupancy(pc, occupancy))
                            available++;
                    }
                }
                row.AvailableCount = available;
            }
        }

        private List<RemotePcDto> GetCachedSitePcs(string siteCode)
        {
            if (string.IsNullOrWhiteSpace(siteCode))
                return new List<RemotePcDto>();

            string key = siteCode.Trim().ToUpperInvariant();
            List<RemotePcDto> cached;
            if (_sitePcCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            cached = _remotePcBiz.GetRemotePcListBySite(key) ?? new List<RemotePcDto>();
            _sitePcCache[key] = cached;
            return cached;
        }

        private static bool IsPcAvailableInOccupancy(RemotePcDto pc, IList<RemotePcStatus> occupancy)
        {
            if (pc == null)
                return false;
            if (occupancy == null || occupancy.Count == 0)
                return true;

            string site = pc.HospitalCode;
            foreach (var status in occupancy)
            {
                if (status == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(status.SiteCode)
                    && !string.IsNullOrWhiteSpace(site)
                    && !string.Equals(status.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!RemotePcStatusMapper.StatusFitsPc(status, pc.PcName, pc.IpAddress, pc.HostAddress))
                    continue;
                if (!IsOccupancyAvailable(status))
                    return false;
            }
            return true;
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

            if (!string.Equals(SelectedGroupFilter, "전체", StringComparison.Ordinal)
                && !string.Equals(item.GroupName, SelectedGroupFilter, StringComparison.Ordinal))
            {
                return false;
            }

            string q = (SearchText ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;

            return ContainsIgnoreCase(item.PcName, q)
                || ContainsIgnoreCase(item.HostAddress, q)
                || ContainsIgnoreCase(item.DisplayTitle, q)
                || ContainsIgnoreCase(item.DisplaySubtitle, q)
                || ContainsIgnoreCase(item.SiteCode, q)
                || ContainsIgnoreCase(item.StatusCode, q)
                || ContainsIgnoreCase(item.StatusDisplayName, q)
                || ContainsIgnoreCase(item.SimpleStatusText, q)
                || ContainsIgnoreCase(item.AccessUserId, q)
                || ContainsIgnoreCase(item.AccessPcName, q)
                || ContainsIgnoreCase(item.OccupantText, q)
                || ContainsIgnoreCase(item.UserDisplayText, q)
                || ContainsIgnoreCase(item.GroupName, q)
                || ContainsIgnoreCase(item.PcDomain, q)
                || (LooksLikeIpv4(item.IpAddress) && ContainsIgnoreCase(item.IpAddress, q));
        }

        private static bool ContainsIgnoreCase(string source, string query)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
                return false;
            return source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeIpv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0 || n > 255)
                    return false;
            }
            return true;
        }

        private void RefreshGalleryFilter()
        {
            if (_filteredRemoteComputers != null)
                _filteredRemoteComputers.Refresh();
            RebuildGalleryGroups();
        }

        private void RebuildGalleryGroups()
        {
            if (_galleryGroups == null)
                return;

            _galleryGroups.Clear();
            if (_filteredRemoteComputers == null)
                return;

            RemotePcTeamGroupViewModel current = null;
            foreach (RemoteComputerItemViewModel item in _filteredRemoteComputers)
            {
                if (item == null)
                    continue;

                string name = item.GroupName ?? string.Empty;
                if (current == null || !string.Equals(current.Name, name, StringComparison.Ordinal))
                {
                    current = new RemotePcTeamGroupViewModel(name);
                    _galleryGroups.Add(current);
                }

                current.Computers.Add(item);
            }
        }

        private void SelectStatusFilter(string filter)
        {
            SelectedStatusFilter = string.IsNullOrWhiteSpace(filter) ? "ALL" : filter;
        }

        /// <summary>가나다 순(한글 완성형은 코드값 순 = 가나다 순) 먼저, 영어 등 그 외 문자는 뒤로.</summary>
        private static int CompareGroupNames(string a, string b)
        {
            bool aKorean = !string.IsNullOrEmpty(a) && a[0] >= 0xAC00 && a[0] <= 0xD7A3;
            bool bKorean = !string.IsNullOrEmpty(b) && b[0] >= 0xAC00 && b[0] <= 0xD7A3;
            if (aKorean != bKorean)
                return aKorean ? -1 : 1;
            return string.Compare(a, b, StringComparison.Ordinal);
        }

        /// <summary>현재 로드된 PC들의 GroupName 값들로 그룹 필터 목록을 갱신한다.</summary>
        private void RebuildGroupFilterOptions()
        {
            if (_groupFilterOptions == null || _remoteComputers == null)
                return;

            var distinct = new List<string>();
            foreach (RemoteComputerItemViewModel item in _remoteComputers)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.GroupName))
                    continue;
                string name = item.GroupName.Trim();
                bool exists = false;
                foreach (string g in distinct)
                {
                    if (string.Equals(g, name, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    distinct.Add(name);
            }
            distinct.Sort(CompareGroupNames);

            // ObservableCollection을 매번 Clear+Add 하면 ListBox의 SelectedItem 바인딩이 그때마다
            // 끊겨서(WPF Reset 처리) 필터가 자꾸 풀리는 것처럼 보인다 — 실제로 목록이 바뀔 때만 갱신한다.
            bool changed = _groupFilterOptions.Count != distinct.Count + 1;
            if (!changed)
            {
                for (int i = 0; i < distinct.Count; i++)
                {
                    if (!string.Equals(_groupFilterOptions[i + 1], distinct[i], StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                _groupFilterOptions.Clear();
                _groupFilterOptions.Add("전체");
                foreach (string name in distinct)
                    _groupFilterOptions.Add(name);
            }

            // 그룹(진료지원/진료간호/원무 등)은 사이트마다 달라지는 카테고리가 아니므로,
            // 사이트를 옮겨도 선택은 그대로 둔다 — 리셋하는 건 로그인한 사용자의 소속으로
            // 최초 1회 자동 선택할 때뿐.
            if (!_didAutoApplyGroupFilter && distinct.Count > 0)
            {
                _didAutoApplyGroupFilter = true;
                string myAffiliation = OccupancyNameStore.TryGetAffiliation();
                if (!string.IsNullOrWhiteSpace(myAffiliation))
                {
                    foreach (string name in distinct)
                    {
                        if (string.Equals(name, myAffiliation.Trim(), StringComparison.Ordinal))
                        {
                            SelectedGroupFilter = name;
                            break;
                        }
                    }
                }
            }
            RaisePropertyChanged("HasGroupFilterOptions");
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

        private async Task ActivateRemoteComputerAsync(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return;

            if (_selectedRemoteComputer == item)
            {
                await ExecutePrimaryActionAsync(item).ConfigureAwait(true);
                return;
            }

            SelectRemoteComputer(item);
        }

        public void ClearRemoteComputerSelection()
        {
            SelectedRemoteComputer = null;
        }

        private void SyncDetailPanelFromSelected()
        {
            var item = SelectedRemoteComputer;
            if (item == null)
                return;

            AssignAlways(ref _remoteComputerName, item.DisplayTitle, "RemoteComputerName");
            AssignAlways(ref _currentStatus, item.StatusDisplayName, "CurrentStatus");
            if (!string.IsNullOrWhiteSpace(item.AccessUserId))
                AssignAlways(ref _workHubUserAccount, OccupancyNameStore.ToDisplayName(item.AccessUserId, item.AccessPcName), "WorkHubUserAccount");
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

            if (!await EnsureLoggedInForConnectAsync().ConfigureAwait(true))
                return;

            await ConnectRemoteComputerAsync(item).ConfigureAwait(true);
        }

        private async Task ConnectRemoteComputerAsync(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return;

            if (!await EnsureLoggedInForConnectAsync().ConfigureAwait(true))
                return;

            SelectedRemoteComputer = item;

            if (item.IsOwnedByOtherUser)
            {
                if (!await ConfirmTakeoverAsync(item).ConfigureAwait(true))
                    return;
                _session.AllowTakeoverOnce(GetShareKey(item));
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

        public void RefreshConnectAuthUi()
        {
            if (RemoteComputers == null)
                return;
            foreach (var pc in RemoteComputers)
            {
                if (pc != null)
                    pc.RefreshComputedUi();
            }
            RaisePropertyChanged("IsAdmin");
            RaisePropertyChanged("ShowAdminDeletePcButton");
            var add = AddRemotePcCommand as RelayCommand;
            if (add != null)
                add.RaiseCanExecuteChanged();
            var del = DeleteRemotePcCommand as RelayCommand;
            if (del != null)
                del.RaiseCanExecuteChanged();
        }

        private async Task<bool> EnsureLoggedInForConnectAsync()
        {
            if (OccupancyNameStore.HasName)
                return true;

            if (_popup == null)
            {
                await ShowInfoPopupAsync("원격 접속", "로그인 후 접속할 수 있습니다.").ConfigureAwait(true);
                return false;
            }

            bool ok = await OccupancyNamePrompt.LoginAsync(_popup).ConfigureAwait(true);
            if (!ok || !OccupancyNameStore.HasName)
                return false;

            if (_notifyIdentityChanged != null)
                _notifyIdentityChanged();
            else
                RefreshConnectAuthUi();

            if (_onAuthSucceeded != null)
                _onAuthSucceeded();

            return true;
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

        private static string GetShareKey(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return null;
            if (!string.IsNullOrWhiteSpace(item.IpAddress))
                return item.IpAddress.Trim();
            return string.IsNullOrWhiteSpace(item.PcName) ? null : item.PcName.Trim();
        }

        private async Task<bool> ConfirmTakeoverAsync(RemoteComputerItemViewModel item)
        {
            string who = RemoteComputerItemViewModel.FormatUserId(item.AccessUserId, item.AccessPcName);
            if (string.IsNullOrWhiteSpace(who) || who == "-")
                who = "다른 사용자";
            string target = OccupancyTakeoverMessage.FormatTarget(item.SiteCode, item.PcName);

            if (_popup == null)
                return false;

            var result = await _popup.ShowConfirmAsync(new PopupRequest
            {
                Title = "점유 가져가기",
                Message = "현재 " + who + "님이 " + target + "을 사용 중입니다.\n점유를 가져가시겠습니까?",
                Detail = "가져가면 상대방 Work Hub에 알림이 표시됩니다.",
                Icon = PopupIconKind.Warning,
                Kind = PopupKind.Confirm,
                Buttons = new List<PopupButtonDefinition>
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("가져가기", PopupResultType.Primary, isDefault: true)
                }
            }).ConfigureAwait(true);

            return result != null && result.IsPrimary;
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
                WorkHubUserAccount = OccupancyNameStore.ToDisplayName(status.AccessUserId, status.AccessPcName);
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

        public void RefreshLocalIdentity()
        {
            WorkHubUserAccount = RemotePcShareBiz.LocalUserAccount;
            WorkHubClientPc = RemotePcShareBiz.LocalClientPc;
            LocalAccessIp = RemotePcShareBiz.LocalAccessIp ?? "-";
            ApplyWorkHubUserToSession();
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
            ctx.CurrentUserId = RemotePcShareBiz.LocalUserAccount;
            ctx.CurrentUserName = RemotePcShareBiz.LocalUserAccount;
        }

        private static string FormatTs(DateTime? value)
        {
            return KoreaTime.Format(value, "yyyy-MM-dd HH:mm:ss");
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

    public sealed class SiteShortcutItemViewModel : ViewModelBase
    {
        private int _availableCount;
        private bool _isCurrent;

        public SiteShortcutItemViewModel(string siteCode, bool isEnabled)
        {
            SiteCode = siteCode ?? string.Empty;
            IsEnabled = isEnabled;
        }

        public string SiteCode { get; private set; }
        public bool IsEnabled { get; private set; }

        public int AvailableCount
        {
            get { return _availableCount; }
            set { SetProperty(ref _availableCount, value); }
        }

        public bool IsCurrent
        {
            get { return _isCurrent; }
            set { SetProperty(ref _isCurrent, value); }
        }
    }

    public sealed class RemotePcTeamGroupViewModel
    {
        public RemotePcTeamGroupViewModel(string name)
        {
            Name = name ?? string.Empty;
            Computers = new ObservableCollection<RemoteComputerItemViewModel>();
        }

        public string Name { get; private set; }

        public bool HasName
        {
            get { return !string.IsNullOrWhiteSpace(Name); }
        }

        public ObservableCollection<RemoteComputerItemViewModel> Computers { get; private set; }
    }

    internal sealed class RemotePcGalleryComparer : IComparer
    {
        public int Compare(object x, object y)
        {
            var a = x as RemoteComputerItemViewModel;
            var b = y as RemoteComputerItemViewModel;
            int g = PcNameNaturalSort.CompareGroup(
                a != null ? a.GroupName : null,
                b != null ? b.GroupName : null);
            if (g != 0)
                return g;
            return PcNameNaturalSort.Compare(NameOf(a), NameOf(b));
        }

        private static string NameOf(RemoteComputerItemViewModel item)
        {
            if (item == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(item.PcName))
                return item.PcName.Trim();
            return string.Empty;
        }
    }
}
