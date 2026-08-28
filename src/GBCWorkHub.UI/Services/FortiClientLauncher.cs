using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// 로컬 FortiClient VPN 창 실행/활성화.
    /// 트레이만 떠 있는 경우에도 UI 창이 보이도록 바로가기·exe 실행 후 전면 활성화.
    /// </summary>
    public static class FortiClientLauncher
    {
        private const int SwRestore = 9;
        private static readonly string[] UiProcessNames = { "FortiClient", "FortiGui", "FortiVPN" };

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        public static LaunchResult TryLaunch()
        {
            // 이미 FortiClient 창이 있으면 전면으로
            if (TryActivateExistingWindow())
                return LaunchResult.Ok("(activated)");

            string configured = (ConfigurationManager.AppSettings["FortiClient.ExePath"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!File.Exists(configured))
                    return LaunchResult.Fail("설정된 FortiClient 경로를 찾을 수 없습니다.\n" + configured);
                var started = StartPath(configured);
                if (started.Succeeded)
                    TryActivateExistingWindow(waitMs: 1500);
                return started;
            }

            foreach (var path in DiscoverCandidates())
            {
                if (!File.Exists(path))
                    continue;

                var started = StartPath(path);
                if (!started.Succeeded)
                    continue;

                TryActivateExistingWindow(waitMs: 1500);
                return started;
            }

            return LaunchResult.Fail(
                "로컬 PC에서 FortiClient를 찾지 못했습니다.\n" +
                "App.config에 FortiClient.ExePath 를 넣어 주세요.\n" +
                @"(예: C:\ProgramData\Microsoft\Windows\Start Menu\Programs\FortiClient VPN\FortiClient VPN.lnk)");
        }

        private static bool TryActivateExistingWindow(int waitMs = 0)
        {
            if (waitMs > 0)
            {
                int elapsed = 0;
                while (elapsed < waitMs)
                {
                    if (ActivateFortiMainWindow())
                        return true;
                    Thread.Sleep(150);
                    elapsed += 150;
                }
                return ActivateFortiMainWindow();
            }

            return ActivateFortiMainWindow();
        }

        private static bool ActivateFortiMainWindow()
        {
            try
            {
                foreach (var name in UiProcessNames)
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            IntPtr hwnd = proc.MainWindowHandle;
                            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
                                continue;

                            if (IsIconic(hwnd))
                                ShowWindow(hwnd, SwRestore);
                            ShowWindow(hwnd, SwRestore);
                            SetForegroundWindow(hwnd);
                            return true;
                        }
                        catch
                        {
                            // next
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        /// <summary>Forti UI 메인 창이 보이는지 (트레이 프로세스는 제외).</summary>
        public static bool HasVisibleMainWindow()
        {
            try
            {
                foreach (var name in UiProcessNames)
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            IntPtr hwnd = proc.MainWindowHandle;
                            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                                return true;
                        }
                        catch
                        {
                            // next
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        /// <summary>
        /// Forti 메인 창이 뜬 뒤 닫힐 때까지 대기. 창을 닫으면 VPN 완료로 간주.
        /// UI 스레드를 막지 않도록 Task.Run 사용.
        /// </summary>
        public static Task<WaitResult> WaitUntilMainWindowClosedAsync(
            int appearTimeoutSeconds = 60,
            int closeTimeoutSeconds = 900)
        {
            return Task.Run(() => WaitUntilMainWindowClosed(appearTimeoutSeconds, closeTimeoutSeconds));
        }

        private static WaitResult WaitUntilMainWindowClosed(int appearTimeoutSeconds, int closeTimeoutSeconds)
        {
            if (appearTimeoutSeconds < 5)
                appearTimeoutSeconds = 5;
            if (closeTimeoutSeconds < 30)
                closeTimeoutSeconds = 30;

            const int pollMs = 400;
            bool sawWindow = HasVisibleMainWindow();

            if (!sawWindow)
            {
                int appearDeadline = Environment.TickCount + appearTimeoutSeconds * 1000;
                while (Environment.TickCount < appearDeadline)
                {
                    if (HasVisibleMainWindow())
                    {
                        sawWindow = true;
                        break;
                    }
                    Thread.Sleep(pollMs);
                }

                if (!sawWindow)
                {
                    return WaitResult.Fail(
                        "FortiClient 창이 열리지 않았습니다.\n창을 연 뒤 VPN 연결하고 창을 닫아 주세요.");
                }
            }

            // 창이 잠깐 깜빡이며 사라지는 경우 대비: 연속 N회 미검출 시 닫힘으로 판정
            const int closedStreakNeeded = 3;
            int closedStreak = 0;
            int closeDeadline = Environment.TickCount + closeTimeoutSeconds * 1000;

            while (Environment.TickCount < closeDeadline)
            {
                if (!HasVisibleMainWindow())
                {
                    closedStreak++;
                    if (closedStreak >= closedStreakNeeded)
                        return WaitResult.Ok();
                }
                else
                {
                    closedStreak = 0;
                }

                Thread.Sleep(pollMs);
            }

            return WaitResult.Fail(
                "FortiClient 창이 닫히기를 기다리는 중 시간이 초과되었습니다.\n"
                + "VPN 연결 후 Forti 창을 닫으면 원격 접속이 진행됩니다.");
        }

        public sealed class WaitResult
        {
            public bool Succeeded { get; private set; }
            public string Message { get; private set; }

            public static WaitResult Ok()
            {
                return new WaitResult { Succeeded = true, Message = "FortiClient 창이 닫혔습니다." };
            }

            public static WaitResult Fail(string message)
            {
                return new WaitResult { Succeeded = false, Message = message ?? "실패" };
            }
        }

        private static IEnumerable<string> DiscoverCandidates()
        {
            foreach (var lnk in FindStartMenuShortcuts())
                yield return lnk;

            foreach (var path in new[]
            {
                @"C:\Program Files\Fortinet\FortiClient\FortiClient.exe",
                @"C:\Program Files (x86)\Fortinet\FortiClient\FortiClient.exe",
                @"C:\Program Files\Fortinet\FortiClient\FortiVPN.exe",
                @"C:\Program Files (x86)\Fortinet\FortiClient\FortiVPN.exe"
            })
                yield return path;

            foreach (var exe in FindUnderFortinetFolders())
                yield return exe;
        }

        private static IEnumerable<string> FindStartMenuShortcuts()
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
            };

            var found = new List<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    foreach (var lnk in Directory.EnumerateFiles(root, "*Forti*.lnk", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileNameWithoutExtension(lnk) ?? string.Empty;
                        if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        found.Add(lnk);
                    }
                }
                catch
                {
                    // skip
                }
            }

            return found
                .OrderByDescending(p =>
                {
                    string n = Path.GetFileNameWithoutExtension(p) ?? string.Empty;
                    if (n.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
                    if (n.IndexOf("FortiClient", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                    return 0;
                })
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> FindUnderFortinetFolders()
        {
            var roots = new[]
            {
                @"C:\Program Files\Fortinet",
                @"C:\Program Files (x86)\Fortinet"
            };

            var names = new[] { "FortiClient.exe", "FortiVPN.exe" };
            var found = new List<string>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    foreach (var name in names)
                        found.AddRange(Directory.EnumerateFiles(root, name, SearchOption.AllDirectories));
                }
                catch
                {
                    // skip
                }
            }

            return found.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static LaunchResult StartPath(string path)
        {
            string ext = Path.GetExtension(path) ?? string.Empty;

            // .lnk: 바로가기 대상/작업폴더를 풀어서 exe처럼 실행 (창이 실제로 뜨도록)
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                ShortcutInfo shortcut;
                if (TryResolveShortcut(path, out shortcut) && !string.IsNullOrWhiteSpace(shortcut.TargetPath)
                    && File.Exists(shortcut.TargetPath))
                {
                    var fromLnk = StartExe(shortcut.TargetPath, shortcut.WorkingDirectory, shortcut.Arguments);
                    if (fromLnk.Succeeded)
                        return fromLnk;
                }

                // 바로가기 해석 실패 시 셸로 바로가기 자체 실행
                return StartViaShell(path, null);
            }

            if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.GetDirectoryName(path);
                return StartExe(path, dir, null);
            }

            return StartViaShell(path, null);
        }

        private static LaunchResult StartExe(string exePath, string workingDirectory, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Arguments = arguments ?? string.Empty
                };

                if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;
                else
                {
                    string dir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        psi.WorkingDirectory = dir;
                }

                Process.Start(psi);
                return LaunchResult.Ok(exePath);
            }
            catch (Exception ex)
            {
                // explorer로 한 번 더 시도
                var fallback = StartViaShell(exePath, workingDirectory);
                if (fallback.Succeeded)
                    return fallback;
                return LaunchResult.Fail("FortiClient 실행 실패: " + ex.Message);
            }
        }

        private static LaunchResult StartViaShell(string path, string workingDirectory)
        {
            try
            {
                // explorer가 시작메뉴와 동일하게 바로가기/앱을 열어 줌
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                        : workingDirectory
                });
                return LaunchResult.Ok(path);
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail("FortiClient 실행 실패: " + ex.Message);
            }
        }

        private static bool TryResolveShortcut(string lnkPath, out ShortcutInfo info)
        {
            info = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return false;

                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { lnkPath });

                if (shortcut == null)
                    return false;

                Type shortcutType = shortcut.GetType();
                string target = shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                string workDir = shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                string args = shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;

                if (string.IsNullOrWhiteSpace(target))
                    return false;

                info = new ShortcutInfo
                {
                    TargetPath = target.Trim(),
                    WorkingDirectory = (workDir ?? string.Empty).Trim(),
                    Arguments = (args ?? string.Empty).Trim()
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ShortcutInfo
        {
            public string TargetPath { get; set; }
            public string WorkingDirectory { get; set; }
            public string Arguments { get; set; }
        }

        public sealed class LaunchResult
        {
            public bool Succeeded { get; private set; }
            public string Message { get; private set; }
            public string ExePath { get; private set; }

            public static LaunchResult Ok(string path)
            {
                return new LaunchResult { Succeeded = true, ExePath = path, Message = "FortiClient를 실행했습니다." };
            }

            public static LaunchResult Fail(string message)
            {
                return new LaunchResult { Succeeded = false, Message = message ?? "실패" };
            }
        }
    }
}
