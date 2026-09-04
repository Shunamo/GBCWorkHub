using System;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// XSUP.MSDWHTKD 행 모델
    /// </summary>
    public class RemotePcStatus
    {
        public string AccessIpAddress { get; set; }
        public string AccessStatusCode { get; set; }
        public string RemoteAccessIpAddress { get; set; }
        public DateTime? RemoteAccessDateTime { get; set; }
        public string RemotePcName { get; set; }
        /// <summary>사이트 코드 (AURORA / RC 등).</summary>
        public string SiteCode { get; set; }
        public string AccessUserId { get; set; }
        public string AccessPcName { get; set; }
        public string SessionToken { get; set; }
        public DateTime? AccessStartDateTime { get; set; }
        public DateTime? LastHeartbeatDateTime { get; set; }
        public DateTime? UpdatedDateTime { get; set; }

        public string DisplayStatus
        {
            get { return RemotePcStatusMapper.ToDisplayStatus(AccessStatusCode); }
        }
    }

    public static class RemotePcStatusMapper
    {
        public static string ToDisplayStatus(string code)
        {
            if (string.Equals(code, RemotePcDbStatuses.Available, StringComparison.OrdinalIgnoreCase))
                return "사용 가능";
            if (string.Equals(code, RemotePcDbStatuses.Connecting, StringComparison.OrdinalIgnoreCase))
                return "접속 시도 중";
            if (string.Equals(code, RemotePcDbStatuses.InUse, StringComparison.OrdinalIgnoreCase))
                return "사용 중";
            if (string.Equals(code, RemotePcDbStatuses.CheckRequired, StringComparison.OrdinalIgnoreCase))
                return "TFS 내역 확인 중";
            return string.IsNullOrEmpty(code) ? "-" : code;
        }

        public static bool SameKey(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 점유 행이 이 갤러리 PC의 것인지. 이름·점유키가 다른 PC를 가리키면 false.
        /// </summary>
        public static bool StatusFitsPc(RemotePcStatus status, string pcName, string shareKey, string hostAddress)
        {
            if (status == null)
                return false;

            string occName = status.RemotePcName;
            string occKey = status.RemoteAccessIpAddress;

            if (SameKey(occName, pcName) || SameKey(occKey, pcName))
                return true;

            if (SameKey(occKey, shareKey)
                && (string.IsNullOrWhiteSpace(occName) || SameKey(occName, pcName)))
                return true;

            if (SameKey(occKey, hostAddress) && SameKey(occName, pcName))
                return true;

            return false;
        }
    }
}
