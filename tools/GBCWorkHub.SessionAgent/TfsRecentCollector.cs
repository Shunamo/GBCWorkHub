using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GBCWorkHub.SessionAgent
{
    /// <summary>
    /// RC TFS(TFVC) REST로 최근 체크인 조회 → GBCWORKHUB_TFS:: 페이로드.
    /// Windows 인증(DefaultCredentials). OM DLL 불필요.
    /// </summary>
    internal static class TfsRecentCollector
    {
        public const string ClipboardPrefix = "GBCWORKHUB_TFS::";
        public const string ExpectedType = "GBC_TFS_RECENT_CHANGESETS";

        public class CollectResult
        {
            public bool Success { get; set; }
            public string ClipboardText { get; set; }
            public string JsonBody { get; set; }
            public string Error { get; set; }
        }

        public static CollectResult Collect()
        {
            string collectionUrl = (ConfigurationManager.AppSettings["Tfs.CollectionUrl"] ?? string.Empty).Trim().TrimEnd('/');
            string serverPath = (ConfigurationManager.AppSettings["Tfs.ServerPath"] ?? "$/HISSolutions").Trim();
            int recentCount = 3;
            int.TryParse(ConfigurationManager.AppSettings["Tfs.RecentCount"], out recentCount);
            if (recentCount <= 0)
                recentCount = 3;

            if (string.IsNullOrWhiteSpace(collectionUrl))
            {
                return new CollectResult { Success = false, Error = "Tfs.CollectionUrl empty" };
            }

            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                using (var handler = new HttpClientHandler
                {
                    UseDefaultCredentials = true,
                    PreAuthenticate = true
                })
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(45);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    string author = Environment.UserDomainName + "\\" + Environment.UserName;
                    string listUrl = collectionUrl
                        + "/_apis/tfvc/changesets"
                        + "?searchCriteria.itemPath=" + Uri.EscapeDataString(serverPath)
                        + "&searchCriteria.author=" + Uri.EscapeDataString(author)
                        + "&$top=" + recentCount
                        + "&api-version=4.1";

                    string listJson = GetString(client, listUrl);
                    if (listJson == null)
                    {
                        // author 필터가 서버에서 거절되는 경우 경로만으로 재시도
                        listUrl = collectionUrl
                            + "/_apis/tfvc/changesets"
                            + "?searchCriteria.itemPath=" + Uri.EscapeDataString(serverPath)
                            + "&$top=" + recentCount
                            + "&api-version=4.1";
                        listJson = GetString(client, listUrl);
                    }

                    if (listJson == null)
                        return new CollectResult { Success = false, Error = "changesets HTTP failed" };

                    var root = JObject.Parse(listJson);
                    var values = root["value"] as JArray;
                    if (values == null || values.Count == 0)
                        return new CollectResult { Success = false, Error = "no changesets" };

                    var changesets = new List<object>();
                    foreach (var item in values.Take(recentCount))
                    {
                        int changesetId = item.Value<int?>("changesetId") ?? 0;
                        if (changesetId <= 0)
                            continue;

                        string checkedInAt = FormatTfsDate(item["createdDate"]);
                        string comment = item.Value<string>("comment") ?? string.Empty;
                        var authorObj = item["author"] as JObject;
                        string authorName = authorObj != null ? (authorObj.Value<string>("displayName") ?? string.Empty) : string.Empty;
                        string authorId = authorObj != null
                            ? (authorObj.Value<string>("uniqueName") ?? authorObj.Value<string>("displayName") ?? string.Empty)
                            : string.Empty;

                        var changedFiles = LoadChangedFiles(client, collectionUrl, changesetId);

                        changesets.Add(new Dictionary<string, object>
                        {
                            { "changesetId", changesetId },
                            { "authorName", authorName },
                            { "authorId", authorId },
                            { "checkedInAt", checkedInAt },
                            { "comment", comment },
                            { "changedFileCount", changedFiles.Count },
                            { "changedFiles", changedFiles }
                        });
                    }

                    if (changesets.Count == 0)
                        return new CollectResult { Success = false, Error = "no valid changesets" };

                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string deliveryId = Guid.NewGuid().ToString("N");
                    string machine = Environment.MachineName;
                    string userId = Environment.UserDomainName + "\\" + Environment.UserName;

                    var payload = new Dictionary<string, object>
                    {
                        { "type", ExpectedType },
                        { "schemaVersion", 1 },
                        { "success", true },
                        { "errorCode", null },
                        { "message", null },
                        { "collectionUrl", collectionUrl },
                        { "serverPath", serverPath },
                        { "queryMode", "RECENT_COUNT" },
                        { "sessionStartAt", null },
                        { "sessionEndAt", null },
                        { "sessionToken", null },
                        { "remoteComputerName", machine },
                        { "computerName", machine },
                        { "sourceClientName", Environment.GetEnvironmentVariable("CLIENTNAME") },
                        { "collectedAt", now },
                        { "requestId", null },
                        { "deliveryId", deliveryId },
                        { "deliveryMode", "DISCONNECT" },
                        { "deliverySentAt", now },
                        { "authorizedUserId", userId },
                        { "returnedItemCount", changesets.Count },
                        { "changesets", changesets }
                    };

                    string json = JsonConvert.SerializeObject(payload);
                    return new CollectResult
                    {
                        Success = true,
                        JsonBody = json,
                        ClipboardText = ClipboardPrefix + json
                    };
                }
            }
            catch (Exception ex)
            {
                return new CollectResult { Success = false, Error = ex.Message };
            }
        }

        private static List<object> LoadChangedFiles(HttpClient client, string collectionUrl, int changesetId)
        {
            var files = new List<object>();
            try
            {
                string url = collectionUrl
                    + "/_apis/tfvc/changesets/" + changesetId
                    + "/changes?$top=200&api-version=4.1";
                string json = GetString(client, url);
                if (json == null)
                    return files;

                var root = JObject.Parse(json);
                var values = root["value"] as JArray;
                if (values == null)
                    return files;

                foreach (var ch in values)
                {
                    var item = ch["item"] as JObject;
                    if (item == null)
                        continue;

                    string path = item.Value<string>("path") ?? string.Empty;
                    string fileName = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path.Replace('/', '\\'));
                    string changeType = ch.Value<string>("changeType") ?? string.Empty;
                    bool isFolder = item.Value<bool?>("isFolder") == true;

                    files.Add(new Dictionary<string, object>
                    {
                        { "changeType", NormalizeChangeType(changeType) },
                        { "itemType", isFolder ? "folder" : "file" },
                        { "fileName", fileName },
                        { "path", path },
                        { "version", item.Value<string>("version") }
                    });
                }
            }
            catch
            {
            }

            return files;
        }

        private static string NormalizeChangeType(string changeType)
        {
            if (string.IsNullOrWhiteSpace(changeType))
                return "edit";
            string c = changeType.Trim().ToLowerInvariant();
            if (c.Contains("add") || c.Contains("branch"))
                return "add";
            if (c.Contains("delete"))
                return "delete";
            if (c.Contains("rename"))
                return "rename";
            return "edit";
        }

        private static string FormatTfsDate(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            DateTime dt;
            if (token.Type == JTokenType.Date)
            {
                dt = token.Value<DateTime>();
                return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            }

            string raw = token.ToString();
            if (DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static string GetString(HttpClient client, string url)
        {
            try
            {
                using (var response = client.GetAsync(url).GetAwaiter().GetResult())
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
