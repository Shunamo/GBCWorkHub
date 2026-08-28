using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using GBCWorkHub.DTO;
using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>
    /// XSUP.MSDWHTKH_FILE Oracle Repository — one dev session's classified file results
    /// (CHECKED_IN / PENDING_CHANGED / SESSION_METADATA_CHANGED / ADDED / DELETED), keyed by
    /// SESSION_TOKEN (the same correlation key RemoteSessionController already holds; the
    /// numeric MSDWHTKH.LOG_ID is never fetched back into the app). Written once when the
    /// remote SessionAgent's GBCWORKHUB_SESSION_RESULT:: payload is received at disconnect.
    /// Never stores file content — paths, TFVC correlation, and classification metadata only.
    /// </summary>
    public class OracleDevSessionFileRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string TableName = "XSUP.MSDWHTKH_FILE";
        private const string SequenceName = "XSUP.SEQ_MSDWHTKH_FILE";

        private readonly string _connectionString;
        private string _lastConnectionError;

        public OracleDevSessionFileRepository()
        {
            _connectionString = ResolveConnectionString();
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_connectionString); }
        }

        public string LastConnectionError
        {
            get { return _lastConnectionError; }
        }

        /// <summary>Batch insert for one session's classified files. Best-effort per row.</summary>
        public int InsertSessionFiles(string sessionToken, IEnumerable<DevSessionFileDto> files)
        {
            if (!IsConfigured || files == null || string.IsNullOrWhiteSpace(sessionToken))
                return 0;

            int inserted = 0;
            try
            {
                using (var conn = OpenConnection())
                {
                    foreach (var f in files)
                    {
                        try
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText =
                                    @"INSERT INTO " + TableName + @"
                                        (FILE_ROW_ID, SESSION_TOKEN, FILE_PATH, CONTENT_CHANGED,
                                         CHANGE_KIND, TFVC_STATUS, CHANGESET_ID, CONFIDENCE, REASON,
                                         DIFF_AVAILABLE, DIFF_SUMMARY, CREATED_AT, UPDT_DTM)
                                      VALUES
                                        (" + SequenceName + @".NEXTVAL, :sessionToken, :filePath, 'Y',
                                         :changeKind, :tfvcStatus, :changesetId, :confidence, :reason,
                                         :diffAvailable, :diffSummary, SYSTIMESTAMP, SYSTIMESTAMP)";
                                cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                                cmd.Parameters.Add("filePath", OracleDbType.Varchar2).Value = Truncate(f.FilePath, 1000);
                                cmd.Parameters.Add("changeKind", OracleDbType.Varchar2).Value = (object)f.ChangeKind ?? DBNull.Value;
                                cmd.Parameters.Add("tfvcStatus", OracleDbType.Varchar2).Value = (object)f.TfvcStatus ?? DBNull.Value;
                                cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = f.ChangesetId.HasValue ? (object)f.ChangesetId.Value : DBNull.Value;
                                cmd.Parameters.Add("confidence", OracleDbType.Varchar2).Value = (object)f.Confidence ?? DBNull.Value;
                                cmd.Parameters.Add("reason", OracleDbType.Varchar2).Value = (object)Truncate(f.Reason, 500) ?? DBNull.Value;
                                cmd.Parameters.Add("diffAvailable", OracleDbType.Varchar2).Value = f.DiffAvailable ? "Y" : "N";
                                cmd.Parameters.Add("diffSummary", OracleDbType.Varchar2).Value = (object)Truncate(f.DiffSummary, 1000) ?? DBNull.Value;
                                cmd.ExecuteNonQuery();
                                inserted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            WorkHubFileLogger.Error("DEV_SESSION_FILE_INSERT", "token=" + sessionToken + " path=" + f.FilePath + " err=" + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastConnectionError = SafeError(ex);
                WorkHubFileLogger.Error("DEV_SESSION_FILE_INSERT", "token=" + sessionToken + " err=" + _lastConnectionError);
            }

            return inserted;
        }

        public List<DevSessionFileDto> GetSessionFiles(string sessionToken)
        {
            var result = new List<DevSessionFileDto>();
            if (!IsConfigured || string.IsNullOrWhiteSpace(sessionToken))
                return result;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT FILE_ROW_ID, SESSION_TOKEN, FILE_PATH, CHANGE_KIND, TFVC_STATUS,
                                 CHANGESET_ID, CONFIDENCE, REASON, DIFF_AVAILABLE, DIFF_SUMMARY
                            FROM " + TableName + @"
                           WHERE SESSION_TOKEN = :sessionToken
                           ORDER BY FILE_PATH";
                    cmd.Parameters.Add("sessionToken", OracleDbType.Varchar2).Value = sessionToken;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new DevSessionFileDto
                            {
                                FileRowId = reader.GetInt64(0),
                                SessionToken = reader.GetString(1),
                                FilePath = reader.GetString(2),
                                ChangeKind = reader.IsDBNull(3) ? null : reader.GetString(3),
                                TfvcStatus = reader.IsDBNull(4) ? null : reader.GetString(4),
                                ChangesetId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                                Confidence = reader.IsDBNull(6) ? null : reader.GetString(6),
                                Reason = reader.IsDBNull(7) ? null : reader.GetString(7),
                                DiffAvailable = !reader.IsDBNull(8) && reader.GetString(8) == "Y",
                                DiffSummary = reader.IsDBNull(9) ? null : reader.GetString(9)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastConnectionError = SafeError(ex);
                WorkHubFileLogger.Error("DEV_SESSION_FILE_READ", "token=" + sessionToken + " err=" + _lastConnectionError);
            }

            return result;
        }

        private OracleConnection OpenConnection()
        {
            var conn = new OracleConnection(_connectionString);
            conn.Open();
            return conn;
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

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;
            return value.Substring(0, maxLength);
        }

        private static string SafeError(Exception ex)
        {
            return ex == null ? null : ex.Message;
        }
    }
}
