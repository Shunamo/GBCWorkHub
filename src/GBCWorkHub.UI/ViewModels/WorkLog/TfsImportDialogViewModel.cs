using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.ViewModels;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// TFS 체크인 선택 팝업.
    /// 그룹으로 합치기: 1~5 번호 반복 배정, 뱃지 클릭으로 변경. 같은 번호끼리 합침.
    /// </summary>
    public sealed class TfsImportDialogViewModel : ViewModelBase
    {
        public const string ModeSeparate = "Separate";
        public const string ModeMergeAll = "MergeAll";
        public const string ModeCustomGroups = "CustomGroups";
        public const string ModeAppendExisting = "AppendExisting";

        private TfsImportCandidateRow _focused;
        private string _statusMessage = string.Empty;
        private string _importMode = ModeSeparate;
        private TfsImportAppendTarget _selectedAppendTarget;
        private string _appendSearchText = string.Empty;

        public TfsImportDialogViewModel()
        {
            Candidates = new ObservableCollection<TfsImportCandidateRow>();
            AppendTargets = new ObservableCollection<TfsImportAppendTarget>();
            FilteredAppendTargets = new ObservableCollection<TfsImportAppendTarget>();
            SelectAllCommand = new RelayCommand(SelectAll);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            FocusCandidateCommand = new RelayCommand<TfsImportCandidateRow>(FocusCandidate);
            SetImportModeCommand = new RelayCommand<string>(SetImportMode);
            CycleMergeGroupCommand = new RelayCommand<TfsImportCandidateRow>(CycleMergeGroup);
            ToggleFileIncludeCommand = new RelayCommand<TfsImportFileRow>(ToggleFileInclude);
            SelectAppendTargetCommand = new RelayCommand<TfsImportAppendTarget>(SelectAppendTarget);
            SelectSessionPcCommand = new RelayCommand<string>(SelectSessionPc);
            SessionSiteOptions = new ObservableCollection<string>
            {
                WorkLogSiteCodes.Aurora,
                WorkLogSiteCodes.Rc,
                WorkLogSiteCodes.Cmc,
                WorkLogSiteCodes.Mngha
            };
            SessionPcOptions = new ObservableCollection<string>();
            SessionPcGroups = new ObservableCollection<TfsImportPcTeamGroup>();
        }

        public ObservableCollection<TfsImportCandidateRow> Candidates { get; private set; }

        public ObservableCollection<string> SessionSiteOptions { get; private set; }

        public ObservableCollection<string> SessionPcOptions { get; private set; }

        public ObservableCollection<TfsImportPcTeamGroup> SessionPcGroups { get; private set; }

        public ICommand SelectSessionPcCommand { get; private set; }

        private string _sessionSite = WorkLogSiteCodes.Aurora;
        public string SessionSite
        {
            get { return _sessionSite; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? WorkLogSiteCodes.Aurora : value.Trim();
                if (SetProperty(ref _sessionSite, next))
                    RaisePropertyChanged("CanFetchSessionQuery");
            }
        }

        private string _sessionDateText = string.Empty;
        public string SessionDateText
        {
            get { return _sessionDateText; }
            set
            {
                string masked = WorkLogDraftMapper.MaskDateInput(value);
                if (!SetProperty(ref _sessionDateText, masked)
                    && !string.Equals(value ?? string.Empty, masked, StringComparison.Ordinal))
                    RaisePropertyChanged("SessionDateText");
                RaisePropertyChanged("CanFetchSessionQuery");
            }
        }

        private string _sessionStartTimeText = string.Empty;
        public string SessionStartTimeText
        {
            get { return _sessionStartTimeText; }
            set
            {
                string masked = WorkLogDraftMapper.MaskTimeInput(value);
                if (!SetProperty(ref _sessionStartTimeText, masked)
                    && !string.Equals(value ?? string.Empty, masked, StringComparison.Ordinal))
                    RaisePropertyChanged("SessionStartTimeText");
                RaisePropertyChanged("CanFetchSessionQuery");
            }
        }

        private string _sessionEndTimeText = string.Empty;
        public string SessionEndTimeText
        {
            get { return _sessionEndTimeText; }
            set
            {
                string masked = WorkLogDraftMapper.MaskTimeInput(value);
                if (!SetProperty(ref _sessionEndTimeText, masked)
                    && !string.Equals(value ?? string.Empty, masked, StringComparison.Ordinal))
                    RaisePropertyChanged("SessionEndTimeText");
                RaisePropertyChanged("CanFetchSessionQuery");
            }
        }

        private string _sessionPc;
        public string SessionPc
        {
            get { return _sessionPc; }
            set
            {
                if (SetProperty(ref _sessionPc, value))
                {
                    RefreshPcChipSelection();
                    RaisePropertyChanged("CanFetchSessionQuery");
                }
            }
        }

        private string _sessionSearchMessage = string.Empty;
        public string SessionSearchMessage
        {
            get { return _sessionSearchMessage; }
            set
            {
                if (SetProperty(ref _sessionSearchMessage, value ?? string.Empty))
                    RaisePropertyChanged("HasSessionSearchMessage");
            }
        }

        public bool HasSessionSearchMessage
        {
            get { return !string.IsNullOrWhiteSpace(SessionSearchMessage); }
        }

        public bool CanFetchSessionQuery
        {
            get
            {
                DateTime fromAt;
                DateTime toAt;
                string error;
                return TryGetSearchWindow(out fromAt, out toAt, out error)
                    && !string.IsNullOrWhiteSpace(SessionPc);
            }
        }

        public ICommand FetchSessionQueryCommand { get; set; }

        /// <summary>체크인 상세 팝업에서는 이력 검색을 섞지 않음. 이력은 업무기록 TFS 버튼에서만.</summary>
        public bool ShowSessionPickerWithCandidates
        {
            get { return false; }
        }

        /// <summary>기존 업무기록에 추가할 때 대상 전체 목록.</summary>
        public ObservableCollection<TfsImportAppendTarget> AppendTargets { get; private set; }

        /// <summary>검색어로 걸러진 추가 대상 목록.</summary>
        public ObservableCollection<TfsImportAppendTarget> FilteredAppendTargets { get; private set; }

        public ICommand SelectAppendTargetCommand { get; private set; }

        /// <summary>티켓·메뉴·내용 등 전체 컬럼 검색어.</summary>
        public string AppendSearchText
        {
            get { return _appendSearchText; }
            set
            {
                string next = value ?? string.Empty;
                if (!SetProperty(ref _appendSearchText, next))
                    return;
                ApplyAppendFilter();
                RaisePropertyChanged("ShowAppendTargetList");
            }
        }

        /// <summary>안내 칩은 쓰지 않음(검색 placeholder로 충분).</summary>
        public string AppendFilterHint { get { return string.Empty; } }
        public bool HasAppendFilterHint { get { return false; } }

        public string SelectedAppendTargetSummary
        {
            get
            {
                if (SelectedAppendTarget == null)
                    return string.Empty;
                return SelectedAppendTarget.DisplayLabel;
            }
        }

        /// <summary>추천 TN 매칭이 있거나, 검색 중일 때만 대상 리스트 표시.</summary>
        public bool ShowAppendTargetList
        {
            get
            {
                if (!IsAppendExistingMode || AppendTargets == null || AppendTargets.Count == 0)
                    return false;
                if (!string.IsNullOrWhiteSpace(AppendSearchText))
                    return true;
                return FilteredAppendTargets != null && FilteredAppendTargets.Count > 0;
            }
        }

        public TfsImportAppendTarget SelectedAppendTarget
        {
            get { return _selectedAppendTarget; }
            set
            {
                if (SetProperty(ref _selectedAppendTarget, value))
                {
                    RaisePropertyChanged("ConfirmButtonLabel");
                    RaisePropertyChanged("CanConfirm");
                    RaisePropertyChanged("SelectedAppendTargetSummary");
                    RaisePropertyChanged("HasSelectedAppendTarget");
                    RaisePropertyChanged("ShowAppendTargetList");
                }
            }
        }

        public bool HasSelectedAppendTarget
        {
            get { return SelectedAppendTarget != null && SelectedAppendTarget.Item != null; }
        }

        public string ImportMode
        {
            get { return _importMode; }
            set
            {
                string next = NormalizeMode(value);
                if (!SetProperty(ref _importMode, next))
                    return;
                RaisePropertyChanged("IsSeparateMode");
                RaisePropertyChanged("IsMergeAllMode");
                RaisePropertyChanged("IsCustomGroupsMode");
                RaisePropertyChanged("IsAppendExistingMode");
                RaisePropertyChanged("ShowCustomGroupsTab");
                RaisePropertyChanged("ShowAppendTargetPicker");
                RaisePropertyChanged("ImportModeHint");
                RaisePropertyChanged("HasImportModeHint");
                RaisePropertyChanged("ConfirmButtonLabel");
                RaisePropertyChanged("CanConfirm");
                RaisePropertyChanged("SelectedAppendTargetSummary");
                RaisePropertyChanged("ShowAppendTargetList");
                ApplyModeToRows();
                if (IsAppendExistingMode)
                    SuggestAppendTargetFromSelection();
            }
        }

        public bool IsSeparateMode { get { return ImportMode == ModeSeparate; } }
        public bool IsMergeAllMode { get { return ImportMode == ModeMergeAll; } }
        public bool IsCustomGroupsMode { get { return ImportMode == ModeCustomGroups; } }
        public bool IsAppendExistingMode { get { return ImportMode == ModeAppendExisting; } }
        public bool ShowCustomGroupsTab { get { return SelectedCount >= 3; } }
        public bool ShowAppendTargetPicker
        {
            get { return IsAppendExistingMode && AppendTargets.Count > 0; }
        }
        public bool HasAppendTargets { get { return AppendTargets != null && AppendTargets.Count > 0; } }
        public bool HasImportModeHint { get { return !string.IsNullOrWhiteSpace(ImportModeHint); } }

        public bool CanConfirm
        {
            get
            {
                if (SelectedCount <= 0)
                    return false;
                if (!HasImportableSelectedFiles())
                    return false;
                if (IsAppendExistingMode)
                    return SelectedAppendTarget != null
                        && SelectedAppendTarget.Item != null
                        && SelectedAppendTarget.Item.IsOwnedByCurrentUser;
                return true;
            }
        }

        /// <summary>
        /// 선택된 체크인마다 포함 파일이 있거나(파일 목록이 비어 전체 허용),
        /// 파일은 있는데 전부 제외한 경우는 불가.
        /// </summary>
        private bool HasImportableSelectedFiles()
        {
            bool any = false;
            foreach (var c in Candidates)
            {
                if (c == null || !c.IsImportSelected)
                    continue;
                any = true;
                int fileCount = c.Files != null ? c.Files.Count : 0;
                if (fileCount > 0 && c.IncludedFileCount <= 0)
                    return false;
            }
            return any;
        }

        /// <summary>선택 N건 → 그룹 최대 N-1개 (3→2, 4→3, 5→4…).</summary>
        public int MaxMergeGroups
        {
            get
            {
                int n = SelectedCount;
                if (n < 3)
                    return 1;
                return n - 1;
            }
        }

        /// <summary>안내 문구 최소화 — 검색 placeholder / 선택 UI로 충분.</summary>
        public string ImportModeHint { get { return string.Empty; } }

        public string ConfirmButtonLabel
        {
            get
            {
                if (SelectedCount <= 0)
                    return "선택한 체크인 가져오기";
                if (IsAppendExistingMode)
                {
                    string target = SelectedAppendTarget != null
                        ? SelectedAppendTarget.DisplayLabel
                        : "(대상 선택)";
                    return "선택 " + SelectedCount + "건 → 기존 기록과 병합 · " + target;
                }
                if (IsMergeAllMode)
                    return "선택 " + SelectedCount + "건 → 1건으로 가져오기";
                if (IsCustomGroupsMode)
                {
                    int groups = GetSelectedImportBatches().Count;
                    return "선택 " + SelectedCount + "건 → " + groups + "건으로 가져오기";
                }
                return "선택 " + SelectedCount + "건 각각 가져오기";
            }
        }

        public TfsImportCandidateRow FocusedCandidate
        {
            get { return _focused; }
            private set
            {
                if (SetProperty(ref _focused, value))
                {
                    RaisePropertyChanged("HasFocus");
                    RaisePropertyChanged("FocusedFiles");
                    RaisePropertyChanged("FocusedFileCount");
                    RaisePropertyChanged("FocusedHiddenFileCount");
                    RaisePropertyChanged("FocusedIncludedFileCount");
                    RaisePropertyChanged("HasFocusedHiddenFiles");
                    RaisePropertyChanged("FocusedHiddenFilesLabel");
                    RaisePropertyChanged("HasFocusedFiles");
                    RaisePropertyChanged("ShowFocusedFilesEmpty");
                }
            }
        }

        public bool HasFocus { get { return FocusedCandidate != null; } }

        public bool HasFocusedFiles
        {
            get { return FocusedCandidate != null && FocusedCandidate.Files != null && FocusedCandidate.Files.Count > 0; }
        }

        public bool ShowFocusedFilesEmpty
        {
            get { return HasFocus && !HasFocusedFiles; }
        }

        public ObservableCollection<TfsImportFileRow> FocusedFiles
        {
            get
            {
                return FocusedCandidate != null
                    ? FocusedCandidate.Files
                    : new ObservableCollection<TfsImportFileRow>();
            }
        }

        public int FocusedFileCount
        {
            get { return FocusedCandidate != null ? FocusedCandidate.Files.Count : 0; }
        }

        public int FocusedHiddenFileCount
        {
            get { return FocusedCandidate != null ? FocusedCandidate.HiddenFileCount : 0; }
        }

        public int FocusedIncludedFileCount
        {
            get { return FocusedCandidate != null ? FocusedCandidate.IncludedFileCount : 0; }
        }

        public bool HasFocusedHiddenFiles
        {
            get { return FocusedHiddenFileCount > 0; }
        }

        public string FocusedHiddenFilesLabel
        {
            get
            {
                int n = FocusedHiddenFileCount;
                if (n <= 0)
                    return string.Empty;
                return "숨긴 파일 " + n + "개";
            }
        }

        public bool HasCandidates { get { return Candidates.Count > 0; } }
        public bool IsEmpty { get { return Candidates.Count == 0; } }

        public int SelectedCount
        {
            get { return Candidates.Count(c => c.IsImportSelected); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value ?? string.Empty); }
        }

        public ICommand SelectAllCommand { get; private set; }
        public ICommand ClearSelectionCommand { get; private set; }
        public ICommand FocusCandidateCommand { get; private set; }
        public ICommand SetImportModeCommand { get; private set; }
        public ICommand CycleMergeGroupCommand { get; private set; }
        public ICommand ToggleFileIncludeCommand { get; private set; }

        /// <summary>호스트(ListVM)에서 연결 — 취소.</summary>
        public ICommand CloseCommand { get; set; }

        /// <summary>호스트(ListVM)에서 연결 — 가져오기 확정.</summary>
        public ICommand ConfirmCommand { get; set; }

        public void Load(
            IEnumerable<TfsChangesetCandidateViewModel> source,
            ISet<int> alreadyImportedChangesetIds,
            string statusMessage)
        {
            foreach (var old in Candidates)
            {
                if (old != null)
                    old.PropertyChanged -= OnCandidatePropertyChanged;
            }
            Candidates.Clear();
            FocusedCandidate = null;
            StatusMessage = statusMessage ?? string.Empty;
            ImportMode = ModeSeparate;

            if (source != null)
            {
                foreach (var c in source.OrderByDescending(x => x.CheckedInAt ?? DateTime.MinValue))
                {
                    if (c == null)
                        continue;
                    if (alreadyImportedChangesetIds != null
                        && alreadyImportedChangesetIds.Contains(c.ChangesetId))
                        continue;
                    if (string.IsNullOrWhiteSpace(c.RemoteComputerName)
                        && !string.IsNullOrWhiteSpace(SessionPc))
                        c.RemoteComputerName = SessionPc;
                    var row = TfsImportCandidateRow.FromCandidate(c, alreadyImported: false);
                    row.MergeGroup = 1;
                    row.IsImportSelected = true;
                    row.PropertyChanged += OnCandidatePropertyChanged;
                    Candidates.Add(row);
                }
            }

            RaisePropertyChanged("HasCandidates");
            RaisePropertyChanged("IsEmpty");
            RaisePropertyChanged("ShowSessionPickerWithCandidates");
            RaisePropertyChanged("SelectedCount");
            RaisePropertyChanged("ShowCustomGroupsTab");
            EnsureModeMatchesSelection();
            RaisePropertyChanged("ConfirmButtonLabel");
            RaisePropertyChanged("CanConfirm");

            if (Candidates.Count > 0)
                FocusCandidate(Candidates[0]);
        }

        public void PrepareSessionQuery(string preferredSite)
        {
            string site = (preferredSite ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(site)
                || string.Equals(site, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                site = WorkLogSiteCodes.Aurora;
            SessionSite = site;
            SessionDateText = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            SessionStartTimeText = string.Empty;
            SessionEndTimeText = string.Empty;
            SessionPc = null;
            SessionSearchMessage = string.Empty;
        }

        public void ReplaceSessionPcOptions(IEnumerable<string> pcs)
        {
            string keep = SessionPc;
            SessionPcOptions.Clear();
            SessionPcGroups.Clear();
            var chips = new List<TfsImportPcChip>();
            if (pcs != null)
            {
                foreach (string pc in pcs)
                {
                    if (string.IsNullOrWhiteSpace(pc))
                        continue;
                    string value = pc.Trim();
                    SessionPcOptions.Add(value);
                    string team = TeamAccent.Resolve(null, null, null, value, SessionSite);
                    chips.Add(new TfsImportPcChip
                    {
                        Value = value,
                        TeamName = team
                    });
                }
            }

            chips.Sort(ComparePcChip);
            TfsImportPcTeamGroup group = null;
            string current = null;
            for (int i = 0; i < chips.Count; i++)
            {
                TfsImportPcChip chip = chips[i];
                string name = string.IsNullOrWhiteSpace(chip.TeamName) ? string.Empty : chip.TeamName.Trim();
                if (group == null || !string.Equals(current, name, StringComparison.Ordinal))
                {
                    group = new TfsImportPcTeamGroup(name);
                    SessionPcGroups.Add(group);
                    current = name;
                }
                group.Computers.Add(chip);
            }

            if (!string.IsNullOrWhiteSpace(keep)
                && SessionPcOptions.Contains(keep))
                SessionPc = keep;
            else
                SessionPc = SessionPcOptions.Count == 1 ? SessionPcOptions[0] : null;
            RefreshPcChipSelection();
            RaisePropertyChanged("CanFetchSessionQuery");
        }

        private static int ComparePcChip(TfsImportPcChip left, TfsImportPcChip right)
        {
            string a = left != null ? left.TeamName : null;
            string b = right != null ? right.TeamName : null;
            int g = PcNameNaturalSort.CompareGroup(a, b);
            if (g != 0)
                return g;
            return PcNameNaturalSort.Compare(
                left != null ? left.Value : null,
                right != null ? right.Value : null);
        }

        private void SelectSessionPc(string value)
        {
            SessionPc = value;
        }

        private void RefreshPcChipSelection()
        {
            if (SessionPcGroups == null)
                return;
            for (int i = 0; i < SessionPcGroups.Count; i++)
            {
                TfsImportPcTeamGroup group = SessionPcGroups[i];
                if (group == null || group.Computers == null)
                    continue;
                for (int j = 0; j < group.Computers.Count; j++)
                {
                    TfsImportPcChip chip = group.Computers[j];
                    if (chip == null)
                        continue;
                    chip.IsSelected = string.Equals(chip.Value, SessionPc, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public bool TryGetSearchWindow(out DateTime fromAt, out DateTime toAt, out string error)
        {
            fromAt = default(DateTime);
            toAt = default(DateTime);
            error = null;

            DateTime? date;
            if (!WorkLogDraftMapper.TryParseDateText(SessionDateText, out date, out error) || !date.HasValue)
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "날짜를 입력해 주세요.";
                return false;
            }

            TimeSpan? start;
            TimeSpan? end;
            string timeErr;
            if (!WorkLogDraftMapper.TryParseTimeText(SessionStartTimeText, out start, out timeErr))
            {
                error = timeErr;
                return false;
            }
            if (!WorkLogDraftMapper.TryParseTimeText(SessionEndTimeText, out end, out timeErr))
            {
                error = timeErr;
                return false;
            }

            fromAt = date.Value.Date.Add(start ?? TimeSpan.Zero);
            toAt = date.Value.Date.Add(end ?? new TimeSpan(23, 59, 59));
            if (toAt < fromAt)
                toAt = toAt.AddDays(1);
            return true;
        }

        /// <summary>기존 업무기록 목록을 추가 대상(검색) 목록에 채운다.</summary>
        public void SetAppendTargets(IEnumerable<WorkLogListItemViewModel> items, string preferredSiteCode)
        {
            AppendTargets.Clear();
            SelectedAppendTarget = null;
            _appendSearchText = string.Empty;
            RaisePropertyChanged("AppendSearchText");

            var preferred = new List<TfsImportAppendTarget>();
            var others = new List<TfsImportAppendTarget>();

            if (items != null)
            {
                foreach (var item in items
                    .Where(i => i != null && (i.IsOwnedByCurrentUser || i.IsDraft))
                    .OrderByDescending(i => i.LastModifiedAt)
                    .ThenByDescending(i => i.CheckedInAt ?? DateTime.MinValue))
                {
                    var target = TfsImportAppendTarget.FromItem(item);
                    if (target == null)
                        continue;

                    bool sameSite = string.IsNullOrWhiteSpace(preferredSiteCode)
                        || string.IsNullOrWhiteSpace(item.SiteCode)
                        || string.Equals(item.SiteCode, preferredSiteCode, StringComparison.OrdinalIgnoreCase);
                    if (sameSite)
                        preferred.Add(target);
                    else
                        others.Add(target);
                }
            }

            // 같은 사이트 우선, 검색 시 다른 사이트도 전부 포함
            foreach (var t in preferred)
                AppendTargets.Add(t);
            foreach (var t in others)
                AppendTargets.Add(t);

            RaisePropertyChanged("ShowAppendTargetPicker");
            RaisePropertyChanged("HasAppendTargets");
            RaisePropertyChanged("ImportModeHint");
            RaisePropertyChanged("HasImportModeHint");
            RaisePropertyChanged("ConfirmButtonLabel");
            RaisePropertyChanged("CanConfirm");
            RaisePropertyChanged("SelectedAppendTargetSummary");
            RaisePropertyChanged("HasSelectedAppendTarget");
            RaisePropertyChanged("ShowAppendTargetList");

            if (IsAppendExistingMode)
                SuggestAppendTargetFromSelection();
            else
                ApplyAppendFilter();
        }

        private void SelectAppendTarget(TfsImportAppendTarget target)
        {
            if (target == null)
                return;
            SelectedAppendTarget = target;
        }

        private void ApplyAppendFilter()
        {
            FilteredAppendTargets.Clear();
            string q = (AppendSearchText ?? string.Empty).Trim();

            List<TfsImportAppendTarget> list;
            if (!string.IsNullOrEmpty(q))
            {
                string[] tokens = q.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                list = AppendTargets
                    .Where(t => t != null && t.MatchesAllTokens(tokens))
                    .ToList();
            }
            else
            {
                // 검색 전: 추천 TN과 매칭되는 기존 기록만 (없으면 리스트 숨김)
                list = CollectRecommendedAppendTargets();
            }

            // 이미 고른 대상이 필터 밖이면 목록 맨 위에 유지 (선택 해제 방지)
            if (SelectedAppendTarget != null && !list.Contains(SelectedAppendTarget))
                list.Insert(0, SelectedAppendTarget);

            foreach (var t in list)
                FilteredAppendTargets.Add(t);

            RaisePropertyChanged("ShowAppendTargetList");
        }

        private HashSet<string> CollectSelectedTicketKeys()
        {
            var ticketKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Candidates.Where(c => c != null && c.IsImportSelected))
            {
                string raw = TfsWorkLogParser.ExtractTicketNumbers(row.OriginalComment ?? string.Empty);
                foreach (var part in WorkLogDraftMapper.SplitMultiValues(raw))
                {
                    string key = WorkLogDraftMapper.NormalizeTicketStorage(part);
                    if (!string.IsNullOrEmpty(key))
                        ticketKeys.Add(key);
                }
            }
            return ticketKeys;
        }

        private List<TfsImportAppendTarget> CollectRecommendedAppendTargets()
        {
            var ticketKeys = CollectSelectedTicketKeys();
            if (ticketKeys.Count == 0)
                return new List<TfsImportAppendTarget>();

            return AppendTargets
                .Where(t => t != null && t.Item != null && TicketKeysOverlap(t.Item.TicketNo, ticketKeys))
                .ToList();
        }

        private static bool TicketKeysOverlap(string ticketNo, HashSet<string> ticketKeys)
        {
            if (ticketKeys == null || ticketKeys.Count == 0)
                return false;
            foreach (var part in WorkLogDraftMapper.SplitMultiValues(ticketNo))
            {
                string key = WorkLogDraftMapper.NormalizeTicketStorage(part);
                if (!string.IsNullOrEmpty(key) && ticketKeys.Contains(key))
                    return true;
            }
            string whole = WorkLogDraftMapper.NormalizeTicketStorage(ticketNo);
            if (!string.IsNullOrEmpty(whole))
            {
                foreach (var part in WorkLogDraftMapper.SplitMultiValues(whole))
                {
                    string key = WorkLogDraftMapper.NormalizeTicketStorage(part);
                    if (!string.IsNullOrEmpty(key) && ticketKeys.Contains(key))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 선택 체크인을 가져오기 배치로 반환. 각 행의 포함(IsIncluded) 파일만 Changeset에 반영.
        /// </summary>
        public IList<IList<TfsImportCandidateRow>> GetSelectedImportBatches()
        {
            var selected = Candidates
                .Where(c => c.IsImportSelected && c.Source != null)
                .ToList();

            if (selected.Count == 0)
                return new List<IList<TfsImportCandidateRow>>();

            // 기존에 추가 / 전부 합치기 → 한 배치
            if (IsMergeAllMode || IsAppendExistingMode)
            {
                return new List<IList<TfsImportCandidateRow>>
                {
                    selected.Cast<TfsImportCandidateRow>().ToList()
                };
            }

            if (IsCustomGroupsMode)
            {
                return selected
                    .GroupBy(c => c.MergeGroup)
                    .OrderBy(g => g.Key)
                    .Select(g => (IList<TfsImportCandidateRow>)g.ToList())
                    .ToList();
            }

            return selected
                .Select(c => (IList<TfsImportCandidateRow>)new List<TfsImportCandidateRow> { c })
                .ToList();
        }

        public void SuggestAppendTargetFromSelection()
        {
            if (AppendTargets.Count == 0)
            {
                SelectedAppendTarget = null;
                ApplyAppendFilter();
                return;
            }

            var matches = CollectRecommendedAppendTargets();
            if (matches.Count == 1)
            {
                SelectedAppendTarget = matches[0];
            }
            else if (matches.Count > 1)
            {
                // 여러 추천이면 자동 선택하지 않고 리스트만 보여 사용자가 고르게 함
                if (SelectedAppendTarget == null || !matches.Contains(SelectedAppendTarget))
                    SelectedAppendTarget = null;
            }

            ApplyAppendFilter();
        }

        private void ToggleFileInclude(TfsImportFileRow file)
        {
            if (file == null)
                return;
            file.IsIncluded = !file.IsIncluded;
            if (FocusedCandidate != null)
            {
                FocusedCandidate.RaiseFileIncludeChanged();
                RaisePropertyChanged("FocusedHiddenFileCount");
                RaisePropertyChanged("FocusedIncludedFileCount");
                RaisePropertyChanged("HasFocusedHiddenFiles");
                RaisePropertyChanged("FocusedHiddenFilesLabel");
            }
            RaisePropertyChanged("CanConfirm");
            RaisePropertyChanged("ConfirmButtonLabel");
        }

        private void OnCandidatePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsImportSelected")
            {
                RaisePropertyChanged("SelectedCount");
                RaisePropertyChanged("ShowCustomGroupsTab");
                EnsureModeMatchesSelection();
                if (IsCustomGroupsMode)
                    AssignCyclingGroups();
                if (IsAppendExistingMode)
                    SuggestAppendTargetFromSelection();
                RaisePropertyChanged("ConfirmButtonLabel");
                RaisePropertyChanged("CanConfirm");
            }
            else if (e.PropertyName == "MergeGroup")
            {
                RaisePropertyChanged("ConfirmButtonLabel");
            }
            else if (e.PropertyName == "IncludedFileCount")
            {
                RaisePropertyChanged("CanConfirm");
                RaisePropertyChanged("ConfirmButtonLabel");
            }
        }

        private void EnsureModeMatchesSelection()
        {
            if (IsCustomGroupsMode && SelectedCount < 3)
                ImportMode = ModeSeparate;
            RaisePropertyChanged("ShowCustomGroupsTab");
            RaisePropertyChanged("MaxMergeGroups");
            RaisePropertyChanged("ImportModeHint");
            RaisePropertyChanged("HasImportModeHint");
            RaisePropertyChanged("CanConfirm");
        }

        private void SetImportMode(string mode)
        {
            string next = NormalizeMode(mode);
            if (next == ModeCustomGroups && SelectedCount < 3)
                return;
            if (next == ModeAppendExisting && AppendTargets.Count == 0)
                return;
            ImportMode = next;
        }

        private static string NormalizeMode(string mode)
        {
            if (string.Equals(mode, ModeMergeAll, StringComparison.OrdinalIgnoreCase))
                return ModeMergeAll;
            if (string.Equals(mode, ModeCustomGroups, StringComparison.OrdinalIgnoreCase))
                return ModeCustomGroups;
            if (string.Equals(mode, ModeAppendExisting, StringComparison.OrdinalIgnoreCase))
                return ModeAppendExisting;
            return ModeSeparate;
        }

        private void ApplyModeToRows()
        {
            if (Candidates.Count == 0)
                return;

            if (IsMergeAllMode || IsAppendExistingMode)
            {
                foreach (var c in Candidates)
                    c.MergeGroup = 1;
            }
            else if (IsSeparateMode)
            {
                foreach (var c in Candidates)
                    c.MergeGroup = 1;
            }
            else if (IsCustomGroupsMode)
            {
                AssignCyclingGroups();
            }

            RaisePropertyChanged("ConfirmButtonLabel");
            RaisePropertyChanged("ImportModeHint");
            RaisePropertyChanged("HasImportModeHint");
            RaisePropertyChanged("ShowCustomGroupsTab");
        }

        /// <summary>선택 N건 → 1..(N-1) 반복 배정. 예: 3건→1,2,1 / 4건→1,2,3,1</summary>
        private void AssignCyclingGroups()
        {
            int max = MaxMergeGroups;
            if (max < 1)
                max = 1;

            var selected = Candidates
                .Where(c => c.IsImportSelected)
                .OrderByDescending(c => c.Source != null && c.Source.CheckedInAt.HasValue
                    ? c.Source.CheckedInAt.Value
                    : DateTime.MinValue)
                .ToList();

            for (int i = 0; i < selected.Count; i++)
                selected[i].SetMergeGroup(max, (i % max) + 1);

            foreach (var c in Candidates.Where(x => !x.IsImportSelected))
                c.SetMergeGroup(max, 1);

            RaisePropertyChanged("MaxMergeGroups");
            RaisePropertyChanged("ConfirmButtonLabel");
            RaisePropertyChanged("ImportModeHint");
        }

        private void CycleMergeGroup(TfsImportCandidateRow row)
        {
            if (row == null || !IsCustomGroupsMode)
                return;
            int max = MaxMergeGroups;
            if (max < 1)
                max = 1;
            int next = row.MergeGroup + 1;
            if (next > max)
                next = 1;
            row.SetMergeGroup(max, next);
            RaisePropertyChanged("ConfirmButtonLabel");
        }

        private void SelectAll()
        {
            foreach (var c in Candidates)
                c.IsImportSelected = true;
            RaisePropertyChanged("SelectedCount");
            RaisePropertyChanged("ShowCustomGroupsTab");
            if (IsCustomGroupsMode)
                AssignCyclingGroups();
            EnsureModeMatchesSelection();
            RaisePropertyChanged("ConfirmButtonLabel");
        }

        private void ClearSelection()
        {
            foreach (var c in Candidates)
                c.IsImportSelected = false;
            RaisePropertyChanged("SelectedCount");
            RaisePropertyChanged("ShowCustomGroupsTab");
            EnsureModeMatchesSelection();
            RaisePropertyChanged("ConfirmButtonLabel");
        }

        private void FocusCandidate(TfsImportCandidateRow row)
        {
            if (row == null)
                return;
            foreach (var c in Candidates)
                c.IsFocused = c == row;
            FocusedCandidate = row;
        }
    }

    public sealed class TfsImportPcChip : ViewModelBase
    {
        private bool _isSelected;

        public string Value { get; set; }
        public string TeamName { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }

    public sealed class TfsImportPcTeamGroup
    {
        public TfsImportPcTeamGroup(string name)
        {
            Name = name ?? string.Empty;
            Computers = new ObservableCollection<TfsImportPcChip>();
        }

        public string Name { get; private set; }

        public bool HasName
        {
            get { return !string.IsNullOrWhiteSpace(Name); }
        }

        public ObservableCollection<TfsImportPcChip> Computers { get; private set; }
    }

    public sealed class TfsImportCandidateRow : ViewModelBase
    {
        private bool _isImportSelected;
        private bool _isFocused;
        private int _mergeGroup = 1;

        public TfsChangesetCandidateViewModel Source { get; private set; }
        public ObservableCollection<TfsImportFileRow> Files { get; private set; }

        public int ChangesetId { get; private set; }
        public string AuthorDisplay { get; private set; }
        public string CheckedInAtDisplay { get; private set; }
        public string OriginalComment { get; private set; }
        public int ChangedFileCount { get; private set; }
        public string InboxStatusLabel { get; private set; }
        public bool HasInboxStatus { get { return !string.IsNullOrWhiteSpace(InboxStatusLabel); } }

        public int IncludedFileCount
        {
            get { return Files == null ? 0 : Files.Count(f => f != null && f.IsIncluded); }
        }

        /// <summary>현재 흐리게(미포함) 표시 중인 파일 수.</summary>
        public int HiddenFileCount
        {
            get { return Files == null ? 0 : Files.Count(f => f != null && !f.IsIncluded); }
        }

        public string FileCountSummary
        {
            get
            {
                int total = Files != null ? Files.Count : ChangedFileCount;
                int hidden = HiddenFileCount;
                if (hidden > 0)
                    return "변경 파일 " + total + "개 · 숨긴 파일 " + hidden + "개";
                return "변경 파일 " + total + "개";
            }
        }

        public void RaiseFileIncludeChanged()
        {
            RaisePropertyChanged("IncludedFileCount");
            RaisePropertyChanged("HiddenFileCount");
            RaisePropertyChanged("FileCountSummary");
        }

        /// <summary>포함 체크된 파일만 담은 Changeset (가져오기용).</summary>
        public TfsChangesetItem BuildChangesetForImport()
        {
            if (Source == null)
                return null;

            var item = Source.ToChangesetItem();
            var files = new List<TfsChangedFileItem>();
            if (Files != null)
            {
                foreach (var f in Files)
                {
                    if (f == null || !f.IsIncluded)
                        continue;
                    files.Add(f.ToDto());
                }
            }
            item.Files = files;
            item.ChangedFileCount = files.Count;
            return item;
        }

        public bool IsImportSelected
        {
            get { return _isImportSelected; }
            set { SetProperty(ref _isImportSelected, value); }
        }

        public bool IsFocused
        {
            get { return _isFocused; }
            set { SetProperty(ref _isFocused, value); }
        }

        public int MergeGroup
        {
            get { return _mergeGroup; }
            set { SetMergeGroup(9, value); }
        }

        public void SetMergeGroup(int maxAllowed, int value)
        {
            if (maxAllowed < 1)
                maxAllowed = 1;
            int next = value < 1 ? 1 : (value > maxAllowed ? maxAllowed : value);
            if (!SetProperty(ref _mergeGroup, next))
                return;
            RaisePropertyChanged("MergeGroupLabel");
            RaisePropertyChanged("MergeGroupBackground");
            RaisePropertyChanged("MergeGroupForegroundBrush");
        }

        public string MergeGroupLabel
        {
            get { return _mergeGroup.ToString(); }
        }

        public Brush MergeGroupBackground
        {
            get { return GroupBg(_mergeGroup); }
        }

        public Brush MergeGroupForegroundBrush
        {
            get { return GroupFg(_mergeGroup); }
        }

        private static Brush GroupBg(int g)
        {
            switch (g)
            {
                case 1: return BrushFrom("#DBEAFE");
                case 2: return BrushFrom("#D1FAE5");
                case 3: return BrushFrom("#FFEDD5");
                case 4: return BrushFrom("#EDE9FE");
                case 5: return BrushFrom("#FCE7F3");
                default: return BrushFrom("#F1F5F9");
            }
        }

        private static Brush GroupFg(int g)
        {
            switch (g)
            {
                case 1: return BrushFrom("#1D4ED8");
                case 2: return BrushFrom("#047857");
                case 3: return BrushFrom("#C2410C");
                case 4: return BrushFrom("#6D28D9");
                case 5: return BrushFrom("#BE185D");
                default: return BrushFrom("#475569");
            }
        }

        private static Brush BrushFrom(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }

        public static TfsImportCandidateRow FromCandidate(
            TfsChangesetCandidateViewModel c,
            bool alreadyImported)
        {
            var row = new TfsImportCandidateRow
            {
                Source = c,
                ChangesetId = c.ChangesetId,
                AuthorDisplay = c.AuthorDisplay,
                CheckedInAtDisplay = c.CheckedInAtDisplay,
                OriginalComment = c.OriginalComment ?? string.Empty,
                ChangedFileCount = c.ChangedFileCount,
                InboxStatusLabel = ResolveInboxStatusLabel(c.ChangesetId, alreadyImported),
                Files = new ObservableCollection<TfsImportFileRow>()
            };

            if (c.SourceFiles != null)
            {
                foreach (var f in c.SourceFiles)
                {
                    if (f == null)
                        continue;
                    row.Files.Add(TfsImportFileRow.FromDto(f));
                }
            }

            if (row.ChangedFileCount <= 0)
                row.ChangedFileCount = row.Files.Count;

            return row;
        }

        private static string ResolveInboxStatusLabel(int changesetId, bool alreadyImported)
        {
            if (alreadyImported)
                return TfsCheckinInboxStatus.ToLabel(TfsCheckinInboxStatus.Reported);
            string status = TfsCheckinInboxStore.GetStatus(changesetId);
            return string.IsNullOrWhiteSpace(status) ? null : TfsCheckinInboxStatus.ToLabel(status);
        }
    }

    /// <summary>TFS 가져오기 — 기존 업무기록 추가 대상.</summary>
    public sealed class TfsImportAppendTarget
    {
        public WorkLogListItemViewModel Item { get; set; }
        public string DisplayLabel { get; set; }
        public string DetailLabel { get; set; }
        /// <summary>티켓·메뉴·내용 등 전체 컬럼을 합친 검색용 문자열.</summary>
        public string SearchBlob { get; set; }

        public static TfsImportAppendTarget FromItem(WorkLogListItemViewModel item)
        {
            if (item == null)
                return null;

            string ticket = string.IsNullOrWhiteSpace(item.TicketDisplay)
                ? "(티켓 없음)"
                : item.TicketDisplay.Trim();
            string menu = string.IsNullOrWhiteSpace(item.MenuName) ? "-" : item.MenuName.Trim();
            string cs = item.ChangesetId > 0 ? "CS " + item.ChangesetId : "초안";
            if (item.SourceChangesetIds != null && item.SourceChangesetIds.Count > 1)
                cs = "CS " + item.SourceChangesetIds.Count + "건";

            string person = string.IsNullOrWhiteSpace(item.PersonInCharge)
                ? string.Empty
                : WorkLogDraftMapper.FormatPersonDisplay(item.PersonInCharge);
            string author = item.AuthorDisplayName;
            if (string.Equals(author, "알 수 없음", StringComparison.Ordinal))
                author = string.Empty;
            string site = string.IsNullOrWhiteSpace(item.SiteCode) ? string.Empty : item.SiteCode.Trim();
            string contentPreview = BuildContentPreview(item);

            var blobParts = new List<string>();
            AppendBlob(blobParts, item.TicketNo);
            AppendBlob(blobParts, item.TicketDisplay);
            AppendBlob(blobParts, item.TicketContents);
            AppendBlob(blobParts, item.MenuName);
            AppendBlob(blobParts, item.PersonInCharge);
            AppendBlob(blobParts, person);
            AppendBlob(blobParts, item.AuthorName);
            AppendBlob(blobParts, item.TeamName);
            AppendBlob(blobParts, author);
            AppendBlob(blobParts, item.TfsAuthor);
            AppendBlob(blobParts, item.Pc);
            AppendBlob(blobParts, item.SiteCode);
            AppendBlob(blobParts, item.Comment);
            AppendBlob(blobParts, item.TfsComment);
            AppendBlob(blobParts, item.ListCommentFullText);
            AppendBlob(blobParts, item.Id);
            if (item.DbLogId > 0)
                AppendBlob(blobParts, item.DbLogId.ToString());
            if (item.ChangesetId > 0)
                AppendBlob(blobParts, item.ChangesetId.ToString());
            if (item.SourceChangesetIds != null)
            {
                foreach (int id in item.SourceChangesetIds)
                    AppendBlob(blobParts, id.ToString());
            }
            if (item.Groups != null)
            {
                foreach (var g in item.Groups)
                {
                    if (g == null)
                        continue;
                    AppendBlob(blobParts, g.Type);
                    AppendBlob(blobParts, g.Category);
                    AppendBlob(blobParts, g.ProjectName);
                    AppendBlob(blobParts, g.Comment);
                    AppendBlob(blobParts, g.PrimarySourceName);
                    AppendBlob(blobParts, g.SourcesTooltip);
                    AppendBlob(blobParts, g.DeploymentStatus);
                }
            }
            if (item.Sources != null)
            {
                foreach (var s in item.Sources)
                {
                    if (s == null)
                        continue;
                    AppendBlob(blobParts, s.FileName);
                    AppendBlob(blobParts, s.OriginalPath);
                    AppendBlob(blobParts, s.ChangeDetailText);
                    AppendBlob(blobParts, s.Type);
                    AppendBlob(blobParts, s.Category);
                    AppendBlob(blobParts, s.ProjectName);
                }
            }

            string detail = contentPreview;
            if (!string.IsNullOrEmpty(person))
                detail = string.IsNullOrEmpty(detail) ? person : person + " · " + detail;
            else if (!string.IsNullOrEmpty(author))
                detail = string.IsNullOrEmpty(detail) ? author : author + " · " + detail;
            if (!string.IsNullOrEmpty(site))
                detail = string.IsNullOrEmpty(detail) ? site : site + " · " + detail;

            return new TfsImportAppendTarget
            {
                Item = item,
                DisplayLabel = ticket + " · " + menu + " · " + cs,
                DetailLabel = detail,
                SearchBlob = string.Join(" ", blobParts)
            };
        }

        public bool MatchesAllTokens(IEnumerable<string> tokens)
        {
            if (tokens == null)
                return true;
            string hay = SearchBlob ?? string.Empty;
            foreach (string token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                if (hay.IndexOf(token.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            return true;
        }

        private static void AppendBlob(List<string> parts, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(value))
                return;
            parts.Add(value.Trim());
        }

        private static string BuildContentPreview(WorkLogListItemViewModel item)
        {
            if (item == null)
                return string.Empty;
            string text = item.ListCommentFullText;
            if (string.IsNullOrWhiteSpace(text))
                text = item.TicketContents;
            if (string.IsNullOrWhiteSpace(text))
                text = item.Comment;
            if (string.IsNullOrWhiteSpace(text))
                text = item.TfsComment;
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length > 80)
                return text.Substring(0, 80) + "…";
            return text;
        }

        public override string ToString()
        {
            return DisplayLabel ?? string.Empty;
        }
    }

    public sealed class TfsImportFileRow : ViewModelBase
    {
        private bool _isIncluded;

        public string FileName { get; set; }
        public string ServerPath { get; set; }
        public string ChangeType { get; set; }

        /// <summary>자동 추천이 아닌 메타/비후보 — 기본 숨김(흐림).</summary>
        public bool IsHiddenCandidate { get; private set; }

        public string CandidateRuleCode { get; private set; }
        public string CandidateReason { get; private set; }

        public bool IsIncluded
        {
            get { return _isIncluded; }
            set
            {
                if (!SetProperty(ref _isIncluded, value))
                    return;
                RaisePropertyChanged("IsDimmed");
                RaisePropertyChanged("RowOpacity");
                RaisePropertyChanged("IncludeHint");
            }
        }

        public bool IsDimmed
        {
            get { return !IsIncluded; }
        }

        public double RowOpacity
        {
            get { return IsIncluded ? 1.0 : 0.38; }
        }

        public string IncludeHint
        {
            get
            {
                if (IsIncluded)
                    return IsHiddenCandidate
                        ? "포함됨 — 클릭하면 다시 숨김"
                        : "포함됨 — 클릭하면 제외";
                return "숨김 — 클릭하면 업무기록에 추가";
            }
        }

        public string ChangeTypeMark
        {
            get
            {
                string c = (ChangeType ?? string.Empty).Trim().ToLowerInvariant();
                if (c.Contains("add") || c == "a")
                    return "A";
                if (c.Contains("delete") || c == "d")
                    return "D";
                if (c.Contains("rename") || c == "r")
                    return "R";
                if (c.Contains("edit") || c.Contains("mod") || c == "m" || c == "edit")
                    return "M";
                if (string.IsNullOrEmpty(c))
                    return "?";
                return c.Length <= 1 ? c.ToUpperInvariant() : c.Substring(0, 1).ToUpperInvariant();
            }
        }

        public bool IsAdd { get { return ChangeTypeMark == "A"; } }
        public bool IsDelete { get { return ChangeTypeMark == "D"; } }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(FileName))
                    return FileName;
                if (string.IsNullOrWhiteSpace(ServerPath))
                    return "(파일명 없음)";
                string p = ServerPath.Replace('\\', '/');
                int idx = p.LastIndexOf('/');
                return idx >= 0 && idx < p.Length - 1 ? p.Substring(idx + 1) : p;
            }
        }

        public static TfsImportFileRow FromDto(TfsChangedFileItem f)
        {
            var decision = TfsWorkLogCandidateClassifier.Classify(f.FileName, f.ServerPath);
            bool recommended = decision.Decision == TfsWorkLogCandidateClassifier.Decision.Recommended;
            var row = new TfsImportFileRow
            {
                FileName = f.FileName,
                ServerPath = f.ServerPath,
                ChangeType = f.ChangeType,
                IsHiddenCandidate = !recommended,
                CandidateRuleCode = decision.RuleCode,
                CandidateReason = decision.Reason
            };
            row._isIncluded = recommended;
            return row;
        }

        public TfsChangedFileItem ToDto()
        {
            return new TfsChangedFileItem
            {
                FileName = FileName,
                ServerPath = ServerPath,
                ChangeType = ChangeType
            };
        }
    }
}
