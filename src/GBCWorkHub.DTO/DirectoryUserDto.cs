namespace GBCWorkHub.DTO
{
    /// <summary>이 Windows PC의 점유명·소속. 로그인 계정이 아님.</summary>
    public sealed class DirectoryUserDto
    {
        public string UserName { get; set; }
        public string TeamName { get; set; }
        public string LocalPcName { get; set; }
        public string LocalPcIp { get; set; }
        public string WindowsAccount { get; set; }
    }
}
