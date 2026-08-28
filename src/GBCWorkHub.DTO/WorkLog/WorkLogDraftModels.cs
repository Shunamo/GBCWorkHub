using System.Collections.Generic;

namespace GBCWorkHub.DTO.WorkLog
{
    public sealed class WorkSourceItem
    {
        public int ChangesetId { get; set; }
        public string ChangeType { get; set; }
        public string ItemType { get; set; }
        public string FileName { get; set; }
        public string OriginalPath { get; set; }
        public string DisplayValue { get; set; }
        public string SourceOrigin { get; set; }
        public string Confidence { get; set; }
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }

        public string Type { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string AppliedRuleCode { get; set; }
    }

    public sealed class WorkGroupDraft
    {
        public string Type { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public List<WorkSourceItem> SourceItems { get; set; }
        public string Confidence { get; set; }
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }
        public string AppliedRuleCode { get; set; }

        public WorkGroupDraft()
        {
            SourceItems = new List<WorkSourceItem>();
        }
    }

    public sealed class WorkLogDraft
    {
        public string TicketNo { get; set; }
        public string TicketContents { get; set; }
        public bool TicketContentsIsSuggested { get; set; }
        public string MenuName { get; set; }
        public string Pc { get; set; }
        public string PersonInCharge { get; set; }
        public System.DateTime? StartDate { get; set; }
        public System.DateTime? EndDate { get; set; }
        public string DeploymentStatus { get; set; }
        public System.DateTime? DeploymentDate { get; set; }
        public string Comment { get; set; }

        public int ChangesetId { get; set; }
        public string TfsComment { get; set; }
        public string TfsAuthorId { get; set; }
        public string TfsAuthorName { get; set; }
        public System.DateTime? CheckedInAt { get; set; }

        public List<WorkGroupDraft> WorkGroups { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> SkippedItems { get; set; }

        public WorkLogDraft()
        {
            WorkGroups = new List<WorkGroupDraft>();
            Warnings = new List<string>();
            SkippedItems = new List<string>();
        }
    }

    /// <summary>
    /// 엑셀 14컬럼 Preview용 Flat Row (실제 엑셀 저장 아님).
    /// </summary>
    public sealed class WorkLogExcelPreviewRow
    {
        public string TicketNo { get; set; }
        public string TicketContents { get; set; }
        public string MenuName { get; set; }
        public string Pc { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string SourcePathAndFileName { get; set; }
        public string PersonInCharge { get; set; }
        public System.DateTime? StartDate { get; set; }
        public System.DateTime? EndDate { get; set; }
        public string DeploymentStatus { get; set; }
        public System.DateTime? DeploymentDate { get; set; }
        public string Comment { get; set; }
        public int ChangesetId { get; set; }
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }
    }
}
