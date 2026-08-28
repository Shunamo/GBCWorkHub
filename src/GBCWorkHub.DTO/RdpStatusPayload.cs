using System.Collections.Generic;
using Newtonsoft.Json;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 클립보드 GBC_RDP_STATUS JSON 페이로드
    /// </summary>
    public class RdpStatusPayload
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("computerName")]
        public string ComputerName { get; set; }

        [JsonProperty("windowsUser")]
        public string WindowsUser { get; set; }

        [JsonProperty("clientName")]
        public string ClientName { get; set; }

        [JsonProperty("collectedAt")]
        public string CollectedAt { get; set; }

        [JsonProperty("latestEventId")]
        public int? LatestEventId { get; set; }

        [JsonProperty("latestRecordId")]
        public long? LatestRecordId { get; set; }

        [JsonProperty("sessionRaw")]
        public string SessionRaw { get; set; }

        [JsonProperty("triggerType")]
        public string TriggerType { get; set; }

        [JsonProperty("isVerifiedDisconnect")]
        public bool? IsVerifiedDisconnect { get; set; }

        [JsonProperty("events")]
        public List<RdpEventPayload> Events { get; set; }
    }
}
