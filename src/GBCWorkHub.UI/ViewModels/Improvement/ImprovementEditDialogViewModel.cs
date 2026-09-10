using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.Improvement;
using GBCWorkHub.DTO.Improvement;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.Update;

namespace GBCWorkHub.UI.ViewModels.Improvement
{
    /// <summary>PC 선택 팝업 한 줄. PC명만으로는 구분이 안 될 때를 대비해 접속 도메인(PcDomain)도 같이 보여준다.</summary>
    public sealed class ImprovementPcOptionViewModel
    {
        public ImprovementPcOptionViewModel(string pcName, string pcDomain)
        {
            PcName = pcName;
            PcDomain = pcDomain;
        }

        public string PcName { get; private set; }
        public string PcDomain { get; private set; }
        public bool HasPcDomain { get { return !string.IsNullOrWhiteSpace(PcDomain); } }
    }

    /// <summary>
    /// PC 선택 팝업의 팀 그룹 한 칸. 원격 PC 화면(RemoteWorkspaceViewModel)의 팀별 그룹핑과 같은
    /// 개념을 이 다이얼로그의 간단한 "이름만 고르면 되는" 용도에 맞게 가볍게 재구성한 것.
    /// </summary>
    public sealed class ImprovementPcGroupViewModel
    {
        public ImprovementPcGroupViewModel(string name, IEnumerable<ImprovementPcOptionViewModel> pcs)
        {
            Name = name;
            Pcs = new ObservableCollection<ImprovementPcOptionViewModel>(pcs);
        }

        public string Name { get; private set; }
        public bool HasName { get { return !string.IsNullOrWhiteSpace(Name); } }
        public ObservableCollection<ImprovementPcOptionViewModel> Pcs { get; private set; }
    }

    /// <summary>
    /// 요청 등록(신규) + 상세(기존, 댓글/반응/관리자 처리)를 겸하는 다이얼로그.
    /// WorkLogEditDialogViewModel과 동일한 관례: 부모(ImprovementListViewModel)가 열고,
    /// ApplyRequested/CloseRequested 이벤트로 저장/닫기를 위임한다.
    /// </summary>
    public sealed class ImprovementEditDialogViewModel : ViewModelBase
    {
        private readonly ImprovementBiz _biz;
        private IPopupService _popup;

        private bool _isNewRecord;
        private bool _isEditingContent;
        private long _reqId;
        private long _authorUserId;
        private ImprovementRequestDto _loadedDto;
        private static readonly Regex ImageTokenRegex = new Regex(@"\{\{img:([A-Za-z0-9]+)\}\}", RegexOptions.Compiled);

        private string _requestType = ImprovementRequestTypes.Bug;
        private string _title = string.Empty;
        private string _reproductionSteps = string.Empty;
        private string _siteCode;
        private string _pcName = string.Empty;
        private string _authorName;
        private bool _hasDeletedAuthorSuffix;
        private string _teamName;
        private string _appVersion;
        private string _createdAtText;
        private string _requestEditedAtText;
        private string _statusLabel;
        private string _resolvedNoteText;
        private string _validationError;

        private int _reproducedCount;
        private int _notReproducedCount;
        private string _myReaction;

        private string _newCommentText = string.Empty;
        private string _adminStatus;
        private string _adminResolvedVersion = string.Empty;
        private string _adminNote = string.Empty;
        private bool _isBusy;

        public ImprovementEditDialogViewModel(ImprovementBiz biz)
        {
            _biz = biz ?? throw new ArgumentNullException("biz");

            TypeOptions = new ObservableCollection<string>
            {
                ImprovementTypeLabels.Bug, ImprovementTypeLabels.Improvement, ImprovementTypeLabels.Etc
            };
            SiteOptions = new ObservableCollection<string> { "전체", "AURORA", "CMC", "RC", "MNGHA" };
            AdminStatusOptions = new ObservableCollection<string>
            {
                ImprovementStatusLabels.Open, ImprovementStatusLabels.Checking,
                ImprovementStatusLabels.InProgress, ImprovementStatusLabels.Resolved
            };
            Comments = new ObservableCollection<ImprovementCommentItemViewModel>();
            ContentBlocks = new ObservableCollection<ImprovementContentBlockViewModel>();
            PcOptions = new ObservableCollection<string>();
            PcGroups = new ObservableCollection<ImprovementPcGroupViewModel>();

            SaveCommand = new RelayCommand(() => { var _ = SaveAsync(); }, () => !IsBusy && IsNewRecord);
            DeleteCommand = new RelayCommand(() => { var _ = DeleteAsync(); }, () => !IsBusy && !IsNewRecord && CanManageRequest);
            BeginEditRequestCommand = new RelayCommand(() =>
            {
                IsEditingContent = true;
                // RichTextBox 문서는 읽기 전용 상태로 이미 구성돼 있어 기존 이미지에 삭제 버튼이
                // 없다 — 수정 모드 진입 시 다시 구성해야 기존 이미지에도 삭제 버튼이 붙는다.
                var reloaded = ContentReloaded; if (reloaded != null) reloaded();
            }, () => !IsBusy && !IsNewRecord && !IsEditingContent && CanManageRequest);
            CancelEditRequestCommand = new RelayCommand(() => { if (_loadedDto != null) LoadExisting(_loadedDto); }, () => !IsBusy && IsEditingContent);
            SaveEditRequestCommand = new RelayCommand(() => { var _ = SaveEditRequestAsync(); }, () => !IsBusy && IsEditingContent);
            CloseCommand = new RelayCommand(() => { var h = CloseRequested; if (h != null) h(); });
            ReactReproducedCommand = new RelayCommand(() => { var _ = ReactAsync(ImprovementReactionTypes.Reproduced); }, () => !IsBusy && !IsNewRecord);
            ReactNotReproducedCommand = new RelayCommand(() => { var _ = ReactAsync(ImprovementReactionTypes.NotReproduced); }, () => !IsBusy && !IsNewRecord);
            AddCommentCommand = new RelayCommand(() => { var _ = AddCommentAsync(); }, () => !IsBusy && !IsNewRecord && !string.IsNullOrWhiteSpace(NewCommentText));
            BeginEditCommentCommand = new RelayCommand<ImprovementCommentItemViewModel>(c => { if (c != null) c.IsEditing = true; });
            CancelEditCommentCommand = new RelayCommand<ImprovementCommentItemViewModel>(c => { if (c != null) { c.EditText = c.CommentText; c.IsEditing = false; } });
            SaveEditCommentCommand = new RelayCommand<ImprovementCommentItemViewModel>(c => { var _ = SaveCommentEditAsync(c); });
            DeleteCommentCommand = new RelayCommand<ImprovementCommentItemViewModel>(c => { var _ = DeleteCommentAsync(c); });
            // 답글도 댓글과 같은 입력창 하나를 같이 쓴다(따로 입력창을 안 만듦) — 답글 버튼은
            // "지금부터 이 댓글에 답글을 단다"는 대상만 지정하고, 등록은 AddCommentCommand가 담당.
            ToggleReplyCommand = new RelayCommand<ImprovementCommentItemViewModel>(c => ReplyTarget = ReferenceEquals(ReplyTarget, c) ? null : c);
            CancelReplyCommand = new RelayCommand(() => ReplyTarget = null);
            SaveAdminCommand = new RelayCommand(() => { var _ = SaveAdminAsync(); }, () => !IsBusy && !IsNewRecord && IsAdmin);
            // PC 선택 팝업의 열림/닫힘 자체는 일부러 이 ViewModel에 두지 않는다 — 이 다이얼로그의
            // 부모(ImprovementListViewModel)는 메인 탭과 관리자 화면에 동시에 임베드되어 재사용되는
            // 같은 인스턴스이고, 그 EditDialog도 두 화면에서 동시에 같은 View로 렌더링된다. 팝업
            // 열림 상태를 여기 두면 한쪽에서 열 때 안 보이는 다른 쪽 사본의 팝업도 같이 열린다 —
            // 그래서 View(코드비하인드)의 로컬 상태로 옮겼다.
            SelectPcCommand = new RelayCommand<string>(name => PcName = name);
        }

