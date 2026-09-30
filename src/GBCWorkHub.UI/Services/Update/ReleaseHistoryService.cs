using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.Update
{
    public sealed class SkippedReleaseNote
    {
        public string Version { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>업데이트 팝업의 "이전 릴리즈 변경사항 보기" 토글용 — 건너뛴 버전들의 릴리즈 노트를
    /// GitHub Releases API로 가져온다. 실패하거나 조회 대상이 없으면 빈 리스트를 돌려주고,
    /// 호출자는 그냥 토글 없이 최신 버전 팝업만 보여주면 된다.</summary>
    public static class ReleaseHistoryService
    {
        private sealed class GitHubReleaseDto
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("body")]
            public string Body { get; set; }

            [JsonProperty("draft")]
            public bool Draft { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }
        }

        /// <summary>oldVersion(제외) ~ newVersion(제외) 사이에 건너뛴 버전들의 노트를 최신순으로 반환한다.
        /// oldVersion을 모르거나, 커스텀 릴리즈 호스트를 쓰거나, API 호출이 실패하면 빈 리스트.</summary>
        public static async Task<List<SkippedReleaseNote>> GetSkippedNotesAsync(
            string oldVersionRaw, string newVersionRaw, CancellationToken cancellationToken)
        {
            var result = new List<SkippedReleaseNote>();
            try
            {
                if (UpdateService.HasCustomReleaseBaseUrl())
                    return result;

                Version oldV, newV;
                if (!TryParseVersion(oldVersionRaw, out oldV) || !TryParseVersion(newVersionRaw, out newV))
                    return result;
                if (oldV >= newV)
                    return result;

                string owner = UpdateService.ResolveReleaseOwner();
                string repo = UpdateService.ResolveReleaseRepo();
                if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
                    return result;

                string url = "https://api.github.com/repos/" + owner + "/" + repo + "/releases?per_page=30";

                using (var handler = new HttpClientHandler { AllowAutoRedirect = true })
                using (var http = new HttpClient(handler))
                {
                    http.DefaultRequestHeaders.UserAgent.Clear();
                    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GBCWorkHub", "1.0"));
                    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    http.Timeout = TimeSpan.FromSeconds(10);

                    using (var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return result;
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var releases = JsonConvert.DeserializeObject<List<GitHubReleaseDto>>(json);
                        if (releases == null)
                            return result;

                        foreach (var r in releases)
                        {
                            if (r == null || r.Draft || r.Prerelease)
                                continue;
                            Version v;
                            if (!TryParseVersion(r.TagName, out v))
                                continue;
                            if (v > oldV && v < newV)
                            {
                                result.Add(new SkippedReleaseNote
                                {
                                    Version = NormalizeVersionText(r.TagName),
                                    Notes = (r.Body ?? string.Empty).Trim()
                                });
                            }
                        }
                    }
                }

                result.Sort(delegate (SkippedReleaseNote a, SkippedReleaseNote b)
                {
                    Version va, vb;
                    TryParseVersion(a.Version, out va);
                    TryParseVersion(b.Version, out vb);
                    if (va == null || vb == null)
                        return string.CompareOrdinal(b.Version, a.Version);
                    return vb.CompareTo(va);
                });
            }
            catch
            {
                result.Clear();
            }
            return result;
        }

        private static bool TryParseVersion(string raw, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            string v = raw.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            return Version.TryParse(v, out version);
        }

        private static string NormalizeVersionText(string raw)
        {
            string v = (raw ?? string.Empty).Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            return v;
        }
    }
}
