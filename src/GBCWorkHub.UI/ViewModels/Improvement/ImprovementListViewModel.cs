using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using GBCWorkHub.BIZ.Improvement;
using GBCWorkHub.DTO.Improvement;
using GBCWorkHub.UI.Services.Popup;

namespace GBCWorkHub.UI.ViewModels.Improvement
{
    /// <summary>
    /// 개선사항 요청 목록. WorkLogListViewModel과 동일한 관례: 서버사이드 페이징,
    /// IsLoading/ShowEmptyState/ShowListContent 트리오, 인라인 EditDialog(IsEditOpen) 전환.
    /// </summary>
    public sealed class ImprovementListViewModel : ViewModelBase
    {
        private const int DefaultPageSize = 20;

        private readonly ImprovementBiz _biz = new ImprovementBiz();
        private IPopupService _popup;
        private int _pageLoadGeneration;

        private string _searchText = string.Empty;
        private string _filterType = ImprovementTypeLabels.All;
        private string _filterStatus = ImprovementStatusLabels.All;
        private string _filterSite = "ALL";
        private int _pageIndex;
        private int _totalCount;
        private bool _isLoading;
        private bool _isOpeningItem;
        private bool _isEditOpen;
        private ImprovementEditDialogViewModel _editDialog;

        public ImprovementListViewModel()
        {
            Items = new ObservableCollection<ImprovementListItemViewModel>();
            TypeFilterOptions = new ObservableCollection<string>
            {
                ImprovementTypeLabels.All, ImprovementTypeLabels.Bug, ImprovementTypeLabels.Improvement, ImprovementTypeLabels.Etc
            };
            StatusFilterOptions = new ObservableCollection<string>
            {
                ImprovementStatusLabels.All, ImprovementStatusLabels.Open, ImprovementStatusLabels.Checking,
                ImprovementStatusLabels.InProgress, ImprovementStatusLabels.Resolved
            };
            SiteFilterOptions = new ObservableCollection<string> { "ALL", "AURORA", "CMC", "RC", "MNGHA" };

            NewRequestCommand = new RelayCommand(OpenNew);
            SelectItemCommand = new RelayCommand<ImprovementListItemViewModel>(item => { var _ = OpenExistingAsync(item); });
            RefreshCommand = new RelayCommand(() => { var _ = LoadPageAsync(); }, () => !IsLoading && !_isOpeningItem);
            PrevPageCommand = new RelayCommand(() => { _pageIndex--; var _ = LoadPageAsync(); }, () => !IsLoading && !_isOpeningItem && PageIndex > 0);
            NextPageCommand = new RelayCommand(() => { _pageIndex++; var _ = LoadPageAsync(); }, () => !IsLoading && !_isOpeningItem && CanGoNextPage);
            SelectSiteCommand = new RelayCommand<string>(site => FilterSite = site);
            SelectTypeFilterCommand = new RelayCommand<string>(type => FilterType = type);
            SelectStatusFilterCommand = new RelayCommand<string>(status => FilterStatus = status);

            var _1 = LoadPageAsync();
        }

        public void AttachPopup(IPopupService popup)
        {
            _popup = popup;
            if (_editDialog != null)
                _editDialog.AttachPopup(popup);
        }

        public ObservableCollection<ImprovementListItemViewModel> Items { get; private set; }
        public ObservableCollection<string> TypeFilterOptions { get; private set; }
        public ObservableCollection<string> StatusFilterOptions { get; private set; }
        public ObservableCollection<string> SiteFilterOptions { get; private set; }

        public string SearchText
        {
            get { return _searchText; }
            set { if (SetProperty(ref _searchText, value)) OnFilterChanged(); }
        }

        public string FilterType
        {
            get { return _filterType; }
            set
            {
                if (SetProperty(ref _filterType, value))
                {
                    RaisePropertyChanged("HasActiveFilters");
                    RaisePropertyChanged("TypeFilterDisplayLabel");
                    OnFilterChanged();
                }
            }
        }

        /// <summary>기본값(전체)일 때는 필터 알약에 "전체" 대신 항목 이름("유형")을 보여준다.</summary>
        public string TypeFilterDisplayLabel
        {
            get { return string.Equals(FilterType, ImprovementTypeLabels.All, StringComparison.Ordinal) ? "유형" : FilterType; }
        }

        public string FilterStatus
        {
            get { return _filterStatus; }
            set
            {
                if (SetProperty(ref _filterStatus, value))
                {
                    RaisePropertyChanged("HasActiveFilters");
                    RaisePropertyChanged("StatusFilterDisplayLabel");
                    OnFilterChanged();
                }
            }
        }

