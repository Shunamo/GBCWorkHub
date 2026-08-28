using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using GBCWorkHub.DTO;
using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>
    /// XSUP.MSDWHTFS — TFS 업무기록 (MSDWHTKD와 분리)
    /// </summary>
    public class OracleTfsWorkLogRepository : ITfsWorkLogRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string TableName = "XSUP.MSDWHTFS";

        private readonly string _connectionString;
        private bool _lastConnectionOk;
        private string _lastConnectionError;

        public OracleTfsWorkLogRepository()
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

        public Task<bool> ExistsAsync(string collectionUrl, int changesetId)
        {
            return Task.Run(() => ExistsCore(collectionUrl, changesetId));
        }

        public Task<bool> SaveAsync(TfsWorkLogRecord record)
        {
            return Task.Run(() => SaveCore(record));
        }

        public Task<IList<bool>> SaveManyAsync(IList<TfsWorkLogRecord> records)
        {
            return Task.Run(() => SaveManyCore(records));
        }

        private bool ExistsCore(string collectionUrl, int changesetId)
        {
            if (!EnsureConfigured())
                return false;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT COUNT(1)
                            FROM " + TableName + @"
                           WHERE COLLECTION_URL = :collectionUrl
                             AND CHANGESET_ID = :changesetId";
                    cmd.Parameters.Add("collectionUrl", OracleDbType.Varchar2).Value = collectionUrl ?? string.Empty;
                    cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = changesetId;
                    object scalar = cmd.ExecuteScalar();
                    int count = scalar == null || scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
                    _lastConnectionOk = true;
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Error("TFS_SAVE_FAILED", "Exists " + _lastConnectionError);
                return false;
            }
        }

        private bool SaveCore(TfsWorkLogRecord record)
        {
            if (record == null)
                return false;
            if (!EnsureConfigured())
            {
                WorkHubFileLogger.Error("TFS_SAVE_FAILED", "DB 미설정 — 메모리 저장으로 위장하지 않음");
                return false;
            }

            try
            {
                using (var conn = OpenConnection())
                {
                    if (ExistsInConnection(conn, record.CollectionUrl, record.ChangesetId))
                    {
                        WorkHubFileLogger.Info("TFS_SAVE_DUPLICATE",
                            "CollectionUrl=" + (record.CollectionUrl ?? "-")
                            + " ChangesetId=" + record.ChangesetId
                            + " duplicate=true");
                        return UpdateCore(conn, record);
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            @"INSERT INTO " + TableName + @" (
                                  LOG_ID, COLLECTION_URL, SERVER_PATH, CHANGESET_ID,
                                  AUTHOR_NAME, AUTHOR_ID, CHECKED_IN_AT, ORIGINAL_COMMENT,
                                  WORK_TITLE, WORK_CONTENT, NOTE,
                                  CHANGED_FILE_COUNT, CHANGED_FILE_SUMMARY,
                                  REMOTE_PC_NM, SOURCE_CLIENT_NM, QUERY_MODE,
                                  SESSION_START_AT, SESSION_END_AT, SESSION_TOKEN,
                                  CREATED_BY, CREATED_AT, UPDT_DTM
                              ) VALUES (
                                  SEQ_MSDWHTFS.NEXTVAL, :collectionUrl, :serverPath, :changesetId,
                                  :authorName, :authorId, :checkedInAt, :originalComment,
                                  :workTitle, :workContent, :note,
                                  :changedFileCount, :changedFileSummary,
                                  :remotePcNm, :sourceClientNm, :queryMode,
                                  :sessionStartAt, :sessionEndAt, :sessionToken,
                                  :createdBy, SYSTIMESTAMP, SYSTIMESTAMP
                              )";
                        BindRecord(cmd, record);
                        int rows = cmd.ExecuteNonQuery();
                        _lastConnectionOk = true;
                        WorkHubFileLogger.Info("TFS_SAVE_SUCCESS",
                            "CollectionUrl=" + (record.CollectionUrl ?? "-")
                            + " ChangesetId=" + record.ChangesetId
                            + " Rows=" + rows);
                        return rows == 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Error("TFS_SAVE_FAILED",
                    "ChangesetId=" + record.ChangesetId + " " + _lastConnectionError);
                return false;
            }
        }

        private IList<bool> SaveManyCore(IList<TfsWorkLogRecord> records)
        {
            var results = new List<bool>();
            if (records == null)
                return results;

            foreach (var r in records)
                results.Add(SaveCore(r));

            return results;
        }

        private bool UpdateCore(OracleConnection conn, TfsWorkLogRecord record)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    @"UPDATE " + TableName + @"
                         SET WORK_TITLE = :workTitle,
                             WORK_CONTENT = :workContent,
                             NOTE = :note,
                             CHANGED_FILE_SUMMARY = :changedFileSummary,
                             CHANGED_FILE_COUNT = :changedFileCount,
                             ORIGINAL_COMMENT = :originalComment,
                             AUTHOR_NAME = :authorName,
                             AUTHOR_ID = :authorId,
                             CHECKED_IN_AT = :checkedInAt,
                             UPDT_DTM = SYSTIMESTAMP
                       WHERE COLLECTION_URL = :collectionUrl
                         AND CHANGESET_ID = :changesetId";
                cmd.Parameters.Add("workTitle", OracleDbType.Varchar2).Value = (object)record.WorkTitle ?? DBNull.Value;
                cmd.Parameters.Add("workContent", OracleDbType.Varchar2).Value = (object)record.WorkContent ?? DBNull.Value;
                cmd.Parameters.Add("note", OracleDbType.Varchar2).Value = (object)record.Note ?? DBNull.Value;
                cmd.Parameters.Add("changedFileSummary", OracleDbType.Varchar2).Value = (object)record.ChangedFileSummary ?? DBNull.Value;
                cmd.Parameters.Add("changedFileCount", OracleDbType.Int32).Value = record.ChangedFileCount;
                cmd.Parameters.Add("originalComment", OracleDbType.Varchar2).Value = (object)record.OriginalComment ?? DBNull.Value;
                cmd.Parameters.Add("authorName", OracleDbType.Varchar2).Value = (object)record.AuthorName ?? DBNull.Value;
                cmd.Parameters.Add("authorId", OracleDbType.Varchar2).Value = (object)record.AuthorId ?? DBNull.Value;
                cmd.Parameters.Add("checkedInAt", OracleDbType.TimeStamp).Value =
                    record.CheckedInAt.HasValue ? (object)record.CheckedInAt.Value : DBNull.Value;
                cmd.Parameters.Add("collectionUrl", OracleDbType.Varchar2).Value = record.CollectionUrl ?? string.Empty;
                cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = record.ChangesetId;
                int rows = cmd.ExecuteNonQuery();
                return rows == 1;
            }
        }

        private static bool ExistsInConnection(OracleConnection conn, string collectionUrl, int changesetId)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT COUNT(1) FROM " + TableName + @"
                       WHERE COLLECTION_URL = :collectionUrl AND CHANGESET_ID = :changesetId";
                cmd.Parameters.Add("collectionUrl", OracleDbType.Varchar2).Value = collectionUrl ?? string.Empty;
                cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = changesetId;
                object scalar = cmd.ExecuteScalar();
                return scalar != null && scalar != DBNull.Value && Convert.ToInt32(scalar) > 0;
            }
        }

        private static void BindRecord(OracleCommand cmd, TfsWorkLogRecord record)
        {
            cmd.Parameters.Add("collectionUrl", OracleDbType.Varchar2).Value = record.CollectionUrl ?? string.Empty;
            cmd.Parameters.Add("serverPath", OracleDbType.Varchar2).Value = (object)record.ServerPath ?? DBNull.Value;
            cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = record.ChangesetId;
            cmd.Parameters.Add("authorName", OracleDbType.Varchar2).Value = (object)record.AuthorName ?? DBNull.Value;
            cmd.Parameters.Add("authorId", OracleDbType.Varchar2).Value = (object)record.AuthorId ?? DBNull.Value;
            cmd.Parameters.Add("checkedInAt", OracleDbType.TimeStamp).Value =
                record.CheckedInAt.HasValue ? (object)record.CheckedInAt.Value : DBNull.Value;
            cmd.Parameters.Add("originalComment", OracleDbType.Varchar2).Value = (object)record.OriginalComment ?? DBNull.Value;
            cmd.Parameters.Add("workTitle", OracleDbType.Varchar2).Value = (object)record.WorkTitle ?? DBNull.Value;
            cmd.Parameters.Add("workContent", OracleDbType.Clob).Value = (object)record.WorkContent ?? DBNull.Value;
            cmd.Parameters.Add("note", OracleDbType.Varchar2).Value = (object)record.Note ?? DBNull.Value;
            cmd.Parameters.Add("changedFileCount", OracleDbType.Int32).Value = record.ChangedFileCount;
            cmd.Parameters.Add("changedFileSummary", OracleDbType.Varchar2).Value = (object)record.ChangedFileSummary ?? DBNull.Value;
            cmd.Parameters.Add("remotePcNm", OracleDbType.Varchar2).Value = (object)record.RemoteComputerName ?? DBNull.Value;
            cmd.Parameters.Add("sourceClientNm", OracleDbType.Varchar2).Value = (object)record.SourceClientName ?? DBNull.Value;
            cmd.Parameters.Add("queryMode", OracleDbType.Varchar2).Value = (object)record.QueryMode ?? DBNull.Value;
            cmd.Parameters.Add("sessionStartAt", OracleDbType.TimeStamp).Value =
                record.SessionStartAt.HasValue ? (object)record.SessionStartAt.Value : DBNull.Value;
            cmd.Parameters.Add("sessionEndAt", OracleDbType.TimeStamp).Value =
                record.SessionEndAt.HasValue ? (object)record.SessionEndAt.Value : DBNull.Value;
            cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = (object)record.SessionToken ?? DBNull.Value;
            cmd.Parameters.Add("createdBy", OracleDbType.Varchar2).Value = (object)record.CreatedBy ?? DBNull.Value;
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

        private void Fail(Exception ex)
        {
            _lastConnectionOk = false;
            string msg = ex != null ? ex.Message : "unknown";
            if (msg.IndexOf("Password=", StringComparison.OrdinalIgnoreCase) >= 0)
                msg = ex.GetType().Name;
            if (msg.Length > 400)
                msg = msg.Substring(0, 400);
            _lastConnectionError = (ex != null ? ex.GetType().Name : "Exception") + ": " + msg;
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
            return upper.Contains("YOUR_") || upper.Contains("CHANGE_ME") || upper.Contains("TODO");
        }
    }
}
