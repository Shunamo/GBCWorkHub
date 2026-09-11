using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GBCWorkHub.UI.Services.Update
{
    public sealed class UpdateService : IUpdateService
    {
        public const string UpdaterFileName = "GBCWorkHubUpdater.exe";
        public const string MainExeFileName = "GBCWorkHub.exe";

        private readonly IUpdateSource _source;

        public UpdateService(IUpdateSource source)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            _source = source;
        }

        public string CurrentVersion
        {
            get { return AppVersion.Current; }
        }

        /// <summary>
        /// Production default: GitHubRelease → static HTTPS latest/download/version.json.
        /// LocalFolder remains for offline tests. No PAT/token ever read from config.
        /// </summary>
        public static IUpdateSource CreateSourceFromConfig()
        {
            string enabled = ReadSetting("Update.Enabled");
            if (!string.IsNullOrWhiteSpace(enabled) &&
                (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase) || enabled == "0"))
                return null;

            string type = ReadSetting("Update.SourceType");
            if (string.IsNullOrWhiteSpace(type))
                type = "GitHubRelease";

            if (string.Equals(type, "LocalFolder", StringComparison.OrdinalIgnoreCase))
            {
                string path = ReadSetting("Update.ManifestPath");
                if (string.IsNullOrWhiteSpace(path))
                    return null;
                return new LocalFolderUpdateSource(path);
            }

            // GitHubRelease | Http | HttpUpdateSource
            string manifestUrl = ReadSetting("Update.ManifestUrl");
            if (string.IsNullOrWhiteSpace(manifestUrl))
                manifestUrl = ResolveGitHubLatestManifestUrl();
            if (string.IsNullOrWhiteSpace(manifestUrl))
                return null;
            return new HttpUpdateSource(manifestUrl);
        }

        public static string ResolveGitHubLatestManifestUrl()
        {
            string baseUrl = ResolveReleaseBaseUrl();
            return GitHubReleaseUrls.LatestManifestUrl(baseUrl);
        }

        public static string ResolveReleaseBaseUrl()
        {
            string baseOverride = ReadSetting("Update.ReleaseBaseUrl");
            if (!string.IsNullOrWhiteSpace(baseOverride))
                return baseOverride.Trim().TrimEnd('/');

            // Defaults match App.config.example so stale LocalAppData configs still check updates.
            string owner = ReadSetting("Update.ReleaseOwner");
            if (string.IsNullOrWhiteSpace(owner))
                owner = "Shunamo";
            string repo = ReadSetting("Update.ReleaseRepo");
            if (string.IsNullOrWhiteSpace(repo))
                repo = "GBCWorkHub";
            return GitHubReleaseUrls.NormalizeBaseUrl(owner, repo);
        }

        public async Task<UpdateManifest> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            var latest = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            if (latest == null || !AppVersion.IsNewer(latest.Version))
                return null;
            return latest;
        }

        public async Task ApplyUpdateAsync(UpdateManifest manifest, CancellationToken cancellationToken)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");

            string session = Guid.NewGuid().ToString("N");
            string downloadDir = Path.Combine(Path.GetTempPath(), "GBCWorkHubUpdate", session);
            Directory.CreateDirectory(downloadDir);
            string packagePath = Path.Combine(downloadDir, "package.zip");

            await _source.DownloadPackageAsync(manifest, packagePath, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                string actual = ComputeSha256Hex(packagePath);
                if (!string.Equals(actual, manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Downloaded package SHA256 mismatch");
            }

            string installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(installDir))
                throw new InvalidOperationException("Install directory unknown");

            string installedUpdater = Path.Combine(installDir, UpdaterFileName);
            if (!File.Exists(installedUpdater))
                throw new FileNotFoundException("Updater not found next to the app. Redeploy with GBCWorkHubUpdater.exe.", installedUpdater);

            string tempUpdaterDir = Path.Combine(Path.GetTempPath(), "GBCWorkHubUpdater", session);
            Directory.CreateDirectory(tempUpdaterDir);
            string tempUpdater = Path.Combine(tempUpdaterDir, UpdaterFileName);
            File.Copy(installedUpdater, tempUpdater, true);

            string launchPath = Path.Combine(installDir, MainExeFileName);
            string workDir = Path.Combine(Path.GetTempPath(), "GBCWorkHubUpdater", session + "-work");

            var psi = new ProcessStartInfo
            {
                FileName = tempUpdater,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempUpdaterDir,
                Arguments = string.Format(
                    "--package \"{0}\" --install-dir \"{1}\" --wait-pid {2} --launch \"{3}\" --work-dir \"{4}\"{5}",
                    packagePath,
                    installDir,
                    Process.GetCurrentProcess().Id,
                    launchPath,
                    workDir,
                    string.IsNullOrWhiteSpace(manifest.Sha256) ? "" : (" --sha256 " + manifest.Sha256.Trim()))
            };

            Process.Start(psi);
        }

        private static string ReadSetting(string key)
        {
            try
            {
                return ConfigurationManager.AppSettings[key];
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using (var fs = File.OpenRead(filePath))
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
