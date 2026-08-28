using System;
using Newtonsoft.Json.Linq;

namespace GBCWorkHub.SessionAgent.DevSession
{
    /// <summary>
    /// Parses the GBCWORKHUB_SESSION_TOKEN:: clipboard payload the local WorkHub writes right
    /// before mstsc launches, for every session (see TfsClipboardAckService.TryAnnounceSessionToken
    /// on the local side). Deliberately a separate, minimal message from
    /// GBCWORKHUB_TFS_SYNC_REQUEST:: — that one is reserved for the existing TFS-sync-reconnect
    /// feature, whose presence Aurora_SessionAgent.ps1 uses to decide whether to fetch TFS
    /// history; reusing it here for every ordinary connect would change that script's behavior.
    /// </summary>
    public sealed class SyncRequest
    {
        public const string Prefix = "GBCWORKHUB_SESSION_TOKEN::";

        public string RequestId { get; set; }
        public string SessionToken { get; set; }

        public static SyncRequest TryParse(string clipboardText)
        {
            if (string.IsNullOrWhiteSpace(clipboardText))
                return null;

            string trimmed = clipboardText.TrimStart();
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
                return null;

            try
            {
                string json = trimmed.Substring(Prefix.Length).Trim();
                JObject obj = JObject.Parse(json);

                string sessionToken = (string)obj["sessionToken"];
                if (string.IsNullOrWhiteSpace(sessionToken))
                    return null;

                return new SyncRequest
                {
                    RequestId = (string)obj["requestId"],
                    SessionToken = sessionToken
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
