using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Services;

namespace GBCWorkHub.UI
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "GBCWorkHub_SingleInstance_9F3E2C7A";
        private static Mutex _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        static App()
        {
            BundledConfig.EnsureExtracted();
            BundledFonts.EnsureExtracted();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            if (!createdNew)
            {
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            BundledFonts.ApplyTo(this);
            LoadSiteTimeZones();
            CleanupLegacyRedirectStub();

            try
            {
                var window = new MainWindow();
                MainWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                WriteCrash(ex);
                MessageBox.Show(ex.ToString(), "GBC WorkHub — 시작 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        /// <summary>
        /// GBCWorkHub.exe → GBCWorkHub.UI.exe 이름 변경 과도기에 같은 폴더에 남을 수 있는
        /// 예전 이름 안내용 실행 파일(GBCWorkHub.LegacyRedirect 프로젝트 산출물)을 조용히 정리한다.
        /// 이 앱 자신은 항상 GBCWorkHub.UI.exe이므로 동일 폴더의 GBCWorkHub.exe는 항상 그 잔재다.
        /// </summary>
        private static void CleanupLegacyRedirectStub()
        {
            try
            {
                string installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrWhiteSpace(installDir))
                    return;
                string stubPath = Path.Combine(installDir, "GBCWorkHub.exe");
                if (File.Exists(stubPath))
                    File.Delete(stubPath);
            }
            catch
            {
                // 지우기 실패해도(사용 중 등) 다음 실행 때 다시 시도되므로 무시.
            }
        }

        /// <summary>이미 떠 있는 인스턴스의 창을 앞으로 가져온다 — 새 창은 안 띄우고 그냥 종료.</summary>
        private static void ActivateExistingInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id == current.Id)
                        continue;
                    IntPtr hWnd = p.MainWindowHandle;
                    if (hWnd == IntPtr.Zero)
                        continue;
                    if (IsIconic(hWnd))
                        ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                    break;
                }
            }
            catch
            {
            }
        }

        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrash(e.Exception);
            try
            {
                MessageBox.Show(
                    e.Exception != null ? e.Exception.ToString() : "unknown",
                    "GBC WorkHub — UI 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            WriteCrash(ex ?? new Exception(e.ExceptionObject != null ? e.ExceptionObject.ToString() : "unknown"));
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                string dir = @"C:\GBCWorkHub\Logs";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "Crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                var sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("o"));
                for (Exception cur = ex; cur != null; cur = cur.InnerException)
                {
                    sb.AppendLine(cur.GetType().FullName);
                    sb.AppendLine(cur.Message);
                    var xaml = cur as XamlParseException;
                    if (xaml != null)
                    {
                        sb.AppendLine("LineNumber=" + xaml.LineNumber);
                        sb.AppendLine("LinePosition=" + xaml.LinePosition);
                        sb.AppendLine("BaseUri=" + (xaml.BaseUri != null ? xaml.BaseUri.ToString() : ""));
                        sb.AppendLine("KeyContext=" + (xaml.KeyContext != null ? xaml.KeyContext.ToString() : ""));
                        sb.AppendLine("NameContext=" + (xaml.NameContext ?? ""));
                    }
                    sb.AppendLine(cur.StackTrace);
                    sb.AppendLine("---");
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void LoadSiteTimeZones()
        {
            string[] sites = { "CMC", "MNGHA", "RC", "AURORA" };
            foreach (string site in sites)
            {
                try
                {
                    string raw = ConfigurationManager.AppSettings["SiteTimeZone." + site];
                    if (!string.IsNullOrWhiteSpace(raw))
                        KoreaTime.RegisterSiteTimeZone(site, raw.Trim());
                }
                catch
                {
                }
            }
        }
    }
}
