namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 중앙 DB CURRENT_STATUS / SESSION_STATUS 상수
    /// </summary>
    public static class RemotePcDbStatuses
    {
        public const string Available = "AVAILABLE";
        public const string Connecting = "CONNECTING";
        public const string InUse = "IN_USE";
        public const string CheckRequired = "CHECK_REQUIRED";

        public const string SessionConnecting = "CONNECTING";
        public const string SessionInUse = "IN_USE";
        public const string SessionEnded = "ENDED";
        public const string SessionCancelled = "CANCELLED";
        public const string SessionFailed = "FAILED";
        public const string SessionCheckRequired = "CHECK_REQUIRED";
    }
}
