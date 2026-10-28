using System;
using System.IO;
using System.Text;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// C:\GBCWorkHub\Logs\WorkHub-yyyyMMdd.log (비밀번호/connection string 금지).
    /// GBCWorkHub.DAC.WorkHubFileLogger / GBCWorkHub.UI.Services.DiagnosticLogger가
    /// 각자 독립적으로 들고 있던 완전히 동일한 구현을 하나로 모은 것(동작 변경 없음).
    /// UI는 DAC를 직접 참조하지 않는 계층 구조라, 양쪽이 공통으로 참조하는 DTO에 둔다.
    /// </summary>
    public static class WorkHubLogWriter
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
