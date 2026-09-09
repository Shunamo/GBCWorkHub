using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO.Improvement;

namespace GBCWorkHub.DAC
{
    public interface IImprovementRepository
    {
        bool IsConfigured { get; }

        Task<ImprovementPageResult> GetPageAsync(ImprovementListQuery query, long? currentUserId);
        Task<ImprovementRequestDto> GetByIdAsync(long reqId, long? currentUserId);
        /// <summary>ReqId&lt;=0 이면 INSERT(성공 시 record.ReqId 채움), 아니면 UPDATE.</summary>
        Task<bool> SaveAsync(ImprovementRequestDto record);
        /// <summary>관리자 전용 필드만 갱신.</summary>
        Task<bool> UpdateAdminFieldsAsync(long reqId, string status, string resolvedVersion, string adminNote);
        Task<bool> DeleteByIdAsync(long reqId);

        Task<IList<ImprovementCommentDto>> GetCommentsAsync(long reqId);
        /// <summary>CommentId&lt;=0 이면 INSERT(성공 시 comment.CommentId 채움), 아니면 UPDATE.</summary>
        Task<bool> SaveCommentAsync(ImprovementCommentDto comment);
        Task<bool> DeleteCommentAsync(long commentId);

        Task<bool> UpsertReactionAsync(long reqId, long userId, string reactionType);

        Task<bool> SaveAttachmentAsync(ImprovementAttachmentDto attachment);
        /// <summary>includeData=false면 FILE_DATA(BLOB)는 채우지 않고 메타데이터만 반환.</summary>
        Task<IList<ImprovementAttachmentDto>> GetAttachmentsAsync(long reqId, bool includeData);
        Task<bool> DeleteAttachmentAsync(long attachId);
    }
}
