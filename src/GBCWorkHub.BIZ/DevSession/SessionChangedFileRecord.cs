using System;

namespace GBCWorkHub.BIZ.DevSession
{
    /// <summary>One row of the SessionChangedFiles result for a single dev session.</summary>
    public sealed class SessionChangedFileRecord
    {
        public string FilePath { get; set; }
        public DateTime FirstChangeUtc { get; set; }
        public DateTime LastChangeUtc { get; set; }
        public bool ContentChanged { get; set; }
        public string StartHash { get; set; }
        public string EndHash { get; set; }

        /// <summary>HIGH / MEDIUM / LOW.</summary>
        public string Confidence { get; set; }
        public string Reason { get; set; }
    }
}
