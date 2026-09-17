using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GBCWorkHub.LegacyRedirect
{
    /// <summary>
    /// 예전 이름(GBCWorkHub.exe)으로 설치되어 있던 앱이 자동 업데이트를 적용한 뒤 재실행하는 대상이다.
    /// 새 exe(GBCWorkHub.UI.exe)는 같은 업데이트 패키지 안에 이미 같이 들어있으므로, 추가로 다시
    /// 받을 필요 없이 바로 실행해 넘겨준다 — "업데이트 한 번"으로 끝나도록.
    ///
    /// 보안팀 예외 정책이 폴더 경로 기준(C:\BESTCare\GBCWorkHub)이라, 사이트마다 예전 설치 위치가
    /// 제각각이었을 수 있는 문제를 여기서 함께 해결한다: 지금 위치가 그 표준 경로가 아니면 새 파일을
    /// 그 경로로 옮긴 뒤 그곳에서 실행한다.
    /// </summary>
    internal static class Program
    {
        private const string RealExeName = "GBCWorkHub.UI.exe";
        private const string UpdaterExeName = "GBCWorkHubUpdater.exe";
        private static readonly string StandardInstallDir = @"C:\BESTCare\GBCWorkHub";
        private const string ReleasesUrl = "https://github.com/Shunamo/GBCWorkHub/releases/latest";

        [STAThread]
        private static void Main()
        {
            string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            bool relocated;
            string targetDir = ResolveTargetDir(currentDir, out relocated);
            string realExePath = Path.Combine(targetDir, RealExeName);

            if (!File.Exists(realExePath))
            {
                MessageBox.Show(
                    "보안 정책에 따라 프로그램 이름이 GBCWorkHub.UI로 변경되었습니다.\n" +
                    "새 프로그램을 찾지 못해 다운로드 페이지를 엽니다.",
                    "GBCWorkHub 업데이트 안내",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                OpenUrl(ReleasesUrl);
                return;
            }

            MessageBox.Show(
                "보안 정책에 따라 프로그램 이름이 GBCWorkHub.UI로 변경되었습니다.\n" +
                "위치: " + targetDir + "\n" +
                "확인을 누르면 새 프로그램이 바로 실행됩니다.",
                "GBCWorkHub 업데이트 안내",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Process.Start(new ProcessStartInfo
            {
                FileName = realExePath,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            });
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                // 브라우저를 못 열어도 안내 문구는 이미 봤으니 무시.
            }
        }

        /// <summary>
        /// 지금 폴더가 표준 설치 경로가 아니면, 새 exe들을 표준 경로로 옮기고 그 경로를 반환한다.
        /// 옮기다 실패하면(권한 문제 등) 안전하게 지금 폴더를 그대로 쓴다.
        /// </summary>
        private static string ResolveTargetDir(string currentDir, out bool relocated)
        {
            relocated = false;
            if (string.IsNullOrWhiteSpace(currentDir)
                || string.Equals(
                    currentDir.TrimEnd('\\'),
                    StandardInstallDir.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
            {
                return currentDir;
            }

            // 이미 예전에 한 번 이동을 마쳤다면(그때 예전 폴더의 사본은 지웠음) 이 stub은
            // 표준 경로에 남겨둔 흔적을 다시 실행한 것일 뿐이다 — 복사할 게 없으니 바로 표준
            // 경로를 쓴다. 이 검사가 없으면 "복사할 원본이 없다"는 이유로 잘못 실패 처리된다.
            if (File.Exists(Path.Combine(StandardInstallDir, RealExeName)))
                return StandardInstallDir;

            try
            {
                Directory.CreateDirectory(StandardInstallDir);
                bool movedReal = CopyIfExists(currentDir, StandardInstallDir, RealExeName);
                CopyIfExists(currentDir, StandardInstallDir, UpdaterExeName);
                if (!movedReal)
                    return currentDir;

                // 예전 폴더의 사본은 정리(안내용 stub 자신은 그대로 둬도 무해함 — 나중에 실수로
                // 다시 실행돼도 여기서 다시 표준 경로로 안내해주는 역할을 그대로 한다).
                TryDelete(Path.Combine(currentDir, RealExeName));
                TryDelete(Path.Combine(currentDir, UpdaterExeName));

                relocated = true;
                return StandardInstallDir;
            }
            catch
            {
                return currentDir;
            }
        }

        private static bool CopyIfExists(string fromDir, string toDir, string fileName)
        {
            string from = Path.Combine(fromDir, fileName);
            if (!File.Exists(from))
                return false;
            File.Copy(from, Path.Combine(toDir, fileName), true);
            return true;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 지우기 실패해도(사용 중 등) 무시 — 중복 파일이 남는 정도.
            }
        }
    }
}
