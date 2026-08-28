using System;
using GBCWorkHub.DTO.Enums;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 원격 접속/상태 확인 결과
    /// </summary>
    public class RemoteConnectionResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Enums.RemotePcStatus Status { get; set; }
        public long? RoundtripTimeMs { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.Now;
    }
}
