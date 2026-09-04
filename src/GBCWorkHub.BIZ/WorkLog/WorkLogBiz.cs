using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ.WorkLog
{
    public class WorkLogBiz
    {
        private readonly IWorkLogRepository _repository;

        public WorkLogBiz()
            : this(new OracleWorkLogRepository())
        {
        }

        public WorkLogBiz(IWorkLogRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException("repository");
        }

        public bool IsConfigured
        {
            get { return _repository.IsConfigured; }
        }

        public bool LastConnectionOk
        {
            get { return _repository.LastConnectionOk; }
        }

        public string LastConnectionError
        {
            get { return _repository.LastConnectionError; }
        }

        public Task<IList<WorkLogRecordDto>> GetAllAsync()
        {
            return _repository.GetAllAsync();
        }

        public Task<WorkLogPageResult> GetPageAsync(WorkLogListQuery query)
        {
            return _repository.GetPageAsync(query);
        }

        public Task<WorkLogRecordDto> GetByIdAsync(long logId)
        {
            return _repository.GetByIdAsync(logId);
        }

        public Task<bool> SaveAsync(WorkLogRecordDto record)
        {
            return _repository.SaveAsync(record);
        }

        public Task<bool> DeleteAsync(long logId)
        {
            return _repository.DeleteByIdAsync(logId);
        }

        public Task<int> DeleteAllAsync()
        {
            return _repository.DeleteAllAsync();
        }

        /// <summary>업무기록에 등록된 Changeset ID (오늘 폴백 미등록 필터용).</summary>
        public Task<ISet<int>> GetRegisteredChangesetIdsAsync()
        {
            return _repository.GetRegisteredChangesetIdsAsync();
        }

        public Task<int> RenameAuthorAsync(string oldName, string newName, string localPcIp)
        {
            return _repository.RenameAuthorAsync(oldName, newName, localPcIp);
        }

        public Task<int> RenameTeamAsync(string authorName, string teamName, string localPcIp)
        {
            return _repository.RenameTeamAsync(authorName, teamName, localPcIp);
        }

        public Task<int> FillMissingTeamAsync(string authorName, string teamName, string localPcIp)
        {
            return _repository.FillMissingTeamAsync(authorName, teamName, localPcIp);
        }

        public Task<IList<string>> GetDistinctTeamNamesAsync()
        {
            return _repository.GetDistinctTeamNamesAsync();
        }

        /// <summary>엑셀 중복 스킵: 헤더+PRJ+SRC 내용이 전부 같은 건의 fingerprint.</summary>
        public async Task<ISet<string>> GetContentDedupKeysAsync()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var all = await GetAllAsync().ConfigureAwait(false);
            if (all == null)
                return set;
            foreach (var r in all)
            {
                string key = WorkLogContentDedup.BuildKey(r);
                if (!string.IsNullOrEmpty(key))
                    set.Add(key);
            }
            return set;
        }
    }
}
