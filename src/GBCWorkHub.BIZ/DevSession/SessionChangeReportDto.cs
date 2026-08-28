using System.Collections.Generic;
using Newtonsoft.Json;

namespace GBCWorkHub.BIZ.DevSession
{
    /// <summary>
    /// Mirrors GBCWorkHub.SessionAgent/DevSession/SessionChangeResult.cs's wire shape exactly —
    /// this is the receiving side of the GBCWORKHUB_SESSION_RESULT:: clipboard payload.
    /// </summary>
    public sealed class SessionFileResultWireDto
    {
        [JsonProperty("path")] public string FilePath { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("confidence")] public string Confidence { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
        [JsonProperty("changesetId")] public int? ChangesetId { get; set; }
        [JsonProperty("tfvcStatus")] public string TfvcStatus { get; set; }
        [JsonProperty("startLength")] public long? StartLength { get; set; }
        [JsonProperty("endLength")] public long? EndLength { get; set; }
        [JsonProperty("diffAvailable")] public bool DiffAvailable { get; set; }
        [JsonProperty("diffSummary")] public string DiffSummary { get; set; }
    }

    public sealed class ChangesetSummaryWireDto
    {
        [JsonProperty("changesetId")] public int ChangesetId { get; set; }
        [JsonProperty("checkedInAtUtc")] public string CheckedInAtUtc { get; set; }
        [JsonProperty("comment")] public string Comment { get; set; }
        [JsonProperty("fileCount")] public int FileCount { get; set; }
    }

    public sealed class SessionChangeReportDto
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonProperty("sessionToken")] public string SessionToken { get; set; }
        [JsonProperty("remotePcName")] public string RemotePcName { get; set; }
        [JsonProperty("remoteUser")] public string RemoteUser { get; set; }
        [JsonProperty("sessionStartUtc")] public string SessionStartUtc { get; set; }
        [JsonProperty("sessionEndUtc")] public string SessionEndUtc { get; set; }
        [JsonProperty("changesets")] public List<ChangesetSummaryWireDto> Changesets { get; set; } = new List<ChangesetSummaryWireDto>();
        [JsonProperty("files")] public List<SessionFileResultWireDto> Files { get; set; } = new List<SessionFileResultWireDto>();
        [JsonProperty("preexistingPendingExcludedCount")] public int PreexistingPendingExcludedCount { get; set; }
        [JsonProperty("truncated")] public bool Truncated { get; set; }
        [JsonProperty("droppedFileCount")] public int DroppedFileCount { get; set; }
    }
}
