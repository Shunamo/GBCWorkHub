using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 단일 업무기록: 조회/편집 모드 + Tree/Inspector 동일 객체 참조.
    /// Mock Data 없음. Wizard 없음.
    /// </summary>
    public sealed class WorkLogEditDialogViewModel : ViewModelBase, IDataErrorInfo
    {
        private string _ticketNumberText = string.Empty;
        private string _ticketComment = string.Empty;
        private bool _isTicketInternal;
        private bool _isTicketUnregistered;
        private int _changesetId;
        private string _changesetDisplay = string.Empty;
        private string _checkedInDisplay = string.Empty;
        private string _writeStatus = WorkLogWriteStatus.Draft;
        private string _validationMessage;
        private string _validationField;
        private string _hintMessage;
        private bool _showTfsOriginal;
        private bool _isEditMode;
        private bool _isNewRecord;
        private bool _canEditByCurrentUser;
        private string _localPcIp = string.Empty;
        private string _siteCode = string.Empty;
        private WorkLogTicketTreeViewModel _tree;
        private ObservableCollection<string> _filteredCategories;
        private WorkLogListItemViewModel _cancelSnapshot;
        private INotifyPropertyChanged _subscribedNode;
        private INotifyPropertyChanged _subscribedProject;
        private INotifyPropertyChanged _subscribedMenu;
        private bool _syncingTree;
        private bool _autoScaffolding;
        private int _editStepIndex;
        private string _treeSearchText = string.Empty;
        private WorkLogProjectNode _workRevealProject;
        private bool _workCategoryUnlocked;
        private bool _showWorkCategorySection;
        private bool _showWorkProjectNameSection;
        private bool _showWorkDeployStatusSection;
        private bool _showWorkDeployDateSection;
        private bool _showWorkCommentSection;
        private readonly ObservableCollection<WorkLogBreadcrumbPart> _breadcrumbParts
            = new ObservableCollection<WorkLogBreadcrumbPart>();
        private readonly ObservableCollection<WorkLogCommentBlock> _commentBlocks
            = new ObservableCollection<WorkLogCommentBlock>();
        private static readonly Regex CommentBadgeLine =
            new Regex(@"^\s*(\d{4}-\d{2}-\d{2})\s*/\s*(.+?)\s*$", RegexOptions.Compiled);

        public WorkLogEditDialogViewModel()
        {
            TypeOptions = WorkLogFieldMasters.CreateTypes();
            DeploymentStatusOptions = WorkLogFieldMasters.CreateDeploymentStatuses();
            SiteOptions = WorkLogFieldMasters.CreateSiteCodes();
            FilteredCategories = new ObservableCollection<string> { WorkLogFieldMasters.EmptyCategory };
            PcOptions = new ObservableCollection<string>();

            CloseCommand = new RelayCommand(() => { if (CloseRequested != null) CloseRequested(); });
            CancelCommand = new RelayCommand(CancelEdit);
            SaveCommand = new RelayCommand(Save, CanSave);
            EnterEditModeCommand = new RelayCommand(EnterEditMode, () => IsReadOnlyMode && CanEditByCurrentUser);
            DeleteWorkLogCommand = new RelayCommand(
                () => { if (DeleteRequested != null) DeleteRequested(); },
                () => CanEditByCurrentUser);
            SelectNodeCommand = new RelayCommand<WorkLogTreeNodeBase>(OnSelectNode);
            SelectSourceCommand = new RelayCommand<WorkLogSourceEditItem>(OnSelectSource);
            ToggleNodeCommand = new RelayCommand<WorkLogTreeNodeBase>(n => { if (n != null) n.IsExpanded = !n.IsExpanded; });
            AddMenuCommand = new RelayCommand(AddMenu, () => IsEditMode);
            // 트리 + 버튼: 해당 노드 아래에 자식 추가 (Menu→Type→Category→Project→Source)
            AddChildCommand = new RelayCommand<WorkLogTreeNodeBase>(AddChild, n => IsEditMode && n != null && !(n is WorkLogProjectNode));
            AddTypeCommand = new RelayCommand(AddType, () => IsEditMode && ActiveMenu != null);
            AddCategoryCommand = new RelayCommand(AddCategory, () => IsEditMode && ResolveActiveType() != null);
            AddProjectCommand = new RelayCommand(AddProject, () => IsEditMode && ResolveActiveCategory() != null);
            AddSourceCommand = new RelayCommand(AddSource, () => IsEditMode && HasActiveProject);
            RemoveSourceCommand = new RelayCommand<WorkLogSourceEditItem>(RemoveSource, s => IsEditMode && s != null);
            RemoveNodeCommand = new RelayCommand<WorkLogTreeNodeBase>(RemoveNode, n => IsEditMode && n != null);
            RemoveSelectedNodeCommand = new RelayCommand(RemoveSelectedNode, () => IsEditMode && Tree != null && Tree.SelectedNode != null);
            ToggleTfsOriginalCommand = new RelayCommand(() => ShowTfsOriginal = !ShowTfsOriginal);
            NextStepCommand = new RelayCommand(GoNextStep, () => CanGoNextStep);
            PrevStepCommand = new RelayCommand(GoPrevStep, () => CanGoPrevStep);
            SelectStepCommand = new RelayCommand<object>(SelectStep);
            UnlockWorkCategoryCommand = new RelayCommand(UnlockWorkCategoryReveal);
            ToggleTicketInternalCommand = new RelayCommand(ToggleTicketInternal);
            ToggleTicketUnregisteredCommand = new RelayCommand(ToggleTicketUnregistered);
        }

        public event Action CloseRequested;
        public event Action ApplyRequested;
        public event Action DeleteRequested;
        /// <summary>Menu 기입 후 Work 스택이 처음 나타날 때 스크롤 요청.</summary>
        public event Action RequestScrollToWorkForm;

        public bool IsEditMode
        {
            get { return _isEditMode; }
            private set
            {
                if (SetProperty(ref _isEditMode, value))
                {
                    RaisePropertyChanged("IsReadOnlyMode");
                    RaisePropertyChanged("ShowEnterEditButton");
                    RaisePropertyChanged("ShowLocalPcIp");
                    RaiseCanExecutes();
                }
            }
        }

        public bool IsReadOnlyMode
        {
            get { return !IsEditMode; }
        }

        public bool IsNewRecord
        {
            get { return _isNewRecord; }
            private set { SetProperty(ref _isNewRecord, value); }
        }

        /// <summary>
        /// 가져오기 직후 미저장 상태 등 — 취소 시 편집 모드 유지가 아니라 닫기(목록 철회).
        /// </summary>
        public bool DiscardOnCancel { get; set; }

        /// <summary>작성자 본인만 수정 진입 가능.</summary>
        public bool CanEditByCurrentUser
        {
            get { return _canEditByCurrentUser; }
            private set
            {
                if (SetProperty(ref _canEditByCurrentUser, value))
                {
                    RaisePropertyChanged("ShowEnterEditButton");
                    RaisePropertyChanged("ShowDeleteButton");
                    var enter = EnterEditModeCommand as RelayCommand;
                    if (enter != null)
                        enter.RaiseCanExecuteChanged();
                    var del = DeleteWorkLogCommand as RelayCommand;
                    if (del != null)
                        del.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ShowEnterEditButton
        {
            get { return IsReadOnlyMode && CanEditByCurrentUser; }
        }

        public bool ShowDeleteButton
        {
            get { return CanEditByCurrentUser; }
        }

        /// <summary>소유자(이 PC)만 편집 화면에서 Local PC IP를 볼 수 있음. 타인·조회 화면에는 미표시.</summary>
        public bool ShowLocalPcIp
        {
            get
            {
                if (!IsEditMode || !WorkHubUserProfile.HasLocalIp)
                    return false;
                if (IsNewRecord || string.IsNullOrWhiteSpace(LocalPcIp))
                    return true;
                return WorkHubUserProfile.Matches(LocalPcIp);
            }
        }

        public string LocalPcIp
        {
            get { return _localPcIp; }
            private set
            {
                if (SetProperty(ref _localPcIp, value ?? string.Empty))
                    RaisePropertyChanged("ShowLocalPcIp");
            }
        }

        public string RecordStatus
        {
            get { return WriteStatus; }
        }

        public bool IsCompleted
        {
            get { return string.Equals(WriteStatus, WorkLogWriteStatus.Completed, StringComparison.Ordinal); }
        }

        public string DialogTitle
        {
            get
            {
                string no = string.IsNullOrWhiteSpace(TicketNumberText) ? "티켓 미확인" : TicketNumberText;
                string title = WorkLogDraftMapper.ExtractTitleBeforeTimeline(TicketComment);
                return string.IsNullOrEmpty(title) ? no : no + "  " + title;
            }
        }

        public string HeaderTicketLine
        {
            get
            {
                return string.IsNullOrWhiteSpace(TicketNumberText) ? "티켓 미확인" : TicketNumberText;
            }
        }

        /// <summary>헤더 업무 제목(Ticket Contents). COMMENT 타임라인은 포함하지 않음.</summary>
        public string HeaderCommentLine
        {
            get
            {
                return WorkLogListItemViewModel.EnsureNewlineAfterBrackets(
                    WorkLogDraftMapper.ExtractTitleBeforeTimeline(TicketComment));
            }
        }

        public bool HasHeaderComment
        {
            get { return !string.IsNullOrWhiteSpace(HeaderCommentLine); }
        }

        private string _importProgressText = string.Empty;

        /// <summary>가져오기 순차 작성 시 "그룹1" 표시.</summary>
        public string ImportProgressText
        {
            get { return _importProgressText; }
            set
            {
                if (SetProperty(ref _importProgressText, value ?? string.Empty))
                    RaisePropertyChanged("HasImportProgress");
            }
        }

        public bool HasImportProgress
        {
            get { return !string.IsNullOrWhiteSpace(ImportProgressText); }
        }

        /// <summary>좌측 트리 검색 (메뉴/프로젝트/파일).</summary>
        public string TreeSearchText
        {
            get { return _treeSearchText; }
            set
            {
                if (SetProperty(ref _treeSearchText, value ?? string.Empty))
                    ApplyTreeSearchFilter();
            }
        }

        public ObservableCollection<WorkLogBreadcrumbPart> BreadcrumbParts
        {
            get { return _breadcrumbParts; }
        }

        public bool HasBreadcrumb
        {
            get { return _breadcrumbParts.Count > 0; }
        }

        public ObservableCollection<WorkLogCommentBlock> CommentBlocks
        {
            get { return _commentBlocks; }
        }

        public bool HasCommentBlocks
        {
            get { return _commentBlocks.Count > 0; }
        }

        public string DetailPerson
        {
            get
            {
                var menu = ResolveDetailMenu();
                return menu == null || string.IsNullOrWhiteSpace(menu.PersonInCharge)
                    ? "-"
                    : menu.PersonInCharge;
            }
        }

        public string DetailPersonMeta
        {
            get { return "담당자  " + DetailPerson; }
        }

        public string DetailPeriod
        {
            get
            {
                var menu = ResolveDetailMenu();
                return menu == null ? "-" : menu.PeriodDisplay;
            }
        }

        public string DetailPeriodMeta
        {
            get { return "작업 기간  " + DetailPeriod; }
        }

        public string DetailMenu
        {
            get
            {
                var menu = ResolveDetailMenu();
                return menu == null ? "-" : menu.Title;
            }
        }

        public string DetailSite
        {
            get
            {
                return string.IsNullOrWhiteSpace(SiteCode) ? "-" : SiteCode.Trim().ToUpperInvariant();
            }
        }

        public string DetailIp
        {
            get
            {
                // 상세(읽기 전용): DB에서 온 원본 값 그대로. PC 옵션/편집 상태와 무관.
                string pc = Tree != null && Tree.SourceItem != null ? Tree.SourceItem.Pc : null;
                return string.IsNullOrWhiteSpace(pc) ? "-" : pc.Trim();
            }
        }

        public string SiteCode
        {
            get { return _siteCode; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
                if (string.Equals(next, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                    next = string.Empty;
                if (SetProperty(ref _siteCode, next))
                {
                    RaisePropertyChanged("DetailSite");
                    RaisePropertyChanged("HasSiteCode");
                    RaisePropertyChanged("ShowEmptyPcHint");
                    RaisePropertyChanged("CanGoNextStep");
                    RaiseStepCommands();
                    if (string.Equals(_validationField, "Site", StringComparison.Ordinal))
                        ClearFieldValidation();
                    TryAutoRevealWorkFromMenu();
                }
            }
        }

        public bool HasSiteCode
        {
            get
            {
                return !string.IsNullOrWhiteSpace(SiteCode)
                    && !string.Equals(SiteCode, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool ShowEmptyPcHint
        {
            get { return HasSiteCode && (PcOptions == null || PcOptions.Count == 0); }
        }

        /// <summary>작성 화면용 사이트별 PC 목록. 목록 필터 컬렉션과 공유하지 않음.</summary>
        /// <param name="clearMissingSelection">true면 사이트 변경 시 마스터에 없는 PC 선택을 비움.</param>
        public void ReplacePcOptions(IEnumerable<string> ips, bool clearMissingSelection = false)
        {
            if (PcOptions == null)
                PcOptions = new ObservableCollection<string>();

            PcOptions.Clear();
            if (ips != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string ip in ips)
                {
                    if (string.IsNullOrWhiteSpace(ip) || !seen.Add(ip.Trim()))
                        continue;
                    PcOptions.Add(ip.Trim());
                }
            }

            if (clearMissingSelection
                && ActiveMenu != null
                && !string.IsNullOrWhiteSpace(ActiveMenu.Pc))
            {
                string current = ActiveMenu.Pc.Trim();
                bool stillValid = false;
                foreach (string option in PcOptions)
                {
                    if (string.Equals(option, current, StringComparison.OrdinalIgnoreCase))
                    {
                        stillValid = true;
                        break;
                    }
                }
                if (!stillValid)
                    ActiveMenu.Pc = string.Empty;
            }

            RaisePropertyChanged("ShowEmptyPcHint");
        }

        public string DetailTypeChip
        {
            get
            {
                var p = ResolveDetailProject();
                return p == null || string.IsNullOrWhiteSpace(p.Type) ? string.Empty : p.Type;
            }
        }

        public bool HasDetailTypeChip
        {
            get { return !string.IsNullOrWhiteSpace(DetailTypeChip); }
        }

        public string DetailCategoryChip
        {
            get
            {
                var p = ResolveDetailProject();
                if (p == null || string.IsNullOrWhiteSpace(p.Category))
                    return string.Empty;
                if (string.Equals(p.Category, WorkLogFieldMasters.EmptyCategory, StringComparison.Ordinal))
                    return string.Empty;
                return p.Category;
            }
        }

        public bool HasDetailCategoryChip
        {
            get { return !string.IsNullOrWhiteSpace(DetailCategoryChip); }
        }

        public string DetailDeployChip
        {
            get
            {
                var p = ResolveDetailProject();
                if (p == null || string.IsNullOrWhiteSpace(p.DeploymentStatus))
                    return string.Empty;
                if (string.Equals(p.DeploymentStatus, WorkLogFieldMasters.Unselected, StringComparison.Ordinal))
                    return string.Empty;
                string s = p.DeploymentStatus.Trim();
                if (string.Equals(s, "운영기", StringComparison.Ordinal)
                    || string.Equals(s, "스테이징", StringComparison.Ordinal))
                    return s + " 반영";
                return s;
            }
        }

        public bool HasDetailDeployChip
        {
            get { return !string.IsNullOrWhiteSpace(DetailDeployChip); }
        }

        public string DetailDeployDate
        {
            get
            {
                var p = ResolveDetailProject();
                if (p == null || !p.DeploymentDate.HasValue)
                    return "-";
                return p.DeploymentDate.Value.ToString("yyyy-MM-dd");
            }
        }

        public string DetailProject
        {
            get
            {
                var p = ResolveDetailProject();
                if (p == null)
                    return "-";
                return string.IsNullOrWhiteSpace(p.ProjectName) ? "-" : p.ProjectName;
            }
        }

        public string DetailSourceHeader
        {
            get
            {
                var p = ResolveDetailProject();
                int n = p != null && p.Sources != null ? p.Sources.Count : 0;
                return "SOURCE (" + n + ")";
            }
        }

        public ObservableCollection<WorkLogSourceEditItem> DetailSources
        {
            get
            {
                var p = ResolveDetailProject();
                return p != null && p.Sources != null
                    ? p.Sources
                    : new ObservableCollection<WorkLogSourceEditItem>();
            }
        }

        public bool HasDetailSources
        {
            get
            {
                var p = ResolveDetailProject();
                return p != null && p.Sources != null && p.Sources.Count > 0;
            }
        }

        private WorkLogSourceEditItem _focusedSource;

        public string BreadcrumbPath
        {
            get
            {
                if (_breadcrumbParts.Count == 0)
                    return string.Empty;
                return string.Join(" > ", _breadcrumbParts.Select(p => p.Text).ToArray());
            }
        }

        public string DeleteNodeLabel
        {
            get
            {
                if (Tree == null || Tree.SelectedNode == null)
                    return "삭제";
                if (Tree.IsTypeSelected) return "Type 삭제";
                if (Tree.IsCategorySelected) return "Category 삭제";
                if (Tree.IsProjectSelected) return "Project 삭제";
                return "삭제";
            }
        }

        public bool ShowProjectWorkspace
        {
            get
            {
                return IsEditMode && HasWorkNodeSelected;
            }
        }

        public bool HasWorkNodeSelected
        {
            get
            {
                return Tree != null
                    && (Tree.IsTypeSelected || Tree.IsCategorySelected || Tree.IsProjectSelected);
            }
        }

        public bool HasActiveProject
        {
            get { return ActiveProject != null; }
        }

        public bool ShowSourcePanel
        {
            get { return HasActiveProject; }
        }

        /// <summary>Work 단계 점진 공개: Type 선택 후 Category.</summary>
        public bool ShowWorkCategorySection
        {
            get { return _showWorkCategorySection; }
        }

        public bool ShowWorkProjectNameSection
        {
            get { return _showWorkProjectNameSection; }
        }

        public bool ShowWorkDeployStatusSection
        {
            get { return _showWorkDeployStatusSection; }
        }

        public bool ShowWorkDeployDateSection
        {
            get { return _showWorkDeployDateSection; }
        }

        public bool ShowWorkCommentSection
        {
            get { return _showWorkCommentSection; }
        }

        /// <summary>Menu 아래에 Work 필드 스택 표시 (페이지 전환 없음).</summary>
        public bool ShowWorkFormStack
        {
            get { return HasActiveProject || HasWorkNodeSelected; }
        }

        /// <summary>Category 이후(또는 Source 있을 때) Work 페이지 하단에 Source 표시.</summary>
        public bool ShowSourceFormStack
        {
            get
            {
                if (!HasActiveProject)
                    return false;
                if (ShowWorkProjectNameSection)
                    return true;
                var p = ActiveProject;
                return p != null && p.Sources != null && p.Sources.Count > 0;
            }
        }

        public bool HasEditFooterMessage
        {
            // 필드 검증은 해당 입력 아래 FieldError. 푸터는 필드 없는 시스템 메시지만.
            get
            {
                return HasValidationMessage && string.IsNullOrEmpty(_validationField);
            }
        }

        public string SiteFieldError
        {
            get { return FieldError("Site"); }
        }

        public bool HasSiteFieldError
        {
            get { return !string.IsNullOrEmpty(SiteFieldError); }
        }

        public string PersonInChargeFieldError
        {
            get { return FieldError("PersonInCharge"); }
        }

        public bool HasPersonInChargeFieldError
        {
            get { return !string.IsNullOrEmpty(PersonInChargeFieldError); }
        }

        public string PeriodFieldError
        {
            get { return FieldError("Period"); }
        }

        public bool HasPeriodFieldError
        {
            get { return !string.IsNullOrEmpty(PeriodFieldError); }
        }

        public string TypeFieldError
        {
            get { return FieldError("Type"); }
        }

        public bool HasTypeFieldError
        {
            get { return !string.IsNullOrEmpty(TypeFieldError); }
        }

        public string SourceFieldError
        {
            get { return FieldError("Source"); }
        }

        public bool HasSourceFieldError
        {
            get { return !string.IsNullOrEmpty(SourceFieldError); }
        }

        private string FieldError(string field)
        {
            if (string.IsNullOrWhiteSpace(_validationMessage)
                || !string.Equals(_validationField, field, StringComparison.Ordinal))
                return null;
            return _validationMessage;
        }

        private void ClearFieldValidation()
        {
            if (string.IsNullOrEmpty(_validationField) && string.IsNullOrEmpty(_validationMessage))
                return;
            string prev = _validationField;
            _validationField = null;
            ValidationMessage = null;
            RaiseFieldErrorProps(prev);
        }

        private void SetFieldValidation(string field, string message)
        {
            string prev = _validationField;
            _validationField = field;
            ValidationMessage = message;
            if (!string.Equals(prev, field, StringComparison.Ordinal))
                RaiseFieldErrorProps(prev);
            RaiseFieldErrorProps(field);
        }

        private void RaiseFieldErrorProps(string field)
        {
            if (string.IsNullOrEmpty(field))
                return;
            if (string.Equals(field, "Site", StringComparison.Ordinal))
            {
                RaisePropertyChanged("SiteFieldError");
                RaisePropertyChanged("HasSiteFieldError");
            }
            else if (string.Equals(field, "PersonInCharge", StringComparison.Ordinal))
            {
                RaisePropertyChanged("PersonInChargeFieldError");
                RaisePropertyChanged("HasPersonInChargeFieldError");
            }
            else if (string.Equals(field, "Period", StringComparison.Ordinal))
            {
                RaisePropertyChanged("PeriodFieldError");
                RaisePropertyChanged("HasPeriodFieldError");
            }
            else if (string.Equals(field, "Type", StringComparison.Ordinal))
            {
                RaisePropertyChanged("TypeFieldError");
                RaisePropertyChanged("HasTypeFieldError");
            }
            else if (string.Equals(field, "Source", StringComparison.Ordinal))
            {
                RaisePropertyChanged("SourceFieldError");
                RaisePropertyChanged("HasSourceFieldError");
            }
        }

        /// <summary>0=Menu, 1=Work(+Source). Source는 Work 아래 동일 페이지.</summary>
        public int EditStepIndex
        {
            get { return _editStepIndex; }
            set
            {
                if (value < 0) value = 0;
                if (value > 1) value = 1;
                if (!SetProperty(ref _editStepIndex, value))
                    return;
                RaisePropertyChanged("IsStepMenu");
                RaisePropertyChanged("IsStepWork");
                RaisePropertyChanged("IsStepSource");
                RaisePropertyChanged("ShowPrevStepButton");
                RaisePropertyChanged("ShowNextStepButton");
                RaisePropertyChanged("CanGoNextStep");
                RaisePropertyChanged("CanGoPrevStep");
                RaisePropertyChanged("NextStepLabel");
                RaiseStepCommands();
                if (value == 1)
                {
                    EnsureTypeSelectedForWorkStep();
                    EnsureScratchProjectUnderMenu();
                    RefreshWorkFormReveal(resetUnlock: false);
                }
                RaiseStackVisibility();
            }
        }

        public bool IsStepMenu { get { return EditStepIndex == 0; } }
        public bool IsStepWork { get { return EditStepIndex == 1; } }
        /// <summary>호환용. Source는 Work 페이지에 포함.</summary>
        public bool IsStepSource { get { return false; } }
        public bool ShowPrevStepButton { get { return EditStepIndex > 0; } }
        public bool ShowNextStepButton { get { return EditStepIndex < 1; } }
        public bool CanGoPrevStep { get { return EditStepIndex > 0; } }
        public bool CanGoNextStep
        {
            get
            {
                if (!IsEditMode || EditStepIndex >= 1)
                    return false;
                return HasSiteCode
                    && ActiveMenu != null
                    && !string.IsNullOrWhiteSpace(ActiveMenu.MenuName);
            }
        }

        public string NextStepLabel
        {
            get { return "다음"; }
        }

        /// <summary>현재 편집 컨텍스트의 Menu (트리 선택 기준).</summary>
        public WorkLogMenuSectionNode ActiveMenu
        {
            get
            {
                if (Tree == null)
                    return null;
                if (Tree.SelectedMenu != null)
                    return Tree.SelectedMenu;
                if (Tree.SelectedTypeNode != null)
                    return FindMenuForType(Tree.SelectedTypeNode);
                if (Tree.SelectedCategoryNode != null)
                    return FindMenuForCategory(Tree.SelectedCategoryNode);
                if (Tree.SelectedProject != null)
                    return FindMenuForProject(Tree.SelectedProject);
                return Tree.MenuSections.FirstOrDefault();
            }
        }

        public bool HasActiveMenu
        {
            get { return ActiveMenu != null; }
        }

        /// <summary>
        /// Type/Category/Project 선택 시 편집 대상 Project (동일 객체 참조).
        /// </summary>
        public WorkLogProjectNode ActiveProject
        {
            get
            {
                if (Tree == null)
                    return null;
                if (Tree.SelectedProject != null)
                    return Tree.SelectedProject;
                if (Tree.SelectedCategoryNode != null)
                    return Tree.SelectedCategoryNode.Projects.FirstOrDefault();
                if (Tree.SelectedTypeNode != null)
                {
                    foreach (var c in Tree.SelectedTypeNode.Categories)
                    {
                        if (c.Projects.Count > 0)
                            return c.Projects[0];
                    }
                }
                return null;
            }
        }

        public string TicketNumberText
        {
            get { return WorkLogDraftMapper.FormatTicketDisplay(_ticketNumberText); }
            set
            {
                ApplyTicketStorage(value);
            }
        }

        /// <summary>TN- 옆 숫자만. 내부/미등록 모드에서는 비활성.</summary>
        public string TicketNumberDigits
        {
            get
            {
                if (_isTicketInternal || _isTicketUnregistered)
                    return string.Empty;
                if (!WorkLogDraftMapper.IsNumericTicket(_ticketNumberText))
                    return string.Empty;
                return WorkLogDraftMapper.NormalizeTicketStorage(_ticketNumberText);
            }
            set
            {
                if (_isTicketInternal || _isTicketUnregistered)
                    return;
                string digits = WorkLogDraftMapper.NormalizeTicketStorage(value ?? string.Empty);
                if (!string.IsNullOrEmpty(digits) && !WorkLogDraftMapper.IsNumericTicket(digits))
                {
                    // 숫자만 허용 — 비숫자는 무시
                    var onlyDigits = new System.Text.StringBuilder();
                    foreach (char c in digits)
                    {
                        if (char.IsDigit(c) || c == ',' || c == ' ')
                            onlyDigits.Append(c);
                    }
                    digits = WorkLogDraftMapper.NormalizeTicketStorage(onlyDigits.ToString());
                }
                if (SetProperty(ref _ticketNumberText, digits ?? string.Empty))
                    RaiseTicketPresentation();
            }
        }

        public bool IsTicketInternal
        {
            get { return _isTicketInternal; }
        }

        public bool IsTicketUnregistered
        {
            get { return _isTicketUnregistered; }
        }

        public bool IsTicketNumberMode
        {
            get { return !_isTicketInternal && !_isTicketUnregistered; }
        }

        public bool HasTicketComment
        {
            get { return !string.IsNullOrWhiteSpace(TicketComment); }
        }

        public string TicketComment
        {
            get { return _ticketComment; }
            set
            {
                if (SetProperty(ref _ticketComment, value ?? string.Empty))
                {
                    RaisePropertyChanged("DialogTitle");
                    RaisePropertyChanged("HeaderCommentLine");
                    RaisePropertyChanged("HasHeaderComment");
                    RaisePropertyChanged("HasTicketComment");
                }
            }
        }

        public void ApplyTicketStorage(string raw)
        {
            string normalized = WorkLogDraftMapper.NormalizeTicketStorage(raw ?? string.Empty);
            bool internalTicket = string.Equals(normalized, WorkLogFieldMasters.TicketInternal, StringComparison.OrdinalIgnoreCase);
            bool unregistered = string.Equals(normalized, WorkLogFieldMasters.TicketUnregistered, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "티켓파악불가", StringComparison.OrdinalIgnoreCase);

            _isTicketInternal = internalTicket;
            _isTicketUnregistered = unregistered && !internalTicket;

            if (_isTicketInternal)
                _ticketNumberText = WorkLogFieldMasters.TicketInternal;
            else if (_isTicketUnregistered)
                _ticketNumberText = WorkLogFieldMasters.TicketUnregistered;
            else
                _ticketNumberText = normalized ?? string.Empty;

            RaiseTicketPresentation();
        }

        private string ResolveTicketStorageForSave()
        {
            if (_isTicketInternal)
                return WorkLogFieldMasters.TicketInternal;
            if (_isTicketUnregistered)
                return WorkLogFieldMasters.TicketUnregistered;
            return WorkLogDraftMapper.NormalizeTicketStorage(_ticketNumberText);
        }

        private void ToggleTicketInternal()
        {
            if (_isTicketInternal)
            {
                _isTicketInternal = false;
                _ticketNumberText = string.Empty;
            }
            else
            {
                _isTicketInternal = true;
                _isTicketUnregistered = false;
                _ticketNumberText = WorkLogFieldMasters.TicketInternal;
            }
            RaiseTicketPresentation();
        }

        private void ToggleTicketUnregistered()
        {
            if (_isTicketUnregistered)
            {
                _isTicketUnregistered = false;
                _ticketNumberText = string.Empty;
            }
            else
            {
                _isTicketUnregistered = true;
                _isTicketInternal = false;
                _ticketNumberText = WorkLogFieldMasters.TicketUnregistered;
            }
            RaiseTicketPresentation();
        }

        private void RaiseTicketPresentation()
        {
            RaisePropertyChanged("TicketNumberText");
            RaisePropertyChanged("TicketNumberDigits");
            RaisePropertyChanged("IsTicketInternal");
            RaisePropertyChanged("IsTicketUnregistered");
            RaisePropertyChanged("IsTicketNumberMode");
            RaisePropertyChanged("DialogTitle");
            RaisePropertyChanged("HeaderTicketLine");
        }

        public int ChangesetId
        {
            get { return _changesetId; }
            set
            {
                if (SetProperty(ref _changesetId, value))
                    RaisePropertyChanged("HasChangeset");
            }
        }

        public string ChangesetDisplay
        {
            get { return _changesetDisplay; }
            set
            {
                if (SetProperty(ref _changesetDisplay, value ?? string.Empty))
                    RaisePropertyChanged("HasChangeset");
            }
        }

        public bool HasChangeset
        {
            get { return ChangesetId > 0 || !string.IsNullOrWhiteSpace(ChangesetDisplay); }
        }

        public string CheckedInDisplay
        {
            get { return _checkedInDisplay; }
            set { SetProperty(ref _checkedInDisplay, value ?? string.Empty); }
        }

        public string WriteStatus
        {
            get { return _writeStatus; }
            set
            {
                if (SetProperty(ref _writeStatus, value))
                {
                    RaisePropertyChanged("RecordStatus");
                    RaisePropertyChanged("IsCompleted");
                }
            }
        }

        public WorkLogTicketTreeViewModel Tree
        {
            get { return _tree; }
            set { SetProperty(ref _tree, value); }
        }

        public ObservableCollection<string> TypeOptions { get; private set; }
        public ObservableCollection<string> DeploymentStatusOptions { get; private set; }
        public ObservableCollection<string> SiteOptions { get; private set; }
        public ObservableCollection<string> PcOptions { get; private set; }

        public ObservableCollection<string> FilteredCategories
        {
            get { return _filteredCategories; }
            private set { SetProperty(ref _filteredCategories, value); }
        }

        public string ValidationMessage
        {
            get { return _validationMessage; }
            private set
            {
                if (SetProperty(ref _validationMessage, value))
                {
                    RaisePropertyChanged("HasValidationMessage");
                    RaisePropertyChanged("HasEditFooterMessage");
                    RaisePropertyChanged("SiteFieldError");
                    RaisePropertyChanged("HasSiteFieldError");
                    RaisePropertyChanged("PersonInChargeFieldError");
                    RaisePropertyChanged("HasPersonInChargeFieldError");
                    RaisePropertyChanged("PeriodFieldError");
                    RaisePropertyChanged("HasPeriodFieldError");
                    RaisePropertyChanged("TypeFieldError");
                    RaisePropertyChanged("HasTypeFieldError");
                    RaisePropertyChanged("SourceFieldError");
                    RaisePropertyChanged("HasSourceFieldError");
                }
            }
        }

        public bool HasValidationMessage
        {
            get { return !string.IsNullOrWhiteSpace(ValidationMessage); }
        }

        public string HintMessage
        {
            get { return _hintMessage; }
            private set
            {
                if (SetProperty(ref _hintMessage, value))
                {
                    RaisePropertyChanged("HasHintMessage");
                    RaisePropertyChanged("HasEditFooterMessage");
                }
            }
        }

        public bool HasHintMessage
        {
            get { return !string.IsNullOrWhiteSpace(HintMessage); }
        }

        public bool ShowTfsOriginal
        {
            get { return _showTfsOriginal; }
            set { SetProperty(ref _showTfsOriginal, value); }
        }

        public int MenuSectionCount
        {
            get { return Tree != null && Tree.MenuSections != null ? Tree.MenuSections.Count : 0; }
        }

        public ICommand CloseCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand EnterEditModeCommand { get; private set; }
        public ICommand DeleteWorkLogCommand { get; private set; }
        public ICommand SelectNodeCommand { get; private set; }
        public ICommand SelectSourceCommand { get; private set; }
        public ICommand ToggleNodeCommand { get; private set; }
        public ICommand AddMenuCommand { get; private set; }
        public ICommand AddChildCommand { get; private set; }
        public ICommand AddTypeCommand { get; private set; }
        public ICommand AddCategoryCommand { get; private set; }
        public ICommand AddProjectCommand { get; private set; }
        public ICommand AddSourceCommand { get; private set; }
        public ICommand RemoveSourceCommand { get; private set; }
        public ICommand RemoveNodeCommand { get; private set; }
        public ICommand RemoveSelectedNodeCommand { get; private set; }
        public ICommand ToggleTfsOriginalCommand { get; private set; }
        public ICommand NextStepCommand { get; private set; }
        public ICommand PrevStepCommand { get; private set; }
        public ICommand SelectStepCommand { get; private set; }
        /// <summary>Category 칩 선택 시 Project Name 이하 공개.</summary>
        public ICommand UnlockWorkCategoryCommand { get; private set; }
        public ICommand ToggleTicketInternalCommand { get; private set; }
        public ICommand ToggleTicketUnregisteredCommand { get; private set; }

        public string this[string columnName]
        {
            get
            {
                if (Tree == null || Tree.SelectedMenu == null)
                    return null;
                if (columnName == "StartDateText" || columnName == "PersonInCharge")
                    return WorkLogEditValidator.ValidateMenuRequired(Tree.SelectedMenu);
                return null;
            }
        }

        public string Error { get { return null; } }

        public static WorkLogEditDialogViewModel FromListItem(
            WorkLogListItemViewModel item,
            bool isNew,
            bool startInEditMode,
            ObservableCollection<string> pcOptions)
        {
            var vm = new WorkLogEditDialogViewModel();
            // PcOptions는 OpenDialog 이후 사이트 기준으로 채움 (목록 필터 컬렉션과 공유하지 않음).
            _ = pcOptions;

            vm.IsNewRecord = isNew;
            // 신규 작성만 예외. 기존 건은 LocalPcIp가 이 PC와 일치할 때만 수정.
            // (미기록 IP는 차단 — 체크인 합치기로 IP를 채운 뒤 가능)
            vm.CanEditByCurrentUser = isNew
                || (item != null && item.CanEditByCurrentUser);

            if (item == null)
            {
                vm.Tree = new WorkLogTicketTreeViewModel();
                var newMenu = new WorkLogMenuSectionNode { IsExpanded = true };
                vm.Tree.MenuSections.Add(newMenu);
                // Menu PropertyChanged 구독·선택 필수. 미선택이면 Site→Menu 순으로 써도 Work 스택이 안 열림.
                vm.OnSelectNode(newMenu);
                vm.LocalPcIp = WorkHubUserProfile.LocalIp;
                vm.SiteCode = string.Empty;
                vm.IsEditMode = true;
                vm.CanEditByCurrentUser = true;
                return vm;
            }

            vm.ApplyTicketStorage(item.TicketNo ?? string.Empty);
            // 헤더 제목 = Ticket Contents만 (Comment/TfsComment를 섞지 않음)
            vm.TicketComment = item.TicketContents ?? string.Empty;
            vm.ChangesetId = item.ChangesetId;
            vm.ChangesetDisplay = item.ChangesetDisplay ?? string.Empty;
            vm.CheckedInDisplay = item.CheckedInAt.HasValue
                ? item.CheckedInAt.Value.ToString("yyyy-MM-dd HH:mm")
                : string.Empty;
            vm.WriteStatus = item.WriteStatus ?? WorkLogWriteStatus.Draft;
            vm.SiteCode = item.SiteCode ?? string.Empty;
            vm.LocalPcIp = !string.IsNullOrWhiteSpace(item.LocalPcIp)
                ? item.LocalPcIp
                : (isNew ? WorkHubUserProfile.LocalIp : string.Empty);
            vm.Tree = WorkLogTicketTreeViewModel.FromListItem(item, expandTypesOnly: false);

            var menu = vm.Tree.MenuSections.FirstOrDefault();
            if (menu != null)
                vm.OnSelectNode(menu);

            vm.IsEditMode = startInEditMode && vm.CanEditByCurrentUser;
            if (vm.IsEditMode)
                vm._cancelSnapshot = vm.CaptureSnapshot();

            vm.RefreshDetailPresentation();
            return vm;
        }

        public void ApplyUiStateTo(WorkLogListItemViewModel target)
        {
            if (target == null || Tree == null)
                return;

            target.TicketNo = ResolveTicketStorageForSave();
            target.TicketContents = TicketComment ?? string.Empty;
            target.WriteStatus = WriteStatus ?? WorkLogWriteStatus.Draft;
            target.SiteCode = string.IsNullOrWhiteSpace(SiteCode)
                ? null
                : SiteCode.Trim().ToUpperInvariant();
            target.LastModifiedAt = DateTime.Now;
            Tree.ApplyTo(target);
            if (!string.IsNullOrWhiteSpace(target.TicketNo))
                target.NeedsTicketReview = false;

            // Local PC IP: 비어 있을 때만 이 PC IP로 채움 (타인 초안 저장 시 덮어쓰지 않음)
            if (string.IsNullOrWhiteSpace(target.LocalPcIp))
            {
                if (!string.IsNullOrWhiteSpace(LocalPcIp))
                    target.LocalPcIp = LocalPcIp;
                else if (WorkHubUserProfile.HasLocalIp)
                    target.LocalPcIp = WorkHubUserProfile.LocalIp;
            }

            target.NotifyListPresentation();
        }

        private void EnterEditMode()
        {
            if (!CanEditByCurrentUser)
                return;
            _cancelSnapshot = CaptureSnapshot();
            IsEditMode = true;
            ClearFieldValidation();
            RefreshWorkFormReveal(resetUnlock: true);
        }

        private void CancelEdit()
        {
            ClearFieldValidation();
            if (IsNewRecord || DiscardOnCancel)
            {
                if (CloseRequested != null)
                    CloseRequested();
                return;
            }

            if (_cancelSnapshot != null)
                RestoreSnapshot(_cancelSnapshot);
            IsEditMode = false;
            RaisePropertyChanged("BreadcrumbPath");
            RaisePropertyChanged("DialogTitle");
            RaisePropertyChanged("MenuSectionCount");
        }

        private WorkLogListItemViewModel CaptureSnapshot()
        {
            // 현재 다이얼로그 상태를 list-item staging으로 모은 뒤 DraftMapper baseline과 동일 복제
            var staging = new WorkLogListItemViewModel
            {
                TicketNo = ResolveTicketStorageForSave(),
                TicketContents = TicketComment,
                WriteStatus = WriteStatus,
                SiteCode = SiteCode,
                ChangesetId = ChangesetId,
                CheckedInAt = null,
                TfsComment = TicketComment,
                LocalPcIp = LocalPcIp
            };
            if (Tree != null)
                Tree.ApplyTo(staging);

            return WorkLogDraftMapper.CloneListItemBaseline(staging);
        }

        private void RestoreSnapshot(WorkLogListItemViewModel snap)
        {
            if (snap == null)
                return;

            UnsubscribeNode();
            // list-item baseline → 다이얼로그 UI (선택/Step 등 UI-only는 여기서 재구성)
            ApplyTicketStorage(snap.TicketNo ?? string.Empty);
            TicketComment = snap.TicketContents ?? string.Empty;
            WriteStatus = snap.WriteStatus ?? WorkLogWriteStatus.Draft;
            SiteCode = snap.SiteCode ?? string.Empty;
            LocalPcIp = snap.LocalPcIp ?? string.Empty;
            ChangesetId = snap.ChangesetId;
            Tree = WorkLogTicketTreeViewModel.FromListItem(snap, expandTypesOnly: false);
            var menu = Tree.MenuSections.FirstOrDefault();
            if (menu != null)
                OnSelectNode(menu);
            RefreshCategoryOptions();
            RefreshHints();
        }

        private void OnSelectNode(WorkLogTreeNodeBase node)
        {
            if (Tree == null || node == null)
                return;
            UnsubscribeNode();
            Tree.Select(node);
            if (node is WorkLogTypeNode || node is WorkLogCategoryNode || node is WorkLogProjectNode)
                node.IsExpanded = true;

            // Type/Category에 이미 Project가 있으면 중간 화면 건너뛰고 Project로 바로 이동
            if (IsEditMode && (node is WorkLogTypeNode || node is WorkLogCategoryNode))
            {
                var project = ActiveProject;
                if (project != null)
                {
                    project.IsExpanded = true;
                    Tree.Select(project);
                    node = project;
                }
            }

            SubscribeNode(node);
            RefreshCategoryOptions();
            RefreshHints();
            SyncEditStepToSelection();
            if (!(node is WorkLogProjectNode))
                ClearFocusedSource();
            else if (_focusedSource != null)
            {
                var p = node as WorkLogProjectNode;
                if (p == null || p.Sources == null || !p.Sources.Contains(_focusedSource))
                    ClearFocusedSource();
            }
            RaisePropertyChanged("BreadcrumbPath");
            RaisePropertyChanged("DeleteNodeLabel");
            RaisePropertyChanged("ShowProjectWorkspace");
            RaisePropertyChanged("HasWorkNodeSelected");
            RaisePropertyChanged("ActiveProject");
            RaisePropertyChanged("HasActiveProject");
            RaisePropertyChanged("ActiveMenu");
            RaisePropertyChanged("HasActiveMenu");
            RaisePropertyChanged("ShowSourcePanel");
            RefreshWorkFormReveal(resetUnlock: true);
            RaiseStackVisibility();
            TryAutoRevealWorkFromMenu();
            RefreshDetailPresentation();
            RaiseCanExecutes();
        }

        private void SyncEditStepToSelection()
        {
            // 단일 스택 UI: 트리 선택만 맞추고 페이지 전환은 하지 않음.
            if (!IsEditMode || Tree == null)
                return;
            if (Tree.IsMenuSelected)
                TryAutoRevealWorkFromMenu();
            else if (Tree.IsTypeSelected || Tree.IsCategorySelected || Tree.IsProjectSelected)
                EnsureProjectUnderSelection();
        }

        /// <summary>Site + Menu명이 채워지면 Type/Project 골격을 만들어 Work 스택을 아래에 붙인다.</summary>
        private void TryAutoRevealWorkFromMenu()
        {
            if (_autoScaffolding)
                return;

            // ActiveMenu가 있어도 트리 미선택이면 PropertyChanged 구독이 없을 수 있음 → 보강
            EnsureActiveMenuSubscribed();

            if (!IsEditMode || ActiveMenu == null || !HasSiteCode)
            {
                RaiseStackVisibility();
                return;
            }

            if (string.IsNullOrWhiteSpace(ActiveMenu.MenuName))
            {
                RaiseStackVisibility();
                return;
            }

            bool wasShowingWork = ShowWorkFormStack;

            _autoScaffolding = true;
            try
            {
                EnsureTypeSelectedForWorkStep();
                EnsureScratchProjectUnderMenu();
            }
            finally
            {
                _autoScaffolding = false;
            }
            RaiseStackVisibility();

            if (!wasShowingWork && ShowWorkFormStack && RequestScrollToWorkForm != null)
                RequestScrollToWorkForm();
        }

        /// <summary>신규 작성처럼 Menu만 있고 SelectedNode가 비어 있어도 Menu 입력을 감지하도록 구독.</summary>
        private void EnsureActiveMenuSubscribed()
        {
            var menu = ActiveMenu;
            if (menu == null)
                return;
            if (ReferenceEquals(_subscribedMenu, menu))
                return;
            // OnSelectNode(menu)면 이미 _subscribedNode로 구독 중
            if (ReferenceEquals(_subscribedNode, menu))
                return;
            if (_subscribedMenu != null)
                _subscribedMenu.PropertyChanged -= OnSubscribedNodePropertyChanged;
            _subscribedMenu = menu;
            _subscribedMenu.PropertyChanged += OnSubscribedNodePropertyChanged;
        }

        /// <summary>메뉴 작성 직후 Unselected Type이어도 Project를 만들어 점진 폼(Type 칩)을 바로 쓴다.</summary>
        private void EnsureScratchProjectUnderMenu()
        {
            if (!IsEditMode || Tree == null || ActiveMenu == null)
                return;
            if (ActiveProject != null)
                return;

            EnsureTypeSelectedForWorkStep();

            var typeNode = Tree.SelectedTypeNode;
            if (typeNode == null && ActiveMenu.Types.Count > 0)
                typeNode = ActiveMenu.Types[0];
            if (typeNode == null)
                return;

            WorkLogCategoryNode cat = typeNode.Categories.FirstOrDefault();
            if (cat == null)
            {
                cat = new WorkLogCategoryNode
                {
                    Category = string.Empty,
                    IsExpanded = true
                };
                typeNode.Categories.Add(cat);
            }

            typeNode.IsExpanded = true;
            cat.IsExpanded = true;
            ActiveMenu.IsExpanded = true;

            WorkLogProjectNode project = cat.Projects.FirstOrDefault();
            if (project == null)
            {
                project = new WorkLogProjectNode
                {
                    Type = string.IsNullOrWhiteSpace(typeNode.Type)
                        ? WorkLogFieldMasters.Unselected
                        : typeNode.Type,
                    IsExpanded = true
                };
                cat.Projects.Add(project);
            }

            if (!ReferenceEquals(Tree.SelectedProject, project))
                OnSelectNode(project);
        }

        /// <summary>Type/Category 선택 시 Project가 없으면 만들어 점진 입력 폼을 쓸 수 있게 한다.</summary>
        private void EnsureProjectUnderSelection()
        {
            if (!IsEditMode || Tree == null || ActiveProject != null)
                return;

            if (Tree.SelectedCategoryNode != null)
            {
                var cat = Tree.SelectedCategoryNode;
                if (cat.Projects.Count == 0)
                {
                    var parentType = FindTypeForCategory(cat);
                    var project = new WorkLogProjectNode
                    {
                        Type = parentType != null && !string.IsNullOrWhiteSpace(parentType.Type)
                            ? parentType.Type
                            : WorkLogFieldMasters.Unselected,
                        Category = cat.Category, // null 유지 가능(미선택). 값이 있으면 그대로.
                        IsExpanded = true
                    };
                    cat.Projects.Add(project);
                    cat.IsExpanded = true;
                    OnSelectNode(project);
                }
                else
                {
                    OnSelectNode(cat.Projects[0]);
                }
                return;
            }

            if (Tree.SelectedTypeNode != null)
            {
                // Unselected Type이어도 Project 골격 생성 (Type 칩부터 바로 선택)
                EnsureScratchProjectUnderMenu();
            }
        }

        private WorkLogTypeNode FindTypeForCategory(WorkLogCategoryNode cat)
        {
            if (Tree == null || cat == null)
                return null;
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    if (t.Categories.Contains(cat))
                        return t;
            return null;
        }

        private void UnlockWorkCategoryReveal()
        {
            _workCategoryUnlocked = true;
            RefreshWorkFormReveal(resetUnlock: false);
            RaiseStackVisibility();
        }

        /// <summary>
        /// Work 단계 필드 점진 공개.
        /// resetUnlock=true면 Project 전환/Type 변경 시 잠금 상태를 데이터 기준으로 다시 잡는다.
        /// </summary>
        private void RefreshWorkFormReveal(bool resetUnlock)
        {
            var p = ActiveProject;
            if (p == null)
            {
                _workRevealProject = null;
                _workCategoryUnlocked = false;
                SetWorkRevealFlags(false, false, false, false, false);
                return;
            }

            if (!ReferenceEquals(_workRevealProject, p))
            {
                _workRevealProject = p;
                resetUnlock = true;
            }

            if (resetUnlock)
                _workCategoryUnlocked = ComputeWorkCategoryUnlocked(p);
            else if (!_workCategoryUnlocked)
                _workCategoryUnlocked = ComputeWorkCategoryUnlocked(p);

            // Type 미선택이어도 Category부터 공개. named category 없는 Type은 나머지까지 바로.
            bool namedCategories = TypeHasNamedCategories(p.Type);
            if (!namedCategories)
                _workCategoryUnlocked = true;

            bool showCategory = true;
            bool showRest = _workCategoryUnlocked;
            bool showProjectName = showRest;
            bool showDeployStatus = showRest;
            bool showDeployDate = showRest;
            bool showComment = showRest;

            // 기존 값 보존: 하위 값이 있으면 해당 구간 강제 공개
            if (HasWorkComment(p) || HasWorkDeployDate(p)
                || IsWorkDeploySelected(p.DeploymentStatus)
                || HasWorkProjectName(p)
                || p.Category != null)
            {
                showCategory = true;
                if (p.Category != null || HasWorkProjectName(p) || IsWorkDeploySelected(p.DeploymentStatus)
                    || HasWorkDeployDate(p) || HasWorkComment(p) || !namedCategories)
                {
                    showProjectName = true;
                    showDeployStatus = true;
                    showDeployDate = true;
                    showComment = true;
                    _workCategoryUnlocked = true;
                }
            }

            SetWorkRevealFlags(showCategory, showProjectName, showDeployStatus, showDeployDate, showComment);
        }

        private void SetWorkRevealFlags(
            bool category, bool projectName, bool deployStatus, bool deployDate, bool comment)
        {
            bool changed = false;
            if (_showWorkCategorySection != category)
            {
                _showWorkCategorySection = category;
                changed = true;
            }
            if (_showWorkProjectNameSection != projectName)
            {
                _showWorkProjectNameSection = projectName;
                changed = true;
            }
            if (_showWorkDeployStatusSection != deployStatus)
            {
                _showWorkDeployStatusSection = deployStatus;
                changed = true;
            }
            if (_showWorkDeployDateSection != deployDate)
            {
                _showWorkDeployDateSection = deployDate;
                changed = true;
            }
            if (_showWorkCommentSection != comment)
            {
                _showWorkCommentSection = comment;
                changed = true;
            }

            if (!changed)
            {
                RaiseStackVisibility();
                return;
            }

            RaisePropertyChanged("ShowWorkCategorySection");
            RaisePropertyChanged("ShowWorkProjectNameSection");
            RaisePropertyChanged("ShowWorkDeployStatusSection");
            RaisePropertyChanged("ShowWorkDeployDateSection");
            RaisePropertyChanged("ShowWorkCommentSection");
            RaiseStackVisibility();
        }

        private void RaiseStackVisibility()
        {
            RaisePropertyChanged("ShowWorkFormStack");
            RaisePropertyChanged("ShowSourceFormStack");
            RaisePropertyChanged("CanGoNextStep");
            RaiseStepCommands();
        }

        private static bool ComputeWorkCategoryUnlocked(WorkLogProjectNode p)
        {
            if (p == null)
                return false;
            // Type 미선택/named category 없음 → 나머지 필드 바로 공개
            if (!TypeHasNamedCategories(p.Type))
                return true;
            // null = 아직 미선택. ""(미선택 옵션) 또는 값이면 선택 완료.
            if (p.Category != null)
                return true;
            if (HasWorkProjectName(p) || IsWorkDeploySelected(p.DeploymentStatus)
                || HasWorkDeployDate(p) || HasWorkComment(p))
                return true;
            return false;
        }

        private static bool TypeHasNamedCategories(string type)
        {
            var cats = WorkLogFieldMasters.CategoriesForType(
                string.IsNullOrWhiteSpace(type) ? WorkLogFieldMasters.Unselected : type);
            if (cats == null)
                return false;
            foreach (string c in cats)
            {
                if (!string.IsNullOrEmpty(c))
                    return true;
            }
            return false;
        }

        private static bool IsWorkTypeSelected(string type)
        {
            return !string.IsNullOrWhiteSpace(type)
                && !string.Equals(type, WorkLogFieldMasters.Unselected, StringComparison.Ordinal);
        }

        private static bool IsWorkDeploySelected(string status)
        {
            return !string.IsNullOrWhiteSpace(status)
                && !string.Equals(status, WorkLogFieldMasters.Unselected, StringComparison.Ordinal);
        }

        private static bool HasWorkProjectName(WorkLogProjectNode p)
        {
            return p != null && !string.IsNullOrWhiteSpace(p.ProjectName);
        }

        private static bool HasWorkDeployDate(WorkLogProjectNode p)
        {
            if (p == null)
                return false;
            if (p.DeploymentDate.HasValue)
                return true;
            return !string.IsNullOrWhiteSpace(p.DeploymentDateText);
        }

        private static bool HasWorkComment(WorkLogProjectNode p)
        {
            return p != null && !string.IsNullOrWhiteSpace(p.Comment);
        }

        /// <summary>트리 Source 클릭 → 조회 시 선택만, 편집 시 Source 단계로.</summary>
        private void OnSelectSource(WorkLogSourceEditItem source)
        {
            if (source == null || Tree == null)
                return;

            var project = FindProjectContainingSource(source);
            if (project == null)
                return;

            if (IsReadOnlyMode)
            {
                OnSelectNode(project);
                SetFocusedSource(source);
                RefreshDetailPresentation();
                return;
            }

            OnSelectNode(project);
            SetFocusedSource(source);
            EditStepIndex = 1;
        }

        private void SetFocusedSource(WorkLogSourceEditItem source)
        {
            _focusedSource = source;
            ClearAllSourceActive();
            if (source != null)
                source.IsActive = true;
        }

        private void ClearFocusedSource()
        {
            _focusedSource = null;
            ClearAllSourceActive();
        }

        private void ClearAllSourceActive()
        {
            if (Tree == null)
                return;
            foreach (var menu in Tree.MenuSections)
            {
                if (menu == null || menu.Types == null)
                    continue;
                foreach (var t in menu.Types)
                {
                    if (t == null || t.Categories == null)
                        continue;
                    foreach (var c in t.Categories)
                    {
                        if (c == null || c.Projects == null)
                            continue;
                        foreach (var p in c.Projects)
                        {
                            if (p == null || p.Sources == null)
                                continue;
                            foreach (var s in p.Sources)
                            {
                                if (s != null)
                                    s.IsActive = false;
                            }
                        }
                    }
                }
            }
        }

        private WorkLogProjectNode FindProjectContainingSource(WorkLogSourceEditItem source)
        {
            if (source == null || Tree == null)
                return null;
            foreach (var menu in Tree.MenuSections)
                foreach (var t in menu.Types)
                    foreach (var c in t.Categories)
                        foreach (var p in c.Projects)
                            if (p.Sources != null && p.Sources.Contains(source))
                                return p;
            return null;
        }

        private void GoNextStep()
        {
            if (!CanGoNextStep)
                return;

            if (EditStepIndex == 0)
            {
                EnsureTypeSelectedForWorkStep();
                EnsureScratchProjectUnderMenu();
                EditStepIndex = 1;
            }
        }

        /// <summary>작업 단계 진입 시 Type이 없으면 추가, 있으면 첫 Type 선택.</summary>
        private void EnsureTypeSelectedForWorkStep()
        {
            var menu = ActiveMenu;
            if (menu == null)
                return;
            if (menu.Types.Count == 0)
            {
                var node = new WorkLogTypeNode { Type = WorkLogFieldMasters.Unselected, IsExpanded = true };
                menu.Types.Add(node);
                menu.IsExpanded = true;
                OnSelectNode(node);
                return;
            }

            if (!HasWorkNodeSelected)
            {
                var first = menu.Types[0];
                first.IsExpanded = true;
                OnSelectNode(first);
            }
        }

        private void GoPrevStep()
        {
            if (EditStepIndex > 0)
                EditStepIndex--;
        }

        private void SelectStep(object param)
        {
            int step;
            if (param is int)
                step = (int)param;
            else if (param != null && int.TryParse(param.ToString(), out step))
            { }
            else
                return;
            EditStepIndex = step;
        }

        private void RaiseStepCommands()
        {
            var next = NextStepCommand as RelayCommand;
            if (next != null) next.RaiseCanExecuteChanged();
            var prev = PrevStepCommand as RelayCommand;
            if (prev != null) prev.RaiseCanExecuteChanged();
        }

        private void SubscribeNode(WorkLogTreeNodeBase node)
        {
            _subscribedNode = node;
            if (_subscribedNode != null)
                _subscribedNode.PropertyChanged += OnSubscribedNodePropertyChanged;

            var project = ActiveProject;
            if (project != null && !ReferenceEquals(project, node))
            {
                _subscribedProject = project;
                _subscribedProject.PropertyChanged += OnSubscribedNodePropertyChanged;
            }

            // Menu 입력 중에도 Work 스택 자동 공개가 끊기지 않게 Menu는 항상 구독
            var menu = ActiveMenu;
            if (menu != null
                && !ReferenceEquals(menu, node)
                && !ReferenceEquals(menu, project))
            {
                _subscribedMenu = menu;
                _subscribedMenu.PropertyChanged += OnSubscribedNodePropertyChanged;
            }
        }

        private void UnsubscribeNode()
        {
            if (_subscribedNode != null)
                _subscribedNode.PropertyChanged -= OnSubscribedNodePropertyChanged;
            _subscribedNode = null;
            if (_subscribedProject != null)
                _subscribedProject.PropertyChanged -= OnSubscribedNodePropertyChanged;
            _subscribedProject = null;
            if (_subscribedMenu != null)
                _subscribedMenu.PropertyChanged -= OnSubscribedNodePropertyChanged;
            _subscribedMenu = null;
        }

        private void OnSubscribedNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_syncingTree)
                return;

            var menu = sender as WorkLogMenuSectionNode;
            if (menu != null)
            {
                if (e.PropertyName == "MenuName" || e.PropertyName == "Title"
                    || e.PropertyName == "Pc" || e.PropertyName == "PersonInCharge"
                    || e.PropertyName == "StartDate" || e.PropertyName == "EndDate"
                    || e.PropertyName == "MetaLine" || e.PropertyName == "PeriodDisplay")
                {
                    RaisePropertyChanged("BreadcrumbPath");
                    RaisePropertyChanged("DialogTitle");
                    RefreshDetailPresentation();
                    if (e.PropertyName == "MenuName" || e.PropertyName == "Title"
                        || e.PropertyName == "Pc" || e.PropertyName == "PersonInCharge"
                        || e.PropertyName == "StartDate" || e.PropertyName == "EndDate")
                    {
                        RaisePropertyChanged("CanGoNextStep");
                        RaiseStepCommands();
                        if (e.PropertyName == "PersonInCharge"
                            && string.Equals(_validationField, "PersonInCharge", StringComparison.Ordinal))
                            ClearFieldValidation();
                        else if ((e.PropertyName == "StartDate" || e.PropertyName == "EndDate")
                            && string.Equals(_validationField, "Period", StringComparison.Ordinal))
                            ClearFieldValidation();
                        TryAutoRevealWorkFromMenu();
                    }
                }
                return;
            }

            var typeNode = sender as WorkLogTypeNode;
            if (typeNode != null && e.PropertyName == "Type")
            {
                _syncingTree = true;
                try
                {
                    Tree.ApplyTypeNodeRename(typeNode, typeNode.Type);
                    ResubscribeSelection();
                    RefreshCategoryOptions();
                    RefreshHints();
                    RaisePropertyChanged("BreadcrumbPath");
                    RaisePropertyChanged("Tree");
                    RaisePropertyChanged("ActiveProject");
                    RaisePropertyChanged("HasActiveProject");
                    RaisePropertyChanged("ShowSourcePanel");
                    RaisePropertyChanged("ShowProjectWorkspace");
                    RaisePropertyChanged("HasWorkNodeSelected");
                    RefreshWorkFormReveal(resetUnlock: true);
                }
                finally
                {
                    _syncingTree = false;
                }
                EnsureProjectUnderSelection();
                RaiseStackVisibility();
                return;
            }

            var catNode = sender as WorkLogCategoryNode;
            if (catNode != null && e.PropertyName == "Category")
            {
                _syncingTree = true;
                try
                {
                    Tree.ApplyCategoryNodeRename(catNode, catNode.Category);
                    ResubscribeSelection();
                    RefreshHints();
                    RaisePropertyChanged("BreadcrumbPath");
                    RaisePropertyChanged("Tree");
                    RaisePropertyChanged("ActiveProject");
                    RaisePropertyChanged("HasActiveProject");
                    RaisePropertyChanged("ShowSourcePanel");
                    UnlockWorkCategoryReveal();
                }
                finally
                {
                    _syncingTree = false;
                }
                EnsureProjectUnderSelection();
                RaiseStackVisibility();
                return;
            }

            var project = sender as WorkLogProjectNode;
            if (project != null)
            {
                if (e.PropertyName == "Type" || e.PropertyName == "Category")
                {
                    _syncingTree = true;
                    try
                    {
                        Tree.RelocateProject(project, project.Type, project.Category);
                        Tree.Select(project);
                        ResubscribeSelection();
                        RefreshCategoryOptions();
                        RefreshHints();
                        RaisePropertyChanged("BreadcrumbPath");
                        RaisePropertyChanged("Tree");
                        RaisePropertyChanged("ActiveProject");
                        RaisePropertyChanged("HasActiveProject");
                        RaisePropertyChanged("ShowSourcePanel");
                        RaisePropertyChanged("ShowProjectWorkspace");
                        RaisePropertyChanged("HasWorkNodeSelected");
                        RaisePropertyChanged("CanGoNextStep");
                        RaiseStepCommands();
                        if (e.PropertyName == "Type"
                            && string.Equals(_validationField, "Type", StringComparison.Ordinal))
                            ClearFieldValidation();
                        if (e.PropertyName == "Type")
                            RefreshWorkFormReveal(resetUnlock: true);
                        else
                            UnlockWorkCategoryReveal();
                    }
                    finally
                    {
                        _syncingTree = false;
                    }
                }
                else if (e.PropertyName == "ProjectName" || e.PropertyName == "Title")
                {
                    RaisePropertyChanged("BreadcrumbPath");
                    RaisePropertyChanged("ActiveProject");
                    RefreshWorkFormReveal(resetUnlock: false);
                    RefreshDetailPresentation();
                }
                else if (e.PropertyName == "DeploymentStatus"
                    || e.PropertyName == "DeploymentDate"
                    || e.PropertyName == "DeploymentDateText"
                    || e.PropertyName == "Comment")
                {
                    RefreshHints();
                    RefreshWorkFormReveal(resetUnlock: false);
                    RefreshDetailPresentation();
                }
                else
                {
                    RefreshHints();
                    RefreshDetailPresentation();
                }
            }
        }

        private void ResubscribeSelection()
        {
            UnsubscribeNode();
            if (Tree != null && Tree.SelectedNode != null)
                SubscribeNode(Tree.SelectedNode);
            RaisePropertyChanged("ActiveProject");
            RaisePropertyChanged("HasActiveProject");
            RaisePropertyChanged("ActiveMenu");
            RaisePropertyChanged("HasActiveMenu");
            RaisePropertyChanged("ShowSourcePanel");
            RaisePropertyChanged("ShowProjectWorkspace");
            RaisePropertyChanged("HasWorkNodeSelected");
            RefreshWorkFormReveal(resetUnlock: true);
            RaiseStackVisibility();
            RefreshDetailPresentation();
        }

        private string FindParentTypeTitle(WorkLogCategoryNode cat)
        {
            if (Tree == null || cat == null)
                return "Type";
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    if (t.Categories.Contains(cat))
                        return t.Title;
            return "Type";
        }

        private WorkLogMenuSectionNode FindMenuForType(WorkLogTypeNode type)
        {
            if (Tree == null || type == null)
                return null;
            return Tree.MenuSections.FirstOrDefault(m => m.Types.Contains(type));
        }

        private WorkLogMenuSectionNode FindMenuForCategory(WorkLogCategoryNode cat)
        {
            if (Tree == null || cat == null)
                return null;
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    if (t.Categories.Contains(cat))
                        return m;
            return null;
        }

        private WorkLogMenuSectionNode FindMenuForProject(WorkLogProjectNode project)
        {
            if (Tree == null || project == null)
                return null;
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    foreach (var c in t.Categories)
                        if (c.Projects.Contains(project))
                            return m;
            return null;
        }

        private void RefreshCategoryOptions()
        {
            string type = null;
            if (Tree != null && Tree.SelectedProject != null)
                type = Tree.SelectedProject.Type;
            else if (Tree != null && Tree.SelectedTypeNode != null)
                type = Tree.SelectedTypeNode.Type;
            else if (Tree != null && Tree.SelectedCategoryNode != null)
            {
                foreach (var m in Tree.MenuSections)
                    foreach (var t in m.Types)
                        if (t.Categories.Contains(Tree.SelectedCategoryNode))
                            type = t.Type;
            }

            FilteredCategories = WorkLogFieldMasters.CategoriesForType(
                string.IsNullOrWhiteSpace(type) ? WorkLogFieldMasters.Unselected : type);
        }

        private void RefreshHints()
        {
            HintMessage = null;
        }

        private bool CanSave()
        {
            return IsEditMode && Tree != null && Tree.MenuSections.Count > 0;
        }

        private void Save()
        {
            ClearFieldValidation();
            var issue = WorkLogEditValidator.ValidateForSave(
                SiteCode,
                Tree != null ? Tree.MenuSections : null);
            if (issue != null)
            {
                ApplyValidationIssue(issue);
                return;
            }

            WriteStatus = WorkLogWriteStatus.Completed;
            if (ApplyRequested != null)
                ApplyRequested();
            // IsEditMode / Close 는 Persist 성공 후 ListVM 에서 처리
        }

        /// <summary>Validator Issue → FieldError / Step / Tree 선택 (UI reaction만).</summary>
        private void ApplyValidationIssue(WorkLogValidationIssue issue)
        {
            if (issue == null)
                return;

            SetFieldValidation(issue.FieldKey, issue.Message);

            switch (issue.Kind)
            {
                case WorkLogValidationKind.MissingSite:
                case WorkLogValidationKind.MissingMenuPerson:
                case WorkLogValidationKind.MissingMenuStartDate:
                    EditStepIndex = 0;
                    break;
                case WorkLogValidationKind.MissingProject:
                case WorkLogValidationKind.MissingType:
                case WorkLogValidationKind.MissingSource:
                    EditStepIndex = 1;
                    break;
            }

            if (issue.TargetNode != null && Tree != null)
                Tree.Select(issue.TargetNode);
        }

        /// <summary>DB 저장 성공 후 호출 — 편집 헤더 상태 정리.</summary>
        public void MarkPersistedAfterSave(bool completed)
        {
            _cancelSnapshot = CaptureSnapshot();
            IsNewRecord = false;
            DiscardOnCancel = false;
            if (completed)
                IsEditMode = false;
            ClearFieldValidation();
        }

        /// <summary>Persist 실패 시 편집 헤더(저장)가 유지되도록.</summary>
        public void RestoreEditModeAfterFailedPersist(string message)
        {
            IsEditMode = true;
            _validationField = null;
            ValidationMessage = string.IsNullOrWhiteSpace(message) ? null : message;
            RaiseCanExecutes();
        }

        private void AddMenu()
        {
            if (Tree == null)
                Tree = new WorkLogTicketTreeViewModel();
            var menu = new WorkLogMenuSectionNode
            {
                MenuName = string.Empty,
                IsExpanded = true
            };
            Tree.MenuSections.Add(menu);
            OnSelectNode(menu);
            RaisePropertyChanged("MenuSectionCount");
        }

        /// <summary>트리 + : Menu→Type, Type→Category, Category→Project (Source는 Source 탭에서만)</summary>
        private void AddChild(WorkLogTreeNodeBase parent)
        {
            if (parent is WorkLogMenuSectionNode menu)
            {
                AddTypeUnder(menu);
                return;
            }
            if (parent is WorkLogTypeNode type)
            {
                AddCategoryUnder(type);
                return;
            }
            if (parent is WorkLogCategoryNode cat)
            {
                AddProjectUnder(cat);
            }
        }

        private void AddType()
        {
            AddTypeUnder(ActiveMenu);
        }

        private void AddTypeUnder(WorkLogMenuSectionNode menu)
        {
            if (menu == null)
                return;
            var node = new WorkLogTypeNode { Type = WorkLogFieldMasters.Unselected, IsExpanded = true };
            menu.Types.Add(node);
            menu.IsExpanded = true;
            OnSelectNode(node);
            if (IsEditMode)
                EditStepIndex = 1;
        }

        private void AddCategory()
        {
            AddCategoryUnder(ResolveActiveType());
        }

        private void AddCategoryUnder(WorkLogTypeNode type)
        {
            if (type == null)
                return;
            var node = new WorkLogCategoryNode
            {
                Type = type.Type ?? string.Empty,
                Category = string.Empty,
                IsExpanded = true
            };
            type.Categories.Add(node);
            type.IsExpanded = true;
            OnSelectNode(node);
            if (IsEditMode)
                EditStepIndex = 1;
        }

        private void AddProject()
        {
            AddProjectUnder(ResolveActiveCategory());
        }

        private void AddProjectUnder(WorkLogCategoryNode cat)
        {
            if (cat == null || Tree == null)
                return;
            string type = null;
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    if (t.Categories.Contains(cat))
                        type = t.Type;

            var node = new WorkLogProjectNode
            {
                ProjectName = string.Empty,
                Type = type ?? string.Empty,
                Category = cat.Category ?? string.Empty,
                IsExpanded = true
            };
            cat.Projects.Add(node);
            cat.IsExpanded = true;
            OnSelectNode(node);
            if (IsEditMode)
                EditStepIndex = 1;
        }

        /// <summary>Type 선택 또는 현재 Project의 부모 Type.</summary>
        private WorkLogTypeNode ResolveActiveType()
        {
            if (Tree == null)
                return null;
            if (Tree.SelectedTypeNode != null)
                return Tree.SelectedTypeNode;
            var project = ActiveProject;
            if (project == null)
                return null;
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    foreach (var c in t.Categories)
                        if (c.Projects.Contains(project))
                            return t;
            return null;
        }

        /// <summary>Category 선택 또는 현재 Project의 부모 Category.</summary>
        private WorkLogCategoryNode ResolveActiveCategory()
        {
            if (Tree == null)
                return null;
            if (Tree.SelectedCategoryNode != null)
                return Tree.SelectedCategoryNode;
            var project = ActiveProject;
            if (project == null)
                return null;
            foreach (var m in Tree.MenuSections)
                foreach (var t in m.Types)
                    foreach (var c in t.Categories)
                        if (c.Projects.Contains(project))
                            return c;
            return null;
        }

        private void AddSource()
        {
            var p = ActiveProject;
            if (p == null)
                return;
            if (Tree != null && Tree.SelectedProject != p)
                Tree.Select(p);
            p.Sources.Add(new WorkLogSourceEditItem
            {
                ChangeDetailText = string.Empty,
                SourceOrigin = "Manual",
                Type = p.Type,
                Category = p.Category,
                ProjectName = p.ProjectName,
                RecordedAt = DateTime.Now
            });
            EditStepIndex = 1;
            if (string.Equals(_validationField, "Source", StringComparison.Ordinal)
                || string.Equals(_validationField, "Type", StringComparison.Ordinal))
                ClearFieldValidation();
            RaisePropertyChanged("ActiveProject");
            RaisePropertyChanged("ShowSourcePanel");
            RaiseStackVisibility();
        }

        private void RemoveSource(WorkLogSourceEditItem s)
        {
            if (s == null || Tree == null)
                return;
            foreach (var menu in Tree.MenuSections)
                foreach (var t in menu.Types)
                    foreach (var c in t.Categories)
                        foreach (var p in c.Projects)
                            if (p.Sources.Contains(s))
                            {
                                p.Sources.Remove(s);
                                return;
                            }
        }

        private void RemoveSelectedNode()
        {
            if (Tree != null)
                RemoveNode(Tree.SelectedNode);
        }

        private void RemoveNode(WorkLogTreeNodeBase node)
        {
            if (Tree == null || node == null)
                return;

            if (node is WorkLogMenuSectionNode menuNode)
            {
                int idx = Tree.MenuSections.IndexOf(menuNode);
                Tree.MenuSections.Remove(menuNode);
                RaisePropertyChanged("MenuSectionCount");
                WorkLogTreeNodeBase next = null;
                if (Tree.MenuSections.Count > 0)
                {
                    if (idx >= Tree.MenuSections.Count)
                        idx = Tree.MenuSections.Count - 1;
                    if (idx >= 0)
                        next = Tree.MenuSections[idx];
                }
                OnSelectNode(next);
                return;
            }

            foreach (var menu in Tree.MenuSections)
            {
                if (node is WorkLogTypeNode typeNode && menu.Types.Contains(typeNode))
                {
                    menu.Types.Remove(typeNode);
                    OnSelectNode(menu);
                    return;
                }

                foreach (var t in menu.Types)
                {
                    if (node is WorkLogCategoryNode catNode && t.Categories.Contains(catNode))
                    {
                        t.Categories.Remove(catNode);
                        OnSelectNode(t);
                        return;
                    }

                    foreach (var c in t.Categories)
                    {
                        if (node is WorkLogProjectNode projNode && c.Projects.Contains(projNode))
                        {
                            c.Projects.Remove(projNode);
                            OnSelectNode(c);
                            return;
                        }
                    }
                }
            }
        }

        private void RaiseCanExecutes()
        {
            ((RelayCommand)AddMenuCommand).RaiseCanExecuteChanged();
            ((RelayCommand<WorkLogTreeNodeBase>)AddChildCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddTypeCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddCategoryCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddProjectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddSourceCommand).RaiseCanExecuteChanged();
            ((RelayCommand<WorkLogTreeNodeBase>)RemoveNodeCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveSelectedNodeCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            ((RelayCommand)EnterEditModeCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteWorkLogCommand).RaiseCanExecuteChanged();
            RaiseStepCommands();
        }

        private void RefreshDetailPresentation()
        {
            RebuildBreadcrumbParts();
            RebuildCommentBlocks();
            RaisePropertyChanged("BreadcrumbPath");
            RaisePropertyChanged("HasBreadcrumb");
            RaisePropertyChanged("DetailPerson");
            RaisePropertyChanged("DetailPersonMeta");
            RaisePropertyChanged("DetailPeriod");
            RaisePropertyChanged("DetailPeriodMeta");
            RaisePropertyChanged("DetailMenu");
            RaisePropertyChanged("DetailSite");
            RaisePropertyChanged("DetailIp");
            RaisePropertyChanged("DetailTypeChip");
            RaisePropertyChanged("HasDetailTypeChip");
            RaisePropertyChanged("DetailCategoryChip");
            RaisePropertyChanged("HasDetailCategoryChip");
            RaisePropertyChanged("DetailDeployChip");
            RaisePropertyChanged("HasDetailDeployChip");
            RaisePropertyChanged("DetailDeployDate");
            RaisePropertyChanged("DetailProject");
            RaisePropertyChanged("DetailSourceHeader");
            RaisePropertyChanged("DetailSources");
            RaisePropertyChanged("HasDetailSources");
            RaisePropertyChanged("HasCommentBlocks");
            RaisePropertyChanged("CommentBlocks");
        }

        private WorkLogMenuSectionNode ResolveDetailMenu()
        {
            if (ActiveMenu != null)
                return ActiveMenu;
            if (Tree == null || Tree.MenuSections == null)
                return null;
            return Tree.MenuSections.FirstOrDefault();
        }

        private WorkLogProjectNode ResolveDetailProject()
        {
            if (ActiveProject != null)
                return ActiveProject;
            if (Tree == null || Tree.MenuSections == null)
                return null;
            foreach (var menu in Tree.MenuSections)
            {
                if (menu == null || menu.Types == null)
                    continue;
                foreach (var t in menu.Types)
                {
                    if (t == null || t.Categories == null)
                        continue;
                    foreach (var c in t.Categories)
                    {
                        if (c == null || c.Projects == null)
                            continue;
                        var p = c.Projects.FirstOrDefault();
                        if (p != null)
                            return p;
                    }
                }
            }
            return null;
        }

        private void RebuildBreadcrumbParts()
        {
            _breadcrumbParts.Clear();
            if (Tree == null || Tree.SelectedNode == null)
            {
                RaisePropertyChanged("HasBreadcrumb");
                return;
            }

            var parts = new System.Collections.Generic.List<string>();
            if (Tree.IsMenuSelected)
            {
                parts.Add(Tree.SelectedMenu.Title);
            }
            else if (Tree.IsTypeSelected)
            {
                var menu = FindMenuForType(Tree.SelectedTypeNode);
                if (menu != null)
                    parts.Add(menu.Title);
                parts.Add(Tree.SelectedTypeNode.Title);
            }
            else if (Tree.IsCategorySelected)
            {
                var menu = FindMenuForCategory(Tree.SelectedCategoryNode);
                if (menu != null)
                    parts.Add(menu.Title);
                parts.Add(FindParentTypeTitle(Tree.SelectedCategoryNode));
                if (!string.IsNullOrWhiteSpace(Tree.SelectedCategoryNode.Title))
                    parts.Add(Tree.SelectedCategoryNode.Title);
            }
            else if (Tree.IsProjectSelected)
            {
                var p = Tree.SelectedProject;
                var menu = FindMenuForProject(p);
                if (menu != null)
                    parts.Add(menu.Title);
                parts.Add(string.IsNullOrWhiteSpace(p.Type) ? "Type" : p.Type);
                if (!string.IsNullOrWhiteSpace(p.Category)
                    && !string.Equals(p.Category, WorkLogFieldMasters.Unselected, StringComparison.Ordinal))
                    parts.Add(p.Category);
                if (!string.IsNullOrWhiteSpace(p.Title))
                    parts.Add(p.Title);
                if (_focusedSource != null
                    && projectHasFocusedSource(p)
                    && !string.IsNullOrWhiteSpace(_focusedSource.ChangeDetailText))
                    parts.Add(_focusedSource.ChangeDetailText);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    continue;
                _breadcrumbParts.Add(new WorkLogBreadcrumbPart
                {
                    Text = parts[i],
                    IsCurrent = i == parts.Count - 1
                });
            }
            RaisePropertyChanged("HasBreadcrumb");
        }

        private bool projectHasFocusedSource(WorkLogProjectNode p)
        {
            return p != null && p.Sources != null && _focusedSource != null
                && p.Sources.Contains(_focusedSource);
        }

        private void RebuildCommentBlocks()
        {
            _commentBlocks.Clear();
            var project = ResolveDetailProject();
            // COMMENT 카드: 프로젝트/업무 Comment. 예전에 TicketContents에 붙었던 타임라인은 상세에서만 복구.
            string raw = project != null ? (project.Comment ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                raw = WorkLogDraftMapper.ExtractTimelinePortion(TicketComment);
            if (string.IsNullOrWhiteSpace(raw))
            {
                RaisePropertyChanged("HasCommentBlocks");
                return;
            }

            raw = WorkLogListItemViewModel.EnsureNewlineAfterBrackets(raw);
            string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var body = new System.Text.StringBuilder();
            string pendingBadge = null;

            Action flush = () =>
            {
                string text = body.ToString().Trim();
                if (string.IsNullOrEmpty(pendingBadge) && string.IsNullOrEmpty(text))
                    return;
                _commentBlocks.Add(new WorkLogCommentBlock
                {
                    BadgeText = pendingBadge,
                    Body = text
                });
                body.Clear();
                pendingBadge = null;
            };

            foreach (string line in lines)
            {
                var m = CommentBadgeLine.Match(line ?? string.Empty);
                if (m.Success)
                {
                    flush();
                    pendingBadge = m.Groups[1].Value + " / " + m.Groups[2].Value.Trim();
                    continue;
                }
                if (body.Length > 0)
                    body.AppendLine();
                body.Append(line);
            }
            flush();
            RaisePropertyChanged("HasCommentBlocks");
        }

        private void ApplyTreeSearchFilter()
        {
            if (Tree == null || Tree.MenuSections == null)
                return;

            string q = (TreeSearchText ?? string.Empty).Trim();
            bool anyFilter = q.Length > 0;

            foreach (var menu in Tree.MenuSections)
            {
                if (menu == null)
                    continue;
                bool menuMatch = !anyFilter || ContainsIgnoreCase(menu.Title, q)
                    || ContainsIgnoreCase(menu.Pc, q)
                    || ContainsIgnoreCase(menu.PersonInCharge, q);
                bool anyChild = false;

                foreach (var type in menu.Types ?? Enumerable.Empty<WorkLogTypeNode>())
                {
                    if (type == null)
                        continue;
                    bool typeMatch = !anyFilter || ContainsIgnoreCase(type.Title, q);
                    bool typeChild = false;

                    foreach (var cat in type.Categories ?? Enumerable.Empty<WorkLogCategoryNode>())
                    {
                        if (cat == null)
                            continue;
                        bool catMatch = !anyFilter || ContainsIgnoreCase(cat.Title, q);
                        bool catChild = false;

                        foreach (var proj in cat.Projects ?? Enumerable.Empty<WorkLogProjectNode>())
                        {
                            if (proj == null)
                                continue;
                            bool projMatch = !anyFilter || ContainsIgnoreCase(proj.Title, q)
                                || ContainsIgnoreCase(proj.ProjectName, q);
                            bool srcMatch = false;
                            if (proj.Sources != null)
                            {
                                foreach (var s in proj.Sources)
                                {
                                    if (s == null)
                                        continue;
                                    bool hit = !anyFilter
                                        || ContainsIgnoreCase(s.ChangeDetailText, q)
                                        || ContainsIgnoreCase(s.FileName, q)
                                        || ContainsIgnoreCase(s.OriginalPath, q);
                                    // sources don't have IsTreeVisible - project shows if any source matches
                                    if (hit && anyFilter)
                                        srcMatch = true;
                                }
                            }

                            bool showProj = !anyFilter || projMatch || srcMatch;
                            proj.IsTreeVisible = showProj;
                            if (showProj)
                            {
                                catChild = true;
                                if (anyFilter && (projMatch || srcMatch))
                                    proj.IsExpanded = true;
                            }
                        }

                        bool showCat = !anyFilter || catMatch || catChild;
                        cat.IsTreeVisible = showCat;
                        if (showCat)
                        {
                            typeChild = true;
                            if (anyFilter && (catMatch || catChild))
                                cat.IsExpanded = true;
                        }
                    }

                    bool showType = !anyFilter || typeMatch || typeChild;
                    type.IsTreeVisible = showType;
                    if (showType)
                    {
                        anyChild = true;
                        if (anyFilter && (typeMatch || typeChild))
                            type.IsExpanded = true;
                    }
                }

                menu.IsTreeVisible = !anyFilter || menuMatch || anyChild;
                if (anyFilter && (menuMatch || anyChild))
                    menu.IsExpanded = true;
            }
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle))
                return true;
            if (string.IsNullOrEmpty(haystack))
                return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return string.Empty;
        }
    }
}
