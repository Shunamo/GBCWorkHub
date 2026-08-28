using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GBCWorkHub.SessionAgent
{
    internal static class RdpClipboardPayload
    {
        public const string ClipboardPrefix = "GBCWORKHUB::";

        public static string BuildConnectClipboard()
        {
            DateTime now = DateTime.Now;
            var data = new Dictionary<string, object>
            {
                { "type", "GBC_RDP_STATUS" },
                { "schemaVersion", 1 },
                { "computerName", Environment.MachineName },
                { "windowsUser", Environment.UserDomainName + "\\" + Environment.UserName },
                { "clientName", Environment.GetEnvironmentVariable("CLIENTNAME") },
                { "collectedAt", now.ToString("yyyy-MM-dd HH:mm:ss") },
                { "latestEventId", 21 },
                { "latestRecordId", now.Ticks },
                { "sessionRaw", "rdp-tcp#0 Active" },
                { "triggerType", "RC_CONNECT" },
                { "isVerifiedDisconnect", null },
                { "events", null }
            };
            return ClipboardPrefix + JsonConvert.SerializeObject(data);
        }

        public static string BuildDisconnectClipboard()
        {
            DateTime now = DateTime.Now;
            var data = new Dictionary<string, object>
            {
                { "type", "GBC_RDP_STATUS" },
                { "schemaVersion", 1 },
                { "computerName", Environment.MachineName },
                { "windowsUser", Environment.UserDomainName + "\\" + Environment.UserName },
                { "clientName", Environment.GetEnvironmentVariable("CLIENTNAME") },
                { "collectedAt", now.ToString("yyyy-MM-dd HH:mm:ss") },
                { "latestEventId", 24 },
                { "latestRecordId", now.Ticks },
                { "sessionRaw", null },
                { "triggerType", "RDP_DISCONNECT" },
                { "isVerifiedDisconnect", true },
                { "events", null }
            };
            return ClipboardPrefix + JsonConvert.SerializeObject(data);
        }

        public static string ToJsonBody(string clipboardValue)
        {
            if (string.IsNullOrEmpty(clipboardValue))
                return null;
            if (clipboardValue.StartsWith(ClipboardPrefix, StringComparison.Ordinal))
                return clipboardValue.Substring(ClipboardPrefix.Length);
            return clipboardValue;
        }
    }
}
