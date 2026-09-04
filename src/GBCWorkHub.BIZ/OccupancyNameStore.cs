using System;
using System.IO;
using Newtonsoft.Json;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// 첫 실행에 정한 점유명. 점유(ACCS_USER_ID)와 업무기록 작성자 표시에 쓴다.
    /// Windows 로그인 계정명과 별개다.
    /// </summary>
    public static class OccupancyNameStore
    {
        private sealed class FileDto
        {
            public string OccupancyName { get; set; }
            public string Affiliation { get; set; }
        }

        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub",
                    "occupancy-name.json");
            }
        }

        public static string TryGet()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                var dto = JsonConvert.DeserializeObject<FileDto>(File.ReadAllText(FilePath));
                if (dto == null || string.IsNullOrWhiteSpace(dto.OccupancyName))
                    return null;
                return dto.OccupancyName.Trim();
            }
            catch
            {
                return null;
            }
        }

        public static string TryGetAffiliation()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                var dto = JsonConvert.DeserializeObject<FileDto>(File.ReadAllText(FilePath));
                if (dto == null || string.IsNullOrWhiteSpace(dto.Affiliation))
                    return null;
                return dto.Affiliation.Trim();
            }
            catch
            {
                return null;
            }
        }

        public const string NameTeamSeparator = " · ";

        /// <summary>목록·검색용. 소속이 있으면 "김수현 · 진료지원".</summary>
        public static string FormatDisplayName(string name, string affiliation)
        {
            string n = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            string a = string.IsNullOrWhiteSpace(affiliation) ? null : affiliation.Trim();
            if (n == null)
                return a ?? string.Empty;
            if (a == null)
                return n;
            return n + NameTeamSeparator + a;
        }

        public static string DisplayName
        {
            get { return FormatDisplayName(TryGet(), TryGetAffiliation()); }
        }

        /// <summary>탭바·마이페이지 헤더. 이름만.</summary>
        public static string HeaderName
        {
            get
            {
                string n = TryGet();
                return string.IsNullOrWhiteSpace(n) ? string.Empty : n.Trim();
            }
        }

        public static bool HasName
        {
            get { return !string.IsNullOrWhiteSpace(TryGet()); }
        }

        public static bool HasAffiliation
        {
            get { return !string.IsNullOrWhiteSpace(TryGetAffiliation()); }
        }

        public static bool IsLocalOccupant(string storedUserId)
        {
            return IsLocalOccupant(storedUserId, null);
        }

        public static bool IsLocalOccupant(string storedUserId, string accessPcName)
        {
            if (!string.IsNullOrWhiteSpace(accessPcName)
                && string.Equals(accessPcName.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrWhiteSpace(storedUserId))
                return false;

            string raw = storedUserId.Trim();
            string occupancy = TryGet();
            if (!string.IsNullOrWhiteSpace(occupancy)
                && string.Equals(raw, occupancy, StringComparison.OrdinalIgnoreCase))
                return true;

            string windows = RemotePcShareBiz.LocalWindowsAccount;
            if (!string.IsNullOrWhiteSpace(windows)
                && string.Equals(raw, windows, StringComparison.OrdinalIgnoreCase))
                return true;

            string sam = Environment.UserName;
            if (string.IsNullOrWhiteSpace(sam))
                return false;
            if (string.Equals(raw, sam, StringComparison.OrdinalIgnoreCase))
                return true;

            int slash = raw.LastIndexOf('\\');
            if (slash >= 0 && slash < raw.Length - 1)
            {
                string domain = raw.Substring(0, slash);
                string user = raw.Substring(slash + 1);
                if (string.Equals(user, sam, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(domain, Environment.UserDomainName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 이 PC 점유(Windows 계정·점유명)면 점유명을, 다른 접속자면 저장값을 이름으로 보여 준다.
        /// 소속이 있으면 업무기록과 같이 "이름 · 소속".
        /// </summary>
        public static string ToDisplayName(string storedUserId)
        {
            return ToDisplayName(storedUserId, null);
        }

        public static string ToDisplayName(string storedUserId, string accessPcName)
        {
            if (string.IsNullOrWhiteSpace(storedUserId) && string.IsNullOrWhiteSpace(accessPcName))
                return "-";

            string name = ResolveNameOnly(storedUserId, accessPcName);
            if (string.IsNullOrWhiteSpace(name) || name == "-")
                return "-";

            if (name.IndexOf(NameTeamSeparator, StringComparison.Ordinal) >= 0)
                return name;

            return FormatDisplayName(name, ResolveAffiliation(storedUserId, accessPcName, name));
        }

        public static string ToNameOnly(string storedUserId, string accessPcName)
        {
            string name = ResolveNameOnly(storedUserId, accessPcName);
            return string.IsNullOrWhiteSpace(name) ? "-" : name;
        }

        public static string ToAffiliation(string storedUserId, string accessPcName)
        {
            string name = ResolveNameOnly(storedUserId, accessPcName);
            string team = ResolveAffiliation(storedUserId, accessPcName, name);
            return string.IsNullOrWhiteSpace(team) ? "-" : team;
        }

        private static string ResolveNameOnly(string storedUserId, string accessPcName)
        {
            if (IsLocalOccupant(storedUserId, accessPcName))
            {
                string occupancy = TryGet();
                if (!string.IsNullOrWhiteSpace(occupancy))
                    return occupancy.Trim();
            }

            return StripDomain(storedUserId);
        }

        private static string ResolveAffiliation(string storedUserId, string accessPcName, string resolvedName)
        {
            if (IsLocalOccupant(storedUserId, accessPcName))
            {
                string local = TryGetAffiliation();
                if (!string.IsNullOrWhiteSpace(local))
                    return local.Trim();
            }

            string fromDir = DirectoryBiz.LookupTeam(resolvedName, accessPcName);
            if (!string.IsNullOrWhiteSpace(fromDir))
                return fromDir;
            return DirectoryBiz.LookupTeam(storedUserId, accessPcName);
        }

        private static string StripDomain(string storedUserId)
        {
            if (string.IsNullOrWhiteSpace(storedUserId))
                return null;
            string raw = storedUserId.Trim();
            int slash = raw.LastIndexOf('\\');
            if (slash >= 0 && slash < raw.Length - 1)
                return raw.Substring(slash + 1);
            return raw;
        }

        public static void Save(string name)
        {
            Save(name, TryGetAffiliation());
        }

        public static void Save(string name, string affiliation)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("점유명이 비어 있습니다.", "name");

            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string team = string.IsNullOrWhiteSpace(affiliation) ? null : affiliation.Trim();
            if (team != null && team.Length > 100)
                team = team.Substring(0, 100);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(new FileDto
            {
                OccupancyName = name.Trim(),
                Affiliation = team
            }));
        }
    }
}
