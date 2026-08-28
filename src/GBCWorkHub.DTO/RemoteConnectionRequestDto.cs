namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 원격 접속 요청
    /// </summary>
    public class RemoteConnectionRequestDto
    {
        public string IpAddress { get; set; }
        public int Port { get; set; } = 3389;
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Domain { get; set; }
        public bool FullScreen { get; set; }
        public bool PromptForCredentials { get; set; } = true;
    }
}
