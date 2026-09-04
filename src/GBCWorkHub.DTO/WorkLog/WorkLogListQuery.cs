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

        /// <summary>PC 필터: PC명·IP·점유 키 별명. 있으면 PcName 단독 일치 대신 이 목록으로 찾는다.</summary>
        public System.Collections.Generic.IList<string> PcAliases { get; set; }

        /// <summary>검색어가 IP/PC명 일부일 때 PC_NM 매칭용 별명.</summary>
        public System.Collections.Generic.IList<string> SearchAliases { get; set; }
        public string WriteStatus { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public string DeployStatus { get; set; }

        /// <summary>마이페이지: AUTHOR_NM 일치. 이름 없는 옛 기록은 AuthorLocalPcIp.</summary>
        public string AuthorName { get; set; }

        /// <summary>AUTHOR_NM이 비어 있는 옛 기록의 LOCAL_PC_IP 폴백.</summary>
        public string AuthorLocalPcIp { get; set; }

        /// <summary>TEAM_NM 및 작성자/담당자 문자열에 포함된 소속.</summary>
        public string TeamName { get; set; }

        /// <summary>
        /// 선택한 소속이 이 PC 점유 소속과 같을 때 AUTHOR_NM 폴백.
        /// TEAM_NM이 비어 있어도 본인 기록을 포함한다.
        /// </summary>
        public string OccupancyAuthorName { get; set; }

        /// <summary>점유 소속 필터일 때 LOCAL_PC_IP 폴백.</summary>
        public string OccupancyLocalPcIp { get; set; }

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
