using System;
using System.Collections.Generic;

namespace GBCWorkHub.SessionAgent.DevSession
{
    public enum DiffKind
    {
        Added,
        Modified,
        Deleted,
        Unchanged
    }

    public sealed class DiffEntry
    {
        public string FilePath { get; set; }
        public DiffKind Kind { get; set; }
        public FileMeta Start { get; set; }
        public FileMeta End { get; set; }
    }

    /// <summary>Pure two-point metadata comparison — the entire replacement for a live watcher.</summary>
    public static class SnapshotDiff
    {
        public static List<DiffEntry> Compute(Dictionary<string, FileMeta> start, Dictionary<string, FileMeta> end)
        {
            var allPaths = new HashSet<string>(start.Keys, StringComparer.OrdinalIgnoreCase);
            allPaths.UnionWith(end.Keys);

            var result = new List<DiffEntry>();
            foreach (string path in allPaths)
            {
                FileMeta s, e;
                bool inStart = start.TryGetValue(path, out s);
                bool inEnd = end.TryGetValue(path, out e);

                if (inStart && !inEnd)
                {
                    result.Add(new DiffEntry { FilePath = path, Kind = DiffKind.Deleted, Start = s, End = null });
                }
                else if (!inStart && inEnd)
                {
                    result.Add(new DiffEntry { FilePath = path, Kind = DiffKind.Added, Start = null, End = e });
                }
                else
                {
                    bool changed = s.Length != e.Length || s.LastWriteTimeUtcTicks != e.LastWriteTimeUtcTicks;
                    result.Add(new DiffEntry
                    {
                        FilePath = path,
                        Kind = changed ? DiffKind.Modified : DiffKind.Unchanged,
                        Start = s,
                        End = e
                    });
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.FilePath, b.FilePath));
            return result;
        }
    }
}
