namespace GBCWorkHub.DTO.Improvement
{
    /// <summary>개선사항 요청 목록 조회 조건. WorkLogListQuery와 동일한 관례(서버사이드 페이징).</summary>
    public sealed class ImprovementListQuery
    {
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public string SearchText { get; set; }
        public string RequestType { get; set; }
        public string Status { get; set; }
        public string SiteCode { get; set; }

        /// <summary>관리자 화면 전용 정렬: "UNRESOLVED_FIRST" | "MOST_REPRODUCED" | "RECENT"(기본).</summary>
        public string SortMode { get; set; }

        public int Offset { get { return PageIndex * PageSize; } }
        public int Take { get { return PageSize <= 0 ? 30 : PageSize; } }
    }

    public sealed class ImprovementPageResult
    {
        public System.Collections.Generic.IList<ImprovementRequestDto> Items { get; set; }
        public int TotalCount { get; set; }

        public ImprovementPageResult()
        {
            Items = new System.Collections.Generic.List<ImprovementRequestDto>();
        }
    }
}
