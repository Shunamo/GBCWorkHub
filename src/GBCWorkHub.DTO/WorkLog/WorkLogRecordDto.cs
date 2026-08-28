using System;
using System.Collections.Generic;

namespace GBCWorkHub.DTO.WorkLog
{
    /// <summary>XSUP.MSDWHTKD_WRK 업무기록 1건 (+ 프로젝트/소스).</summary>
    public sealed class WorkLogRecordDto
    {
        public long LogId { get; set; }
        public string ClientKey { get; set; }
        /// <summary>사이트 코드 (AURORA / RC 등). 목록 분리용.</summary>
        public string SiteCode { get; set; }
        /// <summary>DRAFT | COMPLETED</summary>
        public string WriteStatus { get; set; }
        public string TicketNo { get; set; }
        public string TicketContents { get; set; }
        public string MenuName { get; set; }
        public string PcName { get; set; }
        public string PersonInCharge { get; set; }
        public string LocalPcIp { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string DeploymentStatus { get; set; }
        public DateTime? DeploymentDate { get; set; }
        public string WorkComment { get; set; }
        public int ChangesetId { get; set; }
        /// <summary>MSDWHTKD_WRK_CS 에 저장되는 등록 Changeset ID 목록.</summary>
        public List<int> ChangesetIds { get; set; }
        public string TfsComment { get; set; }
        public string TfsAuthor { get; set; }
        public string AuthorName { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public int ChangedFileCount { get; set; }
        public bool NeedsTicketReview { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public List<WorkLogProjectDto> Projects { get; set; }
        public List<WorkLogSourceDto> Sources { get; set; }

        public WorkLogRecordDto()
        {
            Projects = new List<WorkLogProjectDto>();
            Sources = new List<WorkLogSourceDto>();
            ChangesetIds = new List<int>();
            WriteStatus = "DRAFT";
        }
    }

    public sealed class WorkLogProjectDto
    {
        public long ProjectId { get; set; }
        public long LogId { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string SourceOrigin { get; set; }
        public string DeploymentStatus { get; set; }
        public DateTime? DeploymentDate { get; set; }
        public string Comment { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class WorkLogSourceDto
    {
        public long SourceId { get; set; }
        public long LogId { get; set; }
        public long? ProjectId { get; set; }
        public string FileName { get; set; }
        public string OriginalPath { get; set; }
        public string ChangeType { get; set; }
        public string ChangeDetail { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string SourceOrigin { get; set; }
        public string AppliedRuleCode { get; set; }
        public bool IsAutoClassified { get; set; }
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }
        public int SortOrder { get; set; }
        /// <summary>소스 행이 업무기록에 붙은 시각 (DB CREATED_AT 보존).</summary>
        public DateTime? CreatedAt { get; set; }
    }
}
