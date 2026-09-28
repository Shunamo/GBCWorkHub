using System;
using System.IO;
using System.Text;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// 업데이트 적용 직전에 "새 버전 + 릴리즈 노트"를 저장해뒀다가, 업데이트로 재시작된 다음
    /// 딱 한 번 안내 팝업을 띄우기 위한 %LocalAppData%\GBCWorkHub\ 마커 파일.
    /// 읽으면 즉시 지워서 그 다음 실행부터는 다시 뜨지 않는다.
    /// </summary>
    public static class PendingReleaseNoticeStore
    {
        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "PendingReleaseNotice.txt");
            }
        }

        /// <summary>업데이트 적용 직전(재시작 전)에 호출. releaseNotes가 비어 있으면 아무것도 안 남긴다.</summary>
        public static void Save(string version, string releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(releaseNotes))
                return;
            try
            {
                string path = FilePath;
                string content = (version ?? string.Empty).Trim() + "\n" + releaseNotes;
                File.WriteAllText(path, content, Encoding.UTF8);
                DiagnosticLogger.Info("UPDATE", "Pending release notice saved to " + path + " for v" + version);
            }
            catch (Exception ex)
            {
                // 저장 실패해도 업데이트 자체 진행에는 영향 없음 — 안내 팝업만 못 뜨는 정도.
                DiagnosticLogger.Warn("UPDATE", "Pending release notice save failed: " + ex.Message);
            }
        }

        /// <summary>재시작 후 앱 시작 시 한 번 호출. 있으면 즉시 삭제하고 내용을 꺼내온다.</summary>
        public static bool TryConsume(out string version, out string releaseNotes)
        {
            version = null;
            releaseNotes = null;
            string path = FilePath;
            try
            {
                if (!File.Exists(path))
                {
                    DiagnosticLogger.Info("UPDATE", "No pending release notice file at " + path);
                    return false;
                }

                string content = File.ReadAllText(path, Encoding.UTF8);
                File.Delete(path);

                int idx = content.IndexOf('\n');
                if (idx < 0)
                {
                    DiagnosticLogger.Warn("UPDATE", "Pending release notice file malformed (no newline): " + path);
                    return false;
                }

                version = content.Substring(0, idx).Trim();
                releaseNotes = content.Substring(idx + 1);
                bool hasNotes = !string.IsNullOrWhiteSpace(releaseNotes);
                DiagnosticLogger.Info("UPDATE", "Pending release notice consumed for v" + version + ", hasNotes=" + hasNotes);
                return hasNotes;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("UPDATE", "Pending release notice consume failed at " + path + ": " + ex.Message);
                return false;
            }
        }
    }
}
