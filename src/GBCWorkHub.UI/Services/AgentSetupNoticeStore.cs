using System;
using System.IO;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// "초기 세팅 완료" 체크 안내를 한 번만(사용자가 "다시 보지 않기"를 누를 때까지) 보여주기 위한
    /// 로컬 %LocalAppData%\GBCWorkHub 마커 파일.
    /// </summary>
    public static class AgentSetupNoticeStore
    {
        private static string FlagPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "AgentSetupNoticeDismissed.flag");
            }
        }

        public static bool IsDismissed()
        {
            try
            {
                return File.Exists(FlagPath);
            }
            catch
            {
                return false;
            }
        }

        public static void MarkDismissed()
        {
            try
            {
                File.WriteAllText(FlagPath, DateTime.Now.ToString("s"));
            }
            catch
            {
                // 저장 실패해도 이번 세션 진행에는 영향 없음 — 다음 로그인에 다시 뜨는 정도.
            }
        }
    }
}
