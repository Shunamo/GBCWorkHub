using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// 활성 사내 IPv4 주소 조회 (APIPA/루프백/IPv6 제외)
    /// </summary>
    public static class LocalNetworkHelper
    {
        public static string GetLocalIPv4Address()
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address)
                    .Where(IsUsableIPv4)
                    .ToList();

                if (candidates.Count == 0)
                    return null;

                // 사설 대역 우선
                var preferred = candidates.FirstOrDefault(a =>
                    a.ToString().StartsWith("10.", StringComparison.Ordinal)
                    || a.ToString().StartsWith("172.", StringComparison.Ordinal)
                    || a.ToString().StartsWith("192.168.", StringComparison.Ordinal));

                return (preferred ?? candidates[0]).ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUsableIPv4(IPAddress address)
        {
            if (address == null)
                return false;

            string text = address.ToString();
            if (string.IsNullOrEmpty(text))
                return false;
            if (IPAddress.IsLoopback(address))
                return false;
            if (text.StartsWith("169.254.", StringComparison.Ordinal))
                return false;
            return true;
        }
    }
}
