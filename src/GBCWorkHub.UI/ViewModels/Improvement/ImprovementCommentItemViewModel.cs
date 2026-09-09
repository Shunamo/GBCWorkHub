using System;
using System.Collections.ObjectModel;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.Improvement;
using GBCWorkHub.DTO.Improvement;

namespace GBCWorkHub.UI.ViewModels.Improvement
{
    public sealed class ImprovementCommentItemViewModel : ViewModelBase
    {
        private bool _isEditing;
        private string _editText;
        private bool _isReplyTarget;

        public ImprovementCommentDto Dto { get; private set; }

        /// <summary>이 댓글에 달린 답글(대댓글). 1단계만 지원 — 답글에는 답글을 달지 않는다.</summary>
        public ObservableCollection<ImprovementCommentItemViewModel> Replies { get; private set; }

        public ImprovementCommentItemViewModel(ImprovementCommentDto dto)
        {
            Dto = dto ?? new ImprovementCommentDto();
            _editText = Dto.CommentText;
            Replies = new ObservableCollection<ImprovementCommentItemViewModel>();
        }

        public long CommentId { get { return Dto.CommentId; } }
        public long? ParentCommentId { get { return Dto.ParentCommentId; } }
        public bool IsReply { get { return Dto.ParentCommentId.HasValue; } }
        public string AuthorName { get { return Dto.AuthorName; } }
        public bool HasDeletedAuthorSuffix { get { return Dto.IsAuthorDeleted; } }
        public string TeamName { get { return Dto.TeamName; } }
        public bool HasTeamName { get { return !string.IsNullOrWhiteSpace(Dto.TeamName); } }
        public string CommentText { get { return Dto.CommentText; } }
        public string AppVersion { get { return Dto.AppVersion; } }

        public string CreatedAtText
        {
            get { return Dto.CreatedAt.HasValue ? Dto.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "-"; }
        }

        /// <summary>INSERT 시 CREATED_AT/UPDATED_AT을 같은 SYSTIMESTAMP로 채우므로, 수정 UPDATE만
        /// UPDATED_AT을 앞으로 민다 — 그래서 단순 비교로 "수정됨" 여부를 판단해도 안전하다.</summary>
        public bool IsEdited
        {
            get { return Dto.UpdatedAt.HasValue && Dto.CreatedAt.HasValue && Dto.UpdatedAt.Value > Dto.CreatedAt.Value; }
        }

        public string EditedAtText { get { return "(수정됨)"; } }

        /// <summary>실제 DELETE 대신 소프트 삭제 — 답글이 달려 있을 수 있어 행 자체는 남긴다.</summary>
        public bool IsDeleted { get { return Dto.IsDeleted; } }

        /// <summary>본인 댓글 또는 관리자만 true — 이름 비교가 아니라 세션 USER_ID 비교.</summary>
        public bool CanManage
        {
            get { return !IsDeleted && ImprovementOwnership.CanManage(ImprovementBiz.CurrentUserId, Dto.UserId, OccupancyNameStore.IsAdmin); }
        }

        public bool IsEditing
        {
            get { return _isEditing; }
            set { SetProperty(ref _isEditing, value); }
        }

        public string EditText
        {
            get { return _editText; }
            set { SetProperty(ref _editText, value); }
        }

        /// <summary>지금 이 댓글에 답글을 다는 중인지 — 목록에서 배경을 옅게 칠해 어떤 댓글에
        /// 답글을 다는지 알려주는 데 쓴다. ImprovementEditDialogViewModel.ReplyTarget이 관리.</summary>
        public bool IsReplyTarget
        {
            get { return _isReplyTarget; }
            set { SetProperty(ref _isReplyTarget, value); }
        }

        public void ApplySavedText(string text)
        {
            Dto.CommentText = text;
            Dto.UpdatedAt = DateTime.Now;
            RaisePropertyChanged("CommentText");
            RaisePropertyChanged("IsEdited");
            RaisePropertyChanged("EditedAtText");
            IsEditing = false;
        }

        /// <summary>
        /// 실제로 목록에서 제거하지 않는다 — 답글이 달려 있으면 그 답글들이 같이 사라져 버리기
        /// 때문. 대신 소프트 삭제 표시만 하고 자리는 그대로 남겨서 밑의 답글이 계속 보이게 한다.
        /// </summary>
        public void ApplyDeleted()
        {
            Dto.IsDeleted = true;
            RaisePropertyChanged("IsDeleted");
            RaisePropertyChanged("CanManage");
        }
    }
}
