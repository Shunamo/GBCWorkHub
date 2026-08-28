using System;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// XSUP.MSDWHTKH_FILE row — one HIS source file attributed to a dev session by the
    /// two-point snapshot + TFVC classification pipeline (see
    /// GBCWorkHub.SessionAgent/DevSession/SessionChangeClassifier.cs on the remote side).
    /// FirstChangeAt/LastChangeAt/StartHash/EndHash are watcher-era holdovers from the
    /// FileSystemWatcher prototype and are left null by this pipeline; ChangeKind/TfvcStatus/
    /// ChangesetId/Confidence/Reason are what this pipeline actually populates.
    /// </summary>
    public sealed class DevSessionFileDto
    {
        public long FileRowId { get; set; }
        public string SessionToken { get; set; }
        public string FilePath { get; set; }

        /// <summary>CheckedIn / PendingChanged / SessionMetadataChanged / Added / Deleted.</summary>
        public string ChangeKind { get; set; }

        /// <summary>CHECKED_IN / PENDING / NONE.</summary>
        public string TfvcStatus { get; set; }
        public int? ChangesetId { get; set; }
        public string Confidence { get; set; }
        public string Reason { get; set; }
        public bool DiffAvailable { get; set; }
        public string DiffSummary { get; set; }

        // Watcher-era fields — nullable, unused by the current pipeline, kept for schema
        // stability since a Phase 1 prototype may still reference them.
        public DateTime? FirstChangeAt { get; set; }
        public DateTime? LastChangeAt { get; set; }
        public bool? ContentChanged { get; set; }
        public string StartHash { get; set; }
        public string EndHash { get; set; }
    }
}
