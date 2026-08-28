using System;
using System.Collections.Generic;
using System.Linq;

namespace GBCWorkHub.SessionAgent.DevSession
{
    public enum ChangeKind
    {
        CheckedIn,
        PendingChanged,
        SessionMetadataChanged,
        Added,
        Deleted
    }

    public enum Confidence
    {
        High,
        Medium,
        Low
    }

    public sealed class SessionFileResult
    {
        public string FilePath { get; set; }
        public ChangeKind Kind { get; set; }
        public Confidence Confidence { get; set; }
        public string Reason { get; set; }
        public int? ChangesetId { get; set; }

        /// <summary>CHECKED_IN / PENDING / NONE.</summary>
        public string TfvcStatus { get; set; }
        public FileMeta Start { get; set; }
        public FileMeta End { get; set; }
    }

    public sealed class ExcludedPreexisting
    {
        public string FilePath { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ClassificationResult
    {
        public List<SessionFileResult> Files { get; } = new List<SessionFileResult>();
        public List<ExcludedPreexisting> PreexistingPendingExcluded { get; } = new List<ExcludedPreexisting>();
    }

    /// <summary>
    /// The authoritative layer on top of SnapshotDiff: TFVC evidence (Changesets checked in
    /// during the session, pending changes at connect/disconnect) takes precedence over raw
    /// metadata deltas. Pure function of its inputs — no I/O, no clock reads — so it is fully
    /// unit-testable without a live TFS server or filesystem.
    /// </summary>
    public static class SessionChangeClassifier
    {
        public static ClassificationResult Classify(
            Dictionary<string, FileMeta> startSnapshot,
            Dictionary<string, FileMeta> endSnapshot,
            List<PendingItemInfo> startPending,
            List<PendingItemInfo> endPending,
            List<ChangesetInfo> sessionChangesets)
        {
            startPending = startPending ?? new List<PendingItemInfo>();
            endPending = endPending ?? new List<PendingItemInfo>();
            sessionChangesets = sessionChangesets ?? new List<ChangesetInfo>();

            var result = new ClassificationResult();

            var startPendingSet = new HashSet<string>(
                startPending.Select(p => p.FilePath), StringComparer.OrdinalIgnoreCase);
            var endPendingSet = new HashSet<string>(
                endPending.Select(p => p.FilePath), StringComparer.OrdinalIgnoreCase);

            var checkedInMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cs in sessionChangesets.OrderBy(c => c.ChangesetId))
                foreach (var f in cs.FilePaths)
                    if (!checkedInMap.ContainsKey(f))
                        checkedInMap[f] = cs.ChangesetId;

            var diff = SnapshotDiff.Compute(startSnapshot, endSnapshot);
            var diffByPath = diff.ToDictionary(d => d.FilePath, StringComparer.OrdinalIgnoreCase);

            var allPaths = new HashSet<string>(diffByPath.Keys, StringComparer.OrdinalIgnoreCase);
            allPaths.UnionWith(checkedInMap.Keys);
            allPaths.UnionWith(endPendingSet);
            allPaths.UnionWith(startPendingSet);

            foreach (string path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                DiffEntry d;
                diffByPath.TryGetValue(path, out d);

                int changesetId;
                bool checkedIn = checkedInMap.TryGetValue(path, out changesetId);
                bool endPend = endPendingSet.Contains(path);
                bool startPend = startPendingSet.Contains(path);

                // Case 9: a session Changeset is authoritative regardless of end-state pending
                // status or raw metadata delta — a file can be edited, checked in, and end the
                // session with a perfectly clean workspace.
                if (checkedIn)
                {
                    result.Files.Add(new SessionFileResult
                    {
                        FilePath = path,
                        Kind = ChangeKind.CheckedIn,
                        Confidence = Confidence.High,
                        Reason = "File appears in Changeset " + changesetId + " checked in during this session.",
                        ChangesetId = changesetId,
                        TfvcStatus = "CHECKED_IN",
                        Start = d != null ? d.Start : null,
                        End = d != null ? d.End : null
                    });
                    continue;
                }

                if (endPend)
                {
                    if (startPend)
                    {
                        // Case 8: distinguish "pre-existing pending, untouched" from
                        // "pre-existing pending, further modified" using the snapshot delta.
                        bool touchedThisSession = d != null && d.Kind != DiffKind.Unchanged;
                        if (touchedThisSession)
                        {
                            result.Files.Add(new SessionFileResult
                            {
                                FilePath = path,
                                Kind = ChangeKind.PendingChanged,
                                Confidence = Confidence.High,
                                Reason = "Already pending at session start and further modified during the session.",
                                TfvcStatus = "PENDING",
                                Start = d.Start,
                                End = d.End
                            });
                        }
                        else
                        {
                            result.PreexistingPendingExcluded.Add(new ExcludedPreexisting
                            {
                                FilePath = path,
                                Reason = "Pending before this session started; no metadata change observed during the session — not attributed to this session."
                            });
                        }
                    }
                    else
                    {
                        result.Files.Add(new SessionFileResult
                        {
                            FilePath = path,
                            Kind = ChangeKind.PendingChanged,
                            Confidence = Confidence.High,
                            Reason = "Newly pending during this session (not pending at connect).",
                            TfvcStatus = "PENDING",
                            Start = d != null ? d.Start : null,
                            End = d != null ? d.End : null
                        });
                    }
                    continue;
                }

                // Not checked in, not pending at disconnect — fall back to raw metadata delta.
                if (d == null)
                    continue;

                switch (d.Kind)
                {
                    case DiffKind.Added:
                        result.Files.Add(new SessionFileResult
                        {
                            FilePath = path,
                            Kind = ChangeKind.Added,
                            Confidence = Confidence.High,
                            Reason = "Present at session end but not at session start, and not checked in or pending.",
                            TfvcStatus = "NONE",
                            Start = null,
                            End = d.End
                        });
                        break;

                    case DiffKind.Deleted:
                        result.Files.Add(new SessionFileResult
                        {
                            FilePath = path,
                            Kind = ChangeKind.Deleted,
                            Confidence = Confidence.High,
                            Reason = "Present at session start but missing at session end.",
                            TfvcStatus = "NONE",
                            Start = d.Start,
                            End = null
                        });
                        break;

                    case DiffKind.Modified:
                        // Case 5 (accepted limitation): a fully reverted, never-checked-in edit
                        // ends up Unchanged here and is silently dropped, by design.
                        result.Files.Add(new SessionFileResult
                        {
                            FilePath = path,
                            Kind = ChangeKind.SessionMetadataChanged,
                            Confidence = Confidence.Low,
                            Reason = "Size/write-time changed since session start, but neither a session Changeset nor a pending change corroborates it.",
                            TfvcStatus = "NONE",
                            Start = d.Start,
                            End = d.End
                        });
                        break;

                    case DiffKind.Unchanged:
                        break;
                }
            }

            result.Files.Sort((a, b) => string.CompareOrdinal(a.FilePath, b.FilePath));
            return result;
        }
    }
}
