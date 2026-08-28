using System;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 중앙 DB GBC_REMOTE_PC 조회 결과
    /// </summary>
    public class RemotePcShareDto
    {
        public long RemotePcId { get; set; }
        public string ComputerName { get; set; }
        public string IpAddress { get; set; }
        public string DisplayName { get; set; }
        /// <summary>DB 원본 상태 (AVAILABLE/CONNECTING/IN_USE/CHECK_REQUIRED)</summary>
        public string DbStatus { get; set; }
        /// <summary>UI 표시용 (상태 확인 필요 등 stale 반영 후)</summary>
        public string DisplayStatus { get; set; }
        public string CurrentSessionToken { get; set; }
        public string CurrentUserAccount { get; set; }
        public string CurrentClientPc { get; set; }
        public DateTime? ConnectionRequestedAt { get; set; }
        public DateTime? ConnectionConfirmedAt { get; set; }
        public DateTime? LastHeartbeatAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public long VersionNo { get; set; }
        public bool IsActive { get; set; }
        public bool IsStale { get; set; }
    }
}
