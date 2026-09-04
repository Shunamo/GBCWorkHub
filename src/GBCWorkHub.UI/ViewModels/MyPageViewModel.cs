using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>
    /// 나: 내 업무기록, 체크인 보관함, 내 접속 이력.
    /// 업무기록 탭의 담당자 필터와 별개.
    /// </summary>
    public sealed class MyPageViewModel : ViewModelBase
    {
        private const int WorkLogTake = 200;
        private const int SessionTake = 200;

        private readonly WorkLogListViewModel _workLogList;
        private readonly Action<WorkLogListItemViewModel> _openWorkLog;
        private readonly Func<Task> _changeName;
        private readonly WorkLogPersistenceService _persistence =
            new WorkLogPersistenceService(new GBCWorkHub.BIZ.WorkLog.WorkLogBiz());
        private readonly RemotePcShareBiz _share = new RemotePcShareBiz();
        private readonly List<WorkLogListItemViewModel> _workLogSource =
            new List<WorkLogListItemViewModel>();
        private readonly List<RemotePcUsageLogItemViewModel> _sessionSource =
            new List<RemotePcUsageLogItemViewModel>();
        private bool _isLoading;
        private string _sharedSiteCode = TfsCheckinInboxStore.AllSites;
        private string _workLogWriteFilter = "completed";
        private bool _isWorkLogFilterMenuOpen;
        private string _expandedSection;
        private string _sessionSearchText = string.Empty;
        private bool _isSessionSearchOpen;
        private string _sessionDurationFilter = "all";
        private string _workLogSearchText = string.Empty;
        private bool _isInboxSearchOpen;
        private bool _isWorkLogSearchOpen;
        private DateTime _calendarMonth;
        private DateTime? _draftStartDate;
        private DateTime? _draftEndDate;
        private int _draftStartHour = 9;
        private int _draftStartMinute;
        private int _draftEndHour = 18;
        private int _draftEndMinute;
        private DateTime? _appliedFrom;
        private DateTime? _appliedTo;

        public MyPageViewModel(
            WorkLogListViewModel workLogList,
            Action<WorkLogListItemViewModel> openWorkLog,
            Func<Task> changeName = null)
        {
            if (workLogList == null)
                throw new ArgumentNullException("workLogList");
            _workLogList = workLogList;
            _openWorkLog = openWorkLog;
            _changeName = changeName;
            WorkLogs = new ObservableCollection<WorkLogListItemViewModel>();
            WorkLogDateGroups = new ObservableCollection<WorkLogDateGroupViewModel>();
            Sessions = new ObservableCollection<RemotePcUsageLogItemViewModel>();
            SessionDateGroups = new ObservableCollection<SessionDateGroupViewModel>();
            ReloadCommand = new RelayCommand(() => { var ignored = ReloadAsync(); });
            OpenWorkLogCommand = new RelayCommand<WorkLogListItemViewModel>(OpenWorkLog);
            OpenAllWorkLogsCommand = new RelayCommand(OpenAllWorkLogs);
            ChangeNameCommand = new RelayCommand(() => { var ignored = ChangeNameAsync(); });
            CloseInboxCommand = new RelayCommand(() => { });
            SelectSharedSiteCommand = new RelayCommand<string>(SelectSharedSite);
            ToggleExpandCommand = new RelayCommand<string>(ToggleExpand);
            ToggleWorkLogFilterMenuCommand = new RelayCommand(ToggleWorkLogFilterMenu);
            SetWorkLogWriteFilterCommand = new RelayCommand<string>(SetWorkLogWriteFilter);
            ToggleSessionSearchCommand = new RelayCommand(ToggleSessionSearch);
            SetSessionDurationFilterCommand = new RelayCommand<string>(SetSessionDurationFilter);
            ToggleInboxSearchCommand = new RelayCommand(ToggleInboxSearch);
            ToggleWorkLogSearchCommand = new RelayCommand(ToggleWorkLogSearch);
            PrevCalendarMonthCommand = new RelayCommand(PrevCalendarMonth);
            NextCalendarMonthCommand = new RelayCommand(NextCalendarMonth);
            SelectCalendarDayCommand = new RelayCommand<DateTime>(SelectCalendarDay);
            ApplySessionDateFilterCommand = new RelayCommand(ApplySessionDateFilter);
            ResetSessionDateDraftCommand = new RelayCommand(ResetSessionDateDraft);
            ClearSessionDateFilterCommand = new RelayCommand(ClearSessionDateFilter);
            CalendarDays = new ObservableCollection<CalendarDayItem>();
            _calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            RebuildCalendarDays();
            if (Inbox != null)
                Inbox.PropertyChanged += OnInboxPropertyChanged;
        }

        public TfsCheckinInboxViewModel Inbox
        {
            get { return _workLogList.Inbox; }
        }

        public bool ShowInboxCloseButton
        {
            get { return false; }
        }

        public bool ShowInboxSiteTabs
        {
            get { return false; }
        }

        public ObservableCollection<WorkLogListItemViewModel> WorkLogs { get; private set; }
        public ObservableCollection<WorkLogDateGroupViewModel> WorkLogDateGroups { get; private set; }
        public ObservableCollection<RemotePcUsageLogItemViewModel> Sessions { get; private set; }
        public ObservableCollection<SessionDateGroupViewModel> SessionDateGroups { get; private set; }
        public ObservableCollection<CalendarDayItem> CalendarDays { get; private set; }

        public ICommand ReloadCommand { get; private set; }
        public ICommand OpenWorkLogCommand { get; private set; }
        public ICommand OpenAllWorkLogsCommand { get; private set; }
        public ICommand ChangeNameCommand { get; private set; }
        public ICommand CloseInboxCommand { get; private set; }
        public ICommand SelectSharedSiteCommand { get; private set; }
        public ICommand ToggleExpandCommand { get; private set; }
        public ICommand ToggleWorkLogFilterMenuCommand { get; private set; }
        public ICommand SetWorkLogWriteFilterCommand { get; private set; }
        public ICommand ToggleSessionSearchCommand { get; private set; }
        public ICommand SetSessionDurationFilterCommand { get; private set; }
        public ICommand ToggleInboxSearchCommand { get; private set; }
        public ICommand ToggleWorkLogSearchCommand { get; private set; }
        public ICommand PrevCalendarMonthCommand { get; private set; }
        public ICommand NextCalendarMonthCommand { get; private set; }
        public ICommand SelectCalendarDayCommand { get; private set; }
        public ICommand ApplySessionDateFilterCommand { get; private set; }
        public ICommand ResetSessionDateDraftCommand { get; private set; }
        public ICommand ClearSessionDateFilterCommand { get; private set; }

        public bool IsInboxExpanded
        {
            get { return string.Equals(_expandedSection, "Inbox", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsWorkLogExpanded
        {
            get { return string.Equals(_expandedSection, "WorkLog", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSessionExpanded
        {
            get { return string.Equals(_expandedSection, "Session", StringComparison.OrdinalIgnoreCase); }
        }

        public bool ShowInboxSection
        {
            get { return string.IsNullOrWhiteSpace(_expandedSection) || IsInboxExpanded; }
        }

        public bool ShowWorkLogSection
        {
            get { return string.IsNullOrWhiteSpace(_expandedSection) || IsWorkLogExpanded; }
        }

        public bool ShowSessionSection
        {
            get { return string.IsNullOrWhiteSpace(_expandedSection) || IsSessionExpanded; }
        }

        public string InboxExpandToolTip
        {
            get { return IsInboxExpanded ? "원래 크기로" : "크게 보기"; }
        }

        public string WorkLogExpandToolTip
        {
            get { return IsWorkLogExpanded ? "원래 크기로" : "크게 보기"; }
        }

        public string SessionExpandToolTip
        {
            get { return IsSessionExpanded ? "원래 크기로" : "크게 보기"; }
        }

        public bool HasInboxRows
        {
            get { return Inbox != null && Inbox.HasRows; }
        }

        public bool IsInboxCompact
        {
            get { return !IsInboxExpanded; }
        }

        public GridLength InboxRowHeight
        {
            get { return IsInboxExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto; }
        }

        public GridLength BottomRowHeight
        {
            get { return IsInboxExpanded ? new GridLength(0) : new GridLength(1, GridUnitType.Star); }
        }

        public GridLength InboxBodyRowHeight
        {
            get { return IsInboxExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto; }
        }

        public double InboxBodyMaxHeight
        {
            get
            {
                if (IsInboxExpanded)
                    return double.PositiveInfinity;
                return HasInboxRows ? 280 : 120;
            }
        }

        public double InboxBodyMinHeight
        {
            get
            {
                if (IsInboxExpanded)
                    return 0;
                return HasInboxRows ? 0 : 120;
            }
        }

        public string CalendarMonthTitle
        {
            get { return _calendarMonth.ToString("M월 yyyy"); }
        }

        public string DraftRangeLabel
        {
            get
            {
                if (!_draftStartDate.HasValue)
                    return "기간을 선택하세요";
                string start = _draftStartDate.Value.ToString("yyyy.MM.dd");
                if (!_draftEndDate.HasValue)
                    return start + " ~";
                return start + " ~ " + _draftEndDate.Value.ToString("yyyy.MM.dd");
            }
        }

        public bool HasAppliedSessionRange
        {
            get { return _appliedFrom.HasValue && _appliedTo.HasValue; }
        }

        public string AppliedSessionRangeChipText
        {
            get
            {
                if (!HasAppliedSessionRange)
                    return string.Empty;
                DateTime from = _appliedFrom.Value;
                DateTime to = _appliedTo.Value;
                return from.Month + "." + from.Day + " " + from.ToString("HH:mm")
                    + " – " + to.Month + "." + to.Day + " " + to.ToString("HH:mm");
            }
        }

        public string SessionPeriodTabText
        {
            get
            {
                return HasAppliedSessionRange ? AppliedSessionRangeChipText : "기간";
            }
        }

        public int DraftStartHour
        {
            get { return _draftStartHour; }
            set { SetProperty(ref _draftStartHour, ClampHour(value)); }
        }

        public int DraftStartMinute
        {
            get { return _draftStartMinute; }
            set { SetProperty(ref _draftStartMinute, ClampMinute(value)); }
        }

        public int DraftEndHour
        {
            get { return _draftEndHour; }
            set { SetProperty(ref _draftEndHour, ClampHour(value)); }
        }

        public int DraftEndMinute
        {
            get { return _draftEndMinute; }
            set { SetProperty(ref _draftEndMinute, ClampMinute(value)); }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                if (SetProperty(ref _isLoading, value))
                    RaisePropertyChanged("IsReady");
            }
        }

        public bool IsReady
        {
            get { return !IsLoading; }
        }

        public string OccupancyDisplayName
        {
            get
            {
                string name = OccupancyNameStore.HeaderName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return WorkHubUserProfile.HasDisplayName
                        ? WorkHubUserProfile.OccupancyName
                        : "이름 없음";
                }
                string team = OccupancyNameStore.TryGetAffiliation();
                if (string.IsNullOrWhiteSpace(team))
                    return name;
                return name + " · " + team;
            }
        }

        public string OccupancyHint
        {
            get { return "이 PC에서 쓰는 이름입니다. 업무기록 탭은 팀 전체 목록입니다."; }
        }

        public string OccupancyInitial
        {
            get { return WorkHubUserProfile.OccupancyInitial; }
        }

        public string OccupancySubtext
        {
            get
            {
                string pc = Environment.MachineName;
                if (string.IsNullOrWhiteSpace(pc))
                    return "이 PC에서 쓰는 이름";
                return "이 PC · " + pc.Trim();
            }
        }

        public string InboxWaitingLabel
        {
            get
            {
                int waiting = Inbox != null ? Inbox.SiteWaitingCount : 0;
                return waiting > 0 ? "대기 " + waiting + "건" : "가져온 체크인";
            }
        }

        public int WorkLogCount { get; private set; }
        public int SessionCount { get; private set; }

        public bool HasWorkLogs
        {
            get { return WorkLogs.Count > 0; }
        }

        public bool HasSessions
        {
            get { return Sessions.Count > 0; }
        }

        public string SessionEmptyText
        {
            get
            {
                if (HasAppliedSessionRange && Sessions.Count == 0)
                    return "선택한 기간에 해당하는 접속 이력이 없습니다.";
                if (SessionDurationAllCount > 0 && Sessions.Count == 0)
                    return "이 작업 시간에 해당하는 접속 이력이 없습니다.";
                return "이 PC에서 접속한 이력이 없습니다.";
            }
        }

        public string WorkLogSectionTitle
        {
            get
            {
                return WorkLogCount <= 0
                    ? "나의 업무기록"
                    : "나의 업무기록 " + WorkLogCount;
            }
        }

        public string SessionSectionTitle
        {
            get
            {
                return SessionCount <= 0
                    ? "PC 접속 이력"
                    : "PC 접속 이력 " + SessionCount;
            }
        }

        public string SessionSiteCode
        {
            get { return _sharedSiteCode; }
        }

        public string SharedSiteCode
        {
            get { return _sharedSiteCode; }
        }

        public string WorkLogSearchText
        {
            get { return _workLogSearchText; }
            set
            {
                if (SetProperty(ref _workLogSearchText, value ?? string.Empty))
                    ApplyWorkLogFilters();
            }
        }

        public bool IsInboxSearchOpen
        {
            get { return _isInboxSearchOpen; }
            set { SetProperty(ref _isInboxSearchOpen, value); }
        }

        public bool IsWorkLogSearchOpen
        {
            get { return _isWorkLogSearchOpen; }
            set { SetProperty(ref _isWorkLogSearchOpen, value); }
        }

        public bool IsSessionSearchOpen
        {
            get { return _isSessionSearchOpen; }
            set { SetProperty(ref _isSessionSearchOpen, value); }
        }

        public string SessionSearchText
        {
            get { return _sessionSearchText; }
            set
            {
                if (SetProperty(ref _sessionSearchText, value ?? string.Empty))
                    ApplySessionSite();
            }
        }

        public bool IsSessionDurationAll
        {
            get { return string.Equals(_sessionDurationFilter, "all", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSessionDurationUnder10
        {
            get { return string.Equals(_sessionDurationFilter, "under10", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSessionDurationUnder30
        {
            get { return string.Equals(_sessionDurationFilter, "under30", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSessionDurationMid60
        {
            get { return string.Equals(_sessionDurationFilter, "mid60", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSessionDurationOver60
        {
            get { return string.Equals(_sessionDurationFilter, "over60", StringComparison.OrdinalIgnoreCase); }
        }

        public int SessionDurationAllCount { get; private set; }
        public int SessionDurationUnder10Count { get; private set; }
        public int SessionDurationUnder30Count { get; private set; }
        public int SessionDurationMid60Count { get; private set; }
        public int SessionDurationOver60Count { get; private set; }

        public bool IsWorkLogFilterAll
        {
            get { return string.Equals(_workLogWriteFilter, "all", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsWorkLogFilterDraft
        {
            get { return string.Equals(_workLogWriteFilter, "draft", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsWorkLogFilterCompleted
        {
            get { return string.Equals(_workLogWriteFilter, "completed", StringComparison.OrdinalIgnoreCase); }
        }

        public bool HasNonDefaultWorkLogFilter
        {
            get { return !IsWorkLogFilterAll; }
        }

        public bool IsWorkLogFilterMenuOpen
        {
            get { return _isWorkLogFilterMenuOpen; }
            set { SetProperty(ref _isWorkLogFilterMenuOpen, value); }
        }

        public int WorkLogFilterAllCount { get; private set; }
        public int WorkLogFilterDraftCount { get; private set; }
        public int WorkLogFilterCompletedCount { get; private set; }

        public void RefreshIdentity()
        {
            RaisePropertyChanged("OccupancyDisplayName");
            RaisePropertyChanged("OccupancyInitial");
            RaisePropertyChanged("OccupancySubtext");
        }

        public async Task ReloadAsync()
        {
            if (IsLoading)
                return;
            IsLoading = true;
            try
            {
                if (Inbox != null)
                {
                    if (!string.Equals(Inbox.SelectedSiteCode, _sharedSiteCode, StringComparison.OrdinalIgnoreCase))
                        Inbox.SelectedSiteCode = _sharedSiteCode;
                    else
                        Inbox.Reload();
                }
                _workLogList.NotifyInboxBadge();
                RaisePropertyChanged("InboxWaitingLabel");

                await LoadWorkLogsAsync().ConfigureAwait(true);
                await LoadSessionsAsync().ConfigureAwait(true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadWorkLogsAsync()
        {
            _workLogSource.Clear();
            bool allSites = string.Equals(
                _sharedSiteCode, TfsCheckinInboxStore.AllSites, StringComparison.OrdinalIgnoreCase);
            var query = new WorkLogListQuery
            {
                PageIndex = 0,
                PageSize = WorkLogTake,
                SiteCode = allSites ? null : _sharedSiteCode,
                AuthorName = string.IsNullOrWhiteSpace(WorkHubUserProfile.OccupancyName)
                    ? null
                    : WorkHubUserProfile.OccupancyName,
                AuthorLocalPcIp = string.IsNullOrWhiteSpace(WorkHubUserProfile.LocalIp)
                    ? null
                    : WorkHubUserProfile.LocalIp
            };

            WorkLogPageResult page = null;
            try
            {
                page = await _persistence.GetPageAsync(query).ConfigureAwait(true);
            }
            catch
            {
                page = null;
            }

            if (page != null && page.Items != null)
            {
                foreach (var dto in page.Items)
                {
                    var item = WorkLogDbMapper.FromDto(dto);
                    if (item != null)
                        _workLogSource.Add(item);
                }
            }

            ApplyWorkLogFilters();
        }

        private async Task LoadSessionsAsync()
        {
            _sessionSource.Clear();
            var logs = await _share.GetMyRecentSessionsAsync(SessionTake, _appliedFrom, _appliedTo).ConfigureAwait(true);
            if (logs != null)
            {
                foreach (var dto in logs)
                {
                    var item = RemotePcUsageLogItemViewModel.FromDto(dto);
                    if (item == null)
                        continue;
                    item.SiteCode = ResolveSessionSite(item);
                    _sessionSource.Add(item);
                }
            }

            SessionCount = _sessionSource.Count;
            RaisePropertyChanged("SessionCount");
            RaisePropertyChanged("SessionSectionTitle");
            ApplySessionSite();
        }

        private string ResolveSessionSite(RemotePcUsageLogItemViewModel item)
        {
            if (item == null)
                return TfsCheckinInboxStore.FolderSiteCodes[0];
            string site = TfsCheckinInboxStore.ResolveFolderSite(item.SiteCode, item.RemotePcName);
            if (!string.IsNullOrWhiteSpace(site)
                && !string.Equals(site, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase))
                return site;
            string fromPc = _workLogList.ResolvePcSite(item.RemoteAccessIpAddress, item.RemotePcName);
            if (!string.IsNullOrWhiteSpace(fromPc)
                && !string.Equals(fromPc, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase))
                return fromPc;
            return TfsCheckinInboxStore.FolderSiteCodes[0];
        }

        private void SelectSharedSite(string site)
        {
            string next = string.IsNullOrWhiteSpace(site)
                ? TfsCheckinInboxStore.AllSites
                : site.Trim();
            if (string.Equals(next, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase)
                || string.Equals(next, "OTHER", StringComparison.OrdinalIgnoreCase))
                next = TfsCheckinInboxStore.AllSites;
            _sharedSiteCode = next;
            if (Inbox != null)
                Inbox.SelectedSiteCode = next;
            RaisePropertyChanged("SharedSiteCode");
            RaisePropertyChanged("SessionSiteCode");
            RaisePropertyChanged("InboxWaitingLabel");
            ApplySessionSite();
            var ignored = LoadWorkLogsAsync();
        }

        private void ToggleWorkLogFilterMenu()
        {
            IsWorkLogFilterMenuOpen = !IsWorkLogFilterMenuOpen;
        }

        private void SetWorkLogWriteFilter(string status)
        {
            _workLogWriteFilter = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim();
            IsWorkLogFilterMenuOpen = false;
            RaisePropertyChanged("IsWorkLogFilterAll");
            RaisePropertyChanged("IsWorkLogFilterDraft");
            RaisePropertyChanged("IsWorkLogFilterCompleted");
            RaisePropertyChanged("HasNonDefaultWorkLogFilter");
            ApplyWorkLogFilters();
        }

        private void ApplyWorkLogFilters()
        {
            WorkLogs.Clear();
            int allCount = 0;
            int draftCount = 0;
            int completedCount = 0;
            bool allSites = string.Equals(
                _sharedSiteCode, TfsCheckinInboxStore.AllSites, StringComparison.OrdinalIgnoreCase);
            var matched = new List<WorkLogListItemViewModel>();
            foreach (var item in _workLogSource)
            {
                if (item == null)
                    continue;
                if (!allSites
                    && !string.Equals(item.SiteCode, _sharedSiteCode, StringComparison.OrdinalIgnoreCase))
                    continue;
                allCount++;
                if (item.IsDraft)
                    draftCount++;
                if (item.IsCompleted)
                    completedCount++;
                if (IsWorkLogFilterDraft && !item.IsDraft)
                    continue;
                if (IsWorkLogFilterCompleted && !item.IsCompleted)
                    continue;
                if (!MatchesWorkLogSearch(item))
                    continue;
                matched.Add(item);
            }
            matched.Sort((a, b) =>
            {
                DateTime at = a != null && a.TimelineDate.HasValue ? a.TimelineDate.Value : DateTime.MinValue;
                DateTime bt = b != null && b.TimelineDate.HasValue ? b.TimelineDate.Value : DateTime.MinValue;
                int cmp = bt.CompareTo(at);
                if (cmp != 0)
                    return cmp;
                long aId = a != null ? a.DbLogId : 0;
                long bId = b != null ? b.DbLogId : 0;
                return bId.CompareTo(aId);
            });
            foreach (var item in matched)
                WorkLogs.Add(item);
            WorkLogCount = WorkLogs.Count;
            RebuildWorkLogDateGroups();
            WorkLogFilterAllCount = allCount;
            WorkLogFilterDraftCount = draftCount;
            WorkLogFilterCompletedCount = completedCount;
            RaisePropertyChanged("WorkLogCount");
            RaisePropertyChanged("HasWorkLogs");
            RaisePropertyChanged("WorkLogSectionTitle");
            RaisePropertyChanged("WorkLogFilterAllCount");
            RaisePropertyChanged("WorkLogFilterDraftCount");
            RaisePropertyChanged("WorkLogFilterCompletedCount");
        }

        private void OnInboxPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.PropertyName))
                return;
            if (e.PropertyName == "SiteWaitingCount" || e.PropertyName == "WaitingCount")
                RaisePropertyChanged("InboxWaitingLabel");
            if (e.PropertyName == "HasRows")
                RaiseInboxLayout();
            if (e.PropertyName == "SelectedSiteCode" && Inbox != null)
            {
                string next = string.IsNullOrWhiteSpace(Inbox.SelectedSiteCode)
                    ? TfsCheckinInboxStore.AllSites
                    : Inbox.SelectedSiteCode;
                if (!string.Equals(_sharedSiteCode, next, StringComparison.OrdinalIgnoreCase))
                {
                    _sharedSiteCode = next;
                    RaisePropertyChanged("SharedSiteCode");
                    RaisePropertyChanged("SessionSiteCode");
                    RaisePropertyChanged("InboxWaitingLabel");
                    ApplySessionSite();
                    var ignored = LoadWorkLogsAsync();
                }
            }
        }

        private void ToggleExpand(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                return;
            if (string.Equals(_expandedSection, section, StringComparison.OrdinalIgnoreCase))
                _expandedSection = null;
            else
                _expandedSection = section.Trim();
            RaisePropertyChanged("IsInboxExpanded");
            RaisePropertyChanged("IsWorkLogExpanded");
            RaisePropertyChanged("IsSessionExpanded");
            RaisePropertyChanged("ShowInboxSection");
            RaisePropertyChanged("ShowWorkLogSection");
            RaisePropertyChanged("ShowSessionSection");
            RaisePropertyChanged("InboxExpandToolTip");
            RaisePropertyChanged("WorkLogExpandToolTip");
            RaisePropertyChanged("SessionExpandToolTip");
            RaiseInboxLayout();
        }

        private void SetSessionDurationFilter(string filter)
        {
            _sessionDurationFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim();
            RaisePropertyChanged("IsSessionDurationAll");
            RaisePropertyChanged("IsSessionDurationUnder10");
            RaisePropertyChanged("IsSessionDurationUnder30");
            RaisePropertyChanged("IsSessionDurationMid60");
            RaisePropertyChanged("IsSessionDurationOver60");
            ApplySessionSite();
        }

        private void ApplySessionSite()
        {
            Sessions.Clear();
            bool allSites = string.Equals(
                _sharedSiteCode, TfsCheckinInboxStore.AllSites, StringComparison.OrdinalIgnoreCase);
            var matched = new List<RemotePcUsageLogItemViewModel>();
            int allCount = 0;
            int under10 = 0;
            int under30 = 0;
            int mid60 = 0;
            int over60 = 0;
            foreach (var item in _sessionSource)
            {
                if (item == null)
                    continue;
                if (!allSites
                    && !string.Equals(item.SiteCode, _sharedSiteCode, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!MatchesSessionSearch(item))
                    continue;
                allCount++;
                string bucket = ClassifySessionDuration(item);
                if (bucket == "under10")
                    under10++;
                else if (bucket == "under30")
                    under30++;
                else if (bucket == "mid60")
                    mid60++;
                else if (bucket == "over60")
                    over60++;
                if (!MatchesSessionDuration(item))
                    continue;
                matched.Add(item);
            }
            matched.Sort((a, b) =>
            {
                DateTime at = a != null && a.RequestedAt.HasValue ? a.RequestedAt.Value : DateTime.MinValue;
                DateTime bt = b != null && b.RequestedAt.HasValue ? b.RequestedAt.Value : DateTime.MinValue;
                int cmp = bt.CompareTo(at);
                if (cmp != 0)
                    return cmp;
                return (b != null ? b.LogId : 0).CompareTo(a != null ? a.LogId : 0);
            });
            foreach (var item in matched)
                Sessions.Add(item);
            SessionCount = Sessions.Count;
            SessionDurationAllCount = allCount;
            SessionDurationUnder10Count = under10;
            SessionDurationUnder30Count = under30;
            SessionDurationMid60Count = mid60;
            SessionDurationOver60Count = over60;
            RaisePropertyChanged("HasSessions");
            RaisePropertyChanged("SessionEmptyText");
            RaisePropertyChanged("SessionCount");
            RaisePropertyChanged("SessionSectionTitle");
            RaisePropertyChanged("SessionDurationAllCount");
            RaisePropertyChanged("SessionDurationUnder10Count");
            RaisePropertyChanged("SessionDurationUnder30Count");
            RaisePropertyChanged("SessionDurationMid60Count");
            RaisePropertyChanged("SessionDurationOver60Count");
            RebuildSessionDateGroups();
        }

        private bool MatchesSessionDuration(RemotePcUsageLogItemViewModel item)
        {
            if (IsSessionDurationAll)
                return true;
            string bucket = ClassifySessionDuration(item);
            if (string.IsNullOrEmpty(bucket))
                return false;
            return string.Equals(bucket, _sessionDurationFilter, StringComparison.OrdinalIgnoreCase);
        }

        private static string ClassifySessionDuration(RemotePcUsageLogItemViewModel item)
        {
            if (item == null || !item.DurationMinutes.HasValue)
                return null;
            double minutes = item.DurationMinutes.Value;
            if (minutes < 10)
                return "under10";
            if (minutes < 30)
                return "under30";
            if (minutes < 60)
                return "mid60";
            return "over60";
        }

        private void RebuildSessionDateGroups()
        {
            SessionDateGroups.Clear();
            var buckets = new Dictionary<DateTime, SessionDateGroupViewModel>();
            SessionDateGroupViewModel unknown = null;
            foreach (var item in Sessions)
            {
                if (item == null)
                    continue;
                if (!item.RequestedAt.HasValue)
                {
                    if (unknown == null)
                        unknown = new SessionDateGroupViewModel { IsUnknownDate = true };
                    unknown.Items.Add(item);
                    continue;
                }
                DateTime day = item.RequestedAt.Value.Date;
                SessionDateGroupViewModel group;
                if (!buckets.TryGetValue(day, out group))
                {
                    group = new SessionDateGroupViewModel { Date = day };
                    buckets.Add(day, group);
                }
                group.Items.Add(item);
            }

            var days = new List<DateTime>(buckets.Keys);
            days.Sort();
            days.Reverse();
            foreach (DateTime day in days)
                SessionDateGroups.Add(buckets[day]);
            if (unknown != null)
                SessionDateGroups.Add(unknown);
        }

        private bool MatchesWorkLogSearch(WorkLogListItemViewModel item)
        {
            string q = (_workLogSearchText ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;
            if (item == null)
                return false;
            return ContainsIgnoreCase(item.ListTitleText, q)
                || ContainsIgnoreCase(item.TicketDisplay, q)
                || ContainsIgnoreCase(item.ListMenuText, q)
                || ContainsIgnoreCase(item.TfsComment, q)
                || ContainsIgnoreCase(item.TicketNo, q)
                || ContainsIgnoreCase(item.Pc, q)
                || ContainsIgnoreCase(item.SiteCode, q);
        }

        private bool MatchesSessionSearch(RemotePcUsageLogItemViewModel item)
        {
            if (!_appliedFrom.HasValue || !_appliedTo.HasValue)
                return true;
            if (item == null || !item.RequestedAt.HasValue)
                return false;
            DateTime at = item.RequestedAt.Value;
            return at >= _appliedFrom.Value && at <= _appliedTo.Value;
        }

        private void ToggleSessionSearch()
        {
            if (IsSessionSearchOpen)
            {
                CloseSessionPickerAndSearch();
                return;
            }
            IsInboxSearchOpen = false;
            IsWorkLogSearchOpen = false;
            PrepareSessionDateDraft();
            IsSessionSearchOpen = true;
        }

        private void CloseSessionPickerAndSearch()
        {
            CommitAppliedRangeFromDraft();
            IsSessionSearchOpen = false;
            var ignored = LoadSessionsAsync();
        }

        private void ToggleInboxSearch()
        {
            bool next = !IsInboxSearchOpen;
            IsInboxSearchOpen = next;
            if (next)
            {
                IsWorkLogSearchOpen = false;
                IsSessionSearchOpen = false;
            }
        }

        private void ToggleWorkLogSearch()
        {
            bool next = !IsWorkLogSearchOpen;
            IsWorkLogSearchOpen = next;
            if (next)
            {
                IsInboxSearchOpen = false;
                IsSessionSearchOpen = false;
            }
        }

        private void PrepareSessionDateDraft()
        {
            if (_appliedFrom.HasValue && _appliedTo.HasValue)
            {
                _draftStartDate = _appliedFrom.Value.Date;
                _draftEndDate = _appliedTo.Value.Date;
                _draftStartHour = _appliedFrom.Value.Hour;
                _draftStartMinute = ClampMinute(_appliedFrom.Value.Minute);
                _draftEndHour = _appliedTo.Value.Hour;
                _draftEndMinute = ClampMinute(_appliedTo.Value.Minute);
                _calendarMonth = new DateTime(_draftStartDate.Value.Year, _draftStartDate.Value.Month, 1);
            }
            else if (!_draftStartDate.HasValue)
            {
                _draftStartHour = 9;
                _draftStartMinute = 0;
                _draftEndHour = 18;
                _draftEndMinute = 0;
                _calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            }
            RaisePropertyChanged("DraftStartHour");
            RaisePropertyChanged("DraftStartMinute");
            RaisePropertyChanged("DraftEndHour");
            RaisePropertyChanged("DraftEndMinute");
            RaisePropertyChanged("DraftRangeLabel");
            RaisePropertyChanged("CalendarMonthTitle");
            RebuildCalendarDays();
        }

        private void PrevCalendarMonth()
        {
            _calendarMonth = _calendarMonth.AddMonths(-1);
            RaisePropertyChanged("CalendarMonthTitle");
            RebuildCalendarDays();
        }

        private void NextCalendarMonth()
        {
            _calendarMonth = _calendarMonth.AddMonths(1);
            RaisePropertyChanged("CalendarMonthTitle");
            RebuildCalendarDays();
        }

        private void SelectCalendarDay(DateTime day)
        {
            DateTime d = day.Date;
            if (!_draftStartDate.HasValue || _draftEndDate.HasValue)
            {
                _draftStartDate = d;
                _draftEndDate = null;
            }
            else if (d < _draftStartDate.Value)
            {
                _draftEndDate = _draftStartDate;
                _draftStartDate = d;
            }
            else
            {
                _draftEndDate = d;
            }
            RaisePropertyChanged("DraftRangeLabel");
            RebuildCalendarDays();
        }

        private void ApplySessionDateFilter()
        {
            CloseSessionPickerAndSearch();
        }

        private void CommitAppliedRangeFromDraft()
        {
            if (!_draftStartDate.HasValue)
                return;

            DateTime startDay = _draftStartDate.Value.Date;
            DateTime endDay = _draftEndDate.HasValue ? _draftEndDate.Value.Date : startDay;
            DateTime from = startDay.AddHours(DraftStartHour).AddMinutes(DraftStartMinute);
            DateTime to = endDay.AddHours(DraftEndHour).AddMinutes(DraftEndMinute);
            if (to < from)
            {
                DateTime swap = from;
                from = to;
                to = swap;
            }
            _appliedFrom = from;
            _appliedTo = to;
            _sessionSearchText = string.Empty;
            RaisePropertyChanged("SessionSearchText");
            RaisePropertyChanged("HasAppliedSessionRange");
            RaisePropertyChanged("AppliedSessionRangeChipText");
            RaisePropertyChanged("SessionPeriodTabText");
        }

        private void ResetSessionDateDraft()
        {
            _draftStartDate = null;
            _draftEndDate = null;
            _draftStartHour = 9;
            _draftStartMinute = 0;
            _draftEndHour = 18;
            _draftEndMinute = 0;
            RaisePropertyChanged("DraftStartHour");
            RaisePropertyChanged("DraftStartMinute");
            RaisePropertyChanged("DraftEndHour");
            RaisePropertyChanged("DraftEndMinute");
            RaisePropertyChanged("DraftRangeLabel");
            RebuildCalendarDays();
            _appliedFrom = null;
            _appliedTo = null;
            RaisePropertyChanged("HasAppliedSessionRange");
            RaisePropertyChanged("AppliedSessionRangeChipText");
            RaisePropertyChanged("SessionPeriodTabText");
            var ignored = LoadSessionsAsync();
        }

        private void ClearSessionDateFilter()
        {
            ResetSessionDateDraft();
            IsSessionSearchOpen = false;
        }

        private void RebuildCalendarDays()
        {
            if (CalendarDays == null)
                return;
            CalendarDays.Clear();
            DateTime first = new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1);
            DateTime start = first.AddDays(-(int)first.DayOfWeek);
            int daysInMonth = DateTime.DaysInMonth(_calendarMonth.Year, _calendarMonth.Month);
            int cellCount = (((int)first.DayOfWeek + daysInMonth + 6) / 7) * 7;
            DateTime? rangeStart = _draftStartDate;
            DateTime? rangeEnd = _draftEndDate ?? _draftStartDate;
            for (int i = 0; i < cellCount; i++)
            {
                DateTime d = start.AddDays(i);
                bool currentMonth = d.Month == _calendarMonth.Month;
                bool inRange = rangeStart.HasValue
                    && rangeEnd.HasValue
                    && d.Date >= rangeStart.Value.Date
                    && d.Date <= rangeEnd.Value.Date;
                bool isStart = rangeStart.HasValue && d.Date == rangeStart.Value.Date;
                bool isEnd = rangeEnd.HasValue && d.Date == rangeEnd.Value.Date;
                bool multi = rangeStart.HasValue && rangeEnd.HasValue
                    && rangeStart.Value.Date != rangeEnd.Value.Date;
                CalendarDays.Add(new CalendarDayItem
                {
                    Date = d,
                    DayText = currentMonth ? d.Day.ToString() : string.Empty,
                    IsCurrentMonth = currentMonth,
                    IsToday = currentMonth && d.Date == DateTime.Today,
                    IsSunday = d.DayOfWeek == DayOfWeek.Sunday,
                    IsSaturday = d.DayOfWeek == DayOfWeek.Saturday,
                    IsRangeStart = isStart,
                    IsRangeEnd = isEnd,
                    IsInRange = inRange,
                    RangeFillMid = inRange && multi && !isStart && !isEnd,
                    RangeFillFromStart = inRange && multi && isStart && !isEnd,
                    RangeFillToEnd = inRange && multi && isEnd && !isStart
                });
            }
        }

        private void RaiseInboxLayout()
        {
            RaisePropertyChanged("HasInboxRows");
            RaisePropertyChanged("IsInboxCompact");
            RaisePropertyChanged("InboxRowHeight");
            RaisePropertyChanged("BottomRowHeight");
            RaisePropertyChanged("InboxBodyRowHeight");
            RaisePropertyChanged("InboxBodyMaxHeight");
            RaisePropertyChanged("InboxBodyMinHeight");
        }

        private static int ClampHour(int value)
        {
            if (value < 0)
                return 0;
            if (value > 23)
                return 23;
            return value;
        }

        private static int ClampMinute(int value)
        {
            if (value < 0)
                return 0;
            int snapped = (int)Math.Round(value / 10.0) * 10;
            if (snapped > 50)
                snapped = 50;
            return snapped;
        }

        private static bool ContainsIgnoreCase(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
                return false;
            return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RebuildWorkLogDateGroups()
        {
            WorkLogDateGroups.Clear();
            var buckets = new Dictionary<DateTime, WorkLogDateGroupViewModel>();
            WorkLogDateGroupViewModel unknown = null;
            foreach (var item in WorkLogs)
            {
                if (item == null)
                    continue;
                DateTime? daySrc = item.TimelineDate ?? item.StartDate ?? item.CheckedInAt ?? item.LastModifiedAt;
                if (!daySrc.HasValue)
                {
                    if (unknown == null)
                        unknown = new WorkLogDateGroupViewModel { IsUnknownDate = true };
                    unknown.Items.Add(item);
                    continue;
                }
                DateTime day = daySrc.Value.Date;
                WorkLogDateGroupViewModel group;
                if (!buckets.TryGetValue(day, out group))
                {
                    group = new WorkLogDateGroupViewModel { Date = day };
                    buckets.Add(day, group);
                }
                group.Items.Add(item);
            }

            var days = new List<DateTime>(buckets.Keys);
            days.Sort();
            days.Reverse();
            foreach (DateTime day in days)
                WorkLogDateGroups.Add(buckets[day]);
            if (unknown != null)
                WorkLogDateGroups.Add(unknown);
        }

        private async Task ChangeNameAsync()
        {
            if (_changeName == null)
                return;
            await _changeName().ConfigureAwait(true);
        }

        private void OpenWorkLog(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;
            if (_openWorkLog != null)
                _openWorkLog(item);
        }

        private void OpenAllWorkLogs()
        {
            if (_openWorkLog != null)
                _openWorkLog(null);
        }
    }

    public sealed class SessionDateGroupViewModel
    {
        public SessionDateGroupViewModel()
        {
            Items = new ObservableCollection<RemotePcUsageLogItemViewModel>();
        }

        public DateTime Date { get; set; }
        public bool IsUnknownDate { get; set; }
        public ObservableCollection<RemotePcUsageLogItemViewModel> Items { get; private set; }

        public string DateHeader
        {
            get
            {
                if (IsUnknownDate)
                    return "일시 미확인";
                DateTime today = DateTime.Today;
                if (Date.Date == today)
                    return "오늘";
                if (Date.Date == today.AddDays(-1))
                    return "어제";
                return Date.ToString("yyyy년 M월 d일");
            }
        }
    }

    public sealed class WorkLogDateGroupViewModel
    {
        public WorkLogDateGroupViewModel()
        {
            Items = new ObservableCollection<WorkLogListItemViewModel>();
        }

        public DateTime Date { get; set; }
        public bool IsUnknownDate { get; set; }
        public ObservableCollection<WorkLogListItemViewModel> Items { get; private set; }

        public string DateHeader
        {
            get
            {
                if (IsUnknownDate)
                    return "일시 미확인";
                DateTime today = DateTime.Today;
                if (Date.Date == today)
                    return "오늘";
                if (Date.Date == today.AddDays(-1))
                    return "어제";
                return Date.ToString("yyyy년 M월 d일");
            }
        }
    }

    public sealed class CalendarDayItem
    {
        public DateTime Date { get; set; }
        public string DayText { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsToday { get; set; }
        public bool IsRangeStart { get; set; }
        public bool IsRangeEnd { get; set; }
        public bool IsInRange { get; set; }
        public bool RangeFillMid { get; set; }
        public bool RangeFillFromStart { get; set; }
        public bool RangeFillToEnd { get; set; }
        public bool IsSunday { get; set; }
        public bool IsSaturday { get; set; }
        public bool IsTodayOnly
        {
            get { return IsToday && !IsEndpoint; }
        }
        public bool IsEndpoint
        {
            get { return IsRangeStart || IsRangeEnd; }
        }
    }
}
