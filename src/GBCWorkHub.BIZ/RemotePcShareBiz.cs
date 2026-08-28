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

        public static string LocalUserAccount
        {
            get { return Environment.UserDomainName + "\\" + Environment.UserName; }
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
            return _repository.TryReserveAsync(new RemotePcReserveParams
            {
                RemoteAccessIpAddress = remoteIp,
                RemotePcName = remotePcName,
                AccessIpAddress = LocalAccessIp,
                AccessUserId = LocalUserAccount,
                AccessPcName = LocalClientPc,
                SessionToken = sessionToken
            });
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

        public Task<bool> MarkCheckRequiredAsync(string remoteIp, string sessionToken)
        {
            return _repository.MarkCheckRequiredAsync(remoteIp, sessionToken);
        }

        public Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsAsync(string remoteIp, int take)
        {
            return _repository.GetRecentUsageLogsAsync(remoteIp, take);
        }
    }
}
