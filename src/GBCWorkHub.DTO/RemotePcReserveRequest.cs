namespace GBCWorkHub.DTO
{
    public class RemotePcReserveRequest
    {
        public string ComputerName { get; set; }
        public string SessionToken { get; set; }
        public string UserAccount { get; set; }
        public string ClientPc { get; set; }
    }

    public class RemotePcReserveResult
    {
        public bool Success { get; set; }
        public int RowsAffected { get; set; }
        public string Message { get; set; }
        public string PreviousDbStatus { get; set; }
        public string NewDbStatus { get; set; }
        public RemotePcShareDto Current { get; set; }
        public bool DbUnavailable { get; set; }
    }

    public class RemotePcDbWriteResult
    {
        public bool Success { get; set; }
        public int RowsAffected { get; set; }
        public string Message { get; set; }
        public string PreviousDbStatus { get; set; }
        public string NewDbStatus { get; set; }
        public bool DbUnavailable { get; set; }
    }
}
