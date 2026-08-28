using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Models;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// Work Hub가 실행한 mstsc 세션의 시작/연결확인/종료를 로컬에서 추적한다.
    /// AURORA(IP): Process.Exited 우선 + HasExited fallback.
    /// RC(게시 RDP): 원격 창(윈도우) 존재 여부로 세션 유지/종료 판단.
    /// </summary>
    public sealed class RdpSessionTrackingService : IDisposable
    {
        private const int WindowGoneTicksToEnd = 3; // 창 없음 2초×3 ≈ 6초 후 종료
        private const int WindowPresentTicksToConfirm = 3; // 창 유지 후 연결 확정(로그인 UI 통과)

        private readonly object _sync = new object();
        private readonly List<TrackedProcess> _tracked = new List<TrackedProcess>();
        private DispatcherTimer _watchdog;
        private DispatcherTimer _minimizeRetryTimer;
        private int _minimizeRetryCount;
        private bool _launchPreferMinimized;
        private bool _disposed;
        private bool _endHandled;
        private DateTime _launchTimeUtc;
        private Process _startReturnedProcess;
        private int _sessionGeneration;
        /// <summary>게시 RDP(RC): PID 대신 창 기준으로 세션 추적</summary>
        private bool _windowTrackMode;
        private string _rdpFileNeedle;
        private int _windowGoneTicks;
        private int _windowPresentTicks;
        private bool _everSawRemoteWindow;

        public string TargetIp { get; private set; }
        public string RemoteComputerName { get; private set; }
        public DateTime? LaunchTime { get; private set; }
        public DateTime? ConnectionConfirmedAt { get; private set; }
        public DateTime? EndedAt { get; private set; }
        public bool IsConnectionConfirmed { get; private set; }
        public bool IsTracking { get; private set; }
        public string StatusSource { get; private set; }
        public string EndReason { get; private set; }
        public int SessionGeneration { get { return _sessionGeneration; } }
        public RdpLaunchPurpose LaunchPurpose { get; private set; }

        public IReadOnlyList<int> TrackedProcessIds
        {
            get
            {
                lock (_sync)
                {
                    return _tracked.Where(t => IsAlive(t)).Select(t => t.ProcessId).ToList();
                }
            }
        }

        public event Action<int> RdpStarted;
        public event Action<RdpStatusPayload, int> RdpConnectionConfirmed;
        public event Action<RdpSessionEndInfo> RdpEnded;
        public event Action<string> RdpTrackingError;

        public class RdpSessionEndInfo
        {
            public int SessionGeneration { get; set; }
            public bool WasConnectionConfirmed { get; set; }
            public string Reason { get; set; }
            public string StatusSource { get; set; }
            public DateTime EndedAt { get; set; }
            public RdpLaunchPurpose LaunchPurpose { get; set; }
            public int PrimaryProcessId { get; set; }
        }

        private sealed class TrackedProcess
        {
            public int ProcessId { get; set; }
            public Process Process { get; set; }
            public string CommandLine { get; set; }
            public EventHandler ExitedHandler { get; set; }
        }

        public bool TryStart(string targetIp, string remoteComputerName, out string message)
        {
            return TryStart(targetIp, remoteComputerName, RdpLaunchPurpose.UserSession, false, out message);
        }

        public bool TryStart(string targetIp, string remoteComputerName, RdpLaunchPurpose purpose, bool startMinimized, out string message)
        {
            message = null;
            int startedGeneration;
            lock (_sync)
            {
                if (_disposed)
                {
                    message = "서비스가 종료되었습니다.";
                    return false;
                }

                if (IsTracking)
                {
                    message = "이미 해당 원격 PC 세션을 추적 중입니다.";
                    DiagnosticLogger.Info("RdpTrack", "CONNECT_BUTTON_CLICKED ignored (already tracking) TargetIp=" + TargetIp
                        + " TrackedPids=" + FormatPids() + " Gen=" + _sessionGeneration
                        + " Purpose=" + LaunchPurpose);
                    return false;
                }

                TargetIp = (targetIp ?? string.Empty).Trim();
                RemoteComputerName = (remoteComputerName ?? string.Empty).Trim();
                LaunchPurpose = purpose;
                _launchPreferMinimized = startMinimized;
                _windowTrackMode = false;
                _rdpFileNeedle = null;
                _windowGoneTicks = 0;
                _windowPresentTicks = 0;
                _everSawRemoteWindow = false;
                if (string.IsNullOrWhiteSpace(TargetIp))
                {
                    message = "대상 IP가 없습니다.";
                    return false;
                }

                _sessionGeneration++;
                startedGeneration = _sessionGeneration;


                DiagnosticLogger.Info("RdpTrack", "CONNECT_BUTTON_CLICKED TargetIp=" + TargetIp
                    + " ComputerName=" + RemoteComputerName + " Gen=" + startedGeneration
                    + " Purpose=" + purpose + " Minimized=" + startMinimized);

                try
                {
                    var beforeIds = GetMstscProcessIds();
                    _launchTimeUtc = DateTime.UtcNow;
                    LaunchTime = DateTime.Now;
                    ConnectionConfirmedAt = null;
                    EndedAt = null;
                    IsConnectionConfirmed = false;
                    EndReason = null;
                    _endHandled = false;
                    StatusSource = purpose == RdpLaunchPurpose.TfsSyncReconnect
                        ? "LOCAL_MSTSC_TFS_SYNC"
                        : "LOCAL_MSTSC_START";

                    // 일반 접속과 동일: mstsc /v: (Purpose만 분기)
                    var psi = new ProcessStartInfo
                    {
                        FileName = "mstsc.exe",
                        Arguments = "/v:" + TargetIp,
                        UseShellExecute = false,
                        WindowStyle = startMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
                    };

                    DiagnosticLogger.Info("RdpTrack", "MSTSC_LAUNCH_V TargetIp=" + TargetIp
                        + " Purpose=" + purpose
                        + " Args=" + psi.Arguments);

                    Process started;
                    try
                    {
                        started = Process.Start(psi);
                    }
                    catch
                    {
                        psi.UseShellExecute = true;
                        started = Process.Start(psi);
                    }

                    _startReturnedProcess = started;
                    int startedId = started != null ? started.Id : -1;
                    DiagnosticLogger.Info("RdpTrack", "MSTSC_PROCESS_START_RETURNED TargetIp=" + TargetIp
                        + " ProcessId=" + startedId
                        + " Gen=" + startedGeneration
                        + " StatusSource=" + StatusSource);

                    // 이미 추적 중이면 Exited 훅만 보장
                    if (started != null)
                    {
                        try
                        {
                            if (!started.HasExited)
                                AddCandidateUnlocked(started.Id, started);
                        }
                        catch
                        {
                        }
                    }

                    System.Threading.Thread.Sleep(1000);

                    if (startMinimized)
                        MinimizeTrackedWindowsUnlocked("post_start_1s");

                    var afterIds = GetMstscProcessIds();
                    var candidateIds = afterIds.Except(beforeIds).ToList();

                    if (started != null)
                    {
                        try
                        {
                            if (!started.HasExited && !candidateIds.Contains(started.Id))
                                candidateIds.Add(started.Id);
                        }
                        catch
                        {
                        }
                    }

                    foreach (var w in QueryMstscByCommandLine(TargetIp))
                    {
                        if (afterIds.Contains(w.ProcessId) && !candidateIds.Contains(w.ProcessId))
                            candidateIds.Add(w.ProcessId);
                    }

                    if (candidateIds.Count == 0)
                    {
                        DiagnosticLogger.Warn("RdpTrack", "MSTSC_PROCESS_REUSED_SUSPECTED TargetIp=" + TargetIp
                            + " ProcessId=" + startedId + " reason=no_new_pid Gen=" + startedGeneration);
                        foreach (var w in QueryMstscByCommandLine(TargetIp))
                            candidateIds.Add(w.ProcessId);
                    }

                    foreach (var pid in candidateIds.Distinct())
                        AddCandidateUnlocked(pid, started != null && started.Id == pid ? started : null);

                    if (_tracked.Count == 0)
                    {
                        message = "mstsc 후보 프로세스를 찾지 못했습니다.";
                        RaiseError(message);
                        ResetTrackingStateUnlocked();
                        return false;
                    }

                    try
                    {
                        if (started != null && started.HasExited)
                        {
                            DiagnosticLogger.Warn("RdpTrack", "MSTSC_PROCESS_REUSED_SUSPECTED TargetIp=" + TargetIp
                                + " ProcessId=" + startedId
                                + " reason=start_process_already_exited TrackedPids=" + FormatPids()
                                + " Gen=" + startedGeneration);
                        }
                    }
                    catch
                    {
                    }

                    if (startMinimized)
                    {
                        MinimizeTrackedWindowsUnlocked("post_track");
                        StartMinimizeRetryUnlocked();
                    }

                    IsTracking = true;
                    StartWatchdogUnlocked();
                    message = startMinimized
                        ? "원격 접속을 시작했습니다. (최소화)"
                        : "원격 접속을 시작했습니다.";
                }
                catch (Exception ex)
                {
                    message = "mstsc 실행 실패: " + ex.Message;
                    RaiseError(message);
                    ResetTrackingStateUnlocked();
                    return false;
                }
            }

            var startedHandler = RdpStarted;
            if (startedHandler != null)
                startedHandler(startedGeneration);
            return true;
        }

        /// <summary>
        /// 게시 .rdp 파일 실행 후 mstsc 추적. shareKey는 DB 점유 키(IP 또는 PC명).
        /// </summary>
        public bool TryStartPublishedRdp(string rdpFilePath, string shareKey, string remoteComputerName, out string message)
        {
            return TryStartPublishedRdp(
                rdpFilePath, shareKey, remoteComputerName, RdpLaunchPurpose.UserSession, out message);
        }

        public bool TryStartPublishedRdp(
            string rdpFilePath,
            string shareKey,
            string remoteComputerName,
            RdpLaunchPurpose purpose,
            out string message)
        {
            message = null;
            int startedGeneration;
            string rdpPath = (rdpFilePath ?? string.Empty).Trim();
            string key = (shareKey ?? string.Empty).Trim();
            string fileNeedle = string.IsNullOrWhiteSpace(rdpPath)
                ? string.Empty
                : Path.GetFileName(rdpPath);

            lock (_sync)
            {
                if (_disposed)
                {
                    message = "서비스가 종료되었습니다.";
                    return false;
                }

                if (IsTracking)
                {
                    message = "이미 해당 원격 PC 세션을 추적 중입니다.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(rdpPath) || !File.Exists(rdpPath))
                {
                    message = "RDP 파일이 없습니다.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    message = "점유 키(IP/PC명)가 없습니다.";
                    return false;
                }

                TargetIp = key;
                RemoteComputerName = (remoteComputerName ?? string.Empty).Trim();
                LaunchPurpose = purpose;
                _launchPreferMinimized = false;
                _windowTrackMode = true;
                _rdpFileNeedle = fileNeedle;
                _windowGoneTicks = 0;
                _windowPresentTicks = 0;
                _everSawRemoteWindow = false;

                _sessionGeneration++;
                startedGeneration = _sessionGeneration;

                DiagnosticLogger.Info("RdpTrack", "PUBLISHED_RDP_LAUNCH Key=" + key
                    + " ComputerName=" + RemoteComputerName
                    + " Purpose=" + purpose
                    + " TrackMode=Window"
                    + " File=" + rdpPath
                    + " Gen=" + startedGeneration);

                try
                {
                    var beforeIds = GetMstscProcessIds();
                    _launchTimeUtc = DateTime.UtcNow;
                    LaunchTime = DateTime.Now;
                    ConnectionConfirmedAt = null;
                    EndedAt = null;
                    IsConnectionConfirmed = false;
                    EndReason = null;
                    _endHandled = false;
                    StatusSource = purpose == RdpLaunchPurpose.TfsSyncReconnect
                        ? "LOCAL_PUBLISHED_RDP_TFS_SYNC"
                        : "LOCAL_PUBLISHED_RDP_START";

                    Process started = Process.Start(new ProcessStartInfo
                    {
                        FileName = rdpPath,
                        UseShellExecute = true
                    });

                    _startReturnedProcess = started;
                    int startedId = started != null ? started.Id : -1;

                    if (started != null)
                    {
                        try
                        {
                            if (!started.HasExited)
                                AddCandidateUnlocked(started.Id, started);
                        }
                        catch
                        {
                        }
                    }

                    System.Threading.Thread.Sleep(1200);

                    var afterIds = GetMstscProcessIds();
                    var candidateIds = afterIds.Except(beforeIds).ToList();

                    if (started != null)
                    {
                        try
                        {
                            if (!started.HasExited && !candidateIds.Contains(started.Id))
                                candidateIds.Add(started.Id);
                        }
                        catch
                        {
                        }
                    }

                    // .rdp 경로/파일명이 cmdline에 남은 mstsc
                    if (!string.IsNullOrWhiteSpace(fileNeedle))
                    {
                        foreach (var w in QueryMstscByCommandLine(fileNeedle))
                        {
                            if (afterIds.Contains(w.ProcessId) && !candidateIds.Contains(w.ProcessId))
                                candidateIds.Add(w.ProcessId);
                        }
                    }

                    if (candidateIds.Count == 0)
                    {
                        // 재사용 의심: 파일명 매칭 전체
                        if (!string.IsNullOrWhiteSpace(fileNeedle))
                        {
                            foreach (var w in QueryMstscByCommandLine(fileNeedle))
                                candidateIds.Add(w.ProcessId);
                        }
                    }

                    // 그래도 없으면 새로 뜬 mstsc만 (before 대비 after)
                    if (candidateIds.Count == 0)
                        candidateIds.AddRange(afterIds.Except(beforeIds));

                    foreach (var pid in candidateIds.Distinct())
                        AddCandidateUnlocked(pid, started != null && started.Id == pid ? started : null);

                    if (_tracked.Count == 0)
                    {
                        message = "mstsc 후보 프로세스를 찾지 못했습니다. (.rdp)";
                        RaiseError(message);
                        ResetTrackingStateUnlocked();
                        return false;
                    }

                    IsTracking = true;
                    StartWatchdogUnlocked();
                    message = "원격 접속을 시작했습니다. (.rdp)";
                }
                catch (Exception ex)
                {
                    message = "RDP 실행 실패: " + ex.Message;
                    RaiseError(message);
                    ResetTrackingStateUnlocked();
                    return false;
                }
            }

            var startedHandler = RdpStarted;
            if (startedHandler != null)
                startedHandler(startedGeneration);
            return true;
        }

        /// <summary>
        /// 추적 중인 mstsc 세션 강제 종료
        /// </summary>
        public void ForceEnd(string reason)
        {
            int gen;
            bool confirmed;
            lock (_sync)
            {
                if (!IsTracking || _endHandled)
                    return;
                gen = _sessionGeneration;
                confirmed = IsConnectionConfirmed;
                KillTrackedProcessesUnlocked();
            }
            CompleteSessionEnd(confirmed, reason ?? "force_end", gen, -1);
        }

        private void KillTrackedProcessesUnlocked()
        {
            foreach (var t in _tracked.ToList())
            {
                try
                {
                    if (t.Process != null && !t.Process.HasExited)
                        t.Process.Kill();
                }
                catch
                {
                }
            }
        }

        private void StartMinimizeRetryUnlocked()
        {
            StopMinimizeRetryUnlocked();
            _minimizeRetryCount = 0;
            var app = System.Windows.Application.Current;
            var dispatcher = app != null ? app.Dispatcher : null;
            if (dispatcher == null)
                return;

            _minimizeRetryTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _minimizeRetryTimer.Tick += MinimizeRetryOnTick;
            _minimizeRetryTimer.Start();
            DiagnosticLogger.Info("RdpTrack", "MINIMIZE_RETRY_START TargetIp=" + TargetIp + " Purpose=" + LaunchPurpose);
        }

        private void StopMinimizeRetryUnlocked()
        {
            if (_minimizeRetryTimer == null)
                return;
            try
            {
                _minimizeRetryTimer.Stop();
                _minimizeRetryTimer.Tick -= MinimizeRetryOnTick;
            }
            catch
            {
            }
            _minimizeRetryTimer = null;
        }

        private void MinimizeRetryOnTick(object sender, EventArgs e)
        {
            lock (_sync)
            {
                if (_disposed || !_launchPreferMinimized || !IsTracking || _endHandled)
                {
                    StopMinimizeRetryUnlocked();
                    return;
                }

                MinimizeTrackedWindowsUnlocked("retry_" + _minimizeRetryCount);
                _minimizeRetryCount++;
                // mstsc 창이 늦게 뜨므로 약 6초간 재시도
                if (_minimizeRetryCount >= 15)
                {
                    DiagnosticLogger.Info("RdpTrack", "MINIMIZE_RETRY_DONE TargetIp=" + TargetIp
                        + " attempts=" + _minimizeRetryCount);
                    StopMinimizeRetryUnlocked();
                }
            }
        }

        private void MinimizeTrackedWindowsUnlocked(string reason)
        {
            int minimized = 0;
            foreach (var t in _tracked)
            {
                if (!IsAlive(t))
                    continue;
                minimized += MinimizeWindowsForProcessId(t.ProcessId);
            }
            if (minimized > 0)
            {
                DiagnosticLogger.Info("RdpTrack", "MSTSC_MINIMIZED TargetIp=" + TargetIp
                    + " windows=" + minimized
                    + " reason=" + (reason ?? "-")
                    + " Purpose=" + LaunchPurpose);
            }
        }

        /// <summary>
        /// mstsc는 MainWindowHandle이 비어 있는 경우가 많아 EnumWindows로 PID 창을 찾아 최소화한다.
        /// </summary>
        private static int MinimizeWindowsForProcessId(int processId)
        {
            if (processId <= 0)
                return 0;

            int count = 0;
            EnumWindows((hWnd, lParam) =>
            {
                int pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid != processId)
                    return true;

                if (!IsWindowVisible(hWnd))
                    return true;

                // 소유자 창만 (툴팁/자식 제외)
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                    return true;

                ShowWindow(hWnd, SW_SHOWMINNOACTIVE);
                count++;
                return true;
            }, IntPtr.Zero);
            return count;
        }

        private const int SW_SHOWMINNOACTIVE = 7;
        private const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        public void ConfirmConnection(RdpStatusPayload payload)
        {
            int gen;
            lock (_sync)
            {
                if (!IsTracking || _endHandled)
                    return;
                if (payload == null)
                    return;
                if (!string.IsNullOrWhiteSpace(RemoteComputerName)
                    && !string.Equals(payload.ComputerName, RemoteComputerName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(payload.ComputerName, TargetIp, StringComparison.OrdinalIgnoreCase))
                    return;
                if (!IsConnectionConfirmPayload(payload))
                    return;

                IsConnectionConfirmed = true;
                ConnectionConfirmedAt = DateTime.Now;
                StatusSource = "REMOTE_CLIPBOARD_EVENT";
                gen = _sessionGeneration;

                DiagnosticLogger.Info("RdpTrack", "REMOTE_CONNECTION_CONFIRMED TargetIp=" + TargetIp
                    + " TrackedPids=" + FormatPids()
                    + " Gen=" + gen
                    + " LatestEventId=" + (payload.LatestEventId.HasValue ? payload.LatestEventId.Value.ToString() : "null"));
            }

            var handler = RdpConnectionConfirmed;
            if (handler != null)
                handler(payload, gen);
        }

        public static bool IsConnectionConfirmPayload(RdpStatusPayload payload)
        {
            if (payload == null)
                return false;
            if (payload.LatestEventId == 21 || payload.LatestEventId == 25)
                return true;
            if (string.Equals(payload.TriggerType, "RC_CONNECT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(payload.TriggerType, "AURORA_CONNECT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(payload.TriggerType, "CMC_CONNECT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(payload.TriggerType, "RC_WINDOW_DETECT", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(payload.SessionRaw))
            {
                string raw = payload.SessionRaw;
                if (raw.IndexOf("rdp-tcp", StringComparison.OrdinalIgnoreCase) >= 0
                    && raw.IndexOf("Active", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                StopWatchdogUnlocked();
                DetachAllProcessesUnlocked();
                IsTracking = false;
            }
        }

        /// <summary>
        /// 대상 IP의 mstsc가 현재 실행 중인지 (stale session 복구용)
        /// </summary>
        public bool IsMstscRunningForIp(string targetIp)
        {
            if (string.IsNullOrWhiteSpace(targetIp))
                return false;
            try
            {
                return QueryMstscByCommandLine(targetIp.Trim()).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Work Hub 재시작 후: 이미 떠 있는 mstsc를 추적만 재개 (새 프로세스 실행 없음).
        /// needle = IP / PC명 / .rdp 파일명 중 커맨드라인에 걸리는 값.
        /// </summary>
        public bool TryAdoptExisting(
            string shareKey,
            string remoteComputerName,
            string commandLineNeedle,
            out string message)
        {
            message = null;
            string key = (shareKey ?? string.Empty).Trim();
            string needle = (commandLineNeedle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                message = "shareKey 없음";
                return false;
            }
            if (string.IsNullOrWhiteSpace(needle))
                needle = key;

            lock (_sync)
            {
                if (_disposed)
                {
                    message = "서비스가 종료되었습니다.";
                    return false;
                }
                if (IsTracking)
                {
                    message = "이미 추적 중";
                    return false;
                }

                var matches = QueryMstscByCommandLine(needle);
                if (matches == null || matches.Count == 0)
                {
                    message = "실행 중인 mstsc를 찾지 못함 (" + needle + ")";
                    return false;
                }

                TargetIp = key;
                RemoteComputerName = string.IsNullOrWhiteSpace(remoteComputerName)
                    ? key
                    : remoteComputerName.Trim();
                LaunchPurpose = RdpLaunchPurpose.UserSession;
                _launchPreferMinimized = false;
                _windowTrackMode = true;
                _rdpFileNeedle = needle;
                _windowGoneTicks = 0;
                _windowPresentTicks = 0;
                _everSawRemoteWindow = true;
                LaunchTime = DateTime.Now;
                ConnectionConfirmedAt = DateTime.Now;
                EndedAt = null;
                IsConnectionConfirmed = true;
                StatusSource = "ADOPT_EXISTING";
                EndReason = null;
                _endHandled = false;
                _sessionGeneration++;
                DetachAllProcessesUnlocked();

                foreach (var w in matches)
                    AddCandidateUnlocked(w.ProcessId, null);

                if (_tracked.Count == 0)
                {
                    message = "mstsc PID 연결 실패";
                    ResetTrackingStateUnlocked();
                    return false;
                }

                IsTracking = true;
                StartWatchdogUnlocked();
                DiagnosticLogger.Info("RdpTrack",
                    "ADOPT_EXISTING Key=" + key
                    + " Needle=" + needle
                    + " Pids=" + FormatPids()
                    + " Gen=" + _sessionGeneration);
            }

            var started = RdpStarted;
            if (started != null)
                started(_sessionGeneration);

            return true;
        }

        private void AddCandidateUnlocked(int pid, Process existing)
        {
            if (_tracked.Any(t => t.ProcessId == pid))
            {
                // 이미 추적 중이면 Exited 훅만 보장
                var existingTracked = _tracked.First(t => t.ProcessId == pid);
                if (existingTracked.Process != null && existingTracked.ExitedHandler == null)
                {
                    try
                    {
                        existingTracked.Process.EnableRaisingEvents = true;
                        EventHandler handler = (s, e) => OnTrackedProcessExited(pid, _sessionGeneration);
                        existingTracked.ExitedHandler = handler;
                        existingTracked.Process.Exited += handler;
                        DiagnosticLogger.Info("RdpTrack", "PROCESS_EXITED_HOOKED ProcessId=" + pid
                            + " Purpose=" + LaunchPurpose + " Gen=" + _sessionGeneration);
                    }
                    catch
                    {
                    }
                }
                return;
            }

            Process proc = existing;
            try
            {
                if (proc == null)
                    proc = Process.GetProcessById(pid);
                if (proc.HasExited)
                {
                    DiagnosticLogger.Info("RdpTrack", "Skip dead candidate pid=" + pid);
                    return;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("RdpTrack", "AddCandidate failed pid=" + pid + " " + ex.Message);
                return;
            }

            string cmd = null;
            try
            {
                var info = QueryMstscByCommandLine(TargetIp).FirstOrDefault(x => x.ProcessId == pid);
                if (info != null)
                    cmd = info.CommandLine;
            }
            catch
            {
            }

            var tracked = new TrackedProcess
            {
                ProcessId = pid,
                Process = proc,
                CommandLine = cmd
            };

            try
            {
                proc.EnableRaisingEvents = true;
                EventHandler handler = (s, e) => OnTrackedProcessExited(pid, _sessionGeneration);
                tracked.ExitedHandler = handler;
                proc.Exited += handler;
                DiagnosticLogger.Info("RdpTrack", "PROCESS_EXITED_HOOKED ProcessId=" + pid
                    + " Purpose=" + LaunchPurpose + " Gen=" + _sessionGeneration);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("RdpTrack", "EnableRaisingEvents failed pid=" + pid + " " + ex.Message);
            }

            _tracked.Add(tracked);
            DiagnosticLogger.Info("RdpTrack", "MSTSC_CANDIDATE_ADDED TargetIp=" + TargetIp
                + " ProcessId=" + pid
                + " TrackedPids=" + FormatPids()
                + " CommandLine=" + (cmd ?? "(unknown)")
                + " Gen=" + _sessionGeneration);
        }

        private void OnTrackedProcessExited(int pid, int generation)
        {
            DiagnosticLogger.Info("LOCAL_MSTSC_EXIT_DETECTED",
                "RemoteIp=" + (TargetIp ?? "-")
                + " ProcessId=" + pid
                + " Purpose=" + LaunchPurpose
                + " Gen=" + generation
                + " source=Process.Exited");

            bool shouldEnd = false;
            bool wasConfirmed = false;
            int gen = 0;
            int primaryPid = pid;

            try
            {
                lock (_sync)
                {
                    if (!IsTracking || _endHandled || generation != _sessionGeneration)
                        return;

                    // 기동 직후 starter 프로세스가 바로 죽는 경우(mstsc 자식 분리) 종료 확정 금지
                    double elapsed = (DateTime.UtcNow - _launchTimeUtc).TotalSeconds;
                    if (elapsed < 3.0)
                    {
                        _tracked.RemoveAll(t => t.ProcessId == pid || !IsAlive(t));
                        DiagnosticLogger.Info("RdpTrack", "PROCESS_EXIT_IGNORED_GRACE ProcessId=" + pid
                            + " elapsed=" + elapsed.ToString("0.0") + "s Gen=" + generation);
                        return;
                    }

                    _tracked.RemoveAll(t => t.ProcessId == pid || !IsAlive(t));

                    // RC 게시 RDP: 중간 PID 종료는 무시 — 창 기준으로만 종료(워치독)
                    if (_windowTrackMode)
                    {
                        DiscoverAdditionalCandidatesUnlocked();
                        AttachMstscPidsFromWindowsUnlocked();
                        if (HasRemoteSessionWindowUnlocked())
                        {
                            DiagnosticLogger.Info("RdpTrack", "PROCESS_EXIT_IGNORED_WINDOW_ALIVE ProcessId=" + pid
                                + " Gen=" + generation);
                            return;
                        }
                        // 창도 없으면 워치독 debounce에 맡김
                        return;
                    }

                    bool anyAlive = _tracked.Any(IsAlive);
                    bool targetAlive = IsTargetIpMstscAliveUnlocked();
                    if (!anyAlive && !targetAlive)
                    {
                        shouldEnd = true;
                        wasConfirmed = IsConnectionConfirmed;
                        gen = generation;
                    }
                }

                if (shouldEnd)
                    CompleteSessionEnd(wasConfirmed, "process_exited", gen, primaryPid);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("RdpTrack", "OnTrackedProcessExited: " + ex.Message);
            }
        }

        private void StartWatchdogUnlocked()
        {
            StopWatchdogUnlocked();

            // Process.Exited 누락 대비: 2초 간격으로 HasExited만 확인
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _watchdog.Tick += WatchdogOnTick;
            _watchdog.Start();
        }

        private void StopWatchdogUnlocked()
        {
            if (_watchdog != null)
            {
                _watchdog.Tick -= WatchdogOnTick;
                _watchdog.Stop();
                _watchdog = null;
            }
        }

        private void WatchdogOnTick(object sender, EventArgs e)
        {
            try
            {
                bool shouldEnd = false;
                bool shouldConfirmByWindow = false;
                bool wasConfirmed = false;
                string reason = null;
                int gen = 0;
                int primaryPid = -1;
                RdpStatusPayload confirmPayload = null;

                lock (_sync)
                {
                    if (!IsTracking || _endHandled)
                        return;

                    // 기동 직후 짧은 유예 (프로세스 핸들 안정화)
                    double elapsed = (DateTime.UtcNow - _launchTimeUtc).TotalSeconds;
                    if (elapsed < 1.0)
                        return;

                    foreach (var t in _tracked)
                    {
                        try
                        {
                            if (t.Process != null)
                                t.Process.Refresh();
                        }
                        catch
                        {
                        }
                    }

                    DiscoverAdditionalCandidatesUnlocked();
                    AttachMstscPidsFromWindowsUnlocked();

                    // ----- RC: 원격 창 기준 -----
                    if (_windowTrackMode)
                    {
                        bool windowAlive = HasRemoteSessionWindowUnlocked();
                        if (windowAlive)
                        {
                            _everSawRemoteWindow = true;
                            _windowGoneTicks = 0;
                            _windowPresentTicks++;
                            if (!IsConnectionConfirmed
                                && elapsed >= 8.0
                                && _windowPresentTicks >= WindowPresentTicksToConfirm)
                            {
                                shouldConfirmByWindow = true;
                                confirmPayload = new RdpStatusPayload
                                {
                                    Type = "GBC_RDP_STATUS",
                                    SchemaVersion = 1,
                                    ComputerName = string.IsNullOrWhiteSpace(RemoteComputerName)
                                        ? TargetIp
                                        : RemoteComputerName,
                                    CollectedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                    LatestEventId = 21,
                                    LatestRecordId = DateTime.Now.Ticks,
                                    TriggerType = "RC_WINDOW_DETECT",
                                    SessionRaw = "rdp-tcp#0 Active"
                                };
                            }
                        }
                        else
                        {
                            _windowPresentTicks = 0;
                            // 로그인(VPN/인증) 중에는 창이 늦게 뜨거나 PID가 바뀌어도 종료하지 않음
                            if (!_everSawRemoteWindow)
                            {
                                if (elapsed < 90.0)
                                    return;
                                shouldEnd = true;
                                wasConfirmed = false;
                                reason = "remote_window_never_appeared";
                                gen = _sessionGeneration;
                                primaryPid = _tracked.Count > 0 ? _tracked[0].ProcessId : -1;
                            }
                            else
                            {
                                _windowGoneTicks++;
                                if (_windowGoneTicks < WindowGoneTicksToEnd)
                                {
                                    DiagnosticLogger.Info("RdpTrack", "WINDOW_GONE_TICK "
                                        + _windowGoneTicks + "/" + WindowGoneTicksToEnd
                                        + " Key=" + (TargetIp ?? "-"));
                                    return;
                                }

                                shouldEnd = true;
                                wasConfirmed = IsConnectionConfirmed;
                                reason = "remote_window_closed";
                                gen = _sessionGeneration;
                                primaryPid = _tracked.Count > 0 ? _tracked[0].ProcessId : -1;
                                DiagnosticLogger.Info("LOCAL_MSTSC_EXIT_DETECTED",
                                    "RemoteIp=" + (TargetIp ?? "-")
                                    + " ProcessId=" + primaryPid
                                    + " Purpose=" + LaunchPurpose
                                    + " Gen=" + gen
                                    + " source=RemoteWindowClosed");
                            }
                        }
                    }
                    else
                    {
                        // ----- AURORA: 프로세스 기준 -----
                        bool anyAlive = _tracked.Any(IsAlive);
                        if (anyAlive)
                            return;

                        if (_tracked.Count == 0)
                            DiscoverAdditionalCandidatesUnlocked();

                        anyAlive = _tracked.Any(IsAlive);
                        if (anyAlive)
                            return;

                        shouldEnd = true;
                        wasConfirmed = IsConnectionConfirmed;
                        reason = "process_has_exited_fallback";
                        gen = _sessionGeneration;
                        primaryPid = _tracked.Count > 0 ? _tracked[0].ProcessId : -1;

                        DiagnosticLogger.Info("LOCAL_MSTSC_EXIT_DETECTED",
                            "RemoteIp=" + (TargetIp ?? "-")
                            + " ProcessId=" + primaryPid
                            + " Purpose=" + LaunchPurpose
                            + " Gen=" + gen
                            + " source=HasExitedFallback");
                    }
                }

                if (shouldConfirmByWindow && confirmPayload != null)
                    ConfirmConnection(confirmPayload);

                if (shouldEnd)
                    CompleteSessionEnd(wasConfirmed, reason, gen, primaryPid);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("RdpTrack", "Watchdog error: " + ex.Message);
            }
        }

        private void DiscoverAdditionalCandidatesUnlocked()
        {
            try
            {
                var needles = GetSessionNeedles();
                foreach (string needle in needles)
                {
                    if (string.IsNullOrWhiteSpace(needle))
                        continue;
                    foreach (var w in QueryMstscByCommandLine(needle))
                    {
                        if (!_tracked.Any(t => t.ProcessId == w.ProcessId))
                            AddCandidateUnlocked(w.ProcessId, null);
                    }
                }
            }
            catch
            {
            }
        }

        private void AttachMstscPidsFromWindowsUnlocked()
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                    return true;

                int pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid <= 0 || _tracked.Any(t => t.ProcessId == pid))
                    return true;

                string title = GetWindowTitle(hWnd);
                bool titleOk = TitleMatchesSession(title);
                // 로그인 UI(일반 제목)는 cmd에 RDP 파일명이 있을 때만 편입
                if (!titleOk)
                {
                    if (!_windowTrackMode || !IsGenericRemoteDesktopTitle(title)
                        || !CommandLineMatchesSessionUnlocked(pid))
                        return true;
                }

                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        if (!string.Equals(p.ProcessName, "mstsc", StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (p.HasExited)
                            return true;
                    }
                    AddCandidateUnlocked(pid, null);
                }
                catch
                {
                }
                return true;
            }, IntPtr.Zero);
        }

        private bool CommandLineMatchesSessionUnlocked(int pid)
        {
            foreach (string needle in GetSessionNeedles())
            {
                if (string.IsNullOrWhiteSpace(needle))
                    continue;
                if (QueryMstscByCommandLine(needle).Any(w => w.ProcessId == pid))
                    return true;
            }
            return false;
        }

        private static bool IsGenericRemoteDesktopTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;
            return title.IndexOf("Remote Desktop", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("원격 데스크톱", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsTargetIpMstscAliveUnlocked()
        {
            try
            {
                return QueryMstscByCommandLine(TargetIp).Any(w =>
                {
                    try
                    {
                        using (var p = Process.GetProcessById(w.ProcessId))
                            return !p.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAlive(TrackedProcess t)
        {
            if (t == null || t.Process == null)
                return false;
            try
            {
                return !t.Process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void CompleteSessionEnd(bool wasConfirmed, string reason, int generation, int primaryProcessId)
        {
            RdpSessionEndInfo info;
            lock (_sync)
            {
                if (_endHandled || !IsTracking || generation != _sessionGeneration)
                {
                    DiagnosticLogger.Info("RdpTrack", "DUPLICATE_END_IGNORED TargetIp=" + TargetIp
                        + " reason=" + reason
                        + " reqGen=" + generation
                        + " curGen=" + _sessionGeneration
                        + " endHandled=" + _endHandled
                        + " tracking=" + IsTracking);
                    return;
                }

                _endHandled = true;
                EndedAt = DateTime.Now;
                EndReason = reason;
                StatusSource = "LOCAL_MSTSC_EXIT";
                StopWatchdogUnlocked();
                StopMinimizeRetryUnlocked();
                DetachAllProcessesUnlocked();
                IsTracking = false;
                _launchPreferMinimized = false;

                info = new RdpSessionEndInfo
                {
                    SessionGeneration = generation,
                    WasConnectionConfirmed = wasConfirmed,
                    Reason = reason,
                    StatusSource = StatusSource,
                    EndedAt = EndedAt.Value,
                    LaunchPurpose = LaunchPurpose,
                    PrimaryProcessId = primaryProcessId
                };

                string logCode = wasConfirmed ? "RDP_SESSION_END_CONFIRMED" : "RDP_CONNECT_CANCELLED";
                DiagnosticLogger.Info("RdpTrack", logCode
                    + " TargetIp=" + TargetIp
                    + " ProcessId=" + primaryProcessId
                    + " Gen=" + generation
                    + " StatusSource=" + StatusSource
                    + " EndReason=" + reason
                    + " EndedAt=" + EndedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    + " WasConfirmed=" + wasConfirmed
                    + " Purpose=" + LaunchPurpose);
            }

            var handler = RdpEnded;
            if (handler != null)
                handler(info);
        }

        private void DetachAllProcessesUnlocked()
        {
            foreach (var t in _tracked)
            {
                try
                {
                    if (t.Process != null && t.ExitedHandler != null)
                        t.Process.Exited -= t.ExitedHandler;
                }
                catch
                {
                }
                try
                {
                    if (t.Process != null)
                        t.Process.Dispose();
                }
                catch
                {
                }
            }
            _tracked.Clear();
            _startReturnedProcess = null;
        }

        private void ResetTrackingStateUnlocked()
        {
            StopWatchdogUnlocked();
            StopMinimizeRetryUnlocked();
            DetachAllProcessesUnlocked();
            IsTracking = false;
            IsConnectionConfirmed = false;
            _endHandled = false;
            _launchPreferMinimized = false;
            _windowTrackMode = false;
            _rdpFileNeedle = null;
            _windowGoneTicks = 0;
            _windowPresentTicks = 0;
            _everSawRemoteWindow = false;
        }

        private void RaiseError(string message)
        {
            DiagnosticLogger.Error("RdpTrack", "RdpTrackingError: " + message);
            var handler = RdpTrackingError;
            if (handler != null)
                handler(message);
        }

        private string FormatPids()
        {
            return string.Join(",", _tracked.Select(t => t.ProcessId));
        }

        private static HashSet<int> GetMstscProcessIds()
        {
            var set = new HashSet<int>();
            try
            {
                foreach (var p in Process.GetProcessesByName("mstsc"))
                {
                    try { set.Add(p.Id); }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch
            {
            }
            return set;
        }

        private class WmiProcInfo
        {
            public int ProcessId { get; set; }
            public string CommandLine { get; set; }
        }

        private static List<WmiProcInfo> QueryMstscByCommandLine(string targetIp)
        {
            var list = new List<WmiProcInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'mstsc.exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject obj in results)
                    {
                        using (obj)
                        {
                            object pidObj = obj["ProcessId"];
                            string cmd = obj["CommandLine"] as string;
                            if (pidObj == null)
                                continue;
                            int pid = Convert.ToInt32(pidObj);
                            if (string.IsNullOrEmpty(targetIp)
                                || cmd == null
                                || cmd.IndexOf(targetIp, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            list.Add(new WmiProcInfo { ProcessId = pid, CommandLine = cmd });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("RdpTrack", "WMI QueryMstsc failed: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// RC 세션 창이 보이는지. 로그인 UI("원격 데스크톱 연결") + PC명/파일명 매칭 창 포함.
        /// </summary>
        private bool HasRemoteSessionWindowUnlocked()
        {
            var alivePids = new HashSet<int>(_tracked.Where(IsAlive).Select(t => t.ProcessId));
            bool found = false;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                    return true;

                int pid;
                GetWindowThreadProcessId(hWnd, out pid);

                string title = GetWindowTitle(hWnd);
                bool pidMatch = alivePids.Contains(pid);
                bool titleMatch = TitleMatchesSession(title);
                // 일반 "원격 데스크톱 연결" 제목은 우리 추적 PID일 때만 인정 (다른 RDP 세션과 구분)
                if (!pidMatch && !titleMatch)
                    return true;

                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        if (!string.Equals(p.ProcessName, "mstsc", StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (p.HasExited)
                            return true;
                        found = true;
                        return false;
                    }
                }
                catch
                {
                    return true;
                }
            }, IntPtr.Zero);

            return found;
        }

        private bool TitleMatchesSession(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            foreach (string needle in GetSessionNeedles())
            {
                if (!string.IsNullOrEmpty(needle)
                    && title.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private List<string> GetSessionNeedles()
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(TargetIp))
                list.Add(TargetIp.Trim());
            if (!string.IsNullOrWhiteSpace(RemoteComputerName))
                list.Add(RemoteComputerName.Trim());
            if (!string.IsNullOrWhiteSpace(_rdpFileNeedle))
            {
                list.Add(_rdpFileNeedle);
                string noExt = Path.GetFileNameWithoutExtension(_rdpFileNeedle);
                if (!string.IsNullOrWhiteSpace(noExt))
                    list.Add(noExt);
                // cpub-HOBCARE07-QuickSession... → HOBCARE07
                if (!string.IsNullOrEmpty(noExt) && noExt.StartsWith("cpub-", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = noExt.Substring(5);
                    int dash = rest.IndexOf('-');
                    if (dash > 0)
                        list.Add(rest.Substring(0, dash));
                }
            }
            return list
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
    }
}
