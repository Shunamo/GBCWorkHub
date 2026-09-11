using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// XSUP.MSDWHTKD 중앙 상태 공유
    /// </summary>
    public class RemotePcShareBiz
    {
        private static readonly object PurgeSync = new object();
        private static DateTime _lastPurgeUtc = DateTime.MinValue;

        private readonly IRemotePcRepository _repository;

        public RemotePcShareBiz()
            : this(new OracleRemotePcRepository())
        {
        }

        public RemotePcShareBiz(IRemotePcRepository repository)
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

        public static string LocalWindowsAccount
        {
            get { return Environment.UserDomainName + "\\" + Environment.UserName; }
        }

        /// <summary>점유명. 첫 실행에 저장한 이름이 있으면 그것을, 없으면 Windows 로그인 계정.</summary>
        public static string LocalUserAccount
        {
            get
            {
                string occupancy = OccupancyNameStore.TryGet();
                if (!string.IsNullOrWhiteSpace(occupancy))
                    return occupancy;
                return LocalWindowsAccount;
            }
        }

        public static string LocalClientPc
        {
            get { return Environment.MachineName; }
        }

        public static string LocalAccessIp
        {
            get { return LocalNetworkHelper.GetLocalIPv4Address(); }
        }

        public static string NewSessionToken()
        {
            return Guid.NewGuid().ToString("N");
        }

        public Task<bool> TestConnectionAsync()
        {
            return _repository.TestConnectionAsync();
        }

        public Task<IList<RemotePcStatus>> GetAllAsync()
        {
            return _repository.GetAllAsync();
        }

        public Task<DateTime?> GetMaxUpdatedAtAsync()
        {
            return _repository.GetMaxUpdatedAtAsync();
        }

        public Task<RemotePcStatus> GetByRemoteIpAsync(string remoteIp)
        {
            return _repository.GetByRemoteIpAsync(remoteIp);
        }

        public Task<bool> TryReserveAsync(string remoteIp, string sessionToken)
        {
            return TryReserveAsync(remoteIp, sessionToken, null);
        }

        public Task<bool> TryReserveAsync(string remoteIp, string sessionToken, string remotePcName)
        {
            return TryReserveAsync(remoteIp, sessionToken, remotePcName, false, null, null);
        }

        public Task<bool> TryReserveAsync(
            string remoteIp,
            string sessionToken,
            string remotePcName,
            bool forceTakeover,
            string affiliation,
            string siteCode)
        {
            return _repository.TryReserveAsync(new RemotePcReserveParams
            {
                RemoteAccessIpAddress = remoteIp,
                RemotePcName = remotePcName,
                AccessIpAddress = LocalAccessIp,
                AccessUserId = LocalUserAccount,
                AccessPcName = LocalClientPc,
                SessionToken = sessionToken,
                ForceTakeover = forceTakeover,
                TakeoverNotice = forceTakeover
                    ? OccupancyTakeoverMessage.BuildNotice(affiliation, LocalUserAccount, siteCode, remotePcName)
                    : null
            });
        }

        public Task<RemotePcUsageLogDto> GetUsageLogByTokenAsync(string sessionToken)
        {
            return _repository.GetUsageLogByTokenAsync(sessionToken);
        }

        public Task<bool> ConfirmConnectionAsync(string remoteIp, string sessionToken)
        {
            return _repository.ConfirmConnectionAsync(remoteIp, sessionToken);
        }

        public Task<bool> ReleaseAsync(string remoteIp, string sessionToken)
        {
            return ReleaseAsync(remoteIp, sessionToken, "RELEASE");
        }

        public Task<bool> ReleaseAsync(string remoteIp, string sessionToken, string endSource)
        {
            return _repository.ReleaseAsync(remoteIp, sessionToken, endSource);
        }

        /// <summary>관리자: 점유 강제 해제 (세션 토큰 필요).</summary>
        public async Task<string> ForceReleaseForAdminAsync(string remoteIp, string sessionToken)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (string.IsNullOrWhiteSpace(remoteIp))
                return "원격 PC를 지정해 주세요.";
            if (string.IsNullOrWhiteSpace(sessionToken))
                return "세션 토큰이 없어 해제할 수 없습니다.";

            bool ok = await ReleaseAsync(remoteIp.Trim(), sessionToken.Trim(), "ADMIN_FORCE")
                .ConfigureAwait(false);
            return ok ? null : "점유 해제에 실패했습니다. 이미 해제됐거나 상태가 바뀌었습니다.";
        }

        public Task<bool> MarkCheckRequiredAsync(string remoteIp, string sessionToken)
        {
            return _repository.MarkCheckRequiredAsync(remoteIp, sessionToken);
        }

        public Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsAsync(string remoteIp, int take)
        {
            return _repository.GetRecentUsageLogsAsync(remoteIp, take);
        }

        public Task<IList<RemotePcUsageLogDto>> GetMyRecentSessionsAsync(int take)
        {
            return GetMyRecentSessionsAsync(take, null, null);
        }

        public Task<IList<RemotePcUsageLogDto>> GetMyRecentSessionsAsync(int take, DateTime? fromAt, DateTime? toAt)
        {
            int fetch = take <= 0 ? 20 : take;
            if (fetch > 200)
                fetch = 200;

            // Logged-in: query by app login id only (not Windows account / machine name).
            string occupancy = OccupancyNameStore.TryGet();
            if (!string.IsNullOrWhiteSpace(occupancy))
            {
                return _repository.GetRecentUsageLogsForOccupantAsync(
                    occupancy,
                    null,
                    null,
                    null,
                    fetch,
                    fromAt,
                    toAt);
            }

            return _repository.GetRecentUsageLogsForOccupantAsync(
                null,
                LocalWindowsAccount,
                Environment.UserName,
                LocalClientPc,
                fetch,
                fromAt,
                toAt);
        }

        public async Task<IList<RemotePcUsageLogDto>> GetMyRecentEndedSessionsAsync(int take)
        {
            int fetch = take <= 0 ? 20 : take;
            if (fetch > 50)
                fetch = 50;
            var all = await GetMyRecentSessionsAsync(Math.Min(50, fetch * 2)).ConfigureAwait(false);
            var mine = new List<RemotePcUsageLogDto>();
            if (all == null)
                return mine;
            foreach (var log in all)
            {
                if (log == null || !log.RequestedAt.HasValue || !log.EndedAt.HasValue)
                    continue;
                if (!OccupancyNameStore.IsLocalOccupant(log.AccessUserId, log.AccessPcName))
                    continue;
                mine.Add(log);
                if (mine.Count >= fetch)
                    break;
            }
            return mine;
        }

        public Task<int> RenameOccupantAsync(string oldName, string newName)
        {
            return _repository.RenameOccupantAsync(oldName, newName, LocalClientPc);
        }

        /// <summary>접속 이력 한 달 창. 앱에서 하루 한 번, 또는 DBA 월간 배치로 정리.</summary>
        public Task<int> PurgeOldUsageLogsAsync()
        {
            lock (PurgeSync)
            {
                if (_lastPurgeUtc != DateTime.MinValue
                    && (DateTime.UtcNow - _lastPurgeUtc).TotalHours < 12)
                    return Task.FromResult(0);
                _lastPurgeUtc = DateTime.UtcNow;
            }
            return _repository.PurgeUsageLogsOlderThanMonthsAsync(1);
        }

        /// <summary>관리자: 전체 접속 이력 조회.</summary>
        public Task<IList<RemotePcUsageLogDto>> GetUsageLogsForAdminAsync(string search, int take)
        {
            if (!OccupancyNameStore.IsAdmin)
                return Task.FromResult((IList<RemotePcUsageLogDto>)new List<RemotePcUsageLogDto>());
            return _repository.GetUsageLogsForAdminAsync(search, take);
        }

        /// <summary>관리자: 접속 이력 수정.</summary>
        public async Task<string> UpdateUsageLogForAdminAsync(
            long logId,
            string accessUserId,
            string sessionStatus,
            DateTime? endedAt,
            string resultMessage)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (logId <= 0)
                return "이력을 지정해 주세요.";

            bool ok = await _repository.UpdateUsageLogForAdminAsync(
                    logId, accessUserId, sessionStatus, endedAt, resultMessage)
                .ConfigureAwait(false);
            return ok ? null : "접속 이력 수정에 실패했습니다.";
        }

        /// <summary>관리자: 접속 이력 삭제.</summary>
        public async Task<string> DeleteUsageLogForAdminAsync(long logId)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (logId <= 0)
                return "이력을 지정해 주세요.";

            bool ok = await _repository.DeleteUsageLogForAdminAsync(logId).ConfigureAwait(false);
            return ok ? null : "접속 이력 삭제에 실패했습니다.";
        }
    }
}
