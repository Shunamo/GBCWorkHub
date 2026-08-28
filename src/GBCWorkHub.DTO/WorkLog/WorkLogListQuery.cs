using System;

namespace GBCWorkHub.DTO.WorkLog
{
    /// <summary>업무기록 목록 DB 페이징 + 필터 조건.</summary>
    public sealed class WorkLogListQuery
    {
        public int PageIndex { get; set; }
        public int PageSize { get; set; }

        public string SearchText { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string SiteCode { get; set; }
        public string PcName { get; set; }
        public string WriteStatus { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public string DeployStatus { get; set; }

        public int Offset
        {
            get
            {
                int size = PageSize <= 0 ? 30 : PageSize;
                int index = PageIndex < 0 ? 0 : PageIndex;
                return index * size;
            }
        }

        public int Take
        {
            get { return PageSize <= 0 ? 30 : PageSize; }
        }
    }

    public sealed class WorkLogPageResult
    {
        public WorkLogPageResult()
        {
            Items = new System.Collections.Generic.List<WorkLogRecordDto>();
        }

        public System.Collections.Generic.IList<WorkLogRecordDto> Items { get; set; }
        public int TotalCount { get; set; }
    }
}
