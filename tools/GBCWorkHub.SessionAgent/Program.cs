using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;
using GBCWorkHub.SessionAgent.DevSession;

namespace GBCWorkHub.SessionAgent
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                string eventType = args.Length > 0 ? (args[0] ?? "test").Trim() : "test";
                string baseDirectory = (ConfigurationManager.AppSettings["BaseDirectory"] ?? @"C:\GBCWorkHub").Trim();
                if (string.IsNullOrWhiteSpace(baseDirectory))
                    baseDirectory = @"C:\GBCWorkHub";

                string logDirectory = Path.Combine(baseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                Directory.CreateDirectory(baseDirectory);

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
                {
                    HandleConnect(baseDirectory);
                }
                else if (action == "disconnect")
                {
                    HandleDisconnect(baseDirectory);
                }
                else
                {
                    // test / 기타: 로그 + 기존 한 줄 클립보드 (호환)
                    ClipboardHelper.TryCopyToClipboard(logLine);
                }
            }
            catch (Exception ex)
            {
                TryWriteErrorLog(ex);
            }
        }

        private static void HandleConnect(string baseDirectory)
        {
            string clipboard = RdpClipboardPayload.BuildConnectClipboard();
            TryWriteText(Path.Combine(baseDirectory, "LastStatus.json"),
                RdpClipboardPayload.ToJsonBody(clipboard));
            ClipboardHelper.TryCopyToClipboard(clipboard);

            TryCaptureDevSessionStart(baseDirectory);
        }

        /// <summary>
        /// Two-point tracking, connect half. No FileSystemWatcher, no resident process: this
        /// process captures a lightweight metadata snapshot and exits. The SYNC_REQUEST clipboard
        /// message (already used for the TFS-sync-reconnect flow — see TfsSyncCoordinator.cs on
        /// the local side) is reused here as the only channel that carries SESSION_TOKEN to this
        /// machine; if it never arrives, no snapshot is captured and disconnect-time diffing for
        /// this session is simply skipped (logged, not fatal).
        /// </summary>
        private static void TryCaptureDevSessionStart(string baseDirectory)
        {
            try
            {
                SyncRequest sync = null;
                for (int i = 0; i < 10 && sync == null; i++)
                {
                    sync = SyncRequest.TryParse(ClipboardHelper.TryReadClipboardText());
                    if (sync == null)
                        Thread.Sleep(300);
                }

                if (sync == null || string.IsNullOrWhiteSpace(sync.SessionToken))
                {
                    AppendLog(baseDirectory, "DEV_SESSION_START_SKIPPED | no SESSION_TOKEN in SYNC_REQUEST within poll window");
                    return;
                }

                string sourceRoot = (ConfigurationManager.AppSettings["DevSession.SourceRoot"] ?? @"D:\HISSolutions").Trim();
                var snapshot = SourceTreeSnapshot.Capture(sourceRoot);

                var tfvc = BuildTfvcProvider();
                var startPending = tfvc != null ? tfvc.GetPendingChanges(sourceRoot) : new System.Collections.Generic.List<PendingItemInfo>();

                var state = new SessionStateFile
                {
                    SessionToken = sync.SessionToken,
                    RequestId = sync.RequestId,
                    StartedAtUtc = DateTime.UtcNow.ToString("o"),
                    RemotePcName = Environment.MachineName,
                    RemoteUser = Environment.UserDomainName + "\\" + Environment.UserName,
                    Snapshot = snapshot,
                    StartPending = startPending
                };
                SessionStateStore.Save(baseDirectory, state);

                AppendLog(baseDirectory, "DEV_SESSION_START_CAPTURED | token=" + TokenPrefix(sync.SessionToken)
                    + " files=" + snapshot.Count + " pending=" + startPending.Count);
            }
            catch (Exception ex)
            {
                AppendLog(baseDirectory, "DEV_SESSION_START_FAILED | " + ex.Message);
            }
        }

        private static void HandleDisconnect(string baseDirectory)
        {
            TryFinishDevSessions(baseDirectory);

            var tfs = TfsRecentCollector.Collect();
            if (tfs.Success && !string.IsNullOrEmpty(tfs.ClipboardText))
            {
                TryWriteText(Path.Combine(baseDirectory, "PendingTfsRecent.json"), tfs.JsonBody);
                TryWriteText(Path.Combine(baseDirectory, "LastStatus.json"),
                    "{\"type\":\"TFS_PENDING\",\"collectedAt\":\""
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + "\"}");
                ClipboardHelper.TryCopyToClipboard(tfs.ClipboardText);
                return;
            }

            File.AppendAllText(
                Path.Combine(baseDirectory, "Logs", "SessionTest.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + " | TFS_COLLECT_FAILED | "
                + (tfs.Error ?? "unknown")
                + Environment.NewLine,
                Encoding.UTF8);

            string clipboard = RdpClipboardPayload.BuildDisconnectClipboard();
            TryWriteText(Path.Combine(baseDirectory, "LastStatus.json"),
                RdpClipboardPayload.ToJsonBody(clipboard));
            ClipboardHelper.TryCopyToClipboard(clipboard);
        }

        /// <summary>
        /// Two-point tracking, disconnect half. Loads whatever connect-time snapshot(s) exist
        /// (normally exactly one — only one developer uses this PC at a time), captures the
        /// end-state snapshot + pending changes, correlates against TFVC Changesets checked in
        /// during the session window, classifies, and sends one bounded
        /// GBCWORKHUB_SESSION_RESULT:: payload per session back to the local WorkHub.
        /// </summary>
        private static void TryFinishDevSessions(string baseDirectory)
        {
            System.Collections.Generic.List<SessionStateFile> states;
            try
            {
                states = SessionStateStore.LoadAll(baseDirectory);
            }
            catch (Exception ex)
            {
                AppendLog(baseDirectory, "DEV_SESSION_END_LOAD_FAILED | " + ex.Message);
                return;
            }

            if (states.Count == 0)
            {
                AppendLog(baseDirectory, "DEV_SESSION_END_SKIPPED | no session-state file found");
                return;
            }

            string sourceRoot = (ConfigurationManager.AppSettings["DevSession.SourceRoot"] ?? @"D:\HISSolutions").Trim();
            var tfvc = BuildTfvcProvider();

            foreach (var state in states)
            {
                try
                {
                    var endSnapshot = SourceTreeSnapshot.Capture(sourceRoot);
                    var endPending = tfvc != null
                        ? tfvc.GetPendingChanges(sourceRoot)
                        : new System.Collections.Generic.List<PendingItemInfo>();

                    DateTime? startedUtc = state.StartedAtUtcValue;
                    DateTime nowUtc = DateTime.UtcNow;
                    var changesets = tfvc != null && startedUtc.HasValue
                        ? tfvc.GetChangesetsInWindow(state.RemoteUser, startedUtc.Value, nowUtc)
                        : new System.Collections.Generic.List<ChangesetInfo>();

                    var classification = SessionChangeClassifier.Classify(
                        state.Snapshot, endSnapshot, state.StartPending, endPending, changesets);

                    var report = SessionChangeReport.Build(
                        state.SessionToken, state.RemotePcName, state.RemoteUser,
                        startedUtc, nowUtc, classification, changesets);

                    string payload = "GBCWORKHUB_SESSION_RESULT::" + report.ToJson();
                    bool sent = ClipboardHelper.TryCopyToClipboard(payload);

                    AppendLog(baseDirectory, "DEV_SESSION_END_SENT | token=" + TokenPrefix(state.SessionToken)
                        + " files=" + report.Files.Count + " changesets=" + report.Changesets.Count
                        + " truncated=" + report.Truncated + " clipboardOk=" + sent);

                    SessionStateStore.Delete(baseDirectory, state.SessionToken);
                }
                catch (Exception ex)
                {
                    AppendLog(baseDirectory, "DEV_SESSION_END_FAILED | token=" + TokenPrefix(state.SessionToken) + " | " + ex.Message);
                }
            }
        }

        private static ITfvcEvidenceProvider BuildTfvcProvider()
        {
            string collectionUrl = (ConfigurationManager.AppSettings["Tfs.CollectionUrl"] ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(collectionUrl))
                return null;
            string serverPath = (ConfigurationManager.AppSettings["Tfs.ServerPath"] ?? "$/HISSolutions").Trim();
            string sourceRoot = (ConfigurationManager.AppSettings["DevSession.SourceRoot"] ?? @"D:\HISSolutions").Trim();
            string tfExePath = (ConfigurationManager.AppSettings["Tfs.TfExePath"] ?? "tf.exe").Trim();
            return new TfvcRestEvidenceProvider(collectionUrl, serverPath, sourceRoot, tfExePath);
        }

        private static string TokenPrefix(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "-";
            return token.Length <= 8 ? token : token.Substring(0, 8) + "...";
        }

        private static void AppendLog(string baseDirectory, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(baseDirectory, "Logs"));
                File.AppendAllText(
                    Path.Combine(baseDirectory, "Logs", "SessionAgent.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + message + Environment.NewLine,
                    Encoding.UTF8);
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
                string baseDirectory = (ConfigurationManager.AppSettings["BaseDirectory"] ?? @"C:\GBCWorkHub").Trim();
                Directory.CreateDirectory(baseDirectory);
                File.AppendAllText(
                    Path.Combine(baseDirectory, "SessionAgentError.log"),
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
