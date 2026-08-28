using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using GBCWorkHub.BIZ;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// Win32 AddClipboardFormatListener 기반 클립보드 변경 감지.
    /// Prefix는 긴/구체적 것부터 검사한다.
    /// </summary>
    public sealed class ClipboardMonitorService : IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private readonly Window _window;
        private HwndSource _hwndSource;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _isHooked;
        private bool _isDisposed;

        /// <summary>RDP 상태 GBCWORKHUB::</summary>
        public event Action<string> GbcClipboardReceived;

        /// <summary>TFS GBCWORKHUB_TFS::</summary>
        public event Action<string> TfsClipboardReceived;

        /// <summary>Dev-session two-point tracking result GBCWORKHUB_SESSION_RESULT::</summary>
        public event Action<string> SessionChangeResultReceived;

        public ClipboardMonitorService(Window window)
        {
            if (window == null)
                throw new ArgumentNullException("window");

            _window = window;
        }

        public void Start()
        {
            if (_isDisposed || _isHooked)
                return;

            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            _hwnd = helper.Handle;
            if (_hwnd == IntPtr.Zero)
                return;

            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource == null)
                return;

            _hwndSource.AddHook(WndProc);
            AddClipboardFormatListener(_hwnd);
            _isHooked = true;
            DiagnosticLogger.Info("ClipboardMonitor", "Start: AddClipboardFormatListener hwnd=" + _hwnd);
        }

        public void Stop()
        {
            if (!_isHooked)
                return;

            try
            {
                if (_hwnd != IntPtr.Zero)
                    RemoveClipboardFormatListener(_hwnd);
            }
            catch
            {
            }

            try
            {
                if (_hwndSource != null)
                    _hwndSource.RemoveHook(WndProc);
            }
            catch
            {
            }

            DiagnosticLogger.Info("ClipboardMonitor", "Stop: RemoveClipboardFormatListener");
            _hwndSource = null;
            _hwnd = IntPtr.Zero;
            _isHooked = false;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Stop();
            _isDisposed = true;
        }

        /// <summary>
        /// 현재 클립보드를 동일 ProcessClipboardText 파이프라인으로 재처리
        /// </summary>
        public void ReprocessCurrentClipboard()
        {
            DiagnosticLogger.Info("ClipboardMonitor", "ReprocessCurrentClipboard");
            string text;
            TryReadClipboard(3, 50, out text);
            ProcessClipboardText(text);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                try
                {
                    DiagnosticLogger.Info("ClipboardMonitor", "WM_CLIPBOARDUPDATE 수신");
                    string text;
                    TryReadClipboard(3, 50, out text);
                    ProcessClipboardText(text);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Error("ClipboardMonitor", "WM_CLIPBOARDUPDATE 처리 예외: " + ex.Message);
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Prefix 분기 (긴 것부터):
        /// 1. GBCWORKHUB_TFS::
        /// 2. GBCWORKHUB_ACK::
        /// 3. GBCWORKHUB_TFS_SYNC_REQUEST::
        /// 4. GBCWORKHUB::
        /// </summary>
        public void ProcessClipboardText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    DiagnosticLogger.Info("Decision", "IGNORE_NON_GBC_CLIPBOARD (empty/null)");
                    return;
                }

                string trimmed = text.TrimStart();

                // 0) Dev-session two-point tracking result (remote SessionAgent → here)
                const string sessionResultPrefix = "GBCWORKHUB_SESSION_RESULT::";
                if (trimmed.StartsWith(sessionResultPrefix, StringComparison.Ordinal))
                {
                    DiagnosticLogger.Info("ClipboardMonitor", "SESSION_RESULT prefix detected length=" + trimmed.Length);
                    var sessionResultHandler = SessionChangeResultReceived;
                    if (sessionResultHandler != null)
                        sessionResultHandler(trimmed);
                    else
                        DiagnosticLogger.Warn("ClipboardMonitor", "SessionChangeResultReceived 구독자 없음");
                    return;
                }

                // 0b) Session-token announce (로컬이 쓴 요청 — 재처리하지 않음)
                if (trimmed.StartsWith(TfsClipboardAckService.SessionTokenAnnouncePrefix, StringComparison.Ordinal))
                {
                    DiagnosticLogger.Info("ClipboardMonitor", "SESSION_TOKEN announce prefix ignored (local-authored)");
                    return;
                }

                // 1) TFS Payload
                if (trimmed.StartsWith(TfsClipboardPayloadService.ClipboardPrefix, StringComparison.Ordinal))
                {
                    int length = trimmed.Length;
                    string preview = trimmed.Length <= 100 ? trimmed : trimmed.Substring(0, 100);
                    DiagnosticLogger.Info("TFS_PREFIX_DETECTED",
                        "length=" + length + " preview=" + ReplaceNewlines(preview));

                    var tfsHandler = TfsClipboardReceived;
                    if (tfsHandler != null)
                        tfsHandler(trimmed);
                    else
                        DiagnosticLogger.Warn("ClipboardMonitor", "TfsClipboardReceived 구독자 없음");
                    return;
                }

                // 2) ACK (로컬/원격 확인용 — 상위 핸들러로 올리지 않음)
                if (trimmed.StartsWith(TfsClipboardAckService.AckPrefix, StringComparison.Ordinal))
                {
                    DiagnosticLogger.Info("ClipboardMonitor", "ACK prefix ignored (local/remote ack)");
                    return;
                }

                // 3) Sync request token (로컬이 쓴 요청 — 재처리하지 않음)
                if (trimmed.StartsWith(TfsClipboardAckService.SyncRequestPrefix, StringComparison.Ordinal))
                {
                    DiagnosticLogger.Info("ClipboardMonitor", "SYNC_REQUEST prefix ignored");
                    return;
                }

                // 4) RDP status (GBCWORKHUB:: JSON)
                if (trimmed.StartsWith(RdpStatusBiz.ClipboardPrefix, StringComparison.Ordinal))
                {
                    int length = trimmed.Length;
                    string preview = trimmed.Length <= 100 ? trimmed : trimmed.Substring(0, 100);
                    DiagnosticLogger.Info("ClipboardMonitor", "RDP 후보 수신 length=" + length + " preview=" + ReplaceNewlines(preview));

                    var handler = GbcClipboardReceived;
                    if (handler != null)
                        handler(trimmed);
                    else
                        DiagnosticLogger.Warn("ClipboardMonitor", "GbcClipboardReceived 구독자 없음");
                    return;
                }

                // 5) RC 한 줄: "ts | user | HO-BCARE-07 | connect"
                if (RdpStatusBiz.LooksLikeRcStatusLine(trimmed))
                {
                    string preview = trimmed.Length <= 120 ? trimmed : trimmed.Substring(0, 120);
                    DiagnosticLogger.Info("ClipboardMonitor", "RC_RDP_LINE 수신 preview=" + ReplaceNewlines(preview));

                    var handler = GbcClipboardReceived;
                    if (handler != null)
                        handler(trimmed);
                    else
                        DiagnosticLogger.Warn("ClipboardMonitor", "GbcClipboardReceived 구독자 없음");
                    return;
                }

                DiagnosticLogger.Info("Decision", "IGNORE_NON_GBC_CLIPBOARD");
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("ClipboardMonitor", "ProcessClipboardText 예외: " + ex.Message);
            }
        }

        private void TryReadClipboard(int maxRetry, int delayMs, out string text)
        {
            text = null;
            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    if (!Clipboard.ContainsText())
                    {
                        DiagnosticLogger.Info("ClipboardMonitor", "Clipboard.ContainsText=False (시도 " + (i + 1) + ")");
                        return;
                    }

                    text = Clipboard.GetText();
                    DiagnosticLogger.Info("ClipboardMonitor", "Clipboard.GetText 성공 (시도 " + (i + 1) + ")");
                    return;
                }
                catch (COMException ex)
                {
                    DiagnosticLogger.Warn("ClipboardMonitor", "Clipboard.GetText COMException 시도 " + (i + 1) + ": " + ex.Message);
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Warn("ClipboardMonitor", "Clipboard.GetText 실패 시도 " + (i + 1) + ": " + ex.Message);
                    Thread.Sleep(delayMs);
                }
            }

            DiagnosticLogger.Error("ClipboardMonitor", "Clipboard.GetText 최종 실패");
        }

        private static string ReplaceNewlines(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
