using Newtonsoft.Json;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// GBCWORKHUB_TFS_SYNC_REQUEST::{JSON}
    /// 원격 Handle-RdpConnect.ps1과 필드명을 통일한다.
    /// </summary>
    public sealed class TfsSyncRequestClipboardDto
    {
        public const string ExpectedType = "TFS_SYNC_REQUEST";

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("targetComputerName")]
        public string TargetComputerName { get; set; }

        [JsonProperty("remoteIp")]
        public string RemoteIp { get; set; }

        [JsonProperty("requestedAtUtc")]
        public string RequestedAtUtc { get; set; }

        [JsonProperty("sessionStartedAtUtc")]
        public string SessionStartedAtUtc { get; set; }

        [JsonProperty("sessionEndedAtUtc")]
        public string SessionEndedAtUtc { get; set; }

        /// <summary>
        /// Was never actually serialized before this field existed — TfsSyncRequest.SessionToken
        /// (the in-process request object) was never copied onto this wire DTO, so the remote
        /// side never received it despite the field being present one layer up. Added so the
        /// remote SessionAgent can echo it back in its GBCWORKHUB_TFS:: reply.
        /// </summary>
        [JsonProperty("sessionToken")]
        public string SessionToken { get; set; }
    }
}
