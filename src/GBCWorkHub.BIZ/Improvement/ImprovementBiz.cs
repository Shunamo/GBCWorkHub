using System;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO.Improvement;

namespace GBCWorkHub.BIZ.Improvement
{
    /// <summary>
    /// 개선사항 요청 업무 로직. 작성자/댓글/반응은 항상 세션 USER_ID(DirectoryBiz.ResolveCurrentUserId)로
    /// 스탬프하며, 이름/PC로 재해석하지 않는다. 소유권 판정은 ImprovementOwnership을 통해서만 한다.
    /// AppVersion은 UI 계층(GBCWorkHub.UI.Services.Update.AppVersion.Current)에서 읽어 파라미터로 전달받는다
    /// — BIZ는 UI를 참조할 수 없으므로 이 계층에서 직접 조회하지 않는다.
    /// </summary>
    public sealed class ImprovementBiz
    {
        private readonly IImprovementRepository _repository;

        public ImprovementBiz()
            : this(new OracleImprovementRepository())
        {
        }

        public ImprovementBiz(IImprovementRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException("repository");
        }

        public bool IsConfigured
        {
            get { return _repository.IsConfigured; }
        }

        public static long? CurrentUserId
        {
            get { return DirectoryBiz.ResolveCurrentUserId(); }
        }

        /// <summary>
        /// 저장/삭제 실패 시 사용자에게 덧붙이는 안내. 가장 흔한 원인(sql/24, sql/26~29 미적용으로
        /// 테이블/컬럼이 아직 없는 경우)을 바로 알 수 있도록 한다 — 상세 원인은
        /// WorkHubFileLogger 로그에 남는다.
        /// </summary>
        private string FailureHint()
        {
            return _repository.IsConfigured
                ? " (DB 테이블/컬럼이 아직 반영되지 않았을 수 있습니다 — 관리자에게 sql/23·24·26·27·28·29 적용 여부를 확인해 주세요.)"
                : " (DB 연결이 설정되어 있지 않습니다 — 관리자에게 문의해 주세요.)";
        }

        public Task<ImprovementPageResult> GetPageAsync(ImprovementListQuery query)
        {
            return _repository.GetPageAsync(query, CurrentUserId);
        }

        public Task<ImprovementRequestDto> GetByIdAsync(long reqId)
        {
            return _repository.GetByIdAsync(reqId, CurrentUserId);
        }

        /// <summary>
        /// 요청 등록. 작성자/소속/생성시각은 세션 값으로 강제 스탬프한다(클라이언트가 준 값 신뢰 안 함).
        /// </summary>
        public async Task<string> CreateAsync(ImprovementRequestDto draft, string appVersion)
        {
            if (draft == null)
                return "요청 내용이 없습니다.";
            if (string.IsNullOrWhiteSpace(draft.RequestType))
                return "유형을 선택해 주세요.";
            if (string.IsNullOrWhiteSpace(draft.Title))
                return "제목을 입력해 주세요.";
            if (string.IsNullOrWhiteSpace(draft.Description))
                return "설명을 입력해 주세요.";

            long? userId = CurrentUserId;
            if (!userId.HasValue)
                return "로그인이 필요합니다.";

            draft.ReqId = 0;
            draft.UserId = userId.Value;
            draft.AuthorName = OccupancyNameStore.HeaderName;
            draft.TeamName = OccupancyNameStore.TryGetAffiliation();
            draft.AppVersion = appVersion;
            draft.Status = ImprovementStatuses.Open;

            bool ok = await _repository.SaveAsync(draft).ConfigureAwait(false);
            return ok ? null : "저장에 실패했습니다." + FailureHint();
        }

        /// <summary>본인 글만 내용 수정(제목/설명/재현방법/사이트/PC). 관리자 전용 필드는 별도 메서드로.</summary>
        public async Task<string> UpdateAsync(ImprovementRequestDto edited)
        {
            if (edited == null || edited.ReqId <= 0)
                return "요청을 찾을 수 없습니다.";
            if (string.IsNullOrWhiteSpace(edited.Title))
                return "제목을 입력해 주세요.";
            if (string.IsNullOrWhiteSpace(edited.Description))
                return "설명을 입력해 주세요.";

            long? userId = CurrentUserId;
            if (!ImprovementOwnership.CanManage(userId, edited.UserId, OccupancyNameStore.IsAdmin))
                return "본인이 작성한 요청만 수정할 수 있습니다.";

            bool ok = await _repository.SaveAsync(edited).ConfigureAwait(false);
            return ok ? null : "저장에 실패했습니다." + FailureHint();
        }

