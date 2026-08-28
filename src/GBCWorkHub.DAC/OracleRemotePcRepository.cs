using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using GBCWorkHub.DTO;
using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>
    /// XSUP.MSDWHTKD Oracle Repository
    /// </summary>
    public class OracleRemotePcRepository : IRemotePcRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string TableName = "XSUP.MSDWHTKD";
        private const string LogTableName = "XSUP.MSDWHTKH";
        private const string LogSequenceName = "XSUP.SEQ_MSDWHTKH";

        private readonly string _connectionString;
        private bool _lastConnectionOk;
        private string _lastConnectionError;

        public OracleRemotePcRepository()
        {
            _connectionString = ResolveConnectionString();
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_connectionString) && !LooksLikePlaceholder(_connectionString); }
        }

        public bool LastConnectionOk
        {
            get { return _lastConnectionOk; }
        }

        public string LastConnectionError
        {
            get { return _lastConnectionError; }
        }

        public Task<bool> TestConnectionAsync()
        {
            return Task.Run(() => TestConnectionCore());
        }

        public Task<IList<RemotePcStatus>> GetAllAsync()
        {
            return Task.Run(() => (IList<RemotePcStatus>)GetAllCore());
        }

        public Task<RemotePcStatus> GetByRemoteIpAsync(string remoteIp)
        {
            return Task.Run(() => GetByRemoteIpCore(remoteIp));
        }

        public Task<bool> TryReserveAsync(RemotePcReserveParams parameters)
        {
            return Task.Run(() => TryReserveCore(parameters));
        }

        public Task<bool> ConfirmConnectionAsync(string remoteIp, string sessionToken)
        {
            return Task.Run(() => ConfirmConnectionCore(remoteIp, sessionToken));
        }

        public Task<bool> ReleaseAsync(string remoteIp, string sessionToken)
        {
            return ReleaseAsync(remoteIp, sessionToken, "RELEASE");
        }

        public Task<bool> ReleaseAsync(string remoteIp, string sessionToken, string endSource)
        {
            return Task.Run(() => ReleaseCore(remoteIp, sessionToken, endSource));
        }

        public Task<bool> MarkCheckRequiredAsync(string remoteIp, string sessionToken)
        {
            return Task.Run(() => MarkCheckRequiredCore(remoteIp, sessionToken));
        }

        public Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsAsync(string remoteIp, int take)
        {
            return Task.Run(() => (IList<RemotePcUsageLogDto>)GetRecentUsageLogsCore(remoteIp, take));
        }

        private bool TestConnectionCore()
        {
            if (!EnsureConfigured())
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM DUAL";
                    cmd.ExecuteScalar();
                }

                _lastConnectionOk = true;
                _lastConnectionError = null;
                WorkHubFileLogger.Info("DB_CONNECTION_SUCCESS", "Oracle 연결 성공");
                return true;
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("DB_CONNECTION_FAILED", _lastConnectionError);
                return false;
            }
        }

        private List<RemotePcStatus> GetAllCore()
        {
            var list = new List<RemotePcStatus>();
            if (!EnsureConfigured())
                return list;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT ACCS_IP_ADDR, ACCS_STS_CD, REMOTE_ACCS_IP_ADDR, REMOTE_ACCS_DTM,
                                 REMOTE_PC_NM, ACCS_USER_ID, ACCS_PC_NM, SESSION_TOKEN,
                                 ACCS_STRT_DTM, LAST_HRTBT_DTM, UPDT_DTM, SITE_CD
                            FROM " + TableName + @"
                           ORDER BY SITE_CD NULLS LAST, REMOTE_PC_NM, REMOTE_ACCS_IP_ADDR";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            list.Add(MapRow(reader));
                    }
                }

                _lastConnectionOk = true;
                WorkHubFileLogger.Info("DB_SELECT_SUCCESS", "rows=" + list.Count);
                WorkHubFileLogger.Info("DB_POLL_SUCCESS", "rows=" + list.Count);
                return list;
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("DB_SELECT_FAILED", _lastConnectionError);
                WorkHubFileLogger.Error("DB_POLL_FAILED", _lastConnectionError);
                return list;
            }
        }

        private RemotePcStatus GetByRemoteIpCore(string remoteIp)
        {
            if (!EnsureConfigured() || string.IsNullOrWhiteSpace(remoteIp))
                return null;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT ACCS_IP_ADDR, ACCS_STS_CD, REMOTE_ACCS_IP_ADDR, REMOTE_ACCS_DTM,
                                 REMOTE_PC_NM, ACCS_USER_ID, ACCS_PC_NM, SESSION_TOKEN,
                                 ACCS_STRT_DTM, LAST_HRTBT_DTM, UPDT_DTM, SITE_CD
                            FROM " + TableName + @"
                           WHERE REMOTE_ACCS_IP_ADDR = :remoteIp";
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = remoteIp.Trim();

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            _lastConnectionOk = true;
                            return MapRow(reader);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("DB_SELECT_FAILED", "GetByRemoteIp " + _lastConnectionError);
                return null;
            }
        }

        private bool TryReserveCore(RemotePcReserveParams p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.RemoteAccessIpAddress) || string.IsNullOrWhiteSpace(p.SessionToken))
            {
                WorkHubFileLogger.Warn("RESERVE_REJECTED", LogCtx(p != null ? p.RemoteAccessIpAddress : null, null, p != null ? p.SessionToken : null, p != null ? p.AccessUserId : null, p != null ? p.AccessPcName : null, RemotePcDbStatuses.Available, RemotePcDbStatuses.InUse, 0, "잘못된 선점 파라미터"));
                return false;
            }

            if (!EnsureConfigured())
            {
                WorkHubFileLogger.Warn("RESERVE_REJECTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, null, RemotePcDbStatuses.InUse, 0, "DB 미설정"));
                return false;
            }

            WorkHubFileLogger.Info("RESERVE_REQUESTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, RemotePcDbStatuses.Available, RemotePcDbStatuses.InUse, 0, null));

            try
            {
                using (var conn = OpenConnection())
                using (var tx = conn.BeginTransaction())
                {
                    int rows;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            @"UPDATE " + TableName + @"
                                 SET ACCS_IP_ADDR = :accessIp,
                                     ACCS_STS_CD = :inUse,
                                     ACCS_USER_ID = :accessUserId,
                                     ACCS_PC_NM = :accessPcName,
                                     SESSION_TOKEN = :sessionToken,
                                     ACCS_STRT_DTM = SYSTIMESTAMP,
                                     REMOTE_ACCS_DTM = SYSTIMESTAMP,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                                 AND ACCS_STS_CD = :available";
                        cmd.Parameters.Add("accessIp", OracleDbType.Varchar2).Value = (object)p.AccessIpAddress ?? DBNull.Value;
                        cmd.Parameters.Add("inUse", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                        cmd.Parameters.Add("accessUserId", OracleDbType.Varchar2).Value = p.AccessUserId ?? string.Empty;
                        cmd.Parameters.Add("accessPcName", OracleDbType.Varchar2).Value = p.AccessPcName ?? string.Empty;
                        cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = p.SessionToken;
                        cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = p.RemoteAccessIpAddress.Trim();
                        cmd.Parameters.Add("available", OracleDbType.Varchar2).Value = RemotePcDbStatuses.Available;
                        rows = cmd.ExecuteNonQuery();
                    }

                    if (rows == 1)
                    {
                        tx.Commit();
                        _lastConnectionOk = true;
                        WorkHubFileLogger.Info("RESERVE_SUCCESS", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, RemotePcDbStatuses.Available, RemotePcDbStatuses.InUse, rows, null));
                        TryInsertUsageLog(p);
                        return true;
                    }

                    tx.Rollback();

                    // rows=0: 행 없음 / 이미 IN_USE — 원인을 로그에 구분
                    var existing = GetByRemoteIpCore(p.RemoteAccessIpAddress);
                    string rejectReason = existing == null
                        ? "DB 행 없음"
                        : ("상태=" + (existing.AccessStatusCode ?? "?")
                           + " 소유=" + (existing.AccessUserId ?? "-"));
                    WorkHubFileLogger.Warn("RESERVE_REJECTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, existing != null ? existing.AccessStatusCode : null, RemotePcDbStatuses.InUse, rows, rejectReason));
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("RESERVE_REJECTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, null, RemotePcDbStatuses.InUse, 0, _lastConnectionError));
                return false;
            }
        }

        private bool ConfirmConnectionCore(string remoteIp, string sessionToken)
        {
            if (!EnsureConfigured() || string.IsNullOrWhiteSpace(remoteIp) || string.IsNullOrWhiteSpace(sessionToken))
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    // Reserve가 이미 IN_USE로 올릴 수 있음 — CONNECTING/IN_USE 모두 REMOTE_ACCS_DTM 갱신
                    cmd.CommandText =
                        @"UPDATE " + TableName + @"
                             SET ACCS_STS_CD = :inUse,
                                 REMOTE_ACCS_DTM = SYSTIMESTAMP,
                                 UPDT_DTM = SYSTIMESTAMP
                           WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                             AND SESSION_TOKEN = :sessionToken
                             AND ACCS_STS_CD IN (:connecting, :inUse2)";
                    cmd.Parameters.Add("inUse", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = remoteIp.Trim();
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                    cmd.Parameters.Add("connecting", OracleDbType.Varchar2).Value = RemotePcDbStatuses.Connecting;
                    cmd.Parameters.Add("inUse2", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    int rows = cmd.ExecuteNonQuery();

                    if (rows == 1)
                    {
                        _lastConnectionOk = true;
                        WorkHubFileLogger.Info("REMOTE_CONNECTION_CONFIRMED", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.InUse, rows, null));
                        TryConfirmUsageLog(sessionToken);
                        return true;
                    }

                    WorkHubFileLogger.Warn("REMOTE_CONNECTION_CONFIRMED", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.InUse, rows, "세션 불일치 또는 이미 변경됨"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("REMOTE_CONNECTION_CONFIRMED", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.InUse, 0, _lastConnectionError));
                return false;
            }
        }

        private bool ReleaseCore(string remoteIp, string sessionToken, string endSource)
        {
            if (!EnsureConfigured() || string.IsNullOrWhiteSpace(remoteIp) || string.IsNullOrWhiteSpace(sessionToken))
                return false;

            WorkHubFileLogger.Info("RELEASE_REQUESTED", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.Available, 0, null));

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"UPDATE " + TableName + @"
                             SET ACCS_IP_ADDR = NULL,
                                 ACCS_STS_CD = :available,
                                 ACCS_USER_ID = NULL,
                                 ACCS_PC_NM = NULL,
                                 SESSION_TOKEN = NULL,
                                 ACCS_STRT_DTM = NULL,
                                 REMOTE_ACCS_DTM = NULL,
                                 LAST_HRTBT_DTM = NULL,
                                 UPDT_DTM = SYSTIMESTAMP
                           WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                             AND SESSION_TOKEN = :sessionToken";
                    cmd.Parameters.Add("available", OracleDbType.Varchar2).Value = RemotePcDbStatuses.Available;
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = remoteIp.Trim();
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                    int rows = cmd.ExecuteNonQuery();

                    if (rows == 1)
                    {
                        _lastConnectionOk = true;
                        WorkHubFileLogger.Info("RELEASE_SUCCESS", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.Available, rows, null));
                        TryEndUsageLog(sessionToken, string.IsNullOrWhiteSpace(endSource) ? "RELEASE" : endSource.Trim());
                        return true;
                    }

                    WorkHubFileLogger.Warn("RELEASE_REJECTED", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.Available, rows, "세션 불일치"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("RELEASE_REJECTED", LogCtx(remoteIp, null, sessionToken, null, null, null, RemotePcDbStatuses.Available, 0, _lastConnectionError));
                return false;
            }
        }

        /// <summary>선점 성공 후 접속 이력 INSERT. 실패해도 선점은 유지.</summary>
        private void TryInsertUsageLog(RemotePcReserveParams p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.SessionToken))
                return;

            try
            {
                string remotePcNm = p.RemotePcName;
                if (string.IsNullOrWhiteSpace(remotePcNm))
                    remotePcNm = LookupRemotePcName(p.RemoteAccessIpAddress);

                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"INSERT INTO " + LogTableName + @"
                            (LOG_ID, SESSION_TOKEN, REMOTE_ACCS_IP_ADDR, REMOTE_PC_NM,
                             ACCS_USER_ID, ACCS_PC_NM, ACCS_IP_ADDR, SESSION_STATUS,
                             REQUESTED_AT, CREATED_AT, UPDT_DTM)
                          VALUES
                            (" + LogSequenceName + @".NEXTVAL, :sessionToken, :remoteIp, :remotePcNm,
                             :accessUserId, :accessPcName, :accessIp, :status,
                             SYSTIMESTAMP, SYSTIMESTAMP, SYSTIMESTAMP)";
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = p.SessionToken;
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = (p.RemoteAccessIpAddress ?? string.Empty).Trim();
                    cmd.Parameters.Add("remotePcNm", OracleDbType.Varchar2).Value = (object)remotePcNm ?? DBNull.Value;
                    cmd.Parameters.Add("accessUserId", OracleDbType.Varchar2).Value = p.AccessUserId ?? string.Empty;
                    cmd.Parameters.Add("accessPcName", OracleDbType.Varchar2).Value = (object)p.AccessPcName ?? DBNull.Value;
                    cmd.Parameters.Add("accessIp", OracleDbType.Varchar2).Value = (object)p.AccessIpAddress ?? DBNull.Value;
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    cmd.ExecuteNonQuery();
                }

                WorkHubFileLogger.Info("USAGE_LOG_INSERT",
                    "token=" + TruncToken(p.SessionToken) + " remoteIp=" + p.RemoteAccessIpAddress);
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("USAGE_LOG_INSERT", "failed: " + ex.Message);
            }
        }

        private void TryConfirmUsageLog(string sessionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionToken))
                return;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"UPDATE " + LogTableName + @"
                             SET SESSION_STATUS = :status,
                                 CONFIRMED_AT = SYSTIMESTAMP,
                                 UPDT_DTM = SYSTIMESTAMP
                           WHERE SESSION_TOKEN = :sessionToken
                             AND ENDED_AT IS NULL";
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                    int rows = cmd.ExecuteNonQuery();
                    WorkHubFileLogger.Info("USAGE_LOG_CONFIRM",
                        "token=" + TruncToken(sessionToken) + " rows=" + rows);
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("USAGE_LOG_CONFIRM", "failed: " + ex.Message);
            }
        }

        private void TryEndUsageLog(string sessionToken, string endSource)
        {
            if (string.IsNullOrWhiteSpace(sessionToken))
                return;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"UPDATE " + LogTableName + @"
                             SET SESSION_STATUS = :status,
                                 ENDED_AT = SYSTIMESTAMP,
                                 END_SOURCE = :endSource,
                                 UPDT_DTM = SYSTIMESTAMP
                           WHERE SESSION_TOKEN = :sessionToken
                             AND ENDED_AT IS NULL";
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = "ENDED";
                    cmd.Parameters.Add("endSource", OracleDbType.Varchar2).Value = endSource ?? "RELEASE";
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                    int rows = cmd.ExecuteNonQuery();
                    WorkHubFileLogger.Info("USAGE_LOG_END",
                        "token=" + TruncToken(sessionToken) + " rows=" + rows + " source=" + endSource);
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("USAGE_LOG_END", "failed: " + ex.Message);
            }
        }

        private string LookupRemotePcName(string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
                return null;
            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT REMOTE_PC_NM FROM " + TableName + @"
                           WHERE REMOTE_ACCS_IP_ADDR = :remoteIp";
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = remoteIp.Trim();
                    object val = cmd.ExecuteScalar();
                    return val == null || val == DBNull.Value ? null : Convert.ToString(val);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string TruncToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "-";
            return token.Length <= 8 ? token : token.Substring(0, 8);
        }

        private bool MarkCheckRequiredCore(string remoteIp, string sessionToken)
        {
            if (!EnsureConfigured() || string.IsNullOrWhiteSpace(remoteIp) || string.IsNullOrWhiteSpace(sessionToken))
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"UPDATE " + TableName + @"
                             SET ACCS_STS_CD = :checkRequired,
                                 UPDT_DTM = SYSTIMESTAMP
                           WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                             AND SESSION_TOKEN = :sessionToken";
                    cmd.Parameters.Add("checkRequired", OracleDbType.Varchar2).Value = RemotePcDbStatuses.CheckRequired;
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = remoteIp.Trim();
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                    int rows = cmd.ExecuteNonQuery();
                    return rows == 1;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                return false;
            }
        }

        private List<RemotePcUsageLogDto> GetRecentUsageLogsCore(string remoteIp, int take)
        {
            var list = new List<RemotePcUsageLogDto>();
            if (!EnsureConfigured() || string.IsNullOrWhiteSpace(remoteIp))
                return list;

            if (take <= 0)
                take = 3;
            if (take > 50)
                take = 50;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT * FROM (
                              SELECT LOG_ID, SESSION_TOKEN, REMOTE_ACCS_IP_ADDR, REMOTE_PC_NM,
                                     ACCS_USER_ID, ACCS_PC_NM, ACCS_IP_ADDR, SESSION_STATUS,
                                     REQUESTED_AT, CONFIRMED_AT, ENDED_AT, END_SOURCE
                                FROM " + LogTableName + @"
                               WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                               ORDER BY REQUESTED_AT DESC NULLS LAST, LOG_ID DESC
                          ) WHERE ROWNUM <= :take";
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = remoteIp.Trim();
                    cmd.Parameters.Add("take", OracleDbType.Int32).Value = take;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new RemotePcUsageLogDto
                            {
                                LogId = Convert.ToInt64(reader.GetValue(0)),
                                SessionToken = ReadString(reader, 1),
                                RemoteAccessIpAddress = ReadString(reader, 2),
                                RemotePcName = ReadString(reader, 3),
                                AccessUserId = ReadString(reader, 4),
                                AccessPcName = ReadString(reader, 5),
                                AccessIpAddress = ReadString(reader, 6),
                                SessionStatus = ReadString(reader, 7),
                                RequestedAt = ReadTimestamp(reader, 8),
                                ConfirmedAt = ReadTimestamp(reader, 9),
                                EndedAt = ReadTimestamp(reader, 10),
                                EndSource = ReadString(reader, 11)
                            });
                        }
                    }
                }

                _lastConnectionOk = true;
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_SELECT", "failed: " + ex.Message);
            }

            return list;
        }

        private static RemotePcStatus MapRow(IDataRecord reader)
        {
            return new RemotePcStatus
            {
                AccessIpAddress = ReadString(reader, 0),
                AccessStatusCode = ReadString(reader, 1),
                RemoteAccessIpAddress = ReadString(reader, 2),
                RemoteAccessDateTime = ReadTimestamp(reader, 3),
                RemotePcName = ReadString(reader, 4),
                AccessUserId = ReadString(reader, 5),
                AccessPcName = ReadString(reader, 6),
                SessionToken = ReadString(reader, 7),
                AccessStartDateTime = ReadTimestamp(reader, 8),
                LastHeartbeatDateTime = ReadTimestamp(reader, 9),
                UpdatedDateTime = ReadTimestamp(reader, 10),
                SiteCode = reader.FieldCount > 11 ? ReadString(reader, 11) : null
            };
        }

        private bool EnsureConfigured()
        {
            if (IsConfigured)
                return true;

            _lastConnectionOk = false;
            _lastConnectionError = "GbcWorkHubDb connection string 미설정";
            return false;
        }

        private OracleConnection OpenConnection()
        {
            var conn = new OracleConnection(_connectionString);
            conn.Open();
            _lastConnectionOk = true;
            _lastConnectionError = null;
            return conn;
        }

        private void FailConnection(Exception ex)
        {
            _lastConnectionOk = false;
            _lastConnectionError = SafeError(ex);
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
                || upper.Contains("사용자가 설정");
        }

        private static string ReadString(IDataRecord reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));
        }

        private static DateTime? ReadTimestamp(IDataRecord reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;
            object v = reader.GetValue(ordinal);
            if (v is DateTime)
                return (DateTime)v;
            DateTime dt;
            if (DateTime.TryParse(Convert.ToString(v), out dt))
                return dt;
            return null;
        }

        private static string SafeError(Exception ex)
        {
            if (ex == null)
                return "unknown";
            string msg = ex.Message ?? string.Empty;
            if (msg.IndexOf("Password=", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("User Id=", StringComparison.OrdinalIgnoreCase) >= 0)
                return ex.GetType().Name + " (자격증명 포함 가능 — 상세 생략)";
            if (msg.Length > 400)
                msg = msg.Substring(0, 400);
            return ex.GetType().Name + ": " + msg;
        }

        private static string LogCtx(
            string remoteIp,
            string remotePcName,
            string sessionToken,
            string userId,
            string clientPc,
            string before,
            string after,
            int rows,
            string error)
        {
            return "RemoteIp=" + (remoteIp ?? "-")
                + " RemotePcName=" + (remotePcName ?? "-")
                + " SessionToken=" + (sessionToken ?? "-")
                + " UserId=" + (userId ?? "-")
                + " ClientPc=" + (clientPc ?? "-")
                + " Before=" + (before ?? "-")
                + " After=" + (after ?? "-")
                + " Rows=" + rows
                + (string.IsNullOrEmpty(error) ? string.Empty : " Error=" + error);
        }
    }
}
