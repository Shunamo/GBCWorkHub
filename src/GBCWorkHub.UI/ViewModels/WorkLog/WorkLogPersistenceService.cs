using System;
using System.Threading.Tasks;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 업무기록 DB 조회/저장/삭제 orchestration.
    /// UI 상태(필터, Items, 페이지)는 ViewModel에 두고, 여기서는 Biz/Mapper 호출만 담당.
    /// </summary>
    public sealed class WorkLogPersistenceService
    {
        private readonly WorkLogBiz _workLogBiz;

        public WorkLogPersistenceService(WorkLogBiz workLogBiz)
        {
            if (workLogBiz == null)
                throw new ArgumentNullException("workLogBiz");
            _workLogBiz = workLogBiz;
        }

        public bool IsConfigured
        {
            get { return _workLogBiz.IsConfigured; }
        }

        public string LastConnectionError
        {
            get { return _workLogBiz.LastConnectionError; }
        }

        public WorkLogListQuery BuildListQuery(
            int pageIndex,
            int pageSize,
            string searchText,
            DateTime? fromDate,
            DateTime? toDate,
            string siteCode,
            string pcName,
            string type,
            string category,
            string deployStatus,
            string teamName)
        {
            return new WorkLogListQuery
            {
                PageIndex = pageIndex < 0 ? 0 : pageIndex,
                PageSize = pageSize,
                SearchText = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(),
                FromDate = fromDate,
                ToDate = toDate,
                SiteCode = ToDbFilterOrNull(siteCode),
                PcName = ToDbFilterOrNull(pcName),
                WriteStatus = null,
                Type = ToDbFilterOrNull(type),
                Category = ToDbFilterOrNull(category),
                DeployStatus = ToDbFilterOrNull(deployStatus),
                TeamName = ToDbFilterOrNull(teamName)
            };
        }

        public static string ToDbFilterOrNull(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string text = value.Trim();
            if (string.Equals(text, "ALL", StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(text, WorkLogFilterLabels.All, StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(text, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                return null;
            return text;
        }

        public static DateTime? ParseFilterDateOrNull(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length != 10)
                return null;
            DateTime? parsed;
            string err;
            if (!WorkLogDraftMapper.TryParseDateText(text, out parsed, out err))
                return null;
            return parsed;
        }

        public Task<WorkLogPageResult> GetPageAsync(WorkLogListQuery query)
        {
            return _workLogBiz.GetPageAsync(query);
        }

        public Task<System.Collections.Generic.IList<string>> GetDistinctTeamNamesAsync()
        {
            return _workLogBiz.GetDistinctTeamNamesAsync();
        }

        public Task<int> FillMissingTeamAsync(string authorName, string teamName, string localPcIp)
        {
            return _workLogBiz.FillMissingTeamAsync(authorName, teamName, localPcIp);
        }

        /// <summary>DTO 매핑 + Save. 성공 시 logId 반영된 DTO 반환.</summary>
        public async Task<WorkLogPersistResult> SaveAsync(WorkLogListItemViewModel item)
        {
            if (item == null)
                return WorkLogPersistResult.Fail("item is null");
            if (!_workLogBiz.IsConfigured)
                return WorkLogPersistResult.Fail("업무기록 DB 미설정 — 저장되지 않았습니다");

            try
            {
                var dto = WorkLogDbMapper.ToDto(item);
                bool ok = await _workLogBiz.SaveAsync(dto).ConfigureAwait(true);
                if (ok && dto != null)
                {
                    DiagnosticLogger.Info("WORKLOG_DB",
                        "saved LogId=" + dto.LogId + " Status=" + (dto.WriteStatus ?? "-"));
                    return WorkLogPersistResult.Ok(dto.LogId);
                }

                string err = _workLogBiz.LastConnectionError ?? "unknown";
                DiagnosticLogger.Warn("WORKLOG_DB", "save failed: " + err);
                return WorkLogPersistResult.Fail("업무기록 DB 저장 실패: " + err);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("WORKLOG_DB", "save exception: " + ex.Message);
                return WorkLogPersistResult.Fail("업무기록 DB 저장 실패: " + ex.Message);
            }
        }

        public async Task<WorkLogPersistResult> DeleteAsync(long dbLogId)
        {
            if (dbLogId <= 0)
                return WorkLogPersistResult.Ok(0);
            if (!_workLogBiz.IsConfigured)
                return WorkLogPersistResult.Fail("업무기록 DB가 설정되지 않아 삭제할 수 없습니다.");

            bool ok = await _workLogBiz.DeleteAsync(dbLogId).ConfigureAwait(true);
            if (!ok)
                return WorkLogPersistResult.Fail(_workLogBiz.LastConnectionError ?? "삭제에 실패했습니다.");
            return WorkLogPersistResult.Ok(dbLogId);
        }

        public Task<System.Collections.Generic.ISet<int>> GetRegisteredChangesetIdsAsync()
        {
            return _workLogBiz.GetRegisteredChangesetIdsAsync();
        }
    }

    public sealed class WorkLogPersistResult
    {
        public bool Success { get; private set; }
        public long LogId { get; private set; }
        public string Error { get; private set; }

        public static WorkLogPersistResult Ok(long logId)
        {
            return new WorkLogPersistResult { Success = true, LogId = logId };
        }

        public static WorkLogPersistResult Fail(string error)
        {
            return new WorkLogPersistResult { Success = false, Error = error ?? string.Empty };
        }
    }
}
