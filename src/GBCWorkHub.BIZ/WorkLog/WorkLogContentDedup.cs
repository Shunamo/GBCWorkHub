using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.WorkLog
{
    /// <summary>엑셀 가져오기: 업무기록 본문(헤더+PRJ+SRC)이 전부 같을 때만 중복으로 본다.</summary>
    public static class WorkLogContentDedup
    {
        public static string BuildKey(WorkLogRecordDto r)
        {
            if (r == null)
                return null;

            var sb = new StringBuilder(512);
            Append(sb, "S", r.SiteCode);
            Append(sb, "T", NormalizeTicket(r.TicketNo));
            Append(sb, "TC", r.TicketContents);
            Append(sb, "M", r.MenuName);
            Append(sb, "PC", r.PcName);
            Append(sb, "P", r.PersonInCharge);
            Append(sb, "SD", FormatDate(r.StartDate));
            Append(sb, "ED", FormatDate(r.EndDate));
            Append(sb, "DS", r.DeploymentStatus);
            Append(sb, "DD", FormatDate(r.DeploymentDate));
            Append(sb, "C", r.WorkComment);
            Append(sb, "WS", r.WriteStatus);

            if (r.Projects != null && r.Projects.Count > 0)
            {
                foreach (var p in r.Projects
                    .OrderBy(x => Norm(x != null ? x.Type : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.Category : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.ProjectName : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.Comment : null), StringComparer.Ordinal))
                {
                    if (p == null)
                        continue;
                    Append(sb, "PT", p.Type);
                    Append(sb, "PCAT", p.Category);
                    Append(sb, "PN", p.ProjectName);
                    Append(sb, "PDS", p.DeploymentStatus);
                    Append(sb, "PDD", FormatDate(p.DeploymentDate));
                    Append(sb, "PCM", p.Comment);
                }
            }

            if (r.Sources != null && r.Sources.Count > 0)
            {
                foreach (var s in r.Sources
                    .OrderBy(x => Norm(x != null ? x.OriginalPath : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.FileName : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.ChangeDetail : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.Type : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.Category : null), StringComparer.Ordinal)
                    .ThenBy(x => Norm(x != null ? x.ProjectName : null), StringComparer.Ordinal))
                {
                    if (s == null)
                        continue;
                    Append(sb, "SF", s.FileName);
                    Append(sb, "SP", s.OriginalPath);
                    Append(sb, "SCT", s.ChangeType);
                    Append(sb, "SCD", s.ChangeDetail);
                    Append(sb, "ST", s.Type);
                    Append(sb, "SCAT", s.Category);
                    Append(sb, "SPN", s.ProjectName);
                }
            }

            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string tag, string value)
        {
            sb.Append(tag).Append('=').Append(Norm(value)).Append('\n');
        }

        private static string Norm(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim().Replace("\r\n", "\n").Replace('\r', '\n').ToUpperInvariant();
        }

        private static string NormalizeTicket(string ticketNo)
        {
            if (string.IsNullOrWhiteSpace(ticketNo))
                return string.Empty;

            var parts = ticketNo.Split(new[] { ',', ';', '/', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>();
            foreach (var part in parts)
            {
                string t = part.Trim();
                if (t.Length == 0)
                    continue;
                if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']')
                    t = t.Substring(1, t.Length - 2).Trim();
                while (t.StartsWith("TN-", StringComparison.OrdinalIgnoreCase))
                    t = t.Substring(3).Trim();
                if (t.Length > 0)
                    cleaned.Add(t.ToUpperInvariant());
            }
            return string.Join(", ", cleaned);
        }

        private static string FormatDate(DateTime? dt)
        {
            return dt.HasValue ? dt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        }
    }
}
