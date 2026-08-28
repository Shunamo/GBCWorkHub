using System;
using GBCWorkHub.BIZ;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// WorkHub 작성/수정 권한 식별자. 개인명이 아니라 이 PC의 로컬 IPv4.
    /// </summary>
    public static class WorkHubUserProfile
    {
        /// <summary>현재 로컬 PC IPv4 (소유권·작성자 키).</summary>
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

        /// <summary>하위 호환: 작성자 키(로컬 IP).</summary>
        public static string DisplayName
        {
            get { return LocalIp; }
        }

        public static bool HasDisplayName
        {
            get { return HasLocalIp; }
        }

        public static bool Matches(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !HasLocalIp)
                return false;
            return string.Equals(candidate.Trim(), LocalIp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
