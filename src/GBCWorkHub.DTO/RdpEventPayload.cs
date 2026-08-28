using Newtonsoft.Json;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// RDP 이벤트 로그 항목
    /// </summary>
    public class RdpEventPayload
    {
        [JsonProperty("recordId")]
        public long RecordId { get; set; }

        [JsonProperty("eventId")]
        public int EventId { get; set; }

        [JsonProperty("eventTime")]
        public string EventTime { get; set; }

        [JsonProperty("eventMessage")]
        public string EventMessage { get; set; }
    }
}
