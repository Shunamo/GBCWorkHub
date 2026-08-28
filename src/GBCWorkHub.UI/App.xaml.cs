using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace GBCWorkHub.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

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
    }
}
