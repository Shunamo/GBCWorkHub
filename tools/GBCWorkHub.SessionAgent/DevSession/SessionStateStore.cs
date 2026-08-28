using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GBCWorkHub.SessionAgent.DevSession
{
    /// <summary>
    /// The connect-time snapshot, persisted to a small local JSON file on THIS (remote) PC only
    /// — session-state/{SESSION_TOKEN}.start.json — so the disconnect-time SessionAgent
    /// invocation (a separate process) can read it back. Never sent anywhere; only the
    /// disconnect-time diff/classification result crosses the clipboard.
    /// </summary>
    public sealed class SessionStateFile
    {
        [JsonProperty("sessionToken")] public string SessionToken { get; set; }
        [JsonProperty("requestId")] public string RequestId { get; set; }
        [JsonProperty("startedAtUtc")] public string StartedAtUtc { get; set; }
        [JsonProperty("remotePcName")] public string RemotePcName { get; set; }
        [JsonProperty("remoteUser")] public string RemoteUser { get; set; }
        [JsonProperty("snapshot")] public Dictionary<string, FileMeta> Snapshot { get; set; } = new Dictionary<string, FileMeta>();
        [JsonProperty("startPending")] public List<PendingItemInfo> StartPending { get; set; } = new List<PendingItemInfo>();

        public DateTime? StartedAtUtcValue
        {
            get
            {
                DateTime dt;
                return DateTime.TryParse(StartedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out dt)
                    ? dt.ToUniversalTime()
                    : (DateTime?)null;
            }
        }
    }

    public static class SessionStateStore
    {
        private const string Extension = ".start.json";

        public static string DirectoryFor(string baseDirectory)
        {
            return Path.Combine(baseDirectory, "session-state");
        }

        public static void Save(string baseDirectory, SessionStateFile state)
        {
            string dir = DirectoryFor(baseDirectory);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, SafeFileName(state.SessionToken) + Extension);
            string json = JsonConvert.SerializeObject(state, Formatting.Indented);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>
        /// Only one developer can use this remote PC at a time, so under normal operation this
        /// returns at most one file. If more than one is found (e.g. a crashed prior session
        /// whose disconnect never ran), all are returned — the caller decides how to handle that.
        /// </summary>
        public static List<SessionStateFile> LoadAll(string baseDirectory)
        {
            var result = new List<SessionStateFile>();
            string dir = DirectoryFor(baseDirectory);
            if (!Directory.Exists(dir))
                return result;

            foreach (string file in Directory.GetFiles(dir, "*" + Extension))
            {
                try
                {
                    var state = JsonConvert.DeserializeObject<SessionStateFile>(File.ReadAllText(file));
                    if (state != null)
                        result.Add(state);
                }
                catch
                {
                    // Corrupt/partial state file — skip rather than fail the whole disconnect flow.
                }
            }
            return result;
        }

        public static void Delete(string baseDirectory, string sessionToken)
        {
            string path = Path.Combine(DirectoryFor(baseDirectory), SafeFileName(sessionToken) + Extension);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static string SafeFileName(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "unknown-" + Guid.NewGuid().ToString("N");
            var invalid = Path.GetInvalidFileNameChars();
            return new string(token.Where(c => !invalid.Contains(c)).ToArray());
        }
    }
}
