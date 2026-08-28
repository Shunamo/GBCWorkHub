using System;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.Models
{
    /// <summary>
    /// computerName별 마지막 정상 승인 GBC payload
    /// </summary>
    public class LastProcessedRdpState
    {
        public string ComputerName { get; set; }
        public long? LatestRecordId { get; set; }
        public DateTime? CollectedAt { get; set; }
        public string PayloadHash { get; set; }
        public RdpStatusPayload Payload { get; set; }
    }
}
