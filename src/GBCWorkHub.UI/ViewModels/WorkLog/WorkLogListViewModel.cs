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
        private string _filterSite = WorkLogSiteCodes.All;
        private string _activeSiteCode;
        private bool _isEditOpen;
        private bool _isImportOpen;
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
        private const int DefaultPageSize = 30;

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
            CloseEditCommand = new RelayCommand(() => CloseEdit(advanceImportQueue: true));
            CloseDetailCommand = new RelayCommand(() => SelectedItem = null);
            OpenImportCommand = new RelayCommand(OpenImport);
            ImportCsvCommand = new RelayCommand(ImportCsv);
            CloseImportCommand = new RelayCommand(CloseImport, () => IsImportOpen);
            ConfirmImportCommand = new RelayCommand(ConfirmImport,
                () => IsImportOpen
                    && !_tfsImport.IsConfirmingImport
                    && ImportDialog != null
                    && ImportDialog.CanConfirm);
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

        /// <summary>사이트가 ALL이 아닐 때만 PC 필터 표시 (사이트별 IP).</summary>
        public bool HasPcFilter
        {
            get
            {
                return !string.IsNullOrWhiteSpace(FilterSite)
                    && !string.Equals(FilterSite, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool HasItems { get { return Items.Count > 0; } }
        public bool IsEmpty { get { return Items.Count == 0; } }

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
            get { return !_isLoading && Items.Count == 0; }
        }

        /// <summary>로딩 중이 아닐 때만 실제 목록 표시.</summary>
        public bool ShowListContent
        {
            get { return !_isLoading && Items.Count > 0; }
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
                    || IsPcFilterActive
                    || IsDateFilterActive;
            }
        }

        public ICommand OpenFilterMenuCommand { get; private set; }
        public ICommand CloseFilterMenuCommand { get; private set; }
        public ICommand SelectTypeFilterCommand { get; private set; }
        public ICommand SelectCategoryFilterCommand { get; private set; }
        public ICommand SelectDeployFilterCommand { get; private set; }
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
        public ICommand ImportCsvCommand { get; private set; }
        public ICommand CloseImportCommand { get; private set; }
        public ICommand ConfirmImportCommand { get; private set; }
        public ICommand ToggleDetailNodeCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand DeleteWorkLogCommand { get; private set; }

        /// <summary>TFS 동기화 완료 후 업무기록 탭에서 가져오기 팝업 오픈.</summary>
        public void ShowImportDialog()
        {
            OpenImport();
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
                        return new HashSet<int>(fromDb);
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("WORKLOG_CS", "GetImportedChangesetIds DB failed: " + ex.Message);
            }

            // DDL 미적용 등: 완료 상태 목록만 폴백
            var imported = new HashSet<int>();
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

        /// <summary>화면 목록에 이미 있는 CS (가져오기 팝업 "목록에 있음" 표시용).</summary>
        private HashSet<int> CollectImportedChangesetIds()
        {
            var imported = new HashSet<int>();
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
        }

        private void OpenImport()
        {
            OpenImportWithCandidates(_tfsCandidates, _tfsStatusMessage);
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

        private void OnImportDialogPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedCount"
                || e.PropertyName == "CanConfirm"
                || e.PropertyName == "ImportMode"
                || e.PropertyName == "SelectedAppendTarget")
            {
                var cmd = ConfirmImportCommand as RelayCommand;
                if (cmd != null)
                    cmd.RaiseCanExecuteChanged();
            }
        }

        private void CloseImport()
        {
            if (ImportDialog != null)
                ImportDialog.PropertyChanged -= OnImportDialogPropertyChanged;
            IsImportOpen = false;
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
                if (_tfsImport.ImportEditTotal > 1)
                    EditDialog.ImportProgressText = "그룹" + _tfsImport.ImportEditIndex;
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
                    string ip = ResolvePcFilterChipValue(dto);
                    if (string.IsNullOrWhiteSpace(ip) || !seen.Add(ip))
                        continue;
                    ips.Add(ip);
                }
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

        /// <summary>필터 칩: Host IP 우선, 없으면 점유키(IpAddress).</summary>
        private static string ResolvePcFilterChipValue(RemotePcDto dto)
        {
            if (dto == null)
                return null;
            if (LooksLikeIpAddress(dto.HostAddress))
                return dto.HostAddress.Trim();
            if (LooksLikeIpAddress(dto.IpAddress))
                return dto.IpAddress.Trim();
            if (!string.IsNullOrWhiteSpace(dto.IpAddress))
                return dto.IpAddress.Trim();
            if (!string.IsNullOrWhiteSpace(dto.PcName))
                return dto.PcName.Trim();
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
                        || (item.PersonInCharge ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.MenuName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.Comment ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.ListCommentFullText ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || item.ChangesetId.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
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
                    && FilterPc != WorkLogFieldMasters.Unselected
                    && !string.Equals(item.Pc, FilterPc, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item.LocalPcIp, FilterPc, StringComparison.OrdinalIgnoreCase))
                    return false;

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

                return true;
            });
        }

        private void RebuildDateGroups()
        {
            // Items = 현재 DB 페이지 결과(이미 필터·페이징 적용). 클라이언트 Skip/Take 없음.
            DateGroups.Clear();
            var pageItems = Items
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
            RaisePropertyChanged("IsPcFilterActive");
            RaisePropertyChanged("IsDateFilterActive");
            RaisePropertyChanged("TypeFilterPillLabel");
            RaisePropertyChanged("CategoryFilterPillLabel");
            RaisePropertyChanged("DeployFilterPillLabel");
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

            return _persistence.BuildListQuery(
                _pageIndex,
                PageSize,
                SearchText,
                from,
                to,
                FilterSite,
                FilterPc,
                FilterType,
                FilterCategory,
                FilterDeploy);
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
            RaisePropertyChanged("FilterSite");
            RaisePropertyChanged("HasPcFilter");
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
                SiteCode = ResolveSiteCodeForWrite(),
                WriteStatus = WorkLogWriteStatus.Draft,
                LastModifiedAt = DateTime.Now,
                CheckedInAt = null,
                Pc = ctx != null ? ctx.ResolvePc() : string.Empty,
                PersonInCharge = string.Empty,
                AuthorName = string.Empty,
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
                EditDialog.PropertyChanged -= OnEditDialogPropertyChanged;
            }
            EditDialog = vm;
            EditDialog.CloseRequested += OnEditCloseRequested;
            EditDialog.ApplyRequested += OnEditApplyRequested;
            EditDialog.DeleteRequested += OnEditDeleteRequested;
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
                    DedupKey = "WorkLogDelete:" + item.DbLogId + ":" + (item.Id ?? string.Empty),
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

            // 작성 완료(저장) 후: 가져오기 큐가 있으면 다음 기록 화면으로
            if (wantComplete)
                CloseEdit(advanceImportQueue: true);
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

        private void OnEditCloseRequested()
        {
            CloseEdit(advanceImportQueue: true);
        }

        private void CloseEdit(bool advanceImportQueue = false)
        {
            if (EditDialog != null)
            {
                EditDialog.CloseRequested -= OnEditCloseRequested;
                EditDialog.ApplyRequested -= OnEditApplyRequested;
                EditDialog.DeleteRequested -= OnEditDeleteRequested;
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
                if (discarded)
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
