using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;

namespace GBCWorkHub.BIZ
{
    /// <summary>점유명 디렉터리 + 원격 PC명/IP 별명. 테이블 없으면 건너뜀.</summary>
    public class DirectoryBiz
    {
        private static readonly string[] SiteCodes = { "AURORA", "CMC", "RC", "MNGHA" };

        private readonly IDirectoryRepository _repository;
        private readonly RemotePcDac _remotePcDac;

        public DirectoryBiz()
            : this(new OracleDirectoryRepository(), new RemotePcDac())
        {
        }

        public DirectoryBiz(IDirectoryRepository repository)
            : this(repository, new RemotePcDac())
        {
        }

        public DirectoryBiz(IDirectoryRepository repository, RemotePcDac remotePcDac)
        {
            _repository = repository ?? throw new ArgumentNullException("repository");
            _remotePcDac = remotePcDac ?? throw new ArgumentNullException("remotePcDac");
        }

        public bool IsConfigured
        {
            get { return _repository.IsConfigured; }
        }

        public int UpsertLocalUser()
        {
            string name = OccupancyNameStore.TryGet();
            if (string.IsNullOrWhiteSpace(name))
                return 0;

            string pcNm = RemotePcShareBiz.LocalClientPc;
            if (string.IsNullOrWhiteSpace(pcNm))
                return 0;

            int n = _repository.UpsertUser(new DirectoryUserDto
            {
                UserName = name.Trim(),
                TeamName = OccupancyNameStore.TryGetAffiliation(),
                LocalPcName = pcNm.Trim(),
                LocalPcIp = RemotePcShareBiz.LocalAccessIp,
                WindowsAccount = RemotePcShareBiz.LocalWindowsAccount
            });
            InvalidateUserCache();
            return n;
        }

        public Task<int> UpsertLocalUserAsync()
        {
            return Task.Run(() => UpsertLocalUser());
        }

        public int SyncPcMapFromConfig()
        {
            if (!IsConfigured)
                return 0;

            int rows = 0;
            foreach (string site in SiteCodes)
            {
                List<RemotePcDto> pcs;
                try
                {
                    pcs = _remotePcDac.GetRemotePcListBySite(site);
                }
                catch
                {
                    continue;
                }
                if (pcs == null)
                    continue;

                foreach (var dto in pcs)
                {
                    if (dto == null || string.IsNullOrWhiteSpace(dto.PcName))
                        continue;
                    var map = ToPcMap(dto);
                    if (map == null)
                        continue;
                    int n = _repository.UpsertPcMap(map);
                    if (n > 0)
                        rows += n;
                }
            }
            WarmUserCache();
            return rows;
        }

        public static void WarmUserCache()
        {
            EnsureUserCache();
        }

        public Task<int> SyncPcMapFromConfigAsync()
        {
            return Task.Run(() => SyncPcMapFromConfig());
        }

        public IList<string> ResolvePcAliases(string siteCode, string value)
        {
            var list = new List<string>();
            AddAlias(list, value);
            if (string.IsNullOrWhiteSpace(value))
                return list;

            string needle = value.Trim();
            foreach (var dto in EnumerateConfigPcs(siteCode))
            {
                if (!ConfigPcMatches(dto, needle))
                    continue;
                AddAlias(list, dto.PcName);
                AddAlias(list, dto.HostAddress);
                if (LooksLikeIpv4(dto.IpAddress))
                    AddAlias(list, dto.IpAddress);
            }

            if (IsConfigured)
            {
                IList<string> fromDb;
                try
                {
                    fromDb = _repository.ResolvePcAliases(siteCode, needle);
                }
                catch
                {
                    fromDb = null;
                }
                if (fromDb != null)
                {
                    foreach (string a in fromDb)
                        AddAlias(list, a);
                }
            }

            return list;
        }

