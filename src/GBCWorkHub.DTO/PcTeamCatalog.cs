using System;
using System.Collections.Generic;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// vpn.xlsx / sql/18 소속. DB TEAM_NM이 비어도 PC명·IP로 찾는다.
    /// 하이픈 없는 COMPUTERNAME(HOBCARE07)도 맞춘다.
    /// </summary>
    public static class PcTeamCatalog
    {
        private static readonly Dictionary<string, string> ByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static PcTeamCatalog()
        {
            Add("P-BCTECH8", "10.230.35.252", "진료지원");
            Add("P-BCTECH6", "10.230.35.250", "원무");
            Add("P-BCTECH1", "10.230.35.245", "진료지원");
            Add("P-BCTECH2", "10.230.35.246", "진료지원");
            Add("P-BCTECH3", "10.230.35.247", "원무");
            Add("P-BCTECH5", "10.230.35.249", "진료간호");
            Add("P-BCTECH9", "10.230.35.253", "진료간호");
            Add("P-BCTECH7", "10.230.35.251", "진료간호");
            Add("P-BCTECH4", "10.230.35.248", "ETC");
            Add("HO-BCARE-11", "10.80.26.52", "진료지원");
            Add("HO-01-HIS-11", "10.80.25.66", "진료지원");
            Add("HO-BCARE-07", "10.80.22.73", "진료지원");
            Add("HO-BESTCARE-06", "10.80.25.64", "진료지원");
            Add("HO-BCARE-12", "10.80.25.73", "ETC");
            Add("JM-HIS-RCJ-10", "10.81.10.107", "원무");
            Add("JM-HIS-RCJ-01", "10.81.10.104", "원무");
            Add("HO-BCARE-15", "10.80.25.80", "ETC");
            Add("HO-01-DTT-56", "10.80.25.77", "원무");
            Add("HO-BCARE-10", "10.80.26.54", "진료간호");
            Add("HO-BESTCARE-01", "10.80.26.58", "진료간호");
            Add("HO-01-DTT-03", "10.80.25.81", "진료간호");
            Add("HO-BESTCARE-07", "10.80.26.61", "진료간호");
            Add("HO-BCARE-14", "10.80.14.88", "ETC");
            Add("HO-BESTCARE-02", "10.80.22.59", "ETC");
            Add("HO-BCARE-09", "10.80.26.63", "진료지원");
            Add("HOS-ITD-HIS-02", "10.80.25.60", "진료간호");
            Add("HO-BESTCARE-04", "10.80.26.56", "ETC");
            Add("HO-01-HIS-95", "10.80.25.67", "ETC");
            Add("HO-01-TRN-03", "10.80.22.86", "ETC");
            Add("HO-BCARE-13", "10.80.25.75", "원무");
            Add("JM-HIS-RCJ-25", "10.81.10.94", "ETC");
            Add("HO-BESTCARE-05", "10.80.25.70", "ETC");
            Add("HO-DTT-05", "10.80.25.68", "ETC");
            Add("HO-BCARE-01", "10.80.25.79", "ETC");
            Add("HO-01-DTT-04", "10.80.25.56", "ETC");
            Add("KEB-3TYYVP2", "172.16.49.57", "진료간호");
            Add("KEB-DRY98V3", "172.16.49.56", "진료지원");
            Add("KEB-BC-8TSD0F2", "172.16.49.63", "원무");
            Add("KEB-3VNZVP2", "172.16.49.59", "진료지원");
            Add("KEB-7VY98V3", "172.16.49.61", "진료지원");
            Add("KEB-84Y73Q3", "172.16.49.50", "원무");
            Add("KEB-CRY98V3", "172.16.49.55", "원무");
            Add("KEB-71V73Q3", "172.16.49.53", "진료간호");
            Add("KEB-505F8R2", "172.16.49.58", "진료간호");
            Add("KEB-BC-HTSD0F2", "172.16.49.66", "ETC");
            Add("KEB-HYT73Q3", "172.16.49.52", "ETC");
            Add("KEB-7ZT73Q3", "172.16.49.54", "ETC");
            Add("KEB-B2V73Q3", "172.16.49.51", "ETC");
            Add("KEB-BC-6TSD0F2", "172.16.49.65", "ETC");
            Add("KEB-BC-CSSD0F1", "172.16.49.62", "ETC");
        }

        public static string Find(string pcOrIp)
        {
            if (string.IsNullOrWhiteSpace(pcOrIp))
                return null;
            string raw = pcOrIp.Trim();
            string team;
            if (ByKey.TryGetValue(raw, out team))
                return team;
            string compact = Compact(raw);
            if (compact.Length > 0 && ByKey.TryGetValue(compact, out team))
                return team;
            return null;
        }

        private static void Add(string pcName, string ip, string team)
        {
            ByKey[pcName] = team;
            ByKey[Compact(pcName)] = team;
            if (!string.IsNullOrWhiteSpace(ip))
                ByKey[ip.Trim()] = team;
        }

        private static string Compact(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string s = value.Trim();
            int slash = s.LastIndexOf('\\');
            if (slash >= 0 && slash < s.Length - 1)
                s = s.Substring(slash + 1);
            if (IsIpv4(s))
                return s;
            int dot = s.IndexOf('.');
            if (dot > 0)
                s = s.Substring(0, dot);
            return s.Replace("-", string.Empty).Replace("_", string.Empty);
        }

        private static bool IsIpv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string[] parts = value.Split('.');
            if (parts.Length != 4)
                return false;
            for (int i = 0; i < 4; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0 || n > 255)
                    return false;
            }
            return true;
        }
    }
}
