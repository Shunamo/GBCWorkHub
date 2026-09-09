using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    public interface IRemotePcRepository
    {
        bool IsConfigured { get; }
        bool LastConnectionOk { get; }
        string LastConnectionError { get; }

        Task<bool> TestConnectionAsync();
        Task<IList<RemotePcStatus>> GetAllAsync();
        Task<RemotePcStatus> GetByRemoteIpAsync(string remoteIp);
        Task<bool> TryReserveAsync(RemotePcReserveParams parameters);
        Task<bool> ConfirmConnectionAsync(string remoteIp, string sessionToken);
        Task<bool> ReleaseAsync(string remoteIp, string sessionToken);
        Task<bool> ReleaseAsync(string remoteIp, string sessionToken, string endSource);
        Task<bool> MarkCheckRequiredAsync(string remoteIp, string sessionToken);
        Task<RemotePcUsageLogDto> GetUsageLogByTokenAsync(string sessionToken);
        Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsAsync(string remoteIp, int take);
        Task<IList<RemotePcUsageLogDto>> GetRecentEndedUsageLogsAsync(int take);
        Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsForOccupantAsync(
            string occupancyName,
            string windowsAccount,
            string samAccount,
            string accessPcName,
            int take,
            DateTime? fromAt = null,
            DateTime? toAt = null);
        /// <summary>점유명 변경 시 이 PC의 현재 점유·접속 이력 ACCS_USER_ID 를 새 이름으로 고친다.</summary>
        Task<int> RenameOccupantAsync(string oldName, string newName, string accessPcName);
        /// <summary>한 달보다 오래된 종료·취소 접속 이력을 지운다. 진행 중 세션은 남긴다.</summary>
        Task<int> PurgeUsageLogsOlderThanMonthsAsync(int months);

        /// <summary>관리자: 전체 접속 이력 조회 (선택 검색어).</summary>
        Task<IList<RemotePcUsageLogDto>> GetUsageLogsForAdminAsync(string search, int take);

        /// <summary>관리자: 접속 이력 수정 (접속자/상태/종료시각/메시지).</summary>
        Task<bool> UpdateUsageLogForAdminAsync(
            long logId,
            string accessUserId,
            string sessionStatus,
            DateTime? endedAt,
            string resultMessage);

        /// <summary>관리자: 접속 이력 1건 삭제.</summary>
        Task<bool> DeleteUsageLogForAdminAsync(long logId);
    }
}
