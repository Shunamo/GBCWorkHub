using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// CMC: 창 제목 + Chrome 탭 이름에서 rdp.ma / PMP RDP SESSION 을 찾는다.
    /// 다른 창으로 포커스만 옮겨도 탭/창이 남아 있으면 점유 유지.
    /// RDP 창·탭을 닫아 신호가 없으면 점유 해제.
    /// </summary>
    public sealed class CmcPmpSessionMonitor : IDisposable
    {
        private const uint EventObjectCreate = 0x8000;
        private const uint EventObjectDestroy = 0x8001;
        private const uint EventObjectShow = 0x8002;
        private const uint EventObjectHide = 0x8003;
        private const uint EventObjectNameChange = 0x800C;
        private const uint WineventOutofcontext = 0x0000;
        private const int ObjidWindow = 0;
        private const int HeartbeatMs = 1000;

        private static readonly string[] BrowserProcessNames =
        {
            "chrome", "msedge", "firefox", "iexplore", "brave", "opera"
        };

        /// <summary>실제 웹 RDP 세션 창 제목에만 나오는 마커 (목록/AutoLogon 페이지와 구분)</summary>
        private static readonly string[] RdpSessionMarkers =
        {
            "PMP RDP SESSION",
            "rdp.ma"
        };

        private readonly object _gate = new object();
        private readonly WinEventDelegate _winEventCallback;

        private string _shareKey;
        private string _pcName;
        /// <summary>PC명 등 identity. 단독 매칭하지 않고 RDP 마커와 함께 쓴다.</summary>
        private string[] _identityHints;
        private bool _lastVisible;
        private bool _disposed;
        private IntPtr _hookShow;
        private IntPtr _hookHide;
        private IntPtr _hookName;
        private Timer _heartbeatTimer;
        /// <summary>rdp.ma / PMP RDP SESSION 으로 잡힌 창. 이 HWND가 살아 있는 동안 점유 유지.</summary>
        private IntPtr _trackedHwnd;
        private bool _mismatchNotified;

        /// <summary>웹 RDP 세션이 처음 보일 때 (shareKey)</summary>
        public event Action<string> SessionAppeared;

        /// <summary>세션이 안 보이기 시작했을 때 (하위 호환, 현재는 Lost와 함께 즉시 발생)</summary>
        public event Action<string> SessionMissing;

        /// <summary>웹 RDP 세션이 사라질 때 (shareKey) — 유예 없이 즉시</summary>
        public event Action<string> SessionLost;

        /// <summary>감시 중인 PC가 아닌 웹 RDP가 보일 때 (shareKey, expectedPc, windowTitle)</summary>
        public event Action<string, string, string> MismatchedSessionDetected;

        public CmcPmpSessionMonitor()
        {
            // GC에 콜백이 수거되지 않도록 인스턴스 필드로 유지
            _winEventCallback = OnWinEvent;
        }

        public bool IsWatching
        {
            get
            {
                lock (_gate)
                    return !string.IsNullOrWhiteSpace(_shareKey);
            }
        }

        public string WatchedShareKey
        {
            get
            {
                lock (_gate)
                    return _shareKey;
            }
        }

        public bool IsSessionVisible
        {
            get
            {
                lock (_gate)
                    return _lastVisible;
            }
        }

        public void StartWatching(string shareKey, string pcName)
        {
            if (string.IsNullOrWhiteSpace(shareKey))
                throw new ArgumentException("shareKey required", "shareKey");

            string key = shareKey.Trim();
            string name = string.IsNullOrWhiteSpace(pcName) ? key : pcName.Trim();
            string[] identity = BuildIdentityHints(name);

            lock (_gate)
            {
                if (_disposed)
                    return;

                _shareKey = key;
                _pcName = name;
                _identityHints = identity;
                _trackedHwnd = IntPtr.Zero;
                _mismatchNotified = false;
                EnsureHooksLocked();
                EnsureHeartbeatLocked();
            }

            // 이미 떠 있는 창이 있으면 즉시 반영
            EvaluateFromSnapshot("start");

            DiagnosticLogger.Info("CMC_PMP_MONITOR",
                "Watch start (WinEvent) ShareKey=" + key + " Pc=" + name
                + " Identity=" + string.Join("|", identity)
                + " SessionMarkers=" + string.Join("|", RdpSessionMarkers));
        }

        public void StopWatching()
        {
            lock (_gate)
            {
                _shareKey = null;
                _pcName = null;
                _identityHints = null;
                _lastVisible = false;
                _trackedHwnd = IntPtr.Zero;
                _mismatchNotified = false;
                StopHeartbeatLocked();
                UnhookLocked();
            }

            DiagnosticLogger.Info("CMC_PMP_MONITOR", "Watch stopped");
        }

        public bool IsSessionVisibleFor(string shareKey)
        {
            if (string.IsNullOrWhiteSpace(shareKey))
                return false;
            lock (_gate)
            {
                return _lastVisible
                    && string.Equals(_shareKey, shareKey.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool ProbeSessionVisible(string pcName)
        {
            if (string.IsNullOrWhiteSpace(pcName))
                return false;

            string[] identity = BuildIdentityHints(pcName.Trim());
            // 1) 창 제목에 rdp.ma / PMP RDP SESSION
            if (FindMatchingWindowTitle(identity) != null)
                return true;

            // 2) 백그라운드 탭에만 있는 경우 (제목은 다른 탭)
            IntPtr hwnd;
            string tabName;
            return TryFindWindowWithRdpTab(identity, out hwnd, out tabName);
        }

        /// <summary>PC명 무관 — 화면에 CMC 웹 RDP(rdp.ma) 신호가 있는지.</summary>
        public static bool ProbeAnyRdpSessionVisible()
        {
            return !string.IsNullOrWhiteSpace(TryGetLiveRdpSignalText());
        }

        /// <summary>현재 보이는 rdp.ma / PMP RDP SESSION 창·탭 텍스트 (없으면 null).</summary>
        public static string TryGetLiveRdpSignalText()
        {
            string[] emptyHints = new string[0];
            string title = FindMatchingWindowTitle(emptyHints);
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            IntPtr hwnd;
            string tabName;
            if (TryFindWindowWithRdpTab(emptyHints, out hwnd, out tabName)
                && !string.IsNullOrWhiteSpace(tabName))
                return tabName;

            return null;
        }

        private void EnsureHooksLocked()
        {
            if (_hookShow != IntPtr.Zero)
                return;

            // SHOW~NAMECHANGE 범위를 나눠 등록 (불필요한 EVENT 폭주 완화)
            _hookShow = SetWinEventHook(
                EventObjectCreate, EventObjectShow,
                IntPtr.Zero, _winEventCallback, 0, 0, WineventOutofcontext);
            _hookHide = SetWinEventHook(
                EventObjectDestroy, EventObjectHide,
                IntPtr.Zero, _winEventCallback, 0, 0, WineventOutofcontext);
            _hookName = SetWinEventHook(
                EventObjectNameChange, EventObjectNameChange,
                IntPtr.Zero, _winEventCallback, 0, 0, WineventOutofcontext);

            DiagnosticLogger.Info("CMC_PMP_MONITOR",
                "Hooks installed show=" + (_hookShow != IntPtr.Zero)
                + " hide=" + (_hookHide != IntPtr.Zero)
                + " name=" + (_hookName != IntPtr.Zero));
        }

        private void UnhookLocked()
        {
            if (_hookShow != IntPtr.Zero)
            {
                UnhookWinEvent(_hookShow);
                _hookShow = IntPtr.Zero;
            }
            if (_hookHide != IntPtr.Zero)
            {
                UnhookWinEvent(_hookHide);
                _hookHide = IntPtr.Zero;
            }
            if (_hookName != IntPtr.Zero)
            {
                UnhookWinEvent(_hookName);
                _hookName = IntPtr.Zero;
            }
        }

        private void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            // 최상위 창만 (자식 컨트롤 폭주 무시)
            if (idObject != ObjidWindow || hwnd == IntPtr.Zero)
                return;

            try
            {
                // RDP 추적 창이 실제로 파괴된 경우에만 즉시 해제
                // (Hide는 최소화 등 — 창이 살아 있으면 점유 유지)
                if (eventType == EventObjectDestroy)
                {
                    bool trackedHit;
                    lock (_gate)
                    {
                        trackedHit = _trackedHwnd != IntPtr.Zero && hwnd == _trackedHwnd;
                        if (trackedHit)
                            _trackedHwnd = IntPtr.Zero;
                    }

                    if (trackedHit)
                    {
                        ApplyVisible(false, null, "tracked-destroy", IntPtr.Zero);
                        return;
                    }

                    EvaluateFromSnapshot("destroy-other");
                    return;
                }

                if (eventType == EventObjectHide)
                {
                    EvaluateFromSnapshot("hide");
                    return;
                }

                if (!IsWindowVisible(hwnd))
                    return;

                if (!IsBrowserWindow(hwnd))
                    return;

                string title = GetTitle(hwnd);
                string[] identity;
                lock (_gate)
                {
                    if (_disposed || string.IsNullOrWhiteSpace(_shareKey))
                        return;
                    identity = _identityHints;
                }

                if (IsRdpSessionTitle(title, identity))
                    ApplyVisible(true, title, "event", hwnd);
                else if (eventType == EventObjectNameChange)
                    EvaluateFromSnapshot("namechange-nomatch");
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("CMC_PMP_MONITOR", "WinEvent: " + ex.Message);
            }
        }

        private void EvaluateFromSnapshot(string reason)
        {
            string[] identity;
            IntPtr tracked;
            lock (_gate)
            {
                if (_disposed || string.IsNullOrWhiteSpace(_shareKey))
                    return;
                identity = _identityHints;
                tracked = _trackedHwnd;
            }

            IntPtr matchedHwnd;
            string matchedTitle;
            bool clearTracked;
            bool visible = TryResolveSessionWindow(
                identity, tracked, out matchedHwnd, out matchedTitle, out clearTracked);
            if (clearTracked)
            {
                lock (_gate)
                    _trackedHwnd = IntPtr.Zero;
            }

            if (!visible)
                MaybeNotifyMismatchedRdp(identity);

            ApplyVisible(visible, matchedTitle, reason, matchedHwnd);
        }

        private void ApplyVisible(bool visible, string matchedTitle, string reason, IntPtr matchedHwnd)
        {
            Action<string> appeared = null;
            Action<string> missing = null;
            Action<string> lost = null;
            string shareKey = null;

            lock (_gate)
            {
                if (_disposed || string.IsNullOrWhiteSpace(_shareKey))
                    return;

                shareKey = _shareKey;

                if (visible)
                {
                    // 제목이 RDP인 창을 우선 추적. sticky 유지 중 제목만 바뀐 경우 HWND는 유지.
                    if (matchedHwnd != IntPtr.Zero)
                    {
                        IntPtr prev = _trackedHwnd;
                        bool titleLooksRdp = IsRdpSessionTitle(matchedTitle ?? string.Empty, _identityHints);
                        if (titleLooksRdp || _trackedHwnd == IntPtr.Zero)
                            _trackedHwnd = matchedHwnd;

                        if (!_lastVisible)
                        {
                            _lastVisible = true;
                            appeared = SessionAppeared;
                            DiagnosticLogger.Info("CMC_PMP_MONITOR",
                                "Session visible ShareKey=" + shareKey
                                + " Title=" + (matchedTitle ?? "-")
                                + " hwnd=" + _trackedHwnd.ToInt64().ToString("X")
                                + " via=" + reason);
                        }
                        else if (prev != _trackedHwnd)
                        {
                            DiagnosticLogger.Info("CMC_PMP_MONITOR",
                                "Session track updated ShareKey=" + shareKey
                                + " hwnd=" + _trackedHwnd.ToInt64().ToString("X")
                                + " via=" + reason);
                        }
                    }
                    else if (!_lastVisible)
                    {
                        _lastVisible = true;
                        appeared = SessionAppeared;
                    }
                }
                else if (_lastVisible)
                {
                    _lastVisible = false;
                    _trackedHwnd = IntPtr.Zero;
                    missing = SessionMissing;
                    lost = SessionLost;
                    DiagnosticLogger.Info("CMC_PMP_MONITOR",
                        "Session lost ShareKey=" + shareKey + " via=" + reason);
                }
            }

            Raise(appeared, shareKey, "Appeared");
            Raise(missing, shareKey, "Missing");
            Raise(lost, shareKey, "Lost");
        }

        private void EnsureHeartbeatLocked()
        {
            if (_heartbeatTimer != null)
                return;
            _heartbeatTimer = new Timer(_ =>
            {
                try { EvaluateFromSnapshot("heartbeat"); }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("CMC_PMP_MONITOR", "Heartbeat: " + ex.Message);
                }
            }, null, HeartbeatMs, HeartbeatMs);
        }

        private void StopHeartbeatLocked()
        {
            if (_heartbeatTimer == null)
                return;
            try { _heartbeatTimer.Dispose(); }
            catch { /* ignore */ }
            _heartbeatTimer = null;
        }

        /// <summary>
        /// 1) 창 제목에 RDP 마커
        /// 2) 없으면 브라우저 탭 이름에 RDP 마커 (백그라운드 탭 포함)
        /// 3) 추적 창이 살아 있어도 제목·탭에 마커가 없으면 해제 (탭/창 닫힘)
        /// </summary>
        private static bool TryResolveSessionWindow(
            string[] identityHints,
            IntPtr trackedHwnd,
            out IntPtr hwnd,
            out string title,
            out bool clearTracked)
        {
            hwnd = IntPtr.Zero;
            title = null;
            clearTracked = false;

            IntPtr matchedHwnd;
            string matchedTitle;
            if (TryFindMatchingWindow(identityHints, out matchedHwnd, out matchedTitle))
            {
                hwnd = matchedHwnd;
                title = matchedTitle;
                return true;
            }

            // 추적 중이던 창에서 탭/제목 신호 확인 (빠름)
            if (trackedHwnd != IntPtr.Zero && IsWindow(trackedHwnd))
            {
                string signal;
                if (HasRdpSessionSignal(trackedHwnd, identityHints, out signal))
                {
                    hwnd = trackedHwnd;
                    title = signal;
                    return true;
                }
            }

            // 다른 브라우저 창의 백그라운드 탭
            if (TryFindWindowWithRdpTab(identityHints, out matchedHwnd, out matchedTitle))
            {
                hwnd = matchedHwnd;
                title = matchedTitle;
                return true;
            }

            // RDP 창·탭 신호 없음 → 점유 해제
            if (trackedHwnd != IntPtr.Zero)
                clearTracked = true;
            return false;
        }

        /// <summary>창 제목 또는 하위 탭 이름에 RDP 세션 신호가 있는지.</summary>
        private static bool HasRdpSessionSignal(IntPtr hwnd, string[] identityHints, out string signal)
        {
            signal = null;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return false;

            string windowTitle = GetTitle(hwnd);
            if (LooksLikeRdpSessionWindow(windowTitle))
            {
                signal = windowTitle;
                return true;
            }

            string tabName;
            if (TryFindRdpTabName(hwnd, identityHints, out tabName))
            {
                signal = tabName;
                return true;
            }

            return false;
        }

        private static bool TryFindWindowWithRdpTab(
            string[] identityHints,
            out IntPtr hwnd,
            out string tabName)
        {
            hwnd = IntPtr.Zero;
            tabName = null;

            var browserPids = GetBrowserPids();
            if (browserPids.Count == 0)
                return false;

            IntPtr found = IntPtr.Zero;
            string foundName = null;
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindow(hWnd))
                    return true;

                int pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (!browserPids.Contains(pid))
                    return true;

                // 빈 제목 툴 윈도우 등 스킵
                string title = GetTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title) && !IsWindowVisible(hWnd))
                    return true;

                string name;
                if (!TryFindRdpTabName(hWnd, identityHints, out name))
                    return true;

                found = hWnd;
                foundName = name;
                return false;
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
                return false;

            hwnd = found;
            tabName = foundName;
            return true;
        }

        private static bool TryFindRdpTabName(IntPtr hwnd, string[] identityHints, out string tabName)
        {
            tabName = null;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                if (root == null)
                    return false;

                var cond = new PropertyCondition(
                    AutomationElement.ControlTypeProperty, ControlType.TabItem);
                AutomationElementCollection tabs = root.FindAll(TreeScope.Descendants, cond);
                if (tabs == null || tabs.Count == 0)
                    return false;

                foreach (AutomationElement tab in tabs)
                {
                    if (tab == null)
                        continue;
                    string name;
                    try { name = tab.Current.Name; }
                    catch { continue; }

                    if (LooksLikeRdpTabOrTitle(name, identityHints))
                    {
                        tabName = name;
                        return true;
                    }
                }
            }
            catch
            {
                // UIA 실패 시 제목 매칭만으로 폴백
            }

            return false;
        }

        /// <summary>선택한 PC명이 제목/탭에 있을 때만 인정. 다른 PC의 rdp.ma 는 제외.</summary>
        private static bool LooksLikeRdpTabOrTitle(string name, string[] identityHints)
        {
            if (!LooksLikeRdpSessionWindow(name))
                return false;
            if (identityHints == null || identityHints.Length == 0)
                return true;
            return HasIdentityHint(name, identityHints);
        }

        private void MaybeNotifyMismatchedRdp(string[] identity)
        {
            if (identity == null || identity.Length == 0)
                return;

            string foreign = FindForeignRdpTitle(identity);
            if (string.IsNullOrWhiteSpace(foreign))
                return;

            string shareKey;
            string expectedPc;
            Action<string, string, string> mismatch;
            lock (_gate)
            {
                if (_disposed || _mismatchNotified || string.IsNullOrWhiteSpace(_shareKey))
                    return;
                _mismatchNotified = true;
                shareKey = _shareKey;
                expectedPc = _pcName ?? _shareKey;
                mismatch = MismatchedSessionDetected;
            }

            DiagnosticLogger.Warn("CMC_PMP_MONITOR",
                "Mismatched web RDP ShareKey=" + shareKey
                + " expectedPc=" + expectedPc
                + " title=" + foreign);

            if (mismatch == null)
                return;
            try { mismatch(shareKey, expectedPc, foreign); }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("CMC_PMP_MONITOR", "mismatch handler: " + ex.Message);
            }
        }

        private static string FindForeignRdpTitle(string[] identityHints)
        {
            string found = null;
            var browserPids = GetBrowserPids();
            if (browserPids.Count == 0)
                return null;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;
                int pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (!browserPids.Contains(pid))
                    return true;
                string windowTitle = GetTitle(hWnd);
                if (!LooksLikeRdpSessionWindow(windowTitle))
                    return true;
                if (HasIdentityHint(windowTitle, identityHints))
                    return true;
                found = windowTitle;
                return false;
            }, IntPtr.Zero);

            return found;
        }

        private static void Raise(Action<string> handler, string shareKey, string label)
        {
            if (handler == null || string.IsNullOrWhiteSpace(shareKey))
                return;
            try { handler(shareKey); }
            catch (Exception ex) { DiagnosticLogger.Error("CMC_PMP_MONITOR", label + " handler: " + ex.Message); }
        }

        private static string[] BuildIdentityHints(string pcName)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(pcName))
                list.Add(pcName.Trim());

            string raw = ConfigurationManager.AppSettings["CMC." + pcName + ".MatchHints"]
                ?? ConfigurationManager.AppSettings["CMC.MatchHints"]
                ?? string.Empty;
            foreach (var part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string h = part.Trim();
                if (h.Length == 0)
                    continue;
                // 세션 마커/호스트는 identity로 쓰지 않음 (목록 페이지 오탐 방지)
                if (IsRdpSessionMarker(h) || IsPmpHostOrListHint(h))
                    continue;
                if (!list.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)))
                    list.Add(h);
            }

            return list.ToArray();
        }

        private static bool IsRdpSessionMarker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            foreach (var marker in RdpSessionMarkers)
            {
                if (string.Equals(value.Trim(), marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsPmpHostOrListHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string v = value.Trim();
            return ContainsIgnoreCase(v, "pmp.cmcdubai")
                || ContainsIgnoreCase(v, "STATE_ID")
                || ContainsIgnoreCase(v, "PassTrix")
                || ContainsIgnoreCase(v, "AutoLogon");
        }

        /// <summary>목록 페이지가 아닌 rdp.ma / PMP RDP SESSION 창·탭.</summary>
        private static bool LooksLikeRdpSessionWindow(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;
            if (IsPmpListTitleWithoutRdp(title))
                return false;
            return ContainsIgnoreCase(title, "rdp.ma")
                || ContainsIgnoreCase(title, "PMP RDP SESSION");
        }

        /// <summary>
        /// 웹 RDP 세션만 true. 목록(AutoLogon)만 연 상태는 false.
        /// 감시 중인 PC명이 제목에 있으면 매칭. 창이 하나뿐인 단독 rdp.ma 는
        /// TryFindMatchingWindow 에서 허용한다.
        /// </summary>
        private static bool IsRdpSessionTitle(string title, string[] identityHints)
        {
            if (!LooksLikeRdpSessionWindow(title))
                return false;

            if (identityHints == null || identityHints.Length == 0)
                return true;
            if (HasIdentityHint(title, identityHints))
                return true;

            return false;
        }

        /// <summary>PMP 목록/로그인 페이지 (실제 rdp.ma 세션 아님)</summary>
        private static bool IsPmpListTitleWithoutRdp(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;
            foreach (var marker in RdpSessionMarkers)
            {
                if (ContainsIgnoreCase(title, marker))
                    return false;
            }
            return ContainsIgnoreCase(title, "AutoLogon")
                || ContainsIgnoreCase(title, "PassTrix")
                || ContainsIgnoreCase(title, "pmp.cmcdubai");
        }

        private static bool HasIdentityHint(string title, string[] identityHints)
        {
            if (string.IsNullOrWhiteSpace(title) || identityHints == null)
                return false;
            foreach (var hint in identityHints)
            {
                if (!string.IsNullOrWhiteSpace(hint) && ContainsIgnoreCase(title, hint))
                    return true;
            }
            return false;
        }

        private static string FindMatchingWindowTitle(string[] identityHints)
        {
            IntPtr hwnd;
            string title;
            return TryFindMatchingWindow(identityHints, out hwnd, out title) ? title : null;
        }

        private static bool TryFindMatchingWindow(string[] identityHints, out IntPtr hwnd, out string title)
        {
            hwnd = IntPtr.Zero;
            title = null;

            var browserPids = GetBrowserPids();
            if (browserPids.Count == 0)
                return false;

            // rdp.ma 창을 최우선. PC명이 제목에 있으면 그 창, 창이 하나뿐이면 이름 없어도 허용.
            var namedRdpMa = new List<KeyValuePair<IntPtr, string>>();
            var unnamedRdpMa = new List<KeyValuePair<IntPtr, string>>();
            var namedOther = new List<KeyValuePair<IntPtr, string>>();
            var unnamedOther = new List<KeyValuePair<IntPtr, string>>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                int pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (!browserPids.Contains(pid))
                    return true;

                string windowTitle = GetTitle(hWnd);
                if (!LooksLikeRdpSessionWindow(windowTitle))
                    return true;

                bool named = HasIdentityHint(windowTitle, identityHints);
                var item = new KeyValuePair<IntPtr, string>(hWnd, windowTitle);
                if (ContainsIgnoreCase(windowTitle, "rdp.ma"))
                {
                    if (named)
                        namedRdpMa.Add(item);
                    else
                        unnamedRdpMa.Add(item);
                }
                else
                {
                    if (named)
                        namedOther.Add(item);
                    else
                        unnamedOther.Add(item);
                }
                return true;
            }, IntPtr.Zero);

            if (namedRdpMa.Count > 0)
            {
                hwnd = namedRdpMa[0].Key;
                title = namedRdpMa[0].Value;
                return true;
            }

            // 선택한 PC명이 제목에 없으면 점유하지 않음 (다른 PC 웹 RDP 오탐 방지)
            if (namedOther.Count > 0)
            {
                hwnd = namedOther[0].Key;
                title = namedOther[0].Value;
                return true;
            }

            bool requireName = identityHints != null && identityHints.Length > 0;
            if (requireName)
                return false;

            if (unnamedRdpMa.Count == 1)
            {
                hwnd = unnamedRdpMa[0].Key;
                title = unnamedRdpMa[0].Value;
                return true;
            }

            if (unnamedOther.Count == 1 && unnamedRdpMa.Count == 0)
            {
                hwnd = unnamedOther[0].Key;
                title = unnamedOther[0].Value;
                return true;
            }

            return false;
        }

        private static HashSet<int> GetBrowserPids()
        {
            var browserPids = new HashSet<int>();
            foreach (var name in BrowserProcessNames)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { browserPids.Add(p.Id); }
                        catch { /* ignore */ }
                        finally
                        {
                            try { p.Dispose(); } catch { /* ignore */ }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            return browserPids;
        }

        private static bool IsBrowserWindow(IntPtr hwnd)
        {
            int pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid <= 0)
                return false;

            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    string name = (p.ProcessName ?? string.Empty).ToLowerInvariant();
                    return BrowserProcessNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                return false;
            }
        }

        private static string GetTitle(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
                return false;
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _shareKey = null;
                _trackedHwnd = IntPtr.Zero;
                StopHeartbeatLocked();
                UnhookLocked();
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
    }
}
