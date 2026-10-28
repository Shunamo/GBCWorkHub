using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    /// <summary>
    /// C:\GBCWorkHub\Logs\WorkHub-yyyyMMdd.log (비밀번호/connection string 금지).
    /// GBCWorkHub.UI.Services.DiagnosticLogger와 완전히 동일한 구현이 중복돼 있던 것을 통합 —
    /// 같은 파일/포맷/락으로 기록하므로 동작 변화 없음.
    /// </summary>
    public static class WorkHubFileLogger
    {
        public static void Info(string stage, string message)
        {
            WorkHubLogWriter.Info(stage, message);
        }

        public static void Warn(string stage, string message)
        {
            WorkHubLogWriter.Warn(stage, message);
        }

        public static void Error(string stage, string message)
        {
            WorkHubLogWriter.Error(stage, message);
        }
    }
}
