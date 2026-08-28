using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GBCWorkHub.SessionAgent
{
    internal static class Program
    {
        // ----- RC Team Explorer 기준 (필요 시 여기만 수정) -----
        // RC 원격망: rch-tfs-01 / BESTCare_RCHSP_20170628
        private const string TfsCollectionUrl = "http://rch-tfs-01:8080/tfs/BESTCare_RCHSP_20170628";
        // Source Control 루트. 없으면 $/ 로 폴백
        private const string TfsServerPath = "$/HISSolutions";
        // 세션 구간 내 체크인 전부 (안전상한)
        private const int TfsMaxChangesets = 100;
        // 세션 구간 0건일 때: 오늘(로컬 자정~현재) 동일 계정 체크인 폴백 상한
        private const int TfsFallbackTodayMax = 50;
        private const string BaseDirectory = @"C:\GBCWorkHub";

        private const string RdpPrefix = "GBCWORKHUB::";
        private const string TfsPrefix = "GBCWORKHUB_TFS::";
        private const string SyncRequestPrefix = "GBCWORKHUB_TFS_SYNC_REQUEST::";
        private const string AckPrefix = "GBCWORKHUB_ACK::";
        private const string PendingTfsFileName = "PendingTfsRecent.json";
        private const string SessionStartedFileName = "SessionStartedUtc.txt";

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                string eventType = args.Length > 0 ? (args[0] ?? "test").Trim() : "test";
                string logDirectory = Path.Combine(BaseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                Directory.CreateDirectory(BaseDirectory);

                string userName = Environment.UserDomainName + "\\" + Environment.UserName;
                string logLine = string.Join(
                    " | ",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    userName,
                    Environment.MachineName,
                    eventType);

                File.AppendAllText(
                    Path.Combine(logDirectory, "SessionTest.log"),
                    logLine + Environment.NewLine,
                    Encoding.UTF8);

                string action = eventType.ToLowerInvariant();
                if (action == "connect")
                    HandleConnect(logDirectory);
                else if (action == "disconnect")
                    HandleDisconnect(logDirectory);
                else
                    TryCopyToClipboard(logLine);
            }
            catch (Exception ex)
            {
                TryWriteErrorLog(ex);
            }
        }

        /// <summary>
        /// 1) CONNECT 상태 전송
        /// 2) SYNC_REQUEST(가져오기 팝업)를 읽으면 Pending/수집 TFS 전송. 없으면 CONNECT만.
        /// </summary>
        private static void HandleConnect(string logDirectory)
        {
            string pendingPath = Path.Combine(BaseDirectory, PendingTfsFileName);
            bool hasPending = File.Exists(pendingPath);

            // CONNECT 쓰기 전에 SYNC_REQUEST가 이미 있으면 먼저 확보 (덮어쓰기 방지)
            string requestId = null;
            DateTime? syncStartedUtc = null;
            DateTime? syncEndedUtc = null;
            bool hasSyncRequest = TryReadSyncRequest(out requestId, out syncStartedUtc, out syncEndedUtc);
            if (!hasSyncRequest)
            {
                for (int i = 0; i < 10 && !hasSyncRequest; i++)
                {
                    Thread.Sleep(300);
                    hasSyncRequest = TryReadSyncRequest(out requestId, out syncStartedUtc, out syncEndedUtc);
                }
            }

            DateTime now = DateTime.Now;
            string connectJson = BuildRdpStatusJson(
                eventId: 21,
                triggerType: "RC_CONNECT",
                sessionRaw: "rdp-tcp#0 Active",

                
                isVerifiedDisconnect: null,
                now: now);

            TryWriteText(Path.Combine(BaseDirectory, "LastStatus.json"), connectJson);
            TryCopyToClipboard(RdpPrefix + connectJson);

            // 접속 후 로컬이 SYNC_REQUEST를 다시 쓸 수 있음 → 추가 대기
            if (!hasSyncRequest)
            {
                const int syncWaitSec = 60;
                AppendAgentLog(logDirectory,
                    "CONNECT_STATUS_SENT | wait SYNC_REQUEST up to " + syncWaitSec
                    + "s pending=" + hasPending);
                for (int i = 0; i < syncWaitSec && !hasSyncRequest; i++)
                {
                    Thread.Sleep(1000);
                    hasSyncRequest = TryReadSyncRequest(out requestId, out syncStartedUtc, out syncEndedUtc);
                }
            }

            if (!hasSyncRequest)
            {
                MarkSessionStarted(logDirectory);
                AppendAgentLog(logDirectory,
                    "CONNECT_DEFAULT | no SYNC_REQUEST"
                    + " pendingKept=" + hasPending
                    + " | tip=가져오기 팝업 SYNC_REQUEST가 원격 클립보드에 와야 TFS 전송");
                return;
            }

            AppendAgentLog(logDirectory,
                "CONNECT_SYNC_REQUEST_OK | requestId=" + (requestId ?? "-")
                + " pending=" + File.Exists(pendingPath));

            Thread.Sleep(1200);

            string tfsJson = null;
            string source = null;
            hasPending = File.Exists(pendingPath);

            if (hasPending)
            {
                try
                {
                    tfsJson = File.ReadAllText(pendingPath, Encoding.UTF8);
                    source = "pending_file";
                }
                catch (Exception ex)
                {
                    AppendAgentLog(logDirectory, "PENDING_READ_FAILED | " + ex.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(tfsJson))
            {
                string err;
                tfsJson = TryCollectTfsRecent(
                    requestId: requestId,
                    deliveryMode: "PENDING_RECONNECT",
                    sessionStartedUtc: syncStartedUtc,
                    sessionEndedUtc: syncEndedUtc,
                    error: out err);
                source = "fresh_collect";
                if (string.IsNullOrWhiteSpace(tfsJson))
                {
                    AppendAgentLog(logDirectory, "CONNECT_TFS_FAILED | " + (err ?? "unknown"));
                    return;
                }
            }
            else
            {
                tfsJson = PatchTfsPayloadForReconnect(tfsJson, requestId);
            }

            bool copied = TryCopyToClipboard(TfsPrefix + tfsJson);
            AppendAgentLog(logDirectory,
                "CONNECT_TFS_SENT | source=" + source
                + " requestId=" + (requestId ?? "-")
                + " clipboardOk=" + copied
                + " length=" + tfsJson.Length);

            if (copied)
                TryDeleteFile(pendingPath);
            else
                AppendAgentLog(logDirectory, "CONNECT_TFS_CLIPBOARD_FAIL | pending kept");

            MarkSessionStarted(logDirectory);
        }

        /// <summary>
        /// disconnect: TFS 수집 → Pending 파일 저장 + 가능하면 클립보드.
        /// (세션이 이미 끊기면 클립보드는 실패할 수 있음 → connect+SYNC_REQUEST 때 pending 전송)
        /// </summary>
        private static void HandleDisconnect(string logDirectory)
        {
            string tfsError;
            string tfsJson = TryCollectTfsRecent(
                requestId: null,
                deliveryMode: "DISCONNECT",
                sessionStartedUtc: null,
                sessionEndedUtc: null,
                error: out tfsError);

            if (!string.IsNullOrEmpty(tfsJson))
            {
                string pendingPath = Path.Combine(BaseDirectory, PendingTfsFileName);
                TryWriteText(pendingPath, tfsJson);
                TryWriteText(
                    Path.Combine(BaseDirectory, "LastStatus.json"),
                    "{\"type\":\"TFS_PENDING\",\"collectedAt\":\""
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"}");

                bool copied = TryCopyToClipboard(TfsPrefix + tfsJson);
                AppendAgentLog(logDirectory,
                    "DISCONNECT_TFS_OK | clipboardOk=" + copied + " pendingSaved=True length=" + tfsJson.Length);

                // 클립보드 실패해도 pending은 남김 — 다음 connect/SYNC 때 전송
                if (copied)
                    return;

                // 클립보드 실패 시 disconnect 상태도 한 번 더 시도 (점유 해제 신호)
            }
            else
            {
                AppendAgentLog(logDirectory, "TFS_COLLECT_FAILED | " + (tfsError ?? "unknown"));
            }

            DateTime now = DateTime.Now;
            string json = BuildRdpStatusJson(
                eventId: 24,
                triggerType: "RDP_DISCONNECT",
                sessionRaw: null,
                isVerifiedDisconnect: true,
                now: now);

            TryWriteText(Path.Combine(BaseDirectory, "LastStatus.json"), json);
            TryCopyToClipboard(RdpPrefix + json);
        }

        private static string BuildRdpStatusJson(
            int eventId,
            string triggerType,
            string sessionRaw,
            bool? isVerifiedDisconnect,
            DateTime now)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            AppendJson(sb, "type", "GBC_RDP_STATUS").Append(',');
            AppendJson(sb, "schemaVersion", 1).Append(',');
            AppendJson(sb, "computerName", Environment.MachineName).Append(',');
            AppendJson(sb, "windowsUser", Environment.UserDomainName + "\\" + Environment.UserName).Append(',');
            AppendJson(sb, "clientName", Environment.GetEnvironmentVariable("CLIENTNAME")).Append(',');
            AppendJson(sb, "collectedAt", now.ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
            AppendJson(sb, "latestEventId", eventId).Append(',');
            AppendJson(sb, "latestRecordId", now.Ticks).Append(',');
            AppendJson(sb, "sessionRaw", sessionRaw).Append(',');
            AppendJson(sb, "triggerType", triggerType).Append(',');
            if (isVerifiedDisconnect.HasValue)
                AppendJson(sb, "isVerifiedDisconnect", isVerifiedDisconnect.Value).Append(',');
            else
                sb.Append("\"isVerifiedDisconnect\":null,");
            sb.Append("\"events\":null");
            sb.Append('}');
            return sb.ToString();
        }

        private static string TryCollectTfsRecent(
            string requestId,
            string deliveryMode,
            DateTime? sessionStartedUtc,
            DateTime? sessionEndedUtc,
            out string error)
        {
            error = null;
            string logDirectory = Path.Combine(BaseDirectory, "Logs");
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                string collectionUrl = TfsCollectionUrl.Trim().TrimEnd('/');
                string author = Environment.UserDomainName + "\\" + Environment.UserName;

                DateTime fromUtc;
                DateTime toUtc;
                string windowSource;
                ResolveCollectWindow(sessionStartedUtc, sessionEndedUtc, out fromUtc, out toUtc, out windowSource);
                AppendAgentLog(logDirectory,
                    "TFS_COLLECT_START | url=" + collectionUrl
                    + " author=" + author
                    + " window=" + FormatUtcForApi(fromUtc) + "~" + FormatUtcForApi(toUtc)
                    + " source=" + windowSource);

                string httpDetail;
                string listUrl;
                string listJson = TryGetChangesetsJson(
                    collectionUrl, author, fromUtc, toUtc, out listUrl, out httpDetail);

                if (string.IsNullOrEmpty(listJson))
                {
                    // 진단: 컬렉션/프로젝트가 보이는지
                    string probeDetail;
                    string projectsJson = HttpGet(
                        collectionUrl + "/_apis/projects?api-version=1.0",
                        out probeDetail);
                    error = "changesets HTTP failed | author=" + author
                        + " | url=" + listUrl
                        + " | " + (httpDetail ?? "no detail")
                        + " | projectsProbe=" + (probeDetail ?? "-")
                        + (string.IsNullOrEmpty(projectsJson)
                            ? string.Empty
                            : " | projectsPreview=" + TrimForLog(projectsJson, 160));
                    return null;
                }

                var changesetBlocks = ExtractObjectArray(listJson, "value");
                var changesetJsonParts = new List<string>();
                AppendChangesetParts(
                    collectionUrl,
                    changesetBlocks,
                    author,
                    fromUtc,
                    toUtc,
                    true,
                    false,
                    TfsMaxChangesets,
                    logDirectory,
                    changesetJsonParts);

                string queryMode = "SESSION_WINDOW";
                DateTime todayFromUtc = DateTime.Today.ToUniversalTime();
                DateTime todayToUtc = DateTime.UtcNow.AddMinutes(1);
                if (changesetJsonParts.Count == 0)
                {
                    AppendAgentLog(logDirectory,
                        "TFS_COLLECT_EMPTY_WINDOW | fallback=TODAY_FALLBACK"
                        + " window=" + FormatUtcForApi(todayFromUtc) + "~" + FormatUtcForApi(todayToUtc)
                        + " localDay=" + DateTime.Today.ToString("yyyy-MM-dd"));
                    string fallbackDetail;
                    string fallbackUrl;
                    string fallbackJson = TryGetTodayChangesetsJson(
                        collectionUrl, author, todayFromUtc, todayToUtc, out fallbackUrl, out fallbackDetail);
                    if (!string.IsNullOrEmpty(fallbackJson))
                    {
                        var fallbackBlocks = ExtractObjectArray(fallbackJson, "value");
                        AppendChangesetParts(
                            collectionUrl,
                            fallbackBlocks,
                            author,
                            todayFromUtc,
                            todayToUtc,
                            true,
                            true,
                            TfsFallbackTodayMax,
                            logDirectory,
                            changesetJsonParts);
                        if (changesetJsonParts.Count > 0)
                            queryMode = "TODAY_FALLBACK";
                        AppendAgentLog(logDirectory,
                            "TFS_FALLBACK_DONE | count=" + changesetJsonParts.Count
                            + " mode=TODAY_FALLBACK url=" + (fallbackUrl ?? "-"));
                    }
                    else
                    {
                        AppendAgentLog(logDirectory,
                            "TFS_FALLBACK_FAILED | " + (fallbackDetail ?? "-")
                            + " url=" + (fallbackUrl ?? "-"));
                    }
                }

                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string sessionStartLocal = string.Equals(queryMode, "TODAY_FALLBACK", StringComparison.Ordinal)
                    ? DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss")
                    : fromUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string sessionEndLocal = string.Equals(queryMode, "TODAY_FALLBACK", StringComparison.Ordinal)
                    ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    : toUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string machine = Environment.MachineName;
                string userId = Environment.UserDomainName + "\\" + Environment.UserName;

                AppendAgentLog(logDirectory,
                    "TFS_COLLECT_DONE | count=" + changesetJsonParts.Count
                    + " mode=" + queryMode
                    + " window=" + sessionStartLocal + "~" + sessionEndLocal);

                var sb = new StringBuilder(4096);
                sb.Append('{');
                AppendJson(sb, "type", "GBC_TFS_RECENT_CHANGESETS").Append(',');
                AppendJson(sb, "schemaVersion", 1).Append(',');
                AppendJson(sb, "success", true).Append(',');
                sb.Append("\"errorCode\":null,\"message\":null,");
                AppendJson(sb, "collectionUrl", collectionUrl).Append(',');
                AppendJson(sb, "serverPath", TfsServerPath).Append(',');
                AppendJson(sb, "queryMode", queryMode).Append(',');
                AppendJson(sb, "sessionStartAt", sessionStartLocal).Append(',');
                AppendJson(sb, "sessionEndAt", sessionEndLocal).Append(',');
                sb.Append("\"sessionToken\":null,");
                AppendJson(sb, "remoteComputerName", machine).Append(',');
                AppendJson(sb, "computerName", machine).Append(',');
                AppendJson(sb, "sourceClientName", Environment.GetEnvironmentVariable("CLIENTNAME")).Append(',');
                AppendJson(sb, "collectedAt", now).Append(',');
                if (string.IsNullOrWhiteSpace(requestId))
                    sb.Append("\"requestId\":null,");
                else
                    AppendJson(sb, "requestId", requestId).Append(',');
                AppendJson(sb, "deliveryId", Guid.NewGuid().ToString("N")).Append(',');
                AppendJson(sb, "deliveryMode",
                    string.IsNullOrWhiteSpace(deliveryMode) ? "DISCONNECT" : deliveryMode).Append(',');
                AppendJson(sb, "deliverySentAt", now).Append(',');
                AppendJson(sb, "authorizedUserId", userId).Append(',');
                AppendJson(sb, "returnedItemCount", changesetJsonParts.Count).Append(',');
                sb.Append("\"changesets\":[");
                for (int i = 0; i < changesetJsonParts.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(changesetJsonParts[i]);
                }
                sb.Append("]}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static void MarkSessionStarted(string logDirectory)
        {
            try
            {
                string path = Path.Combine(BaseDirectory, SessionStartedFileName);
                string utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                File.WriteAllText(path, utc, Encoding.UTF8);
                AppendAgentLog(logDirectory, "SESSION_STARTED_MARKED | " + utc);
            }
            catch (Exception ex)
            {
                AppendAgentLog(logDirectory, "SESSION_STARTED_MARK_FAILED | " + ex.Message);
            }
        }

        private static bool TryReadSessionStartedUtc(out DateTime startedUtc)
        {
            startedUtc = default(DateTime);
            try
            {
                string path = Path.Combine(BaseDirectory, SessionStartedFileName);
                if (!File.Exists(path))
                    return false;
                string text = (File.ReadAllText(path) ?? string.Empty).Trim();
                return TryParseUtc(text, out startedUtc);
            }
            catch
            {
                return false;
            }
        }

        private static void ResolveCollectWindow(
            DateTime? requestStartedUtc,
            DateTime? requestEndedUtc,
            out DateTime fromUtc,
            out DateTime toUtc,
            out string source)
        {
            toUtc = DateTime.UtcNow;
            fromUtc = toUtc.AddHours(-8);
            source = "fallback_8h";

            if (requestStartedUtc.HasValue)
            {
                fromUtc = requestStartedUtc.Value.ToUniversalTime();
                toUtc = requestEndedUtc.HasValue
                    ? requestEndedUtc.Value.ToUniversalTime()
                    : DateTime.UtcNow;
                source = "sync_request";
            }
            else if (TryReadSessionStartedUtc(out fromUtc))
            {
                toUtc = DateTime.UtcNow;
                source = "session_file";
            }

            if (toUtc < fromUtc)
            {
                DateTime tmp = fromUtc;
                fromUtc = toUtc;
                toUtc = tmp;
            }

            fromUtc = fromUtc.AddMinutes(-1);
            toUtc = toUtc.AddMinutes(1);
        }

        private static bool IsCreatedDateInWindow(string createdDateRaw, DateTime fromUtc, DateTime toUtc)
        {
            DateTime createdUtc;
            if (!TryParseUtc(createdDateRaw, out createdUtc))
                return false;
            return createdUtc >= fromUtc && createdUtc <= toUtc;
        }

        private static bool IsCreatedOnLocalToday(string createdDateRaw)
        {
            DateTime createdUtc;
            if (!TryParseUtc(createdDateRaw, out createdUtc))
                return false;
            return createdUtc.ToLocalTime().Date == DateTime.Today;
        }

        private static bool TryParseUtc(string raw, out DateTime utc)
        {
            utc = default(DateTime);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            DateTime dt;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out dt)
                && !DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal, out dt))
                return false;

            utc = dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime()
                : dt.ToUniversalTime();
            return true;
        }

        private static string FormatUtcForApi(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        private static bool TryReadSyncRequest(
            out string requestId,
            out DateTime? sessionStartedUtc,
            out DateTime? sessionEndedUtc)
        {
            requestId = null;
            sessionStartedUtc = null;
            sessionEndedUtc = null;
            try
            {
                if (!Clipboard.ContainsText())
                    return false;
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                text = text.TrimStart();
                if (!text.StartsWith(SyncRequestPrefix, StringComparison.Ordinal))
                    return false;
                string json = text.Substring(SyncRequestPrefix.Length).Trim();
                requestId = ExtractString(json, "requestId");
                DateTime started;
                DateTime ended;
                if (TryParseUtc(ExtractString(json, "sessionStartedAtUtc"), out started))
                    sessionStartedUtc = started;
                if (TryParseUtc(ExtractString(json, "sessionEndedAtUtc"), out ended))
                    sessionEndedUtc = ended;
                return !string.IsNullOrWhiteSpace(requestId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>pending JSON에 requestId / deliveryMode=PENDING_RECONNECT 주입</summary>
        private static string PatchTfsPayloadForReconnect(string tfsJson, string requestId)
        {
            if (string.IsNullOrWhiteSpace(tfsJson))
                return tfsJson;

            string json = tfsJson.Trim();
            // deliveryMode 교체
            json = ReplaceJsonStringField(json, "deliveryMode", "PENDING_RECONNECT");
            if (!string.IsNullOrWhiteSpace(requestId))
                json = ReplaceJsonStringField(json, "requestId", requestId);

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            json = ReplaceJsonStringField(json, "deliverySentAt", now);
            json = ReplaceJsonStringField(json, "collectedAt", now);
            return json;
        }

        private static string ReplaceJsonStringField(string json, string fieldName, string value)
        {
            string key = "\"" + fieldName + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
            {
                // 필드 없으면 맨 앞 { 다음에 삽입
                int brace = json.IndexOf('{');
                if (brace < 0)
                    return json;
                return json.Substring(0, brace + 1)
                    + "\"" + Escape(fieldName) + "\":\"" + Escape(value) + "\","
                    + json.Substring(brace + 1);
            }

            int colon = json.IndexOf(':', i + key.Length);
            if (colon < 0)
                return json;
            int valueStart = colon + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            int valueEnd;
            if (valueStart < json.Length && json[valueStart] == '"')
            {
                valueEnd = valueStart + 1;
                bool esc = false;
                while (valueEnd < json.Length)
                {
                    char c = json[valueEnd];
                    if (esc)
                        esc = false;
                    else if (c == '\\')
                        esc = true;
                    else if (c == '"')
                    {
                        valueEnd++;
                        break;
                    }
                    valueEnd++;
                }
            }
            else if (valueStart + 3 < json.Length
                && string.Compare(json, valueStart, "null", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                valueEnd = valueStart + 4;
            }
            else
            {
                valueEnd = valueStart;
                while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}')
                    valueEnd++;
            }

            return json.Substring(0, valueStart)
                + "\"" + Escape(value) + "\""
                + json.Substring(valueEnd);
        }

        private static void AppendAgentLog(string logDirectory, string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(logDirectory, "SessionTest.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + " | " + message
                    + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static string TryGetChangesetChangesJson(
            string collectionUrl,
            int changesetId,
            out string usedUrl,
            out string lastDetail)
        {
            usedUrl = null;
            lastDetail = null;
            string root = collectionUrl.TrimEnd('/') + "/_apis/tfvc";
            string id = changesetId.ToString(CultureInfo.InvariantCulture);

            var attempts = new[]
            {
                root + "/changesets/" + id + "/changes?%24top=200&api-version=4.1",
                root + "/changesets/" + id + "/changes?%24top=200&api-version=1.0",
                root + "/changesets/" + id + "/changes?$top=200&api-version=4.1",
                root + "/changesets/" + id + "/changes?$top=200&api-version=1.0"
            };

            for (int i = 0; i < attempts.Length; i++)
            {
                usedUrl = attempts[i];
                string json = HttpGet(attempts[i], out lastDetail);
                if (string.IsNullOrEmpty(json))
                    continue;
                if (json.IndexOf("\"value\"", StringComparison.Ordinal) >= 0)
                    return json;
            }

            return null;
        }

        private static List<string> BuildChangedFilesJson(string changesJson)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(changesJson))
                return files;

            foreach (string ch in ExtractObjectArray(changesJson, "value"))
            {
                string item = ExtractObject(ch, "item");
                if (string.IsNullOrEmpty(item))
                    continue;

                string path = ExtractString(item, "path");
                if (string.IsNullOrEmpty(path))
                    path = ExtractString(item, "serverItem");
                if (string.IsNullOrEmpty(path))
                    continue;

                string fileName = Path.GetFileName(path.Replace('/', '\\'));
                string changeType = NormalizeChangeType(ExtractJsonToken(ch, "changeType"));
                bool isFolder = ExtractBool(item, "isFolder");
                string version = ExtractJsonToken(item, "version");

                var sb = new StringBuilder(256);
                sb.Append('{');
                AppendJson(sb, "changeType", changeType).Append(',');
                AppendJson(sb, "itemType", isFolder ? "folder" : "file").Append(',');
                AppendJson(sb, "fileName", fileName).Append(',');
                AppendJson(sb, "path", path).Append(',');
                AppendJson(sb, "version", version);
                sb.Append('}');
                files.Add(sb.ToString());
            }

            return files;
        }

        private static string NormalizeChangeType(string changeType)
        {
            if (string.IsNullOrWhiteSpace(changeType))
                return "edit";
            string c = changeType.Trim().ToLowerInvariant();

            int flags;
            if (int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
            {
                if ((flags & 16) != 0) return "delete";
                if ((flags & 8) != 0) return "rename";
                if ((flags & 1) != 0) return "add";
                return "edit";
            }

            if (c.IndexOf("add", StringComparison.Ordinal) >= 0
                || c.IndexOf("branch", StringComparison.Ordinal) >= 0)
                return "add";
            if (c.IndexOf("delete", StringComparison.Ordinal) >= 0)
                return "delete";
            if (c.IndexOf("rename", StringComparison.Ordinal) >= 0)
                return "rename";
            return "edit";
        }

        private static string ExtractJsonToken(string json, string name)
        {
            string asString = ExtractString(json, name);
            if (!string.IsNullOrEmpty(asString))
                return asString;

            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return string.Empty;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0)
                return string.Empty;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;
            if (i >= json.Length || json[i] == '"' || json[i] == 'n' || json[i] == '{' || json[i] == '[')
                return string.Empty;

            int start = i;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c))
                    break;
                i++;
            }
            return json.Substring(start, i - start).Trim();
        }

        private static string FormatIsoDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 구형 TFS는 api-version / itemPath / $top 인코딩에 민감해서 여러 조합을 시도한다.
        /// </summary>
        private static string TryGetChangesetsJson(
            string collectionUrl,
            string author,
            DateTime fromUtc,
            DateTime toUtc,
            out string usedUrl,
            out string lastDetail)
        {
            return TryGetChangesetsJsonCore(
                collectionUrl, author, fromUtc, toUtc, TfsMaxChangesets, true,
                out usedUrl, out lastDetail);
        }

        private static string TryGetTodayChangesetsJson(
            string collectionUrl,
            string author,
            DateTime fromUtc,
            DateTime toUtc,
            out string usedUrl,
            out string lastDetail)
        {
            usedUrl = null;
            lastDetail = null;
            if (string.IsNullOrWhiteSpace(author))
                return null;

            DateTime? from = fromUtc;
            DateTime? to = toUtc;
            int top = TfsFallbackTodayMax;
            string[] apiVersions = { "1.0", "2.0", "3.0", "4.1" };
            string[] itemPaths = { TfsServerPath, "$/", null };

            foreach (string apiVersion in apiVersions)
            {
                foreach (string itemPath in itemPaths)
                {
                    string url = BuildChangesetsUrl(
                        collectionUrl, itemPath, author, from, to, top, apiVersion);
                    string json = HttpGet(url, out lastDetail);
                    usedUrl = url;
                    if (!string.IsNullOrEmpty(json))
                        return json;
                }
            }

            return null;
        }

        private static string TryGetChangesetsJsonCore(
            string collectionUrl,
            string author,
            DateTime fromUtc,
            DateTime toUtc,
            int top,
            bool requireDates,
            out string usedUrl,
            out string lastDetail)
        {
            usedUrl = null;
            lastDetail = null;
            DateTime? from = requireDates ? (DateTime?)fromUtc : null;
            DateTime? to = requireDates ? (DateTime?)toUtc : null;

            string[] apiVersions = { "1.0", "2.0", "3.0", "4.1" };
            string[] itemPaths =
            {
                TfsServerPath,
                "$/",
                null // itemPath 없음
            };

            foreach (string apiVersion in apiVersions)
            {
                foreach (string itemPath in itemPaths)
                {
                    if (!string.IsNullOrEmpty(author))
                    {
                        string url = BuildChangesetsUrl(
                            collectionUrl, itemPath, author, from, to, top, apiVersion);
                        string json = HttpGet(url, out lastDetail);
                        usedUrl = url;
                        if (!string.IsNullOrEmpty(json))
                            return json;
                    }

                    {
                        string url = BuildChangesetsUrl(
                            collectionUrl, itemPath, null, from, to, top, apiVersion);
                        string json = HttpGet(url, out lastDetail);
                        usedUrl = url;
                        if (!string.IsNullOrEmpty(json))
                            return json;
                    }
                }
            }

            return null;
        }

        private static void AppendChangesetParts(
            string collectionUrl,
            List<string> changesetBlocks,
            string expectedAuthor,
            DateTime fromUtc,
            DateTime toUtc,
            bool requireWindow,
            bool requireLocalToday,
            int maxCount,
            string logDirectory,
            List<string> changesetJsonParts)
        {
            if (changesetBlocks == null || changesetJsonParts == null)
                return;

            int taken = 0;
            foreach (string block in changesetBlocks)
            {
                if (taken >= maxCount)
                    break;

                int changesetId = ExtractInt(block, "changesetId");
                if (changesetId <= 0)
                    continue;

                string comment = ExtractString(block, "comment");
                string createdDate = ExtractString(block, "createdDate");
                if (requireWindow && !IsCreatedDateInWindow(createdDate, fromUtc, toUtc))
                    continue;
                if (requireLocalToday && !IsCreatedOnLocalToday(createdDate))
                    continue;

                string authorName = string.Empty;
                string authorId = string.Empty;
                string authorBlock = ExtractObject(block, "author");
                if (!string.IsNullOrEmpty(authorBlock))
                {
                    authorName = ExtractString(authorBlock, "displayName");
                    authorId = ExtractString(authorBlock, "uniqueName");
                    if (string.IsNullOrEmpty(authorId))
                        authorId = authorName;
                }

                if (!IsMatchingQueryAuthor(expectedAuthor, authorId, authorName))
                    continue;

                string checkedInAt = FormatIsoDate(createdDate);
                string changesUrl;
                string changesDetail;
                string changesJson = TryGetChangesetChangesJson(
                    collectionUrl, changesetId, out changesUrl, out changesDetail);
                var files = BuildChangedFilesJson(changesJson);
                if (files.Count == 0)
                {
                    AppendAgentLog(logDirectory,
                        "TFS_CHANGES_EMPTY | cs=" + changesetId
                        + " url=" + (changesUrl ?? "-")
                        + " detail=" + (changesDetail ?? "-")
                        + " bodyLen=" + (changesJson != null ? changesJson.Length : 0));
                }
                else
                {
                    AppendAgentLog(logDirectory,
                        "TFS_CHANGES_OK | cs=" + changesetId + " files=" + files.Count);
                }

                var cs = new StringBuilder(1024);
                cs.Append('{');
                AppendJson(cs, "changesetId", changesetId).Append(',');
                AppendJson(cs, "authorName", authorName).Append(',');
                AppendJson(cs, "authorId", authorId).Append(',');
                AppendJson(cs, "checkedInAt", checkedInAt).Append(',');
                AppendJson(cs, "comment", comment).Append(',');
                AppendJson(cs, "changedFileCount", files.Count).Append(',');
                cs.Append("\"changedFiles\":[");
                for (int i = 0; i < files.Count; i++)
                {
                    if (i > 0) cs.Append(',');
                    cs.Append(files[i]);
                }
                cs.Append("]}");
                changesetJsonParts.Add(cs.ToString());
                taken++;
            }
        }

        private static bool IsMatchingQueryAuthor(string expectedAuthor, string authorId, string authorName)
        {
            if (string.IsNullOrWhiteSpace(expectedAuthor))
                return true;

            string expected = expectedAuthor.Trim();
            if (!string.IsNullOrWhiteSpace(authorId)
                && string.Equals(authorId.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(authorName)
                && string.Equals(authorName.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return true;

            string expectedUser = expected;
            int slash = expected.LastIndexOf('\\');
            if (slash >= 0 && slash < expected.Length - 1)
                expectedUser = expected.Substring(slash + 1);
            if (!string.IsNullOrWhiteSpace(authorId))
            {
                string id = authorId.Trim();
                int idSlash = id.LastIndexOf('\\');
                string idUser = idSlash >= 0 && idSlash < id.Length - 1 ? id.Substring(idSlash + 1) : id;
                if (string.Equals(idUser, expectedUser, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (!string.IsNullOrWhiteSpace(authorName)
                && string.Equals(authorName.Trim(), expectedUser, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string BuildChangesetsUrl(
            string collectionUrl,
            string itemPath,
            string author,
            DateTime? fromUtc,
            DateTime? toUtc,
            int top,
            string apiVersion)
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(itemPath))
                q.Add("searchCriteria.itemPath=" + Uri.EscapeDataString(itemPath));
            if (!string.IsNullOrEmpty(author))
                q.Add("searchCriteria.author=" + Uri.EscapeDataString(author));
            if (fromUtc.HasValue)
                q.Add("searchCriteria.fromDate=" + Uri.EscapeDataString(FormatUtcForApi(fromUtc.Value)));
            if (toUtc.HasValue)
                q.Add("searchCriteria.toDate=" + Uri.EscapeDataString(FormatUtcForApi(toUtc.Value)));
            if (top <= 0)
                top = TfsMaxChangesets;
            // $top 의 $ 를 반드시 인코딩 (%24top). 일부 IIS/TFS에서 미인코딩 시 404
            q.Add("%24top=" + top.ToString(CultureInfo.InvariantCulture));
            q.Add("api-version=" + apiVersion);
            return collectionUrl + "/_apis/tfvc/changesets?" + string.Join("&", q.ToArray());
        }

        private static string TrimForLog(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string oneLine = text.Replace('\r', ' ').Replace('\n', ' ');
            if (oneLine.Length <= maxLen)
                return oneLine;
            return oneLine.Substring(0, maxLen);
        }

        private static string HttpGet(string url, out string detail)
        {
            detail = null;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Accept = "application/json";
                request.UseDefaultCredentials = true;
                request.PreAuthenticate = true;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    detail = "HTTP " + ((int)response.StatusCode);
                    return reader.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                string body = null;
                int status = 0;
                try
                {
                    var resp = wex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        status = (int)resp.StatusCode;
                        using (var stream = resp.GetResponseStream())
                        using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                            body = reader.ReadToEnd();
                    }
                }
                catch
                {
                }

                if (body != null && body.Length > 180)
                    body = body.Substring(0, 180).Replace('\r', ' ').Replace('\n', ' ');

                detail = "WebException status=" + wex.Status
                    + " http=" + status
                    + " msg=" + (wex.Message ?? string.Empty)
                    + (string.IsNullOrEmpty(body) ? string.Empty : " body=" + body);
                return null;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        // ---------- 초경량 JSON helpers (외부 DLL 없음) ----------

        private static StringBuilder AppendJson(StringBuilder sb, string name, string value)
        {
            sb.Append('"').Append(Escape(name)).Append("\":");
            if (value == null)
                sb.Append("null");
            else
                sb.Append('"').Append(Escape(value)).Append('"');
            return sb;
        }

        private static StringBuilder AppendJson(StringBuilder sb, string name, long value)
        {
            sb.Append('"').Append(Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return sb;
        }

        private static StringBuilder AppendJson(StringBuilder sb, string name, bool value)
        {
            sb.Append('"').Append(Escape(name)).Append("\":").Append(value ? "true" : "false");
            return sb;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static int ExtractInt(string json, string name)
        {
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return 0;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0)
                return 0;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'))
                i++;
            int start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-'))
                i++;
            int n;
            if (int.TryParse(json.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            return 0;
        }

        private static bool ExtractBool(string json, string name)
        {
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return false;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0)
                return false;
            string tail = json.Substring(i + 1).TrimStart();
            return tail.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractString(string json, string name)
        {
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return string.Empty;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0)
                return string.Empty;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n'))
                i++;
            if (i >= json.Length)
                return string.Empty;
            if (json[i] == 'n') // null
                return string.Empty;
            if (json[i] != '"')
                return string.Empty;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\')
                {
                    if (i >= json.Length)
                        break;
                    char n = json[i++];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 3 < json.Length)
                            {
                                int code;
                                if (int.TryParse(json.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default:
                            sb.Append(n);
                            break;
                    }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ExtractObject(string json, string name)
        {
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return null;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0)
                return null;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;
            if (i >= json.Length || json[i] != '{')
                return null;
            return SliceBalanced(json, i, '{', '}');
        }

        private static List<string> ExtractObjectArray(string json, string name)
        {
            var list = new List<string>();
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return list;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0)
                return list;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;
            if (i >= json.Length || json[i] != '[')
                return list;
            i++;
            while (i < json.Length)
            {
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ','))
                    i++;
                if (i >= json.Length || json[i] == ']')
                    break;
                if (json[i] != '{')
                    break;
                string obj = SliceBalanced(json, i, '{', '}');
                if (obj == null)
                    break;
                list.Add(obj);
                i += obj.Length;
            }
            return list;
        }

        private static string SliceBalanced(string s, int start, char open, char close)
        {
            if (start < 0 || start >= s.Length || s[start] != open)
                return null;
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (escape)
                        escape = false;
                    else if (c == '\\')
                        escape = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }
                if (c == open)
                    depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                        return s.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        // RDP 클립보드 = 제어채널+사용자 복붙 공유 → Set 전 백업, hold 후 동기 복원
        // (에이전트는 짧은 수명 프로세스라 ThreadPool 복원은 종료와 함께 유실됨 → 반드시 동기)
        private static string _userClipboardBackup;

        private static bool IsProtocolClipboardText(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.StartsWith("GBCWORKHUB", StringComparison.Ordinal);
        }

        private static bool TryCopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            // 사용자 일반 텍스트만 백업 (GBCWORKHUB* 는 백업하지 않음)
            try
            {
                if (Clipboard.ContainsText())
                {
                    string cur = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(cur) && !IsProtocolClipboardText(cur))
                        _userClipboardBackup = cur;
                }
            }
            catch
            {
            }

            bool written = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    written = true;
                    break;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
            if (!written)
                return false;

            // 상태: 짧게 유지 후 복원
            // TFS: 로컬 ACK(또는 타임아웃)까지 유지 후 복원.
            //      사용자가 그 사이 일반 텍스트를 복사하면 복원하지 않음.
            if (text.StartsWith(TfsPrefix, StringComparison.Ordinal))
                WaitAckThenRestoreUserClipboard();
            else
            {
                Thread.Sleep(1500);
                TryRestoreUserClipboard();
            }
            return true;
        }

        /// <summary>
        /// TFS 전달 완료(ACK) 또는 최대 대기 후 사용자 클립보드 복원.
        /// 중간에 사용자가 새 텍스트를 복사했으면 그 내용을 존중하고 복원하지 않음.
        /// </summary>
        private static void WaitAckThenRestoreUserClipboard()
        {
            const int minHoldMs = 2000;
            const int maxWaitMs = 12000;
            const int stepMs = 250;

            Thread.Sleep(minHoldMs);
            int waited = minHoldMs;

            while (waited < maxWaitMs)
            {
                string current = null;
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                }

                // 사용자가 중간에 복사함 → 보호(복원으로 덮지 않음)
                if (!string.IsNullOrEmpty(current) && !IsProtocolClipboardText(current))
                    return;

                // 로컬이 수신 확인(ACK) → 복원해도 안전
                if (!string.IsNullOrEmpty(current)
                    && current.StartsWith(AckPrefix, StringComparison.Ordinal))
                    break;

                Thread.Sleep(stepMs);
                waited += stepMs;
            }

            TryRestoreUserClipboard();
        }

        private static void TryRestoreUserClipboard()
        {
            string backup = _userClipboardBackup;
            // 백업 없으면 GBCWORKHUB* 고착 → 원격 붙여넣기 시 프로토콜만 나옴
            if (string.IsNullOrEmpty(backup))
            {
                TryClearProtocolClipboard();
                return;
            }

            try
            {
                string current = null;
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                }

                // 사용자가 이미 새 내용을 넣었으면 덮지 않음
                if (!string.IsNullOrEmpty(current)
                    && !IsProtocolClipboardText(current)
                    && !string.Equals(current, backup, StringComparison.Ordinal))
                    return;

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(backup);
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }

                TryClearProtocolClipboard();
            }
            catch
            {
            }
        }

        private static void TryClearProtocolClipboard()
        {
            try
            {
                string current = null;
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                }

                if (string.IsNullOrEmpty(current) || !IsProtocolClipboardText(current))
                    return;

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Clipboard.Clear();
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryWriteText(string path, string content)
        {
            try
            {
                if (content == null)
                    return;
                File.WriteAllText(path, content, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void TryWriteErrorLog(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(BaseDirectory);
                File.AppendAllText(
                    Path.Combine(BaseDirectory, "SessionAgentError.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + Environment.NewLine
                    + ex
                    + Environment.NewLine
                    + "--------------------------------"
                    + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
