using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// 점유 뺏김 알림. MainWindow 오버레이가 아니라 Topmost 독립 창이라 RDP 뒤에도 보이게 한다.
    /// </summary>
    internal static class OccupancyTakeoverAlert
    {
        public static Task ShowAsync(string title, string message)
        {
            var tcs = new TaskCompletionSource<bool>();
            var app = Application.Current;
            if (app == null || app.Dispatcher == null)
            {
                tcs.TrySetResult(false);
                return tcs.Task;
            }

            Action open = () =>
            {
                try
                {
                    Window win = BuildWindow(title, message, tcs);
                    win.Loaded += (s, e) =>
                    {
                        ForceForeground(win);
                    };
                    win.Show();
                    ForceForeground(win);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Warn("OCCUPANCY_TAKEOVER", "alert failed: " + ex.Message);
                    tcs.TrySetResult(false);
                }
            };

            if (app.Dispatcher.CheckAccess())
                open();
            else
                app.Dispatcher.BeginInvoke(open, DispatcherPriority.Send);

            return tcs.Task;
        }

        private static Window BuildWindow(string title, string message, TaskCompletionSource<bool> tcs)
        {
            var ink = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            var muted = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
            var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F67D8"));
            ink.Freeze();
            muted.Freeze();
            accent.Freeze();

            var titleBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(title) ? "점유가 해제되었습니다" : title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = ink,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var body = new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = 13,
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };
            var ok = new Button
            {
                Content = "확인",
                MinWidth = 88,
                Height = 34,
                Padding = new Thickness(16, 0, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = accent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = true
            };

            var root = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4F6FA")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22),
                Child = new StackPanel
                {
                    Children = { titleBlock, body, ok }
                }
            };

            var win = new Window
            {
                Title = titleBlock.Text,
                Content = root,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                Topmost = true,
                ShowInTaskbar = true,
                ShowActivated = true,
                Background = root.Background
            };
            win.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { win.DragMove(); }
                    catch { }
                }
            };
            RoutedEventHandler close = (s, e) =>
            {
                try { win.Close(); }
                catch { }
            };
            ok.Click += close;
            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Escape)
                    close(s, e);
            };
            win.Closed += (s, e) => tcs.TrySetResult(true);
            return win;
        }

        private static void ForceForeground(Window w)
        {
            if (w == null)
                return;
            try
            {
                if (w.WindowState == WindowState.Minimized)
                    w.WindowState = WindowState.Normal;
                w.Show();
                w.Activate();
                w.Topmost = false;
                w.Topmost = true;
                w.Focus();

                IntPtr hwnd = new WindowInteropHelper(w).EnsureHandle();
                if (hwnd == IntPtr.Zero)
                    return;

                ShowWindow(hwnd, 9);
                IntPtr fg = GetForegroundWindow();
                uint thisThread = GetCurrentThreadId();
                uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
                if (fgThread != 0 && fgThread != thisThread)
                    AttachThreadInput(thisThread, fgThread, true);
                SetForegroundWindow(hwnd);
                if (fgThread != 0 && fgThread != thisThread)
                    AttachThreadInput(thisThread, fgThread, false);

                var info = new FlashWInfo
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(FlashWInfo)),
                    hwnd = hwnd,
                    dwFlags = 3 | 12,
                    uCount = 8,
                    dwTimeout = 0
                };
                FlashWindowEx(ref info);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashWInfo pwfi);

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashWInfo
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }
    }
}
