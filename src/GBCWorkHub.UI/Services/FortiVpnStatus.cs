using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// Forti SSL VPN 연결 여부. Fortinet SSL VPN 어댑터가 Up이면 연결로 본다.
    /// </summary>
    public static class FortiVpnStatus
    {
        public static bool IsConnected()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Any(IsFortiSslVpnUp);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFortiSslVpnUp(NetworkInterface nic)
        {
            if (nic == null)
                return false;

            if (nic.OperationalStatus != OperationalStatus.Up)
                return false;

            string desc = nic.Description ?? string.Empty;
            string name = nic.Name ?? string.Empty;

            // 우선: SSL VPN 가상 어댑터
            if (desc.IndexOf("Fortinet SSL VPN", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (desc.IndexOf("FortiSSLVPN", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 보조: 이름/설명에 Forti + VPN
            bool forti = desc.IndexOf("Fortinet", StringComparison.OrdinalIgnoreCase) >= 0
                || desc.IndexOf("Forti", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Forti", StringComparison.OrdinalIgnoreCase) >= 0;
            bool vpn = desc.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0;

            return forti && vpn;
        }
    }
}
