using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.DAC
{
    public interface IWorkLogRepository
    {
        bool IsConfigured { get; }
        bool LastConnectionOk { get; }
        string LastConnectionError { get; }

        Task<IList<WorkLogRecordDto>> GetAllAsync();
        /// <summary>필터 + OFFSET/FETCH 페이징. 자식(PRJ/SRC/CS)은 해당 페이지 LOG_ID만 로드.</summary>
        Task<WorkLogPageResult> GetPageAsync(WorkLogListQuery query);
        Task<WorkLogRecordDto> GetByIdAsync(long logId);
        /// <summary>INSERT 또는 UPDATE. 성공 시 record.LogId 채움.</summary>
        Task<bool> SaveAsync(WorkLogRecordDto record);
        /// <summary>단건 삭제 (SRC → PRJ → CS → WRK).</summary>
        Task<bool> DeleteByIdAsync(long logId);
        /// <summary>업무기록 전체 삭제 (SRC → PRJ → CS → WRK). 재적재용.</summary>
        Task<int> DeleteAllAsync();
        /// <summary>등록된 Changeset ID 전체 (미등록 폴백 필터용).</summary>
        Task<ISet<int>> GetRegisteredChangesetIdsAsync();
    }
}
