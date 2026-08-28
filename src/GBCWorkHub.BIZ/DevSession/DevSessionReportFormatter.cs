using System.Linq;
using System.Text;

namespace GBCWorkHub.BIZ.DevSession
{
    /// <summary>
    /// Temporary Phase 1 developer/debug report for one dev session's classified file changes.
    /// Plain text only (no dedicated view yet) — printed via DiagnosticLogger and can be shown
    /// in any existing text box or written to a file.
    /// </summary>
    public static class DevSessionReportFormatter
    {
        public static string Format(SessionChangeReportDto report)
        {
            if (report == null)
                return "(no session change report)";

            var sb = new StringBuilder();
            sb.AppendLine("Session " + report.SessionToken);
            sb.AppendLine("PC      " + report.RemotePcName);
            sb.AppendLine("User    " + report.RemoteUser);
            sb.AppendLine("Start   " + (report.SessionStartUtc ?? "-"));
            sb.AppendLine("End     " + (report.SessionEndUtc ?? "-"));
            sb.AppendLine();

            if (report.Changesets.Count > 0)
            {
                sb.AppendLine("Changesets checked in during this session:");
                foreach (var cs in report.Changesets)
                    sb.AppendLine("  - #" + cs.ChangesetId + " (" + cs.FileCount + " files) " + cs.Comment);
                sb.AppendLine();
            }

            sb.AppendLine("Changed files:");
            if (report.Files.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                int i = 1;
                foreach (var f in report.Files.OrderBy(f => f.FilePath))
                {
                    sb.AppendLine(i + ". " + f.FilePath);
                    sb.AppendLine("   kind: " + f.Kind + "  tfvc: " + f.TfvcStatus
                        + (f.ChangesetId.HasValue ? "  changeset: #" + f.ChangesetId.Value : ""));
                    sb.AppendLine("   confidence: " + f.Confidence + " — " + f.Reason);
                    i++;
                }
            }

            if (report.PreexistingPendingExcludedCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine(report.PreexistingPendingExcludedCount
                    + " file(s) were already pending before this session and untouched during it — not attributed to this session.");
            }

            if (report.Truncated)
            {
                sb.AppendLine();
                sb.AppendLine("NOTE: result truncated for clipboard transport — " + report.DroppedFileCount + " lower-priority file(s) omitted.");
            }

            return sb.ToString();
        }
    }
}
