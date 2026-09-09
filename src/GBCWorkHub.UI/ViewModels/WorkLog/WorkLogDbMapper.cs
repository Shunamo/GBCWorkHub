using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>UI WorkLogListItem ↔ MSDWHTKD_WRK DTO</summary>
    public static class WorkLogDbMapper
    {
        public const string DbDraft = "DRAFT";
        public const string DbCompleted = "COMPLETED";

        public static string ToDbWriteStatus(string uiStatus)
        {
            if (string.Equals(uiStatus, WorkLogWriteStatus.Completed, StringComparison.Ordinal))
                return DbCompleted;
            return DbDraft;
        }

        public static string ToUiWriteStatus(string dbStatus)
        {
            if (string.Equals(dbStatus, DbCompleted, StringComparison.OrdinalIgnoreCase))
                return WorkLogWriteStatus.Completed;
            return WorkLogWriteStatus.Draft;
        }

        public static WorkLogRecordDto ToDto(WorkLogListItemViewModel item)
        {
            if (item == null)
                return null;

            var dto = new WorkLogRecordDto
            {
                LogId = item.DbLogId,
                ClientKey = item.Id,
                SiteCode = item.SiteCode,
                WriteStatus = ToDbWriteStatus(item.WriteStatus),
                TicketNo = item.TicketNo,
                TicketContents = item.TicketContents,
                MenuName = item.MenuName,
                PcName = item.Pc,
                PersonInCharge = item.PersonInCharge,
                LocalPcIp = item.LocalPcIp,
                StartDate = item.StartDate,
                EndDate = item.EndDate,
                DeploymentStatus = item.DeploymentStatus,
                DeploymentDate = item.DeploymentDate,
                WorkComment = item.Comment,
                ChangesetId = item.ChangesetId,
                TfsComment = item.TfsComment,
                TfsAuthor = item.TfsAuthor,
                AuthorName = item.AuthorName,
                UserId = item.AuthorUserId,
                TeamName = ResolveTeamName(item),
                CheckedInAt = item.CheckedInAt,
                ChangedFileCount = item.PayloadChangedFileCount > 0
                    ? item.PayloadChangedFileCount
                    : item.SourceCount,
                NeedsTicketReview = item.NeedsTicketReview,
                UpdatedAt = item.LastModifiedAt
            };

            dto.ChangesetIds = item.GetEffectiveChangesetIds() ?? new List<int>();
            if (dto.ChangesetId <= 0 && dto.ChangesetIds.Count > 0)
                dto.ChangesetId = dto.ChangesetIds[0];

            if (item.Groups != null)
            {
                int ord = 0;
                foreach (var g in item.Groups)
                {
                    if (g == null)
                        continue;
                    dto.Projects.Add(new WorkLogProjectDto
                    {
                        Type = g.Type,
                        Category = g.Category,
                        ProjectName = g.ProjectName,
                        SourceOrigin = g.SourceOrigin,
                        DeploymentStatus = g.DeploymentStatus,
                        DeploymentDate = g.DeploymentDate,
                        Comment = g.Comment,
                        SortOrder = ord++
                    });
                }
            }

            if (item.Sources != null)
            {
                int ord = 0;
                foreach (var s in item.Sources)
                {
                    if (s == null)
                        continue;
                    dto.Sources.Add(new WorkLogSourceDto
                    {
                        FileName = s.FileName,
                        OriginalPath = s.OriginalPath,
                        ChangeType = s.ChangeType,
                        ChangeDetail = s.ChangeDetailText,
                        Type = s.Type,
                        Category = s.Category,
                        ProjectName = s.ProjectName,
                        SourceOrigin = s.SourceOrigin,
                        AppliedRuleCode = s.AppliedRuleCode,
                        IsAutoClassified = s.IsAutoClassified,
                        NeedsReview = s.NeedsReview,
                        ReviewReason = s.ReviewReason,
                        SortOrder = ord++,
                        CreatedAt = s.RecordedAt
                    });
                }
            }

            return dto;
        }

        public static WorkLogListItemViewModel FromDto(WorkLogRecordDto dto)
        {
            if (dto == null)
                return null;

            var item = new WorkLogListItemViewModel
            {
                DbLogId = dto.LogId,
                Id = !string.IsNullOrWhiteSpace(dto.ClientKey)
                    ? dto.ClientKey
                    : ("db-" + dto.LogId),
                SiteCode = dto.SiteCode,
                TicketNo = dto.TicketNo,
                TicketContents = dto.TicketContents,
                MenuName = dto.MenuName,
                Pc = dto.PcName,
                PersonInCharge = dto.PersonInCharge,
                LocalPcIp = dto.LocalPcIp,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                DeploymentStatus = dto.DeploymentStatus,
                DeploymentDate = dto.DeploymentDate,
                Comment = dto.WorkComment,
                WriteStatus = ToUiWriteStatus(dto.WriteStatus),
                LastModifiedAt = dto.UpdatedAt ?? dto.CreatedAt ?? DateTime.Now,
                ChangesetId = dto.ChangesetId,
                TfsComment = dto.TfsComment,
                TfsAuthor = dto.TfsAuthor,
                AuthorName = dto.AuthorName,
                AuthorUserId = dto.UserId,
                IsAuthorDeleted = dto.IsAuthorDeleted,
                TeamName = dto.TeamName,
                CheckedInAt = dto.CheckedInAt,
                PayloadChangedFileCount = dto.ChangedFileCount,
                NeedsTicketReview = dto.NeedsTicketReview
            };

            if (dto.Sources != null)
            {
                foreach (var s in dto.Sources)
                {
                    if (s == null)
                        continue;
                    var baseItem = new WorkLogSourceEditItem
                    {
                        FileName = s.FileName,
                        OriginalPath = s.OriginalPath,
                        ChangeType = s.ChangeType,
                        ChangeDetailText = s.ChangeDetail,
                        Type = s.Type,
                        Category = s.Category,
                        ProjectName = s.ProjectName,
                        SourceOrigin = s.SourceOrigin,
                        AppliedRuleCode = s.AppliedRuleCode,
                        IsAutoClassified = s.IsAutoClassified,
                        NeedsReview = s.NeedsReview,
                        ReviewReason = s.ReviewReason,
                        RecordedAt = s.CreatedAt
                            ?? dto.CreatedAt
                            ?? dto.CheckedInAt
                            ?? dto.StartDate
                    };
                    foreach (var expanded in WorkLogTicketTreeViewModel.ExpandSourceEntries(baseItem))
                        item.Sources.Add(expanded);
                }
            }

            WorkLogDraftMapper.RebuildGroupsFromSources(item);

            if (dto.Projects != null && item.Groups != null)
            {
                foreach (var p in dto.Projects)
                {
                    if (p == null)
                        continue;
                    foreach (var g in item.Groups)
                    {
                        if (g == null)
                            continue;
                        if (!string.Equals(g.Type ?? "", p.Type ?? "", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!string.Equals(g.Category ?? "", p.Category ?? "", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!string.Equals(g.ProjectName ?? "", p.ProjectName ?? "", StringComparison.OrdinalIgnoreCase))
                            continue;
                        g.DeploymentStatus = p.DeploymentStatus;
                        g.DeploymentDate = p.DeploymentDate;
                        g.Comment = p.Comment;
                        if (!string.IsNullOrWhiteSpace(p.SourceOrigin))
                            g.SourceOrigin = p.SourceOrigin;
                        break;
                    }
                }
            }

            if (item.Groups != null && item.Groups.Count > 0)
            {
                var first = item.Groups[0];
                if (first != null)
                {
                    if (string.IsNullOrWhiteSpace(item.DeploymentStatus))
                        item.DeploymentStatus = first.DeploymentStatus;
                    if (!item.DeploymentDate.HasValue)
                        item.DeploymentDate = first.DeploymentDate;
                    if (string.IsNullOrWhiteSpace(item.Comment))
                        item.Comment = first.Comment;
                }
            }

            WorkLogListItemViewModel.RebuildTypeBadges(item);
            if (dto.ChangesetIds != null && dto.ChangesetIds.Count > 0)
            {
                item.SourceChangesetIds = new List<int>();
                foreach (int id in dto.ChangesetIds)
                {
                    if (id > 0 && !item.SourceChangesetIds.Contains(id))
                        item.SourceChangesetIds.Add(id);
                }
                if (item.ChangesetId <= 0)
                    item.ChangesetId = item.SourceChangesetIds[0];
            }
            else
            {
                item.SourceChangesetIds = ParseChangesetIds(item.TfsComment, item.ChangesetId);
            }
            item.NotifyListPresentation();
            return item;
        }

        private static List<int> ParseChangesetIds(string tfsComment, int fallbackId)
        {
            var ids = new List<int>();
            if (!string.IsNullOrWhiteSpace(tfsComment))
            {
                foreach (Match m in Regex.Matches(tfsComment, @"\[CS\s*(\d+)\]", RegexOptions.IgnoreCase))
                {
                    int id;
                    if (int.TryParse(m.Groups[1].Value, out id) && id > 0 && !ids.Contains(id))
                        ids.Add(id);
                }
            }
            if (ids.Count == 0 && fallbackId > 0)
                ids.Add(fallbackId);
            return ids;
        }

        private static string ResolveTeamName(WorkLogListItemViewModel item)
        {
            if (item == null)
                return null;
            if (!string.IsNullOrWhiteSpace(item.TeamName))
                return item.TeamName.Trim();
            if (WorkHubUserProfile.OwnsRecord(item.AuthorName, item.LocalPcIp)
                || WorkHubUserProfile.MatchesIp(item.LocalPcIp))
                return OccupancyNameStore.TryGetAffiliation();
            return null;
        }
    }
}
