using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// 보안팀 예외 정책이 폴더 경로 기준(C:\BESTCare\GBCWorkHub)이라, 사이트마다 설치 위치가
    /// 제각각일 수 있는 문제를 매 시작마다 스스로 교정한다. GBCWorkHub.LegacyRedirect(예전 이름
    /// stub)의 재배치 로직과 같은 아이디어지만, 이름 변경 안내 팝업 없이 조용히 동작한다.
    /// </summary>
    internal static class StandardInstallLocation
    {
        private const string RealExeName = "GBCWorkHub.UI.exe";
        private const string UpdaterExeName = "GBCWorkHubUpdater.exe";
        private const string LegacyStubExeName = "GBCWorkHub.exe";
        private const string CleanupArg = "--cleanup-old-install";

        // LegacyRedirect/Program.cs의 StandardInstallDir과 동일하게 유지할 것.
        private static readonly string StandardInstallDir = @"C:\BESTCare\GBCWorkHub";

        /// <summary>
        /// true를 반환하면 표준 경로에서 새 프로세스가 이미 떴다는 뜻이므로,
        /// 호출자는 창을 띄우지 말고 즉시 Shutdown()해야 한다.
        /// </summary>
        public static bool RelocateIfNeeded()
        {
#if DEBUG
            // 개발 체크아웃(bin\Debug)에서 F5로 실행할 때마다 표준 경로로 끌려가면 안 되므로
            // Debug 빌드에서는 완전히 비활성화한다. 실제 배포본은 항상 Release로 패키징된다.
            return false;
#else
            try
            {
                string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrWhiteSpace(currentDir))
                    return false;

                if (string.Equals(
                        currentDir.TrimEnd('\\'),
                        StandardInstallDir.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                Directory.CreateDirectory(StandardInstallDir);
                bool movedReal = CopyIfExists(currentDir, StandardInstallDir, RealExeName);
                CopyIfExists(currentDir, StandardInstallDir, UpdaterExeName);
                if (!movedReal)
                    return false;

                DiagnosticLogger.Info("Relocate", "Moved install from " + currentDir + " to " + StandardInstallDir);

                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(StandardInstallDir, RealExeName),
                    WorkingDirectory = StandardInstallDir,
                    UseShellExecute = true,
                    Arguments = CleanupArg + " \"" + currentDir + "\" " + Process.GetCurrentProcess().Id
                });
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("Relocate", "Failed, staying in place: " + ex.Message);
                return false;
            }
#endif
        }

        /// <summary>
        /// 재배치 직후 새로 뜬(표준 경로) 프로세스에서 호출. 옛 프로세스가 완전히 종료되길
        /// 기다린 뒤 옛 폴더에 남은 exe 사본들을 정리한다. 실패해도 무해하므로 전부 무시한다.
        /// </summary>
        public static void CleanupOldInstallIfRequested(string[] args)
        {
            if (args == null)
                return;

            int idx = Array.IndexOf(args, CleanupArg);
            if (idx < 0 || idx + 2 >= args.Length)
                return;

            string oldDir = args[idx + 1];
            int oldPid;
            if (!int.TryParse(args[idx + 2], out oldPid))
                return;

            Task.Run(() => CleanupOldInstall(oldDir, oldPid));
        }

        private static void CleanupOldInstall(string oldDir, int oldPid)
        {
            try
            {
                try
                {
                    using (var p = Process.GetProcessById(oldPid))
                    {
                        p.WaitForExit(15000);
                    }
                }
                catch (ArgumentException)
                {
                    // 이미 종료됨 — 그대로 진행.
                }

                System.Threading.Thread.Sleep(500); // 파일 락 해제 대기.

                TryDelete(Path.Combine(oldDir, RealExeName));
                TryDelete(Path.Combine(oldDir, UpdaterExeName));
                TryDelete(Path.Combine(oldDir, LegacyStubExeName));

                DiagnosticLogger.Info("Relocate", "Cleaned up old install at " + oldDir);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("Relocate", "Cleanup of " + oldDir + " failed: " + ex.Message);
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
                // 지우기 실패해도(사용 중 등) 무시 — 다음 재배치 시 다시 시도됨.
            }
        }
    }
}
