using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.ViewModels;
using Microsoft.Win32;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 실제 TFS Candidate → Parser → WorkLogDraft 목록.
    /// Mock Data 없음. 데이터 없으면 Empty State.
    /// 가져오기는 팝업에서 선택 후 반영.
    /// </summary>
    public sealed class WorkLogListViewModel : ViewModelBase
    {
        private IPopupService _popup;
        private string _searchText = string.Empty;
        private string _filterFromText = string.Empty;
        private string _filterToText = string.Empty;
        private string _filterPc = string.Empty;
        private string _filterPerson = string.Empty;
        private string _filterWriteStatus = WorkLogFilterLabels.All;
        private string _filterType = WorkLogFilterLabels.All;
        private string _filterCategory = WorkLogFilterLabels.All;
        private string _filterDeploy = WorkLogFilterLabels.All;
        private string _filterTeam = WorkLogFilterLabels.All;
        private string _filterSite = WorkLogSiteCodes.All;
        private string _activeSiteCode;
        private bool _isEditOpen;
        private bool _isImportOpen;
        private bool _isInboxOpen;
        private WorkLogEditDialogViewModel _editDialog;
        private TfsImportDialogViewModel _importDialog;
        private WorkLogListItemViewModel _selectedItem;
        private readonly WorkLogEditSession _editSession = new WorkLogEditSession();
        private readonly WorkLogTfsImportCoordinator _tfsImport = new WorkLogTfsImportCoordinator();
        private string _ticketConnectMessage;
        private bool _hasTicketConnectMessage;
        private bool _isFilterExpanded;
        private string _activeFilterMenu;
        private bool _isSitePickerOpen;
        private WorkLogTicketTreeViewModel _detailTree;
        private ObservableCollection<RemoteComputerItemViewModel> _remotePcs;
        private Func<WorkSessionContext> _sessionContextFactory;
        private ObservableCollection<TfsChangesetCandidateViewModel> _tfsCandidates;
        private WorkSessionContext _lastSessionContext = new WorkSessionContext();
        private string _tfsStatusMessage = string.Empty;
        private int _availableCandidateCount;
        private int _newCandidateCount;
        private readonly WorkLogBiz _workLogBiz = new WorkLogBiz();
        private readonly DirectoryBiz _directory = new DirectoryBiz();
        private readonly RemotePcShareBiz _share = new RemotePcShareBiz();
        private readonly WorkLogPersistenceService _persistence;
        private readonly WorkLogImportService _importService;
        private readonly RemotePcBiz _remotePcBiz = new RemotePcBiz();
        private bool _dbLoadStarted;
        private bool _isLoading;
        private int _pageIndex;
        private int _serverTotalCount;
        private int _pageLoadGeneration;
        private int _searchDebounceGeneration;
        private int _dateDebounceGeneration;
        private int _teamOptionsGeneration;
        private bool _suppressTeamFilterChanged;
        private bool _didFillMissingTeam;
        private IList<string> _pcFilterAliases = new List<string>();
        private IList<string> _searchPcAliases = new List<string>();
        private const int DefaultPageSize = 30;
        private readonly HashSet<int> _localImportedChangesetIds = new HashSet<int>();

        public WorkLogListViewModel()
        {
            _persistence = new WorkLogPersistenceService(_workLogBiz);
            _importService = new WorkLogImportService(_workLogBiz);

            Items = new ObservableCollection<WorkLogListItemViewModel>();
            DateGroups = new ObservableCollection<WorkLogDateGroupViewModel>();
            PcOptions = new ObservableCollection<string>();
            PersonOptions = new ObservableCollection<string>();
            ImportDialog = new TfsImportDialogViewModel();

            WriteStatusOptions = new ObservableCollection<string>
            {
                WorkLogFilterLabels.All, WorkLogWriteStatus.Draft, WorkLogWriteStatus.Completed
            };
            TypeFilterOptions = new ObservableCollection<string>
            {
                WorkLogFilterLabels.All, "Client", "Server", "EQS", "DB Object", "RebFiles", "Table", "Code",
                WorkLogFilterLabels.UnclassifiedType
            };
            CategoryFilterOptions = new ObservableCollection<string> { WorkLogFilterLabels.All };
            DeployFilterOptions = new ObservableCollection<string> { WorkLogFilterLabels.All };
            TeamFilterOptions = new ObservableCollection<string> { WorkLogFilterLabels.All };
            foreach (string status in WorkLogFieldMasters.CreateDeploymentStatuses())
            {
                if (!string.Equals(status, WorkLogFieldMasters.Unselected, StringComparison.Ordinal))
                    DeployFilterOptions.Add(status);
            }
            PcFilterOptions = new ObservableCollection<string> { WorkLogFilterLabels.All };
            SiteFilterOptions = new ObservableCollection<string>
            {
                WorkLogSiteCodes.All,
                WorkLogSiteCodes.Aurora,
                WorkLogSiteCodes.Rc,
                WorkLogSiteCodes.Cmc,
                WorkLogSiteCodes.Mngha
            };
            SitePickerOptions = new ObservableCollection<string>();
            RebuildCategoryFilterOptions();

            ResetFiltersCommand = new RelayCommand(ResetFilters);
            ToggleFilterCommand = new RelayCommand(ToggleFilterBar);
            OpenFilterMenuCommand = new RelayCommand<object>(OpenFilterMenu);
            CloseFilterMenuCommand = new RelayCommand(() => SetActiveFilterMenu(null));
            SelectTypeFilterCommand = new RelayCommand<object>(p => ApplyChipFilter(() => FilterType = p as string ?? WorkLogFilterLabels.All));
            SelectCategoryFilterCommand = new RelayCommand<object>(p => ApplyChipFilter(() => FilterCategory = p as string ?? WorkLogFilterLabels.All));
            SelectDeployFilterCommand = new RelayCommand<object>(p => ApplyChipFilter(() => FilterDeploy = p as string ?? WorkLogFilterLabels.All));
            SelectTeamFilterCommand = new RelayCommand<object>(p => ApplyChipFilter(() => FilterTeam = p as string ?? WorkLogFilterLabels.All));
            SelectPcFilterCommand = new RelayCommand<object>(p => ApplyChipFilter(() => FilterPcSelection = p as string ?? WorkLogFilterLabels.All));
            ToggleSitePickerCommand = new RelayCommand(() =>
            {
                SetActiveFilterMenu(null);
                IsSitePickerOpen = !IsSitePickerOpen;
            });
            CloseSitePickerCommand = new RelayCommand(() => IsSitePickerOpen = false);
            SelectSiteCommand = new RelayCommand<object>(SelectSiteFromPicker);
            PageNumbers = new ObservableCollection<WorkLogPageNumberItem>();
            PrevPageCommand = new RelayCommand(GoPrevPage, () => CanGoPrevPage);
            NextPageCommand = new RelayCommand(GoNextPage, () => CanGoNextPage);
            GoToPageCommand = new RelayCommand<object>(GoToPage, CanGoToPage);
            NewWorkLogCommand = new RelayCommand(OpenNew);
            EditWorkLogCommand = new RelayCommand<WorkLogListItemViewModel>(item =>
            {
                if (item == null)
                    return;
                _tfsImport.ClearEditQueue();
                // LocalPcIp가 이 PC인 경우만 수정 모드 (미기록 IP는 조회만)
                OpenEdit(item, startInEditMode: item.CanEditByCurrentUser);
            });
            ViewDetailCommand = new RelayCommand<WorkLogListItemViewModel>(SelectItem);
            SelectItemCommand = new RelayCommand<WorkLogListItemViewModel>(SelectItem);
            ConnectTicketCommand = new RelayCommand<WorkLogListItemViewModel>(ConnectTicketUi);
            CreateWorkLogCommand = new RelayCommand(CreateFromSelected, () => SelectedItem != null);
            EditSelectedCommand = new RelayCommand(CreateFromSelected, () => SelectedItem != null);
            ClassifyWorkItemsCommand = new RelayCommand(CreateFromSelected, () => SelectedItem != null);
            CloseEditCommand = new RelayCommand(() => OnEditCloseRequested());
            CloseDetailCommand = new RelayCommand(() => SelectedItem = null);
            OpenImportCommand = new RelayCommand(OpenImportPicker);
            ImportCsvCommand = new RelayCommand(ImportCsv);
            CloseImportCommand = new RelayCommand(CloseImport, () => IsImportOpen);
            ConfirmImportCommand = new RelayCommand(ConfirmImport,
                () => IsImportOpen
                    && !_tfsImport.IsConfirmingImport
                    && ImportDialog != null
                    && ImportDialog.CanConfirm);
            Inbox = new TfsCheckinInboxViewModel();
            Inbox.WriteRequested = WriteFromInbox;
            OpenInboxCommand = new RelayCommand(OpenInbox);
            CloseInboxCommand = new RelayCommand(CloseInbox, () => IsInboxOpen);
            WireImportDialogCommands(ImportDialog);
            ToggleDetailNodeCommand = new RelayCommand<WorkLogTreeNodeBase>(ToggleDetailNode);
            RefreshCommand = new RelayCommand(() => { var ignored = ReloadFromDbAsync(force: true); });
            DeleteWorkLogCommand = new RelayCommand<WorkLogListItemViewModel>(
                item => { var ignored = DeleteWorkLogAsync(item); },
                item => item != null && item.CanDeleteByCurrentUser);

            RebuildDateGroups();
            var loadTask = LoadFromDbAsync();
        }

        /// <summary>MainViewModel에서 실제 Remote PC / Session 연결.</summary>
        public void AttachRuntimeSources(
            ObservableCollection<RemoteComputerItemViewModel> remotePcs,
            Func<WorkSessionContext> sessionContextFactory)
        {
            _remotePcs = remotePcs;
            _sessionContextFactory = sessionContextFactory;
            RefreshPcOptions();
            RefreshPersonOptions();
        }

        public string ResolvePcSite(string remoteIp, string remotePcName)
        {
            if (_remotePcs != null)
            {
                foreach (var pc in _remotePcs)
                {
                    if (pc == null)
                        continue;
                    if (!string.IsNullOrWhiteSpace(remoteIp)
                        && (string.Equals(pc.IpAddress, remoteIp, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(pc.HostAddress, remoteIp, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!string.IsNullOrWhiteSpace(pc.SiteCode))
                            return pc.SiteCode.Trim().ToUpperInvariant();
                    }
                    if (!string.IsNullOrWhiteSpace(remotePcName)
                        && string.Equals(pc.PcName, remotePcName, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(pc.SiteCode))
                        return pc.SiteCode.Trim().ToUpperInvariant();
                }
            }
            return TfsCheckinInboxStore.InferSiteCode(remotePcName, null);
        }

        /// <summary>원격 사이트 진입/이탈 시 업무기록 기본 사이트 컨텍스트.</summary>
        public void SetSiteContext(string siteCode)
        {
            if (string.IsNullOrWhiteSpace(siteCode))
            {
                _activeSiteCode = null;
                FilterSite = WorkLogSiteCodes.All;
                return;
            }

            _activeSiteCode = siteCode.Trim().ToUpperInvariant();
            FilterSite = _activeSiteCode;
        }

        public void AttachPopup(IPopupService popup)
        {
            _popup = popup;
        }

        /// <summary>
        /// TFS Candidate 수신 알림. 후보 수만 갱신.
        /// 가져오기 팝업은 사용자가 버튼/성공 팝업에서 열 때만 연다 (자동 OpenImport 금지).
        /// </summary>
        public void NotifyTfsCandidatesUpdated(
            ObservableCollection<TfsChangesetCandidateViewModel> candidates,
            WorkSessionContext sessionContext,
            string statusMessage,
            bool openImportIfNew)
        {
            _tfsCandidates = candidates;
            _lastSessionContext = sessionContext ?? (_sessionContextFactory != null
                ? _sessionContextFactory()
                : new WorkSessionContext());
            _tfsStatusMessage = statusMessage ?? string.Empty;

            AvailableCandidateCount = candidates != null ? candidates.Count : 0;
            var imported = CollectImportedChangesetIds();
            NewCandidateCount = candidates == null
                ? 0
                : candidates.Count(c => c != null && !imported.Contains(c.ChangesetId));

            RefreshPcOptions();
            RefreshPersonOptions();
            RefreshInboxBadge();

            // openImportIfNew 무시 — 자동 팝업으로 확정/저장이 꼬이던 원인
        }

        /// <summary>기존 호환: 전체 Candidate를 즉시 목록에 반영하지 않고 팝업 오픈.</summary>
        public void ReloadFromTfsCandidates(
            ObservableCollection<TfsChangesetCandidateViewModel> candidates,
            WorkSessionContext sessionContext)
        {
            NotifyTfsCandidatesUpdated(candidates, sessionContext, null, openImportIfNew: false);
        }

        public ObservableCollection<WorkLogListItemViewModel> Items { get; private set; }
        public ObservableCollection<WorkLogDateGroupViewModel> DateGroups { get; private set; }
        public ObservableCollection<string> WriteStatusOptions { get; private set; }
        public ObservableCollection<string> TypeFilterOptions { get; private set; }
        public ObservableCollection<string> CategoryFilterOptions { get; private set; }
        public ObservableCollection<string> DeployFilterOptions { get; private set; }
        public ObservableCollection<string> TeamFilterOptions { get; private set; }
        /// <summary>필터용 PC 칩 ("전체" + 원격 PC).</summary>
        public ObservableCollection<string> PcFilterOptions { get; private set; }
        public ObservableCollection<string> SiteFilterOptions { get; private set; }
        /// <summary>툴바 사이트 버블 선택지 (현재 사이트 제외).</summary>
        public ObservableCollection<string> SitePickerOptions { get; private set; }
        public ObservableCollection<string> PcOptions { get; private set; }
        public ObservableCollection<string> PersonOptions { get; private set; }

        /// <summary>Category 칩이 전체 외에 있을 때만 사이드바에 표시.</summary>
        public bool HasCategoryFilter
        {
            get { return CategoryFilterOptions != null && CategoryFilterOptions.Count > 1; }
        }

        public bool HasTeamFilter
        {
            get { return TeamFilterOptions != null && TeamFilterOptions.Count > 1; }
        }

        /// <summary>사이트가 ALL이 아닐 때만 PC 필터 표시 (사이트별 IP).</summary>
        public bool HasPcFilter
        {
            get
            {
                return !string.IsNullOrWhiteSpace(FilterSite)
                    && !string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase);
            }
        }

        public int VisibleListCount
        {
            get
            {
                int n = 0;
                if (DateGroups == null)
                    return 0;
                foreach (var g in DateGroups)
                {
                    if (g != null && g.Items != null)
                        n += g.Items.Count;
                }
                return n;
            }
        }

        public bool HasItems { get { return VisibleListCount > 0; } }
        public bool IsEmpty { get { return VisibleListCount == 0; } }

        /// <summary>DB 목록 로딩 중 — 스켈레톤 UI 표시.</summary>
        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaisePropertyChanged("ShowEmptyState");
                    RaisePropertyChanged("ShowListContent");
                }
            }
        }

        /// <summary>로딩이 끝났고 항목이 없을 때만 빈 상태.</summary>
        public bool ShowEmptyState
        {
            get { return !_isLoading && VisibleListCount == 0; }
        }

        /// <summary>로딩 중이 아닐 때만 실제 목록 표시.</summary>
        public bool ShowListContent
        {
            get { return !_isLoading && VisibleListCount > 0; }
        }

        /// <summary>
        /// App.config WorkLog.UseGlassmorphism (또는 WorkLogUiOptions 오버라이드).
        /// false면 클래식 목록 UI로 롤백.
        /// </summary>
        public bool UseGlassmorphism
        {
            get { return WorkLogUiOptions.UseGlassmorphism; }
        }

        public WorkLogListItemViewModel SelectedItem
        {
            get { return _selectedItem; }
            private set
            {
                if (_selectedItem == value)
                    return;
                if (_selectedItem != null)
                    _selectedItem.IsSelected = false;
                _selectedItem = value;
                if (_selectedItem != null)
                    _selectedItem.IsSelected = true;
                RaisePropertyChanged("SelectedItem");
                RaisePropertyChanged("HasSelection");
                ((RelayCommand)CreateWorkLogCommand).RaiseCanExecuteChanged();
                ((RelayCommand)EditSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ClassifyWorkItemsCommand).RaiseCanExecuteChanged();
            }
        }

        public bool HasSelection { get { return SelectedItem != null; } }

        public WorkLogTicketTreeViewModel DetailTree
        {
            get { return _detailTree; }
            private set { SetProperty(ref _detailTree, value); }
        }

        public bool IsFilterExpanded
        {
            get { return _isFilterExpanded; }
            set
            {
                if (!SetProperty(ref _isFilterExpanded, value))
                    return;
                if (!value)
                    SetActiveFilterMenu(null);
            }
        }

        public bool IsTypeMenuOpen
        {
            get { return IsMenu("type"); }
            set { SetMenuOpen("type", value); }
        }

        public bool IsCategoryMenuOpen
        {
            get { return IsMenu("category"); }
            set { SetMenuOpen("category", value); }
        }

        public bool IsDeployMenuOpen
        {
            get { return IsMenu("deploy"); }
            set { SetMenuOpen("deploy", value); }
        }

        public bool IsTeamMenuOpen
        {
            get { return IsMenu("team"); }
            set { SetMenuOpen("team", value); }
        }

        public bool IsPcMenuOpen
        {
            get { return IsMenu("pc"); }
            set { SetMenuOpen("pc", value); }
        }

        public bool IsDateMenuOpen
        {
            get { return IsMenu("date"); }
            set { SetMenuOpen("date", value); }
        }

        /// <summary>사이트 피커와 동일: 옵션 버블이 열려 있을 때 딤.</summary>
        public bool IsFilterMenuOpen
        {
            get { return !string.IsNullOrEmpty(_activeFilterMenu); }
        }

        public bool IsTypeFilterActive { get { return !IsFilterAll(FilterType); } }
        public bool IsCategoryFilterActive { get { return !IsFilterAll(FilterCategory); } }
        public bool IsDeployFilterActive { get { return !IsFilterAll(FilterDeploy); } }
        public bool IsTeamFilterActive { get { return !IsFilterAll(FilterTeam); } }
        public bool IsPcFilterActive { get { return !string.IsNullOrWhiteSpace(FilterPc); } }
        public bool IsDateFilterActive
        {
            get { return AreBothDateFiltersComplete(); }
        }

        /// <summary>필터 타이틀 pill 표시문구. 선택 시 옵션값, 미선택 시 기본 라벨.</summary>
        public string TypeFilterPillLabel
        {
            get { return IsTypeFilterActive ? (FilterType ?? "Type") : "Type"; }
        }

        public string CategoryFilterPillLabel
        {
            get { return IsCategoryFilterActive ? (FilterCategory ?? "Category") : "Category"; }
        }

        public string DeployFilterPillLabel
        {
            get { return IsDeployFilterActive ? (FilterDeploy ?? "Deployment") : "Deployment"; }
        }

        public string TeamFilterPillLabel
        {
            get { return IsTeamFilterActive ? (FilterTeam ?? "소속") : "소속"; }
        }

        public string PcFilterPillLabel
        {
            get { return IsPcFilterActive ? (FilterPc ?? "PC") : "PC"; }
        }

        public string DateFilterPillLabel
        {
            get
            {
                if (!IsDateFilterActive)
                    return "Date";
                return FilterFromText.Trim() + " ~ " + FilterToText.Trim();
            }
        }

        public bool HasActiveFilters
        {
            get
            {
                return IsTypeFilterActive
                    || IsCategoryFilterActive
                    || IsDeployFilterActive
                    || IsTeamFilterActive
                    || IsPcFilterActive
                    || IsDateFilterActive;
            }
        }

        public ICommand OpenFilterMenuCommand { get; private set; }
        public ICommand CloseFilterMenuCommand { get; private set; }
        public ICommand SelectTypeFilterCommand { get; private set; }
        public ICommand SelectCategoryFilterCommand { get; private set; }
        public ICommand SelectDeployFilterCommand { get; private set; }
        public ICommand SelectTeamFilterCommand { get; private set; }
        public ICommand SelectPcFilterCommand { get; private set; }

        private void ToggleFilterBar()
        {
            IsSitePickerOpen = false;
            IsFilterExpanded = !IsFilterExpanded;
        }

        private void OpenFilterMenu(object parameter)
        {
            string key = parameter as string;
            if (string.IsNullOrWhiteSpace(key))
                return;
            IsSitePickerOpen = false;
            if (!IsFilterExpanded)
                IsFilterExpanded = true;
            if (IsMenu(key))
                SetActiveFilterMenu(null);
            else
                SetActiveFilterMenu(key.Trim().ToLowerInvariant());
        }

        private void ApplyChipFilter(Action apply)
        {
            if (apply != null)
                apply();
            SetActiveFilterMenu(null);
        }

        private bool IsMenu(string key)
        {
            return string.Equals(_activeFilterMenu, key, StringComparison.OrdinalIgnoreCase);
        }

        private void SetMenuOpen(string key, bool open)
        {
            if (open)
                SetActiveFilterMenu(key);
            else if (IsMenu(key))
                SetActiveFilterMenu(null);
        }

        private void SetActiveFilterMenu(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = null;
            if (string.Equals(_activeFilterMenu, key, StringComparison.OrdinalIgnoreCase))
                return;
            _activeFilterMenu = key;
            RaisePropertyChanged("IsTypeMenuOpen");
            RaisePropertyChanged("IsCategoryMenuOpen");
            RaisePropertyChanged("IsDeployMenuOpen");
            RaisePropertyChanged("IsTeamMenuOpen");
            RaisePropertyChanged("IsPcMenuOpen");
            RaisePropertyChanged("IsDateMenuOpen");
            RaisePropertyChanged("IsFilterMenuOpen");
        }

        private static bool IsFilterAll(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value, WorkLogFilterLabels.All, StringComparison.Ordinal);
        }

        public bool IsSitePickerOpen
        {
            get { return _isSitePickerOpen; }
            set
            {
                if (!SetProperty(ref _isSitePickerOpen, value))
                    return;
                if (value)
                    RebuildSitePickerOptions();
            }
        }

        public System.Collections.IEnumerable SelectedClassificationSummary
        {
            get
            {
                if (SelectedItem == null)
                    return new TypeBadgeItem[0];
                return SelectedItem.ClassificationSummary;
            }
        }

        public int SelectedModifiedCount
        {
            get
            {
                return SelectedItem == null || SelectedItem.Sources == null
                    ? 0
                    : SelectedItem.Sources.Count(s => s.IsChangeEdit);
            }
        }

        public int SelectedAddedCount
        {
            get
            {
                return SelectedItem == null || SelectedItem.Sources == null
                    ? 0
                    : SelectedItem.Sources.Count(s => s.IsChangeAdd);
            }
        }

        public int SelectedDeletedCount
        {
            get
            {
                return SelectedItem == null || SelectedItem.Sources == null
                    ? 0
                    : SelectedItem.Sources.Count(s => s.IsChangeDelete);
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                    ScheduleSearchFilterChanged();
            }
        }

        public string FilterFromText
        {
            get { return _filterFromText; }
            set
            {
                string masked = WorkLogDraftMapper.MaskDateInput(value);
                if (!SetProperty(ref _filterFromText, masked))
                {
                    // digits→hyphen 변환 시 TextBox에 포맷 반영
                    if (!string.Equals(value ?? string.Empty, masked, StringComparison.Ordinal))
                        RaisePropertyChanged("FilterFromText");
                    return;
                }
                RaisePropertyChanged("FilterDateSummary");
                RaiseFilterSummaryChanged();
                ScheduleDateFilterChanged();
            }
        }

        public string FilterToText
        {
            get { return _filterToText; }
            set
            {
                string masked = WorkLogDraftMapper.MaskDateInput(value);
                if (!SetProperty(ref _filterToText, masked))
                {
                    if (!string.Equals(value ?? string.Empty, masked, StringComparison.Ordinal))
                        RaisePropertyChanged("FilterToText");
                    return;
                }
                RaisePropertyChanged("FilterDateSummary");
                RaiseFilterSummaryChanged();
                ScheduleDateFilterChanged();
            }
        }

        /// <summary>기간 필터 접힌 헤더에 표시. 양쪽 작성 완료 시에만 범위 표시.</summary>
        public string FilterDateSummary
        {
            get
            {
                if (!AreBothDateFiltersComplete())
                    return WorkLogFilterLabels.All;
                return FilterFromText.Trim() + " ~ " + FilterToText.Trim();
            }
        }

        public string FilterPc
        {
            get { return _filterPc; }
            set
            {
                string normalized = NormalizeAllFilter(value);
                if (SetProperty(ref _filterPc, normalized))
                {
                    RaisePropertyChanged("FilterPcSelection");
                    OnFilterChanged();
                }
            }
        }

        /// <summary>칩 UI용. 미선택이면 "전체".</summary>
        public string FilterPcSelection
        {
            get { return string.IsNullOrWhiteSpace(_filterPc) ? WorkLogFilterLabels.All : _filterPc; }
            set { FilterPc = value; }
        }

        public string FilterPerson
        {
            get { return _filterPerson; }
            set { if (SetProperty(ref _filterPerson, value ?? string.Empty)) OnFilterChanged(); }
        }

        public string FilterWriteStatus
        {
            get { return _filterWriteStatus; }
            set { if (SetProperty(ref _filterWriteStatus, value ?? WorkLogFilterLabels.All)) OnFilterChanged(); }
        }

        public string FilterType
        {
            get { return _filterType; }
            set
            {
                if (SetProperty(ref _filterType, value ?? WorkLogFilterLabels.All))
                {
                    RebuildCategoryFilterOptions();
                    OnFilterChanged();
                }
            }
        }

        public string FilterCategory
        {
            get { return _filterCategory; }
            set { if (SetProperty(ref _filterCategory, value ?? WorkLogFilterLabels.All)) OnFilterChanged(); }
        }

        public string FilterDeploy
        {
            get { return _filterDeploy; }
            set { if (SetProperty(ref _filterDeploy, value ?? WorkLogFilterLabels.All)) OnFilterChanged(); }
        }

        public string FilterTeam
        {
            get { return _filterTeam; }
            set
            {
                if (!SetProperty(ref _filterTeam, value ?? WorkLogFilterLabels.All))
                    return;
                if (_suppressTeamFilterChanged)
                    return;
                OnFilterChanged();
            }
        }

        public string FilterSite
        {
            get { return _filterSite; }
            set
            {
                if (SetProperty(ref _filterSite, string.IsNullOrWhiteSpace(value) ? WorkLogSiteCodes.All : value))
                {
                    if (!HasPcFilter)
                    {
                        if (!string.IsNullOrWhiteSpace(_filterPc))
                            FilterPc = string.Empty;
                        if (IsMenu("pc"))
                            SetActiveFilterMenu(null);
                    }
                    RefreshPcOptions();
                    OnFilterChanged();
                    RaisePropertyChanged("HasPcFilter");
                    RaisePropertyChanged("SiteHeaderText");
                    RaisePropertyChanged("SiteDisplayName");
                    RaisePropertyChanged("HeaderSiteName");
                }
            }
        }

        /// <summary>목록 헤더용 사이트 표기.</summary>
        public string SiteHeaderText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterSite)
                    || string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.Ordinal))
                    return "사이트: " + WorkLogFilterLabels.All;
                return "사이트: " + FilterSite.Trim().ToUpperInvariant();
            }
        }

        /// <summary>툴바에 굵게 표시할 현재 사이트 (예: CMC / ALL).</summary>
        public string SiteDisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterSite)
                    || string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.Ordinal))
                    return "ALL";
                return FilterSite.Trim().ToUpperInvariant();
            }
        }

        /// <summary>앱 헤더용 사이트명(상세/작성 전용). 목록에서는 쓰지 않음.</summary>
        public string HeaderSiteName
        {
            get
            {
                if (!IsEditOpen || EditDialog == null)
                    return null;
                if (string.IsNullOrWhiteSpace(EditDialog.SiteCode)
                    || string.Equals(EditDialog.SiteCode, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                    return null;
                return EditDialog.SiteCode.Trim().ToUpperInvariant();
            }
        }

        public int PageSize { get { return DefaultPageSize; } }

        public int CurrentPage
        {
            get { return TotalPages == 0 ? 0 : _pageIndex + 1; }
        }

        public int TotalPages
        {
            get
            {
                int total = TotalCount;
                if (total <= 0)
                    return 0;
                return (total + PageSize - 1) / PageSize;
            }
        }

        public bool CanGoPrevPage { get { return _pageIndex > 0; } }
        public bool CanGoNextPage { get { return TotalPages > 0 && _pageIndex < TotalPages - 1; } }

        public string PageInfoText
        {
            get
            {
                int total = TotalCount;
                if (total <= 0)
                    return "0건";
                int from = _pageIndex * PageSize + 1;
                int to = Math.Min(total, (_pageIndex + 1) * PageSize);
                return string.Format("{0}-{1} / 총 {2}건 · {3}/{4}페이지",
                    from, to, total, CurrentPage, TotalPages);
            }
        }

        public bool ShowPagination { get { return TotalPages > 1; } }

        /// <summary>DB COUNT(*) 결과 (필터 반영).</summary>
        public int TotalCount { get { return _serverTotalCount; } }

        public ObservableCollection<WorkLogPageNumberItem> PageNumbers { get; private set; }

        public bool IsEditOpen
        {
            get { return _isEditOpen; }
            set { SetProperty(ref _isEditOpen, value); }
        }

        public bool IsImportOpen
        {
            get { return _isImportOpen; }
            set
            {
                if (SetProperty(ref _isImportOpen, value))
                {
                    var confirm = ConfirmImportCommand as RelayCommand;
                    if (confirm != null)
                        confirm.RaiseCanExecuteChanged();
                    var close = CloseImportCommand as RelayCommand;
                    if (close != null)
                        close.RaiseCanExecuteChanged();
                }
            }
        }

        public WorkLogEditDialogViewModel EditDialog
        {
            get { return _editDialog; }
            private set { SetProperty(ref _editDialog, value); }
        }

        public TfsImportDialogViewModel ImportDialog
        {
            get { return _importDialog; }
            private set { SetProperty(ref _importDialog, value); }
        }

        public TfsCheckinInboxViewModel Inbox { get; private set; }

        public bool ShowInboxCloseButton
        {
            get { return true; }
        }

        public bool ShowInboxSiteTabs
        {
            get { return true; }
        }

        public bool IsInboxOpen
        {
            get { return _isInboxOpen; }
            set
            {
                if (SetProperty(ref _isInboxOpen, value))
                {
                    var close = CloseInboxCommand as RelayCommand;
                    if (close != null)
                        close.RaiseCanExecuteChanged();
                }
            }
        }

        public int InboxWaitingCount
        {
            get { return Inbox != null ? Inbox.WaitingCount : 0; }
        }

        public bool HasInboxWaiting
        {
            get { return InboxWaitingCount > 0; }
        }

        public string InboxButtonLabel
        {
            get
            {
                return InboxWaitingCount > 0
                    ? "체크인 보관함 (" + InboxWaitingCount + ")"
                    : "체크인 보관함";
            }
        }

        public int AvailableCandidateCount
        {
            get { return _availableCandidateCount; }
            private set
            {
                if (SetProperty(ref _availableCandidateCount, value))
                {
                    RaisePropertyChanged("HasAvailableCandidates");
                    RaisePropertyChanged("ImportButtonLabel");
                }
            }
        }

        public int NewCandidateCount
        {
            get { return _newCandidateCount; }
            private set
            {
                if (SetProperty(ref _newCandidateCount, value))
                {
                    RaisePropertyChanged("HasNewCandidates");
                    RaisePropertyChanged("ImportButtonLabel");
                }
            }
        }

        public bool HasAvailableCandidates { get { return AvailableCandidateCount > 0; } }
        public bool HasNewCandidates { get { return NewCandidateCount > 0; } }

        public string ImportButtonLabel
        {
            get
            {
                if (NewCandidateCount > 0)
                    return "TFS 가져오기 (" + NewCandidateCount + ")";
                if (AvailableCandidateCount > 0)
                    return "TFS 가져오기 (" + AvailableCandidateCount + ")";
                return "TFS 가져오기";
            }
        }

        public string TicketConnectMessage
        {
            get { return _ticketConnectMessage; }
            private set { SetProperty(ref _ticketConnectMessage, value); }
        }

        public bool HasTicketConnectMessage
        {
            get { return _hasTicketConnectMessage; }
            private set { SetProperty(ref _hasTicketConnectMessage, value); }
        }

        public ObservableCollection<string> RuntimePcOptions { get { return PcOptions; } }
        public ObservableCollection<string> RuntimePersonOptions { get { return PersonOptions; } }

        public ICommand ResetFiltersCommand { get; private set; }
        public ICommand ToggleFilterCommand { get; private set; }
        public ICommand ToggleSitePickerCommand { get; private set; }
        public ICommand CloseSitePickerCommand { get; private set; }
        public ICommand SelectSiteCommand { get; private set; }
        public ICommand PrevPageCommand { get; private set; }
        public ICommand NextPageCommand { get; private set; }
        public ICommand GoToPageCommand { get; private set; }
        public ICommand NewWorkLogCommand { get; private set; }
        public ICommand EditWorkLogCommand { get; private set; }
        public ICommand EditSelectedCommand { get; private set; }
        public ICommand ViewDetailCommand { get; private set; }
        public ICommand SelectItemCommand { get; private set; }
        public ICommand ConnectTicketCommand { get; private set; }
        public ICommand CreateWorkLogCommand { get; private set; }
        public ICommand ClassifyWorkItemsCommand { get; private set; }
        public ICommand CloseEditCommand { get; private set; }
        public ICommand CloseDetailCommand { get; private set; }
        public ICommand OpenImportCommand { get; private set; }
        public ICommand OpenInboxCommand { get; private set; }
        public ICommand CloseInboxCommand { get; private set; }
        public ICommand ImportCsvCommand { get; private set; }
        public ICommand CloseImportCommand { get; private set; }
        public ICommand ConfirmImportCommand { get; private set; }

        /// <summary>사이트·날짜·PC로 TFS 수집. MainViewModel에서 연결.</summary>
        public Func<string, string, DateTime, DateTime, bool, Task> FetchTfsForWindow { get; set; }
        public ICommand ToggleDetailNodeCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand DeleteWorkLogCommand { get; private set; }

        /// <summary>TFS 동기화 완료 후 업무기록 탭에서 체크인 목록 팝업 오픈.</summary>
        public void ShowImportDialog()
        {
            var _ = OpenImportFetchedAsync();
        }

        /// <summary>
        /// 등록 완료된 Changeset ID (상세 "저장"/COMPLETED → WRK_CS).
        /// 오늘 폴백 미등록 필터용. 가져오기·임시저장만 한 건은 포함하지 않음.
        /// </summary>
        public HashSet<int> GetImportedChangesetIds()
        {
            try
            {
                if (_persistence != null && _persistence.IsConfigured)
                {
                    var fromDb = _persistence.GetRegisteredChangesetIdsAsync()
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (fromDb != null)
                    {
                        var fromDbSet = new HashSet<int>(fromDb);
                        foreach (int id in _localImportedChangesetIds)
                            fromDbSet.Add(id);
                        return fromDbSet;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("WORKLOG_CS", "GetImportedChangesetIds DB failed: " + ex.Message);
            }

            // DDL 미적용 등: 완료 상태 목록만 폴백
            var imported = new HashSet<int>(_localImportedChangesetIds);
            foreach (var item in Items)
            {
                if (item == null)
                    continue;
                if (!string.Equals(item.WriteStatus, WorkLogWriteStatus.Completed, StringComparison.Ordinal))
                    continue;
                foreach (var id in item.GetEffectiveChangesetIds())
                    imported.Add(id);
            }
            return imported;
        }

        public void RememberImportedChangesetId(int changesetId)
        {
            if (changesetId > 0)
                _localImportedChangesetIds.Add(changesetId);
        }

        /// <summary>이미 업무기록에 붙은 CS. 가져오기 후보에서 제외한다.</summary>
        private HashSet<int> CollectImportedChangesetIds()
        {
            var imported = new HashSet<int>(_localImportedChangesetIds);
            foreach (var item in Items)
            {
                if (item == null)
                    continue;
                foreach (var id in item.GetEffectiveChangesetIds())
                    imported.Add(id);
            }
            return imported;
        }

        private async void ImportCsv()
        {
            var dlg = new OpenFileDialog
            {
                Title = "업무기록 가져오기 (Excel/CSV)",
                Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                string path = dlg.FileName;
                var records = await Task.Run(() => WorkLogImportService.ParseFile(path))
                    .ConfigureAwait(true);
                if (records == null || records.Count == 0)
                {
                    MessageBox.Show("가져올 업무기록이 없습니다.", "업무기록 가져오기",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string siteCd = ResolveSiteCodeForWrite();
                if (string.IsNullOrWhiteSpace(siteCd))
                {
                    MessageBox.Show(
                        "사이트를 선택한 뒤 가져와 주세요.\n필터에서 AURORA 또는 RC를 고르거나, 원격 사이트 화면에서 진입하세요.",
                        "업무기록 가져오기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    IsFilterExpanded = true;
                    return;
                }

                var importResult = await _importService.SaveParsedRecordsAsync(records, siteCd)
                    .ConfigureAwait(true);
                if (importResult.HasBlockingError)
                {
                    MessageBox.Show(
                        importResult.ErrorMessage,
                        "업무기록 가져오기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    if (importResult.ErrorMessage != null
                        && importResult.ErrorMessage.IndexOf("사이트", StringComparison.Ordinal) >= 0)
                        IsFilterExpanded = true;
                    return;
                }

                foreach (var item in importResult.SavedItems)
                {
                    if (item == null)
                        continue;

                    // 같은 TicketNo 초안/기존이 있으면 교체
                    var existing = Items.FirstOrDefault(x =>
                        x != null
                        && !string.IsNullOrWhiteSpace(item.TicketNo)
                        && string.Equals(
                            WorkLogDraftMapper.NormalizeTicketStorage(x.TicketNo),
                            WorkLogDraftMapper.NormalizeTicketStorage(item.TicketNo),
                            StringComparison.OrdinalIgnoreCase)
                        && x.DbLogId <= 0);
                    if (existing != null)
                        Items.Remove(existing);
                    Items.Add(item);
                }

                await LoadPageFromDbAsync().ConfigureAwait(true);

                DiagnosticLogger.Info("WORKLOG_FILE_IMPORT",
                    "file=" + path + " parsed=" + importResult.ParsedCount
                    + " ok=" + importResult.OkCount
                    + " skip=" + importResult.SkipCount
                    + " fail=" + importResult.FailCount);

                string msg = "가져오기 완료\n신규 저장 " + importResult.OkCount + "건";
                if (importResult.SkipCount > 0)
                    msg += "\n중복 스킵 " + importResult.SkipCount + "건 (내용이 DB와 완전히 같은 건)";
                if (importResult.FailCount > 0)
                    msg += "\n실패 " + importResult.FailCount + "건: "
                        + (_importService.LastConnectionError ?? "unknown");
                MessageBox.Show(msg, "업무기록 가져오기", MessageBoxButton.OK,
                    importResult.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("WORKLOG_FILE_IMPORT", "failed: " + ex.Message);
                MessageBox.Show("가져오기 실패:\n" + ex.Message, "업무기록 가져오기",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WireImportDialogCommands(TfsImportDialogViewModel dialog)
        {
            if (dialog == null)
                return;
            dialog.CloseCommand = CloseImportCommand;
            dialog.ConfirmCommand = ConfirmImportCommand;
            dialog.FetchSessionQueryCommand = new RelayCommand(
                () => { var _ = FetchSessionQueryAsync(); },
                () => dialog.CanFetchSessionQuery);
        }

        /// <summary>업무기록 툴바: 사이트·날짜·PC로 TFS 수집. 체크인은 가져온 뒤에만.</summary>
        private void OpenImportPicker()
        {
            OpenImportWithCandidates(null, string.Empty);
            ImportDialog.PrepareSessionQuery(FilterSite);
            RefreshImportSessionPcOptions();
        }

        /// <summary>원격 TFS 수집이 끝난 뒤: 체크인 상세만.</summary>
        private async Task OpenImportFetchedAsync()
        {
            OpenImportWithCandidates(_tfsCandidates, _tfsStatusMessage);
            await Task.CompletedTask.ConfigureAwait(true);
        }

        private void OpenImport()
        {
            OpenImportPicker();
        }

        private void OpenImportWithCandidates(
            IEnumerable<TfsChangesetCandidateViewModel> candidates,
            string statusMessage)
        {
            _tfsImport.ClearSession();
            if (ImportDialog == null)
                ImportDialog = new TfsImportDialogViewModel();
            WireImportDialogCommands(ImportDialog);

            var imported = CollectImportedChangesetIds();
            foreach (int id in GetImportedChangesetIds())
                imported.Add(id);
            string status = statusMessage;
            if (string.IsNullOrWhiteSpace(status) && candidates != null)
                status = "수신 후보 " + candidates.Count() + "건";

            ImportDialog.Load(candidates, imported, status);
            string appendSite = string.IsNullOrWhiteSpace(FilterSite)
                || string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase)
                ? null
                : FilterSite;
            ImportDialog.SetAppendTargets(Items, appendSite);
            ImportDialog.PropertyChanged -= OnImportDialogPropertyChanged;
            ImportDialog.PropertyChanged += OnImportDialogPropertyChanged;
            IsImportOpen = true;
            var cmd = ConfirmImportCommand as RelayCommand;
            if (cmd != null)
                cmd.RaiseCanExecuteChanged();

            // 별도 OS Window(ShowDialog) 대신 목록 위 인프로세스 오버레이
        }

        private void RefreshImportSessionPcOptions()
        {
            if (ImportDialog == null)
                return;
            ImportDialog.ReplaceSessionPcOptions(CollectPcIpsForSite(ImportDialog.SessionSite));
        }

        private async Task FetchSessionQueryAsync()
        {
            if (ImportDialog == null || FetchTfsForWindow == null)
                return;

            DateTime fromAt;
            DateTime toAt;
            string error;
            if (!ImportDialog.TryGetSearchWindow(out fromAt, out toAt, out error))
            {
                ImportDialog.SessionSearchMessage = error;
                return;
            }
            if (string.IsNullOrWhiteSpace(ImportDialog.SessionPc))
            {
                ImportDialog.SessionSearchMessage = "PC를 선택해 주세요.";
                return;
            }

            string shareKey;
            string pcName;
            ResolvePcTarget(
                ImportDialog.SessionPc,
                ImportDialog.SessionSite,
                out shareKey,
                out pcName);
            if (string.IsNullOrWhiteSpace(shareKey))
            {
                ImportDialog.SessionSearchMessage = "PC를 선택해 주세요.";
                return;
            }

            ImportDialog.SessionSearchMessage = string.Empty;
            CloseImport();
            bool clipboardOnly = string.Equals(
                ImportDialog.SessionSite, WorkLogSiteCodes.Cmc, StringComparison.OrdinalIgnoreCase);
            await FetchTfsForWindow(shareKey, pcName, fromAt, toAt, clipboardOnly).ConfigureAwait(true);
        }

        /// <summary>
        /// 가져오기 접속구간용. AURORA 등은 mstsc /v: 에 쓸 IPv4를 우선하고,
        /// RC만 PC명 점유키(게시 .rdp)를 유지한다.
        /// </summary>
        private void ResolvePcTarget(
            string selected,
            string siteCode,
            out string shareKey,
            out string pcName)
        {
            shareKey = (selected ?? string.Empty).Trim();
            pcName = shareKey;
            if (string.IsNullOrWhiteSpace(shareKey))
                return;

            string site = string.IsNullOrWhiteSpace(siteCode)
                ? null
                : siteCode.Trim().ToUpperInvariant();
            bool preferPublishedRdp = string.Equals(site, WorkLogSiteCodes.Rc, StringComparison.OrdinalIgnoreCase);

            RemotePcDto fromSite = FindRemotePcDto(selected, site);
            if (fromSite != null)
            {
                ApplyResolvedPcTarget(fromSite, preferPublishedRdp, out shareKey, out pcName);
                return;
            }

            if (_remotePcs != null)
            {
                foreach (var pc in _remotePcs)
                {
                    if (pc == null)
                        continue;
                    bool ipMatch = !string.IsNullOrWhiteSpace(pc.IpAddress)
                        && string.Equals(pc.IpAddress.Trim(), shareKey, StringComparison.OrdinalIgnoreCase);
                    bool hostMatch = !string.IsNullOrWhiteSpace(pc.HostAddress)
                        && string.Equals(pc.HostAddress.Trim(), shareKey, StringComparison.OrdinalIgnoreCase);
                    bool nameMatch = !string.IsNullOrWhiteSpace(pc.PcName)
                        && string.Equals(pc.PcName.Trim(), shareKey, StringComparison.OrdinalIgnoreCase);
                    if (!ipMatch && !hostMatch && !nameMatch)
                        continue;

                    if (!preferPublishedRdp && LooksLikeIpAddress(pc.HostAddress))
                        shareKey = pc.HostAddress.Trim();
                    else if (LooksLikeIpAddress(pc.IpAddress))
                        shareKey = pc.IpAddress.Trim();
                    else if (!string.IsNullOrWhiteSpace(pc.IpAddress))
                        shareKey = pc.IpAddress.Trim();

                    pcName = string.IsNullOrWhiteSpace(pc.PcName) ? shareKey : pc.PcName.Trim();
                    return;
                }
            }
        }

        private RemotePcDto FindRemotePcDto(string selected, string siteCode)
        {
            if (string.IsNullOrWhiteSpace(selected) || string.IsNullOrWhiteSpace(siteCode))
                return null;
            if (string.Equals(siteCode, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                return null;

            List<RemotePcDto> list = null;
            try
            {
                list = _remotePcBiz.GetRemotePcListBySite(siteCode);
            }
            catch
            {
                return null;
            }
            if (list == null)
                return null;

            string key = selected.Trim();
            foreach (var dto in list)
            {
                if (dto == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(dto.PcName)
                    && string.Equals(dto.PcName.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return dto;
                if (!string.IsNullOrWhiteSpace(dto.IpAddress)
                    && string.Equals(dto.IpAddress.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return dto;
                if (!string.IsNullOrWhiteSpace(dto.HostAddress)
                    && string.Equals(dto.HostAddress.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return dto;
            }
            return null;
        }

        private static void ApplyResolvedPcTarget(
            RemotePcDto dto,
            bool preferPublishedRdp,
            out string shareKey,
            out string pcName)
        {
            pcName = !string.IsNullOrWhiteSpace(dto.PcName)
                ? dto.PcName.Trim()
                : (dto.IpAddress ?? string.Empty).Trim();

            if (!preferPublishedRdp)
            {
                if (LooksLikeIpAddress(dto.HostAddress))
                {
                    shareKey = dto.HostAddress.Trim();
                    return;
                }
                if (LooksLikeIpAddress(dto.IpAddress))
                {
                    shareKey = dto.IpAddress.Trim();
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.IpAddress))
            {
                shareKey = dto.IpAddress.Trim();
                return;
            }
            if (LooksLikeIpAddress(dto.HostAddress))
            {
                shareKey = dto.HostAddress.Trim();
                return;
            }

            shareKey = pcName;
        }

        private void OnImportDialogPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SessionSite")
                RefreshImportSessionPcOptions();

            if (e.PropertyName == "SelectedCount"
                || e.PropertyName == "CanConfirm"
                || e.PropertyName == "ImportMode"
                || e.PropertyName == "SelectedAppendTarget")
            {
                var cmd = ConfirmImportCommand as RelayCommand;
                if (cmd != null)
                    cmd.RaiseCanExecuteChanged();
            }

            if (e.PropertyName == "CanFetchSessionQuery"
                || e.PropertyName == "SessionSite"
                || e.PropertyName == "SessionPc"
                || e.PropertyName == "SessionDateText"
                || e.PropertyName == "SessionStartTimeText"
                || e.PropertyName == "SessionEndTimeText")
            {
                var dialog = sender as TfsImportDialogViewModel;
                var fetch = dialog != null ? dialog.FetchSessionQueryCommand as RelayCommand : null;
                if (fetch != null)
                    fetch.RaiseCanExecuteChanged();
            }
        }

        private void CloseImport()
        {
            if (ImportDialog != null)
                ImportDialog.PropertyChanged -= OnImportDialogPropertyChanged;
            IsImportOpen = false;
        }

        private void OpenInbox()
        {
            IsImportOpen = false;
            ReconcileInboxFromWorkLogs();
            if (Inbox != null)
                Inbox.Reload();
            RefreshInboxBadge();
            IsInboxOpen = true;
        }

        private void CloseInbox()
        {
            IsInboxOpen = false;
            RefreshInboxBadge();
        }

        private void RefreshInboxBadge()
        {
            if (Inbox != null)
                Inbox.Reload();
            NotifyInboxBadge();
        }

        public void NotifyInboxBadge()
        {
            RaisePropertyChanged("InboxWaitingCount");
            RaisePropertyChanged("HasInboxWaiting");
            RaisePropertyChanged("InboxButtonLabel");
        }

        private void ReconcileInboxFromWorkLogs()
        {
            try
            {
                TfsCheckinInboxStore.ReconcileReported(GetImportedChangesetIds());
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_INBOX", "reconcile failed: " + ex.Message);
            }
        }

        private void RememberImportInboxDecision()
        {
            if (ImportDialog == null)
                return;
            var selected = new List<int>();
            var skipped = new List<int>();
            foreach (var row in ImportDialog.Candidates)
            {
                if (row == null || row.ChangesetId <= 0)
                    continue;
                if (row.IsImportSelected)
                    selected.Add(row.ChangesetId);
                else
                    skipped.Add(row.ChangesetId);
            }
            TfsCheckinInboxStore.ApplyImportDecision(selected, skipped);
            RefreshInboxBadge();
        }

        private void WriteFromInbox(IList<TfsCheckinInboxRecord> records)
        {
            if (records == null || records.Count == 0)
                return;

            var valid = new List<TfsCheckinInboxRecord>();
            foreach (var record in records)
            {
                if (record != null && record.ChangesetId > 0)
                    valid.Add(record);
            }
            if (valid.Count == 0)
                return;

            var mappedParts = new List<WorkLogListItemViewModel>();
            var wantedIds = new List<int>();
            foreach (var record in valid)
            {
                var candidate = new TfsChangesetCandidateViewModel();
                candidate.ApplySourceInfo(null, record.ToChangesetItem(), false);
                candidate.RemoteComputerName = record.PcName;
                candidate.CollectionUrl = record.CollectionUrl;

                var importRow = TfsImportCandidateRow.FromCandidate(candidate, false);
                var ctx = BuildSessionContextForInbox(record);
                var mapped = WorkLogDraftMapper.FromImportRow(importRow, ctx);
                if (mapped == null)
                    continue;
                ApplyInboxRecordMetadata(mapped, record);
                mappedParts.Add(mapped);
                if (mapped.ChangesetId > 0 && !wantedIds.Contains(mapped.ChangesetId))
                    wantedIds.Add(mapped.ChangesetId);
            }

            if (mappedParts.Count == 0)
                return;

            var existing = WorkLogTfsImportCoordinator.FindDraftWithSameChangesets(Items, wantedIds);
            if (existing != null)
            {
                CloseInbox();
                _tfsImport.ClearEditQueue();
                OpenEdit(existing, startInEditMode: true);
                if (EditDialog != null)
                    EditDialog.DiscardOnCancel = existing.DbLogId <= 0;
                return;
            }

            // 다중 선택: Site/PC가 이미 붙은 mappedParts를 합친다.
            // FromImportRowsMerged로 다시 만들면 보관함 SiteCode가 빠진다.
            WorkLogListItemViewModel target = mappedParts.Count == 1
                ? mappedParts[0]
                : WorkLogDraftMapper.MergeMappedParts(
                    mappedParts,
                    BuildSessionContextForInbox(valid[0]));

            if (target == null)
                return;

            ApplyInboxRecordsMetadata(target, valid);
            Items.Add(target);
            RebuildDateGroups();
            RaisePropertyChanged("HasItems");
            RaisePropertyChanged("IsEmpty");
            RaisePropertyChanged("TotalCount");
            CloseInbox();
            _tfsImport.ClearEditQueue();
            OpenEdit(target, startInEditMode: true);
            if (EditDialog != null)
                EditDialog.DiscardOnCancel = target.DbLogId <= 0;
        }

        private void ApplyInboxRecordMetadata(WorkLogListItemViewModel item, TfsCheckinInboxRecord record)
        {
            if (item == null || record == null)
                return;

            string site = TfsCheckinInboxStore.ResolveSiteCode(record);
            if (!string.IsNullOrWhiteSpace(site)
                && !string.Equals(site, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(site, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase))
            {
                item.SiteCode = site.Trim().ToUpperInvariant();
            }
            else
            {
                EnsureItemSiteCode(item);
            }

            if (!string.IsNullOrWhiteSpace(record.PcName))
                item.Pc = record.PcName.Trim();
        }

        private void ApplyInboxRecordsMetadata(
            WorkLogListItemViewModel target,
            IList<TfsCheckinInboxRecord> records)
        {
            if (target == null || records == null || records.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(target.SiteCode)
                || string.Equals(target.SiteCode, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
            {
                string site = null;
                foreach (var record in records)
                {
                    if (record == null)
                        continue;
                    string resolved = TfsCheckinInboxStore.ResolveSiteCode(record);
                    if (string.IsNullOrWhiteSpace(resolved)
                        || string.Equals(resolved, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(resolved, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase))
                        continue;
                    site = resolved.Trim().ToUpperInvariant();
                    break;
                }
                if (!string.IsNullOrWhiteSpace(site))
                    target.SiteCode = site;
                else
                    EnsureItemSiteCode(target);
            }

            if (string.IsNullOrWhiteSpace(target.Pc))
            {
                var pcs = new List<string>();
                foreach (var record in records)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.PcName))
                        continue;
                    string pc = record.PcName.Trim();
                    bool dup = false;
                    foreach (var x in pcs)
                    {
                        if (string.Equals(x, pc, StringComparison.OrdinalIgnoreCase))
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                        pcs.Add(pc);
                }
                if (pcs.Count > 0)
                    target.Pc = string.Join(", ", pcs);
            }
        }

        private WorkSessionContext BuildSessionContextForInbox(TfsCheckinInboxRecord record)
        {
            var ctx = new WorkSessionContext();
            var source = _lastSessionContext
                ?? (_sessionContextFactory != null ? _sessionContextFactory() : null);
            if (source != null)
            {
                ctx.ClientLocalIp = source.ClientLocalIp;
                ctx.CurrentUserId = source.CurrentUserId;
                ctx.CurrentUserName = source.CurrentUserName;
                ctx.SessionStartedAt = source.SessionStartedAt;
                ctx.SessionEndedAt = source.SessionEndedAt;
            }
            if (record != null)
            {
                if (!string.IsNullOrWhiteSpace(record.PcName))
                    ctx.RemoteComputerName = record.PcName.Trim();
            }
            WorkLogTfsImportCoordinator.EnsureSessionHasWorkHubUser(ctx);
            return ctx;
        }

        private void ConfirmImport()
        {
            if (!_tfsImport.TryBeginConfirm())
                return;
            if (ImportDialog == null || !ImportDialog.CanConfirm)
            {
                _tfsImport.EndConfirm();
                return;
            }

            var batches = ImportDialog.GetSelectedImportBatches();
            if (batches == null || batches.Count == 0)
            {
                _tfsImport.EndConfirm();
                return;
            }

            var confirmCmd = ConfirmImportCommand as RelayCommand;
            if (confirmCmd != null)
                confirmCmd.RaiseCanExecuteChanged();

            try
            {
                var ctx = _lastSessionContext
                    ?? (_sessionContextFactory != null ? _sessionContextFactory() : new WorkSessionContext());

                var apply = _tfsImport.ApplySelectedBatches(
                    batches,
                    ImportDialog.IsAppendExistingMode,
                    ImportDialog.SelectedAppendTarget != null
                        ? ImportDialog.SelectedAppendTarget.Item
                        : null,
                    ctx,
                    Items,
                    EnsureItemSiteCode);

                if (apply.Aborted)
                {
                    if (!string.IsNullOrWhiteSpace(apply.ErrorMessage))
                        SetTicketConnectMessage(apply.ErrorMessage);
                    return;
                }

                RebuildDateGroups();
                RefreshPersonOptions();
                RaisePropertyChanged("HasItems");
                RaisePropertyChanged("IsEmpty");
                RaisePropertyChanged("TotalCount");

                var imported = CollectImportedChangesetIds();
                NewCandidateCount = _tfsCandidates == null
                    ? 0
                    : _tfsCandidates.Count(c => c != null && !imported.Contains(c.ChangesetId));

                if (apply.WasAppendMode)
                {
                    _tfsImport.MarkPromptHandledIfNeeded(apply, "worklog_import_append");
                }
                else
                {
                    DiagnosticLogger.Info(
                        "WORKLOG_IMPORT_CONFIRM",
                        string.Format(
                            "batches={0} addedOrUpdated={1} savedDb=0(deferred) totalItems={2} newCandidatesLeft={3} mode={4} editQueue={5}",
                            batches.Count,
                            apply.AddedOrUpdated,
                            Items.Count,
                            NewCandidateCount,
                            ImportDialog.ImportMode,
                            apply.ItemsToEdit != null ? apply.ItemsToEdit.Count : 0));

                    _tfsImport.MarkPromptHandledIfNeeded(apply, "worklog_import_confirmed");
                }

                RememberImportInboxDecision();
                CloseImport();
                StartImportEditQueue(apply.ItemsToEdit);
            }
            finally
            {
                _tfsImport.EndConfirm();
                if (confirmCmd != null)
                    confirmCmd.RaiseCanExecuteChanged();
            }
        }

        private void StartImportEditQueue(IList<WorkLogListItemViewModel> items)
        {
            WorkLogListItemViewModel next;
            using (_tfsImport.EnterOpeningFromQueueScope())
            {
                next = _tfsImport.StartEditQueue(items, Items);
            }

            if (next == null)
                return;

            OpenImportQueuedEdit(next);
        }

        private void OpenNextImportEdit()
        {
            var next = _tfsImport.TryTakeNextForEdit(Items);
            if (next == null)
                return;

            OpenImportQueuedEdit(next);
        }

        private void OpenImportQueuedEdit(WorkLogListItemViewModel next)
        {
            using (_tfsImport.EnterOpeningFromQueueScope())
            {
                OpenEdit(next, startInEditMode: true);
            }

            if (EditDialog != null)
            {
                // 가져오기 직후 미저장: 취소/닫기 = 목록 반영 철회
                EditDialog.DiscardOnCancel = !_editSession.Persisted;
                BindImportGroupTabs();
            }
        }

        private void RefreshPcOptions()
        {
            // ALL 사이트에서는 PC 필터 자체를 쓰지 않음
            List<string> ips = HasPcFilter
                ? CollectPcIpsForSite(FilterSite)
                : new List<string>();

            if (!HasPcFilter && !string.IsNullOrWhiteSpace(_filterPc))
                FilterPc = string.Empty;

            PcOptions.Clear();
            foreach (string ip in ips)
                PcOptions.Add(ip);

            PcFilterOptions.Clear();
            if (HasPcFilter)
            {
                PcFilterOptions.Add(WorkLogFilterLabels.All);
                foreach (string ip in ips)
                    PcFilterOptions.Add(ip);
            }

            if (!string.IsNullOrWhiteSpace(_filterPc)
                && !PcFilterOptions.Contains(_filterPc))
            {
                FilterPc = string.Empty;
            }
            else
            {
                RaisePropertyChanged("FilterPcSelection");
            }

            RefreshPcFilterAliases();
            RefreshEditDialogPcOptions();
        }

        private void RefreshEditDialogPcOptions(bool clearMissingSelection = false)
        {
            if (EditDialog == null)
                return;
            EditDialog.ReplacePcOptions(CollectPcIpsForSite(EditDialog.SiteCode), clearMissingSelection);
        }

        private List<string> CollectPcIpsForSite(string siteCode)
        {
            var ips = new List<string>();
            if (string.IsNullOrWhiteSpace(siteCode)
                || string.Equals(siteCode, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                return ips;

            string site = siteCode.Trim().ToUpperInvariant();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<RemotePcDto> sitePcs = null;
            try
            {
                sitePcs = _remotePcBiz.GetRemotePcListBySite(site);
            }
            catch
            {
                sitePcs = null;
            }

                if (sitePcs != null)
            {
                foreach (var dto in sitePcs)
                {
                    string chip = ResolvePcFilterChipValue(dto);
                    if (string.IsNullOrWhiteSpace(chip) || !seen.Add(chip))
                        continue;
                    ips.Add(chip);
                }
            }

            try
            {
                IList<PcMapDto> maps = _directory.GetPcMapsBySite(site);
                if (maps != null)
                {
                    foreach (var map in maps)
                    {
                        if (map == null)
                            continue;
                        string chip = !string.IsNullOrWhiteSpace(map.PcName)
                            ? map.PcName.Trim()
                            : map.PcIp;
                        if (string.IsNullOrWhiteSpace(chip) || !seen.Add(chip))
                            continue;
                        ips.Add(chip);
                    }
                }
            }
            catch
            {
            }

            // Fallback: gallery remote PCs for current site (if config empty)
            if (ips.Count == 0 && _remotePcs != null)
            {
                foreach (var pc in _remotePcs)
                {
                    if (pc == null)
                        continue;
                    if (!string.IsNullOrWhiteSpace(pc.SiteCode)
                        && !string.Equals(pc.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string ip = null;
                    if (LooksLikeIpAddress(pc.IpAddress))
                        ip = pc.IpAddress.Trim();
                    else if (!string.IsNullOrWhiteSpace(pc.IpAddress))
                        ip = pc.IpAddress.Trim();
                    if (string.IsNullOrWhiteSpace(ip) || !seen.Add(ip))
                        continue;
                    ips.Add(ip);
                }
            }

            ips.Sort(StringComparer.OrdinalIgnoreCase);
            return ips;
        }

        /// <summary>필터 칩: PC명 우선, 없으면 Host IP·점유키.</summary>
        private static string ResolvePcFilterChipValue(RemotePcDto dto)
        {
            if (dto == null)
                return null;
            if (!string.IsNullOrWhiteSpace(dto.PcName))
                return dto.PcName.Trim();
            if (LooksLikeIpAddress(dto.HostAddress))
                return dto.HostAddress.Trim();
            if (LooksLikeIpAddress(dto.IpAddress))
                return dto.IpAddress.Trim();
            if (!string.IsNullOrWhiteSpace(dto.HostAddress))
                return dto.HostAddress.Trim();
            if (!string.IsNullOrWhiteSpace(dto.IpAddress))
                return dto.IpAddress.Trim();
            return null;
        }

        private static string NormalizeAllFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, WorkLogFilterLabels.All, StringComparison.Ordinal)
                || string.Equals(value, WorkLogSiteCodes.All, StringComparison.Ordinal)
                || string.Equals(value, WorkLogFieldMasters.Unselected, StringComparison.Ordinal))
                return string.Empty;
            return value.Trim();
        }

        private void RebuildCategoryFilterOptions()
        {
            if (CategoryFilterOptions == null)
                return;

            CategoryFilterOptions.Clear();
            CategoryFilterOptions.Add(WorkLogFilterLabels.All);

            string type = FilterType;
            if (string.Equals(type, WorkLogFilterLabels.UnclassifiedType, StringComparison.Ordinal))
            {
                RaisePropertyChanged("HasCategoryFilter");
                if (!string.Equals(FilterCategory, WorkLogFilterLabels.All, StringComparison.Ordinal))
                    FilterCategory = WorkLogFilterLabels.All;
                return;
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(type) || string.Equals(type, WorkLogFilterLabels.All, StringComparison.Ordinal))
            {
                foreach (string t in new[] { "Client", "Server", "EQS", "DB Object", "RebFiles", "Table", "Code" })
                {
                    foreach (string c in WorkLogFieldMasters.CategoriesForType(t))
                    {
                        if (!string.IsNullOrWhiteSpace(c))
                            set.Add(c);
                    }
                }
            }
            else
            {
                foreach (string c in WorkLogFieldMasters.CategoriesForType(type))
                {
                    if (!string.IsNullOrWhiteSpace(c))
                        set.Add(c);
                }
            }

            foreach (string c in set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                CategoryFilterOptions.Add(c);

            RaisePropertyChanged("HasCategoryFilter");
            if (!CategoryFilterOptions.Contains(FilterCategory))
                FilterCategory = WorkLogFilterLabels.All;
        }

        private void RefreshPersonOptions()
        {
            PersonOptions.Clear();

            // 실제 업무기록에 등장한 담당자 이름만 (로컬 IP는 담당자 목록에 넣지 않음)
            foreach (var name in Items
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.PersonInCharge))
                .SelectMany(i => WorkLogDraftMapper.SplitMultiValues(i.PersonInCharge))
                .Where(n => !LooksLikeIpAddress(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n))
            {
                if (!PersonOptions.Contains(name))
                    PersonOptions.Add(name);
            }
        }

        private static bool LooksLikeIpAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            int n;
            return parts.All(p => int.TryParse(p, out n) && n >= 0 && n <= 255);
        }

        private async Task RefreshTeamFilterOptionsAsync()
        {
            if (TeamFilterOptions == null)
                return;

            int gen = ++_teamOptionsGeneration;
            IList<string> names = null;
            try
            {
                names = await _persistence.GetDistinctTeamNamesAsync().ConfigureAwait(true);
            }
            catch
            {
                names = null;
            }
            if (gen != _teamOptionsGeneration)
                return;

            string keep = FilterTeam;
            _suppressTeamFilterChanged = true;
            try
            {
                TeamFilterOptions.Clear();
                TeamFilterOptions.Add(WorkLogFilterLabels.All);
                if (names != null)
                {
                    foreach (string name in names)
                        AddTeamFilterOption(name);
                }
                AddTeamFilterOption(OccupancyNameStore.TryGetAffiliation());
                if (!IsFilterAll(keep))
                    AddTeamFilterOption(keep);

                if (!WorkLogTeamNames.EqualsKey(_filterTeam, keep)
                    && !string.Equals(_filterTeam, keep, StringComparison.Ordinal))
                {
                    _filterTeam = string.IsNullOrWhiteSpace(keep) ? WorkLogFilterLabels.All : keep;
                    RaisePropertyChanged("FilterTeam");
                }
            }
            finally
            {
                _suppressTeamFilterChanged = false;
            }

            RaisePropertyChanged("HasTeamFilter");
            RaiseFilterSummaryChanged();
        }

        private void AddTeamFilterOption(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || TeamFilterOptions == null)
                return;
            string team = name.Trim();
            if (TeamFilterOptions.Any(n => WorkLogTeamNames.EqualsKey(n, team)))
                return;
            TeamFilterOptions.Add(team);
        }

        private System.Collections.Generic.IEnumerable<WorkLogListItemViewModel> GetFilteredItems()
        {
            DateTime? from = null;
            DateTime? to = null;
            if (AreBothDateFiltersComplete())
            {
                from = WorkLogPersistenceService.ParseFilterDateOrNull(FilterFromText);
                to = WorkLogPersistenceService.ParseFilterDateOrNull(FilterToText);
            }

            return Items.Where(item =>
            {
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string q = SearchText.Trim();
                    bool hit = (item.TicketNo ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.TicketContents ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.TfsComment ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.AuthorDisplayName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.TeamName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.PersonInCharge ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.MenuName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.Comment ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.ListCommentFullText ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.Pc ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.LocalPcIp ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.SiteCode ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.AuthorName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.TfsAuthor ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.ListPersonText ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.ListDeployText ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || item.ChangesetId.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || ItemMatchesPcFilter(item, _searchPcAliases, q)
                        || (item.Groups != null && item.Groups.Any(g =>
                            g != null
                            && ((g.ProjectName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                || (g.Comment ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)));
                    if (!hit)
                        return false;
                }

                DateTime? filterDay = item.DeploymentDate
                    ?? item.CheckedInAt
                    ?? item.StartDate
                    ?? item.TimelineDate
                    ?? (item.Groups != null
                        ? item.Groups.Where(g => g != null && g.DeploymentDate.HasValue)
                            .Select(g => g.DeploymentDate)
                            .FirstOrDefault()
                        : null);
                if (from.HasValue && filterDay.HasValue && filterDay.Value.Date < from.Value.Date)
                    return false;
                if (to.HasValue && filterDay.HasValue && filterDay.Value.Date > to.Value.Date)
                    return false;
                if ((from.HasValue || to.HasValue) && !filterDay.HasValue)
                    return false;

                if (!string.IsNullOrWhiteSpace(FilterSite)
                    && !string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.Ordinal)
                    && !string.Equals(item.SiteCode ?? string.Empty, FilterSite, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(FilterPc)
                    && FilterPc != WorkLogFieldMasters.Unselected)
                {
                    if (!ItemMatchesPcFilter(item, _pcFilterAliases, FilterPc))
                        return false;
                }

                // 담당자는 상세 수기 입력 → 필터 제외, 검색으로만 처리

                if (!string.IsNullOrWhiteSpace(FilterType)
                    && !string.Equals(FilterType, WorkLogFilterLabels.All, StringComparison.Ordinal))
                {
                    if (string.Equals(FilterType, WorkLogFilterLabels.UnclassifiedType, StringComparison.Ordinal))
                    {
                        if (item.TypeBadges == null || !item.TypeBadges.Any(b => string.IsNullOrEmpty(b.Type)))
                            return false;
                    }
                    else if (!ItemMatchesTypeColumn(item, FilterType))
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(FilterCategory)
                    && !string.Equals(FilterCategory, WorkLogFilterLabels.All, StringComparison.Ordinal))
                {
                    bool catHit = item.Groups != null && item.Groups.Any(g =>
                    {
                        if (g == null)
                            return false;
                        if (!string.Equals(g.Category ?? string.Empty, FilterCategory, StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (!string.IsNullOrWhiteSpace(FilterType)
                            && !string.Equals(FilterType, WorkLogFilterLabels.All, StringComparison.Ordinal)
                            && !string.Equals(FilterType, WorkLogFilterLabels.UnclassifiedType, StringComparison.Ordinal)
                            && !string.Equals(g.Type ?? string.Empty, FilterType, StringComparison.OrdinalIgnoreCase))
                            return false;
                        return true;
                    });
                    if (!catHit)
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(FilterDeploy)
                    && !string.Equals(FilterDeploy, WorkLogFilterLabels.All, StringComparison.Ordinal))
                {
                    bool deployHit =
                        DeployStatusEquals(item.ListDeployText, FilterDeploy)
                        || (item.Groups != null && item.Groups.Any(g =>
                            g != null && DeployStatusEquals(g.DeploymentStatus, FilterDeploy)));
                    if (!deployHit)
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(FilterTeam)
                    && !string.Equals(FilterTeam, WorkLogFilterLabels.All, StringComparison.Ordinal))
                {
                    if (!ItemMatchesTeam(item, FilterTeam))
                        return false;
                }

                // 목록에는 저장(작성 완료)만. 임시저장은 보관함 재개용으로만 유지.
                if (!item.IsCompleted)
                    return false;

                return true;
            });
        }

        private void RebuildDateGroups()
        {
            // DB 페이지 + 클라이언트 소속 매칭(TEAM_NM 미기록·작성자 문자열 폴백). Skip/Take 없음.
            DateGroups.Clear();
            var pageItems = GetFilteredItems()
                .Where(i => i != null)
                .OrderByDescending(i => i.SortCheckedInAt)
                .ThenByDescending(i => i.DbLogId)
                .ToList();

            var known = pageItems
                .Where(i => i.TimelineDate.HasValue)
                .GroupBy(i => i.TimelineDate.Value.Date)
                .OrderByDescending(g => g.Key);

            foreach (var g in known)
            {
                var dateGroup = new WorkLogDateGroupViewModel { Date = g.Key, IsUnknownDate = false };
                foreach (var item in g.OrderByDescending(i => i.SortCheckedInAt))
                    dateGroup.Items.Add(item);
                DateGroups.Add(dateGroup);
            }

            var unknown = pageItems.Where(i => !i.TimelineDate.HasValue)
                .OrderByDescending(i => i.SortCheckedInAt)
                .ToList();
            if (unknown.Count > 0)
            {
                var unknownGroup = new WorkLogDateGroupViewModel
                {
                    Date = DateTime.MinValue,
                    IsUnknownDate = true
                };
                foreach (var item in unknown)
                    unknownGroup.Items.Add(item);
                DateGroups.Add(unknownGroup);
            }

            RebuildPageNumbers();
            RaisePropertyChanged("TotalCount");
            RaisePropertyChanged("CurrentPage");
            RaisePropertyChanged("TotalPages");
            RaisePropertyChanged("PageInfoText");
            RaisePropertyChanged("ShowPagination");
            RaisePropertyChanged("CanGoPrevPage");
            RaisePropertyChanged("CanGoNextPage");
            RaisePropertyChanged("HasItems");
            RaisePropertyChanged("IsEmpty");
            RaisePropertyChanged("ShowEmptyState");
            RaisePropertyChanged("ShowListContent");
            var prev = PrevPageCommand as RelayCommand;
            if (prev != null)
                prev.RaiseCanExecuteChanged();
            var next = NextPageCommand as RelayCommand;
            if (next != null)
                next.RaiseCanExecuteChanged();
            var go = GoToPageCommand as RelayCommand<object>;
            if (go != null)
                go.RaiseCanExecuteChanged();
        }

        private void RebuildPageNumbers()
        {
            PageNumbers.Clear();
            int total = TotalPages;
            int current = CurrentPage;
            if (total <= 1)
                return;

            // 최대 7칸: 1 … 4 5 6 … 20 형태
            var pages = BuildVisiblePageList(current, total, maxSlots: 7);
            foreach (int p in pages)
            {
                PageNumbers.Add(new WorkLogPageNumberItem
                {
                    PageNumber = p,
                    IsCurrent = p > 0 && p == current
                });
            }
        }

        /// <summary>1-based page list. 0 = ellipsis. 예: 1 … 4 5 6 … 20</summary>
        private static List<int> BuildVisiblePageList(int current, int total, int maxSlots)
        {
            var result = new List<int>();
            if (total <= maxSlots)
            {
                for (int i = 1; i <= total; i++)
                    result.Add(i);
                return result;
            }

            int window = maxSlots - 2;
            if (window < 3)
                window = 3;

            int start = current - window / 2;
            int end = start + window - 1;
            if (start < 2)
            {
                start = 2;
                end = start + window - 1;
            }
            if (end > total - 1)
            {
                end = total - 1;
                start = end - window + 1;
            }
            if (start < 2)
                start = 2;

            result.Add(1);
            if (start > 2)
                result.Add(0);
            for (int i = start; i <= end; i++)
                result.Add(i);
            if (end < total - 1)
                result.Add(0);
            result.Add(total);
            return result;
        }

        private void OnFilterChanged()
        {
            _pageIndex = 0;
            RaiseFilterSummaryChanged();
            var ignored = LoadPageFromDbAsync();
        }

        private void RaiseFilterSummaryChanged()
        {
            RaisePropertyChanged("HasActiveFilters");
            RaisePropertyChanged("IsTypeFilterActive");
            RaisePropertyChanged("IsCategoryFilterActive");
            RaisePropertyChanged("IsDeployFilterActive");
            RaisePropertyChanged("IsTeamFilterActive");
            RaisePropertyChanged("IsPcFilterActive");
            RaisePropertyChanged("IsDateFilterActive");
            RaisePropertyChanged("TypeFilterPillLabel");
            RaisePropertyChanged("CategoryFilterPillLabel");
            RaisePropertyChanged("DeployFilterPillLabel");
            RaisePropertyChanged("TeamFilterPillLabel");
            RaisePropertyChanged("PcFilterPillLabel");
            RaisePropertyChanged("DateFilterPillLabel");
            RaisePropertyChanged("FilterDateSummary");
        }

        private async void ScheduleSearchFilterChanged()
        {
            int gen = ++_searchDebounceGeneration;
            try
            {
                await Task.Delay(350).ConfigureAwait(true);
            }
            catch
            {
                return;
            }
            if (gen != _searchDebounceGeneration)
                return;
            OnFilterChanged();
        }

        /// <summary>
        /// 날짜 필드가 각각 비움/완성 상태로 안정된 뒤에만 DB 조회.
        /// 실제 기간 필터는 양쪽 모두 yyyy-MM-dd 완성일 때만 BuildListQuery에 포함.
        /// </summary>
        private async void ScheduleDateFilterChanged()
        {
            int gen = ++_dateDebounceGeneration;
            try
            {
                await Task.Delay(280).ConfigureAwait(true);
            }
            catch
            {
                return;
            }
            if (gen != _dateDebounceGeneration)
                return;

            // 입력 중(예: 2026-0)이면 대기. 한쪽만 완성된 경우에도 조회해 이전 기간 필터를 해제한다.
            if (!IsDateFilterTextReady(FilterFromText) || !IsDateFilterTextReady(FilterToText))
                return;

            OnFilterChanged();
        }

        private static bool IsDateFilterTextReady(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;
            return IsCompleteFilterDate(text);
        }

        private static bool IsCompleteFilterDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length != 10)
                return false;
            DateTime? parsed;
            string err;
            return WorkLogDraftMapper.TryParseDateText(text, out parsed, out err) && parsed.HasValue;
        }

        private bool AreBothDateFiltersComplete()
        {
            return IsCompleteFilterDate(FilterFromText) && IsCompleteFilterDate(FilterToText);
        }

        private void GoPrevPage()
        {
            if (!CanGoPrevPage)
                return;
            _pageIndex--;
            var ignored = LoadPageFromDbAsync();
        }

        private void GoNextPage()
        {
            if (!CanGoNextPage)
                return;
            _pageIndex++;
            var ignored = LoadPageFromDbAsync();
        }

        private bool CanGoToPage(object parameter)
        {
            int page = ParsePageParameter(parameter);
            return page >= 1 && page <= TotalPages && page != CurrentPage;
        }

        private void GoToPage(object parameter)
        {
            int page = ParsePageParameter(parameter);
            if (page < 1 || page > TotalPages || page == CurrentPage)
                return;
            _pageIndex = page - 1;
            var ignored = LoadPageFromDbAsync();
        }

        private WorkLogListQuery BuildListQuery()
        {
            // 기간은 시작·종료 모두 작성 완료(yyyy-MM-dd)일 때만 적용. 검색어와 다른 필터는 AND로 동시 적용.
            DateTime? from = null;
            DateTime? to = null;
            if (AreBothDateFiltersComplete())
            {
                from = WorkLogPersistenceService.ParseFilterDateOrNull(FilterFromText);
                to = WorkLogPersistenceService.ParseFilterDateOrNull(FilterToText);
            }

            var query = _persistence.BuildListQuery(
                _pageIndex,
                PageSize,
                SearchText,
                from,
                to,
                FilterSite,
                FilterPc,
                FilterType,
                FilterCategory,
                FilterDeploy,
                FilterTeam);

            RefreshPcFilterAliases();
            if (_pcFilterAliases != null && _pcFilterAliases.Count > 0)
                query.PcAliases = _pcFilterAliases;

            try
            {
                _searchPcAliases = string.IsNullOrWhiteSpace(SearchText)
                    ? new List<string>()
                    : (_directory.ResolvePcAliasesContaining(FilterSite, SearchText) ?? new List<string>());
            }
            catch
            {
                _searchPcAliases = new List<string>();
            }
            if (_searchPcAliases.Count > 0)
                query.SearchAliases = _searchPcAliases;

            if (!string.IsNullOrWhiteSpace(query.TeamName)
                && WorkLogTeamNames.EqualsKey(query.TeamName, OccupancyNameStore.TryGetAffiliation()))
            {
                string occ = OccupancyNameStore.TryGet();
                if (!string.IsNullOrWhiteSpace(occ))
                    query.OccupancyAuthorName = occ;
                string ip = WorkHubUserProfile.LocalIp;
                if (!string.IsNullOrWhiteSpace(ip))
                    query.OccupancyLocalPcIp = ip;
            }

            return query;
        }

        /// <summary>Type 컬럼(TYPE_NM)만 매칭.</summary>
        private static bool ItemMatchesTypeColumn(WorkLogListItemViewModel item, string type)
        {
            if (item == null || string.IsNullOrWhiteSpace(type))
                return false;
            if (item.TypeBadges != null
                && item.TypeBadges.Any(b =>
                    b != null && string.Equals(b.Type, type, StringComparison.OrdinalIgnoreCase)))
                return true;
            return item.Groups != null && item.Groups.Any(g =>
                g != null && string.Equals(g.Type ?? string.Empty, type, StringComparison.OrdinalIgnoreCase));
        }

        private static bool DeployStatusEquals(string stored, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;
            if (string.IsNullOrWhiteSpace(stored))
                return false;
            string a = stored.Replace(" ", string.Empty);
            string b = filter.Replace(" ", string.Empty);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// TEAM_NM, 작성자/담당자 문자열, 점유 소속 오버레이까지 포함해 소속 칩과 맞춘다.
        /// </summary>
        private static bool ItemMatchesTeam(WorkLogListItemViewModel item, string filter)
        {
            if (item == null || string.IsNullOrWhiteSpace(filter))
                return true;
            if (WorkLogTeamNames.EqualsKey(item.TeamName, filter)
                || WorkLogTeamNames.ContainsKey(item.TeamName, filter)
                || WorkLogTeamNames.ContainsKey(item.AuthorName, filter)
                || WorkLogTeamNames.ContainsKey(item.PersonInCharge, filter)
                || WorkLogTeamNames.ContainsKey(item.TfsAuthor, filter)
                || WorkLogTeamNames.ContainsKey(item.ListPersonText, filter)
                || WorkLogTeamNames.ContainsKey(item.AuthorDisplayName, filter))
                return true;

            if (!WorkLogTeamNames.EqualsKey(OccupancyNameStore.TryGetAffiliation(), filter))
                return false;
            if (!item.IsOwnedByCurrentUser)
                return false;
            return string.IsNullOrWhiteSpace(item.TeamName)
                || WorkLogTeamNames.EqualsKey(item.TeamName, filter);
        }

        private void RefreshPcFilterAliases()
        {
            try
            {
                _pcFilterAliases = _directory.ResolvePcAliases(FilterSite, FilterPc) ?? new List<string>();
            }
            catch
            {
                _pcFilterAliases = new List<string>();
                if (!string.IsNullOrWhiteSpace(FilterPc))
                    _pcFilterAliases.Add(FilterPc.Trim());
            }
        }

        private static bool ItemMatchesPcFilter(
            WorkLogListItemViewModel item,
            IList<string> aliases,
            string filter)
        {
            if (item == null)
                return false;
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Count; i++)
                {
                    string a = aliases[i];
                    if (string.IsNullOrWhiteSpace(a))
                        continue;
                    if (string.Equals(item.Pc, a, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.LocalPcIp, a, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return string.Equals(item.Pc, filter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.LocalPcIp, filter, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadPageFromDbAsync()
        {
            if (!_persistence.IsConfigured)
            {
                _serverTotalCount = 0;
                Items.Clear();
                RebuildDateGroups();
                return;
            }

            int gen = ++_pageLoadGeneration;
            IsLoading = true;
            try
            {
                if (!_didFillMissingTeam)
                {
                    string occName = OccupancyNameStore.TryGet();
                    string occTeam = OccupancyNameStore.TryGetAffiliation();
                    if (!string.IsNullOrWhiteSpace(occName) && !string.IsNullOrWhiteSpace(occTeam))
                    {
                        _didFillMissingTeam = true;
                        await OccupancyNamePrompt.StampTeamOnLocalLogsAsync().ConfigureAwait(true);
                    }
                }

                var page = await _persistence.GetPageAsync(BuildListQuery()).ConfigureAwait(true);
                if (gen != _pageLoadGeneration)
                    return;

                // 아직 DB 미저장 초안(가져오기 직후 등)은 페이지 갱신 시 유지
                var drafts = Items
                    .Where(i => i != null && i.DbLogId <= 0)
                    .ToList();

                Items.Clear();
                foreach (var draft in drafts)
                    Items.Add(draft);

                if (page != null && page.Items != null)
                {
                    foreach (var dto in page.Items)
                    {
                        var item = WorkLogDbMapper.FromDto(dto);
                        if (item != null)
                            Items.Add(item);
                    }
                    _serverTotalCount = page.TotalCount;
                }
                else
                {
                    _serverTotalCount = 0;
                }

                int pages = _serverTotalCount <= 0
                    ? 0
                    : (_serverTotalCount + PageSize - 1) / PageSize;
                if (pages > 0 && _pageIndex >= pages)
                    _pageIndex = pages - 1;
                if (_pageIndex < 0)
                    _pageIndex = 0;

                RebuildDateGroups();
                RefreshPersonOptions();
                var ignoredTeams = RefreshTeamFilterOptionsAsync();
            }
            catch (Exception ex)
            {
                if (gen != _pageLoadGeneration)
                    return;
                DiagnosticLogger.Warn("WORKLOG_DB", "page load failed: " + ex.Message);
                SetTicketConnectMessage("업무기록 DB 조회 실패: " + ex.Message);
            }
            finally
            {
                if (gen == _pageLoadGeneration)
                    IsLoading = false;
            }
        }

        private static int ParsePageParameter(object parameter)
        {
            if (parameter == null)
                return 0;
            if (parameter is int)
                return (int)parameter;
            if (parameter is WorkLogPageNumberItem)
                return ((WorkLogPageNumberItem)parameter).PageNumber;

            int parsed;
            if (int.TryParse(parameter.ToString(), out parsed))
                return parsed;
            return 0;
        }

        private static readonly string[] SitePickerAllCodes =
        {
            "ALL",
            WorkLogSiteCodes.Aurora,
            WorkLogSiteCodes.Rc,
            WorkLogSiteCodes.Cmc,
            WorkLogSiteCodes.Mngha
        };

        /// <summary>현재 선택 사이트는 제외하고 나머지 옵션만 버블에 표시.</summary>
        private void RebuildSitePickerOptions()
        {
            string current = SiteDisplayName;
            SitePickerOptions.Clear();
            for (int i = 0; i < SitePickerAllCodes.Length; i++)
            {
                string code = SitePickerAllCodes[i];
                if (string.Equals(code, current, StringComparison.OrdinalIgnoreCase))
                    continue;
                SitePickerOptions.Add(code);
            }
        }

        private void SelectSiteFromPicker(object parameter)
        {
            string site = parameter as string;
            if (string.IsNullOrWhiteSpace(site))
                return;

            site = site.Trim();
            if (string.Equals(site, "ALL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(site, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                FilterSite = WorkLogSiteCodes.All;
            else
                FilterSite = site.ToUpperInvariant();

            IsSitePickerOpen = false;
        }

        private void ResetFilters()
        {
            _searchText = string.Empty;
            _filterFromText = string.Empty;
            _filterToText = string.Empty;
            _filterPc = string.Empty;
            _filterPerson = string.Empty;
            _filterWriteStatus = WorkLogFilterLabels.All;
            _filterType = WorkLogFilterLabels.All;
            _filterCategory = WorkLogFilterLabels.All;
            _filterDeploy = WorkLogFilterLabels.All;
            _filterTeam = WorkLogFilterLabels.All;
            _filterSite = string.IsNullOrWhiteSpace(_activeSiteCode)
                ? WorkLogSiteCodes.All
                : _activeSiteCode;
            RaisePropertyChanged("SearchText");
            RaisePropertyChanged("FilterFromText");
            RaisePropertyChanged("FilterToText");
            RaisePropertyChanged("FilterDateSummary");
            RaisePropertyChanged("FilterPc");
            RaisePropertyChanged("FilterPcSelection");
            RaisePropertyChanged("FilterPerson");
            RaisePropertyChanged("FilterWriteStatus");
            RaisePropertyChanged("FilterType");
            RaisePropertyChanged("FilterCategory");
            RaisePropertyChanged("FilterDeploy");
            RaisePropertyChanged("FilterTeam");
            RaisePropertyChanged("FilterSite");
            RaisePropertyChanged("HasPcFilter");
            RaisePropertyChanged("HasTeamFilter");
            RaisePropertyChanged("SiteHeaderText");
            RaisePropertyChanged("SiteDisplayName");
            RaisePropertyChanged("HeaderSiteName");
            RaiseFilterSummaryChanged();
            RebuildCategoryFilterOptions();
            RefreshPcOptions();
            _pageIndex = 0;
            var ignored = LoadPageFromDbAsync();
        }

        /// <summary>저장/가져오기용 사이트. 필터 또는 활성 사이트.</summary>
        private string ResolveSiteCodeForWrite()
        {
            if (!string.IsNullOrWhiteSpace(FilterSite)
                && !string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.Ordinal))
                return FilterSite.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(_activeSiteCode))
                return _activeSiteCode.Trim().ToUpperInvariant();
            return null;
        }

        private void EnsureItemSiteCode(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;
            if (!string.IsNullOrWhiteSpace(item.SiteCode))
            {
                item.SiteCode = item.SiteCode.Trim().ToUpperInvariant();
                return;
            }
            item.SiteCode = ResolveSiteCodeForWrite();
        }

        private void SelectItem(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;
            HasTicketConnectMessage = false;
            // 초안: 바로 수정 가능. 작성 완료: 조회만 (본인은 수정 버튼).
            OpenEdit(item, startInEditMode: item.IsDraft);
        }

        private void ToggleDetailNode(WorkLogTreeNodeBase node)
        {
            if (node == null)
                return;
            node.IsExpanded = !node.IsExpanded;
        }

        private void ConnectTicketUi(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;
            HasTicketConnectMessage = true;
            TicketConnectMessage = "Ticket Number를 오른쪽에서 입력하세요. 외부 조회는 호출하지 않습니다.";
            OpenEdit(item, startInEditMode: item.CanEditByCurrentUser);
        }

        private void CreateFromSelected()
        {
            if (SelectedItem != null)
                OpenEdit(SelectedItem, startInEditMode: SelectedItem.CanEditByCurrentUser);
        }

        private void OpenNew()
        {
            _tfsImport.ClearEditQueue();
            var ctx = _sessionContextFactory != null ? _sessionContextFactory() : new WorkSessionContext();
            WorkLogTfsImportCoordinator.EnsureSessionHasWorkHubUser(ctx);
            string localIp = ctx != null ? ctx.ResolveAuthorLocalIp() : WorkHubUserProfile.LocalIp;
            if (string.IsNullOrWhiteSpace(localIp))
                localIp = WorkHubUserProfile.LocalIp;
            var blank = new WorkLogListItemViewModel
            {
                Id = "wl-new-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                SiteCode = null,
                WriteStatus = WorkLogWriteStatus.Draft,
                LastModifiedAt = DateTime.Now,
                CheckedInAt = null,
                Pc = string.Empty,
                PersonInCharge = string.Empty,
                AuthorName = WorkHubUserProfile.OccupancyName,
                TeamName = OccupancyNameStore.TryGetAffiliation(),
                LocalPcIp = localIp,
                StartDate = ctx != null ? ctx.SessionStartedAt : null,
                EndDate = ctx != null ? ctx.SessionEndedAt : null,
                NeedsTicketReview = true
            };
            _editSession.BeginNew(blank);
            OpenDialog(WorkLogEditDialogViewModel.FromListItem(
                blank, true, startInEditMode: true, PcOptions));
        }

        private void OpenEdit(WorkLogListItemViewModel item, bool startInEditMode)
        {
            if (item == null)
                return;
            if (_selectedItem != item)
            {
                if (_selectedItem != null)
                    _selectedItem.IsSelected = false;
                _selectedItem = item;
                _selectedItem.IsSelected = true;
                RaisePropertyChanged("SelectedItem");
                RaisePropertyChanged("HasSelection");
                ((RelayCommand)CreateWorkLogCommand).RaiseCanExecuteChanged();
                ((RelayCommand)EditSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ClassifyWorkItemsCommand).RaiseCanExecuteChanged();
            }

            _editSession.Begin(item, _tfsImport.GetBaseline(item.Id));
            OpenDialog(WorkLogEditDialogViewModel.FromListItem(
                item, _editSession.IsNew, startInEditMode, PcOptions));
            if (_tfsImport.OpeningFromImportQueue && EditDialog != null)
                EditDialog.DiscardOnCancel = true;
        }

        private void OpenDialog(WorkLogEditDialogViewModel vm)
        {
            if (EditDialog != null)
            {
                EditDialog.CloseRequested -= OnEditCloseRequested;
                EditDialog.ApplyRequested -= OnEditApplyRequested;
                EditDialog.DeleteRequested -= OnEditDeleteRequested;
                EditDialog.ImportGroupSelected -= OnImportGroupSelected;
                EditDialog.PropertyChanged -= OnEditDialogPropertyChanged;
            }
            EditDialog = vm;
            EditDialog.CloseRequested += OnEditCloseRequested;
            EditDialog.ApplyRequested += OnEditApplyRequested;
            EditDialog.DeleteRequested += OnEditDeleteRequested;
            EditDialog.ImportGroupSelected += OnImportGroupSelected;
            EditDialog.PropertyChanged += OnEditDialogPropertyChanged;
            IsEditOpen = true;
            DetailTree = vm != null ? vm.Tree : null;
            RefreshEditDialogPcOptions();
            RaisePropertyChanged("HeaderSiteName");
        }

        private void OnEditDialogPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null)
                return;
            if (e.PropertyName == "SiteCode" || e.PropertyName == "DetailSite")
            {
                // 사이트 변경 시에만 마스터에 없는 PC 선택 해제. 상세 열 때는 유지.
                RefreshEditDialogPcOptions(clearMissingSelection: true);
                RaisePropertyChanged("HeaderSiteName");
            }
        }

        private void OnEditDeleteRequested()
        {
            var ignored = DeleteWorkLogAsync(_editSession.EditingItem);
        }

        private async Task DeleteWorkLogAsync(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;
            if (!item.CanDeleteByCurrentUser)
            {
                await ShowDeleteNoticeAsync(
                    "이 업무기록을 삭제할 권한이 없습니다.",
                    "이 PC에서 작성했거나, 체크인을 합쳐 Local PC IP가 기록된 항목만 삭제할 수 있습니다.").ConfigureAwait(true);
                return;
            }

            string ticket = string.IsNullOrWhiteSpace(item.TicketNo) ? "(티켓 없음)" : item.TicketNo.Trim();

            if (_popup != null)
            {
                var result = await _popup.ShowConfirmAsync(new PopupRequest
                {
                    Kind = PopupKind.Confirm,
                    Icon = PopupIconKind.Warning,
                    Title = "업무기록 삭제",
                    Message = "업무기록을 삭제하시겠습니까?",
                    Detail = "티켓: " + ticket,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition("삭제", PopupResultType.Primary, isDefault: true),
                        new PopupButtonDefinition("취소", PopupResultType.Secondary, isCancel: true)
                    }
                }).ConfigureAwait(true);

                if (result == null || !result.IsPrimary)
                    return;
            }
            else
            {
                var confirm = MessageBox.Show(
                    "업무기록을 삭제하시겠습니까?",
                    "업무기록 삭제",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            if (item.DbLogId > 0)
            {
                var deleteResult = await _persistence.DeleteAsync(item.DbLogId).ConfigureAwait(true);
                if (!deleteResult.Success)
                {
                    await ShowDeleteNoticeAsync(
                        string.IsNullOrWhiteSpace(deleteResult.Error)
                            ? "삭제에 실패했습니다."
                            : deleteResult.Error,
                        null).ConfigureAwait(true);
                    return;
                }
            }

            _tfsImport.MarkSlot(item, ImportGroupSlotStatus.Deleted);

            if (ReferenceEquals(_editSession.EditingItem, item) || IsEditOpen)
                CloseEdit(advanceImportQueue: true);

            if (ReferenceEquals(SelectedItem, item))
                SelectedItem = null;

            Items.Remove(item);
            RebuildDateGroups();
            RefreshPersonOptions();
            RaisePropertyChanged("HasItems");
            RaisePropertyChanged("IsEmpty");
            RaisePropertyChanged("TotalCount");
            DiagnosticLogger.Info("WORKLOG_DB", "deleted LogId=" + item.DbLogId + " Ticket=" + ticket);
            await ShowNoticeAsync(
                "업무기록 삭제",
                "삭제되었습니다.",
                PopupIconKind.Success).ConfigureAwait(true);
        }

        private async Task ShowNoticeAsync(string title, string message, PopupIconKind icon)
        {
            if (_popup != null)
            {
                await _popup.ShowResultAsync(new PopupRequest
                {
                    Kind = PopupKind.Result,
                    Icon = icon,
                    Title = title ?? string.Empty,
                    Message = message ?? string.Empty,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true, isCancel: true)
                    }
                }).ConfigureAwait(true);
                return;
            }

            MessageBox.Show(
                message ?? string.Empty,
                title ?? string.Empty,
                MessageBoxButton.OK,
                icon == PopupIconKind.Error || icon == PopupIconKind.Warning
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }

        private async Task ShowDeleteNoticeAsync(string message, string detail)
        {
            if (_popup != null)
            {
                await _popup.ShowResultAsync(new PopupRequest
                {
                    Kind = PopupKind.Result,
                    Icon = PopupIconKind.Warning,
                    Title = "업무기록 삭제",
                    Message = message ?? string.Empty,
                    Detail = detail ?? string.Empty,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true)
                    }
                }).ConfigureAwait(true);
                return;
            }

            MessageBox.Show(
                string.IsNullOrWhiteSpace(detail) ? message : (message + "\n" + detail),
                "업무기록 삭제",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private async void OnEditApplyRequested()
        {
            if (EditDialog == null || _editSession.EditingItem == null)
                return;

            var editingItem = _editSession.EditingItem;
            bool wantComplete = EditDialog.IsCompleted;
            EditDialog.ApplyUiStateTo(editingItem);
            if (!string.IsNullOrWhiteSpace(editingItem.TicketNo))
                editingItem.NeedsTicketReview = false;
            if (_editSession.IsNew && !Items.Contains(editingItem))
            {
                Items.Add(editingItem);
                SelectedItem = editingItem;
            }
            RebuildDateGroups();
            RaisePropertyChanged("HasItems");
            RaisePropertyChanged("IsEmpty");

            bool ok = await PersistItemAsync(editingItem).ConfigureAwait(true);
            if (!ok)
            {
                // 저장 실패 시 편집 모드 유지 + 작성완료 플래그 되돌림
                if (wantComplete)
                {
                    editingItem.WriteStatus = WorkLogWriteStatus.Draft;
                    EditDialog.WriteStatus = WorkLogWriteStatus.Draft;
                }
                string failMsg = TicketConnectMessage
                    ?? "저장에 실패했습니다. 사이트/DB 연결을 확인하세요.";
                EditDialog.RestoreEditModeAfterFailedPersist(failMsg);
                if (_popup != null)
                {
                    await _popup.ShowConfirmAsync(new PopupRequest
                    {
                        Kind = PopupKind.Result,
                        Icon = PopupIconKind.Error,
                        Title = "업무기록 저장 실패",
                        Message = failMsg,
                        Buttons = new[]
                        {
                            new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true, isCancel: true)
                        }
                    }).ConfigureAwait(true);
                }
                return;
            }

            EditDialog.MarkPersistedAfterSave(wantComplete);
            _editSession.MarkPersisted();
            if (editingItem != null && !string.IsNullOrEmpty(editingItem.Id))
                _tfsImport.RemoveBaseline(editingItem.Id);
            if (EditDialog != null)
                EditDialog.DiscardOnCancel = false;
            DetailTree = EditDialog.Tree;
            _tfsImport.MarkSlot(
                editingItem,
                wantComplete ? ImportGroupSlotStatus.Saved : ImportGroupSlotStatus.Draft);

            if (wantComplete)
            {
                await ShowNoticeAsync("업무기록 저장", "저장되었습니다.", PopupIconKind.Success)
                    .ConfigureAwait(true);
                CloseEdit(advanceImportQueue: true);
            }
            else
            {
                RebuildDateGroups();
                await ShowNoticeAsync("임시저장", "임시저장되었습니다.", PopupIconKind.Success)
                    .ConfigureAwait(true);
            }
        }

        private async Task LoadFromDbAsync()
        {
            await ReloadFromDbAsync(force: false).ConfigureAwait(true);
        }

        /// <summary>DB에서 현재 필터·페이지를 다시 로드. force=false면 최초 1회만.</summary>
        public async Task ReloadFromDbAsync(bool force = true)
        {
            if (!force && _dbLoadStarted)
                return;
            _dbLoadStarted = true;

            if (!_persistence.IsConfigured)
            {
                DiagnosticLogger.Info("WORKLOG_DB", "미설정 — 메모리 목록만 사용");
                SetTicketConnectMessage("업무기록 DB 미설정 — App.config GbcWorkHubDb 확인");
                _serverTotalCount = 0;
                Items.Clear();
                RebuildDateGroups();
                RaisePropertyChanged("ShowEmptyState");
                return;
            }

            if (force)
                _pageIndex = 0;

            RefreshPcOptions();
            await LoadPageFromDbAsync().ConfigureAwait(true);
            ReconcileInboxFromWorkLogs();
            RefreshInboxBadge();
            var q = BuildListQuery();
            DiagnosticLogger.Info("WORKLOG_DB",
                "pageLoaded=" + Items.Count
                + " total=" + _serverTotalCount
                + " site=" + (q.SiteCode ?? "-")
                + " write=" + (q.WriteStatus ?? "-")
                + " type=" + (q.Type ?? "-"));
        }

        private async Task<bool> PersistItemAsync(WorkLogListItemViewModel item)
        {
            if (item == null)
                return false;

            EnsureItemSiteCode(item);
            if (string.IsNullOrWhiteSpace(item.SiteCode))
            {
                SetTicketConnectMessage("사이트를 선택해야 저장할 수 있습니다 (필터: AURORA/RC).");
                IsFilterExpanded = true;
                return false;
            }

            item.LastModifiedAt = DateTime.Now;
            var result = await _persistence.SaveAsync(item).ConfigureAwait(true);
            if (result.Success)
            {
                item.DbLogId = result.LogId;
                if (item.IsCompleted)
                {
                    TfsCheckinInboxStore.MarkReported(item.GetEffectiveChangesetIds());
                    RefreshInboxBadge();
                }
                return true;
            }

            SetTicketConnectMessage(result.Error
                ?? "업무기록 DB 미설정 — 저장되지 않았습니다");
            return false;
        }

        private void SetTicketConnectMessage(string message)
        {
            TicketConnectMessage = message;
            HasTicketConnectMessage = !string.IsNullOrWhiteSpace(message);
        }

        private async void OnEditCloseRequested()
        {
            if (_tfsImport.HasUnfinishedImportGroups)
            {
                await StashUnfinishedImportGroupsAsync().ConfigureAwait(true);
                CloseEdit(advanceImportQueue: false);
                _tfsImport.ClearSession();
                return;
            }

            CloseEdit(advanceImportQueue: true);
        }

        private async Task StashUnfinishedImportGroupsAsync()
        {
            if (EditDialog != null && _editSession.EditingItem != null)
                EditDialog.ApplyUiStateTo(_editSession.EditingItem);

            var unfinished = _tfsImport.CollectUnfinishedSlots();
            var savedLabels = new List<string>();
            foreach (var slot in unfinished)
            {
                if (slot == null || slot.Item == null)
                    continue;
                if (slot.Status == ImportGroupSlotStatus.Draft)
                {
                    savedLabels.Add(slot.Label);
                    continue;
                }
                if (slot.Status != ImportGroupSlotStatus.Pending)
                    continue;

                var item = slot.Item;
                item.WriteStatus = WorkLogWriteStatus.Draft;
                if (ReferenceEquals(_editSession.EditingItem, item) && EditDialog != null)
                    EditDialog.WriteStatus = WorkLogWriteStatus.Draft;

                bool ok = await PersistItemAsync(item).ConfigureAwait(true);
                if (ok)
                {
                    slot.Status = ImportGroupSlotStatus.Draft;
                    savedLabels.Add(slot.Label);
                    if (ReferenceEquals(_editSession.EditingItem, item))
                    {
                        _editSession.MarkPersisted();
                        if (EditDialog != null)
                            EditDialog.DiscardOnCancel = false;
                    }
                }
            }

            AfterListDiscardOrRestore();
            if (savedLabels.Count == 0)
                return;

            string names = string.Join(", ", savedLabels);
            await ShowNoticeAsync(
                "업무일지 보관함",
                names + " 일지가 보관함에 저장되었습니다.",
                PopupIconKind.Success).ConfigureAwait(true);
        }

        private void BindImportGroupTabs()
        {
            if (EditDialog == null)
                return;
            EditDialog.ImportGroupSlots = _tfsImport.ImportGroups;
        }

        private void OnImportGroupSelected(ImportGroupSlot slot)
        {
            if (slot == null || !slot.IsVisible || slot.IsCurrent)
                return;
            if (EditDialog != null && _editSession.EditingItem != null)
                EditDialog.ApplyUiStateTo(_editSession.EditingItem);
            _tfsImport.ActivateSlot(slot);
            OpenImportQueuedEdit(slot.Item);
        }

        private void CloseEdit(bool advanceImportQueue = false)
        {
            if (EditDialog != null)
            {
                EditDialog.CloseRequested -= OnEditCloseRequested;
                EditDialog.ApplyRequested -= OnEditApplyRequested;
                EditDialog.DeleteRequested -= OnEditDeleteRequested;
                EditDialog.ImportGroupSelected -= OnImportGroupSelected;
                EditDialog.PropertyChanged -= OnEditDialogPropertyChanged;
            }
            IsEditOpen = false;
            EditDialog = null;
            DetailTree = null;
            RaisePropertyChanged("HeaderSiteName");

            bool discarded = false;
            if (_editSession.IsActive && !_editSession.Persisted)
            {
                discarded = _editSession.TryDiscardUnsaved(
                    Items,
                    AfterListDiscardOrRestore,
                    _tfsImport.RemoveBaseline);
            }
            _editSession.Clear();

            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
                _selectedItem = null;
                RaisePropertyChanged("SelectedItem");
                RaisePropertyChanged("HasSelection");
            }

            if (advanceImportQueue)
            {
                if (discarded && !_tfsImport.HasImportGroups)
                {
                    // 이어서 열려던 미저장 가져오기 건도 목록에서 철회
                    _tfsImport.DiscardRemainingQueueItems(Items, AfterListDiscardOrRestore);
                }
                else
                {
                    OpenNextImportEdit();
                }
            }
        }

        private void AfterListDiscardOrRestore()
        {
            RebuildDateGroups();
            RefreshPersonOptions();
            RaisePropertyChanged("HasItems");
            RaisePropertyChanged("IsEmpty");
            RaisePropertyChanged("TotalCount");

            var imported = CollectImportedChangesetIds();
            NewCandidateCount = _tfsCandidates == null
                ? 0
                : _tfsCandidates.Count(c => c != null && !imported.Contains(c.ChangesetId));
        }
    }
}
