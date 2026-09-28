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
                string content = (version ?? string.Empty).Trim() + "\n" + releaseNotes;
                File.WriteAllText(FilePath, content, Encoding.UTF8);
            }
            catch
            {
                // 저장 실패해도 업데이트 자체 진행에는 영향 없음 — 안내 팝업만 못 뜨는 정도.
            }
        }

        /// <summary>재시작 후 앱 시작 시 한 번 호출. 있으면 즉시 삭제하고 내용을 꺼내온다.</summary>
        public static bool TryConsume(out string version, out string releaseNotes)
        {
            version = null;
            releaseNotes = null;
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                    return false;

                string content = File.ReadAllText(path, Encoding.UTF8);
                File.Delete(path);

                int idx = content.IndexOf('\n');
                if (idx < 0)
                    return false;

                version = content.Substring(0, idx).Trim();
                releaseNotes = content.Substring(idx + 1);
                return !string.IsNullOrWhiteSpace(releaseNotes);
            }
            catch
            {
                return false;
            }
        }
    }
}
