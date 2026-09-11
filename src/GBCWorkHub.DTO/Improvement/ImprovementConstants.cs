namespace GBCWorkHub.DTO.Improvement
{
    /// <summary>요청 유형. DB 저장값(영문 코드) 기준, UI 라벨은 ImprovementTypeLabels 참조.</summary>
    public static class ImprovementRequestTypes
    {
        public const string Bug = "BUG";
        public const string Improvement = "IMPROVEMENT";
        public const string Etc = "ETC";
    }

    public static class ImprovementTypeLabels
    {
        public const string All = "전체";
        public const string Bug = "버그";
        public const string Improvement = "기능 개선";
        public const string Etc = "기타";

        public static string ToLabel(string code)
        {
            if (string.Equals(code, ImprovementRequestTypes.Bug, System.StringComparison.OrdinalIgnoreCase))
                return Bug;
            if (string.Equals(code, ImprovementRequestTypes.Improvement, System.StringComparison.OrdinalIgnoreCase))
                return Improvement;
            if (string.Equals(code, ImprovementRequestTypes.Etc, System.StringComparison.OrdinalIgnoreCase))
                return Etc;
            return code ?? string.Empty;
        }
    }

    /// <summary>요청 상태. DB 저장값(영문 코드) 기준, UI 라벨은 ImprovementStatusLabels 참조.</summary>
    public static class ImprovementStatuses
    {
        public const string Open = "OPEN";
        public const string Checking = "CHECKING";
        public const string InProgress = "IN_PROGRESS";
        public const string Resolved = "RESOLVED";
        public const string Rejected = "REJECTED";
    }

    public static class ImprovementStatusLabels
    {
        public const string All = "전체";
        public const string Open = "접수";
        public const string Checking = "확인중";
        public const string InProgress = "수정중";
        public const string Resolved = "해결";
        public const string Rejected = "반려";

        public static string ToLabel(string code)
        {
            if (string.Equals(code, ImprovementStatuses.Open, System.StringComparison.OrdinalIgnoreCase))
                return Open;
            if (string.Equals(code, ImprovementStatuses.Checking, System.StringComparison.OrdinalIgnoreCase))
                return Checking;
            if (string.Equals(code, ImprovementStatuses.InProgress, System.StringComparison.OrdinalIgnoreCase))
                return InProgress;
            if (string.Equals(code, ImprovementStatuses.Resolved, System.StringComparison.OrdinalIgnoreCase))
                return Resolved;
            if (string.Equals(code, ImprovementStatuses.Rejected, System.StringComparison.OrdinalIgnoreCase))
                return Rejected;
            return code ?? string.Empty;
        }
    }

    /// <summary>반응 종류. DB 저장값(영문 코드) 기준.</summary>
    public static class ImprovementReactionTypes
    {
        public const string Reproduced = "REPRODUCED";
        public const string NotReproduced = "NOT_REPRODUCED";
    }
}
