using System;

namespace GBCWorkHub.DTO.Improvement
{
    /// <summary>XSUP.MSDWHTKD_REQ_COMMENT 1행.</summary>
    public sealed class ImprovementCommentDto
    {
        public long CommentId { get; set; }
        public long ReqId { get; set; }

        /// <summary>NULL이면 최상위 댓글, 값이 있으면 그 COMMENT_ID에 대한 답글(대댓글).</summary>
        public long? ParentCommentId { get; set; }

        /// <summary>MSDWHTKD_USR.USR_ID FK. 수정/삭제 권한 판정에 사용(이름 비교 아님).</summary>
        public long UserId { get; set; }
        public string AuthorName { get; set; }
        /// <summary>작성 시점 소속 스냅샷 — MSDWHTKD_REQ.TEAM_NM과 동일 관례.</summary>
        public string TeamName { get; set; }
        public string CommentText { get; set; }
        public string AppVersion { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 소프트 삭제 여부. 실제 DELETE를 하지 않는 이유는 답글(대댓글)이 달려 있을 수 있어서다 —
        /// 부모를 지워버리면 그 밑의 답글들이 고아가 되거나 맥락 없이 붕 뜨게 된다. 삭제되면 이 값만
        /// true가 되고 COMMENT_TEXT 등은 그대로 남아 있다(화면에서만 "삭제된 댓글입니다"로 가린다).
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>댓글 자신이 아니라 "작성자 계정"이 이후 삭제됐는지 — 조회 시 채워짐, 저장 대상 아님.</summary>
        public bool IsAuthorDeleted { get; set; }
    }
}