        public string StatusFilterDisplayLabel
        {
            get { return string.Equals(FilterStatus, ImprovementStatusLabels.All, StringComparison.Ordinal) ? "상태" : FilterStatus; }
        }

        /// <summary>업무일지 사이트 선택 팝업과 동일한 방식(ALL/AURORA/CMC/RC/MNGHA 칩).</summary>
        public string FilterSite
        {
            get { return _filterSite; }
            set { if (SetProperty(ref _filterSite, value)) OnFilterChanged(); }
        }

        public bool HasActiveFilters
        {
            get
            {
                return !string.Equals(FilterType, ImprovementTypeLabels.All, StringComparison.Ordinal)
                    || !string.Equals(FilterStatus, ImprovementStatusLabels.All, StringComparison.Ordinal);
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaisePropertyChanged("ShowEmptyState");
                    RaisePropertyChanged("ShowListContent");
                    var refresh = RefreshCommand as RelayCommand; if (refresh != null) refresh.RaiseCanExecuteChanged();
                    var prev = PrevPageCommand as RelayCommand; if (prev != null) prev.RaiseCanExecuteChanged();
                    var next = NextPageCommand as RelayCommand; if (next != null) next.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ShowEmptyState { get { return !_isLoading && Items.Count == 0; } }
        public bool ShowListContent { get { return !_isLoading && Items.Count > 0; } }

        public int PageIndex { get { return _pageIndex; } }
        public int CurrentPage { get { return _pageIndex + 1; } }
        public int TotalPages { get { return _totalCount <= 0 ? 1 : (int)Math.Ceiling(_totalCount / (double)DefaultPageSize); } }
        public bool CanGoNextPage { get { return CurrentPage < TotalPages; } }
        public string PageInfoText { get { return CurrentPage + " / " + TotalPages + " 페이지 (" + _totalCount + "건)"; } }

        public bool IsEditOpen
        {
            get { return _isEditOpen; }
            private set { SetProperty(ref _isEditOpen, value); }
        }

        public ImprovementEditDialogViewModel EditDialog
        {
            get { return _editDialog; }
            private set { SetProperty(ref _editDialog, value); }
        }

        public ICommand NewRequestCommand { get; private set; }
        public ICommand SelectItemCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand PrevPageCommand { get; private set; }
        public ICommand NextPageCommand { get; private set; }
        /// <summary>
        /// 팝업 열림/닫힘 자체(IsSitePickerOpen/IsFilterExpanded 격) 상태는 일부러 이 ViewModel에 두지
        /// 않는다 — 이 인스턴스는 메인 탭과 관리자("개선사항 관리") 화면 양쪽에서 동시에 같은 View를
        /// 통해 재사용되는데, 그 상태를 여기 두면 한쪽에서 팝업을 열 때 다른 쪽(화면에 안 보이는) 사본의
        /// 팝업도 같이 열려서 엉뚱한 위치에 나타난다. 그래서 팝업 열림 상태는 View(코드비하인드)의
        /// 로컬 상태로 관리하고, 이 커맨드는 값(사이트) 선택만 담당한다.
        /// </summary>
        public ICommand SelectSiteCommand { get; private set; }
        public ICommand SelectTypeFilterCommand { get; private set; }
        public ICommand SelectStatusFilterCommand { get; private set; }

        private void OnFilterChanged()
        {
            _pageIndex = 0;
            var _ = LoadPageAsync();
        }

        private async Task LoadPageAsync()
        {
            if (!_biz.IsConfigured)
            {
                Items.Clear();
                _totalCount = 0;
                RaisePropertyChanged("ShowEmptyState");
                RaisePropertyChanged("ShowListContent");
                return;
            }

            int gen = ++_pageLoadGeneration;
            IsLoading = true;
            try
            {
                var query = new ImprovementListQuery
                {
                    PageIndex = _pageIndex,
                    PageSize = DefaultPageSize,
                    SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                    RequestType = ImprovementTypeCodeFromLabel(FilterType),
                    Status = ImprovementStatusCodeFromLabel(FilterStatus),
                    SiteCode = FilterSite
                };
                var page = await _biz.GetPageAsync(query).ConfigureAwait(true);
                if (gen != _pageLoadGeneration)
                    return;

                Items.Clear();
                if (page != null && page.Items != null)
                    foreach (var dto in page.Items)
                        Items.Add(new ImprovementListItemViewModel(dto));
                _totalCount = page != null ? page.TotalCount : 0;

                RaisePropertyChanged("ShowEmptyState");
                RaisePropertyChanged("ShowListContent");
                RaisePropertyChanged("PageInfoText");
                RaisePropertyChanged("CurrentPage");
                RaisePropertyChanged("TotalPages");
                RaisePropertyChanged("CanGoNextPage");
                var prev = PrevPageCommand as RelayCommand; if (prev != null) prev.RaiseCanExecuteChanged();
                var next = NextPageCommand as RelayCommand; if (next != null) next.RaiseCanExecuteChanged();
            }
            finally
            {
                if (gen == _pageLoadGeneration)
                    IsLoading = false;
            }
        }

        private void OpenNew()
        {
            var dialog = new ImprovementEditDialogViewModel(_biz);
            dialog.AttachPopup(_popup);
            dialog.CloseRequested += OnDialogCloseRequested;
            dialog.DataChanged += OnDialogDataChanged;
            dialog.LoadNew();
            EditDialog = dialog;
            IsEditOpen = true;
        }

        /// <summary>
        /// 항목 클릭 시 첨부 이미지까지 한 번에 불러오는 GetByIdAsync가 눈에 띄게 걸릴 수 있어서,
        /// 커서를 즉시 대기 상태로 바꿔 "클릭이 씹혔다"고 오해하지 않게 하고, 응답이 오기 전까지
        /// 새로고침/페이징/다른 항목 클릭을 막아 늦게 도착한 결과가 엉뚱한 타이밍에 열리지 않게 한다.
        /// </summary>
        private async Task OpenExistingAsync(ImprovementListItemViewModel item)
        {
            if (item == null || _isOpeningItem)
                return;

            _isOpeningItem = true;
            RaiseBusyCommandsChanged();
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var full = await _biz.GetByIdAsync(item.ReqId).ConfigureAwait(true);
                if (full == null)
                    return;

                var dialog = new ImprovementEditDialogViewModel(_biz);
                dialog.AttachPopup(_popup);
                dialog.CloseRequested += OnDialogCloseRequested;
                dialog.DataChanged += OnDialogDataChanged;
                dialog.LoadExisting(full);
                EditDialog = dialog;
                IsEditOpen = true;
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isOpeningItem = false;
                RaiseBusyCommandsChanged();
            }
        }

        private void RaiseBusyCommandsChanged()
        {
            var refresh = RefreshCommand as RelayCommand; if (refresh != null) refresh.RaiseCanExecuteChanged();
            var prev = PrevPageCommand as RelayCommand; if (prev != null) prev.RaiseCanExecuteChanged();
            var next = NextPageCommand as RelayCommand; if (next != null) next.RaiseCanExecuteChanged();
        }

        private void OnDialogCloseRequested()
        {
            CloseDialog();
        }

        private void OnDialogDataChanged()
        {
            var _ = LoadPageAsync();
        }

        private void CloseDialog()
        {
            if (_editDialog != null)
            {
                _editDialog.CloseRequested -= OnDialogCloseRequested;
                _editDialog.DataChanged -= OnDialogDataChanged;
            }
            IsEditOpen = false;
            EditDialog = null;
        }

        private static string ImprovementTypeCodeFromLabel(string label)
        {
            if (string.Equals(label, ImprovementTypeLabels.All, StringComparison.Ordinal)) return null;
            if (string.Equals(label, ImprovementTypeLabels.Bug, StringComparison.Ordinal)) return ImprovementRequestTypes.Bug;
            if (string.Equals(label, ImprovementTypeLabels.Improvement, StringComparison.Ordinal)) return ImprovementRequestTypes.Improvement;
            if (string.Equals(label, ImprovementTypeLabels.Etc, StringComparison.Ordinal)) return ImprovementRequestTypes.Etc;
            return null;
        }

        private static string ImprovementStatusCodeFromLabel(string label)
        {
            if (string.Equals(label, ImprovementStatusLabels.All, StringComparison.Ordinal)) return null;
            if (string.Equals(label, ImprovementStatusLabels.Open, StringComparison.Ordinal)) return ImprovementStatuses.Open;
            if (string.Equals(label, ImprovementStatusLabels.Checking, StringComparison.Ordinal)) return ImprovementStatuses.Checking;
            if (string.Equals(label, ImprovementStatusLabels.InProgress, StringComparison.Ordinal)) return ImprovementStatuses.InProgress;
            if (string.Equals(label, ImprovementStatusLabels.Resolved, StringComparison.Ordinal)) return ImprovementStatuses.Resolved;
            return null;
        }
    }
}
