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
        Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsAsync(string remoteIp, int take);
    }
}
