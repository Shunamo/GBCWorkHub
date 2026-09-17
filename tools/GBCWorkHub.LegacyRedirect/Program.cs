using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace GBCWorkHub.LegacyRedirect
{
    /// <summary>
    /// 예전 이름(GBCWorkHub.exe)으로 설치되어 있던 앱이 자동 업데이트를 적용한 뒤 재실행하는 대상이다.
    /// 새 exe는 보안팀 승인을 받은 이름(GBCWorkHub.UI.exe)으로 바뀌었으므로, 여기서는 그냥 실행하지
    /// 않고 zip 직접 다운로드 링크로 안내한다 — 사용자가 직접 새로 받아 실행해야 한다.
    /// 릴리즈 페이지(여러 부가 산출물이 같이 나열됨)로 보내면 헷갈리므로, 이 stub이 실제로 배포되는
    /// 버전의 zip 주소를 직접 가리킨다(이번 전환 버전 전용 — 다음 릴리즈부터는 이 stub 자체를 뺀다).
    /// </summary>
    internal static class Program
    {
        private const string DirectDownloadUrl =
            "https://github.com/Shunamo/GBCWorkHub/releases/download/v1.2.7/GBCWorkHub-Setup-v1.2.7.zip";

        [STAThread]
        private static void Main()
        {
            MessageBox.Show(
                "보안 정책에 따라 프로그램 이름이 GBCWorkHub.UI로 변경되어, 이번 한 번은 자동 업데이트로 적용되지 않습니다.\n\n" +
                "확인을 누르면 새 프로그램 다운로드가 바로 시작됩니다. 받은 압축 파일을 풀어서 실행해 주세요.",
                "GBCWorkHub 업데이트 안내",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DirectDownloadUrl,
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
