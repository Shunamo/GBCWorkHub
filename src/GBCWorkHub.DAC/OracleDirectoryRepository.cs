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
        private bool _userAuthProbed;
        private bool _hasUserAuthColumns;
        private bool _userDeletedProbed;
        private bool _hasUserDeletedColumn;

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

        public Task<DirectoryUserDto> FindUserForAuthAsync(string loginOrName)
        {
            return Task.Run(() => FindUserForAuth(loginOrName));
        }

        public Task<DirectoryUserDto> FindUserByLocalEndpointAsync(string pcName, string pcIp)
        {
            return Task.Run(() => FindUserByLocalEndpoint(pcName, pcIp));
        }

        public bool HasUserAuthColumns
        {
            get
            {
                if (!IsConfigured)
                    return false;
                try
                {
                    using (var conn = OpenConnection())
                    {
                        EnsureUserTable(conn);
                        if (!_hasUserTable)
                            return false;
                        EnsureUserAuthColumns(conn);
                        return _hasUserAuthColumns;
                    }
                }
                catch
                {
                    return false;
                }
            }
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

        public Task<int> DeletePcMapAsync(string siteCode, string pcName)
        {
            return Task.Run(() => DeletePcMap(siteCode, pcName));
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
                    EnsureUserAuthColumns(conn);

                    // Avoid MERGE ORA-38104: ON-clause columns (LOGIN_ID/USER_NM) cannot be UPDATEd.
                    if (_hasUserAuthColumns)
                        return UpsertUserWithAuth(conn, user);
                    return UpsertUserLegacy(conn, user);
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpsertUser failed: " + SafeError(ex));
                return -1;
            }
        }

        private int UpsertUserWithAuth(OracleConnection conn, DirectoryUserDto user)
        {
            string loginId = string.IsNullOrWhiteSpace(user.LoginId) ? user.UserName : user.LoginId;
            object pcNm = Trim(user.LocalPcName, 100);
            object userNm = Trim(user.UserName, 200);
            object teamNm = (object)Trim(user.TeamName, 100) ?? DBNull.Value;
            object ip = (object)Trim(user.LocalPcIp, 50) ?? DBNull.Value;
            object winAcct = (object)Trim(user.WindowsAccount, 200) ?? DBNull.Value;
            object login = (object)Trim(loginId, 100) ?? DBNull.Value;
            object pwHash = (object)Trim(user.PasswordHash, 128) ?? DBNull.Value;
            object isActive = user.IsActive ? "Y" : "N";

            string setSql =
                @"SET USER_NM = :userNm,
                      TEAM_NM = :teamNm,
                      LOCAL_PC_NM = :pcNm,
                      LOCAL_PC_IP = :ip,
                      WIN_ACCOUNT = :winAcct,
                      LOGIN_ID = :loginId,
                      PASSWORD_HASH = NVL(:pwHash, PASSWORD_HASH),
                      IS_ACTIVE = NVL(:isActive, IS_ACTIVE),
                      UPDT_DTM = SYSTIMESTAMP";

            using (var cmd = CreateCommand(conn))
            {
                // 1) Match existing account by LOGIN_ID — the ONLY identity anchor. LOCAL_PC_NM is
                //    never part of identity resolution; it is overwritten below as "latest PC" only.
                cmd.CommandText =
                    @"UPDATE " + UserTable + @"
                      " + setSql + @"
                    WHERE UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:loginId))";
                BindUpsertUserParams(cmd, pcNm, userNm, teamNm, ip, winAcct, login, pwHash, isActive);
                int n = cmd.ExecuteNonQuery();
                if (n > 0)
                {
                    FetchUserId(conn, loginId, user);
                    return n;
                }

                // 2) Legacy row: LOGIN_ID empty, USER_NM matches (pre-sql/20 rows only)
                cmd.Parameters.Clear();
                cmd.CommandText =
                    @"UPDATE " + UserTable + @"
                      " + setSql + @"
                    WHERE UPPER(TRIM(USER_NM)) = UPPER(TRIM(:userNm))
                      AND (LOGIN_ID IS NULL OR TRIM(LOGIN_ID) IS NULL)
                      AND UPPER(TRIM(LOCAL_PC_NM)) <> 'ADMIN'";
                BindUpsertUserParams(cmd, pcNm, userNm, teamNm, ip, winAcct, login, pwHash, isActive);
                n = cmd.ExecuteNonQuery();
                if (n > 0)
                {
                    FetchUserId(conn, loginId, user);
                    return n;
                }

                // 3) New account. LOCAL_PC_NM carries no uniqueness constraint (sql/23
                //    MSDWHTKD_USR_IDENTITY_FIX dropped UK_MSDWHTKD_USR_PC) — registering from a PC
                //    someone else has used before must NEVER reuse/overwrite that person's row.
                cmd.Parameters.Clear();
                cmd.CommandText =
                    @"INSERT INTO " + UserTable + @"
                        (USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT,
                         LOGIN_ID, PASSWORD_HASH, IS_ACTIVE, CREATED_AT, UPDT_DTM)
                      VALUES (" + UserSeq + @".NEXTVAL, :userNm, :teamNm, :pcNm, :ip, :winAcct,
                              :loginId, :pwHash, NVL(:isActive, 'Y'), SYSTIMESTAMP, SYSTIMESTAMP)";
                BindUpsertUserParams(cmd, pcNm, userNm, teamNm, ip, winAcct, login, pwHash, isActive);
                try
                {
                    n = cmd.ExecuteNonQuery();
                    if (n > 0)
                        FetchUserId(conn, loginId, user);
                    return n;
                }
                catch (OracleException ox) when (ox.Number == 1)
                {
                    // Only possible remaining unique key is LOGIN_ID (UX_MSDWHTKD_USR_LOGIN) — a
                    // genuine race between two concurrent registrations of the same LOGIN_ID.
                    // Fail safely: log and return -1. Never overwrite another user's identity row.
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "UpsertUser identity conflict on LOGIN_ID='" + loginId + "' (concurrent registration?): "
                        + SafeError(ox));
                    return -1;
                }
            }
        }

        /// <summary>UPDATE/INSERT 성공 직후 USR_ID를 채워 세션에 즉시 보관할 수 있게 한다.</summary>
        private void FetchUserId(OracleConnection conn, string loginId, DirectoryUserDto user)
        {
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT USR_ID FROM " + UserTable + " WHERE UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:loginId))";
                    cmd.Parameters.Add("loginId", OracleDbType.NVarchar2).Value =
                        (object)Trim(loginId, 100) ?? DBNull.Value;
                    object result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        user.UserId = Convert.ToInt64(result);
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "FetchUserId failed: " + SafeError(ex));
            }
        }

        private int UpsertUserLegacy(OracleConnection conn, DirectoryUserDto user)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText =
                    @"UPDATE " + UserTable + @"
                        SET TEAM_NM = :teamNm,
                            LOCAL_PC_NM = :pcNm,
                            LOCAL_PC_IP = :ip,
                            WIN_ACCOUNT = :winAcct,
                            UPDT_DTM = SYSTIMESTAMP
                      WHERE UPPER(TRIM(USER_NM)) = UPPER(TRIM(:userNm))";
                cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = Trim(user.LocalPcName, 100);
                cmd.Parameters.Add("userNm", OracleDbType.NVarchar2).Value = Trim(user.UserName, 200);
                cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value =
                    (object)Trim(user.TeamName, 100) ?? DBNull.Value;
                cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                    (object)Trim(user.LocalPcIp, 50) ?? DBNull.Value;
                cmd.Parameters.Add("winAcct", OracleDbType.Varchar2).Value =
                    (object)Trim(user.WindowsAccount, 200) ?? DBNull.Value;
                int n = cmd.ExecuteNonQuery();
                if (n > 0)
                    return n;

                cmd.Parameters.Clear();
                cmd.CommandText =
                    @"INSERT INTO " + UserTable + @"
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
                try
                {
                    return cmd.ExecuteNonQuery();
                }
                catch (OracleException ox) when (ox.Number == 1)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText =
                        @"UPDATE " + UserTable + @"
                            SET USER_NM = :userNm,
                                TEAM_NM = :teamNm,
                                LOCAL_PC_IP = :ip,
                                WIN_ACCOUNT = :winAcct,
                                UPDT_DTM = SYSTIMESTAMP
                          WHERE UPPER(TRIM(LOCAL_PC_NM)) = UPPER(TRIM(:pcNm))
                            AND UPPER(TRIM(LOCAL_PC_NM)) <> 'ADMIN'";
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

        private static void BindUpsertUserParams(
            OracleCommand cmd,
            object pcNm,
            object userNm,
            object teamNm,
            object ip,
            object winAcct,
            object loginId,
            object pwHash,
            object isActive)
        {
            cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = pcNm;
            cmd.Parameters.Add("userNm", OracleDbType.NVarchar2).Value = userNm;
            cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value = teamNm;
            cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value = ip;
            cmd.Parameters.Add("winAcct", OracleDbType.Varchar2).Value = winAcct;
            cmd.Parameters.Add("loginId", OracleDbType.NVarchar2).Value = loginId;
            cmd.Parameters.Add("pwHash", OracleDbType.Varchar2).Value = pwHash;
            cmd.Parameters.Add("isActive", OracleDbType.Char).Value = isActive;
        }

        public DirectoryUserDto FindUserForAuth(string loginOrName)
        {
            if (string.IsNullOrWhiteSpace(loginOrName) || !IsConfigured)
                return null;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return null;
                    EnsureUserAuthColumns(conn);
                    if (!_hasUserAuthColumns)
                        return null;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT * FROM (
                                SELECT USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT,
                                       LOGIN_ID, PASSWORD_HASH, IS_ACTIVE
                                  FROM " + UserTable + @"
                                 WHERE UPPER(TRIM(USER_NM)) = UPPER(TRIM(:id))
                                    OR UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:id))
                                 ORDER BY CASE
                                            WHEN UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:id)) THEN 0
                                            ELSE 1
                                          END,
                                          UPDT_DTM DESC
                              ) WHERE ROWNUM = 1";
                        cmd.Parameters.Add("id", OracleDbType.NVarchar2).Value = loginOrName.Trim();
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return null;
                            return new DirectoryUserDto
                            {
                                UserId = reader.IsDBNull(0) ? (long?)null : Convert.ToInt64(reader.GetValue(0)),
                                UserName = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                                TeamName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                LocalPcName = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                LocalPcIp = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                                WindowsAccount = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                                LoginId = reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6)),
                                PasswordHash = reader.IsDBNull(7) ? null : Convert.ToString(reader.GetValue(7)),
                                IsActive = reader.IsDBNull(8)
                                    || string.Equals(Convert.ToString(reader.GetValue(8)), "Y", StringComparison.OrdinalIgnoreCase)
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "FindUserForAuth failed: " + SafeError(ex));
                return null;
            }
        }

        public DirectoryUserDto FindUserByLocalEndpoint(string pcName, string pcIp)
        {
            if (!IsConfigured)
                return null;
            if (string.IsNullOrWhiteSpace(pcName) && string.IsNullOrWhiteSpace(pcIp))
                return null;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return null;
                    EnsureUserAuthColumns(conn);

                    using (var cmd = CreateCommand(conn))
                    {
                        if (_hasUserAuthColumns)
                        {
                            cmd.CommandText =
                                @"SELECT * FROM (
                                    SELECT USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT,
                                           LOGIN_ID, PASSWORD_HASH, IS_ACTIVE
                                      FROM " + UserTable + @"
                                     WHERE (
                                             (:hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)))
                                          OR (:hasPc = 1 AND UPPER(TRIM(LOCAL_PC_NM)) = UPPER(TRIM(:pcNm)))
                                           )
                                       AND UPPER(TRIM(NVL(LOGIN_ID, ' '))) <> 'ADMIN'
                                       AND UPPER(TRIM(NVL(USER_NM, ' '))) NOT IN ('ADMIN', N'관리자')
                                       AND UPPER(TRIM(NVL(LOCAL_PC_NM, ' '))) <> 'ADMIN'
                                     ORDER BY CASE
                                                WHEN :hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)) THEN 0
                                                ELSE 1
                                              END,
                                              UPDT_DTM DESC
                                  ) WHERE ROWNUM = 1";
                        }
                        else
                        {
                            cmd.CommandText =
                                @"SELECT * FROM (
                                    SELECT USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT
                                      FROM " + UserTable + @"
                                     WHERE (
                                             (:hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)))
                                          OR (:hasPc = 1 AND UPPER(TRIM(LOCAL_PC_NM)) = UPPER(TRIM(:pcNm)))
                                           )
                                       AND UPPER(TRIM(NVL(USER_NM, ' '))) NOT IN ('ADMIN', N'관리자')
                                       AND UPPER(TRIM(NVL(LOCAL_PC_NM, ' '))) <> 'ADMIN'
                                     ORDER BY CASE
                                                WHEN :hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)) THEN 0
                                                ELSE 1
                                              END,
                                              UPDT_DTM DESC
                                  ) WHERE ROWNUM = 1";
                        }

                        bool hasIp = !string.IsNullOrWhiteSpace(pcIp) && pcIp.Trim() != "-";
                        bool hasPc = !string.IsNullOrWhiteSpace(pcName);
                        cmd.Parameters.Add("hasIp", OracleDbType.Int32).Value = hasIp ? 1 : 0;
                        cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                            hasIp ? (object)pcIp.Trim() : DBNull.Value;
                        cmd.Parameters.Add("hasPc", OracleDbType.Int32).Value = hasPc ? 1 : 0;
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value =
                            hasPc ? (object)pcName.Trim() : DBNull.Value;

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return null;
                            var dto = new DirectoryUserDto
                            {
                                UserId = reader.IsDBNull(0) ? (long?)null : Convert.ToInt64(reader.GetValue(0)),
                                UserName = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                                TeamName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                LocalPcName = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                LocalPcIp = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                                WindowsAccount = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                                IsActive = true
                            };
                            if (_hasUserAuthColumns)
                            {
                                dto.LoginId = reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6));
                                dto.PasswordHash = reader.IsDBNull(7) ? null : Convert.ToString(reader.GetValue(7));
                                dto.IsActive = reader.IsDBNull(8)
                                    || string.Equals(Convert.ToString(reader.GetValue(8)), "Y", StringComparison.OrdinalIgnoreCase);
                            }
                            return dto;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "FindUserByLocalEndpoint failed: " + SafeError(ex));
                return null;
            }
        }

        public DirectoryUserDto FindClaimableUserOnLocalEndpoint(string pcName, string pcIp)
        {
            if (!IsConfigured)
                return null;
            if (string.IsNullOrWhiteSpace(pcName) && string.IsNullOrWhiteSpace(pcIp))
                return null;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return null;
                    EnsureUserAuthColumns(conn);

                    using (var cmd = CreateCommand(conn))
                    {
                        // Include rows overwritten as 관리자/ADMIN on a real PC (not ADMIN seed).
                        if (_hasUserAuthColumns)
                        {
                            cmd.CommandText =
                                @"SELECT * FROM (
                                    SELECT USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT,
                                           LOGIN_ID, PASSWORD_HASH, IS_ACTIVE
                                      FROM " + UserTable + @"
                                     WHERE (
                                             (:hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)))
                                          OR (:hasPc = 1 AND UPPER(TRIM(LOCAL_PC_NM)) = UPPER(TRIM(:pcNm)))
                                           )
                                       AND UPPER(TRIM(NVL(LOCAL_PC_NM, ' '))) <> 'ADMIN'
                                     ORDER BY CASE
                                                WHEN :hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)) THEN 0
                                                ELSE 1
                                              END,
                                              UPDT_DTM DESC
                                  ) WHERE ROWNUM = 1";
                        }
                        else
                        {
                            cmd.CommandText =
                                @"SELECT * FROM (
                                    SELECT USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT
                                      FROM " + UserTable + @"
                                     WHERE (
                                             (:hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)))
                                          OR (:hasPc = 1 AND UPPER(TRIM(LOCAL_PC_NM)) = UPPER(TRIM(:pcNm)))
                                           )
                                       AND UPPER(TRIM(NVL(LOCAL_PC_NM, ' '))) <> 'ADMIN'
                                     ORDER BY CASE
                                                WHEN :hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(TRIM(:ip)) THEN 0
                                                ELSE 1
                                              END,
                                              UPDT_DTM DESC
                                  ) WHERE ROWNUM = 1";
                        }

                        bool hasIp = !string.IsNullOrWhiteSpace(pcIp) && pcIp.Trim() != "-";
                        bool hasPc = !string.IsNullOrWhiteSpace(pcName);
                        cmd.Parameters.Add("hasIp", OracleDbType.Int32).Value = hasIp ? 1 : 0;
                        cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                            hasIp ? (object)pcIp.Trim() : DBNull.Value;
                        cmd.Parameters.Add("hasPc", OracleDbType.Int32).Value = hasPc ? 1 : 0;
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value =
                            hasPc ? (object)pcName.Trim() : DBNull.Value;

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return null;
                            var dto = new DirectoryUserDto
                            {
                                UserId = reader.IsDBNull(0) ? (long?)null : Convert.ToInt64(reader.GetValue(0)),
                                UserName = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                                TeamName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                LocalPcName = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                LocalPcIp = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                                WindowsAccount = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                                IsActive = true
                            };
                            if (_hasUserAuthColumns)
                            {
                                dto.LoginId = reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6));
                                dto.PasswordHash = reader.IsDBNull(7) ? null : Convert.ToString(reader.GetValue(7));
                                dto.IsActive = reader.IsDBNull(8)
                                    || string.Equals(Convert.ToString(reader.GetValue(8)), "Y", StringComparison.OrdinalIgnoreCase);
                            }
                            return dto;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "FindClaimableUserOnLocalEndpoint failed: " + SafeError(ex));
                return null;
            }
        }

        public int UpdateUserIdentity(long userId, DirectoryUserDto user)
        {
            if (userId <= 0 || user == null || string.IsNullOrWhiteSpace(user.UserName))
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
                    EnsureUserAuthColumns(conn);

                    using (var cmd = CreateCommand(conn))
                    {
                        if (_hasUserAuthColumns)
                        {
                            cmd.CommandText =
                                @"UPDATE " + UserTable + @"
                                    SET USER_NM = :userNm,
                                        TEAM_NM = :teamNm,
                                        LOCAL_PC_NM = :pcNm,
                                        LOCAL_PC_IP = :ip,
                                        WIN_ACCOUNT = :winAcct,
                                        LOGIN_ID = :loginId,
                                        PASSWORD_HASH = NVL(:pwHash, PASSWORD_HASH),
                                        IS_ACTIVE = NVL(:isActive, IS_ACTIVE),
                                        UPDT_DTM = SYSTIMESTAMP
                                  WHERE USR_ID = :usrId";
                        }
                        else
                        {
                            cmd.CommandText =
                                @"UPDATE " + UserTable + @"
                                    SET USER_NM = :userNm,
                                        TEAM_NM = :teamNm,
                                        LOCAL_PC_NM = :pcNm,
                                        LOCAL_PC_IP = :ip,
                                        WIN_ACCOUNT = :winAcct,
                                        UPDT_DTM = SYSTIMESTAMP
                                  WHERE USR_ID = :usrId";
                        }

                        cmd.Parameters.Add("usrId", OracleDbType.Int64).Value = userId;
                        cmd.Parameters.Add("userNm", OracleDbType.NVarchar2).Value = Trim(user.UserName, 200);
                        cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value =
                            (object)Trim(user.TeamName, 100) ?? DBNull.Value;
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value =
                            (object)Trim(user.LocalPcName, 100) ?? DBNull.Value;
                        cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                            (object)Trim(user.LocalPcIp, 50) ?? DBNull.Value;
                        cmd.Parameters.Add("winAcct", OracleDbType.Varchar2).Value =
                            (object)Trim(user.WindowsAccount, 200) ?? DBNull.Value;
                        if (_hasUserAuthColumns)
                        {
                            string loginId = string.IsNullOrWhiteSpace(user.LoginId) ? user.UserName : user.LoginId;
                            cmd.Parameters.Add("loginId", OracleDbType.Varchar2).Value =
                                (object)Trim(loginId, 100) ?? DBNull.Value;
                            cmd.Parameters.Add("pwHash", OracleDbType.Varchar2).Value =
                                (object)Trim(user.PasswordHash, 128) ?? DBNull.Value;
                            cmd.Parameters.Add("isActive", OracleDbType.Char).Value =
                                user.IsActive ? "Y" : "N";
                        }
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpdateUserIdentity failed: " + SafeError(ex));
                return -1;
            }
        }

        public int UpdatePasswordHash(string loginOrName, string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(loginOrName) || string.IsNullOrWhiteSpace(passwordHash))
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
                    EnsureUserAuthColumns(conn);
                    if (!_hasUserAuthColumns)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + UserTable + @"
                                SET PASSWORD_HASH = :pwHash,
                                    UPDT_DTM = SYSTIMESTAMP
                              WHERE UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:id))
                                 OR UPPER(TRIM(USER_NM)) = UPPER(TRIM(:id))";
                        cmd.Parameters.Add("pwHash", OracleDbType.Varchar2).Value = Trim(passwordHash, 128);
                        cmd.Parameters.Add("id", OracleDbType.NVarchar2).Value = loginOrName.Trim();
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpdatePasswordHash failed: " + SafeError(ex));
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
                    EnsureUserAuthColumns(conn);
                    EnsureUserDeletedColumn(conn);

                    using (var cmd = CreateCommand(conn))
                    {
                        if (_hasUserAuthColumns)
                        {
                            cmd.CommandText =
                                @"SELECT USR_ID, USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT,
                                         LOGIN_ID, IS_ACTIVE" + (_hasUserDeletedColumn ? ", IS_DELETED" : string.Empty) + @"
                                    FROM " + UserTable + @"
                                   ORDER BY UPDT_DTM DESC";
                        }
                        else
                        {
                            cmd.CommandText =
                                @"SELECT USER_NM, TEAM_NM, LOCAL_PC_NM, LOCAL_PC_IP, WIN_ACCOUNT
                                    FROM " + UserTable + @"
                                   ORDER BY UPDT_DTM DESC";
                        }
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (_hasUserAuthColumns)
                                {
                                    list.Add(new DirectoryUserDto
                                    {
                                        UserId = reader.IsDBNull(0) ? (long?)null : Convert.ToInt64(reader.GetValue(0)),
                                        UserName = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                                        TeamName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                        LocalPcName = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                        LocalPcIp = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                                        WindowsAccount = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                                        LoginId = reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6)),
                                        IsActive = reader.IsDBNull(7)
                                            || string.Equals(Convert.ToString(reader.GetValue(7)), "Y", StringComparison.OrdinalIgnoreCase),
                                        IsDeleted = _hasUserDeletedColumn
                                            && !reader.IsDBNull(8) && Convert.ToInt32(reader.GetValue(8)) != 0
                                    });
                                }
                                else
                                {
                                    list.Add(new DirectoryUserDto
                                    {
                                        UserName = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0)),
                                        TeamName = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                                        LocalPcName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                        LocalPcIp = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                        WindowsAccount = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                                        IsActive = true
                                    });
                                }
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

        public int SetUserActive(string loginOrName, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(loginOrName) || !IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return 0;
                    EnsureUserAuthColumns(conn);
                    if (!_hasUserAuthColumns)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + UserTable + @"
                                SET IS_ACTIVE = :active,
                                    UPDT_DTM = SYSTIMESTAMP
                              WHERE UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:id))
                                 OR UPPER(TRIM(USER_NM)) = UPPER(TRIM(:id))";
                        cmd.Parameters.Add("active", OracleDbType.Char).Value = isActive ? "Y" : "N";
                        cmd.Parameters.Add("id", OracleDbType.NVarchar2).Value = loginOrName.Trim();
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "SetUserActive failed: " + SafeError(ex));
                return -1;
            }
        }

        /// <summary>실제 DELETE 대신 IS_DELETED만 세운다 — 요청사항/업무기록이 USR_ID를 FK로 참조한다.</summary>
        public int SetUserDeleted(long userId, bool isDeleted)
        {
            if (userId <= 0 || !IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return 0;
                    EnsureUserDeletedColumn(conn);
                    if (!_hasUserDeletedColumn)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + UserTable + @"
                                 SET IS_DELETED = :deleted,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE USR_ID = :userId";
                        cmd.Parameters.Add("deleted", OracleDbType.Int32).Value = isDeleted ? 1 : 0;
                        cmd.Parameters.Add("userId", OracleDbType.Int64).Value = userId;
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "SetUserDeleted failed: " + SafeError(ex));
                return -1;
            }
        }

        /// <summary>
        /// 관리자 개인정보 수정 팝업 전용 — 이름/소속/로그인ID만 좁게 갱신한다. UpdateUserIdentity는
        /// PC/비밀번호/활성여부까지 같이 덮어써서 이 용도로 재사용하면 위험하다.
        /// </summary>
        public int UpdateUserProfile(long userId, string userName, string teamName, string loginId)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(userName) || !IsConfigured)
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
                            @"UPDATE " + UserTable + @"
                                 SET USER_NM = :userName,
                                     TEAM_NM = :teamName,
                                     LOGIN_ID = :loginId,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE USR_ID = :userId";
                        cmd.Parameters.Add("userName", OracleDbType.NVarchar2).Value = Trim(userName, 200);
                        cmd.Parameters.Add("teamName", OracleDbType.NVarchar2).Value = (object)Trim(teamName, 100) ?? DBNull.Value;
                        cmd.Parameters.Add("loginId", OracleDbType.Varchar2).Value = (object)Trim(loginId, 100) ?? DBNull.Value;
                        cmd.Parameters.Add("userId", OracleDbType.Int64).Value = userId;
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "UpdateUserProfile failed: " + SafeError(ex));
                return -1;
            }
        }

        /// <summary>LOGIN_ID 유니크 제약(UX_MSDWHTKD_USR_LOGIN) 위반을 저장 전에 미리 확인.</summary>
        public bool LoginIdExists(string loginId, long excludeUserId)
        {
            if (string.IsNullOrWhiteSpace(loginId) || !IsConfigured)
                return false;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureUserTable(conn);
                    if (!_hasUserTable)
                        return false;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT COUNT(*) FROM " + UserTable + @"
                               WHERE UPPER(TRIM(LOGIN_ID)) = UPPER(TRIM(:loginId))
                                 AND USR_ID <> :excludeUserId";
                        cmd.Parameters.Add("loginId", OracleDbType.Varchar2).Value = loginId.Trim();
                        cmd.Parameters.Add("excludeUserId", OracleDbType.Int64).Value = excludeUserId;
                        object result = cmd.ExecuteScalar();
                        return result != null && Convert.ToInt32(result) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "LoginIdExists failed: " + SafeError(ex));
                return false;
            }
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

        public int DeletePcMap(string siteCode, string pcName)
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

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"DELETE FROM " + PcMapTable + @"
                               WHERE UPPER(TRIM(SITE_CD)) = UPPER(TRIM(:siteCd))
                                 AND UPPER(TRIM(PC_NM)) = UPPER(TRIM(:pcNm))";
                        cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = siteCode.Trim();
                        cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = pcName.Trim();
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("DIRECTORY", "DeletePcMap failed: " + SafeError(ex));
                return -1;
            }
        }

        private void EnsureUserTable(OracleConnection conn)
        {
            if (_userProbed)
                return;
            _userProbed = true;
            _hasUserTable = ProbeTable(conn, UserTable, "MSDWHTKD_USR");
        }

        private void EnsureUserAuthColumns(OracleConnection conn)
        {
            if (_userAuthProbed)
                return;
            _userAuthProbed = true;
            if (!_hasUserTable)
                return;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT LOGIN_ID, PASSWORD_HASH, IS_ACTIVE FROM " + UserTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasUserAuthColumns = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasUserAuthColumns = false;
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "MSDWHTKD_USR auth columns missing; password login limited. Apply sql/20_MSDWHTKD_USR_AUTH.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        private void EnsureUserDeletedColumn(OracleConnection conn)
        {
            if (_userDeletedProbed)
                return;
            _userDeletedProbed = true;
            if (!_hasUserTable)
                return;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT IS_DELETED FROM " + UserTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasUserDeletedColumn = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasUserDeletedColumn = false;
                    WorkHubFileLogger.Warn("DIRECTORY",
                        "MSDWHTKD_USR.IS_DELETED missing; account soft-delete disabled. Apply sql/30_MSDWHTKD_USR_SOFT_DELETE.sql when DBA can.");
                    return;
                }
                throw;
            }
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
