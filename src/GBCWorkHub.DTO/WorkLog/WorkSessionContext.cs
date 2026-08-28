using System;

namespace GBCWorkHub.DTO.WorkLog
{
    /// <summary>
    /// TFS Payload에 없는 현재 작업 세션 정보.
    /// </summary>
    public sealed class WorkSessionContext
    {
        public string RemoteIp { get; set; }
        public string RemoteComputerName { get; set; }
        /// <summary>WorkHub를 실행 중인 로컬 PC IPv4.</summary>
        public string ClientLocalIp { get; set; }
        public string CurrentUserId { get; set; }
        public string CurrentUserName { get; set; }
        public DateTime? SessionStartedAt { get; set; }
        public DateTime? SessionEndedAt { get; set; }

        public string ResolvePc()
        {
            if (!string.IsNullOrWhiteSpace(RemoteIp))
                return RemoteIp.Trim();
            if (!string.IsNullOrWhiteSpace(RemoteComputerName))
                return RemoteComputerName.Trim();
            return string.Empty;
        }

        public string ResolvePersonInCharge()
        {
            if (!string.IsNullOrWhiteSpace(CurrentUserName))
                return CurrentUserName.Trim();
            if (!string.IsNullOrWhiteSpace(CurrentUserId))
                return CurrentUserId.Trim();
            return string.Empty;
        }

        /// <summary>작성 PC(로컬) IP. 없으면 CurrentUserName 등.</summary>
        public string ResolveAuthorLocalIp()
        {
            if (!string.IsNullOrWhiteSpace(ClientLocalIp))
                return ClientLocalIp.Trim();
            return ResolvePersonInCharge();
        }
    }
}