        /// <summary>검색창용. IP 일부(10.230.35)도 PC명 별명까지 묶는다.</summary>
        public IList<string> ResolvePcAliasesContaining(string siteCode, string value)
        {
            var list = new List<string>();
            AddAlias(list, value);
            if (string.IsNullOrWhiteSpace(value))
                return list;

            string needle = value.Trim();
            foreach (var dto in EnumerateConfigPcs(siteCode))
            {
                if (!ContainsIgnoreCase(dto.PcName, needle)
                    && !ContainsIgnoreCase(dto.HostAddress, needle)
                    && !(LooksLikeIpv4(dto.IpAddress) && ContainsIgnoreCase(dto.IpAddress, needle)))
                    continue;
                AddAlias(list, dto.PcName);
                AddAlias(list, dto.HostAddress);
                if (LooksLikeIpv4(dto.IpAddress))
                    AddAlias(list, dto.IpAddress);
            }

            IEnumerable<string> sites = EnumerateSiteCodes(siteCode);
            foreach (string site in sites)
            {
                IList<PcMapDto> maps = GetPcMapsBySite(site);
                if (maps == null)
                    continue;
                foreach (var map in maps)
                {
                    if (map == null)
                        continue;
                    if (!ContainsIgnoreCase(map.PcName, needle)
                        && !ContainsIgnoreCase(map.PcIp, needle))
                        continue;
                    AddAlias(list, map.PcName);
                    AddAlias(list, map.PcIp);
                }
            }

            return list;
        }

        private IEnumerable<string> EnumerateSiteCodes(string siteCode)
        {
            if (!string.IsNullOrWhiteSpace(siteCode)
                && !string.Equals(siteCode.Trim(), "ALL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(siteCode.Trim(), WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
            {
                yield return siteCode.Trim();
                yield break;
            }

            foreach (string site in SiteCodes)
                yield return site;
        }

        private static bool ContainsIgnoreCase(string source, string query)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
                return false;
            return source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>PCMAP.TEAM_NM이 있으면 갤러리 그룹으로 덮어씀. 없으면 App.config Group 유지.</summary>
        public void ApplyPcTeams(string siteCode, IList<RemotePcDto> pcs)
        {
            if (pcs == null || pcs.Count == 0 || string.IsNullOrWhiteSpace(siteCode))
                return;

            IList<PcMapDto> maps = GetPcMapsBySite(siteCode);
            if (maps == null || maps.Count == 0)
                return;

            foreach (var dto in pcs)
            {
                if (dto == null)
                    continue;
                PcMapDto map = FindPcMap(maps, dto);
                if (map == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(map.TeamName))
                    dto.GroupName = map.TeamName.Trim();
                if (!string.IsNullOrWhiteSpace(map.PcDomain))
                    dto.PcDomain = map.PcDomain.Trim();
                if (!string.IsNullOrWhiteSpace(map.PcNote))
                    dto.PcNote = map.PcNote.Trim();
                if (!string.IsNullOrWhiteSpace(map.PcComment))
                    dto.PcComment = map.PcComment.Trim();
                else if (!string.IsNullOrWhiteSpace(map.PcNote))
                    dto.PcComment = PcAccessNoteParser.ParseCommentFromNote(map.PcNote);
                ApplyPcMapIdentity(dto, map);
            }
        }

        /// <summary>PCMAP을 PC명으로만 붙인다. Host/IP가 사이트 공용이면 첫 PC 맵이 전 카드에 덮이지 않게.</summary>
        private static PcMapDto FindPcMap(IList<PcMapDto> maps, RemotePcDto dto)
        {
            if (maps == null || dto == null)
                return null;

            if (!string.IsNullOrWhiteSpace(dto.PcName))
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    PcMapDto map = maps[i];
                    if (map != null && Eq(map.PcName, dto.PcName))
                        return map;
                }
                return null;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                PcMapDto map = maps[i];
                if (map == null)
                    continue;
                if (Eq(map.ShareKey, dto.IpAddress)
                    || (LooksLikeIpv4(dto.IpAddress) && Eq(map.PcIp, dto.IpAddress)))
                    return map;
            }
            return null;
        }

        /// <summary>AURORA 점유 키를 PCMAP IP로 고유화. RC/CMC는 PC명을 유지하고 Host만 채움.</summary>
        private static void ApplyPcMapIdentity(RemotePcDto dto, PcMapDto map)
        {
            if (dto == null || map == null)
                return;
            if (!Eq(map.PcName, dto.PcName))
                return;

            bool aurora = string.Equals(dto.HospitalCode, "AURORA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(map.SiteCode, "AURORA", StringComparison.OrdinalIgnoreCase);

            if (aurora)
            {
                if (LooksLikeIpv4(map.PcIp))
                    dto.HostAddress = map.PcIp.Trim();
                string key = LooksLikeIpv4(map.ShareKey)
                    ? map.ShareKey.Trim()
                    : (LooksLikeIpv4(map.PcIp) ? map.PcIp.Trim() : null);
                if (!string.IsNullOrWhiteSpace(key))
                    dto.IpAddress = key;
                return;
            }

            if (LooksLikeIpv4(map.PcIp)
                && (string.IsNullOrWhiteSpace(dto.HostAddress) || !LooksLikeIpv4(dto.HostAddress)))
                dto.HostAddress = map.PcIp.Trim();
            if (!string.IsNullOrWhiteSpace(map.ShareKey))
                dto.IpAddress = map.ShareKey.Trim();
            else if (!string.IsNullOrWhiteSpace(map.PcName))
                dto.IpAddress = map.PcName.Trim();
        }

        public IList<PcMapDto> GetPcMapsBySite(string siteCode)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(siteCode))
                return new List<PcMapDto>();
            try
            {
                return _repository.GetPcMapsBySite(siteCode) ?? new List<PcMapDto>();
            }
            catch
            {
                return new List<PcMapDto>();
            }
        }

