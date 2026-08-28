using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// Excel/CSV 파일 파싱·중복 제거·DB 저장.
    /// OpenFileDialog / MessageBox / Items 반영은 ViewModel에 남긴다.
    /// </summary>
    public sealed class WorkLogImportService
    {
        private readonly WorkLogBiz _workLogBiz;

        public WorkLogImportService(WorkLogBiz workLogBiz)
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

        /// <summary>파일 파싱만 (UI 스레드 밖 Task.Run에서 호출).</summary>
        public static IList<WorkLogRecordDto> ParseFile(string path)
        {
            return WorkLogExcelImporter.ParseFile(path);
        }

        /// <summary>
        /// 파싱된 레코드를 DB에 저장. UI Items 반영용 SavedItems 반환.
        /// </summary>
        public async Task<WorkLogFileImportResult> SaveParsedRecordsAsync(
            IList<WorkLogRecordDto> records,
            string siteCd)
        {
            var result = new WorkLogFileImportResult();
            if (records == null || records.Count == 0)
                return result;

            if (!_workLogBiz.IsConfigured)
            {
                result.ErrorMessage = "DB가 설정되지 않아 저장할 수 없습니다.\nApp.config의 GbcWorkHubDb를 확인하세요.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(siteCd))
            {
                result.ErrorMessage =
                    "사이트를 선택한 뒤 가져와 주세요.\n필터에서 AURORA 또는 RC를 고르거나, 원격 사이트 화면에서 진입하세요.";
                return result;
            }

            var existingKeys = await _workLogBiz.GetContentDedupKeysAsync().ConfigureAwait(true)
                ?? new HashSet<string>(StringComparer.Ordinal);
            if (!(existingKeys is HashSet<string>))
                existingKeys = new HashSet<string>(existingKeys, StringComparer.Ordinal);

            foreach (var dto in records)
            {
                if (dto == null)
                    continue;
                if (string.IsNullOrWhiteSpace(dto.SiteCode))
                    dto.SiteCode = siteCd;

                string ticketNorm = WorkLogDraftMapper.NormalizeTicketStorage(dto.TicketNo);
                if (!string.IsNullOrWhiteSpace(ticketNorm))
                    dto.TicketNo = ticketNorm;

                string dedupKey = WorkLogContentDedup.BuildKey(dto);
                if (!string.IsNullOrEmpty(dedupKey) && existingKeys.Contains(dedupKey))
                {
                    result.SkipCount++;
                    continue;
                }

                bool saved = await _workLogBiz.SaveAsync(dto).ConfigureAwait(true);
                if (!saved)
                {
                    result.FailCount++;
                    continue;
                }

                result.OkCount++;
                if (!string.IsNullOrEmpty(dedupKey))
                    existingKeys.Add(dedupKey);

                var item = WorkLogDbMapper.FromDto(dto);
                if (item != null)
                    result.SavedItems.Add(item);
            }

            result.ParsedCount = records.Count;
            return result;
        }
    }

    public sealed class WorkLogFileImportResult
    {
        public WorkLogFileImportResult()
        {
            SavedItems = new List<WorkLogListItemViewModel>();
        }

        public int ParsedCount { get; set; }
        public int OkCount { get; set; }
        public int SkipCount { get; set; }
        public int FailCount { get; set; }
        public string ErrorMessage { get; set; }
        public List<WorkLogListItemViewModel> SavedItems { get; private set; }

        public bool HasBlockingError
        {
            get { return !string.IsNullOrWhiteSpace(ErrorMessage); }
        }
    }
}
