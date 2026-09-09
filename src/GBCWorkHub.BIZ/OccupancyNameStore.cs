using System;
using System.IO;
using Newtonsoft.Json;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// 앱 로그인 점유명. 점유(ACCS_USER_ID)·업무기록 작성자 식별에 쓴다.
    /// 로그인 후에는 Windows 계정·로컬 PC명과 별개로 취급한다.
    /// </summary>
    public static class OccupancyNameStore
    {
        private sealed class FileDto
        {
            public string OccupancyName { get; set; }
            public string Affiliation { get; set; }
            public bool IsAdmin { get; set; }
            /// <summary>MSDWHTKD_USR.USR_ID. 로그인 세션의 불변 사용자 식별자 — 표시명/PC와 무관하게 유지된다.</summary>
            public long? UserId { get; set; }
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

        /// <summary>
        /// 현재 세션의 MSDWHTKD_USR.USR_ID. 로그인 시 저장되며, 표시명 변경/PC 변경과 무관하게 유지된다.
        /// 신규 기능(개선사항 요청 등)의 사용자 관계는 반드시 이 값을 FK로 사용하고, 이름/PC 매칭으로 재해석하지 않는다.
        /// </summary>
        public static long? TryGetUserId()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                var dto = JsonConvert.DeserializeObject<FileDto>(File.ReadAllText(FilePath));
                return dto == null ? null : dto.UserId;
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

        public static bool IsAdmin
        {
            get
            {
                // Never trust a stale IsAdmin flag alone — identity is the occupancy name.
                return IsAdminIdentity(TryGet());
            }
        }

        public static bool IsLocalOccupant(string storedUserId)
        {
            return IsLocalOccupant(storedUserId, null);
        }

        public static bool IsLocalOccupant(string storedUserId, string accessPcName)
        {
            // App login identity wins: same PC / Windows account must NOT count as "me"
            // when another login id is active on this machine.
            string occupancy = TryGet();
            if (!string.IsNullOrWhiteSpace(occupancy))
            {
                if (string.IsNullOrWhiteSpace(storedUserId))
                    return false;

                string raw = storedUserId.Trim();
                if (string.Equals(raw, occupancy, StringComparison.OrdinalIgnoreCase))
                    return true;

                string stripped = StripDomain(raw);
                if (!string.IsNullOrWhiteSpace(stripped)
                    && string.Equals(stripped, occupancy, StringComparison.OrdinalIgnoreCase))
                    return true;

                // ADMIN login id vs display name (관리자) — both mean the same account.
                if (IsAdminIdentity(occupancy) && IsAdminIdentity(raw))
                    return true;

                return false;
            }

            // Legacy (no app login): PC / Windows account heuristics.
            if (!string.IsNullOrWhiteSpace(accessPcName)
                && string.Equals(accessPcName.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrWhiteSpace(storedUserId))
                return false;

            string stored = storedUserId.Trim();
            string windows = RemotePcShareBiz.LocalWindowsAccount;
            if (!string.IsNullOrWhiteSpace(windows)
                && string.Equals(stored, windows, StringComparison.OrdinalIgnoreCase))
                return true;

            string sam = Environment.UserName;
            if (string.IsNullOrWhiteSpace(sam))
                return false;
            if (string.Equals(stored, sam, StringComparison.OrdinalIgnoreCase))
                return true;

            int slash = stored.LastIndexOf('\\');
            if (slash >= 0 && slash < stored.Length - 1)
            {
                string domain = stored.Substring(0, slash);
                string user = stored.Substring(slash + 1);
                if (string.Equals(user, sam, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(domain, Environment.UserDomainName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsAdminIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string v = StripDomain(value.Trim());
            return string.Equals(v, AuthBiz.AdminLoginId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, AuthBiz.AdminDisplayName, StringComparison.OrdinalIgnoreCase);
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
            Save(name, TryGetAffiliation(), IsAdminIdentity(name), TryGetUserId());
        }

        public static void Save(string name, string affiliation)
        {
            Save(name, affiliation, IsAdminIdentity(name), TryGetUserId());
        }

        /// <summary>기존 세션의 UserId를 보존한 채 표시명/소속/관리자 플래그만 갱신한다(이름 변경 등).</summary>
        public static void Save(string name, string affiliation, bool isAdmin)
        {
            Save(name, affiliation, isAdmin, TryGetUserId());
        }

        /// <summary>로그인 직후 등 실제 DB에서 확정된 USR_ID로 세션을 새로 쓸 때 사용한다.</summary>
        public static void Save(string name, string affiliation, bool isAdmin, long? userId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("점유명이 비어 있습니다.", "name");

            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string team = string.IsNullOrWhiteSpace(affiliation) ? null : affiliation.Trim();
            if (team != null && team.Length > 100)
                team = team.Substring(0, 100);

            // Flag follows identity only (김수현 + leftover admin flag must not open admin shell).
            bool admin = IsAdminIdentity(name) && isAdmin;
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(new FileDto
            {
                OccupancyName = name.Trim(),
                Affiliation = team,
                IsAdmin = admin,
                UserId = userId
            }));
        }

        /// <summary>로그아웃 시 로컬 점유명/소속/관리자 플래그를 지운다.</summary>
        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // 로그아웃은 실패해도 UI 세션을 막지 않는다.
            }
        }
    }
}