        /// <summary>선택 PC 코멘트 저장. 재시드해도 NVL로 기존값 유지.</summary>
        public int UpdatePcComment(string siteCode, string pcName, string comment)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(siteCode) || string.IsNullOrWhiteSpace(pcName))
                return 0;
            try
            {
                int n = _repository.UpdatePcComment(siteCode, pcName, comment);
                if (n > 0)
                    InvalidateUserCache();
                return n;
            }
            catch
            {
                return -1;
            }
        }

        public Task<int> UpdatePcCommentAsync(string siteCode, string pcName, string comment)
        {
            return Task.Run(() => UpdatePcComment(siteCode, pcName, comment));
        }

        /// <summary>ID/PW/VPN/COMMENT 일괄 저장.</summary>
        public int UpdatePcAccess(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(siteCode) || string.IsNullOrWhiteSpace(pcName))
                return 0;
            try
            {
                int n = _repository.UpdatePcAccess(siteCode, pcName, pcNote, pcComment, pcDomain);
                if (n > 0)
                    InvalidateUserCache();
                return n;
            }
            catch
            {
                return -1;
            }
        }

        public Task<int> UpdatePcAccessAsync(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain)
        {
            return Task.Run(() => UpdatePcAccess(siteCode, pcName, pcNote, pcComment, pcDomain));
        }

        private static readonly object UserCacheSync = new object();
        private static IList<DirectoryUserDto> _userCache;
        private static DateTime _userCacheUtc = DateTime.MinValue;
        private static IList<PcMapDto> _pcMapCache;
        private static DateTime _pcMapCacheUtc = DateTime.MinValue;

        public static void InvalidateUserCache()
        {
            lock (UserCacheSync)
            {
                _userCache = null;
                _userCacheUtc = DateTime.MinValue;
                _pcMapCache = null;
                _pcMapCacheUtc = DateTime.MinValue;
            }
        }

        /// <summary>MSDWHTKD_USR에서 소속. 점유자 표시용.</summary>
        public static string LookupTeam(string userName, string localPcName)
        {
            IList<DirectoryUserDto> users = EnsureUserCache();
            if (users == null || users.Count == 0)
                return null;

            string pc = string.IsNullOrWhiteSpace(localPcName) ? null : localPcName.Trim();
            string nm = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
            string byName = null;
            for (int i = 0; i < users.Count; i++)
            {
                DirectoryUserDto u = users[i];
                if (u == null || string.IsNullOrWhiteSpace(u.TeamName))
                    continue;
                if (pc != null && EqStatic(u.LocalPcName, pc))
                    return u.TeamName.Trim();
                if (nm != null
                    && (EqStatic(u.UserName, nm) || EqStatic(u.WindowsAccount, nm)))
                    byName = u.TeamName.Trim();
            }
            return byName;
        }

        /// <summary>PCMAP.TEAM_NM. 사이트 모르면 전 사이트에서 PC명으로 찾는다.</summary>
        public static string LookupPcTeam(string siteCode, string pcName)
        {
            if (string.IsNullOrWhiteSpace(pcName))
                return null;

            string pc = pcName.Trim();
            IList<PcMapDto> maps = EnsurePcMapCache();
            if (maps != null && maps.Count > 0)
            {
                string site = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();
                string fallback = null;
                for (int i = 0; i < maps.Count; i++)
                {
                    PcMapDto map = maps[i];
                    if (map == null || string.IsNullOrWhiteSpace(map.TeamName))
                        continue;
                    if (!PcMatches(map, pc))
                        continue;
                    if (site != null && EqStatic(map.SiteCode, site))
                        return map.TeamName.Trim();
                    if (fallback == null)
                        fallback = map.TeamName.Trim();
                }
                if (fallback != null)
                    return fallback;
            }
            return PcTeamCatalog.Find(pc);
        }

        private static bool PcMatches(PcMapDto map, string pc)
        {
            if (map == null || string.IsNullOrWhiteSpace(pc))
                return false;
            return RdpStatusBiz.ComputerNamesLooselyMatch(map.PcName, pc)
                || RdpStatusBiz.ComputerNamesLooselyMatch(map.ShareKey, pc)
                || EqStatic(map.PcIp, pc);
        }

        private static IList<PcMapDto> EnsurePcMapCache()
        {
            lock (UserCacheSync)
            {
                if (_pcMapCache != null && (DateTime.UtcNow - _pcMapCacheUtc).TotalSeconds < 20)
                    return _pcMapCache;
            }

            var loaded = new List<PcMapDto>();
            try
            {
                var repo = new OracleDirectoryRepository();
                for (int i = 0; i < SiteCodes.Length; i++)
                {
                    IList<PcMapDto> maps = repo.GetPcMapsBySite(SiteCodes[i]);
                    if (maps == null)
                        continue;
                    for (int j = 0; j < maps.Count; j++)
                    {
                        if (maps[j] != null)
                            loaded.Add(maps[j]);
                    }
                }
            }
            catch
            {
            }

            lock (UserCacheSync)
            {
                _pcMapCache = loaded;
                _pcMapCacheUtc = DateTime.UtcNow;
                return _pcMapCache;
            }
        }

        private static IList<DirectoryUserDto> EnsureUserCache()
        {
            lock (UserCacheSync)
            {
                if (_userCache != null && (DateTime.UtcNow - _userCacheUtc).TotalSeconds < 20)
                    return _userCache;
            }

            IList<DirectoryUserDto> loaded;
            try
            {
                loaded = new OracleDirectoryRepository().GetUsers();
            }
            catch
            {
                loaded = new List<DirectoryUserDto>();
            }

            lock (UserCacheSync)
            {
                _userCache = loaded ?? new List<DirectoryUserDto>();
                _userCacheUtc = DateTime.UtcNow;
                return _userCache;
            }
        }

        private static bool EqStatic(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<RemotePcDto> EnumerateConfigPcs(string siteCode)
        {
            if (!string.IsNullOrWhiteSpace(siteCode)
                && !string.Equals(siteCode.Trim(), "ALL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(siteCode.Trim(), WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
            {
                List<RemotePcDto> one;
                try
                {
                    one = _remotePcDac.GetRemotePcListBySite(siteCode.Trim());
                }
                catch
                {
                    one = null;
                }
                if (one == null)
                    yield break;
                foreach (var dto in one)
                    yield return dto;
                yield break;
            }

            foreach (string site in SiteCodes)
            {
                List<RemotePcDto> pcs;
                try
                {
                    pcs = _remotePcDac.GetRemotePcListBySite(site);
                }
                catch
                {
                    continue;
                }
                if (pcs == null)
                    continue;
                foreach (var dto in pcs)
                    yield return dto;
            }
        }

        private static PcMapDto ToPcMap(RemotePcDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.PcName))
                return null;
            string site = string.IsNullOrWhiteSpace(dto.HospitalCode) ? dto.Remark : dto.HospitalCode;
            if (string.IsNullOrWhiteSpace(site))
                return null;

            string host = dto.HostAddress;
            string share = dto.IpAddress;
            string ip = LooksLikeIpv4(host) ? host.Trim() : (LooksLikeIpv4(share) ? share.Trim() : null);

            return new PcMapDto
            {
                SiteCode = site.Trim().ToUpperInvariant(),
                PcName = dto.PcName.Trim(),
                PcIp = ip,
                ShareKey = string.IsNullOrWhiteSpace(share) ? dto.PcName.Trim() : share.Trim(),
                TeamName = string.IsNullOrWhiteSpace(dto.GroupName) ? null : dto.GroupName.Trim()
            };
        }

        private static bool ConfigPcMatches(RemotePcDto dto, string needle)
        {
            if (dto == null || string.IsNullOrWhiteSpace(needle))
                return false;
            return Eq(dto.PcName, needle)
                || Eq(dto.HostAddress, needle)
                || (LooksLikeIpv4(dto.IpAddress) && Eq(dto.IpAddress, needle));
        }

        private static bool Eq(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void AddAlias(IList<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            string t = value.Trim();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], t, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(t);
        }

        private static bool LooksLikeIpv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            int n;
            foreach (string p in parts)
            {
                if (!int.TryParse(p, out n) || n < 0 || n > 255)
                    return false;
            }
            return true;
        }
    }
}
