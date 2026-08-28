using System;
using GBCWorkHub.DTO.Enums;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 원격 PC 정보
    /// </summary>
    public class RemotePcDto
    {
        public string HospitalCode { get; set; }
        public string HospitalName { get; set; }
        public string Environment { get; set; }
        public string PcName { get; set; }
        /// <summary>점유/공유 키 (사이트에 따라 IP 또는 PC명).</summary>
        public string IpAddress { get; set; }
        /// <summary>접속용 Host (정규화된 IP/호스트). 필터 칩에 우선 사용.</summary>
        public string HostAddress { get; set; }
        public int RdpPort { get; set; } = 3389;
        public Enums.RemotePcStatus Status { get; set; } = Enums.RemotePcStatus.Unknown;
        public string CurrentUser { get; set; }
        public DateTime? SessionStartTime { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        public string Remark { get; set; }

        /// <summary>중앙 DB 공유 상태 (UI 표시)</summary>
        public string SharedStatusText { get; set; }
        public string SharedUserAccount { get; set; }
        public string SharedClientPc { get; set; }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PcName))
                    return IpAddress;
                return string.Format("{0} ({1})", PcName, IpAddress);
            }
        }
    }
}
