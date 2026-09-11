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

        public Task<DateTime?> GetMaxUpdatedAtAsync()
        {
            return Task.Run(() => GetMaxUpdatedAtCore());
        }

        private DateTime? GetMaxUpdatedAtCore()
        {
            if (!EnsureConfigured())
                return null;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT MAX(UPDT_DTM) FROM " + TableName;
                    object result = cmd.ExecuteScalar();
                    _lastConnectionOk = true;
                    if (result == null || result == DBNull.Value)
                        return null;
                    return Convert.ToDateTime(result);
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("DB_MAXUPDT_FAILED", _lastConnectionError);
                return null;
            }
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

        public Task<RemotePcUsageLogDto> GetUsageLogByTokenAsync(string sessionToken)
        {
            return Task.Run(() => GetUsageLogByTokenCore(sessionToken));
        }

        public Task<IList<RemotePcUsageLogDto>> GetRecentEndedUsageLogsAsync(int take)
        {
            return Task.Run(() => (IList<RemotePcUsageLogDto>)GetRecentEndedUsageLogsCore(take));
        }

        public Task<IList<RemotePcUsageLogDto>> GetRecentUsageLogsForOccupantAsync(
            string occupancyName,
            string windowsAccount,
            string samAccount,
            string accessPcName,
            int take,
            DateTime? fromAt = null,
            DateTime? toAt = null)
        {
            return Task.Run(() => (IList<RemotePcUsageLogDto>)GetRecentUsageLogsForOccupantCore(
                occupancyName, windowsAccount, samAccount, accessPcName, take, fromAt, toAt));
        }

        public Task<int> RenameOccupantAsync(string oldName, string newName, string accessPcName)
        {
            return Task.Run(() => RenameOccupantCore(oldName, newName, accessPcName));
        }

        public Task<int> PurgeUsageLogsOlderThanMonthsAsync(int months)
        {
            return Task.Run(() => PurgeUsageLogsOlderThanMonthsCore(months));
        }

        public Task<IList<RemotePcUsageLogDto>> GetUsageLogsForAdminAsync(string search, int take)
        {
            return Task.Run(() => (IList<RemotePcUsageLogDto>)GetUsageLogsForAdminCore(search, take));
        }

        public Task<bool> UpdateUsageLogForAdminAsync(
            long logId,
            string accessUserId,
            string sessionStatus,
            DateTime? endedAt,
            string resultMessage)
        {
            return Task.Run(() => UpdateUsageLogForAdminCore(logId, accessUserId, sessionStatus, endedAt, resultMessage));
        }

        public Task<bool> DeleteUsageLogForAdminAsync(long logId)
        {
            return Task.Run(() => DeleteUsageLogForAdminCore(logId));
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
                        cmd.BindByName = true;
                        cmd.CommandText =
                            @"UPDATE " + TableName + @"
                                 SET ACCS_IP_ADDR = :accessIp,
                                     ACCS_STS_CD = :inUse,
                                     ACCS_USER_ID = :accessUserId,
                                     ACCS_PC_NM = :accessPcName,
                                     SESSION_TOKEN = :sessionToken,
                                     ACCS_STRT_DTM = :koreaNow,
                                     REMOTE_ACCS_DTM = :koreaNow,
                                     UPDT_DTM = :koreaNow
                               WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                                 AND ACCS_STS_CD = :available";
                        cmd.Parameters.Add("accessIp", OracleDbType.Varchar2).Value = (object)p.AccessIpAddress ?? DBNull.Value;
                        cmd.Parameters.Add("inUse", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                        cmd.Parameters.Add("accessUserId", OracleDbType.Varchar2).Value = p.AccessUserId ?? string.Empty;
                        cmd.Parameters.Add("accessPcName", OracleDbType.Varchar2).Value = p.AccessPcName ?? string.Empty;
                        cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = p.SessionToken;
                        AddKoreaNow(cmd, "koreaNow");
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

                    if (p.ForceTakeover)
                        return TryTakeoverCore(p);

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

        private bool TryTakeoverCore(RemotePcReserveParams p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.RemoteAccessIpAddress) || string.IsNullOrWhiteSpace(p.SessionToken))
                return false;

            WorkHubFileLogger.Info("TAKEOVER_REQUESTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, RemotePcDbStatuses.InUse, RemotePcDbStatuses.InUse, 0, null));

            try
            {
                using (var conn = OpenConnection())
                using (var tx = conn.BeginTransaction())
                {
                    string oldToken = null;
                    string oldStatus = null;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.BindByName = true;
                        cmd.CommandText =
                            @"SELECT SESSION_TOKEN, ACCS_STS_CD
                                FROM " + TableName + @"
                               WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                                 FOR UPDATE";
                        cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = p.RemoteAccessIpAddress.Trim();
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                tx.Rollback();
                                WorkHubFileLogger.Warn("TAKEOVER_REJECTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, null, RemotePcDbStatuses.InUse, 0, "DB 행 없음"));
                                return false;
                            }
                            oldToken = ReadString(reader, 0);
                            oldStatus = ReadString(reader, 1);
                        }
                    }

                    if (string.Equals(oldToken, p.SessionToken, StringComparison.Ordinal))
                    {
                        tx.Rollback();
                        return true;
                    }

                    if (string.Equals(oldStatus, RemotePcDbStatuses.Available, StringComparison.OrdinalIgnoreCase))
                    {
                        tx.Rollback();
                        return TryReserveCore(CloneReserveWithoutTakeover(p));
                    }

                    if (!string.IsNullOrWhiteSpace(oldToken))
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.BindByName = true;
                            cmd.CommandText =
                                @"UPDATE " + LogTableName + @"
                                     SET SESSION_STATUS = :ended,
                                         ENDED_AT = :koreaNow,
                                         END_SOURCE = :endSource,
                                         RESULT_MESSAGE = :notice,
                                         UPDT_DTM = :koreaNow
                                   WHERE SESSION_TOKEN = :oldToken
                                     AND ENDED_AT IS NULL";
                            cmd.Parameters.Add("ended", OracleDbType.Varchar2).Value = "ENDED";
                            AddKoreaNow(cmd, "koreaNow");
                            cmd.Parameters.Add("endSource", OracleDbType.Varchar2).Value = "TAKEOVER";
                            cmd.Parameters.Add("notice", OracleDbType.Varchar2).Value = (object)Truncate(p.TakeoverNotice, 1000) ?? DBNull.Value;
                            cmd.Parameters.Add("oldToken", OracleDbType.Varchar2).Value = oldToken;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    int rows;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.BindByName = true;
                        cmd.CommandText =
                            @"UPDATE " + TableName + @"
                                 SET ACCS_IP_ADDR = :accessIp,
                                     ACCS_STS_CD = :inUse,
                                     ACCS_USER_ID = :accessUserId,
                                     ACCS_PC_NM = :accessPcName,
                                     SESSION_TOKEN = :sessionToken,
                                     ACCS_STRT_DTM = :koreaNow,
                                     REMOTE_ACCS_DTM = :koreaNow,
                                     LAST_HRTBT_DTM = NULL,
                                     UPDT_DTM = :koreaNow
                               WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                                 AND ACCS_STS_CD IN (:inUse2, :connecting, :checkRequired)";
                        cmd.Parameters.Add("accessIp", OracleDbType.Varchar2).Value = (object)p.AccessIpAddress ?? DBNull.Value;
                        cmd.Parameters.Add("inUse", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                        cmd.Parameters.Add("accessUserId", OracleDbType.Varchar2).Value = p.AccessUserId ?? string.Empty;
                        cmd.Parameters.Add("accessPcName", OracleDbType.Varchar2).Value = p.AccessPcName ?? string.Empty;
                        cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = p.SessionToken;
                        AddKoreaNow(cmd, "koreaNow");
                        cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = p.RemoteAccessIpAddress.Trim();
                        cmd.Parameters.Add("inUse2", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                        cmd.Parameters.Add("connecting", OracleDbType.Varchar2).Value = RemotePcDbStatuses.Connecting;
                        cmd.Parameters.Add("checkRequired", OracleDbType.Varchar2).Value = RemotePcDbStatuses.CheckRequired;
                        rows = cmd.ExecuteNonQuery();
                    }

                    if (rows != 1)
                    {
                        tx.Rollback();
                        WorkHubFileLogger.Warn("TAKEOVER_REJECTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, oldStatus, RemotePcDbStatuses.InUse, rows, "상태 변경됨"));
                        return false;
                    }

                    tx.Commit();
                    _lastConnectionOk = true;
                    WorkHubFileLogger.Info("TAKEOVER_SUCCESS", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, oldStatus, RemotePcDbStatuses.InUse, rows, "old=" + TruncToken(oldToken)));
                    TryInsertUsageLog(p);
                    return true;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Error("TAKEOVER_REJECTED", LogCtx(p.RemoteAccessIpAddress, null, p.SessionToken, p.AccessUserId, p.AccessPcName, null, RemotePcDbStatuses.InUse, 0, _lastConnectionError));
                return false;
            }
        }

        private static RemotePcReserveParams CloneReserveWithoutTakeover(RemotePcReserveParams p)
        {
            return new RemotePcReserveParams
            {
                RemoteAccessIpAddress = p.RemoteAccessIpAddress,
                RemotePcName = p.RemotePcName,
                AccessIpAddress = p.AccessIpAddress,
                AccessUserId = p.AccessUserId,
                AccessPcName = p.AccessPcName,
                SessionToken = p.SessionToken
            };
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string trimmed = value.Trim();
            if (trimmed.Length <= max)
                return trimmed;
            return trimmed.Substring(0, max);
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
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"UPDATE " + TableName + @"
                             SET ACCS_STS_CD = :inUse,
                                 REMOTE_ACCS_DTM = :koreaNow,
                                 UPDT_DTM = :koreaNow
                           WHERE REMOTE_ACCS_IP_ADDR = :remoteIp
                             AND SESSION_TOKEN = :sessionToken
                             AND ACCS_STS_CD IN (:connecting, :inUse2)";
                    cmd.Parameters.Add("inUse", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    AddKoreaNow(cmd, "koreaNow");
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
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"INSERT INTO " + LogTableName + @"
                            (LOG_ID, SESSION_TOKEN, REMOTE_ACCS_IP_ADDR, REMOTE_PC_NM,
                             ACCS_USER_ID, ACCS_PC_NM, ACCS_IP_ADDR, SESSION_STATUS,
                             REQUESTED_AT, CREATED_AT, UPDT_DTM)
                          VALUES
                            (" + LogSequenceName + @".NEXTVAL, :sessionToken, :remoteIp, :remotePcNm,
                             :accessUserId, :accessPcName, :accessIp, :status,
                             :koreaNow, :koreaNow, :koreaNow)";
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = p.SessionToken;
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = (p.RemoteAccessIpAddress ?? string.Empty).Trim();
                    cmd.Parameters.Add("remotePcNm", OracleDbType.Varchar2).Value = (object)remotePcNm ?? DBNull.Value;
                    cmd.Parameters.Add("accessUserId", OracleDbType.Varchar2).Value = p.AccessUserId ?? string.Empty;
                    cmd.Parameters.Add("accessPcName", OracleDbType.Varchar2).Value = (object)p.AccessPcName ?? DBNull.Value;
                    cmd.Parameters.Add("accessIp", OracleDbType.Varchar2).Value = (object)p.AccessIpAddress ?? DBNull.Value;
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    AddKoreaNow(cmd, "koreaNow");
                    cmd.ExecuteNonQuery();
                }

                WorkHubFileLogger.Info("USAGE_LOG_INSERT",
                    "token=" + TruncToken(p.SessionToken) + " remoteIp=" + p.RemoteAccessIpAddress);
            }
            catch (Exception ex)
            {
                if (TryReopenUsageLog(p))
                    return;
                WorkHubFileLogger.Warn("USAGE_LOG_INSERT", "failed: " + ex.Message);
            }
        }

        /// <summary>
        /// SESSION_TOKEN UNIQUE. 같은 토큰으로 다시 선점하면 INSERT가 실패하고
        /// 점유는 IN_USE인데 이력은 ENDED로 남는다. 끝난 행을 다시 사용 중으로 연다.
        /// </summary>
        private bool TryReopenUsageLog(RemotePcReserveParams p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.SessionToken))
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"UPDATE " + LogTableName + @"
                             SET SESSION_STATUS = :status,
                                 ENDED_AT = NULL,
                                 END_SOURCE = NULL,
                                 ACCS_USER_ID = :accessUserId,
                                 ACCS_PC_NM = :accessPcName,
                                 ACCS_IP_ADDR = :accessIp,
                                 REMOTE_ACCS_IP_ADDR = :remoteIp,
                                 REMOTE_PC_NM = NVL(:remotePcNm, REMOTE_PC_NM),
                                 REQUESTED_AT = :koreaNow,
                                 CONFIRMED_AT = NULL,
                                 UPDT_DTM = :koreaNow
                           WHERE SESSION_TOKEN = :sessionToken
                             AND (ENDED_AT IS NOT NULL OR SESSION_STATUS = :ended)";
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    cmd.Parameters.Add("accessUserId", OracleDbType.Varchar2).Value = p.AccessUserId ?? string.Empty;
                    cmd.Parameters.Add("accessPcName", OracleDbType.Varchar2).Value = (object)p.AccessPcName ?? DBNull.Value;
                    cmd.Parameters.Add("accessIp", OracleDbType.Varchar2).Value = (object)p.AccessIpAddress ?? DBNull.Value;
                    cmd.Parameters.Add("remoteIp", OracleDbType.Varchar2).Value = (p.RemoteAccessIpAddress ?? string.Empty).Trim();
                    cmd.Parameters.Add("remotePcNm", OracleDbType.Varchar2).Value =
                        string.IsNullOrWhiteSpace(p.RemotePcName) ? (object)DBNull.Value : p.RemotePcName.Trim();
                    AddKoreaNow(cmd, "koreaNow");
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = p.SessionToken;
                    cmd.Parameters.Add("ended", OracleDbType.Varchar2).Value = "ENDED";
                    int rows = cmd.ExecuteNonQuery();
                    if (rows >= 1)
                    {
                        WorkHubFileLogger.Info("USAGE_LOG_REOPEN",
                            "token=" + TruncToken(p.SessionToken) + " remoteIp=" + p.RemoteAccessIpAddress);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("USAGE_LOG_REOPEN", "failed: " + ex.Message);
            }

            return false;
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
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"UPDATE " + LogTableName + @"
                             SET SESSION_STATUS = :status,
                                 CONFIRMED_AT = :koreaNow,
                                 UPDT_DTM = :koreaNow
                           WHERE SESSION_TOKEN = :sessionToken
                             AND ENDED_AT IS NULL";
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = RemotePcDbStatuses.InUse;
                    AddKoreaNow(cmd, "koreaNow");
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
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"UPDATE " + LogTableName + @"
                             SET SESSION_STATUS = :status,
                                 ENDED_AT = :koreaNow,
                                 END_SOURCE = :endSource,
                                 UPDT_DTM = :koreaNow
                           WHERE SESSION_TOKEN = :sessionToken
                             AND ENDED_AT IS NULL";
                    cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = "ENDED";
                    cmd.Parameters.Add("endSource", OracleDbType.Varchar2).Value = endSource ?? "RELEASE";
                    AddKoreaNow(cmd, "koreaNow");
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

        private RemotePcUsageLogDto GetUsageLogByTokenCore(string sessionToken)
        {
            if (!EnsureConfigured() || string.IsNullOrWhiteSpace(sessionToken))
                return null;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"SELECT LOG_ID, SESSION_TOKEN, REMOTE_ACCS_IP_ADDR, REMOTE_PC_NM,
                                 ACCS_USER_ID, ACCS_PC_NM, ACCS_IP_ADDR, SESSION_STATUS,
                                 REQUESTED_AT, CONFIRMED_AT, ENDED_AT, END_SOURCE, RESULT_MESSAGE
                            FROM " + LogTableName + @"
                           WHERE SESSION_TOKEN = :sessionToken";
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken.Trim();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;
                        _lastConnectionOk = true;
                        return new RemotePcUsageLogDto
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
                            EndSource = ReadString(reader, 11),
                            ResultMessage = ReadString(reader, 12)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_BY_TOKEN", "failed: " + _lastConnectionError);
                return null;
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

        private List<RemotePcUsageLogDto> GetRecentEndedUsageLogsCore(int take)
        {
            var list = new List<RemotePcUsageLogDto>();
            if (!EnsureConfigured())
                return list;

            if (take <= 0)
                take = 20;
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
                               WHERE ENDED_AT IS NOT NULL
                                 AND REQUESTED_AT IS NOT NULL
                               ORDER BY ENDED_AT DESC, LOG_ID DESC
                          ) WHERE ROWNUM <= :take";
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
                WorkHubFileLogger.Warn("USAGE_LOG_SELECT_ENDED", "failed: " + ex.Message);
            }

            return list;
        }

        private List<RemotePcUsageLogDto> GetRecentUsageLogsForOccupantCore(
            string occupancyName,
            string windowsAccount,
            string samAccount,
            string accessPcName,
            int take,
            DateTime? fromAt,
            DateTime? toAt)
        {
            var list = new List<RemotePcUsageLogDto>();
            if (!EnsureConfigured())
                return list;

            if (take <= 0)
                take = 20;
            if (take > 200)
                take = 200;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"SELECT * FROM (
                              SELECT h.LOG_ID, h.SESSION_TOKEN, h.REMOTE_ACCS_IP_ADDR, h.REMOTE_PC_NM,
                                     h.ACCS_USER_ID, h.ACCS_PC_NM, h.ACCS_IP_ADDR, h.SESSION_STATUS,
                                     h.REQUESTED_AT, h.CONFIRMED_AT, h.ENDED_AT, h.END_SOURCE,
                                     p.SITE_CD
                                FROM " + LogTableName + @" h
                                LEFT JOIN " + TableName + @" p
                                  ON p.REMOTE_ACCS_IP_ADDR = h.REMOTE_ACCS_IP_ADDR
                               WHERE (
                                     (:occ IS NOT NULL AND (
                                          UPPER(TRIM(h.ACCS_USER_ID)) = UPPER(:occ)
                                       OR (
                                              UPPER(TRIM(:occ)) IN (UPPER('ADMIN'), UPPER(N'관리자'))
                                          AND UPPER(TRIM(h.ACCS_USER_ID)) IN (UPPER('ADMIN'), UPPER(N'관리자'))
                                       )
                                     ))
                                  OR (
                                     :occ IS NULL AND (
                                          (:win IS NOT NULL AND UPPER(TRIM(h.ACCS_USER_ID)) = UPPER(:win))
                                       OR (:sam IS NOT NULL AND (
                                              UPPER(TRIM(h.ACCS_USER_ID)) = UPPER(:sam)
                                           OR UPPER(TRIM(h.ACCS_USER_ID)) LIKE '%\\' || UPPER(:sam)
                                          ))
                                       OR (:pc IS NOT NULL AND UPPER(TRIM(h.ACCS_PC_NM)) = UPPER(:pc))
                                     )
                                  )
                               )
                                 AND (:fromAt IS NULL OR h.REQUESTED_AT >= :fromAt)
                                 AND (:toAt IS NULL OR h.REQUESTED_AT <= :toAt)
                               ORDER BY h.REQUESTED_AT DESC NULLS LAST, h.LOG_ID DESC
                          ) WHERE ROWNUM <= :take";
                    cmd.Parameters.Add("occ", OracleDbType.Varchar2).Value = BindOptionalText(occupancyName);
                    cmd.Parameters.Add("win", OracleDbType.Varchar2).Value = BindOptionalText(windowsAccount);
                    cmd.Parameters.Add("sam", OracleDbType.Varchar2).Value = BindOptionalText(samAccount);
                    cmd.Parameters.Add("pc", OracleDbType.Varchar2).Value = BindOptionalText(accessPcName);
                    cmd.Parameters.Add("fromAt", OracleDbType.TimeStamp).Value = BindOptionalTime(fromAt);
                    cmd.Parameters.Add("toAt", OracleDbType.TimeStamp).Value = BindOptionalTime(toAt);
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
                                EndSource = ReadString(reader, 11),
                                SiteCode = reader.FieldCount > 12 ? ReadString(reader, 12) : null
                            });
                        }
                    }
                }

                _lastConnectionOk = true;
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_SELECT_MINE", "failed: " + ex.Message);
            }

            return list;
        }

        private int RenameOccupantCore(string oldName, string newName, string accessPcName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return 0;
            if (string.Equals(oldName.Trim(), newName.Trim(), StringComparison.Ordinal))
                return 0;
            if (!EnsureConfigured())
                return 0;

            try
            {
                using (var conn = OpenConnection())
                using (var tx = conn.BeginTransaction())
                {
                    int rows = 0;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.BindByName = true;
                        cmd.CommandText =
                            @"UPDATE " + TableName + @"
                                 SET ACCS_USER_ID = :newNm,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE UPPER(TRIM(ACCS_USER_ID)) = UPPER(:oldNm)
                                 AND (:pc IS NULL OR UPPER(TRIM(ACCS_PC_NM)) = UPPER(:pc))";
                        cmd.Parameters.Add("newNm", OracleDbType.Varchar2).Value = newName.Trim();
                        cmd.Parameters.Add("oldNm", OracleDbType.Varchar2).Value = oldName.Trim();
                        cmd.Parameters.Add("pc", OracleDbType.Varchar2).Value =
                            string.IsNullOrWhiteSpace(accessPcName) ? (object)DBNull.Value : accessPcName.Trim();
                        rows += cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.BindByName = true;
                        cmd.CommandText =
                            @"UPDATE " + LogTableName + @"
                                 SET ACCS_USER_ID = :newNm
                               WHERE UPPER(TRIM(ACCS_USER_ID)) = UPPER(:oldNm)
                                 AND (:pc IS NULL OR UPPER(TRIM(ACCS_PC_NM)) = UPPER(:pc))";
                        cmd.Parameters.Add("newNm", OracleDbType.Varchar2).Value = newName.Trim();
                        cmd.Parameters.Add("oldNm", OracleDbType.Varchar2).Value = oldName.Trim();
                        cmd.Parameters.Add("pc", OracleDbType.Varchar2).Value =
                            string.IsNullOrWhiteSpace(accessPcName) ? (object)DBNull.Value : accessPcName.Trim();
                        rows += cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                    _lastConnectionOk = true;
                    return rows;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("OCCUPANCY_RENAME", "RenameOccupant failed: " + _lastConnectionError);
                return -1;
            }
        }

        private int PurgeUsageLogsOlderThanMonthsCore(int months)
        {
            if (months <= 0)
                months = 1;
            if (!EnsureConfigured())
                return 0;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"DELETE FROM " + LogTableName + @"
                           WHERE REQUESTED_AT < ADD_MONTHS(SYSTIMESTAMP, 0 - :months)
                             AND (SESSION_STATUS IS NULL
                               OR UPPER(TRIM(SESSION_STATUS)) NOT IN ('IN_USE','CONNECTING'))";
                    cmd.Parameters.Add("months", OracleDbType.Int32).Value = months;
                    int rows = cmd.ExecuteNonQuery();
                    _lastConnectionOk = true;
                    if (rows > 0)
                        WorkHubFileLogger.Info("USAGE_LOG_PURGE", "deleted " + rows + " rows older than " + months + " month(s)");
                    return rows;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_PURGE", "failed: " + ex.Message);
                return 0;
            }
        }

        private List<RemotePcUsageLogDto> GetUsageLogsForAdminCore(string search, int take)
        {
            var list = new List<RemotePcUsageLogDto>();
            if (!EnsureConfigured())
                return list;

            if (take <= 0)
                take = 100;
            if (take > 500)
                take = 500;

            string q = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            string like = q == null ? null : ("%" + q.ToUpperInvariant() + "%");

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"SELECT * FROM (
                              SELECT h.LOG_ID, h.SESSION_TOKEN, h.REMOTE_ACCS_IP_ADDR, h.REMOTE_PC_NM,
                                     h.ACCS_USER_ID, h.ACCS_PC_NM, h.ACCS_IP_ADDR, h.SESSION_STATUS,
                                     h.REQUESTED_AT, h.CONFIRMED_AT, h.ENDED_AT, h.END_SOURCE,
                                     h.RESULT_MESSAGE, p.SITE_CD
                                FROM " + LogTableName + @" h
                                LEFT JOIN " + TableName + @" p
                                  ON p.REMOTE_ACCS_IP_ADDR = h.REMOTE_ACCS_IP_ADDR
                               WHERE (
                                     :q IS NULL
                                  OR UPPER(TRIM(h.ACCS_USER_ID)) LIKE :q
                                  OR UPPER(TRIM(h.REMOTE_PC_NM)) LIKE :q
                                  OR UPPER(TRIM(h.REMOTE_ACCS_IP_ADDR)) LIKE :q
                                  OR UPPER(TRIM(h.ACCS_PC_NM)) LIKE :q
                                  OR UPPER(TRIM(h.ACCS_IP_ADDR)) LIKE :q
                                  OR UPPER(TRIM(h.SESSION_STATUS)) LIKE :q
                                  OR UPPER(TRIM(p.SITE_CD)) LIKE :q
                               )
                               ORDER BY h.REQUESTED_AT DESC NULLS LAST, h.LOG_ID DESC
                          ) WHERE ROWNUM <= :take";
                    cmd.Parameters.Add("q", OracleDbType.Varchar2).Value = BindOptionalText(like);
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
                                EndSource = ReadString(reader, 11),
                                ResultMessage = ReadString(reader, 12),
                                SiteCode = reader.FieldCount > 13 ? ReadString(reader, 13) : null
                            });
                        }
                    }
                }

                _lastConnectionOk = true;
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_ADMIN_SELECT", "failed: " + ex.Message);
            }

            return list;
        }

        private bool UpdateUsageLogForAdminCore(
            long logId,
            string accessUserId,
            string sessionStatus,
            DateTime? endedAt,
            string resultMessage)
        {
            if (logId <= 0 || !EnsureConfigured())
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText =
                        @"UPDATE " + LogTableName + @"
                              SET ACCS_USER_ID = NVL(:userId, ACCS_USER_ID),
                                  SESSION_STATUS = NVL(:sts, SESSION_STATUS),
                                  ENDED_AT = NVL(:endedAt, ENDED_AT),
                                  RESULT_MESSAGE = NVL(:msg, RESULT_MESSAGE),
                                  UPDT_DTM = SYSTIMESTAMP
                            WHERE LOG_ID = :logId";
                    cmd.Parameters.Add("userId", OracleDbType.NVarchar2).Value = BindOptionalText(accessUserId);
                    cmd.Parameters.Add("sts", OracleDbType.Varchar2).Value = BindOptionalText(sessionStatus);
                    cmd.Parameters.Add("endedAt", OracleDbType.TimeStamp).Value = BindOptionalTime(endedAt);
                    cmd.Parameters.Add("msg", OracleDbType.NVarchar2).Value = BindOptionalText(resultMessage);
                    cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                    int n = cmd.ExecuteNonQuery();
                    _lastConnectionOk = true;
                    return n > 0;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_ADMIN_UPDATE", "failed: " + ex.Message);
                return false;
            }
        }

        private bool DeleteUsageLogForAdminCore(long logId)
        {
            if (logId <= 0 || !EnsureConfigured())
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.BindByName = true;
                    cmd.CommandText = @"DELETE FROM " + LogTableName + " WHERE LOG_ID = :logId";
                    cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                    int n = cmd.ExecuteNonQuery();
                    _lastConnectionOk = true;
                    return n > 0;
                }
            }
            catch (Exception ex)
            {
                FailConnection(ex);
                WorkHubFileLogger.Warn("USAGE_LOG_ADMIN_DELETE", "failed: " + ex.Message);
                return false;
            }
        }

        private static object BindOptionalText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DBNull.Value;
            return value.Trim();
        }

        private static object BindOptionalTime(DateTime? value)
        {
            if (!value.HasValue)
                return DBNull.Value;
            return value.Value;
        }

        private static void AddKoreaNow(OracleCommand cmd, string name)
        {
            cmd.Parameters.Add(name, OracleDbType.TimeStamp).Value = KoreaTime.Now;
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
            OracleKoreaSession.Apply(conn);
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
            DateTime dt;
            if (v is DateTime)
                dt = (DateTime)v;
            else if (!DateTime.TryParse(Convert.ToString(v), out dt))
                return null;
            return KoreaTime.ToKorea(dt);
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
