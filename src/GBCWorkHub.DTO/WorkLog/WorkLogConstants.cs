namespace GBCWorkHub.DTO.WorkLog
{
    /// <summary>업무기록 사이트 구분 (원격 사이트 SiteCode 와 동일).</summary>
    public static class WorkLogSiteCodes
    {
        public const string All = "전체";
        public const string Aurora = "AURORA";
        public const string Rc = "RC";
        public const string Cmc = "CMC";
        public const string Mngha = "MNGHA";
    }

    /// <summary>목록 필터 UI 센티널 (DAC에서 한글 리터럴 깨짐 방지용으로 공유).</summary>
    public static class WorkLogFilterLabels
    {
        public const string All = "전체";
        public const string UnclassifiedType = "미분류";
    }

    /// <summary>작성상태 UI 라벨. DB 저장값은 DRAFT/COMPLETED.</summary>
    public static class WorkLogWriteStatusLabels
    {
        public const string Draft = "임시저장";
        public const string Completed = "작성 완료";
    }

    /// <summary>배포 상태 필터/편집 공통 라벨.</summary>
    public static class WorkLogDeployStatusLabels
    {
        public const string LocalPc = "로컬 PC";
        public const string Rollback = "롤백";
        public const string Staging = "스테이징";
        public const string Production = "운영기";
    }

    /// <summary>
    /// Source 출처. 현재는 TFS만 사용, 향후 EQS/MANUAL 확장.
    /// </summary>
    public static class SourceOrigin
    {
        public const string Tfs = "TFS";
        public const string Eqs = "EQS";
        public const string Manual = "MANUAL";
    }

    public static class ParseConfidence
    {
        public const string High = "High";
        public const string Medium = "Medium";
        public const string Low = "Low";
    }
}
