using System;
using System.Collections.Generic;

namespace GBCWorkHub.DTO
{
    public static class TfsCandidateStatuses
    {
        public const string New = "NEW";
        public const string Draft = "DRAFT";
        public const string ReviewRequired = "REVIEW_REQUIRED";
        public const string Ready = "READY";
        public const string Registered = "REGISTERED";
        public const string Duplicate = "DUPLICATE";
        public const string Dismissed = "DISMISSED";
        public const string Failed = "FAILED";
    }

    public static class TfsFieldSources
    {
        public const string Auto = "자동 추출";
        public const string User = "사용자 수정";
        public const string ReviewRequired = "확인 필요";
    }

    public static class TfsQueryModes
    {
        public const string RecentCount = "RECENT_COUNT";
        public const string SessionWindow = "SESSION_WINDOW";
        /// <summary>세션 구간 0건 → 오늘 동일 계정 체크인 폴백</summary>
        public const string TodayFallback = "TODAY_FALLBACK";
        /// <summary>구버전 호환</summary>
        public const string RecentFallback = "RECENT_FALLBACK";
    }

    /// <summary>회사 업무관리 14컬럼 편집 모델</summary>
    public class TfsWorkLogEditModel
    {
        public string WorkKey { get; set; }
        public string DisplayTitle { get; set; }
        public bool IsMergedBundle { get; set; }

        public string TicketNo { get; set; }
        public string TicketContents { get; set; }
        public string MenuScreenName { get; set; }
        public string PcName { get; set; }
        public string WorkType { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string SourcePathFileName { get; set; }
        public string PersonInCharge { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string DeploymentStatus { get; set; }
        public DateTime? DeploymentDate { get; set; }
        public string Comment { get; set; }

        public string TicketNoSource { get; set; }
        public string TicketContentsSource { get; set; }
        public string MenuScreenNameSource { get; set; }
        public string PcNameSource { get; set; }
        public string WorkTypeSource { get; set; }
        public string CategorySource { get; set; }
        public string ProjectNameSource { get; set; }
        public string SourcePathFileNameSource { get; set; }
        public string PersonInChargeSource { get; set; }
        public string StartDateSource { get; set; }
        public string EndDateSource { get; set; }
        public string DeploymentStatusSource { get; set; }
        public string DeploymentDateSource { get; set; }
        public string CommentSource { get; set; }

        public string StartDateHint { get; set; }
        public string EndDateHint { get; set; }
        public string ReviewHints { get; set; }

        public List<TfsWorkLogChangesetLink> LinkedChangesets { get; set; }

        public TfsWorkLogEditModel()
        {
            LinkedChangesets = new List<TfsWorkLogChangesetLink>();
            DeploymentStatus = "미확인";
        }
    }

    public class TfsWorkLogHeader
    {
        public long WorkLogId { get; set; }
        public string TicketNo { get; set; }
        public string TicketContents { get; set; }
        public string MenuScreenName { get; set; }
        public string PcName { get; set; }
        public string WorkType { get; set; }
        public string Category { get; set; }
        public string ProjectName { get; set; }
        public string SourcePathFileName { get; set; }
        public string PersonInCharge { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string DeploymentStatus { get; set; }
        public DateTime? DeploymentDateTime { get; set; }
        public string Comments { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedDateTime { get; set; }
        public DateTime UpdatedDateTime { get; set; }
        public List<TfsWorkLogChangesetLink> Changesets { get; set; }

        public TfsWorkLogHeader()
        {
            Changesets = new List<TfsWorkLogChangesetLink>();
        }
    }

    public class TfsWorkLogChangesetLink
    {
        public long LinkId { get; set; }
        public long WorkLogId { get; set; }
        public string CollectionUrl { get; set; }
        public string ServerPath { get; set; }
        public int ChangesetId { get; set; }
        public string AuthorId { get; set; }
        public string AuthorName { get; set; }
        public DateTime? CheckedInDateTime { get; set; }
        public string OriginalComment { get; set; }
        public string RemotePcName { get; set; }
        public string SourceClientName { get; set; }
        public string QueryMode { get; set; }
        public DateTime? SessionStartDateTime { get; set; }
        public DateTime? SessionEndDateTime { get; set; }
        public string SessionToken { get; set; }
        public int ChangedFileCount { get; set; }
        public string ChangedFileSummary { get; set; }
    }

    public class TfsChangesetKey
    {
        public string CollectionUrl { get; set; }
        public int ChangesetId { get; set; }

        public TfsChangesetKey()
        {
        }

        public TfsChangesetKey(string collectionUrl, int changesetId)
        {
            CollectionUrl = collectionUrl;
            ChangesetId = changesetId;
        }

        public string Key
        {
            get
            {
                return (CollectionUrl ?? string.Empty).Trim().ToLowerInvariant()
                    + "|" + ChangesetId;
            }
        }
    }

    public class TfsSaveWorkResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<TfsChangesetKey> DuplicateKeys { get; set; }
        public long WorkLogId { get; set; }

        public TfsSaveWorkResult()
        {
            DuplicateKeys = new List<TfsChangesetKey>();
        }
    }
}
