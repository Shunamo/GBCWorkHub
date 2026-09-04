using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.Update
{
    /// <summary>
    /// Public HTTP(S) manifest/package download (GitHub Releases static URLs or any HTTPS).
    /// No auth tokens. Follows redirects (GitHub asset CDN).
    /// </summary>
    public sealed class HttpUpdateSource : IUpdateSource
    {
        private readonly string _manifestUrl;
        private readonly HttpClient _http;

        static HttpUpdateSource()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public HttpUpdateSource(string manifestUrl, HttpClient httpClient = null)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
                throw new ArgumentException("manifestUrl required", "manifestUrl");
            _manifestUrl = manifestUrl.Trim();
            if (httpClient != null)
            {
                _http = httpClient;
            }
            else
            {
                var handler = new HttpClientHandler { AllowAutoRedirect = true };
                _http = new HttpClient(handler);
                _http.DefaultRequestHeaders.UserAgent.Clear();
                _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GBCWorkHub", "1.0"));
                _http.Timeout = TimeSpan.FromMinutes(10);
            }
        }

        public string ManifestUrl
        {
            get { return _manifestUrl; }
        }

        public async Task<UpdateManifest> GetLatestAsync(CancellationToken cancellationToken)
        {
            using (var response = await _http.GetAsync(_manifestUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                    throw new InvalidOperationException("Invalid remote version.json");
                return manifest;
            }
        }

        public async Task DownloadPackageAsync(UpdateManifest manifest, string destinationFile, CancellationToken cancellationToken)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");
            string url = manifest.PackageUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                var baseUri = new Uri(_manifestUrl);
                string file = !string.IsNullOrWhiteSpace(manifest.PackageFile)
                    ? manifest.PackageFile
                    : ("GBCWorkHub-v" + manifest.Version + ".zip");
                url = new Uri(baseUri, file).ToString();
            }

            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? Path.GetTempPath());
                using (var fs = File.Create(destinationFile))
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    await stream.CopyToAsync(fs, 81920, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
