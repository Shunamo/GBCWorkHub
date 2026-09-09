using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>XSUP.MSDWHTKD_WRK / _PRJ / _SRC / _CS - main work log</summary>
    public class OracleWorkLogRepository : IWorkLogRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string HeaderTable = "XSUP.MSDWHTKD_WRK";
        private const string ProjectTable = "XSUP.MSDWHTKD_WRK_PRJ";
        private const string SourceTable = "XSUP.MSDWHTKD_WRK_SRC";
        private const string ChangesetTable = "XSUP.MSDWHTKD_WRK_CS";
        private const string HeaderSeq = "XSUP.SEQ_MSDWHTKD_WRK";
        private const string ProjectSeq = "XSUP.SEQ_MSDWHTKD_WRK_PRJ";
        private const string SourceSeq = "XSUP.SEQ_MSDWHTKD_WRK_SRC";
        private const string ChangesetSeq = "XSUP.SEQ_MSDWHTKD_WRK_CS";

        private readonly string _connectionString;
        private bool _lastConnectionOk;
        private string _lastConnectionError;
        private bool _teamNmProbed;
        private bool _hasTeamNm;
        private bool _usrIdProbed;
        private bool _hasUsrId;

        private const string HeaderSelectBase =
            @"LOG_ID, CLIENT_KEY, WRITE_STATUS, TICKET_NO, TICKET_CONTENTS,
              MENU_NM, PC_NM, PERSON_IN_CHARGE, LOCAL_PC_IP,
              START_DT, END_DT, DEPLOY_STATUS, DEPLOY_DT, WORK_COMMENT,
              CHANGESET_ID, TFS_COMMENT, TFS_AUTHOR, AUTHOR_NM, CHECKED_IN_AT,
              CHANGED_FILE_COUNT, NEEDS_TICKET_REVIEW, CREATED_AT, UPDT_DTM,
              SITE_CD";

        /// <summary>목록 그룹과 동일: 체크인 → 시작 → 종료 → 배포일. ALL은 사이트 구분 없이 이 순.</summary>
        private const string TimelineDateExpr =
            "NVL(w.CHECKED_IN_AT, NVL(w.START_DT, NVL(w.END_DT, w.DEPLOY_DT)))";

        public OracleWorkLogRepository()
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

        public Task<IList<WorkLogRecordDto>> GetAllAsync()
        {
            return Task.Run(() => (IList<WorkLogRecordDto>)GetAllCore());
        }

        public Task<WorkLogPageResult> GetPageAsync(WorkLogListQuery query)
        {
            return Task.Run(() => GetPageCore(query));
        }

        public Task<WorkLogRecordDto> GetByIdAsync(long logId)
        {
            return Task.Run(() => GetByIdCore(logId));
        }

        public Task<bool> SaveAsync(WorkLogRecordDto record)
        {
            return Task.Run(() => SaveCore(record));
        }

        public Task<bool> DeleteByIdAsync(long logId)
        {
            return Task.Run(() => DeleteByIdCore(logId));
        }

        public Task<int> DeleteAllAsync()
        {
            return Task.Run(() => DeleteAllCore());
        }

        public Task<ISet<int>> GetRegisteredChangesetIdsAsync()
        {
            return Task.Run(() => (ISet<int>)GetRegisteredChangesetIdsCore());
        }

        public Task<int> RenameAuthorAsync(string oldName, string newName, string localPcIp)
        {
            return Task.Run(() => RenameAuthorCore(oldName, newName, localPcIp));
        }

        public Task<int> RenameTeamAsync(string authorName, string teamName, string localPcIp)
        {
            return Task.Run(() => RenameTeamCore(authorName, teamName, localPcIp));
        }

        public Task<int> FillMissingTeamAsync(string authorName, string teamName, string localPcIp)
        {
            return Task.Run(() => FillMissingTeamCore(authorName, teamName, localPcIp));
        }

        public Task<IList<string>> GetDistinctTeamNamesAsync()
        {
            return Task.Run(() => (IList<string>)GetDistinctTeamNamesCore());
        }

        private int RenameAuthorCore(string oldName, string newName, string localPcIp)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return 0;
            if (string.Equals(oldName.Trim(), newName.Trim(), StringComparison.Ordinal))
                return 0;
            if (!IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    int rows = 0;
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + HeaderTable + @"
                                 SET AUTHOR_NM = :newNm,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE UPPER(TRIM(AUTHOR_NM)) = UPPER(:oldNm)
                                 AND (:ip IS NULL OR UPPER(TRIM(LOCAL_PC_IP)) = UPPER(:ip))";
                        cmd.Parameters.Add("newNm", OracleDbType.Varchar2).Value = Trim(newName.Trim(), 200);
                        cmd.Parameters.Add("oldNm", OracleDbType.Varchar2).Value = oldName.Trim();
                        cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                            string.IsNullOrWhiteSpace(localPcIp) ? (object)DBNull.Value : localPcIp.Trim();
                        rows += cmd.ExecuteNonQuery();
                    }
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + HeaderTable + @"
                                 SET PERSON_IN_CHARGE = :newNm,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE UPPER(TRIM(PERSON_IN_CHARGE)) = UPPER(:oldNm)
                                 AND (:ip IS NULL OR UPPER(TRIM(LOCAL_PC_IP)) = UPPER(:ip))";
                        cmd.Parameters.Add("newNm", OracleDbType.Varchar2).Value = Trim(newName.Trim(), 200);
                        cmd.Parameters.Add("oldNm", OracleDbType.Varchar2).Value = oldName.Trim();
                        cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                            string.IsNullOrWhiteSpace(localPcIp) ? (object)DBNull.Value : localPcIp.Trim();
                        rows += cmd.ExecuteNonQuery();
                    }
                    _lastConnectionOk = true;
                    _lastConnectionError = null;
                    return rows;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_RENAME", "RenameAuthor failed: " + _lastConnectionError);
                return -1;
            }
        }

        private int RenameTeamCore(string authorName, string teamName, string localPcIp)
        {
            if (string.IsNullOrWhiteSpace(authorName))
                return 0;
            if (!IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureTeamNmColumn(conn);
                    if (!_hasTeamNm)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                    cmd.CommandText =
                        @"UPDATE " + HeaderTable + @"
                             SET TEAM_NM = :teamNm,
                                 UPDT_DTM = SYSTIMESTAMP
                           WHERE UPPER(TRIM(AUTHOR_NM)) = UPPER(:authorNm)
                              OR UPPER(TRIM(PERSON_IN_CHARGE)) = UPPER(:authorNm)
                              OR (:hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(:ip))";
                    BindTeamUpdate(cmd, authorName, teamName, localPcIp);
                    int rows = cmd.ExecuteNonQuery();
                    _lastConnectionOk = true;
                    _lastConnectionError = null;
                    return rows;
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_RENAME", "RenameTeam failed: " + _lastConnectionError);
                return -1;
            }
        }

        private int FillMissingTeamCore(string authorName, string teamName, string localPcIp)
        {
            if (string.IsNullOrWhiteSpace(authorName) || string.IsNullOrWhiteSpace(teamName))
                return 0;
            if (!IsConfigured)
                return 0;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureTeamNmColumn(conn);
                    if (!_hasTeamNm)
                        return 0;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + HeaderTable + @"
                                 SET TEAM_NM = :teamNm,
                                     UPDT_DTM = SYSTIMESTAMP
                               WHERE (TEAM_NM IS NULL OR TRIM(TEAM_NM) IS NULL)
                                 AND (" + SqlAuthorMatchesName() + @"
                                   OR (:hasIp = 1 AND UPPER(TRIM(LOCAL_PC_IP)) = UPPER(:ip)))";
                        BindTeamUpdate(cmd, authorName, teamName, localPcIp);
                        int rows = cmd.ExecuteNonQuery();
                        _lastConnectionOk = true;
                        _lastConnectionError = null;
                        if (rows > 0)
                            WorkHubFileLogger.Info("WORKLOG_RENAME", "FillMissingTeam rows=" + rows);
                        return rows;
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_RENAME", "FillMissingTeam failed: " + _lastConnectionError);
                return -1;
            }
        }

        private static void BindTeamUpdate(OracleCommand cmd, string authorName, string teamName, string localPcIp)
        {
            cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value =
                string.IsNullOrWhiteSpace(teamName)
                    ? (object)DBNull.Value
                    : Trim(teamName.Trim(), 100);
            cmd.Parameters.Add("authorNm", OracleDbType.NVarchar2).Value = authorName.Trim();
            cmd.Parameters.Add("hasIp", OracleDbType.Int32).Value =
                string.IsNullOrWhiteSpace(localPcIp) ? 0 : 1;
            cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value =
                string.IsNullOrWhiteSpace(localPcIp) ? (object)"-" : localPcIp.Trim();
        }

        /// <summary>점유명 정확 일치, "김수현 진료지원" 접두, 작성자/담당자/TFS 작성자 포함.</summary>
        private static string SqlAuthorMatchesName()
        {
            return SqlTeamKeyExpr("NVL(TRIM(AUTHOR_NM), '')") + " = " + SqlTeamKeyExpr("TRIM(:authorNm)")
                + " OR " + SqlTeamKeyExpr("NVL(TRIM(AUTHOR_NM), '')")
                + " LIKE " + SqlTeamKeyExpr("TRIM(:authorNm)") + " || '%'"
                + " OR INSTR("
                + SqlTeamKeyExpr("NVL(TRIM(AUTHOR_NM), '') || NVL(TRIM(PERSON_IN_CHARGE), '') || NVL(TRIM(TFS_AUTHOR), '')")
                + ", " + SqlTeamKeyExpr("TRIM(:authorNm)") + ") > 0"
                + " OR UPPER(TRIM(PERSON_IN_CHARGE)) = UPPER(:authorNm)"
                + " OR UPPER(TRIM(TFS_AUTHOR)) = UPPER(:authorNm)";
        }

        private static string SqlTeamKeyCol(string column)
        {
            return SqlTeamKeyExpr("NVL(TRIM(" + column + "), '')");
        }

        private static string SqlTeamKeyExpr(string expr)
        {
            return "REPLACE(REPLACE(REPLACE(" + expr + ", ' ', ''), UNISTR('\\00B7'), ''), UNISTR('\\2022'), '')";
        }

        private List<string> GetDistinctTeamNamesCore()
        {
            var list = new List<string>();
            if (!IsConfigured)
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureTeamNmColumn(conn);
                    if (!_hasTeamNm)
                        return list;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT MIN(TRIM(TEAM_NM))
                                FROM " + HeaderTable + @"
                               WHERE TEAM_NM IS NOT NULL
                                 AND TRIM(TEAM_NM) IS NOT NULL
                               GROUP BY " + SqlTeamKeyCol("TEAM_NM") + @"
                               ORDER BY MIN(TRIM(TEAM_NM))";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (reader.IsDBNull(0))
                                    continue;
                                string name = Convert.ToString(reader.GetValue(0));
                                if (!string.IsNullOrWhiteSpace(name))
                                    list.Add(name.Trim());
                            }
                        }
                    }
                    _lastConnectionOk = true;
                    _lastConnectionError = null;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_SELECT", "GetDistinctTeamNames failed: " + _lastConnectionError);
            }

            return list;
        }

        private HashSet<int> GetRegisteredChangesetIdsCore()
        {
            var set = new HashSet<int>();
            if (!IsConfigured)
                return set;

            try
            {
                using (var conn = new OracleConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            "SELECT CHANGESET_ID FROM " + ChangesetTable + " WHERE CHANGESET_ID > 0";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (!reader.IsDBNull(0))
                                    set.Add(Convert.ToInt32(reader.GetValue(0)));
                            }
                        }
                    }
                    _lastConnectionOk = true;
                    _lastConnectionError = null;
                }
            }
            catch (Exception ex)
            {
                _lastConnectionOk = false;
                _lastConnectionError = ex.Message;
                WorkHubFileLogger.Warn("WORKLOG_CS", "GetRegisteredChangesetIds failed: " + ex.Message);
            }

            return set;
        }

        private bool DeleteByIdCore(long logId)
        {
            if (logId <= 0)
            {
                _lastConnectionOk = false;
                _lastConnectionError = "invalid logId";
                return false;
            }
            if (!IsConfigured)
            {
                _lastConnectionOk = false;
                _lastConnectionError = "connection string not configured";
                return false;
            }

            try
            {
                using (var conn = new OracleConnection(_connectionString))
                {
                    conn.Open();
                    DeleteChildren(conn, logId);
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText = "DELETE FROM " + HeaderTable + " WHERE LOG_ID = :logId";
                        cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                        int n = cmd.ExecuteNonQuery();
                        if (n <= 0)
                        {
                            _lastConnectionOk = false;
                            _lastConnectionError = "LOG_ID=" + logId + " not found";
                            return false;
                        }
                    }
                    _lastConnectionOk = true;
                    _lastConnectionError = null;
                    WorkHubFileLogger.Info("WORKLOG_DELETE", "deleted LogId=" + logId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _lastConnectionOk = false;
                _lastConnectionError = ex.Message;
                WorkHubFileLogger.Warn("WORKLOG_DELETE", "failed LogId=" + logId + ": " + ex.Message);
                return false;
            }
        }

        private int DeleteAllCore()
        {
            if (!IsConfigured)
            {
                _lastConnectionOk = false;
                _lastConnectionError = "connection string not configured";
                return -1;
            }

            try
            {
                using (var conn = new OracleConnection(_connectionString))
                {
                    conn.Open();
                    int deleted = 0;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.BindByName = true;
                        cmd.CommandText = "DELETE FROM " + SourceTable;
                        deleted += cmd.ExecuteNonQuery();
                        cmd.CommandText = "DELETE FROM " + ProjectTable;
                        deleted += cmd.ExecuteNonQuery();
                        try
                        {
                            cmd.CommandText = "DELETE FROM " + ChangesetTable;
                            deleted += cmd.ExecuteNonQuery();
                        }
                        catch (Exception exCs)
                        {
                            WorkHubFileLogger.Warn("WORKLOG_CS", "DeleteAll CS skipped: " + exCs.Message);
                        }
                        cmd.CommandText = "DELETE FROM " + HeaderTable;
                        deleted += cmd.ExecuteNonQuery();
                    }
                    _lastConnectionOk = true;
                    _lastConnectionError = null;
                    return deleted;
                }
            }
            catch (Exception ex)
            {
                _lastConnectionOk = false;
                _lastConnectionError = ex.Message;
                WorkHubFileLogger.Warn("WORKLOG_DELETE_ALL", "failed: " + ex.Message);
                return -1;
            }
        }

        private List<WorkLogRecordDto> GetAllCore()
        {
            var list = new List<WorkLogRecordDto>();
            if (!EnsureConfigured())
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureTeamNmColumn(conn);
                    EnsureUsrIdColumn(conn);
                    var map = new Dictionary<long, WorkLogRecordDto>();
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT " + HeaderSelectBase + HeaderSelectTeam + HeaderSelectAuthorUser + @"
                                FROM " + HeaderTable + @" w
                               ORDER BY " + TimelineDateExpr + @" DESC NULLS LAST, w.LOG_ID DESC";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var dto = MapHeader(reader);
                                map[dto.LogId] = dto;
                                list.Add(dto);
                            }
                        }
                    }

                    if (map.Count == 0)
                    {
                        _lastConnectionOk = true;
                        return list;
                    }

                    LoadProjects(conn, map, restrictToMapKeys: false);
                    LoadSources(conn, map, restrictToMapKeys: false);
                    LoadChangesets(conn, map, restrictToMapKeys: false);
                    _lastConnectionOk = true;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_SELECT", "GetAll failed: " + _lastConnectionError);
            }

            return list;
        }

        private WorkLogPageResult GetPageCore(WorkLogListQuery query)
        {
            var result = new WorkLogPageResult();
            if (!EnsureConfigured())
                return result;
            if (query == null)
                query = new WorkLogListQuery { PageIndex = 0, PageSize = 30 };

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureTeamNmColumn(conn);
                    EnsureUsrIdColumn(conn);
                    string whereSql;
                    Action<OracleCommand> bindFilters;
                    BuildListFilter(query, out whereSql, out bindFilters);

                    using (var countCmd = CreateCommand(conn))
                    {
                        countCmd.CommandText =
                            "SELECT COUNT(*) FROM " + HeaderTable + " w WHERE 1=1" + whereSql;
                        bindFilters(countCmd);
                        object scalar = countCmd.ExecuteScalar();
                        result.TotalCount = scalar == null || scalar == DBNull.Value
                            ? 0
                            : Convert.ToInt32(scalar);
                    }

                    var map = new Dictionary<long, WorkLogRecordDto>();
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT " + HeaderSelectBase + HeaderSelectTeam + HeaderSelectAuthorUser + @"
                                FROM " + HeaderTable + @" w
                               WHERE 1=1" + whereSql + @"
                               ORDER BY " + TimelineDateExpr + @" DESC NULLS LAST, w.LOG_ID DESC
                               OFFSET :offset ROWS FETCH NEXT :pageSize ROWS ONLY";
                        bindFilters(cmd);
                        cmd.Parameters.Add("offset", OracleDbType.Int32).Value = query.Offset;
                        cmd.Parameters.Add("pageSize", OracleDbType.Int32).Value = query.Take;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var dto = MapHeader(reader);
                                map[dto.LogId] = dto;
                                result.Items.Add(dto);
                            }
                        }
                    }

                    if (map.Count > 0)
                    {
                        LoadProjects(conn, map, restrictToMapKeys: true);
                        LoadSources(conn, map, restrictToMapKeys: true);
                        LoadChangesets(conn, map, restrictToMapKeys: true);
                    }

                    _lastConnectionOk = true;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_SELECT", "GetPage failed: " + _lastConnectionError);
            }

            return result;
        }

        /// <summary>
        /// UI GetFilteredItems? ?? ??? WHERE ??.
        /// Timeline = NVL(CHECKED_IN_AT, NVL(START_DT, NVL(END_DT, DEPLOY_DT))).
        /// </summary>
        private void BuildListFilter(
            WorkLogListQuery query,
            out string whereSql,
            out Action<OracleCommand> bindFilters)
        {
            var sql = new StringBuilder();
            string search = NormalizeFilterText(query != null ? query.SearchText : null);
            string site = NormalizeAllFilter(query != null ? query.SiteCode : null);
            string pc = NormalizeFilterText(query != null ? query.PcName : null);
            var pcKeys = new List<string>();
            if (pc != null)
                pcKeys.Add(pc);
            if (query != null && query.PcAliases != null)
            {
                foreach (string alias in query.PcAliases)
                {
                    string a = NormalizeFilterText(alias);
                    if (a == null)
                        continue;
                    bool dup = false;
                    for (int i = 0; i < pcKeys.Count; i++)
                    {
                        if (string.Equals(pcKeys[i], a, StringComparison.OrdinalIgnoreCase))
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                        pcKeys.Add(a);
                }
            }
            if (pcKeys.Count > 8)
                pcKeys = pcKeys.GetRange(0, 8);
            string writeStatus = NormalizeAllFilter(query != null ? query.WriteStatus : null);
            string type = NormalizeAllFilter(query != null ? query.Type : null);
            string category = NormalizeAllFilter(query != null ? query.Category : null);
            string deploy = NormalizeAllFilter(query != null ? query.DeployStatus : null);
            string authorName = NormalizeFilterText(query != null ? query.AuthorName : null);
            string authorLocalIp = NormalizeFilterText(query != null ? query.AuthorLocalPcIp : null);
            string teamName = NormalizeAllFilter(query != null ? query.TeamName : null);
            string occAuthor = NormalizeFilterText(query != null ? query.OccupancyAuthorName : null);
            string occIp = NormalizeFilterText(query != null ? query.OccupancyLocalPcIp : null);
            var searchAliases = new List<string>();
            if (query != null && query.SearchAliases != null)
            {
                foreach (string alias in query.SearchAliases)
                {
                    string a = NormalizeFilterText(alias);
                    if (a == null)
                        continue;
                    bool dup = false;
                    for (int i = 0; i < searchAliases.Count; i++)
                    {
                        if (string.Equals(searchAliases[i], a, StringComparison.OrdinalIgnoreCase))
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                        searchAliases.Add(a);
                }
            }
            if (searchAliases.Count > 8)
                searchAliases = searchAliases.GetRange(0, 8);
            DateTime? from = query != null ? query.FromDate : null;
            DateTime? to = query != null ? query.ToDate : null;

            // Prefer DEPLOY_DT; fall back to check-in / work period (many rows have null deploy date)
            string deployDateExpr =
                "TRUNC(NVL(w.DEPLOY_DT, NVL((SELECT MAX(p.DEPLOY_DT) FROM " + ProjectTable
                + " p WHERE p.LOG_ID = w.LOG_ID), NVL(w.CHECKED_IN_AT, NVL(w.START_DT, w.END_DT)))))";

            if (from.HasValue)
                sql.Append(" AND ").Append(deployDateExpr).Append(" >= TRUNC(:fromDt)");
            if (to.HasValue)
                sql.Append(" AND ").Append(deployDateExpr).Append(" <= TRUNC(:toDt)");
            if (site != null)
                sql.Append(" AND UPPER(TRIM(w.SITE_CD)) = UPPER(:siteCd)");
            if (pcKeys.Count > 0)
            {
                sql.Append(" AND (");
                for (int i = 0; i < pcKeys.Count; i++)
                {
                    if (i > 0)
                        sql.Append(" OR ");
                    sql.Append("UPPER(TRIM(w.PC_NM)) = UPPER(:pcA").Append(i).Append(")");
                    sql.Append(" OR UPPER(TRIM(w.LOCAL_PC_IP)) = UPPER(:pcA").Append(i).Append(")");
                }
                sql.Append(")");
            }
            if (writeStatus != null)
                sql.Append(" AND w.WRITE_STATUS = :writeStatus");
            if (deploy != null)
            {
                // Allow ??PC vs ?? PC (ignore spaces)
                sql.Append(" AND (");
                sql.Append(" REPLACE(UPPER(TRIM(w.DEPLOY_STATUS)), ' ', '') = REPLACE(UPPER(:deployStatus), ' ', '')");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(ProjectTable).Append(" p");
                sql.Append(" WHERE p.LOG_ID = w.LOG_ID");
                sql.Append(" AND REPLACE(UPPER(TRIM(p.DEPLOY_STATUS)), ' ', '') = REPLACE(UPPER(:deployStatus), ' ', ''))");
                sql.Append(")");
            }

            if (type != null && string.Equals(type, WorkLogFilterLabels.UnclassifiedType, StringComparison.Ordinal))
            {
                // Type badge empty
                sql.Append(" AND (");
                sql.Append(" EXISTS (SELECT 1 FROM ").Append(ProjectTable).Append(" p");
                sql.Append(" WHERE p.LOG_ID = w.LOG_ID AND (p.TYPE_NM IS NULL OR TRIM(p.TYPE_NM) IS NULL))");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(SourceTable).Append(" s");
                sql.Append(" WHERE s.LOG_ID = w.LOG_ID AND (s.TYPE_NM IS NULL OR TRIM(s.TYPE_NM) IS NULL))");
                sql.Append(" OR (NOT EXISTS (SELECT 1 FROM ").Append(ProjectTable).Append(" p WHERE p.LOG_ID = w.LOG_ID)");
                sql.Append(" AND NOT EXISTS (SELECT 1 FROM ").Append(SourceTable).Append(" s WHERE s.LOG_ID = w.LOG_ID))");
                sql.Append(")");
                type = null;
            }
            else if (type != null)
            {
                // Type column = TYPE_NM only
                sql.Append(" AND (");
                sql.Append(" EXISTS (SELECT 1 FROM ").Append(ProjectTable).Append(" p");
                sql.Append(" WHERE p.LOG_ID = w.LOG_ID AND UPPER(TRIM(p.TYPE_NM)) = UPPER(:typeNm)");
                if (category != null)
                    sql.Append(" AND UPPER(TRIM(p.CATEGORY_NM)) = UPPER(:categoryNm)");
                sql.Append(")");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(SourceTable).Append(" s");
                sql.Append(" WHERE s.LOG_ID = w.LOG_ID AND UPPER(TRIM(s.TYPE_NM)) = UPPER(:typeNm)");
                if (category != null)
                    sql.Append(" AND UPPER(TRIM(s.CATEGORY_NM)) = UPPER(:categoryNm)");
                sql.Append(")");
                sql.Append(")");
            }
            else if (category != null)
            {
                sql.Append(" AND (");
                sql.Append(" EXISTS (SELECT 1 FROM ").Append(ProjectTable).Append(" p");
                sql.Append(" WHERE p.LOG_ID = w.LOG_ID AND UPPER(TRIM(p.CATEGORY_NM)) = UPPER(:categoryNm))");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(SourceTable).Append(" s");
                sql.Append(" WHERE s.LOG_ID = w.LOG_ID AND UPPER(TRIM(s.CATEGORY_NM)) = UPPER(:categoryNm))");
                sql.Append(")");
            }

            if (authorName != null)
            {
                sql.Append(" AND (UPPER(TRIM(w.AUTHOR_NM)) = UPPER(:authorNm)");
                if (authorLocalIp != null)
                {
                    sql.Append(" OR ((w.AUTHOR_NM IS NULL OR TRIM(w.AUTHOR_NM) IS NULL)");
                    sql.Append(" AND UPPER(TRIM(w.LOCAL_PC_IP)) = UPPER(:authorLocalIp))");
                }
                sql.Append(")");
            }
            else if (authorLocalIp != null)
            {
                sql.Append(" AND UPPER(TRIM(w.LOCAL_PC_IP)) = UPPER(:authorLocalIp)");
            }

            if (teamName != null)
            {
                sql.Append(" AND (");
                if (_hasTeamNm)
                {
                    sql.Append(SqlTeamKeyCol("w.TEAM_NM")).Append(" = ").Append(SqlTeamKeyExpr("TRIM(:teamNm)"));
                    sql.Append(" OR INSTR(")
                        .Append(SqlTeamKeyExpr(
                            "NVL(TRIM(w.TEAM_NM), '') || NVL(TRIM(w.AUTHOR_NM), '') || NVL(TRIM(w.PERSON_IN_CHARGE), '') || NVL(TRIM(w.TFS_AUTHOR), '')"))
                        .Append(", ").Append(SqlTeamKeyExpr("TRIM(:teamNm)")).Append(") > 0");
                }
                else
                {
                    sql.Append("INSTR(")
                        .Append(SqlTeamKeyExpr(
                            "NVL(TRIM(w.AUTHOR_NM), '') || NVL(TRIM(w.PERSON_IN_CHARGE), '') || NVL(TRIM(w.TFS_AUTHOR), '')"))
                        .Append(", ").Append(SqlTeamKeyExpr("TRIM(:teamNm)")).Append(") > 0");
                }

                if (occAuthor != null || occIp != null)
                {
                    sql.Append(" OR (");
                    if (_hasTeamNm)
                    {
                        sql.Append("(w.TEAM_NM IS NULL OR TRIM(w.TEAM_NM) IS NULL OR ")
                            .Append(SqlTeamKeyCol("w.TEAM_NM")).Append(" = ").Append(SqlTeamKeyExpr("TRIM(:teamNm)"))
                            .Append(") AND (");
                    }

                    bool firstOcc = true;
                    if (occAuthor != null)
                    {
                        sql.Append("INSTR(")
                            .Append(SqlTeamKeyExpr(
                                "NVL(TRIM(w.AUTHOR_NM), '') || NVL(TRIM(w.PERSON_IN_CHARGE), '') || NVL(TRIM(w.TFS_AUTHOR), '')"))
                            .Append(", ").Append(SqlTeamKeyExpr("TRIM(:occAuthor)")).Append(") > 0");
                        firstOcc = false;
                    }
                    if (occIp != null)
                    {
                        if (!firstOcc)
                            sql.Append(" OR ");
                        sql.Append("UPPER(TRIM(w.LOCAL_PC_IP)) = UPPER(:occIp)");
                    }

                    if (_hasTeamNm)
                        sql.Append(")");
                    sql.Append(")");
                }

                sql.Append(")");
            }

            if (search != null)
            {
                sql.Append(" AND (");
                sql.Append(" UPPER(w.TICKET_NO) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.TICKET_CONTENTS) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.TFS_COMMENT) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.AUTHOR_NM) LIKE :q ESCAPE '\\'");
                if (_hasTeamNm)
                {
                    sql.Append(" OR UPPER(w.TEAM_NM) LIKE :q ESCAPE '\\'");
                    sql.Append(" OR UPPER(TRIM(w.AUTHOR_NM) || ' ' || NVL(TRIM(w.TEAM_NM), '')) LIKE :q ESCAPE '\\'");
                    sql.Append(" OR UPPER(TRIM(w.AUTHOR_NM) || ' · ' || NVL(TRIM(w.TEAM_NM), '')) LIKE :q ESCAPE '\\'");
                }
                sql.Append(" OR UPPER(w.TFS_AUTHOR) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.PERSON_IN_CHARGE) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.MENU_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.WORK_COMMENT) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.PC_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.LOCAL_PC_IP) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.SITE_CD) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.WRITE_STATUS) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.DEPLOY_STATUS) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(w.CLIENT_KEY) LIKE :q ESCAPE '\\'");
                sql.Append(" OR TO_CHAR(w.START_DT, 'YYYY-MM-DD HH24:MI') LIKE :q ESCAPE '\\'");
                sql.Append(" OR TO_CHAR(w.END_DT, 'YYYY-MM-DD HH24:MI') LIKE :q ESCAPE '\\'");
                sql.Append(" OR TO_CHAR(w.DEPLOY_DT, 'YYYY-MM-DD HH24:MI') LIKE :q ESCAPE '\\'");
                sql.Append(" OR TO_CHAR(w.CHECKED_IN_AT, 'YYYY-MM-DD HH24:MI') LIKE :q ESCAPE '\\'");
                sql.Append(" OR TO_CHAR(w.CHANGESET_ID) LIKE :q ESCAPE '\\'");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(ProjectTable).Append(" p");
                sql.Append(" WHERE p.LOG_ID = w.LOG_ID");
                sql.Append(" AND (UPPER(p.PROJECT_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(p.WORK_COMMENT) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(p.TYPE_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(p.CATEGORY_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(p.DEPLOY_STATUS) LIKE :q ESCAPE '\\'))");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(SourceTable).Append(" s");
                sql.Append(" WHERE s.LOG_ID = w.LOG_ID");
                sql.Append(" AND (UPPER(s.TYPE_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(s.CATEGORY_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(s.FILE_NM) LIKE :q ESCAPE '\\'");
                sql.Append(" OR UPPER(s.ORIGINAL_PATH) LIKE :q ESCAPE '\\'))");
                sql.Append(" OR EXISTS (SELECT 1 FROM ").Append(ChangesetTable).Append(" c");
                sql.Append(" WHERE c.LOG_ID = w.LOG_ID AND TO_CHAR(c.CHANGESET_ID) LIKE :q ESCAPE '\\')");
                for (int i = 0; i < searchAliases.Count; i++)
                {
                    sql.Append(" OR UPPER(TRIM(w.PC_NM)) = UPPER(:sa").Append(i).Append(")");
                    sql.Append(" OR UPPER(TRIM(w.LOCAL_PC_IP)) = UPPER(:sa").Append(i).Append(")");
                }
                sql.Append(")");
            }

            whereSql = sql.ToString();

            string bindSite = site;
            IList<string> bindPcKeys = pcKeys;
            string bindWs = writeStatus;
            string bindType = type;
            string bindCategory = category;
            string bindDeploy = deploy;
            string bindSearch = search == null ? null : ("%" + EscapeLike(search.ToUpperInvariant()) + "%");
            string bindAuthorName = authorName;
            string bindAuthorLocalIp = authorLocalIp;
            string bindTeamName = teamName;
            string bindOccAuthor = occAuthor;
            string bindOccIp = occIp;
            IList<string> bindSearchAliases = searchAliases;
            DateTime? bindFrom = from;
            DateTime? bindTo = to;

            bindFilters = cmd =>
            {
                if (bindFrom.HasValue)
                    cmd.Parameters.Add("fromDt", OracleDbType.TimeStamp).Value = bindFrom.Value.Date;
                if (bindTo.HasValue)
                    cmd.Parameters.Add("toDt", OracleDbType.TimeStamp).Value = bindTo.Value.Date;
                if (bindSite != null)
                    cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = bindSite;
                if (bindPcKeys != null)
                {
                    for (int i = 0; i < bindPcKeys.Count; i++)
                        cmd.Parameters.Add("pcA" + i, OracleDbType.Varchar2).Value = bindPcKeys[i];
                }
                if (bindWs != null)
                    cmd.Parameters.Add("writeStatus", OracleDbType.Varchar2).Value = bindWs;
                if (bindDeploy != null)
                    cmd.Parameters.Add("deployStatus", OracleDbType.Varchar2).Value = bindDeploy;
                if (bindType != null)
                    cmd.Parameters.Add("typeNm", OracleDbType.Varchar2).Value = bindType;
                if (bindCategory != null)
                    cmd.Parameters.Add("categoryNm", OracleDbType.Varchar2).Value = bindCategory;
                if (bindSearch != null)
                    cmd.Parameters.Add("q", OracleDbType.Varchar2).Value = bindSearch;
                if (bindAuthorName != null)
                    cmd.Parameters.Add("authorNm", OracleDbType.Varchar2).Value = bindAuthorName;
                if (bindAuthorLocalIp != null)
                    cmd.Parameters.Add("authorLocalIp", OracleDbType.Varchar2).Value = bindAuthorLocalIp;
                if (bindTeamName != null)
                    cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value = bindTeamName;
                if (bindOccAuthor != null)
                    cmd.Parameters.Add("occAuthor", OracleDbType.NVarchar2).Value = bindOccAuthor;
                if (bindOccIp != null)
                    cmd.Parameters.Add("occIp", OracleDbType.Varchar2).Value = bindOccIp;
                if (bindSearchAliases != null)
                {
                    for (int i = 0; i < bindSearchAliases.Count; i++)
                        cmd.Parameters.Add("sa" + i, OracleDbType.Varchar2).Value = bindSearchAliases[i];
                }
            };
        }

        private static string NormalizeFilterText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Trim();
        }

        /// <summary>"??"/ALL ???? ?? ???? ??. DTO ?? ??(?? ??? ?? ??).</summary>
        private static string NormalizeAllFilter(string value)
        {
            string text = NormalizeFilterText(value);
            if (text == null)
                return null;
            if (string.Equals(text, "ALL", StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(text, WorkLogFilterLabels.All, StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(text, WorkLogSiteCodes.All, StringComparison.OrdinalIgnoreCase))
                return null;
            return text;
        }

        private static string EscapeLike(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }

        private static string BuildLogIdInClause(Dictionary<long, WorkLogRecordDto> map)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (long id in map.Keys)
            {
                if (!first)
                    sb.Append(',');
                sb.Append(id);
                first = false;
            }
            return sb.ToString();
        }

        private WorkLogRecordDto GetByIdCore(long logId)
        {
            if (!EnsureConfigured() || logId <= 0)
                return null;

            try
            {
                using (var conn = OpenConnection())
                using (var cmd = CreateCommand(conn))
                {
                    EnsureTeamNmColumn(conn);
                    EnsureUsrIdColumn(conn);
                    cmd.CommandText =
                        @"SELECT " + HeaderSelectBase + HeaderSelectTeam + HeaderSelectAuthorUser + @"
                            FROM " + HeaderTable + @" w
                           WHERE w.LOG_ID = :logId";
                    cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;
                        var dto = MapHeader(reader);
                        var map = new Dictionary<long, WorkLogRecordDto> { { dto.LogId, dto } };
                        reader.Close();
                        LoadProjects(conn, map, restrictToMapKeys: true);
                        LoadSources(conn, map, restrictToMapKeys: true);
                        LoadChangesets(conn, map, restrictToMapKeys: true);
                        _lastConnectionOk = true;
                        return dto;
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Warn("WORKLOG_SELECT", "GetById failed: " + _lastConnectionError);
                return null;
            }
        }

        private void LoadProjects(OracleConnection conn, Dictionary<long, WorkLogRecordDto> map, bool restrictToMapKeys)
        {
            if (map == null || map.Count == 0)
                return;

            using (var cmd = CreateCommand(conn))
            {
                string sql =
                    @"SELECT PRJ_ID, LOG_ID, TYPE_NM, CATEGORY_NM, PROJECT_NM, SOURCE_ORIGIN,
                             DEPLOY_STATUS, DEPLOY_DT, WORK_COMMENT, SORT_ORD
                        FROM " + ProjectTable;
                if (restrictToMapKeys)
                    sql += " WHERE LOG_ID IN (" + BuildLogIdInClause(map) + ")";
                sql += " ORDER BY LOG_ID, SORT_ORD, PRJ_ID";
                cmd.CommandText = sql;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long logId = Convert.ToInt64(reader.GetValue(1));
                        WorkLogRecordDto parent;
                        if (!map.TryGetValue(logId, out parent))
                            continue;
                        parent.Projects.Add(new WorkLogProjectDto
                        {
                            ProjectId = Convert.ToInt64(reader.GetValue(0)),
                            LogId = logId,
                            Type = ReadString(reader, 2),
                            Category = ReadString(reader, 3),
                            ProjectName = ReadString(reader, 4),
                            SourceOrigin = ReadString(reader, 5),
                            DeploymentStatus = ReadString(reader, 6),
                            DeploymentDate = ReadTimestamp(reader, 7),
                            Comment = ReadString(reader, 8),
                            SortOrder = reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9))
                        });
                    }
                }
            }
        }

        private void LoadSources(OracleConnection conn, Dictionary<long, WorkLogRecordDto> map, bool restrictToMapKeys)
        {
            if (map == null || map.Count == 0)
                return;

            using (var cmd = CreateCommand(conn))
            {
                string sql =
                    @"SELECT SRC_ID, LOG_ID, PRJ_ID, FILE_NM, ORIGINAL_PATH, CHANGE_TYPE, CHANGE_DETAIL,
                             TYPE_NM, CATEGORY_NM, PROJECT_NM, SOURCE_ORIGIN, APPLIED_RULE_CD,
                             IS_AUTO_CLASSIFIED, NEEDS_REVIEW, REVIEW_REASON, SORT_ORD, CREATED_AT
                        FROM " + SourceTable;
                if (restrictToMapKeys)
                    sql += " WHERE LOG_ID IN (" + BuildLogIdInClause(map) + ")";
                sql += " ORDER BY LOG_ID, SORT_ORD, SRC_ID";
                cmd.CommandText = sql;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long logId = Convert.ToInt64(reader.GetValue(1));
                        WorkLogRecordDto parent;
                        if (!map.TryGetValue(logId, out parent))
                            continue;
                        parent.Sources.Add(new WorkLogSourceDto
                        {
                            SourceId = Convert.ToInt64(reader.GetValue(0)),
                            LogId = logId,
                            ProjectId = reader.IsDBNull(2) ? (long?)null : Convert.ToInt64(reader.GetValue(2)),
                            FileName = ReadString(reader, 3),
                            OriginalPath = ReadString(reader, 4),
                            ChangeType = ReadString(reader, 5),
                            ChangeDetail = ReadString(reader, 6),
                            Type = ReadString(reader, 7),
                            Category = ReadString(reader, 8),
                            ProjectName = ReadString(reader, 9),
                            SourceOrigin = ReadString(reader, 10),
                            AppliedRuleCode = ReadString(reader, 11),
                            IsAutoClassified = IsYes(ReadString(reader, 12)),
                            NeedsReview = IsYes(ReadString(reader, 13)),
                            ReviewReason = ReadString(reader, 14),
                            SortOrder = reader.IsDBNull(15) ? 0 : Convert.ToInt32(reader.GetValue(15)),
                            CreatedAt = reader.FieldCount > 16 ? ReadTimestamp(reader, 16) : null
                        });
                    }
                }
            }
        }

        private bool SaveCore(WorkLogRecordDto record)
        {
            if (record == null)
                return false;
            if (!EnsureConfigured())
            {
                WorkHubFileLogger.Error("WORKLOG_SAVE", "DB not configured");
                return false;
            }

            try
            {
                using (var conn = OpenConnection())
                using (var tx = conn.BeginTransaction())
                {
                    EnsureTeamNmColumn(conn);
                    EnsureUsrIdColumn(conn);
                    bool isNew = record.LogId <= 0;
                    if (isNew)
                        record.LogId = NextVal(conn, HeaderSeq);

                    if (isNew)
                        InsertHeader(conn, record);
                    else
                        UpdateHeader(conn, record);

                    DeleteChildren(conn, record.LogId, includeChangesets: false);
                    InsertProjectsAndSources(conn, record);

                    // WRK ?? ?? ?? ? CS ??? ??? ?? ??? ???? ???
                    tx.Commit();
                    _lastConnectionOk = true;

                    // CS registry only on detail "Save" (COMPLETED). Draft/import skip WRK_CS.
                    int csCount = 0;
                    try
                    {
                        DeleteChangesetChildren(conn, record.LogId);
                        if (IsCompletedStatus(record.WriteStatus))
                            csCount = InsertChangesets(conn, record);
                    }
                    catch (Exception exCs)
                    {
                        WorkHubFileLogger.Warn("WORKLOG_CS",
                            "CS registry skipped LogId=" + record.LogId + ": " + exCs.Message);
                    }

                    WorkHubFileLogger.Info("WORKLOG_SAVE",
                        "LogId=" + record.LogId
                        + " Status=" + (record.WriteStatus ?? "-")
                        + " Projects=" + (record.Projects != null ? record.Projects.Count : 0)
                        + " Sources=" + (record.Sources != null ? record.Sources.Count : 0)
                        + " Changesets=" + csCount
                        + " New=" + isNew);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                WorkHubFileLogger.Error("WORKLOG_SAVE", "failed: " + _lastConnectionError);
                return false;
            }
        }

        private void InsertHeader(OracleConnection conn, WorkLogRecordDto r)
        {
            using (var cmd = CreateCommand(conn))
            {
                string usrIdColumn = _hasUsrId ? ", USR_ID" : string.Empty;
                string usrIdValue = _hasUsrId ? ", :usrId" : string.Empty;
                if (_hasTeamNm)
                {
                    cmd.CommandText =
                        @"INSERT INTO " + HeaderTable + @" (
                              LOG_ID, CLIENT_KEY, SITE_CD, WRITE_STATUS, TICKET_NO, TICKET_CONTENTS,
                              MENU_NM, PC_NM, PERSON_IN_CHARGE, LOCAL_PC_IP,
                              START_DT, END_DT, DEPLOY_STATUS, DEPLOY_DT, WORK_COMMENT,
                              CHANGESET_ID, TFS_COMMENT, TFS_AUTHOR, AUTHOR_NM, TEAM_NM, CHECKED_IN_AT,
                              CHANGED_FILE_COUNT, NEEDS_TICKET_REVIEW, CREATED_AT, UPDT_DTM" + usrIdColumn + @"
                          ) VALUES (
                              :logId, :clientKey, :siteCd, :writeStatus, :ticketNo, :ticketContents,
                              :menuNm, :pcNm, :person, :localIp,
                              :startDt, :endDt, :deployStatus, :deployDt, :workComment,
                              :changesetId, :tfsComment, :tfsAuthor, :authorNm, :teamNm, :checkedInAt,
                              :changedFileCount, :needsReview, SYSTIMESTAMP, SYSTIMESTAMP" + usrIdValue + @"
                          )";
                }
                else
                {
                    cmd.CommandText =
                        @"INSERT INTO " + HeaderTable + @" (
                              LOG_ID, CLIENT_KEY, SITE_CD, WRITE_STATUS, TICKET_NO, TICKET_CONTENTS,
                              MENU_NM, PC_NM, PERSON_IN_CHARGE, LOCAL_PC_IP,
                              START_DT, END_DT, DEPLOY_STATUS, DEPLOY_DT, WORK_COMMENT,
                              CHANGESET_ID, TFS_COMMENT, TFS_AUTHOR, AUTHOR_NM, CHECKED_IN_AT,
                              CHANGED_FILE_COUNT, NEEDS_TICKET_REVIEW, CREATED_AT, UPDT_DTM" + usrIdColumn + @"
                          ) VALUES (
                              :logId, :clientKey, :siteCd, :writeStatus, :ticketNo, :ticketContents,
                              :menuNm, :pcNm, :person, :localIp,
                              :startDt, :endDt, :deployStatus, :deployDt, :workComment,
                              :changesetId, :tfsComment, :tfsAuthor, :authorNm, :checkedInAt,
                              :changedFileCount, :needsReview, SYSTIMESTAMP, SYSTIMESTAMP" + usrIdValue + @"
                          )";
                }
                BindHeader(cmd, r);
                cmd.ExecuteNonQuery();
            }
        }

        private void UpdateHeader(OracleConnection conn, WorkLogRecordDto r)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText =
                    @"UPDATE " + HeaderTable + @"
                         SET CLIENT_KEY = :clientKey,
                             SITE_CD = :siteCd,
                             WRITE_STATUS = :writeStatus,
                             TICKET_NO = :ticketNo,
                             TICKET_CONTENTS = :ticketContents,
                             MENU_NM = :menuNm,
                             PC_NM = :pcNm,
                             PERSON_IN_CHARGE = :person,
                             LOCAL_PC_IP = :localIp,
                             START_DT = :startDt,
                             END_DT = :endDt,
                             DEPLOY_STATUS = :deployStatus,
                             DEPLOY_DT = :deployDt,
                             WORK_COMMENT = :workComment,
                             CHANGESET_ID = :changesetId,
                             TFS_COMMENT = :tfsComment,
                             TFS_AUTHOR = :tfsAuthor,
                             AUTHOR_NM = :authorNm,"
                    + (_hasTeamNm ? @"
                             TEAM_NM = :teamNm," : string.Empty)
                    + (_hasUsrId ? @"
                             USR_ID = :usrId," : string.Empty) + @"
                             CHECKED_IN_AT = :checkedInAt,
                             CHANGED_FILE_COUNT = :changedFileCount,
                             NEEDS_TICKET_REVIEW = :needsReview,
                             UPDT_DTM = SYSTIMESTAMP
                       WHERE LOG_ID = :logId";
                BindHeader(cmd, r);
                cmd.ExecuteNonQuery();
            }
        }

        private void BindHeader(OracleCommand cmd, WorkLogRecordDto r)
        {
            cmd.Parameters.Add("logId", OracleDbType.Int64).Value = r.LogId;
            cmd.Parameters.Add("clientKey", OracleDbType.Varchar2).Value = (object)Trim(r.ClientKey, 100) ?? DBNull.Value;
            string site = string.IsNullOrWhiteSpace(r.SiteCode)
                ? null
                : r.SiteCode.Trim().ToUpperInvariant();
            cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = (object)Trim(site, 20) ?? DBNull.Value;
            cmd.Parameters.Add("writeStatus", OracleDbType.Varchar2).Value =
                string.IsNullOrWhiteSpace(r.WriteStatus) ? "DRAFT" : r.WriteStatus.Trim().ToUpperInvariant();
            cmd.Parameters.Add("ticketNo", OracleDbType.Varchar2).Value = (object)Trim(r.TicketNo, 100) ?? DBNull.Value;
            cmd.Parameters.Add("ticketContents", OracleDbType.Varchar2).Value = (object)Trim(r.TicketContents, 2000) ?? DBNull.Value;
            cmd.Parameters.Add("menuNm", OracleDbType.Varchar2).Value = (object)Trim(r.MenuName, 200) ?? DBNull.Value;
            cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = (object)Trim(r.PcName, 100) ?? DBNull.Value;
            cmd.Parameters.Add("person", OracleDbType.Varchar2).Value = (object)Trim(r.PersonInCharge, 200) ?? DBNull.Value;
            cmd.Parameters.Add("localIp", OracleDbType.Varchar2).Value = (object)Trim(r.LocalPcIp, 50) ?? DBNull.Value;
            cmd.Parameters.Add("startDt", OracleDbType.TimeStamp).Value = ToDbDate(r.StartDate);
            cmd.Parameters.Add("endDt", OracleDbType.TimeStamp).Value = ToDbDate(r.EndDate);
            cmd.Parameters.Add("deployStatus", OracleDbType.Varchar2).Value = (object)Trim(r.DeploymentStatus, 50) ?? DBNull.Value;
            cmd.Parameters.Add("deployDt", OracleDbType.TimeStamp).Value = ToDbDate(r.DeploymentDate);
            cmd.Parameters.Add("workComment", OracleDbType.Varchar2).Value = (object)Trim(r.WorkComment, 2000) ?? DBNull.Value;
            cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = r.ChangesetId;
            cmd.Parameters.Add("tfsComment", OracleDbType.Varchar2).Value = (object)Trim(r.TfsComment, 2000) ?? DBNull.Value;
            cmd.Parameters.Add("tfsAuthor", OracleDbType.Varchar2).Value = (object)Trim(r.TfsAuthor, 200) ?? DBNull.Value;
            cmd.Parameters.Add("authorNm", OracleDbType.Varchar2).Value = (object)Trim(r.AuthorName, 200) ?? DBNull.Value;
            if (_hasTeamNm)
                cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value = (object)Trim(r.TeamName, 100) ?? DBNull.Value;
            if (_hasUsrId)
                cmd.Parameters.Add("usrId", OracleDbType.Int64).Value = r.UserId.HasValue ? (object)r.UserId.Value : DBNull.Value;
            cmd.Parameters.Add("checkedInAt", OracleDbType.TimeStamp).Value = ToDbDate(r.CheckedInAt);
            cmd.Parameters.Add("changedFileCount", OracleDbType.Int32).Value = r.ChangedFileCount;
            cmd.Parameters.Add("needsReview", OracleDbType.Char).Value = r.NeedsTicketReview ? "Y" : "N";
        }

        private void DeleteChildren(OracleConnection conn, long logId, bool includeChangesets = true)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText = "DELETE FROM " + SourceTable + " WHERE LOG_ID = :logId";
                cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText = "DELETE FROM " + ProjectTable + " WHERE LOG_ID = :logId";
                cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                cmd.ExecuteNonQuery();
            }
            if (includeChangesets)
                DeleteChangesetChildren(conn, logId);
        }

        private void DeleteChangesetChildren(OracleConnection conn, long logId)
        {
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "DELETE FROM " + ChangesetTable + " WHERE LOG_ID = :logId";
                    cmd.Parameters.Add("logId", OracleDbType.Int64).Value = logId;
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("WORKLOG_CS", "Delete CS children skipped: " + ex.Message);
            }
        }

        private void LoadChangesets(OracleConnection conn, Dictionary<long, WorkLogRecordDto> map, bool restrictToMapKeys)
        {
            if (map == null || map.Count == 0)
                return;

            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    string sql =
                        @"SELECT LOG_ID, CHANGESET_ID
                            FROM " + ChangesetTable + @"
                           WHERE CHANGESET_ID > 0";
                    if (restrictToMapKeys)
                        sql += " AND LOG_ID IN (" + BuildLogIdInClause(map) + ")";
                    sql += " ORDER BY LOG_ID, CHANGESET_ID";
                    cmd.CommandText = sql;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            long logId = Convert.ToInt64(reader.GetValue(0));
                            WorkLogRecordDto parent;
                            if (!map.TryGetValue(logId, out parent) || parent == null)
                                continue;
                            int csId = Convert.ToInt32(reader.GetValue(1));
                            if (parent.ChangesetIds == null)
                                parent.ChangesetIds = new List<int>();
                            if (!parent.ChangesetIds.Contains(csId))
                                parent.ChangesetIds.Add(csId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("WORKLOG_CS", "LoadChangesets skipped: " + ex.Message);
            }
        }

        private int InsertChangesets(OracleConnection conn, WorkLogRecordDto record)
        {
            if (record == null || record.LogId <= 0)
                return 0;

            var ids = ResolveChangesetIds(record);
            if (ids.Count == 0)
                return 0;

            string site = string.IsNullOrWhiteSpace(record.SiteCode)
                ? "AURORA"
                : record.SiteCode.Trim().ToUpperInvariant();

            int inserted = 0;
            foreach (int csId in ids)
            {
                using (var delOther = CreateCommand(conn))
                {
                    delOther.CommandText =
                        "DELETE FROM " + ChangesetTable
                        + " WHERE SITE_CD = :siteCd AND CHANGESET_ID = :changesetId AND LOG_ID <> :logId";
                    delOther.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = Trim(site, 20);
                    delOther.Parameters.Add("changesetId", OracleDbType.Int32).Value = csId;
                    delOther.Parameters.Add("logId", OracleDbType.Int64).Value = record.LogId;
                    delOther.ExecuteNonQuery();
                }

                using (var cmd = CreateCommand(conn))
                {
                    long rowId = NextVal(conn, ChangesetSeq);
                    cmd.CommandText =
                        @"INSERT INTO " + ChangesetTable + @" (
                              CS_ROW_ID, LOG_ID, CHANGESET_ID, SITE_CD, CHECKED_IN_AT, CREATED_AT
                          ) VALUES (
                              :rowId, :logId, :changesetId, :siteCd, :checkedInAt, SYSTIMESTAMP
                          )";
                    cmd.Parameters.Add("rowId", OracleDbType.Int64).Value = rowId;
                    cmd.Parameters.Add("logId", OracleDbType.Int64).Value = record.LogId;
                    cmd.Parameters.Add("changesetId", OracleDbType.Int32).Value = csId;
                    cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = Trim(site, 20);
                    cmd.Parameters.Add("checkedInAt", OracleDbType.TimeStamp).Value = ToDbDate(record.CheckedInAt);
                    cmd.ExecuteNonQuery();
                    inserted++;
                }
            }

            return inserted;
        }

        private static bool IsCompletedStatus(string writeStatus)
        {
            return !string.IsNullOrWhiteSpace(writeStatus)
                && string.Equals(writeStatus.Trim(), "COMPLETED", StringComparison.OrdinalIgnoreCase);
        }

        private static List<int> ResolveChangesetIds(WorkLogRecordDto record)
        {
            var ids = new List<int>();
            if (record == null)
                return ids;

            if (record.ChangesetIds != null)
            {
                foreach (int id in record.ChangesetIds)
                {
                    if (id > 0 && !ids.Contains(id))
                        ids.Add(id);
                }
            }

            if (ids.Count == 0 && record.ChangesetId > 0)
                ids.Add(record.ChangesetId);

            if (record.ChangesetId <= 0 && ids.Count > 0)
                record.ChangesetId = ids[0];

            return ids;
        }

        private void InsertProjectsAndSources(OracleConnection conn, WorkLogRecordDto record)
        {
            var projectIdByKey = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            int pOrd = 0;
            if (record.Projects != null)
            {
                foreach (var p in record.Projects)
                {
                    if (p == null)
                        continue;
                    long prjId = NextVal(conn, ProjectSeq);
                    p.ProjectId = prjId;
                    p.LogId = record.LogId;
                    p.SortOrder = pOrd++;
                    InsertProject(conn, p);
                    string key = GroupKey(p.Type, p.Category, p.ProjectName);
                    projectIdByKey[key] = prjId;
                }
            }

            int sOrd = 0;
            if (record.Sources == null)
                return;

            foreach (var s in record.Sources)
            {
                if (s == null)
                    continue;
                long srcId = NextVal(conn, SourceSeq);
                s.SourceId = srcId;
                s.LogId = record.LogId;
                s.SortOrder = sOrd++;
                string key = GroupKey(s.Type, s.Category, s.ProjectName);
                long prjId;
                if (projectIdByKey.TryGetValue(key, out prjId))
                    s.ProjectId = prjId;
                else
                    s.ProjectId = null;
                InsertSource(conn, s);
            }
        }

        private void InsertProject(OracleConnection conn, WorkLogProjectDto p)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText =
                    @"INSERT INTO " + ProjectTable + @" (
                          PRJ_ID, LOG_ID, TYPE_NM, CATEGORY_NM, PROJECT_NM, SOURCE_ORIGIN,
                          DEPLOY_STATUS, DEPLOY_DT, WORK_COMMENT, SORT_ORD, CREATED_AT, UPDT_DTM
                      ) VALUES (
                          :prjId, :logId, :typeNm, :catNm, :prjNm, :srcOrigin,
                          :deployStatus, :deployDt, :prjComment, :sortOrd, SYSTIMESTAMP, SYSTIMESTAMP
                      )";
                cmd.Parameters.Add("prjId", OracleDbType.Int64).Value = p.ProjectId;
                cmd.Parameters.Add("logId", OracleDbType.Int64).Value = p.LogId;
                cmd.Parameters.Add("typeNm", OracleDbType.Varchar2).Value = (object)Trim(p.Type, 100) ?? DBNull.Value;
                cmd.Parameters.Add("catNm", OracleDbType.Varchar2).Value = (object)Trim(p.Category, 100) ?? DBNull.Value;
                cmd.Parameters.Add("prjNm", OracleDbType.Varchar2).Value = (object)Trim(p.ProjectName, 200) ?? DBNull.Value;
                cmd.Parameters.Add("srcOrigin", OracleDbType.Varchar2).Value = (object)Trim(p.SourceOrigin, 50) ?? DBNull.Value;
                cmd.Parameters.Add("deployStatus", OracleDbType.Varchar2).Value = (object)Trim(p.DeploymentStatus, 50) ?? DBNull.Value;
                cmd.Parameters.Add("deployDt", OracleDbType.TimeStamp).Value = ToDbDate(p.DeploymentDate);
                cmd.Parameters.Add("prjComment", OracleDbType.Varchar2).Value = (object)Trim(p.Comment, 2000) ?? DBNull.Value;
                cmd.Parameters.Add("sortOrd", OracleDbType.Int32).Value = p.SortOrder;
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertSource(OracleConnection conn, WorkLogSourceDto s)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText =
                    @"INSERT INTO " + SourceTable + @" (
                          SRC_ID, LOG_ID, PRJ_ID, FILE_NM, ORIGINAL_PATH, CHANGE_TYPE, CHANGE_DETAIL,
                          TYPE_NM, CATEGORY_NM, PROJECT_NM, SOURCE_ORIGIN, APPLIED_RULE_CD,
                          IS_AUTO_CLASSIFIED, NEEDS_REVIEW, REVIEW_REASON, SORT_ORD, CREATED_AT, UPDT_DTM
                      ) VALUES (
                          :srcId, :logId, :prjId, :fileNm, :origPath, :changeType, :changeDetail,
                          :typeNm, :catNm, :prjNm, :srcOrigin, :ruleCd,
                          :autoCls, :needsRev, :reviewReason, :sortOrd,
                          NVL(:createdAt, SYSTIMESTAMP), SYSTIMESTAMP
                      )";
                cmd.Parameters.Add("srcId", OracleDbType.Int64).Value = s.SourceId;
                cmd.Parameters.Add("logId", OracleDbType.Int64).Value = s.LogId;
                cmd.Parameters.Add("prjId", OracleDbType.Int64).Value =
                    s.ProjectId.HasValue ? (object)s.ProjectId.Value : DBNull.Value;
                cmd.Parameters.Add("fileNm", OracleDbType.Varchar2).Value = (object)Trim(s.FileName, 2000) ?? DBNull.Value;
                cmd.Parameters.Add("origPath", OracleDbType.Varchar2).Value = (object)Trim(s.OriginalPath, 2000) ?? DBNull.Value;
                cmd.Parameters.Add("changeType", OracleDbType.Varchar2).Value = (object)Trim(s.ChangeType, 50) ?? DBNull.Value;
                cmd.Parameters.Add("changeDetail", OracleDbType.Varchar2).Value = (object)Trim(s.ChangeDetail, 1000) ?? DBNull.Value;
                cmd.Parameters.Add("typeNm", OracleDbType.Varchar2).Value = (object)Trim(s.Type, 100) ?? DBNull.Value;
                cmd.Parameters.Add("catNm", OracleDbType.Varchar2).Value = (object)Trim(s.Category, 100) ?? DBNull.Value;
                cmd.Parameters.Add("prjNm", OracleDbType.Varchar2).Value = (object)Trim(s.ProjectName, 200) ?? DBNull.Value;
                cmd.Parameters.Add("srcOrigin", OracleDbType.Varchar2).Value = (object)Trim(s.SourceOrigin, 50) ?? DBNull.Value;
                cmd.Parameters.Add("ruleCd", OracleDbType.Varchar2).Value = (object)Trim(s.AppliedRuleCode, 100) ?? DBNull.Value;
                cmd.Parameters.Add("autoCls", OracleDbType.Char).Value = s.IsAutoClassified ? "Y" : "N";
                cmd.Parameters.Add("needsRev", OracleDbType.Char).Value = s.NeedsReview ? "Y" : "N";
                cmd.Parameters.Add("reviewReason", OracleDbType.Varchar2).Value = (object)Trim(s.ReviewReason, 500) ?? DBNull.Value;
                cmd.Parameters.Add("sortOrd", OracleDbType.Int32).Value = s.SortOrder;
                cmd.Parameters.Add("createdAt", OracleDbType.TimeStamp).Value = ToDbDate(s.CreatedAt);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// USR_ID/AUTHOR_IS_DELETED는 TEAM_NM 유무에 따라 실제 컬럼 위치가 달라지므로(선택적으로
        /// 뒤에 붙는 컬럼들), 고정 인덱스가 아니라 컬럼명으로 찾는다 — 없으면 IndexOutOfRangeException.
        /// </summary>
        private static int? TryGetOrdinal(IDataRecord reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static WorkLogRecordDto MapHeader(IDataRecord reader)
        {
            int? usrIdOrd = TryGetOrdinal(reader, "USR_ID");
            int? authorDeletedOrd = TryGetOrdinal(reader, "AUTHOR_IS_DELETED");
            return new WorkLogRecordDto
            {
                LogId = Convert.ToInt64(reader.GetValue(0)),
                ClientKey = ReadString(reader, 1),
                WriteStatus = ReadString(reader, 2),
                TicketNo = ReadString(reader, 3),
                TicketContents = ReadString(reader, 4),
                MenuName = ReadString(reader, 5),
                PcName = ReadString(reader, 6),
                PersonInCharge = ReadString(reader, 7),
                LocalPcIp = ReadString(reader, 8),
                StartDate = ReadTimestamp(reader, 9),
                EndDate = ReadTimestamp(reader, 10),
                DeploymentStatus = ReadString(reader, 11),
                DeploymentDate = ReadTimestamp(reader, 12),
                WorkComment = ReadString(reader, 13),
                ChangesetId = reader.IsDBNull(14) ? 0 : Convert.ToInt32(reader.GetValue(14)),
                TfsComment = ReadString(reader, 15),
                TfsAuthor = ReadString(reader, 16),
                AuthorName = ReadString(reader, 17),
                CheckedInAt = ReadTimestamp(reader, 18),
                ChangedFileCount = reader.IsDBNull(19) ? 0 : Convert.ToInt32(reader.GetValue(19)),
                NeedsTicketReview = IsYes(ReadString(reader, 20)),
                CreatedAt = ReadTimestamp(reader, 21),
                UpdatedAt = ReadTimestamp(reader, 22),
                SiteCode = reader.FieldCount > 23 ? ReadString(reader, 23) : null,
                TeamName = reader.FieldCount > 24 ? ReadString(reader, 24) : null,
                UserId = usrIdOrd.HasValue && !reader.IsDBNull(usrIdOrd.Value)
                    ? (long?)Convert.ToInt64(reader.GetValue(usrIdOrd.Value)) : null,
                IsAuthorDeleted = authorDeletedOrd.HasValue && !reader.IsDBNull(authorDeletedOrd.Value)
                    && Convert.ToInt32(reader.GetValue(authorDeletedOrd.Value)) != 0
            };
        }

        private static long NextVal(OracleConnection conn, string sequenceName)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText = "SELECT " + sequenceName + ".NEXTVAL FROM DUAL";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private static string GroupKey(string type, string category, string project)
        {
            return (type ?? string.Empty).Trim()
                + "|" + (category ?? string.Empty).Trim()
                + "|" + (project ?? string.Empty).Trim();
        }

        private bool EnsureConfigured()
        {
            if (IsConfigured)
                return true;
            _lastConnectionOk = false;
            _lastConnectionError = "GbcWorkHubDb connection string not configured";
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

        private static OracleCommand CreateCommand(OracleConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            return cmd;
        }

        private string HeaderSelectTeam
        {
            get { return _hasTeamNm ? ", TEAM_NM" : string.Empty; }
        }

        private void EnsureTeamNmColumn(OracleConnection conn)
        {
            if (_teamNmProbed)
                return;
            _teamNmProbed = true;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT TEAM_NM FROM " + HeaderTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasTeamNm = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasTeamNm = false;
                    WorkHubFileLogger.Warn("WORKLOG_SCHEMA",
                        "TEAM_NM column missing; queries run without it. Apply sql/12_ALTER_WRK_TEAM_NM.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        /// <summary>
        /// 작성자 계정 FK(USR_ID) + 그 계정이 삭제됐는지(AUTHOR_IS_DELETED, 상관 서브쿼리). 컬럼명으로
        /// GetOrdinal 조회하므로 TEAM_NM 유무에 따라 실제 컬럼 위치가 달라져도 안전하다.
        /// </summary>
        private string HeaderSelectAuthorUser
        {
            get
            {
                return _hasUsrId
                    ? ", w.USR_ID, (SELECT NVL(u.IS_DELETED,0) FROM XSUP.MSDWHTKD_USR u WHERE u.USR_ID = w.USR_ID) AS AUTHOR_IS_DELETED"
                    : string.Empty;
            }
        }

        private void EnsureUsrIdColumn(OracleConnection conn)
        {
            if (_usrIdProbed)
                return;
            _usrIdProbed = true;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT USR_ID FROM " + HeaderTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasUsrId = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 904)
                {
                    _hasUsrId = false;
                    WorkHubFileLogger.Warn("WORKLOG_SCHEMA",
                        "USR_ID column missing; deleted-account badge disabled for WorkLog. Apply sql/31_MSDWHTKD_WRK_USR_ID.sql when DBA can.");
                    return;
                }
                throw;
            }
        }

        private void Fail(Exception ex)
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
                || upper.Contains("PLACEHOLDER");
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

        private static object ToDbDate(DateTime? value)
        {
            return value.HasValue ? (object)value.Value : DBNull.Value;
        }

        private static bool IsYes(string value)
        {
            return string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Oracle VARCHAR2 BYTE ??? ?? UTF-8 ??? ?? ??.</summary>
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
            return lo <= 0 ? string.Empty : t.Substring(0, lo);
        }

        private static string SafeError(Exception ex)
        {
            if (ex == null)
                return "unknown";
            string msg = ex.Message ?? string.Empty;
            if (msg.IndexOf("Password=", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("User Id=", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Oracle error (credentials masked)";
            return msg;
        }
    }
}