        public event Action CloseRequested;
        /// <summary>저장/삭제/반응 등 데이터 변경 후 부모가 목록을 새로고침하도록 알린다.</summary>
        public event Action DataChanged;
        /// <summary>
        /// LoadNew/LoadExisting으로 ContentBlocks가 통째로 다시 채워졌을 때 발생. 뷰(코드비하인드)는
        /// 이 이벤트를 받아 RichTextBox의 FlowDocument를 ContentBlocks 기준으로 새로 구성해야 한다.
        /// </summary>
        public event Action ContentReloaded;

        public void AttachPopup(IPopupService popup)
        {
            _popup = popup;
        }

        public bool IsAdmin
        {
            get { return OccupancyNameStore.IsAdmin; }
        }

        public bool CanManageRequest
        {
            get { return ImprovementOwnership.CanManage(ImprovementBiz.CurrentUserId, _authorUserId, IsAdmin); }
        }

        /// <summary>수정/삭제 아이콘 버튼 노출 — 편집 중에는 숨겨서 상태를 명확히 한다.</summary>
        public bool ShowRequestActions { get { return CanManageRequest && !IsEditingContent; } }

        public bool IsNewRecord
        {
            get { return _isNewRecord; }
            private set
            {
                if (SetProperty(ref _isNewRecord, value))
                {
                    RaisePropertyChanged("IsDetailMode");
                    RaisePropertyChanged("ShowReproductionCard");
                    RaisePropertyChanged("ReproductionFieldLabel");
                    RaisePropertyChanged("IsContentEditable");
                    RaisePropertyChanged("IsContentReadOnly");
                    RaisePropertyChanged("IsViewingDetail");
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool IsDetailMode { get { return !IsNewRecord; } }

        /// <summary>반응/댓글처럼 "내용을 다 갖춘 글을 보는" 화면 — 본문 수정 중에는 글쓰기에만
        /// 집중하도록 숨긴다.</summary>
        public bool IsViewingDetail { get { return IsDetailMode && !IsEditingContent; } }

        /// <summary>기존 요청 상세를 보다가 "수정" 버튼으로 진입하는 본문 편집 모드.</summary>
        public bool IsEditingContent
        {
            get { return _isEditingContent; }
            private set
            {
                if (SetProperty(ref _isEditingContent, value))
                {
                    RaisePropertyChanged("IsContentEditable");
                    RaisePropertyChanged("IsContentReadOnly");
                    RaisePropertyChanged("ShowRequestActions");
                    RaisePropertyChanged("ShowReproductionCard");
                    RaisePropertyChanged("ReproductionFieldLabel");
                    RaisePropertyChanged("IsViewingDetail");
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        /// <summary>제목/유형/사이트/PC/본문 입력창을 보여줄지 — 신규 작성 중이거나 기존 글 수정 중.</summary>
        public bool IsContentEditable { get { return IsNewRecord || IsEditingContent; } }
        public bool IsContentReadOnly { get { return !IsContentEditable; } }

        public string RequestType
        {
            get { return _requestType; }
            set { SetProperty(ref _requestType, value); }
        }

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public string TicketNoText
        {
            get { return _reqId > 0 ? "#" + _reqId : null; }
        }

        public string ReproductionSteps
        {
            get { return _reproductionSteps; }
            set
            {
                if (SetProperty(ref _reproductionSteps, value))
                {
                    RaisePropertyChanged("HasReproductionSteps");
                    RaisePropertyChanged("ShowReproductionCard");
                }
            }
        }

        /// <summary>재현 방법은 선택 항목 — 상세에서는 값이 있을 때만 카드 자체를 보여준다.</summary>
        public bool HasReproductionSteps { get { return !string.IsNullOrWhiteSpace(_reproductionSteps); } }
        public bool ShowReproductionCard { get { return IsContentEditable || HasReproductionSteps; } }
        /// <summary>작성/수정 중에만 "(선택)"을 붙인다 — 읽기 전용 상세에서는 이미 적힌 값을 보여줄 뿐이라 불필요.</summary>
        public string ReproductionFieldLabel { get { return IsContentEditable ? "재현 방법 (선택)" : "재현 방법"; } }

        public string SiteCode
        {
            get { return _siteCode; }
            set
            {
                if (SetProperty(ref _siteCode, value))
                {
                    RaisePropertyChanged("HasSiteCode");
                    var refreshTask = RefreshPcOptionsAsync(value);
                }
            }
        }

        public string PcName
        {
            get { return _pcName; }
            set
            {
                if (SetProperty(ref _pcName, value))
                    RaisePropertyChanged("HasPcName");
            }
        }

        public string AuthorName { get { return _authorName; } private set { SetProperty(ref _authorName, value); } }
        public bool HasDeletedAuthorSuffix { get { return _hasDeletedAuthorSuffix; } private set { SetProperty(ref _hasDeletedAuthorSuffix, value); } }
        public string TeamName
        {
            get { return _teamName; }
            private set
            {
                if (SetProperty(ref _teamName, value))
                    RaisePropertyChanged("HasTeamName");
            }
        }
        public string AppVersion
        {
            get { return _appVersion; }
            private set
            {
                if (SetProperty(ref _appVersion, value))
                    RaisePropertyChanged("HasAppVersion");
            }
        }

        /// <summary>상세 카드 메타 라인에서 값 없는 항목(및 구분선)을 숨기기 위한 보조 플래그.</summary>
        public bool HasTeamName { get { return !string.IsNullOrWhiteSpace(_teamName); } }
        public bool HasSiteCode { get { return !string.IsNullOrWhiteSpace(_siteCode) && !string.Equals(_siteCode, "전체", StringComparison.Ordinal); } }
        public bool HasPcName { get { return !string.IsNullOrWhiteSpace(_pcName); } }
        public bool HasAppVersion { get { return !string.IsNullOrWhiteSpace(_appVersion); } }
        public string CreatedAtText { get { return _createdAtText; } private set { SetProperty(ref _createdAtText, value); } }
        /// <summary>요청(게시글) 본문도 댓글과 동일하게 수정 이력을 보여준다 — 값이 있을 때만 표시.</summary>
        public string RequestEditedAtText
        {
            get { return _requestEditedAtText; }
            private set
            {
                if (SetProperty(ref _requestEditedAtText, value))
                    RaisePropertyChanged("HasRequestEdit");
            }
        }
        public bool HasRequestEdit { get { return !string.IsNullOrEmpty(_requestEditedAtText); } }
        public string StatusLabel { get { return _statusLabel; } private set { SetProperty(ref _statusLabel, value); } }
        public string ResolvedNoteText
        {
            get { return _resolvedNoteText; }
            private set
            {
                if (SetProperty(ref _resolvedNoteText, value))
                    RaisePropertyChanged("HasResolvedNote");
            }
        }

        public bool HasResolvedNote { get { return !string.IsNullOrWhiteSpace(_resolvedNoteText); } }

        public string ValidationError
        {
            get { return _validationError; }
            private set
            {
                if (SetProperty(ref _validationError, value))
                    RaisePropertyChanged("HasValidationError");
            }
        }

        public bool HasValidationError { get { return !string.IsNullOrWhiteSpace(_validationError); } }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public int ReproducedCount { get { return _reproducedCount; } private set { SetProperty(ref _reproducedCount, value); } }
        public int NotReproducedCount { get { return _notReproducedCount; } private set { SetProperty(ref _notReproducedCount, value); } }

        public string MyReaction
        {
            get { return _myReaction; }
            private set
            {
                if (SetProperty(ref _myReaction, value))
                {
                    RaisePropertyChanged("IsReproducedSelected");
                    RaisePropertyChanged("IsNotReproducedSelected");
                }
            }
        }

        public bool IsReproducedSelected { get { return string.Equals(MyReaction, ImprovementReactionTypes.Reproduced, StringComparison.OrdinalIgnoreCase); } }
        public bool IsNotReproducedSelected { get { return string.Equals(MyReaction, ImprovementReactionTypes.NotReproduced, StringComparison.OrdinalIgnoreCase); } }

        public bool IsBugType { get { return string.Equals(RequestType, ImprovementTypeLabels.Bug, StringComparison.Ordinal); } }

        /// <summary>
        /// 반응 버튼 문구는 유형에 따라 다르게 — "재현됨/안됨"은 버그가 아니면 어색하다.
        /// 버그: 같은 현상을 겪었는지, 기능개선/기타: 그 의견에 공감하는지로 프레이밍한다.
        /// </summary>
        public string ReactedLabel
        {
            get
            {
                return string.Equals(RequestType, ImprovementTypeLabels.Bug, StringComparison.Ordinal)
                    ? "저도 같은 문제예요"
                    : "저도 공감해요";
            }
        }

        public string NotReactedLabel
        {
            get
            {
                return string.Equals(RequestType, ImprovementTypeLabels.Bug, StringComparison.Ordinal)
                    ? "저는 문제없어요"
                    : "공감 안 돼요";
            }
        }

        public string NewCommentText
        {
            get { return _newCommentText; }
            set
            {
                if (SetProperty(ref _newCommentText, value))
                {
                    var cmd = AddCommentCommand as RelayCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public string AdminStatus
        {
            get { return _adminStatus; }
            set { SetProperty(ref _adminStatus, value); }
        }

        public string AdminResolvedVersion
        {
            get { return _adminResolvedVersion; }
            set { SetProperty(ref _adminResolvedVersion, value); }
        }

        public string AdminNote
        {
            get { return _adminNote; }
            set { SetProperty(ref _adminNote, value); }
        }

        public ObservableCollection<string> TypeOptions { get; private set; }
        public ObservableCollection<string> SiteOptions { get; private set; }
        /// <summary>선택된 사이트의 PC 목록(App.config/DB 기반, RemotePcBiz 재사용). 사이트 변경 시 자동 갱신.</summary>
        public ObservableCollection<string> PcOptions { get; private set; }
        /// <summary>
        /// 원격 PC 접속 화면과 동일하게 팀(GroupName)별로 묶은 PC 목록 — PC 선택 팝업에서 사용.
        /// PcOptions와 내용은 같고 표현 방식만 그룹핑되어 있다.
        /// </summary>
        public ObservableCollection<ImprovementPcGroupViewModel> PcGroups { get; private set; }
        public bool HasPcGroups { get { return PcGroups != null && PcGroups.Count > 0; } }
        public ObservableCollection<string> AdminStatusOptions { get; private set; }
        public ObservableCollection<ImprovementCommentItemViewModel> Comments { get; private set; }
        /// <summary>본문(설명)을 이루는 텍스트/이미지 단락. 순서가 곧 화면에 보이는 순서.</summary>
        public ObservableCollection<ImprovementContentBlockViewModel> ContentBlocks { get; private set; }

        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand BeginEditRequestCommand { get; private set; }
        public ICommand CancelEditRequestCommand { get; private set; }
        public ICommand SaveEditRequestCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }
        public ICommand ReactReproducedCommand { get; private set; }
        public ICommand ReactNotReproducedCommand { get; private set; }
        public ICommand AddCommentCommand { get; private set; }
        public ICommand BeginEditCommentCommand { get; private set; }
        public ICommand CancelEditCommentCommand { get; private set; }
        public ICommand SaveEditCommentCommand { get; private set; }
        public ICommand DeleteCommentCommand { get; private set; }
        public ICommand ToggleReplyCommand { get; private set; }
        public ICommand CancelReplyCommand { get; private set; }
        public ICommand SelectPcCommand { get; private set; }
        public ICommand SaveAdminCommand { get; private set; }

        private ImprovementCommentItemViewModel _replyTarget;

        /// <summary>지금 답글을 달고 있는 대상 댓글(없으면 null = 일반 댓글 작성).</summary>
        public ImprovementCommentItemViewModel ReplyTarget
        {
            get { return _replyTarget; }
            set
            {
                var previous = _replyTarget;
                if (SetProperty(ref _replyTarget, value))
                {
                    // 어떤 댓글에 답글을 다는지 목록에서 배경으로 알 수 있게 표시한다.
                    if (previous != null)
                        previous.IsReplyTarget = false;
                    if (_replyTarget != null)
                        _replyTarget.IsReplyTarget = true;
                    RaisePropertyChanged("IsReplyingToComment");
                    RaisePropertyChanged("ReplyTargetAuthorName");
                }
            }
        }

        public bool IsReplyingToComment { get { return _replyTarget != null; } }
        public string ReplyTargetAuthorName { get { return _replyTarget != null ? _replyTarget.AuthorName : null; } }

        /// <summary>댓글 입력창 좌측 아바타용 — 지금 로그인한 사용자(작성자가 아니라 댓글 다는 사람).</summary>
        public string CurrentUserDisplayName
        {
            get
            {
                string name = OccupancyNameStore.HeaderName;
                return string.IsNullOrWhiteSpace(name) ? "나" : name;
            }
        }

        public string CurrentUserInitial
        {
            get
            {
                string name = CurrentUserDisplayName;
                return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
            }
        }

        /// <summary>새 요청 작성 모드로 초기화.</summary>
        public void LoadNew()
        {
            IsNewRecord = true;
            IsEditingContent = false;
            _loadedDto = null;
            _reqId = 0;
            RaisePropertyChanged("TicketNoText");
            _authorUserId = 0;
            RequestType = ImprovementTypeLabels.Bug;
            Title = string.Empty;
            ReproductionSteps = string.Empty;
            PcName = string.Empty;
            SiteCode = "전체";
            AuthorName = OccupancyNameStore.HeaderName;
            HasDeletedAuthorSuffix = false;
            TeamName = OccupancyNameStore.TryGetAffiliation();
            AppVersion = AppVersion_Current();
            CreatedAtText = null;
            RequestEditedAtText = null;
            StatusLabel = null;
            ResolvedNoteText = null;
            ValidationError = null;
            Comments.Clear();
            ContentBlocks.Clear();
            ContentBlocks.Add(new ImprovementContentBlockViewModel(string.Empty));

            var reloaded = ContentReloaded; if (reloaded != null) reloaded();
        }

        /// <summary>기존 요청 상세 로드. 이미 GetByIdAsync로 조회된 전체 DTO를 받는다.</summary>
        public void LoadExisting(ImprovementRequestDto dto)
        {
            IsNewRecord = false;
            IsEditingContent = false;
            _loadedDto = dto;
            _reqId = dto.ReqId;
            RaisePropertyChanged("TicketNoText");
            _authorUserId = dto.UserId;
            RequestType = ImprovementTypeLabels.ToLabel(dto.RequestType);
            Title = dto.Title;
            ReproductionSteps = dto.ReproductionSteps;
            SiteCode = dto.SiteCode;
            PcName = dto.PcName;
            AuthorName = dto.AuthorName;
            HasDeletedAuthorSuffix = dto.IsAuthorDeleted;
            TeamName = dto.TeamName;
            AppVersion = dto.AppVersion;
            CreatedAtText = dto.CreatedAt.HasValue ? dto.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "-";
            RequestEditedAtText = (dto.UpdatedAt.HasValue && dto.CreatedAt.HasValue && dto.UpdatedAt.Value > dto.CreatedAt.Value)
                ? "(수정됨)"
                : null;
            StatusLabel = ImprovementStatusLabels.ToLabel(dto.Status);
            ResolvedNoteText = string.Equals(dto.Status, ImprovementStatuses.Resolved, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(dto.ResolvedVersion)
                ? "v" + dto.ResolvedVersion + "에서 해결"
                : null;
            ReproducedCount = dto.ReproducedCount;
            NotReproducedCount = dto.NotReproducedCount;
            MyReaction = dto.MyReaction;
            ValidationError = null;

            AdminStatus = ImprovementStatusLabels.ToLabel(dto.Status);
            AdminResolvedVersion = dto.ResolvedVersion ?? string.Empty;
            AdminNote = dto.AdminNote ?? string.Empty;

            Comments.Clear();
            ContentBlocks.Clear();

            var allAttachments = dto.Attachments ?? new List<ImprovementAttachmentDto>();
            foreach (var block in ParseContentBlocks(dto.Description, allAttachments))
                ContentBlocks.Add(block);

            RaisePropertyChanged("CanManageRequest");
            RaisePropertyChanged("ShowRequestActions");
            RaiseAllCanExecuteChanged();

            var reloaded = ContentReloaded; if (reloaded != null) reloaded();

            var _ = ReloadCommentsAsync();
        }

        /// <summary>
        /// 등록 버튼을 누르기 직전, 코드비하인드가 RichTextBox(FlowDocument)를 순회해서 읽어낸
        /// 최종 순서를 ContentBlocks에 반영한다. 편집 중에는 FlowDocument가 진짜 소스이고
        /// ContentBlocks는 로드/저장 시점에만 동기화되는 캐시다.
        /// </summary>
        public void SetContentBlocks(IEnumerable<ImprovementContentBlockViewModel> blocks)
        {
            ContentBlocks.Clear();
            foreach (var block in blocks)
                ContentBlocks.Add(block);
        }

        /// <summary>
        /// "이미지 삽입"(파일 선택/클립보드 붙여넣기 공통) 시 이미 메모리에 올라온 바이트로 블록을
        /// 만든다. 실패하면 팝업으로 안내하고 null. 개수 상한은 서버 저장 전(작성 중)에도 바로 알 수
        /// 있도록 여기서 먼저 검사한다. 용량 제한에 맞춘 인코딩/축소는 코드비하인드가 이 메서드를
        /// 부르기 전에 이미 끝낸 상태여야 한다(여기서는 개수 상한/확장자/용량만 검증).
        /// </summary>
        public ImprovementContentBlockViewModel TryStagePastedImageBlock(byte[] bytes, string fileName, int currentImageCount)
        {
            if (currentImageCount >= ImprovementBiz.MaxAttachmentsPerRequest)
            {
                ShowAttachmentError(fileName, "요청 하나에는 이미지를 최대 " + ImprovementBiz.MaxAttachmentsPerRequest + "장까지 첨부할 수 있습니다.");
                return null;
            }

            string error = _biz.ValidateAttachment(fileName, bytes);
            if (error != null)
            {
                ShowAttachmentError(fileName, error);
                return null;
            }

            return new ImprovementContentBlockViewModel(bytes, fileName);
        }

        private void ShowAttachmentError(string fileName, string message)
        {
            if (_popup == null)
                return;
            var _ = _popup.ShowResultAsync(new PopupRequest { Title = "첨부 실패", Message = fileName + ": " + message, Icon = PopupIconKind.Error });
        }

        /// <summary>
        /// 설명 텍스트 안의 {{img:&lt;토큰&gt;}} 마커를 기준으로, 텍스트/이미지 블록을 작성했던 순서
        /// 그대로 복원한다. 토큰이 어떤 첨부의 ContentKey와 일치하면 그 첨부를 쓴다(삭제/재배치에
        /// 영향받지 않는 안정 참조). 토큰이 숫자이고 일치하는 키가 없으면, 이번 기능 도입 전 세션에서
        /// 저장된 옛 순번 마커(레거시)로 보고 1-based 생성 순번으로 첨부를 찾는다.
        /// 마커가 아예 없는 옛 데이터는 통째로 텍스트 블록 하나가 된다.
        /// </summary>
        private static IEnumerable<ImprovementContentBlockViewModel> ParseContentBlocks(
            string description, IList<ImprovementAttachmentDto> attachments)
        {
            var result = new List<ImprovementContentBlockViewModel>();
            var consumedIndexes = new HashSet<int>();

            if (string.IsNullOrEmpty(description))
            {
                result.Add(new ImprovementContentBlockViewModel(string.Empty));
                return result;
            }

            int lastIndex = 0;
            foreach (Match m in ImageTokenRegex.Matches(description))
            {
                string textSegment = description.Substring(lastIndex, m.Index - lastIndex).Trim();
                if (textSegment.Length > 0)
                    result.Add(new ImprovementContentBlockViewModel(textSegment));

                int attachIndex = ResolveAttachmentIndex(m.Groups[1].Value, attachments, consumedIndexes);
                if (attachIndex >= 0)
                {
                    result.Add(new ImprovementContentBlockViewModel(attachments[attachIndex]));
                    consumedIndexes.Add(attachIndex);
                }
                lastIndex = m.Index + m.Length;
            }

            string tail = description.Substring(lastIndex).Trim();
            if (tail.Length > 0)
                result.Add(new ImprovementContentBlockViewModel(tail));

            if (result.Count == 0)
                result.Add(new ImprovementContentBlockViewModel(string.Empty));

            EnsureTextBlocksAroundImages(result);
            return result;
        }

        /// <summary>
        /// 이미지 앞뒤에 빈 문단이 하나도 없으면(연속된 이미지 사이, 문서 맨 앞/뒤) 저장 시 빈
        /// 텍스트 블록이 잘려나가 다시 불러왔을 때 이미지끼리 바로 붙어버린다 — 클릭해서 타이핑할
        /// 자리가 안 보이는 원인이라, 로드 시점에 항상 빈 문단을 다시 채워 넣는다.
        /// </summary>
        private static void EnsureTextBlocksAroundImages(List<ImprovementContentBlockViewModel> blocks)
        {
            if (blocks.Count == 0)
                return;

            if (blocks[0].IsImage)
                blocks.Insert(0, new ImprovementContentBlockViewModel(string.Empty));

            for (int i = 1; i < blocks.Count; i++)
            {
                if (blocks[i - 1].IsImage && blocks[i].IsImage)
                    blocks.Insert(i, new ImprovementContentBlockViewModel(string.Empty));
            }

            if (blocks[blocks.Count - 1].IsImage)
                blocks.Add(new ImprovementContentBlockViewModel(string.Empty));
        }

        private static int ResolveAttachmentIndex(string token, IList<ImprovementAttachmentDto> attachments, ISet<int> consumedIndexes)
        {
            for (int i = 0; i < attachments.Count; i++)
            {
                if (consumedIndexes.Contains(i))
                    continue;
                if (!string.IsNullOrEmpty(attachments[i].ContentKey) && string.Equals(attachments[i].ContentKey, token, StringComparison.Ordinal))
                    return i;
            }

            int n;
            if (int.TryParse(token, out n) && n >= 1 && n <= attachments.Count && !consumedIndexes.Contains(n - 1))
                return n - 1;

            return -1;
        }

        /// <summary>
        /// 작성 중인 블록 순서를 설명 텍스트로 직렬화한다. 이미지 블록은 블록 생성 시 이미 부여된
        /// 고유 키를 {{img:&lt;키&gt;}} 토큰으로 남기고, 실제 바이트는 imageBlocksInOrder로 반환한다.
        /// </summary>
        private string SerializeBlocksToDescription(out List<ImprovementContentBlockViewModel> imageBlocksInOrder)
        {
            var sb = new StringBuilder();
            var images = new List<ImprovementContentBlockViewModel>();
            foreach (var block in ContentBlocks)
            {
                if (block.IsText)
                {
                    sb.Append(block.Text ?? string.Empty);
                }
                else
                {
                    images.Add(block);
                    sb.Append("{{img:").Append(block.ContentKey).Append("}}");
                }
                sb.Append("\n\n");
            }
            imageBlocksInOrder = images;
            return sb.ToString().Trim();
        }

        private static string AppVersion_Current()
        {
            try { return GBCWorkHub.UI.Services.Update.AppVersion.Current; }
            catch { return null; }
        }

        private int _pcOptionsGeneration;

        /// <summary>
        /// 사이트 선택 시 해당 사이트의 PC 목록을 자동으로 채운다. 원격 PC 화면/관리자 화면과
        /// 동일하게 RemotePcBiz.GetRemotePcListBySite(App.config/DB 기반)를 재사용한다.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshPcOptionsAsync(string siteCode)
        {
            int gen = ++_pcOptionsGeneration;
            if (string.IsNullOrWhiteSpace(siteCode) || string.Equals(siteCode, "전체", StringComparison.Ordinal))
            {
                PcOptions.Clear();
                PcGroups.Clear();
                RaisePropertyChanged("HasPcGroups");
                return;
            }

            System.Collections.Generic.List<GBCWorkHub.DTO.RemotePcDto> list;
            try
            {
                list = await System.Threading.Tasks.Task.Run(() => new RemotePcBiz().GetRemotePcListBySite(siteCode))
                    .ConfigureAwait(true);
            }
            catch
            {
                list = null;
            }

            if (gen != _pcOptionsGeneration)
                return; // 그 사이 사용자가 다른 사이트를 다시 선택함 — 오래된 응답은 버림

            PcOptions.Clear();
            PcGroups.Clear();
            if (list != null)
            {
                // 원격 PC 접속 화면(RemoteWorkspaceViewModel/RemotePcGalleryComparer)과 완전히 동일한
                // 정렬 기준(PcNameNaturalSort)을 그대로 재사용한다 — 진료지원/진료간호/원무/배포서버
                // 같은 한글 소속팀이 ETC/미지정보다 항상 먼저 오도록 이미 정해져 있는 규칙이라,
                // 여기서 다시 임의로(가나다순 등) 정렬하면 그 규칙과 어긋난다.
                var byTeam = new Dictionary<string, List<ImprovementPcOptionViewModel>>(StringComparer.Ordinal);
                var teamOrder = new List<string>();
                foreach (var pc in list)
                {
                    if (pc == null || string.IsNullOrWhiteSpace(pc.PcName))
                        continue;
                    if (!PcOptions.Contains(pc.PcName))
                        PcOptions.Add(pc.PcName);

                    string rawTeam = pc.GroupName ?? string.Empty;
                    List<ImprovementPcOptionViewModel> entries;
                    if (!byTeam.TryGetValue(rawTeam, out entries))
                    {
                        entries = new List<ImprovementPcOptionViewModel>();
                        byTeam[rawTeam] = entries;
                        teamOrder.Add(rawTeam);
                    }
                    if (!entries.Any(e => string.Equals(e.PcName, pc.PcName, StringComparison.Ordinal)))
                        entries.Add(new ImprovementPcOptionViewModel(pc.PcName, pc.PcDomain));
                }

                teamOrder.Sort((a, b) => GBCWorkHub.DTO.PcNameNaturalSort.CompareGroup(a, b));
                foreach (var rawTeam in teamOrder)
                {
                    var entries = byTeam[rawTeam];
                    entries.Sort((a, b) => GBCWorkHub.DTO.PcNameNaturalSort.Compare(a.PcName, b.PcName));
                    string displayName = string.IsNullOrWhiteSpace(rawTeam) ? "기타" : rawTeam;
                    PcGroups.Add(new ImprovementPcGroupViewModel(displayName, entries));
                }
            }

            RaisePropertyChanged("HasPcGroups");
        }

        /// <summary>
        /// 서버에서는 REQ_ID 기준 평면 목록으로 오므로(작성 시각순), 여기서 PARENT_COMMENT_ID를 보고
        /// 최상위 댓글 밑에 답글을 묶어 트리로 재구성한다. 답글의 답글은 만들지 않으므로 1단계만 처리.
        /// </summary>
        private async System.Threading.Tasks.Task ReloadCommentsAsync()
        {
            var list = await _biz.GetCommentsAsync(_reqId).ConfigureAwait(true);
            Comments.Clear();
            if (list == null)
                return;

            var byId = new Dictionary<long, ImprovementCommentItemViewModel>();
            foreach (var c in list)
                byId[c.CommentId] = new ImprovementCommentItemViewModel(c);

            foreach (var c in list)
            {
                var item = byId[c.CommentId];
                ImprovementCommentItemViewModel parent;
                if (c.ParentCommentId.HasValue && byId.TryGetValue(c.ParentCommentId.Value, out parent))
                    parent.Replies.Add(item);
                else
                    Comments.Add(item);
            }
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            List<ImprovementContentBlockViewModel> imageBlocks;
            string descriptionText = SerializeBlocksToDescription(out imageBlocks);

            var draft = new ImprovementRequestDto
            {
                RequestType = ImprovementTypeCodeFromLabel(RequestType),
                Title = Title,
                Description = descriptionText,
                ReproductionSteps = string.IsNullOrWhiteSpace(ReproductionSteps) ? null : ReproductionSteps,
                SiteCode = string.Equals(SiteCode, "전체", StringComparison.Ordinal) ? null : SiteCode,
                PcName = string.IsNullOrWhiteSpace(PcName) ? null : PcName
            };

            IsBusy = true;
            try
            {
                string error = await _biz.CreateAsync(draft, AppVersion_Current()).ConfigureAwait(true);
                if (error != null)
                {
                    ValidationError = error;
                    return;
                }
                ValidationError = null;

                // 본문에 배치했던 이미지 블록을 쓴 순서 그대로 업로드 — {{img:N}}의 N과 첨부 생성 순서가
                // 일치해야 상세 조회 시 같은 자리에 그대로 복원된다.
                foreach (var imageBlock in imageBlocks)
                {
                    string attachError = await _biz.AddAttachmentAsync(draft.ReqId, imageBlock.FileName, imageBlock.ImageData, imageBlock.ContentKey).ConfigureAwait(true);
                    if (attachError != null && _popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "이미지 첨부 실패", Message = imageBlock.FileName + ": " + attachError, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                }

                var changed = DataChanged; if (changed != null) changed();

                // 등록 직후 닫지 않고 상세 모드로 전환 — 바로 이어서 사진 첨부/댓글을 할 수 있게 한다.
                var created = await _biz.GetByIdAsync(draft.ReqId).ConfigureAwait(true);
                if (created != null)
                    LoadExisting(created);
                else
                {
                    var close = CloseRequested; if (close != null) close();
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>기존 요청의 본문(제목/유형/사이트/PC/설명/재현방법) 수정 저장. 새 이미지만 업로드하고,
        /// 이미 저장된 첨부는 SerializeBlocksToDescription이 그대로 토큰만 재사용하므로 재업로드하지 않는다.</summary>
        private async System.Threading.Tasks.Task SaveEditRequestAsync()
        {
            List<ImprovementContentBlockViewModel> imageBlocks;
            string descriptionText = SerializeBlocksToDescription(out imageBlocks);

            var edited = new ImprovementRequestDto
            {
                ReqId = _reqId,
                UserId = _authorUserId,
                RequestType = ImprovementTypeCodeFromLabel(RequestType),
                Title = Title,
                Description = descriptionText,
                ReproductionSteps = string.IsNullOrWhiteSpace(ReproductionSteps) ? null : ReproductionSteps,
                SiteCode = string.Equals(SiteCode, "전체", StringComparison.Ordinal) ? null : SiteCode,
                PcName = string.IsNullOrWhiteSpace(PcName) ? null : PcName
            };

            IsBusy = true;
            try
            {
                string error = await _biz.UpdateAsync(edited).ConfigureAwait(true);
                if (error != null)
                {
                    ValidationError = error;
                    return;
                }
                ValidationError = null;

                foreach (var imageBlock in imageBlocks)
                {
                    if (imageBlock.IsExistingAttachment)
                        continue;
                    string attachError = await _biz.AddAttachmentAsync(_reqId, imageBlock.FileName, imageBlock.ImageData, imageBlock.ContentKey).ConfigureAwait(true);
                    if (attachError != null && _popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "이미지 첨부 실패", Message = imageBlock.FileName + ": " + attachError, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                }

                var changed = DataChanged; if (changed != null) changed();

                var reloaded = await _biz.GetByIdAsync(_reqId).ConfigureAwait(true);
                if (reloaded != null)
                    LoadExisting(reloaded);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task DeleteAsync()
        {
            if (_popup != null)
            {
                var confirm = await _popup.ShowConfirmAsync(new PopupRequest
                {
                    Title = "요청 삭제",
                    Message = "이 요청을 삭제할까요? 댓글/반응/첨부가 함께 삭제됩니다.",
                    Icon = PopupIconKind.Warning
                }).ConfigureAwait(true);
                if (confirm == null || !confirm.IsPrimary)
                    return;
            }

            IsBusy = true;
            try
            {
                string error = await _biz.DeleteAsync(new ImprovementRequestDto { ReqId = _reqId, UserId = _authorUserId }).ConfigureAwait(true);
                if (error != null)
                {
                    if (_popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "삭제 실패", Message = error, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                    return;
                }
                var changed = DataChanged; if (changed != null) changed();
                var close = CloseRequested; if (close != null) close();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task ReactAsync(string reactionType)
        {
            IsBusy = true;
            try
            {
                string error = await _biz.SetReactionAsync(_reqId, reactionType).ConfigureAwait(true);
                if (error != null)
                {
                    if (_popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "반응 실패", Message = error, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                    return;
                }
                MyReaction = reactionType;
                var dto = await _biz.GetByIdAsync(_reqId).ConfigureAwait(true);
                if (dto != null)
                {
                    ReproducedCount = dto.ReproducedCount;
                    NotReproducedCount = dto.NotReproducedCount;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 댓글과 답글이 입력창 하나를 같이 쓴다 — ReplyTarget이 있으면 그 댓글에 대한 답글로,
        /// 없으면 최상위 댓글로 등록한다.
        /// </summary>
        private async System.Threading.Tasks.Task AddCommentAsync()
        {
            string text = NewCommentText;
            long? parentCommentId = ReplyTarget != null ? (long?)ReplyTarget.CommentId : null;
            IsBusy = true;
            try
            {
                string error = await _biz.AddCommentAsync(_reqId, text, AppVersion_Current(), parentCommentId).ConfigureAwait(true);
                if (error != null)
                {
                    if (_popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "댓글 실패", Message = error, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                    return;
                }
                NewCommentText = string.Empty;
                ReplyTarget = null;
                await ReloadCommentsAsync().ConfigureAwait(true);
                var changed = DataChanged; if (changed != null) changed();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task SaveCommentEditAsync(ImprovementCommentItemViewModel item)
        {
            if (item == null)
                return;
            IsBusy = true;
            try
            {
                string error = await _biz.UpdateCommentAsync(item.Dto, item.EditText).ConfigureAwait(true);
                if (error != null)
                {
                    if (_popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "댓글 수정 실패", Message = error, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                    return;
                }
                item.ApplySavedText(item.EditText);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task DeleteCommentAsync(ImprovementCommentItemViewModel item)
        {
            if (item == null)
                return;
            IsBusy = true;
            try
            {
                string error = await _biz.DeleteCommentAsync(item.Dto).ConfigureAwait(true);
                if (error != null)
                {
                    if (_popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "댓글 삭제 실패", Message = error, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                    return;
                }
                // 목록에서 아예 빼지 않는다 — 답글이 달려 있으면 그 답글들까지 같이 사라져 버린다.
                item.ApplyDeleted();
                if (ReferenceEquals(ReplyTarget, item))
                    ReplyTarget = null;
                var changed = DataChanged; if (changed != null) changed();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async System.Threading.Tasks.Task SaveAdminAsync()
        {
            IsBusy = true;
            try
            {
                string statusCode = ImprovementStatusCodeFromLabel(AdminStatus);
                string error = await _biz.UpdateAdminFieldsAsync(_reqId, statusCode, AdminResolvedVersion, AdminNote).ConfigureAwait(true);
                if (error != null)
                {
                    if (_popup != null)
                        await _popup.ShowResultAsync(new PopupRequest { Title = "저장 실패", Message = error, Icon = PopupIconKind.Error }).ConfigureAwait(true);
                    return;
                }
                StatusLabel = AdminStatus;
                ResolvedNoteText = string.Equals(statusCode, ImprovementStatuses.Resolved, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(AdminResolvedVersion)
                    ? "v" + AdminResolvedVersion + "에서 해결"
                    : null;
                RequestEditedAtText = "(수정됨)";
                var changed = DataChanged; if (changed != null) changed();

                if (_popup != null)
                    await _popup.ShowResultAsync(new PopupRequest { Title = "저장 완료", Message = "처리 내용이 저장되었습니다.", Icon = PopupIconKind.Success }).ConfigureAwait(true);

                var close = CloseRequested; if (close != null) close();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string ImprovementTypeCodeFromLabel(string label)
        {
            if (string.Equals(label, ImprovementTypeLabels.Bug, StringComparison.Ordinal)) return ImprovementRequestTypes.Bug;
            if (string.Equals(label, ImprovementTypeLabels.Improvement, StringComparison.Ordinal)) return ImprovementRequestTypes.Improvement;
            if (string.Equals(label, ImprovementTypeLabels.Etc, StringComparison.Ordinal)) return ImprovementRequestTypes.Etc;
            return ImprovementRequestTypes.Bug;
        }

        private static string ImprovementStatusCodeFromLabel(string label)
        {
            if (string.Equals(label, ImprovementStatusLabels.Open, StringComparison.Ordinal)) return ImprovementStatuses.Open;
            if (string.Equals(label, ImprovementStatusLabels.Checking, StringComparison.Ordinal)) return ImprovementStatuses.Checking;
            if (string.Equals(label, ImprovementStatusLabels.InProgress, StringComparison.Ordinal)) return ImprovementStatuses.InProgress;
            if (string.Equals(label, ImprovementStatusLabels.Resolved, StringComparison.Ordinal)) return ImprovementStatuses.Resolved;
            return ImprovementStatuses.Open;
        }

        private void RaiseAllCanExecuteChanged()
        {
            foreach (var cmd in new[]
            {
                SaveCommand, DeleteCommand, ReactReproducedCommand, ReactNotReproducedCommand,
                AddCommentCommand, SaveAdminCommand,
                BeginEditRequestCommand, CancelEditRequestCommand, SaveEditRequestCommand
            })
            {
                var relay = cmd as RelayCommand;
                if (relay != null)
                    relay.RaiseCanExecuteChanged();
            }
        }
    }
}
