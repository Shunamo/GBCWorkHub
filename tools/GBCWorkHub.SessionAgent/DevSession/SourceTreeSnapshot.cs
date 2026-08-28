using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GBCWorkHub.SessionAgent.DevSession
{
    /// <summary>Length + LastWriteTimeUtc only — never file content.</summary>
    public sealed class FileMeta
    {
        [JsonProperty("length")]
        public long Length { get; set; }

        [JsonProperty("writeUtcTicks")]
        public long LastWriteTimeUtcTicks { get; set; }
    }

    /// <summary>
    /// Two-point, metadata-only snapshot of the monitored HIS source tree. No FileSystemWatcher,
    /// no resident process: this is called once at connect and once at disconnect by two
    /// separate short-lived SessionAgent invocations, with the connect-time result persisted to
    /// a small local JSON file so the disconnect-time invocation can read it back.
    /// </summary>
    public static class SourceTreeSnapshot
    {
        public static readonly string[] DefaultIncludeExtensions = { ".cs", ".xaml", ".sql", ".xml", ".config" };
        public static readonly string[] DefaultExcludeDirSegments = { "bin", "obj", ".vs", "packages", "TestResults" };
        public static readonly string[] DefaultExcludeFileSuffixes =
        {
            ".g.cs", ".g.i.cs", ".designer.cs", ".AssemblyInfo.cs", ".GeneratedInternalTypeHelper.g.cs"
        };

        public static Dictionary<string, FileMeta> Capture(
            string rootPath,
            string[] includeExtensions = null,
            string[] excludeDirSegments = null,
            string[] excludeFileSuffixes = null)
        {
            includeExtensions = includeExtensions ?? DefaultIncludeExtensions;
            excludeDirSegments = excludeDirSegments ?? DefaultExcludeDirSegments;
            excludeFileSuffixes = excludeFileSuffixes ?? DefaultExcludeFileSuffixes;

            var result = new Dictionary<string, FileMeta>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(rootPath))
                return result;

            foreach (string path in EnumerateFiles(rootPath, excludeDirSegments))
            {
                if (!IsMonitoredPath(path, includeExtensions, excludeFileSuffixes))
                    continue;

                try
                {
                    var fi = new FileInfo(path);
                    result[Normalize(path)] = new FileMeta
                    {
                        Length = fi.Length,
                        LastWriteTimeUtcTicks = fi.LastWriteTimeUtc.Ticks
                    };
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return result;
        }

        public static bool IsMonitoredPath(string path, string[] includeExtensions, string[] excludeFileSuffixes)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) ||
                !includeExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            string fileName = Path.GetFileName(path);
            if (excludeFileSuffixes.Any(sfx => fileName.EndsWith(sfx, StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }

        private static IEnumerable<string> EnumerateFiles(string root, string[] excludeDirSegments)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string sub in subDirs)
                {
                    string name = Path.GetFileName(sub);
                    if (excludeDirSegments.Any(ex => ex.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    pending.Push(sub);
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(dir);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                    yield return file;
            }
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        /// <summary>Deterministic: keys sorted ordinally before serializing.</summary>
        public static string Serialize(Dictionary<string, FileMeta> snapshot)
        {
            var sorted = new SortedDictionary<string, FileMeta>(snapshot, StringComparer.Ordinal);
            return JsonConvert.SerializeObject(sorted, Formatting.Indented);
        }

        public static Dictionary<string, FileMeta> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, FileMeta>(StringComparer.OrdinalIgnoreCase);
            var flat = JsonConvert.DeserializeObject<Dictionary<string, FileMeta>>(json)
                       ?? new Dictionary<string, FileMeta>();
            return new Dictionary<string, FileMeta>(flat, StringComparer.OrdinalIgnoreCase);
        }
    }
}
