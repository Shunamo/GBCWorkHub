using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GBCWorkHub.SessionAgent
{
    internal static class Program
    {
        // ----- RC Team Explorer (HO-BCARE-07). 다른 PC는 C:\GBCWorkHub\TfsSettings.txt -----
        // 브라우저: http://rch-tfs-01:8080/tfs/BESTCare_RCHSP_20170628/HIS.MS.CS.PH
        private const string TfsCollectionUrl = "http://rch-tfs-01:8080/tfs/BESTCare_RCHSP_20170628";
        private const string TfsServerPath = "$/HIS.MS.CS.PH";
        // TFS 전용 계정. RDP whoami(RCHSP\sukhoonyoon) 넣으면 TF30063.
        // Team Explorer 연결 캐시가 있으면 여기 비워 둬도 됨. git에 비밀번호 넣지 말 것.
        private const string TfsAuthUser = @"";
        private const string TfsAuthPassword = "";
        private const string TfsSettingsFileName = "TfsSettings.txt";
        private const string TfsPasswordFileName = "TfsPassword.txt";
        // REST(_apis) 404인 RC는 Team Explorer OM QueryHistory 사용 (Aurora와 동일)
        private const string TfsClientDllDefault =
            @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.TeamFoundation.Client\v4.0_12.0.0.0__b03f5f7f11d50a3a\Microsoft.TeamFoundation.Client.dll";
        private const string TfsVersionControlDllDefault =
            @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.TeamFoundation.VersionControl.Client\v4.0_12.0.0.0__b03f5f7f11d50a3a\Microsoft.TeamFoundation.VersionControl.Client.dll";
        // 세션 구간 내 체크인 전부 (안전상한)
        private const int TfsMaxChangesets = 100;
        // 세션 구간 0건일 때: 오늘(로컬 자정~현재) 동일 계정 체크인 폴백 상한
        private const int TfsFallbackTodayMax = 50;
        private const string BaseDirectory = @"C:\GBCWorkHub";

        private const string RdpPrefix = "GBCWORKHUB::";
        private const string TfsPrefix = "GBCWORKHUB_TFS::";
        private const string SyncRequestPrefix = "GBCWORKHUB_TFS_SYNC_REQUEST::";
        private const string AckPrefix = "GBCWORKHUB_ACK::";
        private const string SessionTokenPrefix = "GBCWORKHUB_SESSION_TOKEN::";
        private const string SessionResultPrefix = "GBCWORKHUB_SESSION_RESULT::";
        private const string PendingTfsFileName = "PendingTfsRecent.json";
        private const string SessionStartedFileName = "SessionStartedUtc.txt";

        private static string _resolvedCollectionUrl;
        private static string _resolvedServerPath;
        private static string _resolvedTfsUser;
        private static string _resolvedTfsPassword;
        private static bool _tfsSettingsLoaded;

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
        /// 1) SYNC가 없으면 CONNECT만 쓰고 복원. 있으면 CONNECT로 덮지 않음.
        /// 2) SYNC_REQUEST(가져오기)면 Pending/수집 TFS 전송.
        /// </summary>
        private static void HandleConnect(string logDirectory)
        {
            string pendingPath = Path.Combine(BaseDirectory, PendingTfsFileName);
            bool hasPending = File.Exists(pendingPath);

            string requestId = null;
            DateTime? syncStartedUtc = null;
            DateTime? syncEndedUtc = null;
            bool hasSyncRequest = TryReadTargetedSyncRequest(
                out requestId, out syncStartedUtc, out syncEndedUtc);

            DateTime now = DateTime.Now;
            string connectJson = BuildRdpStatusJson(
                eventId: 21,
                triggerType: "RC_CONNECT",
                sessionRaw: "rdp-tcp#0 Active",
                isVerifiedDisconnect: null,
                now: now);

            TryWriteText(Path.Combine(BaseDirectory, "LastStatus.json"), connectJson);

            if (!hasSyncRequest)
            {
                TryCopyToClipboard(RdpPrefix + connectJson);
                const int syncWaitSec = 50;
                AppendAgentLog(logDirectory,
                    "CONNECT_STATUS_SENT | restore then wait SYNC_REQUEST up to " + syncWaitSec
                    + "s pending=" + hasPending);
                for (int i = 0; i < syncWaitSec && !hasSyncRequest; i++)
                {
                    Thread.Sleep(1000);
                    hasSyncRequest = TryReadTargetedSyncRequest(
                        out requestId, out syncStartedUtc, out syncEndedUtc);
                }
            }
            else
            {
                AppendAgentLog(logDirectory,
                    "CONNECT_SYNC_ALREADY | skip CONNECT clipboard requestId=" + (requestId ?? "-"));
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
                + " pending=" + File.Exists(pendingPath)
                + " | keep SYNC_REQUEST until TFS");

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
                    TryCopyToClipboard(RdpPrefix + connectJson);
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

                string collectionUrl = GetTfsCollectionUrl();
                string serverPath = GetTfsServerPath();
                string author = Environment.UserDomainName + "\\" + Environment.UserName;

                DateTime fromUtc;
                DateTime toUtc;
                string windowSource;
                ResolveCollectWindow(sessionStartedUtc, sessionEndedUtc, out fromUtc, out toUtc, out windowSource);
                AppendAgentLog(logDirectory,
                    "TFS_COLLECT_START | mode=TFS_OM url=" + collectionUrl
                    + " path=" + serverPath
                    + " author=" + author
                    + " window=" + FormatUtcForApi(fromUtc) + "~" + FormatUtcForApi(toUtc)
                    + " source=" + windowSource);

                string omError;
                string authorizedUserId;
                var changesetJsonParts = new List<string>();
                bool omOk = TryCollectViaTfsOm(
                    collectionUrl,
                    serverPath,
                    fromUtc.ToLocalTime(),
                    toUtc.ToLocalTime(),
                    TfsMaxChangesets,
                    logDirectory,
                    changesetJsonParts,
                    out authorizedUserId,
                    out omError);

                string queryMode = "SESSION_WINDOW";
                if (omOk)
                {
                    if (changesetJsonParts.Count == 0)
                    {
                        AppendAgentLog(logDirectory,
                            "TFS_COLLECT_EMPTY_WINDOW | fallback=TODAY_FALLBACK"
                            + " localDay=" + DateTime.Today.ToString("yyyy-MM-dd"));
                        string fbErr;
                        string fbUser;
                        var fbParts = new List<string>();
                        bool fbOk = TryCollectViaTfsOm(
                            collectionUrl,
                            serverPath,
                            DateTime.Today,
                            DateTime.Now.AddMinutes(1),
                            TfsFallbackTodayMax,
                            logDirectory,
                            fbParts,
                            out fbUser,
                            out fbErr);
                        if (fbOk && fbParts.Count > 0)
                        {
                            changesetJsonParts.AddRange(fbParts);
                            queryMode = "TODAY_FALLBACK";
                            if (!string.IsNullOrWhiteSpace(fbUser))
                                authorizedUserId = fbUser;
                            AppendAgentLog(logDirectory,
                                "TFS_FALLBACK_DONE | count=" + changesetJsonParts.Count + " mode=TODAY_FALLBACK");
                        }
                        else
                        {
                            AppendAgentLog(logDirectory,
                                "TFS_FALLBACK_FAILED | " + (fbErr ?? "empty"));
                        }
                    }

                    if (string.IsNullOrWhiteSpace(authorizedUserId))
                        authorizedUserId = author;

                    string nowOm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string sessionStartOm = string.Equals(queryMode, "TODAY_FALLBACK", StringComparison.Ordinal)
                        ? DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss")
                        : fromUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    string sessionEndOm = string.Equals(queryMode, "TODAY_FALLBACK", StringComparison.Ordinal)
                        ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        : toUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    string machineOm = Environment.MachineName;

                    AppendAgentLog(logDirectory,
                        "TFS_COLLECT_DONE | count=" + changesetJsonParts.Count
                        + " mode=" + queryMode
                        + " author=" + authorizedUserId
                        + " window=" + sessionStartOm + "~" + sessionEndOm);

                    var sbOm = new StringBuilder(4096);
                    sbOm.Append('{');
                    AppendJson(sbOm, "type", "GBC_TFS_RECENT_CHANGESETS").Append(',');
                    AppendJson(sbOm, "schemaVersion", 1).Append(',');
                    AppendJson(sbOm, "success", true).Append(',');
                    sbOm.Append("\"errorCode\":null,\"message\":null,");
                    AppendJson(sbOm, "collectionUrl", collectionUrl).Append(',');
                    AppendJson(sbOm, "serverPath", serverPath).Append(',');
                    AppendJson(sbOm, "queryMode", queryMode).Append(',');
                    AppendJson(sbOm, "sessionStartAt", sessionStartOm).Append(',');
                    AppendJson(sbOm, "sessionEndAt", sessionEndOm).Append(',');
                    sbOm.Append("\"sessionToken\":null,");
                    AppendJson(sbOm, "remoteComputerName", machineOm).Append(',');
                    AppendJson(sbOm, "computerName", machineOm).Append(',');
                    AppendJson(sbOm, "sourceClientName", Environment.GetEnvironmentVariable("CLIENTNAME")).Append(',');
                    AppendJson(sbOm, "collectedAt", nowOm).Append(',');
                    if (string.IsNullOrWhiteSpace(requestId))
                        sbOm.Append("\"requestId\":null,");
                    else
                        AppendJson(sbOm, "requestId", requestId).Append(',');
                    AppendJson(sbOm, "deliveryId", Guid.NewGuid().ToString("N")).Append(',');
                    AppendJson(sbOm, "deliveryMode",
                        string.IsNullOrWhiteSpace(deliveryMode) ? "DISCONNECT" : deliveryMode).Append(',');
                    AppendJson(sbOm, "deliverySentAt", nowOm).Append(',');
                    AppendJson(sbOm, "authorizedUserId", authorizedUserId).Append(',');
                    AppendJson(sbOm, "returnedItemCount", changesetJsonParts.Count).Append(',');
                    sbOm.Append("\"changesets\":[");
                    for (int i = 0; i < changesetJsonParts.Count; i++)
                    {
                        if (i > 0) sbOm.Append(',');
                        sbOm.Append(changesetJsonParts[i]);
                    }
                    sbOm.Append("]}");
                    return sbOm.ToString();
                }

                AppendAgentLog(logDirectory, "TFS_OM_FAIL | fallback=REST | " + (omError ?? "unknown"));

                string httpDetail;
                string listUrl;
                string listJson = TryGetChangesetsJson(
                    collectionUrl, author, fromUtc, toUtc, out listUrl, out httpDetail);

                if (string.IsNullOrEmpty(listJson))
                {
                    string probeDetail;
                    string projectsJson = HttpGet(
                        collectionUrl + "/_apis/projects?api-version=1.0",
                        out probeDetail);
                    error = "TFS OM failed (" + (omError ?? "-") + ") and REST 404 | author=" + author
                        + " | url=" + listUrl
                        + " | " + (httpDetail ?? "no detail")
                        + " | projectsProbe=" + (probeDetail ?? "-")
                        + (string.IsNullOrEmpty(projectsJson)
                            ? string.Empty
                            : " | projectsPreview=" + TrimForLog(projectsJson, 160));
                    return null;
                }

                var changesetBlocks = ExtractObjectArray(listJson, "value");
                changesetJsonParts.Clear();
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

                queryMode = "SESSION_WINDOW";
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
                AppendJson(sb, "serverPath", serverPath).Append(',');
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

        private static string GetTfsCollectionUrl()
        {
            EnsureTfsSettingsLoaded();
            return _resolvedCollectionUrl;
        }

        private static string GetTfsServerPath()
        {
            EnsureTfsSettingsLoaded();
            return _resolvedServerPath;
        }

        private static void EnsureTfsSettingsLoaded()
        {
            if (_tfsSettingsLoaded)
                return;

            string collection = TfsCollectionUrl.Trim().TrimEnd('/');
            string serverPath = TfsServerPath;
            string user = null;
            string password = null;
            try
            {
                string file = Path.Combine(BaseDirectory, TfsSettingsFileName);
                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = (lines[i] ?? string.Empty).Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                            continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            string key = line.Substring(0, eq).Trim();
                            string value = line.Substring(eq + 1).Trim();
                            if (string.Equals(key, "CollectionUrl", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(key, "Url", StringComparison.OrdinalIgnoreCase))
                                collection = value;
                            else if (string.Equals(key, "ServerPath", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(key, "Project", StringComparison.OrdinalIgnoreCase))
                                serverPath = value;
                            else if (string.Equals(key, "User", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(key, "UserName", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(key, "Account", StringComparison.OrdinalIgnoreCase))
                                user = value;
                            else if (string.Equals(key, "Password", StringComparison.OrdinalIgnoreCase))
                                password = value;
                        }
                        else if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            collection = line;
                        }
                        else if (line.StartsWith("$/", StringComparison.Ordinal))
                        {
                            serverPath = line;
                        }
                    }
                }

                if (string.IsNullOrEmpty(password))
                {
                    string pwFile = Path.Combine(BaseDirectory, TfsPasswordFileName);
                    if (File.Exists(pwFile))
                        password = (File.ReadAllText(pwFile) ?? string.Empty).Trim();
                }
            }
            catch
            {
            }

            SplitWebProjectUrl(ref collection, ref serverPath);
            if (string.IsNullOrWhiteSpace(collection))
                collection = TfsCollectionUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(serverPath))
                serverPath = TfsServerPath;
            if (!serverPath.StartsWith("$/", StringComparison.Ordinal)
                && serverPath.IndexOf('/') < 0
                && serverPath.IndexOf('\\') < 0)
                serverPath = "$/" + serverPath.Trim();

            if (string.IsNullOrWhiteSpace(user))
                user = TfsAuthUser;
            if (string.IsNullOrEmpty(password))
                password = TfsAuthPassword;

            _resolvedCollectionUrl = collection.Trim().TrimEnd('/');
            _resolvedServerPath = serverPath.Trim();
            _resolvedTfsUser = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
            _resolvedTfsPassword = string.IsNullOrEmpty(password) ? null : password;
            _tfsSettingsLoaded = true;
        }

        private static NetworkCredential TryGetTfsNetworkCredential()
        {
            EnsureTfsSettingsLoaded();
            if (string.IsNullOrWhiteSpace(_resolvedTfsUser) || string.IsNullOrEmpty(_resolvedTfsPassword))
                return null;

            string domain = string.Empty;
            string name = _resolvedTfsUser;
            int slash = name.IndexOf('\\');
            if (slash > 0)
            {
                domain = name.Substring(0, slash);
                name = name.Substring(slash + 1);
            }
            else
            {
                int at = name.IndexOf('@');
                if (at > 0)
                    return new NetworkCredential(name, _resolvedTfsPassword);
            }
            return new NetworkCredential(name, _resolvedTfsPassword, domain);
        }

        /// <summary>
        /// 브라우저 주소 .../tfs/{컬렉션}/{프로젝트} 를 컬렉션 URL + $/프로젝트 로 나눈다.
        /// </summary>
        private static void SplitWebProjectUrl(ref string collectionUrl, ref string serverPath)
        {
            if (string.IsNullOrWhiteSpace(collectionUrl))
                return;
            try
            {
                var uri = new Uri(collectionUrl.Trim());
                string abs = uri.AbsolutePath.Trim('/');
                string[] parts = abs.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                int tfsAt = -1;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.Equals(parts[i], "tfs", StringComparison.OrdinalIgnoreCase))
                    {
                        tfsAt = i;
                        break;
                    }
                }
                if (tfsAt < 0 || tfsAt + 1 >= parts.Length)
                    return;
                if (tfsAt + 2 >= parts.Length)
                    return;

                string collectionName = parts[tfsAt + 1];
                var projectParts = new List<string>();
                for (int i = tfsAt + 2; i < parts.Length; i++)
                    projectParts.Add(parts[i]);
                string project = string.Join("/", projectParts.ToArray());
                if (string.IsNullOrWhiteSpace(project))
                    return;

                collectionUrl = uri.GetLeftPart(UriPartial.Authority) + "/tfs/" + collectionName;
                if (string.IsNullOrWhiteSpace(serverPath)
                    || string.Equals(serverPath, "$/HISSolutions", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(serverPath, "$/", StringComparison.Ordinal))
                    serverPath = "$/" + project;
            }
            catch
            {
            }
        }

        private static bool TryCollectViaTfsOm(
            string collectionUrl,
            string serverPath,
            DateTime fromLocal,
            DateTime toLocal,
            int maxCount,
            string logDirectory,
            List<string> changesetJsonParts,
            out string authorizedUserId,
            out string error)
        {
            authorizedUserId = null;
            error = null;
            object collection = null;
            try
            {
                Assembly clientAsm;
                Assembly vcAsm;
                string loadErr;
                if (!TryLoadTfsOmAssemblies(out clientAsm, out vcAsm, out loadErr))
                {
                    error = loadErr;
                    return false;
                }

                Type collectionType = clientAsm.GetType(
                    "Microsoft.TeamFoundation.Client.TfsTeamProjectCollection", false);
                if (collectionType == null)
                {
                    error = "TfsTeamProjectCollection type not found";
                    return false;
                }

                string authHow;
                collection = TryCreateTfsCollectionSilent(
                    collectionType, clientAsm, new Uri(collectionUrl), out authHow);
                if (collection == null)
                {
                    error = "TfsTeamProjectCollection create failed";
                    return false;
                }
                AppendAgentLog(logDirectory, "TFS_OM_CONNECT | " + (authHow ?? "-"));

                Type vcType = vcAsm.GetType(
                    "Microsoft.TeamFoundation.VersionControl.Client.VersionControlServer", false);
                if (vcType == null)
                {
                    error = "VersionControlServer type not found";
                    return false;
                }

                MethodInfo getService = collectionType.GetMethod("GetService", new[] { typeof(Type) });
                object vcs = getService.Invoke(collection, new object[] { vcType });
                if (vcs == null)
                {
                    error = "VersionControlServer service null";
                    return false;
                }

                object identity = GetProp(vcs, "AuthorizedIdentity");
                authorizedUserId = FirstNonEmpty(
                    GetPropString(identity, "UniqueName"),
                    GetPropString(identity, "DisplayName"),
                    GetPropString(vcs, "AuthorizedUser"),
                    Environment.UserDomainName + "\\" + Environment.UserName);

                AppendAgentLog(logDirectory,
                    "TFS_OM_AUTH | user=" + authorizedUserId
                    + " from=" + fromLocal.ToString("yyyy-MM-dd HH:mm:ss")
                    + " to=" + toLocal.ToString("yyyy-MM-dd HH:mm:ss"));

                Type versionSpecType = vcAsm.GetType(
                    "Microsoft.TeamFoundation.VersionControl.Client.VersionSpec", false);
                Type dateSpecType = vcAsm.GetType(
                    "Microsoft.TeamFoundation.VersionControl.Client.DateVersionSpec", false);
                Type recursionType = vcAsm.GetType(
                    "Microsoft.TeamFoundation.VersionControl.Client.RecursionType", false);
                if (versionSpecType == null || dateSpecType == null || recursionType == null)
                {
                    error = "VersionSpec/DateVersionSpec/RecursionType not found";
                    return false;
                }

                object latest = versionSpecType.GetProperty("Latest", BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null, null);
                object fromSpec = Activator.CreateInstance(dateSpecType, fromLocal);
                object toSpec = Activator.CreateInstance(dateSpecType, toLocal);
                object recursionFull = Enum.Parse(recursionType, "Full");

                MethodInfo queryHistory = FindQueryHistory10(vcs.GetType());
                if (queryHistory == null)
                {
                    error = "QueryHistory(10-arg) overload not found";
                    return false;
                }

                string[] paths =
                {
                    string.IsNullOrWhiteSpace(serverPath) ? TfsServerPath : serverPath.Trim(),
                    "$/"
                };
                IEnumerable historyEnum = null;
                string usedPath = null;
                for (int p = 0; p < paths.Length; p++)
                {
                    try
                    {
                        object history = queryHistory.Invoke(vcs, new object[]
                        {
                            paths[p],
                            latest,
                            0,
                            recursionFull,
                            authorizedUserId,
                            fromSpec,
                            toSpec,
                            maxCount,
                            true,
                            false
                        });
                        historyEnum = history as IEnumerable;
                        usedPath = paths[p];
                        if (historyEnum != null)
                            break;
                    }
                    catch (TargetInvocationException tex)
                    {
                        AppendAgentLog(logDirectory,
                            "TFS_OM_QUERY_PATH_FAIL | path=" + paths[p]
                            + " err=" + (tex.InnerException != null ? tex.InnerException.Message : tex.Message));
                    }
                }

                if (historyEnum == null)
                {
                    error = "QueryHistory returned null";
                    return false;
                }

                AppendAgentLog(logDirectory, "TFS_OM_QUERY_OK | path=" + (usedPath ?? "-"));

                int taken = 0;
                foreach (object changeset in historyEnum)
                {
                    if (changeset == null || taken >= maxCount)
                        break;

                    string part = BuildChangesetJsonFromOm(changeset);
                    if (string.IsNullOrEmpty(part))
                        continue;
                    changesetJsonParts.Add(part);
                    taken++;
                }

                return true;
            }
            catch (TargetInvocationException tex)
            {
                error = tex.InnerException != null ? tex.InnerException.Message : tex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    IDisposable d = collection as IDisposable;
                    if (d != null)
                        d.Dispose();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// 1) Team Explorer 가 이미 이 PC에 저장한 연결 (Visual Studio 로그인 캐시).
        /// 2) TfsSettings.txt / TfsAuthUser 명시 계정.
        /// 3) Windows 로그인. RCHSP\sukhoonyoon 처럼 TFS ACL 없으면 TF30063.
        /// </summary>
        private static object TryCreateTfsCollectionSilent(
            Type collectionType,
            Assembly clientAsm,
            Uri uri,
            out string how)
        {
            how = null;
            if (collectionType == null || uri == null)
                return null;

            object fromTe = TryCreateCollectionFromTeamExplorerFactory(clientAsm, uri);
            if (fromTe != null)
            {
                how = "team_explorer_cache";
                return fromTe;
            }

            NetworkCredential explicitCred = TryGetTfsNetworkCredential();
            if (explicitCred != null)
            {
                object fromFile = TryCreateCollectionWithNetworkCredential(
                    collectionType, clientAsm, uri, explicitCred);
                if (fromFile != null)
                {
                    how = "settings_user=" + (_resolvedTfsUser ?? "-");
                    return fromFile;
                }
                how = "settings_user_failed=" + (_resolvedTfsUser ?? "-");
            }
            else
            {
                how = "windows_default(" + Environment.UserDomainName + "\\" + Environment.UserName
                    + ") — Team Explorer 미연결이면 TfsAuthUser 에 TFS 전용 계정 필요";
            }

            Type winCredType = clientAsm != null
                ? clientAsm.GetType("Microsoft.TeamFoundation.Client.WindowsCredential", false)
                : null;
            Type tfsCredType = clientAsm != null
                ? clientAsm.GetType("Microsoft.TeamFoundation.Client.TfsClientCredentials", false)
                : null;
            if (winCredType != null && tfsCredType != null)
            {
                try
                {
                    ConstructorInfo winCtor = winCredType.GetConstructor(new[] { typeof(bool) });
                    object winCred = winCtor != null
                        ? winCtor.Invoke(new object[] { true })
                        : Activator.CreateInstance(winCredType);

                    ConstructorInfo credCtor = tfsCredType.GetConstructor(new[] { winCredType });
                    object tfsCreds = credCtor != null
                        ? credCtor.Invoke(new object[] { winCred })
                        : Activator.CreateInstance(tfsCredType, new object[] { winCred });

                    PropertyInfo allow = tfsCredType.GetProperty("AllowInteractive");
                    if (allow != null && allow.CanWrite)
                        allow.SetValue(tfsCreds, false, null);

                    ConstructorInfo colCtor = collectionType.GetConstructor(new[] { typeof(Uri), tfsCredType });
                    if (colCtor != null)
                        return colCtor.Invoke(new object[] { uri, tfsCreds });
                }
                catch
                {
                }
            }

            try
            {
                ConstructorInfo credsCtor = collectionType.GetConstructor(new[] { typeof(Uri), typeof(ICredentials) });
                if (credsCtor != null)
                    return credsCtor.Invoke(new object[] { uri, CredentialCache.DefaultNetworkCredentials });
            }
            catch
            {
            }

            how = (how ?? "") + " | uri_only_may_prompt";
            return Activator.CreateInstance(collectionType, new object[] { uri });
        }

        /// <summary>
        /// Visual Studio Team Explorer 가 이미 이 PC에 등록·인증해 둔 collection 만 재사용.
        /// Uri 단독 GetTeamProjectCollection 은 로그인 창이 뜰 수 있어 쓰지 않음.
        /// </summary>
        private static object TryCreateCollectionFromTeamExplorerFactory(Assembly clientAsm, Uri uri)
        {
            if (clientAsm == null || uri == null)
                return null;
            try
            {
                Type factoryType = clientAsm.GetType(
                    "Microsoft.TeamFoundation.Client.TfsTeamProjectCollectionFactory", false);
                Type registeredType = clientAsm.GetType(
                    "Microsoft.TeamFoundation.Client.RegisteredTfsConnections", false);
                if (factoryType == null)
                    return null;

                object registered = null;
                if (registeredType != null)
                {
                    MethodInfo getOne = registeredType.GetMethod(
                        "GetProjectCollection",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Uri) },
                        null);
                    if (getOne != null)
                        registered = getOne.Invoke(null, new object[] { uri });

                    if (registered == null)
                    {
                        MethodInfo getAll = registeredType.GetMethod(
                            "GetProjectCollections",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            Type.EmptyTypes,
                            null);
                        Array all = getAll != null ? getAll.Invoke(null, null) as Array : null;
                        if (all != null)
                        {
                            string want = uri.ToString().TrimEnd('/');
                            foreach (object item in all)
                            {
                                if (item == null)
                                    continue;
                                Uri itemUri = GetProp(item, "Uri") as Uri;
                                string itemUrl = itemUri != null
                                    ? itemUri.ToString()
                                    : GetPropString(item, "Uri");
                                if (string.IsNullOrWhiteSpace(itemUrl))
                                    continue;
                                if (string.Equals(
                                    itemUrl.TrimEnd('/'),
                                    want,
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    registered = item;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (registered == null)
                    return null;

                MethodInfo fromRegistered = factoryType.GetMethod(
                    "GetTeamProjectCollection",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { registered.GetType() },
                    null);
                if (fromRegistered == null)
                {
                    foreach (MethodInfo m in factoryType.GetMethods(
                        BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "GetTeamProjectCollection")
                            continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length == 1
                            && ps[0].ParameterType.Name == "RegisteredProjectCollection")
                        {
                            fromRegistered = m;
                            break;
                        }
                    }
                }

                if (fromRegistered == null)
                    return null;

                object collection = fromRegistered.Invoke(null, new object[] { registered });
                if (collection == null)
                    return null;
                MethodInfo auth = collection.GetType().GetMethod("EnsureAuthenticated", Type.EmptyTypes);
                if (auth != null)
                    auth.Invoke(collection, null);
                return collection;
            }
            catch
            {
                return null;
            }
        }

        private static object TryCreateCollectionWithNetworkCredential(
            Type collectionType,
            Assembly clientAsm,
            Uri uri,
            NetworkCredential networkCredential)
        {
            Type winCredType = clientAsm != null
                ? clientAsm.GetType("Microsoft.TeamFoundation.Client.WindowsCredential", false)
                : null;
            Type tfsCredType = clientAsm != null
                ? clientAsm.GetType("Microsoft.TeamFoundation.Client.TfsClientCredentials", false)
                : null;

            if (winCredType != null && tfsCredType != null)
            {
                try
                {
                    object winCred = null;
                    ConstructorInfo winNet = winCredType.GetConstructor(new[] { typeof(ICredentials) });
                    if (winNet != null)
                        winCred = winNet.Invoke(new object[] { networkCredential });
                    if (winCred == null)
                    {
                        ConstructorInfo winNc = winCredType.GetConstructor(new[] { typeof(NetworkCredential) });
                        if (winNc != null)
                            winCred = winNc.Invoke(new object[] { networkCredential });
                    }
                    if (winCred != null)
                    {
                        ConstructorInfo credCtor = tfsCredType.GetConstructor(new[] { winCredType });
                        object tfsCreds = credCtor != null
                            ? credCtor.Invoke(new object[] { winCred })
                            : Activator.CreateInstance(tfsCredType, new object[] { winCred });
                        PropertyInfo allow = tfsCredType.GetProperty("AllowInteractive");
                        if (allow != null && allow.CanWrite)
                            allow.SetValue(tfsCreds, false, null);
                        ConstructorInfo colCtor = collectionType.GetConstructor(new[] { typeof(Uri), tfsCredType });
                        if (colCtor != null)
                            return colCtor.Invoke(new object[] { uri, tfsCreds });
                    }
                }
                catch
                {
                }
            }

            try
            {
                ConstructorInfo credsCtor = collectionType.GetConstructor(new[] { typeof(Uri), typeof(ICredentials) });
                if (credsCtor != null)
                    return credsCtor.Invoke(new object[] { uri, networkCredential });
            }
            catch
            {
            }

            return null;
        }

        private static bool TryLoadTfsOmAssemblies(
            out Assembly clientAsm,
            out Assembly vcAsm,
            out string error)
        {
            clientAsm = null;
            vcAsm = null;
            error = null;

            string clientPath = ResolveTfsDllPath(
                "Microsoft.TeamFoundation.Client", TfsClientDllDefault);
            string vcPath = ResolveTfsDllPath(
                "Microsoft.TeamFoundation.VersionControl.Client", TfsVersionControlDllDefault);

            if (string.IsNullOrEmpty(clientPath) || !File.Exists(clientPath))
            {
                error = "TFS Client DLL not found (Team Explorer 필요): " + (clientPath ?? TfsClientDllDefault);
                return false;
            }
            if (string.IsNullOrEmpty(vcPath) || !File.Exists(vcPath))
            {
                error = "TFS VersionControl DLL not found: " + (vcPath ?? TfsVersionControlDllDefault);
                return false;
            }

            try
            {
                clientAsm = Assembly.LoadFrom(clientPath);
                vcAsm = Assembly.LoadFrom(vcPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "Assembly.LoadFrom failed: " + ex.Message;
                return false;
            }
        }

        private static string ResolveTfsDllPath(string assemblySimpleName, string preferredPath)
        {
            if (!string.IsNullOrEmpty(preferredPath) && File.Exists(preferredPath))
                return preferredPath;

            try
            {
                string root = @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\" + assemblySimpleName;
                if (!Directory.Exists(root))
                    return preferredPath;
                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, (a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
                for (int i = 0; i < dirs.Length; i++)
                {
                    string dll = Path.Combine(dirs[i], assemblySimpleName + ".dll");
                    if (File.Exists(dll))
                        return dll;
                }
            }
            catch
            {
            }
            return preferredPath;
        }

        private static MethodInfo FindQueryHistory10(Type vcType)
        {
            MethodInfo[] methods = vcType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, "QueryHistory", StringComparison.Ordinal))
                    continue;
                ParameterInfo[] ps = methods[i].GetParameters();
                if (ps.Length == 10
                    && ps[0].ParameterType == typeof(string)
                    && ps[7].ParameterType == typeof(int)
                    && ps[8].ParameterType == typeof(bool)
                    && ps[9].ParameterType == typeof(bool))
                    return methods[i];
            }
            return null;
        }

        private static string BuildChangesetJsonFromOm(object changeset)
        {
            if (changeset == null)
                return null;

            int changesetId = Convert.ToInt32(GetProp(changeset, "ChangesetId") ?? 0, CultureInfo.InvariantCulture);
            if (changesetId <= 0)
                return null;

            string authorId = FirstNonEmpty(
                GetPropString(changeset, "Owner"),
                GetPropString(changeset, "Committer"));
            string authorName = FirstNonEmpty(
                GetPropString(changeset, "OwnerDisplayName"),
                GetPropString(changeset, "CommitterDisplayName"),
                authorId);
            string comment = GetPropString(changeset, "Comment") ?? string.Empty;

            DateTime creation = DateTime.Now;
            object creationObj = GetProp(changeset, "CreationDate");
            if (creationObj is DateTime)
                creation = (DateTime)creationObj;

            var files = new List<string>();
            object changesObj = GetProp(changeset, "Changes");
            IEnumerable changesEnum = changesObj as IEnumerable;
            if (changesEnum != null)
            {
                foreach (object change in changesEnum)
                {
                    if (change == null)
                        continue;
                    object item = GetProp(change, "Item");
                    if (item == null)
                        continue;

                    string path = FirstNonEmpty(
                        GetPropString(item, "ServerItem"),
                        GetPropString(item, "LocalItem"));
                    if (string.IsNullOrEmpty(path))
                        continue;

                    string fileName = Path.GetFileName(path.Replace('/', '\\'));
                    string changeType = NormalizeChangeType(Convert.ToString(GetProp(change, "ChangeType"), CultureInfo.InvariantCulture));
                    string itemTypeRaw = Convert.ToString(GetProp(item, "ItemType"), CultureInfo.InvariantCulture) ?? string.Empty;
                    bool isFolder = itemTypeRaw.IndexOf("Folder", StringComparison.OrdinalIgnoreCase) >= 0;
                    string version = FirstNonEmpty(
                        Convert.ToString(GetProp(item, "ChangesetId"), CultureInfo.InvariantCulture),
                        Convert.ToString(GetProp(item, "Version"), CultureInfo.InvariantCulture),
                        changesetId.ToString(CultureInfo.InvariantCulture));

                    var fb = new StringBuilder(256);
                    fb.Append('{');
                    AppendJson(fb, "changeType", changeType).Append(',');
                    AppendJson(fb, "itemType", isFolder ? "folder" : "file").Append(',');
                    AppendJson(fb, "fileName", fileName).Append(',');
                    AppendJson(fb, "path", path).Append(',');
                    AppendJson(fb, "version", version);
                    fb.Append('}');
                    files.Add(fb.ToString());
                }
            }

            var cs = new StringBuilder(1024);
            cs.Append('{');
            AppendJson(cs, "changesetId", changesetId).Append(',');
            AppendJson(cs, "authorName", authorName).Append(',');
            AppendJson(cs, "authorId", authorId).Append(',');
            AppendJson(cs, "checkedInAt", creation.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")).Append(',');
            AppendJson(cs, "comment", comment).Append(',');
            AppendJson(cs, "changedFileCount", files.Count).Append(',');
            cs.Append("\"changedFiles\":[");
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) cs.Append(',');
                cs.Append(files[i]);
            }
            cs.Append("]}");
            return cs.ToString();
        }

        private static object GetProp(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name))
                return null;
            PropertyInfo p = target.GetType().GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null)
                return null;
            try
            {
                return p.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static string GetPropString(object target, string name)
        {
            object v = GetProp(target, name);
            if (v == null)
                return null;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return null;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            }
            return null;
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

        private static bool TryReadTargetedSyncRequest(
            out string requestId,
            out DateTime? sessionStartedUtc,
            out DateTime? sessionEndedUtc)
        {
            string target;
            if (!TryReadSyncRequest(out requestId, out sessionStartedUtc, out sessionEndedUtc, out target))
                return false;
            if (IsTargetedAtThisPc(target))
                return true;
            return false;
        }

        private static bool TryReadSyncRequest(
            out string requestId,
            out DateTime? sessionStartedUtc,
            out DateTime? sessionEndedUtc,
            out string targetComputerName)
        {
            requestId = null;
            sessionStartedUtc = null;
            sessionEndedUtc = null;
            targetComputerName = null;
            try
            {
                string text = PeekClipboardText();
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                text = text.TrimStart();
                if (!text.StartsWith(SyncRequestPrefix, StringComparison.Ordinal))
                    return false;
                string json = text.Substring(SyncRequestPrefix.Length).Trim();
                requestId = ExtractString(json, "requestId");
                targetComputerName = ExtractString(json, "targetComputerName");
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

        private static bool IsTargetedAtThisPc(string targetComputerName)
        {
            if (string.IsNullOrWhiteSpace(targetComputerName))
                return true;
            return ComputerNamesMatch(targetComputerName, Environment.MachineName);
        }

        private static bool ComputerNamesMatch(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            return string.Equals(
                NormalizeComputerName(a),
                NormalizeComputerName(b),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeComputerName(string name)
        {
            string s = name.Trim();
            int slash = s.LastIndexOf('\\');
            if (slash >= 0 && slash < s.Length - 1)
                s = s.Substring(slash + 1);
            int dot = s.IndexOf('.');
            if (dot > 0)
                s = s.Substring(0, dot);
            return s;
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
                return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out dt))
                return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
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
            string[] itemPaths = { GetTfsServerPath(), "$/", null };

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
                GetTfsServerPath(),
                "$/",
                null
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
                NetworkCredential tfsCred = TryGetTfsNetworkCredential();
                if (tfsCred != null)
                {
                    request.UseDefaultCredentials = false;
                    request.Credentials = tfsCred;
                }
                else
                {
                    request.UseDefaultCredentials = true;
                }
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

        // Prefix 규칙: GBCWORKHUB* 만 복원/정리. 일반 텍스트는 건드리지 않음.
        // 미수집 TFS / SESSION_RESULT / SYNC_REQUEST / TOKEN 은 덮지 않음.
        private static string _userClipboardBackup;

        private static bool IsProtocolClipboardText(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.StartsWith("GBCWORKHUB", StringComparison.Ordinal);
        }

        private static bool IsJunkClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string t = text.TrimStart();
            return t.StartsWith("powershell.exe -STA", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("powershell -STA", StringComparison.OrdinalIgnoreCase);
        }

        private static string PeekClipboardText()
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return TryGetClipboardText();

            string text = null;
            var t = new Thread(() => { text = TryGetClipboardText(); });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(4000);
            return text;
        }

        private static string TryGetClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryCopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            bool ok = false;
            var worker = new Thread(() =>
            {
                try
                {
                    string cur = TryGetClipboardText();
                    if (!string.IsNullOrEmpty(cur)
                        && !IsProtocolClipboardText(cur)
                        && !IsJunkClipboard(cur))
                        _userClipboardBackup = cur;
                }
                catch
                {
                }

                for (int attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        string roundtrip = TryGetClipboardText();
                        if (!string.IsNullOrEmpty(roundtrip)
                            && roundtrip.StartsWith(text.Substring(0, Math.Min(24, text.Length)), StringComparison.Ordinal))
                        {
                            ok = true;
                            break;
                        }
                    }
                    catch
                    {
                    }
                    Thread.Sleep(250);
                }

                if (!ok)
                    return;

                if (text.StartsWith(TfsPrefix, StringComparison.Ordinal)
                    || text.StartsWith(SessionResultPrefix, StringComparison.Ordinal))
                    WaitAckThenRestoreUserClipboard(text);
                else
                {
                    Thread.Sleep(2500);
                    TryRestoreUserClipboard(text);
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join(25000);
            return ok;
        }

        private static void WaitAckThenRestoreUserClipboard(string writtenText)
        {
            const int minHoldMs = 2000;
            const int maxWaitMs = 12000;
            const int stepMs = 250;

            Thread.Sleep(minHoldMs);
            int waited = minHoldMs;

            while (waited < maxWaitMs)
            {
                string current = TryGetClipboardText();

                if (!string.IsNullOrEmpty(current) && !IsProtocolClipboardText(current))
                    return;

                if (!string.IsNullOrEmpty(current)
                    && current.StartsWith(AckPrefix, StringComparison.Ordinal))
                    break;

                Thread.Sleep(stepMs);
                waited += stepMs;
            }

            TryRestoreUserClipboard(writtenText);
        }

        private static void TryRestoreUserClipboard(string writtenText)
        {
            string current = TryGetClipboardText();

            if (!string.IsNullOrEmpty(current)
                && !IsProtocolClipboardText(current)
                && !string.Equals(current, _userClipboardBackup, StringComparison.Ordinal))
                return;

            if (IsForeignProtocol(current, writtenText))
                return;

            if (string.IsNullOrEmpty(_userClipboardBackup))
            {
                TryClearProtocolClipboard();
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(_userClipboardBackup);
                    return;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            TryClearProtocolClipboard();
        }

        private static bool IsForeignProtocol(string current, string writtenText)
        {
            if (string.IsNullOrEmpty(current) || !IsProtocolClipboardText(current))
                return false;
            if (string.Equals(current, writtenText, StringComparison.Ordinal))
                return false;
            if (current.StartsWith(AckPrefix, StringComparison.Ordinal))
                return false;

            return current.StartsWith(TfsPrefix, StringComparison.Ordinal)
                || current.StartsWith(SessionResultPrefix, StringComparison.Ordinal)
                || current.StartsWith(SyncRequestPrefix, StringComparison.Ordinal)
                || current.StartsWith(SessionTokenPrefix, StringComparison.Ordinal);
        }

        private static void TryClearProtocolClipboard()
        {
            string current = TryGetClipboardText();
            if (string.IsNullOrEmpty(current) || !IsProtocolClipboardText(current))
                return;
            if (IsForeignProtocol(current, null))
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