        /// <summary>본인 글 또는 관리자만 삭제 가능.</summary>
        public async Task<string> DeleteAsync(ImprovementRequestDto request)
        {
            if (request == null || request.ReqId <= 0)
                return "요청을 찾을 수 없습니다.";

            long? userId = CurrentUserId;
            if (!ImprovementOwnership.CanManage(userId, request.UserId, OccupancyNameStore.IsAdmin))
                return "본인이 작성한 요청이거나 관리자만 삭제할 수 있습니다.";

            bool ok = await _repository.DeleteByIdAsync(request.ReqId).ConfigureAwait(false);
            return ok ? null : "삭제에 실패했습니다." + FailureHint();
        }

        /// <summary>관리자 전용: 상태/해결버전/관리자 메모 갱신.</summary>
        public Task<string> UpdateAdminFieldsAsync(long reqId, string status, string resolvedVersion, string adminNote)
        {
            if (!OccupancyNameStore.IsAdmin)
                return Task.FromResult("관리자만 사용할 수 있습니다.");
            if (reqId <= 0 || string.IsNullOrWhiteSpace(status))
                return Task.FromResult("상태를 선택해 주세요.");

            return UpdateAdminFieldsCoreAsync(reqId, status, resolvedVersion, adminNote);
        }

        private async Task<string> UpdateAdminFieldsCoreAsync(long reqId, string status, string resolvedVersion, string adminNote)
        {
            bool ok = await _repository.UpdateAdminFieldsAsync(reqId, status, resolvedVersion, adminNote).ConfigureAwait(false);
            return ok ? null : "저장에 실패했습니다." + FailureHint();
        }

        public Task<System.Collections.Generic.IList<ImprovementCommentDto>> GetCommentsAsync(long reqId)
        {
            return _repository.GetCommentsAsync(reqId);
        }

        public async Task<string> AddCommentAsync(long reqId, string commentText, string appVersion, long? parentCommentId = null)
        {
            if (reqId <= 0 || string.IsNullOrWhiteSpace(commentText))
                return "댓글 내용을 입력해 주세요.";

            long? userId = CurrentUserId;
            if (!userId.HasValue)
                return "로그인이 필요합니다.";

            var comment = new ImprovementCommentDto
            {
                ReqId = reqId,
                ParentCommentId = parentCommentId,
                UserId = userId.Value,
                AuthorName = OccupancyNameStore.HeaderName,
                TeamName = OccupancyNameStore.TryGetAffiliation(),
                CommentText = commentText.Trim(),
                AppVersion = appVersion
            };
            bool ok = await _repository.SaveCommentAsync(comment).ConfigureAwait(false);
            return ok ? null : "댓글 저장에 실패했습니다." + FailureHint();
        }

        /// <summary>본인 댓글만 수정 가능(관리자도 타인 댓글 내용은 수정하지 않음 — 관리는 삭제로만).</summary>
        public async Task<string> UpdateCommentAsync(ImprovementCommentDto existingComment, string newText)
        {
            if (existingComment == null || existingComment.CommentId <= 0)
                return "댓글을 찾을 수 없습니다.";
            if (string.IsNullOrWhiteSpace(newText))
                return "댓글 내용을 입력해 주세요.";

            long? userId = CurrentUserId;
            if (!ImprovementOwnership.IsOwner(userId, existingComment.UserId))
                return "본인이 작성한 댓글만 수정할 수 있습니다.";

            existingComment.CommentText = newText.Trim();
            bool ok = await _repository.SaveCommentAsync(existingComment).ConfigureAwait(false);
            return ok ? null : "댓글 저장에 실패했습니다." + FailureHint();
        }

        /// <summary>본인 댓글 또는 관리자만 삭제 가능.</summary>
        public async Task<string> DeleteCommentAsync(ImprovementCommentDto existingComment)
        {
            if (existingComment == null || existingComment.CommentId <= 0)
                return "댓글을 찾을 수 없습니다.";

            long? userId = CurrentUserId;
            if (!ImprovementOwnership.CanManage(userId, existingComment.UserId, OccupancyNameStore.IsAdmin))
                return "본인이 작성한 댓글이거나 관리자만 삭제할 수 있습니다.";

            bool ok = await _repository.DeleteCommentAsync(existingComment.CommentId).ConfigureAwait(false);
            return ok ? null : "댓글 삭제에 실패했습니다." + FailureHint();
        }

