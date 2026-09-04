using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    /// <summary>
    /// 원격 사이트 / PC 데이터 (App.config 기반)
    /// 사이트별: {SITE}.Host, {SITE}.PcNames
        /// PC별(선택): {SITE}.{PcName}.Host, {SITE}.{PcName}.Group (소속은 PCMAP.TEAM_NM이 우선)
    /// </summary>
    public class RemotePcDac
    {
        private static readonly string[] SiteCodes = { "AURORA", "CMC", "RC", "MNGHA" };

        public List<RemoteSiteDto> GetSiteList()
        {
            var list = new List<RemoteSiteDto>();
            foreach (var code in SiteCodes)
            {
                list.Add(new RemoteSiteDto
                {
                    SiteCode = code,
                    SiteName = code,
                    IsEnabled = HasSiteConfig(code)
                });
            }
            return list;
        }

        public List<RemotePcDto> GetRemotePcListBySite(string siteCode)
        {
            var list = new List<RemotePcDto>();
            if (string.IsNullOrWhiteSpace(siteCode))
                return list;

            string site = siteCode.Trim().ToUpperInvariant();
            if (!HasSiteConfig(site))
                return list;

            string siteHost = NormalizeHost(ConfigurationManager.AppSettings[site + ".Host"]);
            var pcNames = GetPcNames(site);

            // PcNames 없으면 Host IP를 PC명으로 1대 등록
            if (pcNames.Count == 0 && !string.IsNullOrWhiteSpace(siteHost))
                pcNames.Add(siteHost);

            foreach (var pcName in pcNames)
            {
                string pcHost = NormalizeHost(ConfigurationManager.AppSettings[site + "." + pcName + ".Host"]);
                string host = !string.IsNullOrWhiteSpace(pcHost) ? pcHost : siteHost;

                // RC/CMC: PC명을 DB 점유 키로 사용 (MSDWHTKD.REMOTE_ACCS_IP_ADDR 에 동일 값)
                // CMC.Host 는 PMP URL이라 점유 키로 쓰지 않음
                string shareKey = host;
                if (string.Equals(site, "RC", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(site, "CMC", StringComparison.OrdinalIgnoreCase))
                {
                    shareKey = pcName;
                }
                else if (string.IsNullOrWhiteSpace(shareKey))
                {
                    shareKey = pcName;
                }

                string group = (ConfigurationManager.AppSettings[site + "." + pcName + ".Group"] ?? string.Empty).Trim();

                list.Add(new RemotePcDto
                {
                    HospitalCode = site,
                    HospitalName = site,
                    Environment = "REMOTE",
                    PcName = pcName,
                    IpAddress = shareKey,
                    HostAddress = host,
                    GroupName = group,
                    RdpPort = 3389,
                    Remark = site
                });
            }

            list.Sort((a, b) =>
            {
                int g = PcNameNaturalSort.CompareGroup(
                    a != null ? a.GroupName : null,
                    b != null ? b.GroupName : null);
                if (g != 0)
                    return g;
                string an = a != null && !string.IsNullOrWhiteSpace(a.PcName) ? a.PcName.Trim() : string.Empty;
                string bn = b != null && !string.IsNullOrWhiteSpace(b.PcName) ? b.PcName.Trim() : string.Empty;
                return PcNameNaturalSort.Compare(an, bn);
            });

            return list;
        }

        private static bool HasSiteConfig(string siteCode)
        {
            if (string.IsNullOrWhiteSpace(siteCode))
                return false;

            string site = siteCode.Trim().ToUpperInvariant();

            // CMC: Host에 전체 URL을 넣을 수 있음 (Normalize 전 raw)
            string rawHost = (ConfigurationManager.AppSettings[site + ".Host"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(rawHost))
                return true;

            string host = NormalizeHost(rawHost);
            if (!string.IsNullOrWhiteSpace(host))
                return true;

            // RC/CMC: Host 없이도 PcNames만 있으면 타일 활성
            if ((string.Equals(site, "RC", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(site, "CMC", StringComparison.OrdinalIgnoreCase))
                && GetPcNames(site).Count > 0)
                return true;

            return false;
        }

        private static List<string> GetPcNames(string site)
        {
            string pcNamesRaw = ConfigurationManager.AppSettings[site + ".PcNames"] ?? string.Empty;
            return pcNamesRaw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }

        /// <summary>
        /// https://172.16.49.61/ → 172.16.49.61
        /// </summary>
        private static string NormalizeHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string host = value.Trim();
            if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("https://".Length);
            else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("http://".Length);

            int slash = host.IndexOf('/');
            if (slash >= 0)
                host = host.Substring(0, slash);

            int colon = host.IndexOf(':');
            if (colon >= 0)
                host = host.Substring(0, colon);

            return host.Trim();
        }
    }
}
