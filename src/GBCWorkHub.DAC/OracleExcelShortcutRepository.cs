using System;
using System.Collections.Generic;
using System.Configuration;
using Oracle.ManagedDataAccess.Client;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    /// <summary>
    /// XSUP.MSDWHTKD_EXCEL_SHORTCUT Oracle Repository — "엑셀시트" 탭의 바로가기 목록.
    /// 운영팀 전체가 공유하는 링크 모음이라 모든 사용자가 같은 테이블을 읽고, 추가도 바로
    /// 그 자리에서 다른 사람 화면에 반영된다(다음 로드 시).
    /// </summary>
    public class OracleExcelShortcutRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string TableName = "XSUP.MSDWHTKD_EXCEL_SHORTCUT";
        private const string SequenceName = "XSUP.SEQ_MSDWHTKD_EXCEL_SHORTCUT";

        private readonly string _connectionString;
        private string _lastConnectionError;

        public OracleExcelShortcutRepository()
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

        public List<ExcelShortcutDto> GetAll()
        {
            var result = new List<ExcelShortcutDto>();
            if (!IsConfigured)
                return result;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER
                            FROM " + TableName + @"
                           ORDER BY SORT_ORDER, SHORTCUT_ID";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new ExcelShortcutDto
                            {
                                ShortcutId = reader.GetInt64(0),
                                Name = reader.GetString(1),
                                Url = reader.GetString(2),
                                IconKey = reader.IsDBNull(3) ? "ExcelBrandIcon" : reader.GetString(3),
                                SortOrder = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastConnectionError = SafeError(ex);
                WorkHubFileLogger.Error("EXCEL_SHORTCUT_READ", _lastConnectionError);
            }

            return result;
        }

        /// <summary>새 바로가기 추가. 실패 시 null이 아닌 에러 메시지를 반환.</summary>
        public string Insert(string name, string url, string iconKey, string createdBy)
        {
            if (!IsConfigured)
                return "DB 연결이 설정되어 있지 않습니다.";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                return "이름과 URL을 모두 입력해 주세요.";

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"INSERT INTO " + TableName + @"
                            (SHORTCUT_ID, NAME, URL, ICON_KEY, SORT_ORDER, CREATED_BY, CREATED_AT)
                          VALUES
                            (" + SequenceName + @".NEXTVAL, :name, :url, :iconKey,
                             (SELECT NVL(MAX(SORT_ORDER), 0) + 10 FROM " + TableName + @"),
                             :createdBy, SYSTIMESTAMP)";
                    cmd.Parameters.Add("name", OracleDbType.Varchar2).Value = Truncate(name.Trim(), 200);
                    cmd.Parameters.Add("url", OracleDbType.Varchar2).Value = Truncate(url.Trim(), 1000);
                    cmd.Parameters.Add("iconKey", OracleDbType.Varchar2).Value = string.IsNullOrWhiteSpace(iconKey) ? "ExcelBrandIcon" : iconKey;
                    cmd.Parameters.Add("createdBy", OracleDbType.Varchar2).Value = (object)Truncate(createdBy, 200) ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                }
                return null;
            }
            catch (Exception ex)
            {
                _lastConnectionError = SafeError(ex);
                WorkHubFileLogger.Error("EXCEL_SHORTCUT_INSERT", _lastConnectionError);
                return "저장에 실패했습니다: " + _lastConnectionError;
            }
        }

        /// <summary>이름/URL 수정. 실패 시 null이 아닌 에러 메시지를 반환.</summary>
        public string Update(long shortcutId, string name, string url)
        {
            if (!IsConfigured)
                return "DB 연결이 설정되어 있지 않습니다.";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                return "이름과 URL을 모두 입력해 주세요.";

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"UPDATE " + TableName + @"
                             SET NAME = :name, URL = :url
                           WHERE SHORTCUT_ID = :shortcutId";
                    cmd.Parameters.Add("name", OracleDbType.Varchar2).Value = Truncate(name.Trim(), 200);
                    cmd.Parameters.Add("url", OracleDbType.Varchar2).Value = Truncate(url.Trim(), 1000);
                    cmd.Parameters.Add("shortcutId", OracleDbType.Int64).Value = shortcutId;
                    cmd.ExecuteNonQuery();
                }
                return null;
            }
            catch (Exception ex)
            {
                _lastConnectionError = SafeError(ex);
                WorkHubFileLogger.Error("EXCEL_SHORTCUT_UPDATE", _lastConnectionError);
                return "수정에 실패했습니다: " + _lastConnectionError;
            }
        }

        /// <summary>바로가기 삭제. 실패 시 null이 아닌 에러 메시지를 반환.</summary>
        public string Delete(long shortcutId)
        {
            if (!IsConfigured)
                return "DB 연결이 설정되어 있지 않습니다.";

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM " + TableName + " WHERE SHORTCUT_ID = :shortcutId";
                    cmd.Parameters.Add("shortcutId", OracleDbType.Int64).Value = shortcutId;
                    cmd.ExecuteNonQuery();
                }
                return null;
            }
            catch (Exception ex)
            {
                _lastConnectionError = SafeError(ex);
                WorkHubFileLogger.Error("EXCEL_SHORTCUT_DELETE", _lastConnectionError);
                return "삭제에 실패했습니다: " + _lastConnectionError;
            }
        }

        private OracleConnection OpenConnection()
        {
            var conn = new OracleConnection(_connectionString);
            conn.Open();
            OracleKoreaSession.Apply(conn);
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
