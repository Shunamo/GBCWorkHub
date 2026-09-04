using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>작성 상태 (UI 표시). 한글은 DTO 상수 공유.</summary>
    public static class WorkLogWriteStatus
    {
        public const string Draft = WorkLogWriteStatusLabels.Draft;
        public const string Completed = WorkLogWriteStatusLabels.Completed;
    }

    /// <summary>목록 페이지 번호 버튼 (1-based). PageNumber=0 이면 말줄임(…).</summary>
    public sealed class WorkLogPageNumberItem
    {
        public int PageNumber { get; set; }
        public bool IsCurrent { get; set; }

        public bool IsEllipsis
        {
            get { return PageNumber <= 0; }
        }

        public bool IsClickable
        {
            get { return !IsEllipsis && !IsCurrent; }
        }

        public string DisplayText
        {
            get { return IsEllipsis ? "…" : PageNumber.ToString(); }
        }
    }

    public sealed class TypeBadgeItem
    {
        public string Type { get; set; }
        public int Count { get; set; }
        public string DisplayText
        {
            get
            {
                string label = string.IsNullOrEmpty(Type)
                    || string.Equals(Type, WorkLogFieldMasters.Unselected, StringComparison.Ordinal)
                    ? WorkLogFieldMasters.Unselected
                    : Type;
                return label + " " + Count;
            }
        }
        public string TooltipText { get; set; }
        public bool IsClient
        {
            get { return string.Equals(Type, "Client", StringComparison.OrdinalIgnoreCase); }
        }
        public bool IsServer
        {
            get { return string.Equals(Type, "Server", StringComparison.OrdinalIgnoreCase); }
        }
        public bool IsDb
        {
            get { return string.Equals(Type, "DB Object", StringComparison.OrdinalIgnoreCase); }
        }
        public bool IsEqs
        {
            get { return string.Equals(Type, "EQS", StringComparison.OrdinalIgnoreCase); }
        }
        public bool IsUnclassified
        {
            get { return string.IsNullOrEmpty(Type); }
        }
    }

    /// <summary>날짜 Timeline 그룹 (CheckedInAt → Start → End → Deployment).</summary>
    public sealed class WorkLogDateGroupViewModel : ViewModelBase
    {
        private bool _isExpanded = true;

        public DateTime Date { get; set; }
        public bool IsUnknownDate { get; set; }
        public ObservableCollection<WorkLogListItemViewModel> Items { get; set; }

        public WorkLogDateGroupViewModel()
        {
            Items = new ObservableCollection<WorkLogListItemViewModel>();
            ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { SetProperty(ref _isExpanded, value); }
        }

        public ICommand ToggleExpandCommand { get; private set; }

        public string DateHeader
        {
            get
            {
                if (IsUnknownDate)
                    return "체크인 일시 미확인";
                return Date.ToString("yyyy년 M월 d일");
            }
        }

        public int ItemCount
        {
            get { return Items != null ? Items.Count : 0; }
        }

        public string DateHeaderWithCount
        {
            get { return DateHeader + " · " + ItemCount + "건"; }
        }
    }

    /// <summary>목록 Row용 WorkGroup 요약 (공통 업무정보 없음).</summary>
    public sealed class WorkGroupSummaryItem : ViewModelBase
    {
        public string Type { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string SourceOrigin { get; set; }
        public int SourceCount { get; set; }
        public string PrimarySourceName { get; set; }
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }
        public string AppliedRuleCode { get; set; }
        public string DeploymentStatus { get; set; }
        public DateTime? DeploymentDate { get; set; }
        public string Comment { get; set; }

        /// <summary>전체 Source 목록 + OriginalPath (Tooltip용).</summary>
        public string SourcesTooltip { get; set; }

        public string TypeCategoryLabel
        {
            get
            {
                string t = string.IsNullOrEmpty(Type)
                    || string.Equals(Type, WorkLogFieldMasters.Unselected, System.StringComparison.Ordinal)
                    ? WorkLogFieldMasters.Unselected
                    : Type;
                if (string.IsNullOrEmpty(Category)
                    || string.Equals(Category, WorkLogFieldMasters.Unselected, System.StringComparison.Ordinal))
                    return t;
                return t + " / " + Category;
            }
        }

        public string SourceSummaryText
        {
            get
            {
                if (SourceCount <= 1)
                    return PrimarySourceName ?? string.Empty;
                return (PrimarySourceName ?? "항목") + " 외 " + (SourceCount - 1) + "개";
            }
        }

        public string OriginSummary
        {
            get { return "출처 " + (SourceOrigin ?? "TFS") + " · " + SourceCount + "개"; }
        }
    }

    /// <summary>메인 목록의 Changeset/업무 1건 — Timeline 압축 Row용.</summary>
    public sealed class WorkLogListItemViewModel : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isSelected;
        private bool _needsTicketReview;
        private string _ticketNo;

        public string Id { get; set; }
        /// <summary>XSUP.MSDWHTKD_WRK.LOG_ID (0이면 미저장).</summary>
        public long DbLogId { get; set; }
        /// <summary>사이트 코드 (AURORA / RC).</summary>
        public string SiteCode { get; set; }

        public string TicketNo
        {
            get { return _ticketNo; }
            set
            {
                if (SetProperty(ref _ticketNo, value))
                {
                    RaisePropertyChanged("TicketDisplay");
                    RaisePropertyChanged("HasTicket");
                    RaisePropertyChanged("NeedsTicketReview");
                }
            }
        }

        public string TicketContents { get; set; }
        public string MenuName { get; set; }
        public string Pc { get; set; }
        /// <summary>담당자 이름 (사용자가 기입).</summary>
        public string PersonInCharge { get; set; }
        /// <summary>
        /// 작성 PC 로컬 IP (소유권 추적용). UI에서 타인에게 노출하지 않음.
        /// </summary>
        public string LocalPcIp { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string DeploymentStatus { get; set; }
        public DateTime? DeploymentDate { get; set; }

        private string _comment;
        public string Comment
        {
            get { return _comment; }
            set
            {
                if (SetProperty(ref _comment, value))
                {
                    RaisePropertyChanged("HasWorkComment");
                    RaisePropertyChanged("WorkCommentDisplay");
                }
            }
        }

        public string WriteStatus { get; set; }
        public DateTime LastModifiedAt { get; set; }

        public int ChangesetId { get; set; }
        /// <summary>합쳐진 체크인 ID 목록 (표시/중복판정용).</summary>
        public List<int> SourceChangesetIds { get; set; }

        public string TfsComment { get; set; }
        public string TfsAuthor { get; set; }
        /// <summary>화면 표시용 작성자명 (한 번만 표시).</summary>
        public string AuthorName { get; set; }
        /// <summary>작성자 소속. 예: 진료지원.</summary>
        public string TeamName { get; set; }
        public DateTime? CheckedInAt { get; set; }

        /// <summary>페이로드 changedFileCount (files 배열이 비어 있어도 건수 표시용).</summary>
        public int PayloadChangedFileCount { get; set; }

        public ObservableCollection<WorkGroupSummaryItem> Groups { get; set; }
        public ObservableCollection<TypeBadgeItem> TypeBadges { get; set; }
        public ObservableCollection<WorkLogSourceEditItem> Sources { get; set; }

        public WorkLogListItemViewModel()
        {
            Groups = new ObservableCollection<WorkGroupSummaryItem>();
            TypeBadges = new ObservableCollection<TypeBadgeItem>();
            Sources = new ObservableCollection<WorkLogSourceEditItem>();
            SourceChangesetIds = new List<int>();
            WriteStatus = WorkLogWriteStatus.Draft;
        }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { SetProperty(ref _isExpanded, value); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public bool NeedsTicketReview
        {
            get
            {
                if (_needsTicketReview)
                    return true;
                return string.IsNullOrWhiteSpace(TicketNo);
            }
            set
            {
                if (SetProperty(ref _needsTicketReview, value))
                    RaisePropertyChanged("HasTicket");
            }
        }

        public bool HasTicket
        {
            get { return !NeedsTicketReview && !string.IsNullOrWhiteSpace(TicketNo); }
        }

        public int WorkGroupCount
        {
            get { return Groups != null ? Groups.Count : 0; }
        }

        public int SourceCount
        {
            get { return Sources != null ? Sources.Count : 0; }
        }

        public int ChangedFileCount
        {
            get
            {
                int fromSources = SourceCount;
                if (fromSources > 0)
                    return fromSources;
                return PayloadChangedFileCount > 0 ? PayloadChangedFileCount : 0;
            }
        }

        public bool HasChangedFileDetails
        {
            get { return Sources != null && Sources.Count > 0; }
        }

        public bool HasFileCountOnly
        {
            get { return !HasChangedFileDetails && PayloadChangedFileCount > 0; }
        }

        public int NeedsReviewCount
        {
            get
            {
                if (Sources == null)
                    return 0;
                return Sources.Count(s => s != null && s.NeedsReview);
            }
        }

        public bool HasNeedsReview
        {
            get { return NeedsReviewCount > 0; }
        }

        public bool IsDraft
        {
            get { return string.Equals(WriteStatus, WorkLogWriteStatus.Draft, StringComparison.Ordinal); }
        }

        public bool IsCompleted
        {
            get { return string.Equals(WriteStatus, WorkLogWriteStatus.Completed, StringComparison.Ordinal); }
        }

        /// <summary>Ticket Number 표시 (TN- 접두사).</summary>
        public string TicketNumberText
        {
            get { return WorkLogDraftMapper.FormatTicketDisplay(TicketNo); }
        }

        public string ListMenuSummary
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(MenuName))
                    return MenuName.Trim();
                return "메뉴 미입력";
            }
        }

        public string ListRowMetaLine
        {
            get
            {
                string start = StartDate.HasValue
                    ? StartDate.Value.ToString("yyyy-MM-dd")
                    : (CheckedInAt.HasValue ? CheckedInAt.Value.ToString("yyyy-MM-dd") : "-");
                return ListMenuSummary
                    + OccupancyNameStore.NameTeamSeparator
                    + ListPersonText
                    + OccupancyNameStore.NameTeamSeparator
                    + start;
            }
        }

        // ---- Glass 목록 행 표시 (실제 필드만, 빈 값은 Collapsed용) ----

        public bool HasListTitle
        {
            get { return !string.IsNullOrWhiteSpace(ContentsDisplay); }
        }

        public string ListTitleText
        {
            get { return ContentsDisplay; }
        }

        public bool HasListMenu
        {
            get { return !string.IsNullOrWhiteSpace(MenuName); }
        }

        public string ListMenuText
        {
            get { return HasListMenu ? MenuName.Trim() : string.Empty; }
        }

        public bool HasListPerson
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PersonInCharge)
                    || !string.IsNullOrWhiteSpace(AuthorName)
                    || !string.IsNullOrWhiteSpace(TfsAuthor)
                    || !string.IsNullOrWhiteSpace(TeamName);
            }
        }

        public string ListPersonText
        {
            get
            {
                string person;
                if (!string.IsNullOrWhiteSpace(PersonInCharge))
                    person = WorkLogDraftMapper.FormatPersonDisplay(PersonInCharge);
                else if (!string.IsNullOrWhiteSpace(AuthorName))
                    person = AuthorName.Trim();
                else if (!string.IsNullOrWhiteSpace(TfsAuthor))
                    person = TfsAuthor.Trim();
                else
                    person = null;

                string team = string.IsNullOrWhiteSpace(TeamName) ? null : TeamName.Trim();
                if (string.IsNullOrWhiteSpace(person))
                    return team ?? string.Empty;
                if (string.IsNullOrWhiteSpace(team))
                    return person;
                if (person.IndexOf(team, StringComparison.OrdinalIgnoreCase) >= 0)
                    return person;
                return person + OccupancyNameStore.NameTeamSeparator + team;
            }
        }

        public bool HasListWorkDate
        {
            get { return StartDate.HasValue || CheckedInAt.HasValue || TimelineDate.HasValue; }
        }

        public string ListWorkDateText
        {
            get
            {
                DateTime? d = StartDate ?? CheckedInAt ?? TimelineDate;
                return d.HasValue ? d.Value.ToString("yyyy-MM-dd") : string.Empty;
            }
        }

        public bool HasListType
        {
            get { return !string.IsNullOrWhiteSpace(ListTypeText); }
        }

        public string ListTypeText
        {
            get
            {
                if (TypeBadges != null)
                {
                    var badge = TypeBadges.FirstOrDefault(b => b != null && !string.IsNullOrWhiteSpace(b.Type));
                    if (badge != null)
                        return badge.Type.Trim();
                }
                if (Groups != null)
                {
                    var g = Groups.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Type));
                    if (g != null)
                        return g.Type.Trim();
                }
                return string.Empty;
            }
        }

        public bool HasListCategory
        {
            get { return !string.IsNullOrWhiteSpace(ListCategoryText); }
        }

        public string ListCategoryText
        {
            get
            {
                if (Groups == null)
                    return string.Empty;
                var g = Groups.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Category));
                return g != null ? g.Category.Trim() : string.Empty;
            }
        }

        public bool HasListDeploy
        {
            get { return !string.IsNullOrWhiteSpace(ListDeployText); }
        }

        public string ListDeployText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DeploymentStatus))
                    return DeploymentStatus.Trim();
                if (Groups == null)
                    return string.Empty;
                var g = Groups.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.DeploymentStatus));
                return g != null ? g.DeploymentStatus.Trim() : string.Empty;
            }
        }

        public bool HasListRecentHistory
        {
            get { return !string.IsNullOrWhiteSpace(ListRecentHistoryText); }
        }

        /// <summary>목록용 Comment 전체 (말줄임·강제 줄바꿈 없음, TextWrapping에 맡김).</summary>
        public string ListCommentFullText
        {
            get
            {
                string ticket = NormalizeMultiline((TicketContents ?? string.Empty).Trim());
                if (string.IsNullOrWhiteSpace(ticket))
                    ticket = NormalizeMultiline((TfsComment ?? string.Empty).Trim());

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Comment))
                {
                    string c = NormalizeMultiline(Comment.Trim());
                    if (!IsSameText(c, ticket))
                        parts.Add(c);
                }
                if (Groups != null)
                {
                    foreach (var g in Groups)
                    {
                        if (g == null || string.IsNullOrWhiteSpace(g.Comment))
                            continue;
                        string c = NormalizeMultiline(g.Comment.Trim());
                        if (IsSameText(c, ticket))
                            continue;
                        if (!parts.Any(p => IsSameText(p, c)))
                            parts.Add(c);
                    }
                }
                return EnsureNewlineAfterBrackets(string.Join("\n", parts));
            }
        }

        public bool HasListCommentFull
        {
            get { return !string.IsNullOrWhiteSpace(ListCommentFullText); }
        }

        /// <summary>호환용 — ListCommentFullText와 동일.</summary>
        public string ListRecentHistoryText
        {
            get { return ListCommentFullText; }
        }

        /// <summary>목록 메타 (Menu · 담당자). 날짜는 그룹 헤더/상세에만 표시.</summary>
        public string ListMetaCompact
        {
            get
            {
                var parts = new List<string>();
                if (HasListMenu)
                    parts.Add(ListMenuText);
                if (HasListPerson)
                    parts.Add(ListPersonText);
                return string.Join(" · ", parts);
            }
        }

        public bool HasListMetaCompact
        {
            get { return !string.IsNullOrWhiteSpace(ListMetaCompact); }
        }

        public int ProjectCount
        {
            get { return Groups != null ? Groups.Count : 0; }
        }

        public string ListProjectSummary
        {
            get
            {
                int n = ProjectCount;
                int ops = Groups != null
                    ? Groups.Count(g => string.Equals(g.DeploymentStatus, "운영기", StringComparison.Ordinal))
                    : 0;
                if (ops > 0)
                    return "Project " + n + "개 · 운영기 " + ops + "개";
                return "Project " + n + "개";
            }
        }

        public bool HasWorkComment
        {
            get { return !string.IsNullOrWhiteSpace(WorkCommentDisplay); }
        }

        /// <summary>
        /// 목록 우측: 사용자가 따로 남긴 Comment만.
        /// TicketContents(티켓 본문)와 같으면 우측에는 표시하지 않음.
        /// </summary>
        public string WorkCommentDisplay
        {
            get
            {
                string ticket = NormalizeMultiline((TicketContents ?? string.Empty).Trim());
                if (string.IsNullOrWhiteSpace(ticket))
                    ticket = NormalizeMultiline((TfsComment ?? string.Empty).Trim());

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Comment))
                {
                    string c = NormalizeMultiline(Comment.Trim());
                    if (!IsSameText(c, ticket))
                        parts.Add(c);
                }
                if (Groups != null)
                {
                    foreach (var g in Groups)
                    {
                        if (g == null || string.IsNullOrWhiteSpace(g.Comment))
                            continue;
                        string c = NormalizeMultiline(g.Comment.Trim());
                        if (IsSameText(c, ticket))
                            continue;
                        if (!parts.Any(p => IsSameText(p, c)))
                            parts.Add(c);
                    }
                }
                // ListCommentFullText와 동일 파이프라인 (구조 줄바꿈 + MaxWidth 공백 균형 랩)
                return EnsureNewlineAfterBrackets(string.Join("\n", parts));
            }
        }

        private static bool IsSameText(string a, string b)
        {
            return string.Equals(
                NormalizeMultiline(a ?? string.Empty).Trim(),
                NormalizeMultiline(b ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        /// <summary>CSV/엑셀 \r\n·\r 줄바꿈을 UI에서 그대로 보이게 통일.</summary>
        private static string NormalizeMultiline(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>
        /// 목록 Comment MaxWidth(520)에 맞춘 한 줄 글자 수.
        /// WPF가 MaxWidth에서 :, 글자 단위로 임의 줄바꿈하지 않도록 SoftWrap이 먼저 끊는다.
        /// </summary>
        private const int CommentDisplayMaxLineChars = 44;

        /// <summary>() 포함 길이가 이하면 앞·뒤 강제 줄바꿈 없이 한 줄 유지.</summary>
        private const int ShortParenKeepInlineMaxChars = 20;

        /// <summary>
        /// Comment 표시용 줄바꿈 정규화.
        /// - [] 앞·뒤 줄바꿈 / () 는 21자+ 일 때만 앞·뒤 줄바꿈 (≤20자는 한 줄)
        /// - 문장 끝 '.' 뒤 줄바꿈 (파일명·소수점 제외)
        /// - MaxWidth 초과 시에만 줄바꿈. 분할점은 ',' 우선, 없으면 공백(단어). 두 줄 길이 균형.
        /// - ':' 는 줄바꿈 기준이 아님
        /// </summary>
        public static string EnsureNewlineAfterBrackets(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            string text = NormalizeMultiline(value);
            text = InsertStructuralLineBreaks(text);
            text = SoftWrapAtSpaces(text, CommentDisplayMaxLineChars);
            return text;
        }

        private static string InsertStructuralLineBreaks(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length + 16);
            int squareDepth = 0;
            int parenDepth = 0;
            bool lineHasContent = false;
            // 열린 () 가 짧은지(≤20) — 닫을 때 뒤 줄바꿈 스킵용
            var shortParenStack = new Stack<bool>();

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (ch == '\n')
                {
                    sb.Append('\n');
                    lineHasContent = false;
                    continue;
                }

                bool outside = squareDepth == 0 && parenDepth == 0;

                if (outside && ch == '[' && lineHasContent)
                {
                    sb.Append('\n');
                    lineHasContent = false;
                }
                else if (outside && ch == '(' && lineHasContent)
                {
                    int glen = MeasureDelimitedGroup(text, i, '(', ')');
                    bool keepInline = glen > 0 && glen <= ShortParenKeepInlineMaxChars;
                    if (!keepInline)
                    {
                        sb.Append('\n');
                        lineHasContent = false;
                    }
                }

                if (ch == '[')
                    squareDepth++;
                else if (ch == '(')
                {
                    int glen = MeasureDelimitedGroup(text, i, '(', ')');
                    shortParenStack.Push(glen > 0 && glen <= ShortParenKeepInlineMaxChars);
                    parenDepth++;
                }

                // 문장 끝 '.' — 파일명/소수점/경로, 그리고 "1. 2." 번호 목록은 줄바꿈하지 않음
                if (ch == '.' && squareDepth == 0 && parenDepth == 0)
                {
                    sb.Append('.');
                    int j = i + 1;
                    if (j < text.Length && IsFilenameOrDecimalContinue(text[j]))
                    {
                        lineHasContent = true;
                        continue;
                    }

                    // "1. 항목" / "2. 다음" — 번호 매기기 점은 문장 끝이 아님
                    if (IsOrderedListMarkerDot(text, i))
                    {
                        lineHasContent = true;
                        continue;
                    }

                    int k = j;
                    while (k < text.Length && (text[k] == ' ' || text[k] == '\t'))
                        k++;

                    if (k < text.Length && text[k] != '\n')
                    {
                        sb.Append('\n');
                        i = k - 1;
                        lineHasContent = false;
                        continue;
                    }

                    lineHasContent = true;
                    continue;
                }

                sb.Append(ch);
                if (ch != ' ' && ch != '\t')
                    lineHasContent = true;

                if (ch == ']' && squareDepth > 0)
                {
                    squareDepth--;
                    if (squareDepth == 0 && parenDepth == 0
                        && TryBreakAfterGroup(text, ref i, sb, ref lineHasContent))
                        continue;
                }
                else if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                    bool wasShort = shortParenStack.Count > 0 && shortParenStack.Pop();
                    if (squareDepth == 0 && parenDepth == 0 && !wasShort
                        && TryBreakAfterGroup(text, ref i, sb, ref lineHasContent))
                        continue;
                }
            }

            return sb.ToString();
        }

        private static bool TryBreakAfterGroup(
            string text, ref int i, StringBuilder sb, ref bool lineHasContent)
        {
            int k = i + 1;
            while (k < text.Length && (text[k] == ' ' || text[k] == '\t'))
                k++;
            if (k >= text.Length || text[k] == '\n')
                return false;
            sb.Append('\n');
            i = k - 1;
            lineHasContent = false;
            return true;
        }

        /// <summary>openIndex의 괄호 쌍 길이(포함). 못 찾으면 -1.</summary>
        private static int MeasureDelimitedGroup(
            string text, int openIndex, char open, char close)
        {
            if (text == null || openIndex < 0 || openIndex >= text.Length
                || text[openIndex] != open)
                return -1;

            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\n')
                    return -1;
                if (ch == open)
                    depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                        return i - openIndex + 1;
                }
            }
            return -1;
        }

        private static bool IsFilenameOrDecimalContinue(char ch)
        {
            return (ch >= '0' && ch <= '9')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || ch == '_' || ch == '\\' || ch == '/' || ch == '.';
        }

        /// <summary>"1." ~ "99." 형태 번호 목록 마커. (2026. 같은 긴 숫자는 제외)</summary>
        private static bool IsOrderedListMarkerDot(string text, int dotIndex)
        {
            if (text == null || dotIndex <= 0 || dotIndex >= text.Length || text[dotIndex] != '.')
                return false;

            int i = dotIndex - 1;
            if (i < 0 || text[i] < '0' || text[i] > '9')
                return false;

            int digitEnd = i;
            while (i >= 0 && text[i] >= '0' && text[i] <= '9')
                i--;

            int digitLen = digitEnd - i;
            if (digitLen < 1 || digitLen > 2)
                return false;

            if (i < 0)
                return true;

            char prev = text[i];
            return prev == ' ' || prev == '\t' || prev == '\n'
                || prev == '(' || prev == '[' || prev == '（';
        }

        /// <summary>
        /// MaxWidth 초과 시에만 줄바꿈.
        /// 분할점: ','(또는 '，') 뒤 공백 우선 → 없으면 일반 공백(단어).
        /// ':' 는 기준이 아님. 두 줄 길이는 최대한 비슷하게.
        /// </summary>
        private static string SoftWrapAtSpaces(string text, int maxLineChars)
        {
            if (string.IsNullOrEmpty(text) || maxLineChars < 8)
                return text ?? string.Empty;

            var result = new StringBuilder();
            string[] paragraphs = text.Split(new[] { '\n' }, StringSplitOptions.None);
            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0)
                    result.Append('\n');
                AppendSoftWrappedParagraph(result, paragraphs[p], maxLineChars);
            }
            return result.ToString();
        }

        private static void AppendSoftWrappedParagraph(
            StringBuilder result,
            string paragraph,
            int maxLineChars)
        {
            if (string.IsNullOrEmpty(paragraph))
                return;

            var words = SplitWords(paragraph);
            if (words.Count == 0)
                return;

            int total = MeasureWords(words, 0, words.Count - 1);
            // MaxWidth 안이면 절대 SoftWrap 줄바꿈하지 않음 (WPF가 : 로 끊지 않게 XAML도 NoWrap)
            if (total <= maxLineChars || words.Count == 1)
            {
                result.Append(JoinWords(words, 0, words.Count - 1));
                return;
            }

            int lineCount = CountLinesNeeded(words, maxLineChars);
            if (lineCount <= 1)
            {
                result.Append(JoinWords(words, 0, words.Count - 1));
                return;
            }

            PackWordsBalanced(result, words, maxLineChars, lineCount);
        }

        private static int MeasureWords(List<string> words, int from, int to)
        {
            if (words == null || from > to || from < 0 || to >= words.Count)
                return 0;
            int len = 0;
            for (int i = from; i <= to; i++)
                len += words[i].Length + (i > from ? 1 : 0);
            return len;
        }

        private static string JoinWords(List<string> words, int from, int to)
        {
            if (words == null || from > to)
                return string.Empty;
            var sb = new StringBuilder();
            for (int i = from; i <= to; i++)
            {
                if (i > from)
                    sb.Append(' ');
                sb.Append(words[i]);
            }
            return sb.ToString();
        }

        private static int CountLinesNeeded(List<string> words, int maxWidth)
        {
            int lines = 1;
            int len = 0;
            for (int i = 0; i < words.Count; i++)
            {
                int w = words[i].Length;
                int need = len == 0 ? w : len + 1 + w;
                if (need <= maxWidth)
                    len = need;
                else
                {
                    lines++;
                    len = w;
                }
            }
            return lines;
        }

        private static void PackWordsBalanced(
            StringBuilder result, List<string> words, int maxWidth, int lineCount)
        {
            int start = 0;
            int remainingLines = lineCount;
            bool firstOut = true;

            while (start < words.Count)
            {
                int endExclusive;
                if (remainingLines <= 1 || start >= words.Count - 1)
                    endExclusive = words.Count;
                else
                    endExclusive = FindBalancedBreak(words, start, words.Count, maxWidth, remainingLines);

                if (endExclusive <= start)
                    endExclusive = Math.Min(start + 1, words.Count);

                if (!firstOut)
                    result.Append('\n');
                firstOut = false;
                result.Append(JoinWords(words, start, endExclusive - 1));

                start = endExclusive;
                remainingLines--;
            }
        }

        private static bool WordEndsWithComma(string word)
        {
            if (string.IsNullOrEmpty(word))
                return false;
            char c = word[word.Length - 1];
            return c == ',' || c == '，' || c == '、';
        }

        /// <summary>
        /// 첫 줄 끝 단어 다음 index. ',' 경계를 우선하고, 없으면 공백 경계.
        /// ':' 근처는 특별히 선호하지 않음.
        /// </summary>
        private static int FindBalancedBreak(
            List<string> words, int start, int end, int maxWidth, int remainingLines)
        {
            int total = MeasureWords(words, start, end - 1);
            int target = total / remainingLines;
            if (target < 1) target = 1;
            if (target > maxWidth) target = maxWidth;

            int bestComma = -1;
            int bestCommaScore = int.MaxValue;
            int bestSpace = -1;
            int bestSpaceScore = int.MaxValue;

            int lineLen = 0;
            for (int i = start; i < end - 1; i++)
            {
                lineLen = (i == start)
                    ? words[i].Length
                    : lineLen + 1 + words[i].Length;

                if (lineLen > maxWidth)
                    break;

                string next = words[i + 1];
                if (IsStickyWithPrevious(next))
                {
                    int withSticky = lineLen + 1 + next.Length;
                    if (withSticky <= maxWidth)
                        continue;
                }

                int rest = MeasureWords(words, i + 1, end - 1);
                if (rest > (remainingLines - 1) * maxWidth)
                    continue;

                int score = Math.Abs(lineLen - target);
                int breakAt = i + 1;

                if (WordEndsWithComma(words[i]))
                {
                    if (score < bestCommaScore
                        || (score == bestCommaScore && bestComma >= 0
                            && lineLen > MeasureWords(words, start, bestComma - 1)))
                    {
                        bestCommaScore = score;
                        bestComma = breakAt;
                    }
                }
                else
                {
                    if (score < bestSpaceScore
                        || (score == bestSpaceScore && bestSpace >= 0
                            && lineLen > MeasureWords(words, start, bestSpace - 1)))
                    {
                        bestSpaceScore = score;
                        bestSpace = breakAt;
                    }
                }
            }

            if (bestComma > start)
                return bestComma;
            if (bestSpace > start)
                return bestSpace;

            // fallback greedy
            lineLen = 0;
            for (int i = start; i < end; i++)
            {
                int need = (lineLen == 0) ? words[i].Length : lineLen + 1 + words[i].Length;
                if (lineLen > 0 && need > maxWidth)
                    return i;
                lineLen = need;
            }
            return end;
        }

        /// <summary>
        /// 앞뒤 공백으로 떨어진 짧은 토큰 — 줄 앞에 두지 않고 윗줄에 붙임.
        /// 필요하면 이 배열만 수정하면 됨.
        /// </summary>
        private static readonly string[] StickyWithPreviousWords =
        {
            "및", "후", "전", "등", "또", "시", "중", "내", "외", "겸",
            "은", "는", "이", "가", "을", "를", "에", "의", "와", "과", "도",
            "로", "으로", "만", "부터", "까지", "처럼", "보다", "에서", "에게", "한테", "께","나",
            "or", "and", "of", "to"
        };

        private static readonly HashSet<string> StickyWithPreviousSet =
            new HashSet<string>(StickyWithPreviousWords, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 앞뒤 공백으로 떨어진 짧은 조사·의존명사·접속.
        /// 목록: <see cref="StickyWithPreviousWords"/>
        /// </summary>
        private static bool IsStickyWithPrevious(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            string t = word.Trim();
            while (t.Length > 0 && IsTrailPunct(t[t.Length - 1]))
                t = t.Substring(0, t.Length - 1);
            if (t.Length == 0)
                return false;

            return StickyWithPreviousSet.Contains(t);
        }

        private static bool IsTrailPunct(char ch)
        {
            return ch == ',' || ch == '.' || ch == '，' || ch == '。' || ch == '、'
                || ch == ')' || ch == '）' || ch == ']' || ch == '}'
                || ch == '"' || ch == '\'' || ch == ':' || ch == ';';
        }

        private static List<string> SplitWords(string paragraph)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(paragraph))
                return words;

            var current = new StringBuilder();
            for (int i = 0; i < paragraph.Length; i++)
            {
                char ch = paragraph[i];

                // [] / () 는 공백이 있어도 한 단어로 유지 (MaxWidth 랩 시 안쪽 미분리)
                if (ch == '[' || ch == '(')
                {
                    char open = ch;
                    char close = ch == '[' ? ']' : ')';
                    int glen = MeasureDelimitedGroup(paragraph, i, open, close);
                    if (glen > 0)
                    {
                        if (current.Length > 0)
                        {
                            words.Add(current.ToString());
                            current.Clear();
                        }
                        words.Add(paragraph.Substring(i, glen));
                        i += glen - 1;
                        continue;
                    }
                }

                if (IsWrapSpace(ch))
                {
                    if (current.Length > 0)
                    {
                        words.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }
                current.Append(ch);
            }
            if (current.Length > 0)
                words.Add(current.ToString());
            return words;
        }

        private static bool IsWrapSpace(char ch)
        {
            return ch == ' ' || ch == '\t' || ch == '\u3000';
        }

        public void NotifyListPresentation()
        {
            RaisePropertyChanged("HasWorkComment");
            RaisePropertyChanged("WorkCommentDisplay");
            RaisePropertyChanged("ListProjectSummary");
            RaisePropertyChanged("ListRowMetaLine");
            RaisePropertyChanged("ListMenuSummary");
            RaisePropertyChanged("ContentsDisplay");
            RaisePropertyChanged("TicketNumberText");
            RaisePropertyChanged("WriteStatus");
            RaisePropertyChanged("IsCompleted");
            RaisePropertyChanged("IsDraft");
        }

        public bool HasChangeset
        {
            get
            {
                if (SourceChangesetIds != null && SourceChangesetIds.Count > 0)
                    return true;
                return ChangesetId > 0;
            }
        }

        /// <summary>예: [CS 12345] 또는 [CS 12345, 12346]</summary>
        public string ChangesetDisplay
        {
            get
            {
                var ids = GetEffectiveChangesetIds();
                if (ids.Count == 0)
                    return string.Empty;
                if (ids.Count == 1)
                    return "[CS " + ids[0] + "]";
                return "[CS " + string.Join(", ", ids) + "]";
            }
        }

        public List<int> GetEffectiveChangesetIds()
        {
            var ids = new List<int>();
            if (SourceChangesetIds != null)
            {
                foreach (var id in SourceChangesetIds)
                {
                    if (id > 0 && !ids.Contains(id))
                        ids.Add(id);
                }
            }
            if (ids.Count == 0 && ChangesetId > 0)
                ids.Add(ChangesetId);
            return ids;
        }

        /// <summary>Ticket No만. 없으면 빈 값 (Changeset ID로 대체하지 않음).</summary>
        public string TicketDisplay
        {
            get { return TicketNumberText; }
        }

        /// <summary>목록 좌측 본문(업무 제목). 타임라인 배지 줄 이전만.</summary>
        public string ContentsTooltip
        {
            get
            {
                string raw = !string.IsNullOrWhiteSpace(TicketContents)
                    ? TicketContents.Trim()
                    : (TfsComment ?? string.Empty).Trim();
                return NormalizeMultiline(WorkLogDraftMapper.ExtractTitleBeforeTimeline(raw));
            }
        }

        public string ContentsDisplay
        {
            get
            {
                string raw = ContentsTooltip;
                if (string.IsNullOrEmpty(raw))
                    return string.Empty;
                // 너비는 XAML MaxWidth=800 + 컬럼 * 로 반응형. SoftWrap 글자수는 고정폭이라 쓰지 않음.
                return EnsureNewlineAfterBrackets(raw);
            }
        }

        public string AuthorDisplayName
        {
            get
            {
                string name = null;
                if (!string.IsNullOrWhiteSpace(AuthorName))
                    name = AuthorName.Trim();
                else if (!string.IsNullOrWhiteSpace(TfsAuthor))
                    name = TfsAuthor.Trim();
                string display = OccupancyNameStore.FormatDisplayName(name, TeamName);
                return string.IsNullOrWhiteSpace(display) ? "알 수 없음" : display;
            }
        }

        /// <summary>점유명이 AuthorName과 같거나, 이 PC LocalPcIp가 기록된 경우 본인.</summary>
        public bool IsOwnedByCurrentUser
        {
            get { return WorkHubUserProfile.OwnsRecord(AuthorName, LocalPcIp); }
        }

        /// <summary>
        /// 수정 가능: 점유명 또는 이 PC IP.
        /// </summary>
        public bool CanEditByCurrentUser
        {
            get { return IsOwnedByCurrentUser; }
        }

        /// <summary>
        /// 삭제 가능: 이 PC LocalPcIp가 기록된 경우만 (미기록 = 불가).
        /// </summary>
        public bool CanDeleteByCurrentUser
        {
            get { return IsOwnedByCurrentUser; }
        }

        /// <summary>TFS 체크인 작성자 표시(업무 기록 작성자와 별도).</summary>
        public string CheckInAuthorDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(TfsAuthor))
                    return TfsAuthor.Trim();
                if (!string.IsNullOrWhiteSpace(AuthorName))
                    return AuthorName.Trim();
                return "알 수 없음";
            }
        }

        public static bool IsSameUser(string candidate)
        {
            return WorkHubUserProfile.Matches(candidate);
        }

        public string CheckedInTimeDisplay
        {
            get
            {
                return CheckedInAt.HasValue
                    ? CheckedInAt.Value.ToString("HH:mm")
                    : "--:--";
            }
        }

        public string CheckedInFullDisplay
        {
            get
            {
                if (!CheckedInAt.HasValue)
                    return "-";
                return CheckedInAt.Value.ToString("yyyy년 M월 d일 HH:mm");
            }
        }

        /// <summary>이은솔님이 14:31에 체크인 · 파일 4개 변경</summary>
        public string CheckInMetaLine
        {
            get
            {
                return CheckInAuthorDisplay + "님이 " + CheckedInTimeDisplay + "에 체크인 · 파일 "
                    + ChangedFileCount + "개 변경";
            }
        }

        public string DetailCheckInLine
        {
            get
            {
                return CheckInAuthorDisplay + "님이 " + CheckedInFullDisplay + "에 체크인";
            }
        }

        public string ChangesetMetaLine
        {
            get
            {
                string cs = ChangesetDisplay;
                if (string.IsNullOrEmpty(cs))
                    cs = "Changeset";
                return cs + " · 파일 " + ChangedFileCount + "개 변경";
            }
        }

        public string ChangesetShort
        {
            get
            {
                string cs = ChangesetDisplay;
                return string.IsNullOrEmpty(cs) ? "-" : cs;
            }
        }

        /// <summary>
        /// Timeline 그룹/정렬용 일자. 체크인 → Start → End → Deployment 순.
        /// </summary>
        public DateTime? TimelineDate
        {
            get
            {
                return CheckedInAt
                    ?? StartDate
                    ?? EndDate
                    ?? DeploymentDate;
            }
        }

        public DateTime SortCheckedInAt
        {
            get { return TimelineDate ?? LastModifiedAt; }
        }

        public string PeriodText
        {
            get
            {
                string a = StartDate.HasValue ? StartDate.Value.ToString("MM.dd") : "--";
                string b = EndDate.HasValue ? EndDate.Value.ToString("MM.dd") : "--";
                return a + " ~ " + b;
            }
        }

        public string MetaLine
        {
            get
            {
                return "PC " + (Pc ?? "-")
                    + " · " + (string.IsNullOrWhiteSpace(PersonInCharge)
                        ? "-"
                        : WorkLogDraftMapper.FormatPersonDisplay(PersonInCharge))
                    + " · " + PeriodText;
            }
        }

        public string CountsLine
        {
            get { return "작업 " + WorkGroupCount + " · 파일 " + SourceCount; }
        }

        public string LastModifiedText
        {
            get { return LastModifiedAt.ToString("MM.dd HH:mm"); }
        }

        public string MoreInfoTooltip
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("Changeset: " + (string.IsNullOrEmpty(ChangesetDisplay) ? ChangesetId.ToString() : ChangesetDisplay));
                sb.AppendLine("TFS Author: " + (TfsAuthor ?? "-"));
                sb.AppendLine("CheckedIn: " + (CheckedInAt.HasValue ? CheckedInAt.Value.ToString("yyyy-MM-dd HH:mm") : "-"));
                sb.AppendLine("TFS Comment: " + (TfsComment ?? "-"));
                return sb.ToString().TrimEnd();
            }
        }

        public IEnumerable<TypeBadgeItem> ClassificationSummary
        {
            get
            {
                if (Sources == null || Sources.Count == 0)
                    return TypeBadges;
                return Sources
                    .GroupBy(s => s.Type ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new TypeBadgeItem
                    {
                        Type = g.Key,
                        Count = g.Count(),
                        TooltipText = (string.IsNullOrEmpty(g.Key) ? WorkLogFieldMasters.Unselected : g.Key) + " " + g.Count() + "개 파일"
                    })
                    .OrderBy(b => b.Type);
            }
        }

        public static void RebuildTypeBadges(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;
            item.TypeBadges.Clear();
            if (item.Groups == null)
                return;

            var map = new Dictionary<string, List<WorkGroupSummaryItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in item.Groups)
            {
                string key = string.IsNullOrEmpty(g.Type) ? "" : g.Type;
                List<WorkGroupSummaryItem> list;
                if (!map.TryGetValue(key, out list))
                {
                    list = new List<WorkGroupSummaryItem>();
                    map[key] = list;
                }
                list.Add(g);
            }

            foreach (var kv in map.OrderBy(x => x.Key))
            {
                var tip = new StringBuilder();
                tip.AppendLine(string.IsNullOrEmpty(kv.Key) ? WorkLogFieldMasters.Unselected : kv.Key);
                foreach (var cat in kv.Value.GroupBy(x => x.Category ?? "").OrderBy(x => x.Key))
                {
                    string catName = string.IsNullOrEmpty(cat.Key) ? WorkLogFieldMasters.Unselected : cat.Key;
                    tip.AppendLine("- " + catName + " " + cat.Count());
                }
                item.TypeBadges.Add(new TypeBadgeItem
                {
                    Type = kv.Key,
                    Count = kv.Value.Count,
                    TooltipText = tip.ToString().TrimEnd()
                });
            }
        }
    }

    /// <summary>작성 팝업용 Source 행.</summary>
    public sealed class WorkLogSourceEditItem : ViewModelBase
    {
        private bool _isChecked;
        private bool _isActive;
        private string _type;
        private string _category;
        private string _projectName;
        private bool _needsReview;
        private string _reviewReason;

        private string _changeDetailText;

        public string FileName { get; set; }
        public string OriginalPath { get; set; }
        public string ChangeType { get; set; }
        public string SourceOrigin { get; set; }
        public string AppliedRuleCode { get; set; }
        public bool IsAutoClassified { get; set; }

        /// <summary>이 소스가 업무기록에 붙은 시각 (기존 작성일 / 합치기 추가일).</summary>
        public DateTime? RecordedAt { get; set; }

        /// <summary>
        /// 기존 일지에 합치기(APPEND)로 새로 붙은 소스.
        /// UI는 라벨(기존/추가) 없이 연두색으로만 구분.
        /// </summary>
        public bool IsAppended
        {
            get { return IsAppendedOrigin(SourceOrigin); }
        }

        public string TreeBatchLabel
        {
            get { return string.Empty; }
        }

        public string TreeBatchDateText
        {
            get
            {
                if (!RecordedAt.HasValue)
                    return string.Empty;
                return RecordedAt.Value.ToString("MM.dd");
            }
        }

        /// <summary>기존/추가 텍스트 배지는 쓰지 않음.</summary>
        public string TreeBatchBadgeText
        {
            get { return string.Empty; }
        }

        public bool HasTreeBatchBadge
        {
            get { return false; }
        }

        public static bool IsAppendedOrigin(string sourceOrigin)
        {
            if (string.IsNullOrWhiteSpace(sourceOrigin))
                return false;
            return sourceOrigin.IndexOf("|APPEND", StringComparison.OrdinalIgnoreCase) >= 0
                || sourceOrigin.EndsWith("+APPEND", StringComparison.OrdinalIgnoreCase);
        }

        public static string MarkAppendedOrigin(string sourceOrigin)
        {
            string baseOrigin = string.IsNullOrWhiteSpace(sourceOrigin) ? "TFS" : sourceOrigin.Trim();
            if (IsAppendedOrigin(baseOrigin))
                return baseOrigin;
            // SOURCE_ORIGIN VARCHAR2(50)
            string marked = baseOrigin + "|APPEND";
            if (marked.Length > 50)
                marked = "TFS|APPEND";
            return marked;
        }

        /// <summary>Excel/화면용 사용자 편집 텍스트. TFS FileName·OriginalPath와 분리.</summary>
        public string ChangeDetailText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_changeDetailText))
                    return _changeDetailText;
                if (!string.IsNullOrWhiteSpace(FileName))
                    return FileName;
                return OriginalPath ?? string.Empty;
            }
            set
            {
                if (SetProperty(ref _changeDetailText, value))
                {
                    RaisePropertyChanged("TreeTitle");
                    RaisePropertyChanged("IsPlaceholderTitle");
                }
            }
        }

        public string TreeTitle
        {
            get
            {
                return IsPlaceholderTitle
                    ? WorkLogFieldMasters.TreePlaceholderSource
                    : ChangeDetailText;
            }
        }

        public bool IsPlaceholderTitle
        {
            get { return string.IsNullOrWhiteSpace(ChangeDetailText); }
        }

        public bool HasTfsOrigin
        {
            get
            {
                string origin = SourceOrigin ?? string.Empty;
                if (origin.StartsWith("TFS", StringComparison.OrdinalIgnoreCase)
                    && (!string.IsNullOrWhiteSpace(FileName) || !string.IsNullOrWhiteSpace(OriginalPath)))
                    return true;
                return false;
            }
        }

        public bool IsChecked
        {
            get { return _isChecked; }
            set { SetProperty(ref _isChecked, value); }
        }

        public bool IsActive
        {
            get { return _isActive; }
            set { SetProperty(ref _isActive, value); }
        }

        public string Type
        {
            get { return _type; }
            set
            {
                if (SetProperty(ref _type, value))
                {
                    RaisePropertyChanged("TypeDisplay");
                    RaisePropertyChanged("ClassificationLabel");
                    RaisePropertyChanged("IsUnclassified");
                }
            }
        }

        public string Category
        {
            get { return _category; }
            set
            {
                if (SetProperty(ref _category, value))
                {
                    RaisePropertyChanged("CategoryDisplay");
                    RaisePropertyChanged("ClassificationLabel");
                    RaisePropertyChanged("IsUnclassified");
                }
            }
        }

        public string ProjectName
        {
            get { return _projectName; }
            set { SetProperty(ref _projectName, value); }
        }

        public bool NeedsReview
        {
            get { return _needsReview; }
            set
            {
                if (SetProperty(ref _needsReview, value))
                    RaisePropertyChanged("ClassificationLabel");
            }
        }

        public string ReviewReason
        {
            get { return _reviewReason; }
            set { SetProperty(ref _reviewReason, value); }
        }

        public string PathHint
        {
            get
            {
                if (string.IsNullOrEmpty(OriginalPath))
                    return string.Empty;
                string p = OriginalPath.Replace('\\', '/');
                if (p.Length <= 42)
                    return p;
                int idx = p.LastIndexOf('/');
                if (idx > 0)
                {
                    string folder = p.Substring(0, idx + 1);
                    if (folder.Length > 36)
                        return "..." + folder.Substring(folder.Length - 36);
                    return "..." + folder;
                }
                return "..." + p.Substring(p.Length - 40);
            }
        }

        /// <summary>GitHub 스타일 ChangeType 마크 (M/A/D).</summary>
        public string ChangeTypeMark
        {
            get
            {
                string c = (ChangeType ?? string.Empty).Trim().ToLowerInvariant();
                if (c.StartsWith("add") || c == "a")
                    return "A";
                if (c.StartsWith("del") || c == "d" || c.Contains("delete"))
                    return "D";
                if (c.StartsWith("rename") || c.StartsWith("branch"))
                    return "R";
                return "M";
            }
        }

        public bool IsChangeAdd { get { return ChangeTypeMark == "A"; } }
        public bool IsChangeDelete { get { return ChangeTypeMark == "D"; } }
        public bool IsChangeEdit { get { return ChangeTypeMark == "M" || ChangeTypeMark == "R"; } }

        public string TypeDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(Type)
                    || string.Equals(Type, WorkLogFieldMasters.Unselected, System.StringComparison.Ordinal))
                    return WorkLogFieldMasters.Unselected;
                return Type;
            }
        }

        public string CategoryDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(Category)
                    && !string.Equals(Category, WorkLogFieldMasters.Unselected, System.StringComparison.Ordinal))
                    return Category;
                return WorkLogFieldMasters.RequiresCategoryHint(Type, Category)
                    ? WorkLogFieldMasters.Unselected
                    : string.Empty;
            }
        }

        public bool IsUnclassified
        {
            get
            {
                if (string.IsNullOrEmpty(Type))
                    return true;
                // Code/EQS 등 Category 비움은 정상 — Client/Server만 확인 필요로 봄
                return WorkLogFieldMasters.RequiresCategoryHint(Type, Category);
            }
        }

        public string ClassificationLabel
        {
            get
            {
                if (NeedsReview || IsUnclassified)
                    return "확인 필요";
                if (IsAutoClassified)
                    return "자동 분류";
                return "사용자 수정";
            }
        }

    }

    public sealed class WorkLogBreadcrumbPart
    {
        public string Text { get; set; }
        public bool IsCurrent { get; set; }
    }

    public sealed class WorkLogCommentBlock
    {
        public string BadgeText { get; set; }
        public string Body { get; set; }
        public bool HasBadge
        {
            get { return !string.IsNullOrWhiteSpace(BadgeText); }
        }
        public bool HasBody
        {
            get { return !string.IsNullOrWhiteSpace(Body); }
        }
    }

    public sealed class WorkGroupPreviewItem : ViewModelBase
    {
        public string Type { get; set; }
        public string Category { get; set; }
        public int Count { get; set; }
        public string Label
        {
            get
            {
                string t = string.IsNullOrEmpty(Type)
                    || string.Equals(Type, WorkLogFieldMasters.Unselected, System.StringComparison.Ordinal)
                    ? WorkLogFieldMasters.Unselected
                    : Type;
                string c = string.IsNullOrEmpty(Category)
                    || string.Equals(Category, WorkLogFieldMasters.Unselected, System.StringComparison.Ordinal)
                    ? ""
                    : " / " + Category;
                return t + c;
            }
        }
        public string CountText
        {
            get { return Count + "개"; }
        }
    }
}
