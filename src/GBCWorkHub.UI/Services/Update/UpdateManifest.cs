using System;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.Update
{
    public sealed class UpdateManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("packageFile")]
        public string PackageFile { get; set; }

        [JsonProperty("packageUrl")]
        public string PackageUrl { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }

        [JsonIgnore]
        public System.Version ParsedVersion
        {
            get
            {
                System.Version v;
                return System.Version.TryParse(Normalize(Version), out v) ? v : null;
            }
        }

        public static string Normalize(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            string s = version.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);
            var parts = s.Split('.');
            if (parts.Length == 3)
                s = s + ".0";
            return s;
        }
    }
}
