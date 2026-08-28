using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GBCWorkHub.SessionAgent.DevSession
{
    public sealed class SessionFileResultDto
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

    public sealed class ChangesetSummaryDto
    {
        [JsonProperty("changesetId")] public int ChangesetId { get; set; }
        [JsonProperty("checkedInAtUtc")] public string CheckedInAtUtc { get; set; }
        [JsonProperty("comment")] public string Comment { get; set; }
        [JsonProperty("fileCount")] public int FileCount { get; set; }
    }

    /// <summary>
    /// Bounded result for one dev session. This is what actually crosses the RDP clipboard back
    /// to the local WorkHub — never full file contents, and capped so a large session can't blow
    /// up the clipboard payload.
    /// </summary>
    public sealed class SessionChangeReport
    {
        public const string PayloadType = "GBC_SESSION_CHANGE_RESULT";
        public const int DefaultMaxFiles = 300;
        public const int MaxReasonLength = 200;

        [JsonProperty("type")] public string Type { get; set; } = PayloadType;
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("sessionToken")] public string SessionToken { get; set; }
        [JsonProperty("remotePcName")] public string RemotePcName { get; set; }
        [JsonProperty("remoteUser")] public string RemoteUser { get; set; }
        [JsonProperty("sessionStartUtc")] public string SessionStartUtc { get; set; }
        [JsonProperty("sessionEndUtc")] public string SessionEndUtc { get; set; }
        [JsonProperty("changesets")] public List<ChangesetSummaryDto> Changesets { get; set; } = new List<ChangesetSummaryDto>();
        [JsonProperty("files")] public List<SessionFileResultDto> Files { get; set; } = new List<SessionFileResultDto>();
        [JsonProperty("preexistingPendingExcludedCount")] public int PreexistingPendingExcludedCount { get; set; }
        [JsonProperty("truncated")] public bool Truncated { get; set; }
        [JsonProperty("droppedFileCount")] public int DroppedFileCount { get; set; }

        public static SessionChangeReport Build(
            string sessionToken,
            string remotePcName,
            string remoteUser,
            DateTime? sessionStartUtc,
            DateTime? sessionEndUtc,
            ClassificationResult classification,
            List<ChangesetInfo> sessionChangesets,
            int maxFiles = DefaultMaxFiles)
        {
            var report = new SessionChangeReport
            {
                SessionToken = sessionToken,
                RemotePcName = remotePcName,
                RemoteUser = remoteUser,
                SessionStartUtc = sessionStartUtc.HasValue ? sessionStartUtc.Value.ToString("o") : null,
                SessionEndUtc = sessionEndUtc.HasValue ? sessionEndUtc.Value.ToString("o") : null,
                PreexistingPendingExcludedCount = classification.PreexistingPendingExcluded.Count
            };

            foreach (var cs in sessionChangesets.OrderBy(c => c.ChangesetId))
            {
                report.Changesets.Add(new ChangesetSummaryDto
                {
                    ChangesetId = cs.ChangesetId,
                    CheckedInAtUtc = cs.CheckedInAtUtc.ToString("o"),
                    Comment = Truncate(cs.Comment, MaxReasonLength),
                    FileCount = cs.FilePaths.Count
                });
            }

            var ordered = classification.Files.OrderBy(f => KindPriority(f.Kind)).ThenBy(f => f.FilePath, StringComparer.Ordinal).ToList();
            var kept = ordered.Take(maxFiles).ToList();
            report.DroppedFileCount = ordered.Count - kept.Count;
            report.Truncated = report.DroppedFileCount > 0;

            foreach (var f in kept.OrderBy(f => f.FilePath, StringComparer.Ordinal))
            {
                report.Files.Add(new SessionFileResultDto
                {
                    FilePath = f.FilePath,
                    Kind = f.Kind.ToString(),
                    Confidence = f.Confidence.ToString(),
                    Reason = Truncate(f.Reason, MaxReasonLength),
                    ChangesetId = f.ChangesetId,
                    TfvcStatus = f.TfvcStatus,
                    StartLength = f.Start != null ? f.Start.Length : (long?)null,
                    EndLength = f.End != null ? f.End.Length : (long?)null,
                    DiffAvailable = false,
                    DiffSummary = null
                });
            }

            return report;
        }

        private static int KindPriority(ChangeKind kind)
        {
            switch (kind)
            {
                case ChangeKind.CheckedIn: return 0;
                case ChangeKind.PendingChanged: return 1;
                case ChangeKind.Added: return 2;
                case ChangeKind.Deleted: return 3;
                case ChangeKind.SessionMetadataChanged: return 4;
                default: return 5;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;
            return value.Substring(0, maxLength - 3) + "...";
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }
    }
}
