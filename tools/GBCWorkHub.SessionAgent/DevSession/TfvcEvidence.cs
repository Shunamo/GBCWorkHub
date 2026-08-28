using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GBCWorkHub.SessionAgent.DevSession
{
    public sealed class ChangesetInfo
    {
        public int ChangesetId { get; set; }
        public DateTime CheckedInAtUtc { get; set; }
        public string Comment { get; set; }
        public string AuthorId { get; set; }

        /// <summary>Normalized local paths (D:\HISSolutions\...), mapped from TFVC server paths.</summary>
        public List<string> FilePaths { get; set; } = new List<string>();
    }

    public sealed class PendingItemInfo
    {
        public string FilePath { get; set; }
        public string ChangeType { get; set; }
    }

    /// <summary>
    /// Abstraction over "what does TFVC know" so SessionChangeClassifier is testable without a
    /// real TFS server. TfvcRestEvidenceProvider is the production implementation; unit tests
    /// use a simple in-memory fake instead.
    /// </summary>
    public interface ITfvcEvidenceProvider
    {
        List<ChangesetInfo> GetChangesetsInWindow(string userId, DateTime fromUtc, DateTime toUtc);
        List<PendingItemInfo> GetPendingChanges(string workspaceRoot);
    }

    /// <summary>
    /// Production TFVC evidence provider for the compiled SessionAgent.exe.
    ///
    /// Changesets: TFVC REST API (`_apis/tfvc/changesets` / `.../changes`) via plain HttpClient
    /// with Windows default credentials — the exact pattern already proven working in this same
    /// project's TfsRecentCollector.cs (same auth, same JSON shape, same error handling). Server
    /// paths returned by the API are mapped to local paths by replacing the configured
    /// Tfs.ServerPath prefix (e.g. "$/HISSolutions") with the local workspace root
    /// (e.g. "D:\HISSolutions").
    ///
    /// Pending changes: TFVC's on-prem REST API does not expose per-workspace pending changes,
    /// so this shells out to `tf.exe status &lt;serverPath&gt; /recursive /format:brief` — the
    /// tf.exe command-line client bundled with Team Explorer, expected to already be installed
    /// on this remote dev PC (per Aurora_SessionAgent.ps1's TFS OM dependency, Team Explorer is
    /// already a precondition here). NOT executed in this environment (no TFS server, no tf.exe,
    /// no live workspace) — reviewed by inspection only. The brief-format column layout should
    /// be verified against the tf.exe version actually installed at each site before relying on
    /// this in production; it is the most stable/commonly documented subset across TFS versions,
    /// but exact spacing/columns are a real risk this class does not eliminate.
    /// </summary>
    public sealed class TfvcRestEvidenceProvider : ITfvcEvidenceProvider
    {
        private readonly string _collectionUrl;
        private readonly string _serverPath;
        private readonly string _localWorkspaceRoot;
        private readonly string _tfExePath;

        public TfvcRestEvidenceProvider(string collectionUrl, string serverPath, string localWorkspaceRoot, string tfExePath)
        {
            _collectionUrl = (collectionUrl ?? string.Empty).TrimEnd('/');
            _serverPath = serverPath ?? "$/HISSolutions";
            _localWorkspaceRoot = localWorkspaceRoot ?? @"D:\HISSolutions";
            _tfExePath = tfExePath ?? "tf.exe";
        }

        public List<ChangesetInfo> GetChangesetsInWindow(string userId, DateTime fromUtc, DateTime toUtc)
        {
            var result = new List<ChangesetInfo>();
            if (string.IsNullOrWhiteSpace(_collectionUrl))
                return result;

            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            using (var handler = new HttpClientHandler { UseDefaultCredentials = true, PreAuthenticate = true })
            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(45);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                string listUrl = _collectionUrl
                    + "/_apis/tfvc/changesets"
                    + "?searchCriteria.itemPath=" + Uri.EscapeDataString(_serverPath)
                    + "&searchCriteria.author=" + Uri.EscapeDataString(userId ?? string.Empty)
                    + "&searchCriteria.fromDate=" + Uri.EscapeDataString(fromUtc.ToString("o"))
                    + "&searchCriteria.toDate=" + Uri.EscapeDataString(toUtc.ToString("o"))
                    + "&api-version=4.1";

                string listJson = GetString(client, listUrl);
                if (listJson == null)
                    return result;

                JObject root = JObject.Parse(listJson);
                JArray values = root["value"] as JArray;
                if (values == null)
                    return result;

                foreach (JToken item in values)
                {
                    int changesetId = item.Value<int?>("changesetId") ?? 0;
                    if (changesetId <= 0)
                        continue;

                    var info = new ChangesetInfo
                    {
                        ChangesetId = changesetId,
                        CheckedInAtUtc = ParseUtc(item["createdDate"]),
                        Comment = item.Value<string>("comment") ?? string.Empty
                    };
                    JObject authorObj = item["author"] as JObject;
                    info.AuthorId = authorObj != null ? authorObj.Value<string>("uniqueName") : null;

                    info.FilePaths = LoadChangedLocalPaths(client, changesetId);
                    result.Add(info);
                }
            }

            return result;
        }

        /// <summary>
        /// Confirmed against the real GBC TFS server (2026-08-21): "/_apis/tfvc/changesets"
        /// (list) accepts either {collection}/_apis/... or {collection}/{project}/_apis/...,
        /// but "/_apis/tfvc/changesets/{id}/changes" (per-changeset detail) only accepts the
        /// collection-root form — the project-scoped form 404s. Since Tfs.CollectionUrl may
        /// reasonably be configured either way (operators naturally think of "the TFS address"
        /// as including the project), this tries the configured URL first and falls back to the
        /// URL with its last path segment stripped (treated as an optional project name) before
        /// giving up. Business semantics (which files belong to which changeset) are unaffected —
        /// this only fixes which URL form actually reaches the data.
        /// </summary>
        private List<string> LoadChangedLocalPaths(HttpClient client, int changesetId)
        {
            var paths = new List<string>();
            string suffix = "/_apis/tfvc/changesets/" + changesetId + "/changes?$top=500&api-version=4.1";

            string json = GetString(client, _collectionUrl + suffix);
            if (json == null)
            {
                string collectionRoot = StripLastPathSegment(_collectionUrl);
                if (collectionRoot != null && !collectionRoot.Equals(_collectionUrl, StringComparison.OrdinalIgnoreCase))
                    json = GetString(client, collectionRoot + suffix);
            }
            if (json == null)
                return paths;

            JObject root = JObject.Parse(json);
            JArray values = root["value"] as JArray;
            if (values == null)
                return paths;

            foreach (JToken change in values)
            {
                JObject item = change["item"] as JObject;
                if (item == null)
                    continue;
                string serverItem = item.Value<string>("path");
                if (string.IsNullOrEmpty(serverItem))
                    continue;
                string local = MapServerPathToLocal(serverItem);
                if (local != null)
                    paths.Add(local);
            }
            return paths;
        }

        private static string StripLastPathSegment(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                return null;

            string path = uri.AbsolutePath.TrimEnd('/');
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash <= 0)
                return null; // nothing left to strip — already at host root

            string strippedPath = path.Substring(0, lastSlash);
            return uri.GetLeftPart(UriPartial.Authority) + strippedPath;
        }

        private string MapServerPathToLocal(string serverPath)
        {
            string normalizedServerPath = _serverPath.Replace('/', '\\').TrimEnd('\\');
            string normalizedItem = serverPath.Replace('/', '\\');
            if (!normalizedItem.StartsWith(normalizedServerPath, StringComparison.OrdinalIgnoreCase))
                return null;

            string relative = normalizedItem.Substring(normalizedServerPath.Length).TrimStart('\\');
            string combined = Path.Combine(_localWorkspaceRoot, relative);
            try
            {
                return Path.GetFullPath(combined);
            }
            catch
            {
                return combined;
            }
        }

        public List<PendingItemInfo> GetPendingChanges(string workspaceRoot)
        {
            var result = new List<PendingItemInfo>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _tfExePath,
                    Arguments = "status \"" + _serverPath + "\" /recursive /format:brief /noprompt",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(30000);
                    result = ParseTfStatusBrief(output);
                }
            }
            catch
            {
                // tf.exe not available / not on PATH / no active workspace at this path — treat
                // as "no pending changes known", not a hard failure of the whole session result.
            }
            return result;
        }

        private List<PendingItemInfo> ParseTfStatusBrief(string output)
        {
            var result = new List<PendingItemInfo>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            // Brief format is tabular text: "<local path>  <change type>". Header/footer lines
            // and blank lines are skipped; any line that doesn't parse cleanly is ignored rather
            // than aborting the whole scan.
            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.StartsWith("---") || line.StartsWith("There are no") || line.StartsWith("Change"))
                    continue;

                int splitAt = line.LastIndexOf("  ", StringComparison.Ordinal);
                if (splitAt <= 0)
                    continue;

                string path = line.Substring(0, splitAt).Trim();
                string changeType = line.Substring(splitAt).Trim();
                if (string.IsNullOrEmpty(path) || !path.Contains(":\\"))
                    continue;

                try
                {
                    result.Add(new PendingItemInfo
                    {
                        FilePath = Path.GetFullPath(path),
                        ChangeType = changeType
                    });
                }
                catch
                {
                }
            }
            return result;
        }

        private static DateTime ParseUtc(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return DateTime.UtcNow;
            if (token.Type == JTokenType.Date)
                return token.Value<DateTime>().ToUniversalTime();
            DateTime dt;
            if (DateTime.TryParse(token.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                return dt.ToUniversalTime();
            return DateTime.UtcNow;
        }

        private static string GetString(HttpClient client, string url)
        {
            try
            {
                using (HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                        return null;
                    return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
