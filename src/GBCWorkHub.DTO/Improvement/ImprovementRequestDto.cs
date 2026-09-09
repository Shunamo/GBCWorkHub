using System;
using System.Collections.Generic;

namespace GBCWorkHub.DTO.Improvement
{
    /// <summary>XSUP.MSDWHTKD_REQ 1행 + 목록/상세 부가 집계값.</summary>
    public sealed class ImprovementRequestDto
    {
        public long ReqId { get; set; }
        public string RequestType { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ReproductionSteps { get; set; }
        public string AppVersion { get; set; }
        public string SiteCode { get; set; }
        public string PcName { get; set; }

        /// <summary>MSDWHTKD_USR.USR_ID FK — 관계형 식별자. 이름/PC로 재해석하지 않는다.</summary>
        public long UserId { get; set; }
        /// <summary>작성 시점 표시명 스냅샷(이후 개명해도 유지됨). 기존 WorkLog.AUTHOR_NM과 동일 관례.</summary>
        public string AuthorName { get; set; }
        /// <summary>작성자 계정이 이후 삭제됐는지 — 조회 시 채워짐, 저장 대상 아님.</summary>
        public bool IsAuthorDeleted { get; set; }
        public string TeamName { get; set; }

        public string Status { get; set; }
        public string ResolvedVersion { get; set; }
        public string AdminNote { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        // 목록/상세 조회 시 부가 집계(저장 대상 아님)
        public int ReproducedCount { get; set; }
        public int NotReproducedCount { get; set; }
        public int CommentCount { get; set; }
        /// <summary>현재 로그인 사용자의 반응(REPRODUCED/NOT_REPRODUCED/null). 세션 UserId 기준.</summary>
        public string MyReaction { get; set; }

        public IList<ImprovementAttachmentDto> Attachments { get; set; }

        public ImprovementRequestDto()
        {
            Attachments = new List<ImprovementAttachmentDto>();
        }
    }
}
