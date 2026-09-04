using System;
using GBCWorkHub.BIZ;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// 작성/수정 권한. 점유명이 있으면 그 이름, 옛 기록은 로컬 IP.
    /// </summary>
    public static class WorkHubUserProfile
    {
        public static string OccupancyName
        {
            get { return RemotePcShareBiz.LocalUserAccount; }
        }

        public static string LocalIp
        {
            get
            {
                string ip = RemotePcShareBiz.LocalAccessIp;
                return string.IsNullOrWhiteSpace(ip) ? string.Empty : ip.Trim();
            }
        }

        public static bool HasLocalIp
        {
            get { return !string.IsNullOrWhiteSpace(LocalIp); }
        }

        public static string DisplayName
        {
            get
            {
                string display = OccupancyNameStore.DisplayName;
                return string.IsNullOrWhiteSpace(display) ? OccupancyName : display;
            }
        }

        public static bool HasDisplayName
        {
            get { return !string.IsNullOrWhiteSpace(OccupancyName); }
        }

        public static string OccupancyInitial
        {
            get
            {
                if (!HasDisplayName)
                    return "나";
                return OccupancyName.Trim().Substring(0, 1);
            }
        }

        public static bool Matches(string candidate)
        {
            return MatchesIp(candidate);
        }

        public static bool MatchesIp(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !HasLocalIp)
                return false;
            return string.Equals(candidate.Trim(), LocalIp, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesOccupancyName(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            string raw = candidate.Trim();
            string occupancy = OccupancyNameStore.TryGet();
            if (!string.IsNullOrWhiteSpace(occupancy))
            {
                if (string.Equals(raw, occupancy, StringComparison.OrdinalIgnoreCase))
                    return true;
                string display = OccupancyNameStore.DisplayName;
                if (!string.IsNullOrWhiteSpace(display)
                    && string.Equals(raw, display, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (raw.StartsWith(occupancy + " ", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string windows = RemotePcShareBiz.LocalWindowsAccount;
            if (!string.IsNullOrWhiteSpace(windows)
                && string.Equals(raw, windows, StringComparison.OrdinalIgnoreCase))
                return true;

            string sam = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(sam)
                && string.Equals(raw, sam, StringComparison.OrdinalIgnoreCase))
                return true;

            int slash = raw.LastIndexOf('\\');
            if (slash >= 0 && slash < raw.Length - 1
                && !string.IsNullOrWhiteSpace(sam)
                && string.Equals(raw.Substring(slash + 1), sam, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// 점유명(또는 소속 포함 표시)·Windows 계정·이 PC IP 중 하나면 본인.
        /// TFS 작성자명이 점유명과 달라도 LocalPcIp가 이 PC이면 수정/삭제 가능.
        /// </summary>
        public static bool OwnsRecord(string authorName, string localPcIp)
        {
            if (MatchesOccupancyName(authorName))
                return true;
            return MatchesIp(localPcIp);
        }
    }
}
