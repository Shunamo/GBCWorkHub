using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading.Tasks;
using GBCWorkHub.DTO;
using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>XSUP.MSDWHTKD_USR / MSDWHTKD_PCMAP. 없으면 probe 후 건너뜀. CREATE/ALTER 없음.</summary>
    public class OracleDirectoryRepository : IDirectoryRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string UserTable = "XSUP.MSDWHTKD_USR";
        private const string PcMapTable = "XSUP.MSDWHTKD_PCMAP";
        private const string UserSeq = "XSUP.SEQ_MSDWHTKD_USR";
        private const string PcMapSeq = "XSUP.SEQ_MSDWHTKD_PCMAP";
        private const int OracleTableMissing = 942;

        private readonly string _connectionString;
        private bool _userProbed;
        private bool _hasUserTable;
        private bool _pcMapProbed;
        private bool _hasPcMapTable;
        private bool _pcMapTeamProbed;
        private bool _hasPcMapTeam;
        private bool _pcMapDomainProbed;
        private bool _hasPcMapDomain;
        private bool _pcMapNoteProbed;
        private bool _hasPcMapNote;
        private bool _pcMapCommentProbed;
        private bool _hasPcMapComment;

        public OracleDirectoryRepository()
        {
            _connectionString = ResolveConnectionString();
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_connectionString) && !LooksLikePlaceholder(_connectionString); }
        }

        public Task<int> UpsertUserAsync(DirectoryUserDto user)
        {
            return Task.Run(() => UpsertUser(user));
        }

        public Task<int> UpsertPcMapAsync(PcMapDto map)
        {
            return Task.Run(() => UpsertPcMap(map));
        }

        public Task<int> UpdatePcCommentAsync(string siteCode, string pcName, string comment)
        {
            return Task.Run(() => UpdatePcComment(siteCode, pcName, comment));
        }

        public Task<int> UpdatePcAccessAsync(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain)
        {
            return Task.Run(() => UpdatePcAccess(siteCode, pcName, pcNote, pcComment, pcDomain));
        }

        public Task<IList<string>> ResolvePcAliasesAsync(string siteCode, string value)
        {
            return Task.Run(() => ResolvePcAliases(siteCode, value));
        }

        public Task<IList<DirectoryUserDto>> GetUsersAsync()
        {
            return Task.Run(() => GetUsers());
        }

        public Task<IList<PcMapDto>> GetPcMapsBySiteAsync(string siteCode)
        {
            return Task.Run(() => GetPcMapsBySite(siteCode));
        }

        public int UpsertUser(DirectoryUserDto user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.LocalPcName))
                return 0;
            if (!IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"MERGE INTO " + UserTable + @" t
                              USING (SELECT :pcNm AS LOCAL_PC_NM FROM DUAL) s
                                 ON (UPPER(TRIM(t.LOCAL_PC_NM)) = UPPER(TRIM(s.LOCAL_PC_NM)))
                              WHEN MATCHED THEN UPDATE SET
                                    t.USER_NM = :userNm,
                                    t.TEAM_NM = :teamNm,
                                    t.LOCAL_PC_IP = :ip,
                                    t.WIN_ACCOUNT = :winAcct,
                                    t.UPDT_DTM = SYSTIMESTAMP
                              WHEN NOT MATCHED THEN INSERT
                                    (USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT, CREATED_AT, UPDT_DTM)
                              VALUES (" + UserSeq + @".NEXTVAL, :userNm, :teamNm, :pcNm, :ip, :winAcct, SYSTIMESTAMP, SYSTIMESTAMP)";
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = Trim(user.LocalPcName, 100);
                        cmd.Parameters.Add("userNm", OracleDbType.NVarchar2).Value = Trim(user.UserName, 200);
                        cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value =
                            (object)Trim(user.TeamName, 100) ?? DBNull.Value;
                        cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                            (object)Trim(user.LocalPcIp, 50) ?? DBNull.Value;
                        cmd.Parameters.Add("winAcct", OracleDbType.Varchar2).Value =
                            (object)Trim(user.WindowsAccount, 200) ?? DBNull.Value;
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpsertUser failed: " + SafeError(ex));
                return -1;
            }
        }

        public int UpsertPcMap(PcMapDto map)
        {
            if (map == null || string.IsNullOrWhiteSpace(map.SiteCode) || string.IsNullOrWhiteSpace(map.PcName))
                return 0;
            if (!IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsurePcMapTable(conn);
                    if (!_hasPcMapTable)
                        return 0;
                    EnsurePcMapTeamColumn(conn);
                    EnsurePcMapDomainColumn(conn);
                    EnsurePcMapNoteColumn(conn);
                    EnsurePcMapCommentColumn(conn);

                    using (var cmd = CreateCommand(conn))
                    {
                        var updateCols = new StringBuilder();
                        updateCols.Append("t.PC_IP = NVL(:pcIp, t.PC_IP), t.SHARE_KEY = NVL(:shareKey, t.SHARE_KEY)");
                        var insertCols = new StringBuilder("MAP_ID, SITE_CD, PC_NM, PC_IP, SHARE_KEY");
                        var insertVals = new StringBuilder(PcMapSeq + ".NEXTVAL, :siteCd, :pcNm, :pcIp, :shareKey");

                        if (_hasPcMapTeam)
                        {
                            updateCols.Append(", t.TEAM_NM = NVL(:teamNm, t.TEAM_NM)");
                            insertCols.Append(", TEAM_NM");
                            insertVals.Append(", :teamNm");
                        }
                        if (_hasPcMapDomain)
                        {
                            updateCols.Append(", t.PC_DOMAIN = NVL(:pcDomain, t.PC_DOMAIN)");
                            insertCols.Append(", PC_DOMAIN");
                            insertVals.Append(", :pcDomain");
                        }
                        if (_hasPcMapNote)
                        {
                            updateCols.Append(", t.PC_NOTE = NVL(:pcNote, t.PC_NOTE)");
                            insertCols.Append(", PC_NOTE");
                            insertVals.Append(", :pcNote");
                        }
                        if (_hasPcMapComment)
                        {
                            updateCols.Append(", t.PC_COMMENT = NVL(:pcComment, t.PC_COMMENT)");
                            insertCols.Append(", PC_COMMENT");
                            insertVals.Append(", :pcComment");
                        }
                        updateCols.Append(", t.UPDT_DTM = SYSTIMESTAMP");
                        insertCols.Append(", UPDT_DTM");
                        insertVals.Append(", SYSTIMESTAMP");

                        cmd.CommandText =
                            @"MERGE INTO " + PcMapTable + @" t
                              USING (SELECT :siteCd AS SITE_CD, :pcNm AS PC_NM FROM DUAL) s
                                 ON (UPPER(TRIM(t.SITE_CD)) = UPPER(TRIM(s.SITE_CD))
                                 AND UPPER(TRIM(t.PC_NM)) = UPPER(TRIM(s.PC_NM)))
                              WHEN MATCHED THEN UPDATE SET " + updateCols + @"
                              WHEN NOT MATCHED THEN INSERT (" + insertCols + @")
                              VALUES (" + insertVals + ")";

                        cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = Trim(map.SiteCode, 20);
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = Trim(map.PcName, 100);
                        cmd.Parameters.Add("pcIp", OracleDbType.Varchar2).Value =
                            (object)Trim(map.PcIp, 50) ?? DBNull.Value;
                        cmd.Parameters.Add("shareKey", OracleDbType.Varchar2).Value =
                            (object)Trim(map.ShareKey, 100) ?? DBNull.Value;
                        if (_hasPcMapTeam)
                        {
                            cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value =
                                (object)Trim(map.TeamName, 100) ?? DBNull.Value;
                        }
                        if (_hasPcMapDomain)
                        {
                            cmd.Parameters.Add("pcDomain", OracleDbType.Varchar2).Value =
                                (object)Trim(map.PcDomain, 500) ?? DBNull.Value;
                        }
                        if (_hasPcMapNote)
                        {
                            cmd.Parameters.Add("pcNote", OracleDbType.Varchar2).Value =
                                (object)Trim(map.PcNote, 4000) ?? DBNull.Value;
                        }
                        if (_hasPcMapComment)
                        {
                            cmd.Parameters.Add("pcComment", OracleDbType.Varchar2).Value =
                                (object)Trim(map.PcComment, 2000) ?? DBNull.Value;
                        }
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpsertPcMap failed: " + SafeError(ex));
                return -1;
            }
        }

        public int UpdatePcComment(string siteCode, string pcName, string comment)
        {
            if (string.IsNullOrWhiteSpace(siteCode) || string.IsNullOrWhiteSpace(pcName) || !IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsurePcMapTable(conn);
                    if (!_hasPcMapTable)
                        return 0;
                    EnsurePcMapCommentColumn(conn);
                    if (!_hasPcMapComment)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + PcMapTable + @"
                                SET PC_COMMENT = :pcComment,
                                    UPDT_DTM = SYSTIMESTAMP
                              WHERE UPPER(TRIM(SITE_CD)) = UPPER(:siteCd)
                                AND UPPER(TRIM(PC_NM)) = UPPER(:pcNm)";
                        string trimmed = comment == null ? null : comment.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            cmd.Parameters.Add("pcComment", OracleDbType.Varchar2).Value = DBNull.Value;
                        else
                            cmd.Parameters.Add("pcComment", OracleDbType.Varchar2).Value = Trim(trimmed, 2000);
                        cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = siteCode.Trim();
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = pcName.Trim();
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpdatePcComment failed: " + SafeError(ex));
                return -1;
            }
        }

        public int UpdatePcAccess(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain)
        {
            if (string.IsNullOrWhiteSpace(siteCode) || string.IsNullOrWhiteSpace(pcName) || !IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsurePcMapTable(conn);
                    if (!_hasPcMapTable)
                        return 0;
                    EnsurePcMapDomainColumn(conn);
                    EnsurePcMapNoteColumn(conn);
                    EnsurePcMapCommentColumn(conn);
                    if (!_hasPcMapNote && !_hasPcMapComment && !_hasPcMapDomain)
                        return 0;

                    var sets = new StringBuilder();
                    if (_hasPcMapNote)
                        sets.Append("PC_NOTE = :pcNote");
                    if (_hasPcMapComment)
                    {
                        if (sets.Length > 0)
                            sets.Append(", ");
                        sets.Append("PC_COMMENT = :pcComment");
                    }
                    if (_hasPcMapDomain)
                    {
                        if (sets.Length > 0)
                            sets.Append(", ");
                        sets.Append("PC_DOMAIN = :pcDomain");
                    }
                    sets.Append(", UPDT_DTM = SYSTIMESTAMP");

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + PcMapTable + @"
                                SET " + sets + @"
                              WHERE UPPER(TRIM(SITE_CD)) = UPPER(:siteCd)
                                AND UPPER(TRIM(PC_NM)) = UPPER(:pcNm)";
                        if (_hasPcMapNote)
                        {
                            string note = pcNote == null ? null : pcNote.Trim();
                            cmd.Parameters.Add("pcNote", OracleDbType.Varchar2).Value =
                                string.IsNullOrEmpty(note) ? (object)DBNull.Value : Trim(note, 4000);
                        }
                        if (_hasPcMapComment)
                        {
                            string comment = pcComment == null ? null : pcComment.Trim();
                            cmd.Parameters.Add("pcComment", OracleDbType.Varchar2).Value =
                                string.IsNullOrEmpty(comment) ? (object)DBNull.Value : Trim(comment, 2000);
                        }
                        if (_hasPcMapDomain)
                        {
                            string domain = pcDomain == null ? null : pcDomain.Trim();
                            cmd.Parameters.Add("pcDomain", OracleDbType.Varchar2).Value =
                                string.IsNullOrEmpty(domain) ? (object)DBNull.Value : Trim(domain, 500);
                        }
                        cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = siteCode.Trim();
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = pcName.Trim();
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpdatePcAccess failed: " + SafeError(ex));
                return -1;
            }
        }

        public IList<string> ResolvePcAliases(string siteCode, string value)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(value) || !IsConfigured)
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsurePcMapTable(conn);
                    if (!_hasPcMapTable)
                        return list;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT PC_NM, PC_IP
                                FROM " + PcMapTable + @"
                               WHERE (UPPER(TRIM(PC_NM)) = UPPER(:v)
                                   OR UPPER(TRIM(PC_IP)) = UPPER(:v))
                                 AND (:hasSite = 0 OR UPPER(TRIM(SITE_CD)) = UPPER(:siteCd))";
                        cmd.Parameters.Add("v", OracleDbType.Varchar2).Value = value.Trim();
                        bool hasSite = !string.IsNullOrWhiteSpace(siteCode)
                            && !string.Equals(siteCode.Trim(), "ALL", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(siteCode.Trim(), WorkLogSiteCodesAll(), StringComparison.OrdinalIgnoreCase);
                        cmd.Parameters.Add("hasSite", OracleDbType.Int32).Value = hasSite ? 1 : 0;
                        cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value =
                            hasSite ? siteCode.Trim() : (object)"-";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                AddAlias(list, reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0)));
                                AddAlias(list, reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "ResolvePcAliases failed: " + SafeError(ex));
            }

            return list;
        }

        public IList<DirectoryUserDto> GetUsers()
        {
            var list = new List<DirectoryUserDto>();
            if (!IsConfigured)
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return list;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT
                                FROM " + UserTable + @"
                               ORDER BY UPDT_DTM DESC";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new DirectoryUserDto
                                {
                                    UserName = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0)),
                                    TeamName = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                                    LocalPcName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                    LocalPcIp = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                    WindowsAccount = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4))
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "GetUsers failed: " + SafeError(ex));
            }

            return list;
        }

        public IList<PcMapDto> GetPcMapsBySite(string siteCode)
        {
            var list = new List<PcMapDto>();
            if (string.IsNullOrWhiteSpace(siteCode) || !IsConfigured)
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsurePcMapTable(conn);
                    if (!_hasPcMapTable)
                        return list;
                    EnsurePcMapTeamColumn(conn);
                    EnsurePcMapDomainColumn(conn);
                    EnsurePcMapNoteColumn(conn);
                    EnsurePcMapCommentColumn(conn);

                    using (var cmd = CreateCommand(conn))
                    {
                        var select = new StringBuilder("SELECT SITE_CD, PC_NM, PC_IP, SHARE_KEY");
                        if (_hasPcMapTeam)
                            select.Append(", TEAM_NM");
                        if (_hasPcMapDomain)
                            select.Append(", PC_DOMAIN");
                        if (_hasPcMapNote)
                            select.Append(", PC_NOTE");
                        if (_hasPcMapComment)
                            select.Append(", PC_COMMENT");
                        select.Append(" FROM ").Append(PcMapTable)
                            .Append(" WHERE UPPER(TRIM(SITE_CD)) = UPPER(:siteCd) ORDER BY PC_NM");
                        cmd.CommandText = select.ToString();
                        cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = siteCode.Trim();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int i = 0;
                                var row = new PcMapDto
                                {
                                    SiteCode = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i)),
                                    PcName = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i)),
                                    PcIp = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i)),
                                    ShareKey = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i))
                                };
                                if (_hasPcMapTeam)
                                    row.TeamName = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i));
                                if (_hasPcMapDomain)
                                    row.PcDomain = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i));
                                if (_hasPcMapNote)
                                    row.PcNote = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i));
                                if (_hasPcMapComment)
                                    row.PcComment = reader.IsDBNull(++i) ? null : Convert.ToString(reader.GetValue(i));
                                list.Add(row);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "GetPcMapsBySite failed: " + SafeError(ex));
            }

            return list;
        }

        private void EnsureUserTable(OracleConnection conn)
        {
            if (_userProbed)
                return;
            _userProbed = true;
            _hasUserTable = ProbeTable(conn, UserTable, "MSDWHTKD_USR");
        }

        private void EnsurePcMapTable(OracleConnection conn)
        {
            if (_pcMapProbed)
                return;
            _pcMapProbed = true;
            _hasPcMapTable = ProbeTable(conn, PcMapTable, "MSDWHTKD_PCMAP");
        }

        private void EnsurePcMapTeamColumn(OracleConnection conn)
        {
            if (_pcMapTeamProbed)
                return;
            _pcMapTeamProbed = true;
            if (!_hasPcMapTable)
                return;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT TEAM_NM FROM " + PcMapTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasPcMapTeam = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasPcMapTeam = false;
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "MSDWHTKD_PCMAP.TEAM_NM missing; skip. Apply sql/16_ALTER_PCMAP_TEAM_NM.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        private void EnsurePcMapDomainColumn(OracleConnection conn)
        {
            if (_pcMapDomainProbed)
                return;
            _pcMapDomainProbed = true;
            if (!_hasPcMapTable)
                return;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT PC_DOMAIN FROM " + PcMapTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasPcMapDomain = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasPcMapDomain = false;
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "MSDWHTKD_PCMAP.PC_DOMAIN missing; skip. Apply sql/19_ALTER_PCMAP_PC_DOMAIN.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        private void EnsurePcMapNoteColumn(OracleConnection conn)
        {
            if (_pcMapNoteProbed)
                return;
            _pcMapNoteProbed = true;
            if (!_hasPcMapTable)
                return;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT PC_NOTE FROM " + PcMapTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasPcMapNote = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasPcMapNote = false;
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "MSDWHTKD_PCMAP.PC_NOTE missing; skip. Apply sql/19_ALTER_PCMAP_PC_DOMAIN.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        private void EnsurePcMapCommentColumn(OracleConnection conn)
        {
            if (_pcMapCommentProbed)
                return;
            _pcMapCommentProbed = true;
            if (!_hasPcMapTable)
                return;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT PC_COMMENT FROM " + PcMapTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasPcMapComment = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasPcMapComment = false;
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "MSDWHTKD_PCMAP.PC_COMMENT missing; skip. Apply sql/19_ALTER_PCMAP_PC_DOMAIN.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        private static bool ProbeTable(OracleConnection conn, string table, string label)
        {
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT 1 FROM " + table + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                return true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == OracleTableMissing)
                {
                    WorkHubFileLogger.Warn("DIRECTORY",
                        label + " missing; skip. Apply sql/14_MSDWHTKD_USR_PCMAP.sql when DBA can.");
                    return false;
                }
                throw;
            }
        }

        private OracleConnection OpenConnection()
        {
            var conn = new OracleConnection(_connectionString);
            conn.Open();
            OracleKoreaSession.Apply(conn);
            return conn;
        }

        private static OracleCommand CreateCommand(OracleConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            return cmd;
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

        private static string WorkLogSiteCodesAll()
        {
            return GBCWorkHub.DTO.WorkLog.WorkLogSiteCodes.All;
        }

        private static string ResolveConnectionString()
        {
            try
            {
                var cs = ConfigurationManager.ConnectionStrings[ConnectionStringName];
                if (cs == null || string.IsNullOrWhiteSpace(cs.ConnectionString))
                    return null;
                return cs.ConnectionString.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikePlaceholder(string cs)
        {
            if (string.IsNullOrWhiteSpace(cs))
                return true;
            string upper = cs.ToUpperInvariant();
            return upper.Contains("YOUR_")
                || upper.Contains("CHANGE_ME")
                || upper.Contains("TODO")
                || upper.Contains("PLACEHOLDER");
        }

        private static string Trim(string value, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string t = value.Trim();
            if (Encoding.UTF8.GetByteCount(t) <= maxBytes)
                return t;
            int lo = 0;
            int hi = t.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Encoding.UTF8.GetByteCount(t.Substring(0, mid)) <= maxBytes)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo <= 0 ? null : t.Substring(0, lo);
        }

        private static string SafeError(Exception ex)
        {
            return ex == null ? string.Empty : ex.Message;
        }
    }
}
