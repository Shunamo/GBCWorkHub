using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.Update
{
    /// <summary>Reads version.json from a local/network folder and copies the package file.</summary>
    public sealed class LocalFolderUpdateSource : IUpdateSource
    {
        private readonly string _folder;

        public LocalFolderUpdateSource(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("folder required", "folder");
            _folder = folder.Trim();
        }

        public Task<UpdateManifest> GetLatestAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string manifestPath = Path.Combine(_folder, "version.json");
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException("version.json not found", manifestPath);
                string json = File.ReadAllText(manifestPath);
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                    throw new InvalidOperationException("Invalid version.json");
                return manifest;
            }, cancellationToken);
        }

        public Task DownloadPackageAsync(UpdateManifest manifest, string destinationFile, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (manifest == null)
                    throw new ArgumentNullException("manifest");
                string name = !string.IsNullOrWhiteSpace(manifest.PackageFile)
                    ? manifest.PackageFile
                    : ("GBCWorkHub-v" + manifest.Version + ".zip");
                string src = Path.Combine(_folder, name);
                if (!File.Exists(src))
                    throw new FileNotFoundException("Package not found", src);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? Path.GetTempPath());
                File.Copy(src, destinationFile, true);
            }, cancellationToken);
        }
    }
}
