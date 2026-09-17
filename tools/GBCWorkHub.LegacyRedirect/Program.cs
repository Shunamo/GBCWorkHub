using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GBCWorkHub.LegacyRedirect
{
    /// <summary>
    /// 예전 이름(GBCWorkHub.exe)으로 설치되어 있던 앱이 자동 업데이트를 적용한 뒤 재실행하는 대상이다.
    /// 새 exe는 GBCWorkHub.UI.exe로 이름이 바뀌었으므로, 여기서 한 번 안내하고 그 exe로 넘겨준다.
    /// 이후부터는 UpdateManifest.MainExeFileName 덕분에 새 이름으로 정상적으로 자동 업데이트된다.
    /// </summary>
    internal static class Program
    {
        private const string RealExeName = "GBCWorkHub.UI.exe";

        [STAThread]
        private static void Main()
        {
            string installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string realExePath = Path.Combine(installDir, RealExeName);

            if (!File.Exists(realExePath))
            {
                MessageBox.Show(
                    "GBCWorkHub이 GBCWorkHub.UI로 이름이 변경되었습니다.\n\n" +
                    "새 프로그램 파일을 찾지 못했습니다. GitHub Releases에서 최신 버전을 다시 받아 주세요.",
                    "GBCWorkHub 업데이트 안내",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                "보안 정책에 따라 프로그램 이름이 GBCWorkHub.UI로 변경되었습니다.\n" +
                "확인을 누르면 새 프로그램이 바로 실행됩니다.",
                "GBCWorkHub 업데이트 안내",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Process.Start(new ProcessStartInfo
            {
                FileName = realExePath,
                WorkingDirectory = installDir,
                UseShellExecute = true
            });
        }
    }
}