        /// <summary>동일 현상/정상 동작 반응. 1인 1반응, 다시 누르면 변경(MERGE upsert).</summary>
        public async Task<string> SetReactionAsync(long reqId, string reactionType)
        {
            if (reqId <= 0 || string.IsNullOrWhiteSpace(reactionType))
                return "반응을 선택해 주세요.";

            long? userId = CurrentUserId;
            if (!userId.HasValue)
                return "로그인이 필요합니다.";

            bool ok = await _repository.UpsertReactionAsync(reqId, userId.Value, reactionType).ConfigureAwait(false);
            return ok ? null : "반응 저장에 실패했습니다." + FailureHint();
        }

        private const long MaxAttachmentBytes = 5 * 1024 * 1024; // 5MB
        /// <summary>요청 하나당 첨부 가능한 최대 이미지 수. 편집 화면(클라이언트)도 이 값을 참조해 미리 안내한다.</summary>
        public const int MaxAttachmentsPerRequest = 5;

        /// <summary>
        /// 용량/확장자 검사만 수행하고 저장은 하지 않는다. 신규 작성 중 선택한 이미지를 아직 존재하지
        /// 않는 REQ_ID 없이 화면(로컬)에 임시로 들고 있을 때, 등록 전에 미리 검증하기 위해 사용한다.
        /// </summary>
        public string ValidateAttachment(string fileName, byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
                return "첨부 파일을 선택해 주세요.";
            if (fileData.Length > MaxAttachmentBytes)
                return "첨부 파일은 5MB 이하만 가능합니다.";

            string ext = System.IO.Path.GetExtension(fileName);
            ext = string.IsNullOrEmpty(ext) ? string.Empty : ext.TrimStart('.').ToLowerInvariant();
            if (ext != "png" && ext != "jpg" && ext != "jpeg")
                return "png/jpg/jpeg 파일만 첨부할 수 있습니다.";

            return null;
        }

        /// <summary>
        /// contentKey: 본문(설명) 안의 {{img:&lt;키&gt;}} 마커가 참조하는 안정 키. 등록 이후 별도로
        /// 추가하는 첨부(본문과 무관)는 null로 넘긴다.
        /// </summary>
        public async Task<string> AddAttachmentAsync(long reqId, string fileName, byte[] fileData, string contentKey = null)
        {
            if (reqId <= 0)
                return "첨부 파일을 선택해 주세요.";

            string validationError = ValidateAttachment(fileName, fileData);
            if (validationError != null)
                return validationError;

            var existing = await _repository.GetAttachmentsAsync(reqId, includeData: false).ConfigureAwait(false);
            if (existing != null && existing.Count >= MaxAttachmentsPerRequest)
                return "요청 하나에는 이미지를 최대 " + MaxAttachmentsPerRequest + "장까지 첨부할 수 있습니다.";

            string ext = System.IO.Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var attachment = new ImprovementAttachmentDto
            {
                ReqId = reqId,
                FileName = System.IO.Path.GetFileName(fileName),
                FileExt = ext,
                FileSize = fileData.Length,
                FileData = fileData,
                ContentKey = contentKey
            };
            bool ok = await _repository.SaveAttachmentAsync(attachment).ConfigureAwait(false);
            return ok ? null : "첨부 저장에 실패했습니다." + FailureHint();
        }

        /// <summary>본인 요청 또는 관리자만 첨부 삭제 가능(요청 작성자 UserId 기준 판정).</summary>
        public async Task<string> RemoveAttachmentAsync(long attachId, long requestAuthorUserId)
        {
            long? userId = CurrentUserId;
            if (!ImprovementOwnership.CanManage(userId, requestAuthorUserId, OccupancyNameStore.IsAdmin))
                return "본인이 작성한 요청이거나 관리자만 첨부를 삭제할 수 있습니다.";

            bool ok = await _repository.DeleteAttachmentAsync(attachId).ConfigureAwait(false);
            return ok ? null : "첨부 삭제에 실패했습니다." + FailureHint();
        }
    }
}
