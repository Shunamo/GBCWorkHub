using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 실제 WorkLogDraft / Candidate → 화면 모델 매핑. Mock 데이터 없음.
    /// </summary>
    public static class WorkLogDraftMapper
    {
        private static readonly ITfsWorkLogParser Parser = new TfsWorkLogParser();

        /// <summary>
        /// 화면 표시용.
        /// 여러 티켓(줄바꿈/콤마) → "TN-12345, TN-12346".
        /// 숫자 티켓만 TN- 접두사. 문자/한글 티켓은 원문 그대로.
        /// </summary>
        public static string FormatTicketDisplay(string ticketNo)
        {
            var parts = SplitMultiValues(ticketNo);
            if (parts.Count == 0)
                return string.Empty;

            var formatted = new List<string>();
            foreach (var part in parts)
            {
                string raw = StripTicketPrefix(part);
                if (string.IsNullOrEmpty(raw))
                    continue;
                formatted.Add(IsNumericTicket(raw) ? "TN-" + raw : raw);
            }
            return string.Join(", ", formatted);
        }

        /// <summary>담당자 여러 명(줄바꿈/콤마 등) → "정다은, 오기영".</summary>
        public static string FormatPersonDisplay(string personInCharge)
        {
            var parts = SplitMultiValues(personInCharge);
            if (parts.Count == 0)
                return string.Empty;
            return string.Join(", ", parts);
        }

        /// <summary>저장용: TN- 접두사 제거, 복수면 콤마로 정규화.</summary>
        public static string NormalizeTicketStorage(string ticketNo)
        {
            var parts = SplitMultiValues(ticketNo);
            if (parts.Count == 0)
                return string.Empty;

            var cleaned = new List<string>();
            foreach (var part in parts)
            {
                string t = StripTicketPrefix(part);
                if (!string.IsNullOrEmpty(t))
                    cleaned.Add(t);
            }
            return string.Join(", ", cleaned);
        }

        /// <summary>저장용: 담당자 복수 입력을 콤마 구분 한 줄로.</summary>
        public static string NormalizePersonStorage(string personInCharge)
        {
            return FormatPersonDisplay(personInCharge);
        }

        /// <summary>순수 숫자 티켓 여부 (11541 등). 내부/티켓파악불가/ABC-12 는 false.</summary>
        public static bool IsNumericTicket(string ticketNo)
        {
            if (string.IsNullOrWhiteSpace(ticketNo))
                return false;
            string t = StripTicketPrefix(ticketNo);
            if (t.Length == 0)
                return false;
            for (int i = 0; i < t.Length; i++)
            {
                if (!char.IsDigit(t[i]))
                    return false;
            }
            return true;
        }

        private static string StripTicketPrefix(string ticketNo)
        {
            if (string.IsNullOrWhiteSpace(ticketNo))
                return string.Empty;
            string t = ticketNo.Trim();
            // [1234] / [ TN-1234 ] 입력도 숫자만 남김
            if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']')
                t = t.Substring(1, t.Length - 2).Trim();
            while (t.StartsWith("TN-", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("TN.", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("TN ", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(3).Trim();
            }
            if (t.StartsWith("TN", StringComparison.OrdinalIgnoreCase)
                && t.Length > 2
                && char.IsDigit(t[2]))
            {
                t = t.Substring(2).Trim();
            }
            return t.Trim('[', ']').Trim();
        }

        /// <summary>줄바꿈·콤마·슬래시 등으로 나뉜 복수 값을 순서 유지·중복 제거.</summary>
        public static List<string> SplitMultiValues(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return list;

            string normalized = raw.Replace("　", " ")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace('/', '\n')
                .Replace('|', '\n')
                .Replace(';', '\n')
                .Replace('，', '\n');

            foreach (var part in normalized.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (string.IsNullOrEmpty(t) || t == "-" || t == "########")
                    continue;
                bool exists = false;
                foreach (var x in list)
                {
                    if (string.Equals(x, t, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    list.Add(t);
            }
            return list;
        }

        public static WorkLogListItemViewModel FromCandidate(
            TfsChangesetCandidateViewModel candidate,
            WorkSessionContext sessionContext)
        {
            if (candidate == null)
                return null;

            var draft = Parser.Parse(candidate.ToChangesetItem(), sessionContext ?? new WorkSessionContext());
            var item = FromDraft(draft, candidate);
            ApplyWorkHubAuthor(item, sessionContext);
            return item;
        }

        /// <summary>가져오기 팝업에서 사용자가 고른 파일만 포함한 Changeset으로 매핑.</summary>
        public static WorkLogListItemViewModel FromImportRow(
            TfsImportCandidateRow row,
            WorkSessionContext sessionContext)
        {
            if (row == null || row.Source == null)
                return null;

            var changeset = row.BuildChangesetForImport();
            var draft = Parser.Parse(changeset, sessionContext ?? new WorkSessionContext(), true);
            var item = FromDraft(draft, row.Source);
            if (item != null)
                item.PayloadChangedFileCount = row.IncludedFileCount;
            ApplyWorkHubAuthor(item, sessionContext);
            return item;
        }

        public static WorkLogListItemViewModel FromDraft(
            WorkLogDraft draft,
            TfsChangesetCandidateViewModel candidate)
        {
            if (draft == null)
                return null;

            var item = new WorkLogListItemViewModel
            {
                Id = "cs-" + draft.ChangesetId,
                TicketNo = draft.TicketNo ?? string.Empty,
                NeedsTicketReview = string.IsNullOrWhiteSpace(draft.TicketNo),
                TicketContents = draft.TicketContents ?? string.Empty,
                MenuName = draft.MenuName ?? string.Empty,
                Pc = draft.Pc ?? (candidate != null ? candidate.RemoteComputerName : string.Empty),
                PersonInCharge = draft.PersonInCharge ?? string.Empty,
                AuthorName = !string.IsNullOrWhiteSpace(draft.TfsAuthorName)
                    ? draft.TfsAuthorName
                    : (draft.TfsAuthorId ?? (candidate != null ? candidate.AuthorName : string.Empty)),
                StartDate = draft.StartDate,
                EndDate = draft.EndDate,
                DeploymentStatus = draft.DeploymentStatus ?? string.Empty,
                DeploymentDate = draft.DeploymentDate,
                // Comment는 사용자가 Project에 남긴 메모만. TicketContents와 섞지 않음.
                Comment = draft.Comment ?? string.Empty,
                WriteStatus = WorkLogWriteStatus.Draft,
                LastModifiedAt = DateTime.Now,
                ChangesetId = draft.ChangesetId,
                TfsComment = draft.TfsComment ?? (candidate != null ? candidate.OriginalComment : string.Empty),
                TfsAuthor = draft.TfsAuthorId ?? (candidate != null ? candidate.AuthorId : string.Empty),
                CheckedInAt = draft.CheckedInAt
                    ?? (candidate != null ? candidate.CheckedInAt : null),
                PayloadChangedFileCount = candidate != null ? candidate.ChangedFileCount : 0
            };

            if (draft.ChangesetId > 0)
                item.SourceChangesetIds = new List<int> { draft.ChangesetId };

            if (string.IsNullOrWhiteSpace(item.TicketContents))
                item.TicketContents = item.TfsComment ?? string.Empty;

            if (candidate != null && !item.CheckedInAt.HasValue)
            {
                DateTime parsed;
                if (!string.IsNullOrWhiteSpace(candidate.CheckedInAtDisplay)
                    && DateTime.TryParse(candidate.CheckedInAtDisplay, out parsed))
                    item.CheckedInAt = parsed;
            }

            item.Sources.Clear();
            if (draft.WorkGroups != null)
            {
                foreach (var g in draft.WorkGroups)
                {
                    if (g == null || g.SourceItems == null)
                        continue;
                    foreach (var s in g.SourceItems)
                    {
                        if (s == null)
                            continue;
                        item.Sources.Add(new WorkLogSourceEditItem
                        {
                            FileName = s.FileName,
                            OriginalPath = s.OriginalPath,
                            ChangeType = s.ChangeType,
                            ChangeDetailText = !string.IsNullOrWhiteSpace(s.FileName) ? s.FileName : s.OriginalPath,
                            Type = s.Type ?? g.Type,
                            Category = s.Category ?? g.Category,
                            ProjectName = s.ProjectName ?? g.ProjectName ?? string.Empty,
                            SourceOrigin = s.SourceOrigin ?? "TFS",
                            NeedsReview = s.NeedsReview || g.NeedsReview,
                            ReviewReason = s.ReviewReason ?? g.ReviewReason,
                            AppliedRuleCode = s.AppliedRuleCode ?? g.AppliedRuleCode,
                            IsAutoClassified = !s.NeedsReview && !g.NeedsReview,
                            RecordedAt = item.CheckedInAt ?? DateTime.Now
                        });
                    }
                }
            }

            RebuildGroupsFromSources(item);
            WorkLogListItemViewModel.RebuildTypeBadges(item);
            return item;
        }

        /// <summary>여러 체크인(가져오기 행)을 하나의 업무 기록으로 합칩니다.</summary>
        public static WorkLogListItemViewModel FromImportRowsMerged(
            IList<TfsImportCandidateRow> rows,
            WorkSessionContext sessionContext)
        {
            if (rows == null || rows.Count == 0)
                return null;

            var mapped = new List<WorkLogListItemViewModel>();
            foreach (var row in rows)
            {
                var one = FromImportRow(row, sessionContext);
                if (one != null)
                    mapped.Add(one);
            }

            if (mapped.Count == 0)
                return null;
            if (mapped.Count == 1)
                return mapped[0];

            return MergeMappedItems(mapped, sessionContext);
        }

        /// <summary>여러 체크인을 하나의 업무 기록으로 합칩니다.</summary>
        public static WorkLogListItemViewModel FromCandidatesMerged(
            IList<TfsChangesetCandidateViewModel> candidates,
            WorkSessionContext sessionContext)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            var mapped = new List<WorkLogListItemViewModel>();
            foreach (var c in candidates)
            {
                var one = FromCandidate(c, sessionContext);
                if (one != null)
                    mapped.Add(one);
            }

            if (mapped.Count == 0)
                return null;
            if (mapped.Count == 1)
                return mapped[0];

            return MergeMappedItems(mapped, sessionContext);
        }

        /// <summary>
        /// 편집 취소/닫기용 목록 항목 스냅샷 (소스·체크인·티켓 등 UI 상태).
        /// </summary>
        public static WorkLogListItemViewModel CloneListItemBaseline(WorkLogListItemViewModel source)
        {
            if (source == null)
                return null;

            var snap = new WorkLogListItemViewModel
            {
                Id = source.Id,
                DbLogId = source.DbLogId,
                SiteCode = source.SiteCode,
                TicketNo = source.TicketNo,
                TicketContents = source.TicketContents,
                MenuName = source.MenuName,
                Pc = source.Pc,
                PersonInCharge = source.PersonInCharge,
                LocalPcIp = source.LocalPcIp,
                StartDate = source.StartDate,
                EndDate = source.EndDate,
                DeploymentStatus = source.DeploymentStatus,
                DeploymentDate = source.DeploymentDate,
                Comment = source.Comment,
                WriteStatus = source.WriteStatus,
                LastModifiedAt = source.LastModifiedAt,
                ChangesetId = source.ChangesetId,
                TfsComment = source.TfsComment,
                TfsAuthor = source.TfsAuthor,
                AuthorName = source.AuthorName,
                CheckedInAt = source.CheckedInAt,
                PayloadChangedFileCount = source.PayloadChangedFileCount,
                NeedsTicketReview = source.NeedsTicketReview
            };

            if (source.SourceChangesetIds != null)
                snap.SourceChangesetIds = new List<int>(source.SourceChangesetIds);

            if (source.Sources != null)
            {
                foreach (var s in source.Sources)
                {
                    if (s == null)
                        continue;
                    snap.Sources.Add(WorkLogTicketTreeViewModel.CloneSource(s));
                }
            }

            RebuildGroupsFromSources(snap);
            WorkLogListItemViewModel.RebuildTypeBadges(snap);
            return snap;
        }

        /// <summary>스냅샷 내용을 목록 항목에 되돌린다 (미저장 가져오기 취소).</summary>
        public static void RestoreListItemFromBaseline(
            WorkLogListItemViewModel target,
            WorkLogListItemViewModel baseline)
        {
            if (target == null || baseline == null)
                return;

            target.TicketNo = baseline.TicketNo;
            target.TicketContents = baseline.TicketContents;
            target.MenuName = baseline.MenuName;
            target.Pc = baseline.Pc;
            target.PersonInCharge = baseline.PersonInCharge;
            target.LocalPcIp = baseline.LocalPcIp;
            target.StartDate = baseline.StartDate;
            target.EndDate = baseline.EndDate;
            target.DeploymentStatus = baseline.DeploymentStatus;
            target.DeploymentDate = baseline.DeploymentDate;
            target.Comment = baseline.Comment;
            target.WriteStatus = baseline.WriteStatus;
            target.LastModifiedAt = baseline.LastModifiedAt;
            target.ChangesetId = baseline.ChangesetId;
            target.TfsComment = baseline.TfsComment;
            target.TfsAuthor = baseline.TfsAuthor;
            target.AuthorName = baseline.AuthorName;
            target.CheckedInAt = baseline.CheckedInAt;
            target.PayloadChangedFileCount = baseline.PayloadChangedFileCount;
            target.NeedsTicketReview = baseline.NeedsTicketReview;
            target.SiteCode = baseline.SiteCode;

            target.SourceChangesetIds = baseline.SourceChangesetIds != null
                ? new List<int>(baseline.SourceChangesetIds)
                : new List<int>();

            target.Sources.Clear();
            if (baseline.Sources != null)
            {
                foreach (var s in baseline.Sources)
                {
                    if (s == null)
                        continue;
                    target.Sources.Add(WorkLogTicketTreeViewModel.CloneSource(s));
                }
            }

            RebuildGroupsFromSources(target);
            WorkLogListItemViewModel.RebuildTypeBadges(target);
            target.NotifyListPresentation();
        }

        /// <summary>
        /// 가져온 체크인 소스를 기존 업무기록에 추가 (경로 중복은 건너뜀).
        /// LocalPcIp 미기록이면 현재 세션 IP로 채움(합치기 후 수정/삭제 가능).
        /// 코멘트/티켓 본문은 덮어쓰지 않고 타임라인 블록으로 쌓음.
        /// 반환: 새로 붙인 소스 수.
        /// </summary>
        public static int AppendIncomingToExisting(
            WorkLogListItemViewModel existing,
            WorkLogListItemViewModel incoming)
        {
            if (existing == null || incoming == null)
                return 0;

            if (existing.Sources == null)
                existing.Sources = new ObservableCollection<WorkLogSourceEditItem>();

            // 기존 소스: 기록일 보존(없으면 업무기록 날짜로 채움), 추가 표시는 유지
            DateTime? originalStamp = existing.StartDate
                ?? existing.CheckedInAt
                ?? (existing.LastModifiedAt > DateTime.MinValue ? existing.LastModifiedAt : (DateTime?)null);
            foreach (var s in existing.Sources)
            {
                if (s == null)
                    continue;
                if (!s.RecordedAt.HasValue)
                    s.RecordedAt = originalStamp ?? DateTime.Now;
            }

            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in existing.Sources)
            {
                if (s != null && !string.IsNullOrWhiteSpace(s.OriginalPath))
                    existingPaths.Add(s.OriginalPath.Trim());
            }

            DateTime? stampAt = incoming.CheckedInAt ?? DateTime.Now;
            string stampAuthor = FirstNonEmpty(
                incoming.AuthorName,
                incoming.TfsAuthor,
                incoming.PersonInCharge,
                "체크인");

            int added = 0;
            if (incoming.Sources != null)
            {
                foreach (var s in incoming.Sources)
                {
                    if (s == null)
                        continue;
                    var clone = WorkLogTicketTreeViewModel.CloneSource(s);
                    string path = (clone.OriginalPath ?? string.Empty).Trim();
                    if (path.Length > 0 && existingPaths.Contains(path))
                        continue;
                    clone.SourceOrigin = WorkLogSourceEditItem.MarkAppendedOrigin(clone.SourceOrigin);
                    clone.RecordedAt = stampAt;
                    existing.Sources.Add(clone);
                    if (path.Length > 0)
                        existingPaths.Add(path);
                    added++;
                }
            }

            var ids = existing.GetEffectiveChangesetIds() ?? new List<int>();
            foreach (int id in incoming.GetEffectiveChangesetIds() ?? new List<int>())
            {
                if (id > 0 && !ids.Contains(id))
                    ids.Add(id);
            }
            existing.SourceChangesetIds = ids;
            if (existing.ChangesetId <= 0 && ids.Count > 0)
                existing.ChangesetId = ids[0];

            // 미기록 LocalPcIp → 합치는 PC IP로 소유권 부여
            if (string.IsNullOrWhiteSpace(existing.LocalPcIp))
            {
                string claimIp = !string.IsNullOrWhiteSpace(incoming.LocalPcIp)
                    ? incoming.LocalPcIp.Trim()
                    : WorkHubUserProfile.LocalIp;
                if (!string.IsNullOrWhiteSpace(claimIp))
                    existing.LocalPcIp = claimIp;
            }

            if (string.IsNullOrWhiteSpace(existing.Pc) && !string.IsNullOrWhiteSpace(incoming.Pc))
                existing.Pc = incoming.Pc;
            if (string.IsNullOrWhiteSpace(existing.PersonInCharge)
                && !string.IsNullOrWhiteSpace(incoming.PersonInCharge))
                existing.PersonInCharge = incoming.PersonInCharge;
            if (string.IsNullOrWhiteSpace(existing.AuthorName)
                && !string.IsNullOrWhiteSpace(incoming.AuthorName))
                existing.AuthorName = incoming.AuthorName;

            // 업무 작성 기간: 시작일은 보존, 종료일만 확장
            if (!existing.StartDate.HasValue)
            {
                existing.StartDate = existing.CheckedInAt
                    ?? incoming.StartDate
                    ?? stampAt;
            }
            DateTime? endCandidate = incoming.EndDate ?? stampAt;
            if (endCandidate.HasValue
                && (!existing.EndDate.HasValue || endCandidate.Value > existing.EndDate.Value))
                existing.EndDate = endCandidate;

            // 목록 타임라인 날짜(CheckedInAt)는 유지. 없을 때만 채움.
            if (!existing.CheckedInAt.HasValue && incoming.CheckedInAt.HasValue)
                existing.CheckedInAt = incoming.CheckedInAt;

            // TicketContents(업무 제목)는 타임라인에 섞지 않음. COMMENT 필드만 누적.
            string incomingBody = FirstNonEmpty(incoming.Comment, incoming.TfsComment);
            string timelineBody = !string.IsNullOrWhiteSpace(incomingBody)
                ? incomingBody
                : ("체크인 소스 추가" + (added > 0 ? " · " + added + "건" : string.Empty));

            existing.TfsComment = AppendTimelineBlock(existing.TfsComment, timelineBody, stampAt, stampAuthor);
            existing.Comment = AppendTimelineBlock(existing.Comment, timelineBody, stampAt, stampAuthor);

            if (string.IsNullOrWhiteSpace(existing.TicketNo) && !string.IsNullOrWhiteSpace(incoming.TicketNo))
            {
                existing.TicketNo = incoming.TicketNo;
                existing.NeedsTicketReview = incoming.NeedsTicketReview;
            }

            existing.LastModifiedAt = DateTime.Now;
            if (existing.IsCompleted)
                existing.WriteStatus = WorkLogWriteStatus.Draft;

            RebuildGroupsFromSources(existing);

            // 그룹(프로젝트) 코멘트에도 동일 타임라인 블록 누적
            if (existing.Groups != null && existing.Groups.Count > 0)
            {
                var g = existing.Groups[0];
                if (g != null)
                    g.Comment = AppendTimelineBlock(g.Comment, timelineBody, stampAt, stampAuthor);
            }

            WorkLogListItemViewModel.RebuildTypeBadges(existing);
            existing.NotifyListPresentation();
            return added;
        }

        private static readonly Regex TimelineBadgeLine =
            new Regex(@"^\s*\d{4}-\d{2}-\d{2}\s*/\s*.+?\s*$", RegexOptions.Compiled);

        /// <summary>
        /// COMMENT 타임라인용 블록 누적.
        /// 최초 본문은 날짜 없이, 이후 추가는 "yyyy-MM-dd / 작성자" + 본문.
        /// </summary>
        public static string AppendTimelineBlock(
            string existing,
            string incomingBody,
            DateTime? at,
            string author)
        {
            if (string.IsNullOrWhiteSpace(incomingBody))
                return existing ?? string.Empty;

            string body = incomingBody.Trim();
            string current = (existing ?? string.Empty).Trim();
            if (current.IndexOf(body, StringComparison.OrdinalIgnoreCase) >= 0)
                return current;

            // 최초 Comment는 일반 본문(날짜 배지 없음)
            if (string.IsNullOrEmpty(current))
                return body;

            string day = (at ?? DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string who = string.IsNullOrWhiteSpace(author) ? "체크인" : author.Trim();
            string block = day + " / " + who + Environment.NewLine + body;
            return current + Environment.NewLine + Environment.NewLine + block;
        }

        /// <summary>업무 제목: 첫 타임라인 배지 줄 이전만.</summary>
        public static string ExtractTitleBeforeTimeline(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                if (TimelineBadgeLine.IsMatch(line ?? string.Empty))
                    break;
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(line);
            }
            return sb.ToString().Trim();
        }

        /// <summary>잘못 TicketContents에 붙은 타임라인 구간 복구용.</summary>
        public static string ExtractTimelinePortion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (TimelineBadgeLine.IsMatch(lines[i] ?? string.Empty))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(lines[i]);
            }
            return sb.ToString().Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;
            foreach (string v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return string.Empty;
        }

        private static WorkLogListItemViewModel MergeMappedItems(
            IList<WorkLogListItemViewModel> mapped,
            WorkSessionContext sessionContext)
        {
            var primary = mapped[0];
            primary.Id = "merge-" + Guid.NewGuid().ToString("N").Substring(0, 10);

            var comments = new List<string>();
            var tfsAuthors = new List<string>();
            var changesetIds = new List<int>();
            int fileCount = 0;
            DateTime? earliest = null;
            DateTime? latest = null;

            primary.Sources.Clear();
            foreach (var part in mapped)
            {
                foreach (var s in part.Sources)
                    primary.Sources.Add(s);

                if (part.ChangesetId > 0 && !changesetIds.Contains(part.ChangesetId))
                    changesetIds.Add(part.ChangesetId);

                if (!string.IsNullOrWhiteSpace(part.TfsComment))
                    comments.Add("[CS " + part.ChangesetId + "] " + part.TfsComment.Trim());
                else if (part.ChangesetId > 0)
                    comments.Add("[CS " + part.ChangesetId + "]");

                if (!string.IsNullOrWhiteSpace(part.TicketContents)
                    && string.IsNullOrWhiteSpace(part.TfsComment)
                    && part.ChangesetId > 0
                    && !comments.Any(x => x.StartsWith("[CS " + part.ChangesetId + "]", StringComparison.Ordinal)))
                    comments.Add("[CS " + part.ChangesetId + "] " + part.TicketContents.Trim());

                if (!string.IsNullOrWhiteSpace(part.TfsAuthor)
                    && !tfsAuthors.Any(a => string.Equals(a, part.TfsAuthor.Trim(), StringComparison.OrdinalIgnoreCase)))
                    tfsAuthors.Add(part.TfsAuthor.Trim());

                fileCount += part.PayloadChangedFileCount > 0
                    ? part.PayloadChangedFileCount
                    : part.Sources.Count;

                if (part.CheckedInAt.HasValue)
                {
                    if (!earliest.HasValue || part.CheckedInAt.Value < earliest.Value)
                        earliest = part.CheckedInAt;
                    if (!latest.HasValue || part.CheckedInAt.Value > latest.Value)
                        latest = part.CheckedInAt;
                }

                if (string.IsNullOrWhiteSpace(primary.TicketNo) && !string.IsNullOrWhiteSpace(part.TicketNo))
                {
                    primary.TicketNo = part.TicketNo;
                    primary.NeedsTicketReview = part.NeedsTicketReview;
                }
                if (string.IsNullOrWhiteSpace(primary.Pc) && !string.IsNullOrWhiteSpace(part.Pc))
                    primary.Pc = part.Pc;
            }

            string mergedComment = string.Join(Environment.NewLine + Environment.NewLine, comments);
            primary.SourceChangesetIds = changesetIds;
            primary.ChangesetId = changesetIds.Count > 0 ? changesetIds[0] : 0;
            primary.TfsComment = mergedComment;
            primary.TicketContents = mergedComment;
            // 우측 Comment 자리에는 넣지 않음 (티켓 contents는 좌측 전용)
            primary.Comment = string.Empty;
            primary.TfsAuthor = string.Join(", ", tfsAuthors);
            primary.PayloadChangedFileCount = fileCount;
            primary.CheckedInAt = latest ?? earliest;
            primary.WriteStatus = WorkLogWriteStatus.Draft;
            primary.LastModifiedAt = DateTime.Now;

            RebuildGroupsFromSources(primary);
            WorkLogListItemViewModel.RebuildTypeBadges(primary);
            ApplyWorkHubAuthor(primary, sessionContext);
            return primary;
        }

        public static void ApplyWorkHubAuthor(
            WorkLogListItemViewModel item,
            WorkSessionContext sessionContext)
        {
            if (item == null)
                return;

            string localIp = sessionContext != null
                ? sessionContext.ResolveAuthorLocalIp()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(localIp))
                localIp = WorkHubUserProfile.LocalIp;

            if (!string.IsNullOrWhiteSpace(localIp) && string.IsNullOrWhiteSpace(item.LocalPcIp))
                item.LocalPcIp = localIp.Trim();
        }

        public static void RebuildGroupsFromSources(WorkLogListItemViewModel item)
        {
            if (item == null)
                return;

            // Project별 배포/코멘트 보존
            var prev = new Dictionary<string, WorkGroupSummaryItem>(StringComparer.OrdinalIgnoreCase);
            if (item.Groups != null)
            {
                foreach (var g in item.Groups)
                {
                    string key = (g.Type ?? "") + "|" + (g.Category ?? "") + "|" + (g.ProjectName ?? "");
                    prev[key] = g;
                }
            }

            item.Groups.Clear();
            if (item.Sources == null)
                return;

            var groups = item.Sources
                .GroupBy(s => (s.Type ?? "") + "|" + (s.Category ?? "") + "|" + (s.ProjectName ?? ""),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                var sources = g.ToList();
                var first = sources[0];
                var tip = new StringBuilder();
                foreach (var s in sources)
                    tip.AppendLine((s.ChangeDetailText ?? s.FileName) + Environment.NewLine + "  " + s.OriginalPath);

                WorkGroupSummaryItem old;
                prev.TryGetValue(g.Key, out old);

                item.Groups.Add(new WorkGroupSummaryItem
                {
                    Type = first.Type ?? string.Empty,
                    Category = first.Category ?? string.Empty,
                    ProjectName = first.ProjectName ?? string.Empty,
                    SourceOrigin = first.SourceOrigin ?? "TFS",
                    SourceCount = sources.Count,
                    PrimarySourceName = first.ChangeDetailText ?? first.FileName,
                    NeedsReview = sources.Any(x => x.NeedsReview),
                    ReviewReason = sources.Where(x => x.NeedsReview).Select(x => x.ReviewReason).FirstOrDefault(),
                    AppliedRuleCode = first.AppliedRuleCode,
                    SourcesTooltip = tip.ToString().TrimEnd(),
                    DeploymentStatus = old != null ? old.DeploymentStatus : item.DeploymentStatus,
                    DeploymentDate = old != null && old.DeploymentDate.HasValue ? old.DeploymentDate : item.DeploymentDate,
                    Comment = old != null ? old.Comment : null
                });
            }
        }

        public static string FormatDateText(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        }

        /// <summary>
        /// 숫자만 받아 yyyy-MM-dd 형태로 자동 삽입. 최대 8자리.
        /// </summary>
        public static string MaskDateInput(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var digits = new System.Text.StringBuilder(8);
            foreach (char c in raw)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                    if (digits.Length >= 8)
                        break;
                }
            }

            string d = digits.ToString();
            if (d.Length <= 4)
                return d;
            if (d.Length <= 6)
                return d.Substring(0, 4) + "-" + d.Substring(4);
            return d.Substring(0, 4) + "-" + d.Substring(4, 2) + "-" + d.Substring(6);
        }

        public static bool TryParseDateText(string text, out DateTime? date, out string error)
        {
            date = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            string trimmed = text.Trim();
            DateTime parsed;
            if (!DateTime.TryParseExact(
                trimmed,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
            {
                // 형식 자체 불일치 vs 존재하지 않는 날짜 구분
                if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d{4}-\d{2}-\d{2}$"))
                    error = "올바른 날짜를 입력해 주세요.";
                else
                    error = "날짜를 2026-07-28 형식으로 입력해 주세요.";
                return false;
            }

            date = parsed.Date;
            return true;
        }
    }
}
