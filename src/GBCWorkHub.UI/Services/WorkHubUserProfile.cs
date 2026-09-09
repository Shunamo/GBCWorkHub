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
                if (raw.StartsWith(occupancy + OccupancyNameStore.NameTeamSeparator, StringComparison.OrdinalIgnoreCase))
                    return true;

                // ADMIN login id vs display name
                if (AuthBiz.IsAdminLoginId(occupancy)
                    || string.Equals(occupancy, AuthBiz.AdminDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    if (AuthBiz.IsAdminLoginId(raw)
                        || string.Equals(raw, AuthBiz.AdminDisplayName, StringComparison.OrdinalIgnoreCase)
                        || raw.StartsWith(AuthBiz.AdminDisplayName + " ", StringComparison.OrdinalIgnoreCase)
                        || raw.StartsWith(AuthBiz.AdminDisplayName + OccupancyNameStore.NameTeamSeparator, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Logged in: do not treat Windows account / same PC as ownership.
                return false;
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
        /// 앱 로그인(점유명)이 있으면 작성자명만으로 본인 판정.
        /// 로그인 전에는 점유명·로컬 IP(옛 기록)로 판정.
        /// </summary>
        public static bool OwnsRecord(string authorName, string localPcIp)
        {
            if (OccupancyNameStore.HasName)
                return MatchesOccupancyName(authorName);
            if (MatchesOccupancyName(authorName))
                return true;
            return MatchesIp(localPcIp);
        }
    }
}
