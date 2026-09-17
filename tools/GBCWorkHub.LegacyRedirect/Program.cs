using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace GBCWorkHub.LegacyRedirect
{
    /// <summary>
    /// 예전 이름(GBCWorkHub.exe)으로 설치되어 있던 앱이 자동 업데이트를 적용한 뒤 재실행하는 대상이다.
    /// 새 exe는 보안팀 승인을 받은 이름(GBCWorkHub.UI.exe)으로 바뀌었으므로, 여기서는 그냥 실행하지
    /// 않고 GitHub Releases 다운로드 페이지로 안내한다 — 사용자가 직접 새로 받아 실행해야 한다.
    /// 이후부터는 UpdateManifest.MainExeFileName 덕분에 새 이름으로 정상적으로 자동 업데이트된다.
    /// </summary>
    internal static class Program
    {
        private const string ReleasesUrl = "https://github.com/Shunamo/GBCWorkHub/releases/latest";

        [STAThread]
        private static void Main()
        {
            MessageBox.Show(
                "보안 정책에 따라 프로그램 이름이 GBCWorkHub.UI로 변경되어, 이번 한 번은 자동 업데이트로 적용되지 않습니다.\n\n" +
                "확인을 누르면 다운로드 페이지가 열립니다. 그곳에서 최신 버전을 새로 받아 실행해 주세요.",
                "GBCWorkHub 업데이트 안내",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ReleasesUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // 브라우저를 못 열어도 안내 문구는 이미 봤으니 무시.
            }
        }
    }
}
