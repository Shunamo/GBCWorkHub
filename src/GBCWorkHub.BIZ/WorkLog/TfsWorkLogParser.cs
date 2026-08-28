using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.WorkLog
{
    public interface ITfsWorkLogParser
    {
        WorkLogDraft Parse(TfsChangesetItem changeset, WorkSessionContext sessionContext);
        WorkLogDraft Parse(TfsChangesetItem changeset, WorkSessionContext sessionContext, bool trustProvidedFiles);
        IReadOnlyList<WorkLogDraft> ParseMany(IEnumerable<TfsChangesetItem> changesets, WorkSessionContext sessionContext);
    }

    /// <summary>
    /// TFS Changeset → 엑셀 업무기록 WorkLogDraft 파서 (규칙 기반, Changeset 1건 = Draft 1건).
    /// </summary>
    public sealed class TfsWorkLogParser : ITfsWorkLogParser
    {
        /// <summary>
        /// TN-1234 / TN.1234(APO) / [TN-1234] / [1234] 형태.
        /// 대괄호만 있는 숫자는 티켓으로 본다 (최소 3자리 — [1] 같은 잡음 완화).
        /// [CS 1234] 체인지셋 표기는 티켓이 아님.
        /// </summary>
        private static readonly Regex TicketRegex = new Regex(
            @"(?:\[\s*TN[\s\.\-]*(\d+)(?:\([^)]*\))?\s*\])"
            + @"|(?:(?<![A-Za-z0-9_])TN[\s\.\-]+(\d+)(?:\([^)]*\))?)"
            + @"|(?:\[\s*(?!CS\b)(\d{3,})\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>업무기록 합치기 등에서 붙는 체인지셋 표기 — 티켓 파싱 대상에서 제외.</summary>
        private static readonly Regex ChangesetMarkerRegex = new Regex(
            @"\[\s*CS\s*\d+\s*\]|(?<![A-Za-z0-9_])CS[\s\-]+\d+\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LeadingDateRegex = new Regex(
            @"^\s*\d{4}[-/\.]\d{1,2}[-/\.]\d{1,2}\s*,?\s*",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public WorkLogDraft Parse(TfsChangesetItem changeset, WorkSessionContext sessionContext)
        {
            return Parse(changeset, sessionContext, false);
        }

        /// <param name="trustProvidedFiles">
        /// true이면 UI에서 이미 고른 파일 목록을 신뢰하고 후보 필터를 건너뜀(숨김 파일 수동 포함용).
        /// </param>
        public WorkLogDraft Parse(TfsChangesetItem changeset, WorkSessionContext sessionContext, bool trustProvidedFiles)
        {
            var draft = new WorkLogDraft();
            if (changeset == null)
            {
                draft.Warnings.Add("Changeset이 null입니다.");
                return draft;
            }

            sessionContext = sessionContext ?? new WorkSessionContext();

            draft.ChangesetId = changeset.ChangesetId;
            draft.TfsComment = changeset.Comment ?? string.Empty;
            draft.TfsAuthorId = changeset.AuthorId;
            draft.TfsAuthorName = changeset.AuthorName;
            draft.CheckedInAt = TryParseDate(changeset.CheckedInAt);

            draft.Pc = sessionContext.ResolvePc();
            // Person in charge는 이름 기입 칸 — 세션/IP로 자동 채우지 않음
            draft.PersonInCharge = string.Empty;
            draft.StartDate = sessionContext.SessionStartedAt;
            draft.EndDate = sessionContext.SessionEndedAt;
            draft.MenuName = string.Empty;
            draft.DeploymentStatus = null;
            draft.DeploymentDate = null;
            draft.Comment = null;

            ApplyTicketFields(draft, changeset.Comment);

            var classified = new List<WorkSourceItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (changeset.Files != null)
            {
                foreach (var file in changeset.Files)
                {
                    if (file == null)
                        continue;

                    string originalPath = (file.ServerPath ?? string.Empty).Trim();
                    string fileName = !string.IsNullOrWhiteSpace(file.FileName)
                        ? file.FileName.Trim()
                        : ExtractFileName(originalPath);

                    string dedupeKey = changeset.ChangesetId + "|" + originalPath;
                    if (!string.IsNullOrEmpty(originalPath) && !seenPaths.Add(dedupeKey))
                        continue;

                    string itemType = InferItemType(originalPath, fileName);
                    if (!string.Equals(itemType, "File", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(itemType, "Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        draft.SkippedItems.Add("ItemType=" + itemType + " path=" + originalPath);
                        draft.Warnings.Add("File이 아닌 항목을 검토하세요: " + (fileName ?? originalPath));
                    }

                    if (!trustProvidedFiles)
                    {
                        var candidate = TfsWorkLogCandidateClassifier.Classify(fileName, originalPath);
                        if (candidate.Decision != TfsWorkLogCandidateClassifier.Decision.Recommended)
                        {
                            draft.SkippedItems.Add(
                                candidate.RuleCode + " path=" + originalPath
                                + (string.IsNullOrEmpty(candidate.Reason) ? string.Empty : " (" + candidate.Reason + ")"));
                            continue;
                        }
                    }

                    var classifiedItem = TfsPathClassifier.Classify(
                        changeset.ChangesetId,
                        file.ChangeType,
                        itemType,
                        fileName,
                        originalPath);

                    classified.Add(classifiedItem);
                }
            }

            draft.WorkGroups = GroupItems(classified);
            return draft;
        }

        public IReadOnlyList<WorkLogDraft> ParseMany(IEnumerable<TfsChangesetItem> changesets, WorkSessionContext sessionContext)
        {
            var list = new List<WorkLogDraft>();
            if (changesets == null)
                return list;
            foreach (var cs in changesets)
            {
                if (cs == null)
                    continue;
                list.Add(Parse(cs, sessionContext));
            }
            return list;
        }

        private static void ApplyTicketFields(WorkLogDraft draft, string comment)
        {
            string raw = comment ?? string.Empty;
            draft.TicketNo = ExtractTicketNumbers(raw);

            string cleaned;
            if (TryCleanTicketContents(raw, out cleaned))
            {
                draft.TicketContents = cleaned;
                draft.TicketContentsIsSuggested = true;
                draft.Warnings.Add("Ticket Contents는 추천값입니다. 확인하세요.");
            }
            else
            {
                draft.TicketContents = raw;
                draft.TicketContentsIsSuggested = true;
                draft.Warnings.Add("Ticket Contents는 TFS Comment 원문입니다. 확인하세요.");
            }
        }

        /// <summary>코멘트에서 티켓 번호들을 추출 (콤마 구분, 중복 제거). [CS n] 은 무시.</summary>
        public static string ExtractTicketNumbers(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return string.Empty;

            // [CS 1234] 등 체인지셋 마커를 가린 뒤 티켓만 추출
            string masked = ChangesetMarkerRegex.Replace(comment, " ");

            var nums = new List<string>();
            foreach (Match match in TicketRegex.Matches(masked))
            {
                if (match == null || !match.Success)
                    continue;
                if (IsChangesetMarkerMatch(match.Value))
                    continue;
                string n = FirstCapturingGroup(match);
                if (string.IsNullOrEmpty(n))
                    continue;
                if (!nums.Any(x => string.Equals(x, n, StringComparison.Ordinal)))
                    nums.Add(n);
            }
            return string.Join(", ", nums);
        }

        private static bool IsChangesetMarkerMatch(string matched)
        {
            if (string.IsNullOrWhiteSpace(matched))
                return false;
            string t = matched.Trim();
            return t.StartsWith("[CS", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("CS", StringComparison.OrdinalIgnoreCase)
                   && t.Length > 2
                   && (t[2] == ' ' || t[2] == '-' || t[2] == '\t' || char.IsDigit(t[2]));
        }

        private static string FirstCapturingGroup(Match match)
        {
            if (match == null || match.Groups.Count <= 1)
                return string.Empty;
            for (int i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
                    return match.Groups[i].Value;
            }
            return string.Empty;
        }

        /// <summary>
        /// 앞 날짜 / 작성자 토큰 / TN 토큰을 안정적으로 제거할 수 있을 때만 정리.
        /// </summary>
        internal static bool TryCleanTicketContents(string comment, out string cleaned)
        {
            cleaned = comment ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            string work = cleaned.Trim();
            bool changed = false;

            var dateMatch = LeadingDateRegex.Match(work);
            if (dateMatch.Success)
            {
                work = work.Substring(dateMatch.Length).TrimStart();
                changed = true;
            }

            // "author," 형태 두 번째 토큰(쉼표로 구분된 짧은 식별자)
            int comma = work.IndexOf(',');
            if (comma > 0 && comma < 40)
            {
                string firstToken = work.Substring(0, comma).Trim();
                if (firstToken.Length > 0 && firstToken.Length <= 32
                    && !TicketRegex.IsMatch(ChangesetMarkerRegex.Replace(firstToken, " "))
                    && !ChangesetMarkerRegex.IsMatch(firstToken)
                    && !firstToken.Contains(" "))
                {
                    work = work.Substring(comma + 1).TrimStart();
                    changed = true;
                }
            }

            // 티켓 토큰만 제거 — [CS n] 체인지셋 표기는 유지
            bool ticketsRemoved;
            work = RemoveTicketTokensKeepChangesetMarkers(work, out ticketsRemoved);
            if (ticketsRemoved)
                changed = true;

            work = Regex.Replace(work, @"^\s*[\(\)\[\]:\-–—]+\s*", string.Empty).Trim();
            work = Regex.Replace(work, @"\s{2,}", " ").Trim();

            if (string.IsNullOrWhiteSpace(work))
            {
                cleaned = comment.Trim();
                return false;
            }

            cleaned = work;
            return changed || !string.IsNullOrEmpty(ExtractTicketNumbers(comment));
        }

        private static string RemoveTicketTokensKeepChangesetMarkers(string comment, out bool removed)
        {
            removed = false;
            if (string.IsNullOrEmpty(comment))
                return comment;

            // CS 마커 구간을 보호한 뒤 티켓 매칭
            string masked = ChangesetMarkerRegex.Replace(comment, m => new string('\u0001', m.Length));
            var sb = new System.Text.StringBuilder(comment.Length);
            int i = 0;
            while (i < masked.Length)
            {
                var m = TicketRegex.Match(masked, i);
                if (!m.Success)
                {
                    sb.Append(comment, i, comment.Length - i);
                    break;
                }

                if (m.Index > i)
                    sb.Append(comment, i, m.Index - i);

                // 마스크(\u0001)와 겹치면 원문(CS 마커) 유지
                bool overlapsMask = false;
                for (int k = m.Index; k < m.Index + m.Length; k++)
                {
                    if (masked[k] == '\u0001')
                    {
                        overlapsMask = true;
                        break;
                    }
                }

                if (overlapsMask || IsChangesetMarkerMatch(m.Value))
                    sb.Append(comment, m.Index, m.Length);
                else
                    removed = true;

                i = m.Index + m.Length;
            }
            return sb.ToString();
        }

        private static List<WorkGroupDraft> GroupItems(List<WorkSourceItem> items)
        {
            var groups = new List<WorkGroupDraft>();
            if (items == null || items.Count == 0)
                return groups;

            var map = new Dictionary<string, WorkGroupDraft>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                string type = item.Type ?? string.Empty;
                string category = item.Category ?? string.Empty;
                string project = item.ProjectName ?? string.Empty;
                string key = type + "|" + category + "|" + project;

                WorkGroupDraft group;
                if (!map.TryGetValue(key, out group))
                {
                    group = new WorkGroupDraft
                    {
                        Type = type,
                        Category = category,
                        ProjectName = project,
                        Confidence = item.Confidence,
                        NeedsReview = item.NeedsReview,
                        ReviewReason = item.ReviewReason,
                        AppliedRuleCode = item.AppliedRuleCode
                    };
                    map[key] = group;
                    groups.Add(group);
                }
                else
                {
                    // 그룹 내 더 낮은 confidence / review 필요 시 승격
                    if (IsLowerConfidence(item.Confidence, group.Confidence))
                        group.Confidence = item.Confidence;
                    if (item.NeedsReview)
                    {
                        group.NeedsReview = true;
                        if (string.IsNullOrEmpty(group.ReviewReason))
                            group.ReviewReason = item.ReviewReason;
                    }
                }

                group.SourceItems.Add(item);
            }

            return groups;
        }

        private static bool IsLowerConfidence(string candidate, string current)
        {
            return Rank(candidate) < Rank(current);
        }

        private static int Rank(string c)
        {
            if (string.Equals(c, ParseConfidence.High, StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(c, ParseConfidence.Medium, StringComparison.OrdinalIgnoreCase))
                return 2;
            return 1;
        }

        private static string ExtractFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            string normalized = path.Replace('/', '\\').TrimEnd('\\');
            int idx = normalized.LastIndexOf('\\');
            return idx >= 0 && idx < normalized.Length - 1
                ? normalized.Substring(idx + 1)
                : normalized;
        }

        private static string InferItemType(string path, string fileName)
        {
            string name = fileName ?? string.Empty;
            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                name = ExtractFileName(path);
            if (string.IsNullOrEmpty(name))
                return "Unknown";
            if (name.Contains("."))
                return "File";
            return "Unknown";
        }

        private static DateTime? TryParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            DateTime dt;
            if (DateTime.TryParse(value, out dt))
                return dt;
            return null;
        }
    }

    public static class WorkLogExcelPreviewMapper
    {
        public static IReadOnlyList<WorkLogExcelPreviewRow> ToPreviewRows(WorkLogDraft draft)
        {
            var rows = new List<WorkLogExcelPreviewRow>();
            if (draft == null)
                return rows;

            if (draft.WorkGroups == null || draft.WorkGroups.Count == 0)
            {
                rows.Add(CreateRow(draft, null));
                return rows;
            }

            foreach (var group in draft.WorkGroups)
                rows.Add(CreateRow(draft, group));
            return rows;
        }

        private static WorkLogExcelPreviewRow CreateRow(WorkLogDraft draft, WorkGroupDraft group)
        {
            string sources = string.Empty;
            if (group != null && group.SourceItems != null && group.SourceItems.Count > 0)
            {
                sources = string.Join(
                    Environment.NewLine,
                    group.SourceItems
                        .Where(s => s != null)
                        .Select(s => !string.IsNullOrWhiteSpace(s.DisplayValue) ? s.DisplayValue : (s.FileName ?? s.OriginalPath ?? string.Empty)));
            }

            return new WorkLogExcelPreviewRow
            {
                TicketNo = draft.TicketNo,
                TicketContents = draft.TicketContents,
                MenuName = draft.MenuName,
                Pc = draft.Pc,
                Type = group != null ? group.Type : string.Empty,
                Category = group != null ? group.Category : string.Empty,
                ProjectName = group != null ? group.ProjectName : string.Empty,
                SourcePathAndFileName = sources,
                PersonInCharge = draft.PersonInCharge,
                StartDate = draft.StartDate,
                EndDate = draft.EndDate,
                DeploymentStatus = draft.DeploymentStatus,
                DeploymentDate = draft.DeploymentDate,
                Comment = draft.Comment,
                ChangesetId = draft.ChangesetId,
                NeedsReview = group != null && group.NeedsReview,
                ReviewReason = group != null ? group.ReviewReason : null
            };
        }

        public static string FormatDebugPreview(WorkLogDraft draft)
        {
            if (draft == null)
                return "(없음)";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Changeset ID: " + draft.ChangesetId);
            sb.AppendLine("Ticket No: " + (draft.TicketNo ?? ""));
            sb.AppendLine("Ticket Contents: " + (draft.TicketContents ?? "")
                + (draft.TicketContentsIsSuggested ? " [Suggested]" : ""));
            sb.AppendLine("PC: " + (draft.Pc ?? ""));
            sb.AppendLine("Person: " + (draft.PersonInCharge ?? ""));
            sb.AppendLine("Start: " + (draft.StartDate.HasValue ? draft.StartDate.Value.ToString("yyyy-MM-dd HH:mm") : ""));
            sb.AppendLine("End: " + (draft.EndDate.HasValue ? draft.EndDate.Value.ToString("yyyy-MM-dd HH:mm") : ""));
            sb.AppendLine("WorkGroups: " + (draft.WorkGroups != null ? draft.WorkGroups.Count : 0));
            if (draft.WorkGroups != null)
            {
                int i = 1;
                foreach (var g in draft.WorkGroups)
                {
                    sb.AppendLine(string.Format(
                        "  [{0}] Type={1} Category={2} Project={3} Sources={4} NeedsReview={5} Reason={6} Rule={7} Confidence={8}",
                        i++,
                        g.Type ?? "",
                        g.Category ?? "",
                        g.ProjectName ?? "",
                        g.SourceItems != null ? g.SourceItems.Count : 0,
                        g.NeedsReview,
                        g.ReviewReason ?? "",
                        g.AppliedRuleCode ?? "",
                        g.Confidence ?? ""));
                }
            }
            if (draft.Warnings != null && draft.Warnings.Count > 0)
                sb.AppendLine("Warnings: " + string.Join(" | ", draft.Warnings));
            return sb.ToString();
        }
    }
}
