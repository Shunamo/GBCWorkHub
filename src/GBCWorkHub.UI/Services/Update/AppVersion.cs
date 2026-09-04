using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace GBCWorkHub.UI.Services.Update
{
    public static class AppVersion
    {
        public static string Current
        {
            get
            {
                try
                {
                    string path = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var info = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(info.FileVersion))
                            return TrimRevision(info.FileVersion);
                    }
                }
                catch
                {
                }

                try
                {
                    return TrimRevision(Assembly.GetExecutingAssembly().GetName().Version.ToString());
                }
                catch
                {
                    return "0.0.0";
                }
            }
        }

        public static Version CurrentParsed
        {
            get
            {
                Version v;
                return Version.TryParse(UpdateManifest.Normalize(Current), out v) ? v : new Version(0, 0, 0, 0);
            }
        }

        public static bool IsNewer(string latestVersion)
        {
            Version latest;
            if (!Version.TryParse(UpdateManifest.Normalize(latestVersion), out latest))
                return false;
            return latest > CurrentParsed;
        }

        private static string TrimRevision(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return version;
            var parts = version.Split('.');
            if (parts.Length >= 3)
                return parts[0] + "." + parts[1] + "." + parts[2];
            return version.Trim();
        }
    }
}
