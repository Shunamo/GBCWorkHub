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
        /// <summary>갤러리 그룹. 예: 진료지원, 진료간호. App.config {SITE}.{PcName}.Group</summary>
        public string GroupName { get; set; }
        /// <summary>원격 Windows 로그인(엑셀 ID / PC 도메인). 예: dxbcmc\bcarep.admin</summary>
        public string PcDomain { get; set; }
        /// <summary>엑셀 ID/Password 섹션([ID]/[Password]).</summary>
        public string PcNote { get; set; }
        /// <summary>접속 코멘트(사용자 수정 가능).</summary>
        public string PcComment { get; set; }
        /// <summary>AGENT 설치까지 완료된 PC — 목록/갤러리에서 아이콘 우측 상단 노란 점 배지로 표시.</summary>
        public bool AgentInstalled { get; set; }
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
