using System;

namespace GBCWorkHub.DTO
{
    /// <summary>XSUP.MSDWHTKH 접속 이력 1건.</summary>
    public sealed class RemotePcUsageLogDto
    {
        public long LogId { get; set; }
        public string SessionToken { get; set; }
        public string RemoteAccessIpAddress { get; set; }
        public string RemotePcName { get; set; }
        public string AccessUserId { get; set; }
        public string AccessPcName { get; set; }
        public string AccessIpAddress { get; set; }
        public string SessionStatus { get; set; }
        public DateTime? RequestedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string EndSource { get; set; }
    }
}
