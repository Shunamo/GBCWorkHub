using System;

namespace GBCWorkHub.UI.Services.Update
{
    /// <summary>
    /// Builds public GitHub Releases static download URLs (no API, no tokens).
    /// Source repo and release repo may differ via owner/repo or base URL.
    /// </summary>
    public static class GitHubReleaseUrls
    {
        public static string NormalizeBaseUrl(string ownerOrBase, string repo)
        {
            if (!string.IsNullOrWhiteSpace(ownerOrBase) &&
                (ownerOrBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 ownerOrBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return ownerOrBase.Trim().TrimEnd('/');
            }

            if (string.IsNullOrWhiteSpace(ownerOrBase) || string.IsNullOrWhiteSpace(repo))
                return null;

            return "https://github.com/" + ownerOrBase.Trim() + "/" + repo.Trim();
        }

        public static string LatestManifestUrl(string releaseBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(releaseBaseUrl))
                return null;
            return releaseBaseUrl.TrimEnd('/') + "/releases/latest/download/version.json";
        }

        public static string PackageAssetUrl(string releaseBaseUrl, string version, string packageFile)
        {
            if (string.IsNullOrWhiteSpace(releaseBaseUrl))
                return null;
            string v = version != null ? version.Trim() : "";
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            string file = string.IsNullOrWhiteSpace(packageFile)
                ? ("GBCWorkHub-v" + v + ".zip")
                : packageFile.Trim();
            return releaseBaseUrl.TrimEnd('/') + "/releases/download/v" + v + "/" + file;
        }
    }
}
