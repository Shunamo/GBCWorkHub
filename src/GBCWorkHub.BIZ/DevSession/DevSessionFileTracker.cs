using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace GBCWorkHub.BIZ.DevSession
{
    /// <summary>
    /// SUPERSEDED PROTOTYPE — not part of the current runtime architecture and not referenced
    /// by DevSessionFileBiz anymore. A live FileSystemWatcher requires a resident process; the
    /// remote SessionAgent is a short-lived process invoked once per connect/disconnect event,
    /// so the shipped design uses a two-point snapshot diff instead (see
    /// GBCWorkHub.SessionAgent/DevSession/SourceTreeSnapshot.cs + SnapshotDiff.cs +
    /// SessionChangeClassifier.cs). Kept here only as a reviewed, tested reference artifact.
    ///
    /// Session-scoped, deterministic HIS source file change tracker.
    ///
    /// FileSystemWatcher is used only as an event *detector* — every event is checked against
    /// a session-start metadata baseline (file length + last-write-time) before being reported
    /// as a real content change. No file content is copied into the database; only the touched
    /// subset is ever hashed, and hashes/metadata never leave process memory until EndSession()
    /// returns the final SessionChangedFiles list to the caller.
    ///
    /// Not thread-affine: StartSession/EndSession are expected to be called once each from the
    /// session owner (e.g. RemoteSessionController); FileSystemWatcher callbacks run on the
    /// framework's own thread pool and only touch the internal ConcurrentDictionary.
    /// </summary>
    public sealed class DevSessionFileTracker : IDisposable
    {
        private sealed class BaselineMeta
        {
            public long Length;
            public long LastWriteUtcTicks;
        }

        private sealed class TouchedState
        {
            public DateTime FirstUtc;
            public DateTime LastUtc;
            public string StartHash;
            public bool ExistedInBaseline;
            public long BaselineLength;
            public long BaselineLastWriteUtcTicks;
        }

        private readonly DevSessionFileTrackerOptions _options;
        private readonly Dictionary<string, BaselineMeta> _baseline =
            new Dictionary<string, BaselineMeta>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TouchedState> _touched =
            new ConcurrentDictionary<string, TouchedState>(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher _watcher;
        private bool _running;
        private bool _disposed;

        public DevSessionFileTracker(DevSessionFileTrackerOptions options = null)
        {
            _options = options ?? new DevSessionFileTrackerOptions();
        }

        public bool IsRunning { get { return _running; } }
        public int BaselineFileCount { get { return _baseline.Count; } }
        public int TouchedFileCount { get { return _touched.Count; } }

        /// <summary>
        /// Builds the lightweight session-start baseline (metadata only, no content read) for
        /// every monitored file under RootPath, then starts watching for further changes.
        /// </summary>
        public void StartSession()
        {
            if (_running)
                return;

            _baseline.Clear();
            _touched.Clear();

            if (Directory.Exists(_options.RootPath))
            {
                foreach (string path in EnumerateMonitoredFiles(_options.RootPath))
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        _baseline[Normalize(path)] = new BaselineMeta
                        {
                            Length = fi.Length,
                            LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks
                        };
                    }
                    catch (IOException)
                    {
                        // File disappeared mid-walk (build tool, etc.) — simply not part of the baseline.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            _watcher = new FileSystemWatcher(_options.RootPath)
            {
                IncludeSubdirectories = true,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Renamed += OnFsRenamed;
            _watcher.Error += OnFsError;
            _watcher.EnableRaisingEvents = true;
            _running = true;
        }

        /// <summary>
        /// Stops watching and returns the deterministic SessionChangedFiles list. Safe to call
        /// exactly once per session; a second call returns an empty list.
        /// </summary>
        public IReadOnlyList<SessionChangedFileRecord> EndSession()
        {
            if (!_running)
                return new List<SessionChangedFileRecord>();

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFsEvent;
            _watcher.Created -= OnFsEvent;
            _watcher.Renamed -= OnFsRenamed;
            _watcher.Error -= OnFsError;
            _watcher.Dispose();
            _watcher = null;
            _running = false;

            var result = new List<SessionChangedFileRecord>();
            foreach (var kvp in _touched)
            {
                result.Add(Evaluate(kvp.Key, kvp.Value));
            }

            result.Sort((a, b) => a.FirstChangeUtc.CompareTo(b.FirstChangeUtc));
            return result;
        }

        private SessionChangedFileRecord Evaluate(string path, TouchedState state)
        {
            var record = new SessionChangedFileRecord
            {
                FilePath = path,
                FirstChangeUtc = state.FirstUtc,
                LastChangeUtc = state.LastUtc,
                StartHash = state.StartHash
            };

            FileInfo fi = null;
            try
            {
                if (File.Exists(path))
                    fi = new FileInfo(path);
            }
            catch (IOException)
            {
            }

            if (fi == null)
            {
                record.ContentChanged = true;
                record.EndHash = null;
                record.Confidence = "MEDIUM";
                record.Reason = "File no longer exists at session end (deleted or moved).";
                return record;
            }

            record.EndHash = TryHash(path);

            if (!state.ExistedInBaseline)
            {
                record.ContentChanged = true;
                record.Confidence = "HIGH";
                record.Reason = "New file created during session (not present in session-start baseline).";
                return record;
            }

            bool lengthChanged = fi.Length != state.BaselineLength;
            bool writeTimeChanged = fi.LastWriteTimeUtc.Ticks != state.BaselineLastWriteUtcTicks;

            if (lengthChanged)
            {
                record.ContentChanged = true;
                record.Confidence = "HIGH";
                record.Reason = "File size changed since session-start baseline.";
                return record;
            }

            if (writeTimeChanged)
            {
                record.ContentChanged = true;
                if (state.StartHash != null && record.EndHash != null && state.StartHash != record.EndHash)
                {
                    record.Confidence = "HIGH";
                    record.Reason = "Same size, write time advanced, and content changed again after the first observed edit.";
                }
                else if (state.StartHash != null && record.EndHash != null)
                {
                    record.Confidence = "MEDIUM";
                    record.Reason = "Same size, write time advanced since session start; content consistent with a single edit.";
                }
                else
                {
                    record.Confidence = "MEDIUM";
                    record.Reason = "Same size, write time advanced since session start; hash unavailable for corroboration.";
                }
                return record;
            }

            record.ContentChanged = false;
            record.Confidence = "LOW";
            record.Reason = "Watcher event observed but file size/write-time match the session-start baseline (likely a non-content event).";
            return record;
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            TrackTouch(e.FullPath);
        }

        private void OnFsRenamed(object sender, RenamedEventArgs e)
        {
            TrackTouch(e.FullPath);
        }

        private void OnFsError(object sender, ErrorEventArgs e)
        {
            // FileSystemWatcher's internal buffer overflowed or the watch became invalid.
            // Detection-only component: surface nothing further in Phase 1 beyond not crashing.
        }

        private void TrackTouch(string fullPath)
        {
            if (!IsMonitoredPath(fullPath))
                return;

            string key = Normalize(fullPath);
            var now = DateTime.UtcNow;

            _touched.AddOrUpdate(
                key,
                addValueFactory: _ =>
                {
                    BaselineMeta baseline;
                    bool existed = _baseline.TryGetValue(key, out baseline);
                    return new TouchedState
                    {
                        FirstUtc = now,
                        LastUtc = now,
                        StartHash = TryHash(fullPath),
                        ExistedInBaseline = existed,
                        BaselineLength = existed ? baseline.Length : 0,
                        BaselineLastWriteUtcTicks = existed ? baseline.LastWriteUtcTicks : 0
                    };
                },
                updateValueFactory: (_, existing) =>
                {
                    existing.LastUtc = now;
                    return existing;
                });
        }

        private bool IsMonitoredPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) ||
                !_options.IncludeExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            string fileName = Path.GetFileName(path);
            if (_options.ExcludeFileSuffixes.Any(sfx => fileName.EndsWith(sfx, StringComparison.OrdinalIgnoreCase)))
                return false;

            string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(seg => _options.ExcludeDirSegments.Any(ex => ex.Equals(seg, StringComparison.OrdinalIgnoreCase))))
                return false;

            return true;
        }

        private IEnumerable<string> EnumerateMonitoredFiles(string root)
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
                    if (_options.ExcludeDirSegments.Any(ex => ex.Equals(name, StringComparison.OrdinalIgnoreCase)))
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
                {
                    if (IsMonitoredPath(file))
                        yield return file;
                }
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

        private static string TryHash(string path)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sha = SHA256.Create())
                    {
                        byte[] hash = sha.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
                catch (IOException)
                {
                    // File may still be mid-write by another process; brief retry, then give up.
                    if (attempt == maxAttempts)
                        return null;
                    System.Threading.Thread.Sleep(20);
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
