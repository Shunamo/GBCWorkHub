using System;
using System.IO;
using System.Text;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// C:\GBCWorkHub\Logs\WorkHub-yyyyMMdd.log 진단 로그
    /// </summary>
    public static class DiagnosticLogger
    {
        private static readonly object Sync = new object();
        private const string LogRoot = @"C:\GBCWorkHub\Logs";

        public static void Info(string stage, string message)
        {
            Write("INFO", stage, message);
        }

        public static void Warn(string stage, string message)
        {
            Write("WARN", stage, message);
        }

        public static void Error(string stage, string message)
        {
            Write("ERROR", stage, message);
        }

        private static void Write(string level, string stage, string message)
        {
            try
            {
                if (!Directory.Exists(LogRoot))
                    Directory.CreateDirectory(LogRoot);

                string path = Path.Combine(LogRoot, "WorkHub-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                string line = string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}{4}",
                    DateTime.Now,
                    level,
                    stage ?? "-",
                    message ?? string.Empty,
                    Environment.NewLine);

                lock (Sync)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // 로그 실패로 앱 종료 금지
            }
        }
    }
}
