namespace GBCWorkHub.DTO
{
    /// <summary>사이트별 원격 PC명 ↔ IP ↔ 점유 키.</summary>
    public sealed class PcMapDto
    {
        public string SiteCode { get; set; }
        public string PcName { get; set; }
        public string PcIp { get; set; }
        public string ShareKey { get; set; }
        /// <summary>원격 PC 소속. 예: 진료지원, 진료간호.</summary>
        public string TeamName { get; set; }
        /// <summary>원격 Windows 로그인(엑셀 ID). domain\user / user@host, 복수면 줄바꿈.</summary>
        public string PcDomain { get; set; }
        /// <summary>엑셀 ID/Password 섹션만. [ID] / [Password].</summary>
        public string PcNote { get; set; }
        /// <summary>사용자 수정 가능 코멘트(엑셀 Comment 시드 + 앱 편집).</summary>
        public string PcComment { get; set; }
    }
}
