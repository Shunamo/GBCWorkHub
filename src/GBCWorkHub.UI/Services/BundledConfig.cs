using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// App.config를 exe에 넣고, %LocalAppData%\GBCWorkHub 에 풀어 ConfigurationManager가 그걸 읽게 한다.
    /// </summary>
    internal static class BundledConfig
    {
        public const string ResourceName = "GBCWorkHub.UI.BundledApp.config";

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
            }
        }

        public static string ConfigFilePath
        {
            get { return Path.Combine(DataDirectory, "GBCWorkHub.UI.exe.config"); }
        }

        public static string FontsDirectory
        {
            get { return Path.Combine(DataDirectory, "Fonts"); }
        }

        public static void EnsureExtracted()
        {
            ExtractResource(ResourceName, ConfigFilePath);
            RedirectConfiguration(ConfigFilePath);
        }

        private static void RedirectConfiguration(string path)
        {
            try
            {
                AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", path);

                var manager = typeof(ConfigurationManager);
                SetStaticField(manager, "s_initState", 0);
                SetStaticField(manager, "s_configSystem", null);

                var pathsType = manager.Assembly.GetType("System.Configuration.ClientConfigPaths");
                if (pathsType != null)
                    SetStaticField(pathsType, "s_current", null);

                ConfigurationManager.RefreshSection("appSettings");
                ConfigurationManager.RefreshSection("connectionStrings");
            }
            catch
            {
            }
        }

        private static void SetStaticField(Type type, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
                field.SetValue(null, value);
        }

        internal static void ExtractResource(string resourceName, string destPath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                byte[] bundled;
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return;
                    bundled = ReadAll(stream);
                }

                if (bundled == null || bundled.Length == 0)
                    return;

                string dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(destPath) && SameBytes(destPath, bundled))
                    return;

                string tmp = destPath + ".tmp";
                File.WriteAllBytes(tmp, bundled);
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(tmp, destPath);
            }
            catch
            {
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static bool SameBytes(string path, byte[] bundled)
        {
            try
            {
                byte[] existing = File.ReadAllBytes(path);
                if (existing.Length != bundled.Length)
                    return false;
                using (var sha = SHA256.Create())
                {
                    byte[] a = sha.ComputeHash(existing);
                    byte[] b = sha.ComputeHash(bundled);
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (a[i] != b[i])
                            return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
