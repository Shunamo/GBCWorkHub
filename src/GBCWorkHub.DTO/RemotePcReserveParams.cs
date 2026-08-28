namespace GBCWorkHub.DTO
{
    public class RemotePcReserveParams
    {
        public string RemoteAccessIpAddress { get; set; }
        public string RemotePcName { get; set; }
        public string AccessIpAddress { get; set; }
        public string AccessUserId { get; set; }
        public string AccessPcName { get; set; }
        public string SessionToken { get; set; }
    }
}
