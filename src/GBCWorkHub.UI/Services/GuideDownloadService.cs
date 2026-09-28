using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// 사이트별 원격 접속(VPN) 가이드 파일을 GitHub Release(태그 "guides", 항상 prerelease로
    /// 유지해서 앱 자동 업데이트가 참조하는 "latest" 릴리즈와 절대 안 겹치게 함)에서
    /// 필요할 때만 받아 %LocalAppData%\GBCWorkHub\Guides\에 캐싱한다.
    ///
    /// 관리자가 그 릴리즈의 첨부파일을 교체하면, 사용자는 다음에 가이드를 열 때
    /// (파일 크기 비교로 변경을 감지해서) 자동으로 최신 파일을 다시 받는다.
    /// </summary>
    public static class GuideDownloadService
    {
        private const string DownloadBaseUrl = "https://github.com/Shunamo/GBCWorkHub/releases/download/guides/";

        private static readonly Lazy<HttpClient> HttpLazy = new Lazy<HttpClient>(CreateClient);

        private static HttpClient Http
        {
            get { return HttpLazy.Value; }
        }

        private static HttpClient CreateClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            var http = new HttpClient(handler);
            http.DefaultRequestHeaders.UserAgent.Clear();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GBCWorkHub", "1.0"));
            http.Timeout = TimeSpan.FromMinutes(5);
            return http;
        }

        private static string CacheDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub", "Guides");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// fileName(예: "VPN-CMC.pptx")을 최신 상태로 로컬에 준비해서 그 경로를 반환한다.
        /// 캐시된 파일 크기가 원격과 같으면 재다운로드하지 않는다. 오프라인 등으로 원격 크기를
        /// 못 가져오면, 캐시가 있으면 그걸 그대로 쓰고 없으면 예외를 던진다.
        /// </summary>
        public static async Task<string> EnsureLocalCopyAsync(string fileName)
        {
            string localPath = Path.Combine(CacheDir, fileName);
            string url = DownloadBaseUrl + Uri.EscapeDataString(fileName);

            long remoteSize = -1;
            try
            {
                using (var head = new HttpRequestMessage(HttpMethod.Head, url))
                using (var response = await Http.SendAsync(head).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                        remoteSize = response.Content.Headers.ContentLength.Value;
                }
            }
            catch
            {
                // 네트워크 문제 등 — 아래에서 캐시로 대체 시도.
            }

            bool haveCache = File.Exists(localPath);
            if (haveCache && remoteSize >= 0 && new FileInfo(localPath).Length == remoteSize)
                return localPath;

            if (remoteSize < 0)
            {
                if (haveCache)
                    return localPath;
                throw new InvalidOperationException("가이드를 불러올 수 없습니다. 인터넷 연결을 확인해 주세요.");
            }

            string tempPath = localPath + ".tmp";
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var fs = File.Create(tempPath))
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    await stream.CopyToAsync(fs, 81920).ConfigureAwait(false);
                }
            }

            if (File.Exists(localPath))
                File.Delete(localPath);
            File.Move(tempPath, localPath);
            return localPath;
        }
    }
}
