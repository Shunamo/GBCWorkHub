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
        /// <summary>이미 사용 중인 PC를 강제로 가져갈지.</summary>
        public bool ForceTakeover { get; set; }
        /// <summary>뺏긴 쪽에 남길 알림. MSDWHTKH.RESULT_MESSAGE.</summary>
        public string TakeoverNotice { get; set; }
    }
}
