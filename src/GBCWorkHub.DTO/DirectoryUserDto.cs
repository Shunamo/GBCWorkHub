namespace GBCWorkHub.DTO
{
    /// <summary>사용자 마스터(MSDWHTKD_USR). 로그인 시 LOGIN_ID/PASSWORD_HASH 사용.</summary>
    public sealed class DirectoryUserDto
    {
        public long? UserId { get; set; }
        public string UserName { get; set; }
        public string TeamName { get; set; }
        public string LocalPcName { get; set; }
        public string LocalPcIp { get; set; }
        public string WindowsAccount { get; set; }
        public string LoginId { get; set; }
        public string PasswordHash { get; set; }
        public bool IsActive { get; set; }
        /// <summary>소프트 삭제 여부. FK로 참조하는 다른 테이블 기록이 있어 실제 DELETE는 하지 않는다.</summary>
        public bool IsDeleted { get; set; }

        public DirectoryUserDto()
        {
            IsActive = true;
        }
    }
}
