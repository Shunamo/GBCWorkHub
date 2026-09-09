using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading.Tasks;
using GBCWorkHub.DTO.Improvement;
using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>XSUP.MSDWHTKD_REQ / _COMMENT / _REACTION / _ATTACH. 없으면 probe 후 건너뜀 (sql/24 미적용 대비).</summary>
    public class OracleImprovementRepository : IImprovementRepository
    {
        public const string ConnectionStringName = "GbcWorkHubDb";
        private const string ReqTable = "XSUP.MSDWHTKD_REQ";
        private const string CommentTable = "XSUP.MSDWHTKD_REQ_COMMENT";
        private const string ReactionTable = "XSUP.MSDWHTKD_REQ_REACTION";
        private const string AttachTable = "XSUP.MSDWHTKD_REQ_ATTACH";
        private const string ReqSeq = "XSUP.SEQ_MSDWHTKD_REQ";
        private const string CommentSeq = "XSUP.SEQ_MSDWHTKD_REQ_COMMENT";
        private const string AttachSeq = "XSUP.SEQ_MSDWHTKD_REQ_ATTACH";
        private const int OracleTableMissing = 942;

        private const string ReqSelectBase =
            @"r.REQ_ID, r.REQUEST_TYPE, r.TITLE, r.DESCRIPTION, r.REPRODUCTION_STEPS, r.APP_VERSION,
              r.SITE_CD, r.PC_NM, r.USER_ID, r.AUTHOR_NM, r.TEAM_NM, r.STATUS, r.RESOLVED_VERSION,
              r.ADMIN_NOTE, r.CREATED_AT, r.UPDATED_AT, r.RESOLVED_AT,
              (SELECT COUNT(*) FROM " + ReactionTable + @" x WHERE x.REQ_ID = r.REQ_ID AND x.REACTION_TYPE = 'REPRODUCED') AS REPRODUCED_CNT,
              (SELECT COUNT(*) FROM " + ReactionTable + @" x WHERE x.REQ_ID = r.REQ_ID AND x.REACTION_TYPE = 'NOT_REPRODUCED') AS NOT_REPRODUCED_CNT,
              (SELECT COUNT(*) FROM " + CommentTable + @" c WHERE c.REQ_ID = r.REQ_ID) AS COMMENT_CNT,
              (SELECT MAX(x.REACTION_TYPE) FROM " + ReactionTable + @" x WHERE x.REQ_ID = r.REQ_ID AND x.USER_ID = :myUserId) AS MY_REACTION,
              (SELECT NVL(u.IS_DELETED,0) FROM XSUP.MSDWHTKD_USR u WHERE u.USR_ID = r.USER_ID) AS AUTHOR_IS_DELETED";

        private readonly string _connectionString;
        private bool _reqProbed;
        private bool _hasReqTable;

        public OracleImprovementRepository()
        {
            _connectionString = ResolveConnectionString();
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_connectionString) && !LooksLikePlaceholder(_connectionString); }
        }

        public Task<ImprovementPageResult> GetPageAsync(ImprovementListQuery query, long? currentUserId)
        {
            return Task.Run(() => GetPageCore(query, currentUserId));
        }

        public Task<ImprovementRequestDto> GetByIdAsync(long reqId, long? currentUserId)
        {
            return Task.Run(() => GetByIdCore(reqId, currentUserId));
        }

        public Task<bool> SaveAsync(ImprovementRequestDto record)
        {
            return Task.Run(() => SaveCore(record));
        }

        public Task<bool> UpdateAdminFieldsAsync(long reqId, string status, string resolvedVersion, string adminNote)
        {
            return Task.Run(() => UpdateAdminFieldsCore(reqId, status, resolvedVersion, adminNote));
        }

        public Task<bool> DeleteByIdAsync(long reqId)
        {
            return Task.Run(() => DeleteByIdCore(reqId));
        }

        public Task<IList<ImprovementCommentDto>> GetCommentsAsync(long reqId)
        {
            return Task.Run(() => GetCommentsCore(reqId));
        }

        public Task<bool> SaveCommentAsync(ImprovementCommentDto comment)
        {
            return Task.Run(() => SaveCommentCore(comment));
        }

        public Task<bool> DeleteCommentAsync(long commentId)
        {
            return Task.Run(() => DeleteCommentCore(commentId));
        }

        public Task<bool> UpsertReactionAsync(long reqId, long userId, string reactionType)
        {
            return Task.Run(() => UpsertReactionCore(reqId, userId, reactionType));
        }

        public Task<bool> SaveAttachmentAsync(ImprovementAttachmentDto attachment)
        {
            return Task.Run(() => SaveAttachmentCore(attachment));
        }

        public Task<IList<ImprovementAttachmentDto>> GetAttachmentsAsync(long reqId, bool includeData)
        {
            return Task.Run(() => GetAttachmentsCore(reqId, includeData));
        }

        public Task<bool> DeleteAttachmentAsync(long attachId)
        {
            return Task.Run(() => DeleteAttachmentCore(attachId));
        }

        // ------------------------------------------------------------------
        // Core (동기) 구현
        // ------------------------------------------------------------------

        private ImprovementPageResult GetPageCore(ImprovementListQuery query, long? currentUserId)
        {
            var result = new ImprovementPageResult();
            if (query == null || !IsConfigured)
                return result;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return result;

                    string whereSql;
                    Action<OracleCommand> bindFilters;
                    BuildListFilter(query, out whereSql, out bindFilters);

                    using (var countCmd = CreateCommand(conn))
                    {
                        countCmd.CommandText = "SELECT COUNT(*) FROM " + ReqTable + " r WHERE 1=1" + whereSql;
                        bindFilters(countCmd);
                        result.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    string orderBy = ResolveSortExpr(query.SortMode);

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT " + ReqSelectBase + @"
                                FROM " + ReqTable + @" r
                               WHERE 1=1" + whereSql + @"
                               ORDER BY " + orderBy + @"
                               OFFSET :offset ROWS FETCH NEXT :pageSize ROWS ONLY";
                        cmd.Parameters.Add("myUserId", OracleDbType.Int64).Value =
                            currentUserId.HasValue ? (object)currentUserId.Value : DBNull.Value;
                        bindFilters(cmd);
                        cmd.Parameters.Add("offset", OracleDbType.Int32).Value = query.Offset;
                        cmd.Parameters.Add("pageSize", OracleDbType.Int32).Value = query.Take;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                result.Items.Add(ReadRequest(reader));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "GetPage failed: " + SafeError(ex));
            }

            return result;
        }

        private ImprovementRequestDto GetByIdCore(long reqId, long? currentUserId)
        {
            if (reqId <= 0 || !IsConfigured)
                return null;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return null;

                    ImprovementRequestDto dto;
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT " + ReqSelectBase + @"
                                FROM " + ReqTable + @" r
                               WHERE r.REQ_ID = :reqId";
                        cmd.Parameters.Add("myUserId", OracleDbType.Int64).Value =
                            currentUserId.HasValue ? (object)currentUserId.Value : DBNull.Value;
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = reqId;
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return null;
                            dto = ReadRequest(reader);
                        }
                    }

                    dto.Attachments = GetAttachmentsCore(reqId, true);
                    return dto;
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "GetById failed: " + SafeError(ex));
                return null;
            }
        }

        private bool SaveCore(ImprovementRequestDto record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Title) || record.UserId <= 0)
                return false;
            if (!IsConfigured)
                return false;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;

                    bool isNew = record.ReqId <= 0;
                    if (isNew)
                        InsertReq(conn, record);
                    else
                        UpdateReq(conn, record);
                    return true;
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "Save failed: " + SafeError(ex));
                return false;
            }
        }

        private void InsertReq(OracleConnection conn, ImprovementRequestDto r)
        {
            r.ReqId = NextVal(conn, ReqSeq);
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText =
                    @"INSERT INTO " + ReqTable + @"
                        (REQ_ID, REQUEST_TYPE, TITLE, DESCRIPTION, REPRODUCTION_STEPS, APP_VERSION,
                         SITE_CD, PC_NM, USER_ID, AUTHOR_NM, TEAM_NM, STATUS, CREATED_AT, UPDATED_AT)
                      VALUES
                        (:reqId, :reqType, :title, :descr, :repro, :appVer,
                         :siteCd, :pcNm, :userId, :authorNm, :teamNm, :status, SYSTIMESTAMP, SYSTIMESTAMP)";
                BindReqParams(cmd, r);
                cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = r.ReqId;
                cmd.ExecuteNonQuery();
            }
        }

        private void UpdateReq(OracleConnection conn, ImprovementRequestDto r)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText =
                    @"UPDATE " + ReqTable + @"
                         SET REQUEST_TYPE = :reqType,
                             TITLE = :title,
                             DESCRIPTION = :descr,
                             REPRODUCTION_STEPS = :repro,
                             APP_VERSION = :appVer,
                             SITE_CD = :siteCd,
                             PC_NM = :pcNm,
                             UPDATED_AT = SYSTIMESTAMP
                       WHERE REQ_ID = :reqId
                         AND USER_ID = :userId";
                cmd.Parameters.Add("reqType", OracleDbType.Varchar2).Value = Trim(r.RequestType, 20);
                cmd.Parameters.Add("title", OracleDbType.NVarchar2).Value = Trim(r.Title, 200);
                cmd.Parameters.Add("descr", OracleDbType.NVarchar2).Value = (object)Trim(r.Description, 4000) ?? DBNull.Value;
                cmd.Parameters.Add("repro", OracleDbType.NVarchar2).Value = (object)Trim(r.ReproductionSteps, 4000) ?? DBNull.Value;
                cmd.Parameters.Add("appVer", OracleDbType.Varchar2).Value = (object)Trim(r.AppVersion, 20) ?? DBNull.Value;
                cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = (object)Trim(r.SiteCode, 20) ?? DBNull.Value;
                cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = (object)Trim(r.PcName, 100) ?? DBNull.Value;
                cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = r.ReqId;
                cmd.Parameters.Add("userId", OracleDbType.Int64).Value = r.UserId;
                cmd.ExecuteNonQuery();
            }
        }

        private static void BindReqParams(OracleCommand cmd, ImprovementRequestDto r)
        {
            cmd.Parameters.Add("reqType", OracleDbType.Varchar2).Value = Trim(r.RequestType, 20);
            cmd.Parameters.Add("title", OracleDbType.NVarchar2).Value = Trim(r.Title, 200);
            cmd.Parameters.Add("descr", OracleDbType.NVarchar2).Value = (object)Trim(r.Description, 4000) ?? DBNull.Value;
            cmd.Parameters.Add("repro", OracleDbType.NVarchar2).Value = (object)Trim(r.ReproductionSteps, 4000) ?? DBNull.Value;
            cmd.Parameters.Add("appVer", OracleDbType.Varchar2).Value = (object)Trim(r.AppVersion, 20) ?? DBNull.Value;
            cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = (object)Trim(r.SiteCode, 20) ?? DBNull.Value;
            cmd.Parameters.Add("pcNm", OracleDbType.Varchar2).Value = (object)Trim(r.PcName, 100) ?? DBNull.Value;
            cmd.Parameters.Add("userId", OracleDbType.Int64).Value = r.UserId;
            cmd.Parameters.Add("authorNm", OracleDbType.NVarchar2).Value = Trim(r.AuthorName, 200);
            cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value = (object)Trim(r.TeamName, 100) ?? DBNull.Value;
            cmd.Parameters.Add("status", OracleDbType.Varchar2).Value =
                string.IsNullOrWhiteSpace(r.Status) ? ImprovementDefaultStatus : Trim(r.Status, 20);
        }

        private const string ImprovementDefaultStatus = "OPEN";

        private bool UpdateAdminFieldsCore(long reqId, string status, string resolvedVersion, string adminNote)
        {
            if (reqId <= 0 || !IsConfigured)
                return false;
            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;

                    using (var cmd = CreateCommand(conn))
                    {
                        bool resolving = string.Equals(status, "RESOLVED", StringComparison.OrdinalIgnoreCase);
                        cmd.CommandText =
                            @"UPDATE " + ReqTable + @"
                                 SET STATUS = :status,
                                     RESOLVED_VERSION = :resolvedVer,
                                     ADMIN_NOTE = :note,
                                     RESOLVED_AT = CASE WHEN :resolving = 1 THEN SYSTIMESTAMP ELSE RESOLVED_AT END,
                                     UPDATED_AT = SYSTIMESTAMP
                               WHERE REQ_ID = :reqId";
                        cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = Trim(status, 20);
                        cmd.Parameters.Add("resolvedVer", OracleDbType.Varchar2).Value =
                            (object)Trim(resolvedVersion, 20) ?? DBNull.Value;
                        cmd.Parameters.Add("note", OracleDbType.NVarchar2).Value =
                            (object)Trim(adminNote, 2000) ?? DBNull.Value;
                        cmd.Parameters.Add("resolving", OracleDbType.Int32).Value = resolving ? 1 : 0;
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = reqId;
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "UpdateAdminFields failed: " + SafeError(ex));
                return false;
            }
        }

        private bool DeleteByIdCore(long reqId)
        {
            if (reqId <= 0 || !IsConfigured)
                return false;
            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;

                    // 자식(댓글/반응/첨부)은 FK ON DELETE CASCADE로 정리되지만, 기존 관례를 따라 명시적으로도 지운다.
                    ExecuteDelete(conn, CommentTable, "REQ_ID", reqId);
                    ExecuteDelete(conn, ReactionTable, "REQ_ID", reqId);
                    ExecuteDelete(conn, AttachTable, "REQ_ID", reqId);

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText = "DELETE FROM " + ReqTable + " WHERE REQ_ID = :reqId";
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = reqId;
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "DeleteById failed: " + SafeError(ex));
                return false;
            }
        }

        private static void ExecuteDelete(OracleConnection conn, string table, string column, long id)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText = "DELETE FROM " + table + " WHERE " + column + " = :id";
                cmd.Parameters.Add("id", OracleDbType.Int64).Value = id;
                cmd.ExecuteNonQuery();
            }
        }

        private IList<ImprovementCommentDto> GetCommentsCore(long reqId)
        {
            var list = new List<ImprovementCommentDto>();
            if (reqId <= 0 || !IsConfigured)
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return list;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"SELECT c.COMMENT_ID, c.REQ_ID, c.USER_ID, c.AUTHOR_NM, c.COMMENT_TEXT, c.APP_VERSION, c.CREATED_AT, c.UPDATED_AT,
                                     c.PARENT_COMMENT_ID, c.IS_DELETED, c.TEAM_NM,
                                     (SELECT NVL(u.IS_DELETED,0) FROM XSUP.MSDWHTKD_USR u WHERE u.USR_ID = c.USER_ID) AS AUTHOR_IS_DELETED
                                FROM " + CommentTable + @" c
                               WHERE c.REQ_ID = :reqId
                               ORDER BY c.CREATED_AT ASC, c.COMMENT_ID ASC";
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = reqId;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new ImprovementCommentDto
                                {
                                    CommentId = Convert.ToInt64(reader.GetValue(0)),
                                    ReqId = Convert.ToInt64(reader.GetValue(1)),
                                    UserId = Convert.ToInt64(reader.GetValue(2)),
                                    AuthorName = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                    CommentText = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                                    AppVersion = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                                    CreatedAt = reader.IsDBNull(6) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(6)),
                                    UpdatedAt = reader.IsDBNull(7) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(7)),
                                    ParentCommentId = reader.IsDBNull(8) ? (long?)null : Convert.ToInt64(reader.GetValue(8)),
                                    IsDeleted = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetValue(9)) != 0,
                                    TeamName = reader.IsDBNull(10) ? null : Convert.ToString(reader.GetValue(10)),
                                    IsAuthorDeleted = !reader.IsDBNull(11) && Convert.ToInt32(reader.GetValue(11)) != 0
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "GetComments failed: " + SafeError(ex));
            }

            return list;
        }

        private bool SaveCommentCore(ImprovementCommentDto comment)
        {
            if (comment == null || string.IsNullOrWhiteSpace(comment.CommentText) || comment.ReqId <= 0 || comment.UserId <= 0)
                return false;
            if (!IsConfigured)
                return false;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;

                    if (comment.CommentId <= 0)
                    {
                        comment.CommentId = NextVal(conn, CommentSeq);
                        using (var cmd = CreateCommand(conn))
                        {
                            cmd.CommandText =
                                @"INSERT INTO " + CommentTable + @"
                                    (COMMENT_ID, REQ_ID, USER_ID, AUTHOR_NM, TEAM_NM, COMMENT_TEXT, APP_VERSION, CREATED_AT, UPDATED_AT, PARENT_COMMENT_ID)
                                  VALUES
                                    (:commentId, :reqId, :userId, :authorNm, :teamNm, :text, :appVer, SYSTIMESTAMP, SYSTIMESTAMP, :parentCommentId)";
                            cmd.Parameters.Add("commentId", OracleDbType.Int64).Value = comment.CommentId;
                            cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = comment.ReqId;
                            cmd.Parameters.Add("userId", OracleDbType.Int64).Value = comment.UserId;
                            cmd.Parameters.Add("authorNm", OracleDbType.NVarchar2).Value = Trim(comment.AuthorName, 200);
                            cmd.Parameters.Add("teamNm", OracleDbType.NVarchar2).Value = (object)Trim(comment.TeamName, 100) ?? DBNull.Value;
                            cmd.Parameters.Add("text", OracleDbType.NVarchar2).Value = Trim(comment.CommentText, 2000);
                            cmd.Parameters.Add("appVer", OracleDbType.Varchar2).Value = (object)Trim(comment.AppVersion, 20) ?? DBNull.Value;
                            cmd.Parameters.Add("parentCommentId", OracleDbType.Int64).Value =
                                comment.ParentCommentId.HasValue ? (object)comment.ParentCommentId.Value : DBNull.Value;
                            cmd.ExecuteNonQuery();
                        }
                        return true;
                    }

                    // 수정: 소유권/관리자 판단은 BIZ 계층 책임 — 여기서는 순수 CRUD만 수행.
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + CommentTable + @"
                                 SET COMMENT_TEXT = :text,
                                     UPDATED_AT = SYSTIMESTAMP
                               WHERE COMMENT_ID = :commentId";
                        cmd.Parameters.Add("text", OracleDbType.NVarchar2).Value = Trim(comment.CommentText, 2000);
                        cmd.Parameters.Add("commentId", OracleDbType.Int64).Value = comment.CommentId;
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "SaveComment failed: " + SafeError(ex));
                return false;
            }
        }

        /// <summary>
        /// 실제 DELETE 대신 IS_DELETED만 세운다 — 답글(대댓글)이 달려 있을 수 있어서 부모 행을
        /// 지워버리면 안 된다. 화면에서는 IsDeleted를 보고 "삭제된 댓글입니다"로 가려서 보여준다.
        /// </summary>
        private bool DeleteCommentCore(long commentId)
        {
            if (commentId <= 0 || !IsConfigured)
                return false;
            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"UPDATE " + CommentTable + @"
                                 SET IS_DELETED = 1,
                                     UPDATED_AT = SYSTIMESTAMP
                               WHERE COMMENT_ID = :commentId";
                        cmd.Parameters.Add("commentId", OracleDbType.Int64).Value = commentId;
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "DeleteComment failed: " + SafeError(ex));
                return false;
            }
        }

        /// <summary>REQ_ID+USER_ID 복합키 MERGE. ON절 컬럼(REQ_ID/USER_ID)은 갱신하지 않으므로 ORA-38104 해당 없음.</summary>
        private bool UpsertReactionCore(long reqId, long userId, string reactionType)
        {
            if (reqId <= 0 || userId <= 0 || string.IsNullOrWhiteSpace(reactionType) || !IsConfigured)
                return false;
            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"MERGE INTO " + ReactionTable + @" t
                              USING (SELECT :reqId AS REQ_ID, :userId AS USER_ID FROM DUAL) s
                                 ON (t.REQ_ID = s.REQ_ID AND t.USER_ID = s.USER_ID)
                              WHEN MATCHED THEN UPDATE SET
                                    t.REACTION_TYPE = :reactionType,
                                    t.UPDATED_AT = SYSTIMESTAMP
                              WHEN NOT MATCHED THEN INSERT
                                    (REQ_ID, USER_ID, REACTION_TYPE, CREATED_AT, UPDATED_AT)
                              VALUES (:reqId, :userId, :reactionType, SYSTIMESTAMP, SYSTIMESTAMP)";
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = reqId;
                        cmd.Parameters.Add("userId", OracleDbType.Int64).Value = userId;
                        cmd.Parameters.Add("reactionType", OracleDbType.Varchar2).Value = Trim(reactionType, 20);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "UpsertReaction failed: " + SafeError(ex));
                return false;
            }
        }

        private bool SaveAttachmentCore(ImprovementAttachmentDto attachment)
        {
            if (attachment == null || attachment.ReqId <= 0 || attachment.FileData == null || attachment.FileData.Length == 0)
                return false;
            if (!IsConfigured)
                return false;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;

                    attachment.AttachId = NextVal(conn, AttachSeq);
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            @"INSERT INTO " + AttachTable + @"
                                (ATTACH_ID, REQ_ID, FILE_NM, FILE_EXT, FILE_SIZE, CONTENT_KEY, FILE_DATA, CREATED_AT)
                              VALUES
                                (:attachId, :reqId, :fileNm, :fileExt, :fileSize, :contentKey, :fileData, SYSTIMESTAMP)";
                        cmd.Parameters.Add("attachId", OracleDbType.Int64).Value = attachment.AttachId;
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = attachment.ReqId;
                        cmd.Parameters.Add("fileNm", OracleDbType.NVarchar2).Value = Trim(attachment.FileName, 260);
                        cmd.Parameters.Add("fileExt", OracleDbType.Varchar2).Value = Trim(attachment.FileExt, 10);
                        cmd.Parameters.Add("fileSize", OracleDbType.Int64).Value = attachment.FileSize;
                        cmd.Parameters.Add("contentKey", OracleDbType.Varchar2).Value = (object)Trim(attachment.ContentKey, 20) ?? DBNull.Value;
                        cmd.Parameters.Add("fileData", OracleDbType.Blob).Value = attachment.FileData;
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "SaveAttachment failed: " + SafeError(ex));
                return false;
            }
        }

        private IList<ImprovementAttachmentDto> GetAttachmentsCore(long reqId, bool includeData)
        {
            var list = new List<ImprovementAttachmentDto>();
            if (reqId <= 0 || !IsConfigured)
                return list;

            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return list;

                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText =
                            (includeData
                                ? @"SELECT ATTACH_ID, REQ_ID, FILE_NM, FILE_EXT, FILE_SIZE, CONTENT_KEY, FILE_DATA, CREATED_AT FROM "
                                : @"SELECT ATTACH_ID, REQ_ID, FILE_NM, FILE_EXT, FILE_SIZE, CONTENT_KEY, CREATED_AT FROM ")
                            + AttachTable + " WHERE REQ_ID = :reqId ORDER BY CREATED_AT ASC, ATTACH_ID ASC";
                        cmd.Parameters.Add("reqId", OracleDbType.Int64).Value = reqId;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var dto = new ImprovementAttachmentDto
                                {
                                    AttachId = Convert.ToInt64(reader.GetValue(0)),
                                    ReqId = Convert.ToInt64(reader.GetValue(1)),
                                    FileName = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                                    FileExt = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                                    FileSize = reader.IsDBNull(4) ? 0 : Convert.ToInt64(reader.GetValue(4)),
                                    ContentKey = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5))
                                };
                                if (includeData)
                                {
                                    dto.CreatedAt = reader.IsDBNull(7) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(7));
                                    if (!reader.IsDBNull(6))
                                    {
                                        using (var blob = reader.GetOracleBlob(6))
                                            dto.FileData = blob.Value;
                                    }
                                }
                                else
                                {
                                    dto.CreatedAt = reader.IsDBNull(6) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(6));
                                }
                                list.Add(dto);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "GetAttachments failed: " + SafeError(ex));
            }

            return list;
        }

        private bool DeleteAttachmentCore(long attachId)
        {
            if (attachId <= 0 || !IsConfigured)
                return false;
            try
            {
                using (var conn = OpenConnection())
                {
                    EnsureReqTable(conn);
                    if (!_hasReqTable)
                        return false;
                    using (var cmd = CreateCommand(conn))
                    {
                        cmd.CommandText = "DELETE FROM " + AttachTable + " WHERE ATTACH_ID = :attachId";
                        cmd.Parameters.Add("attachId", OracleDbType.Int64).Value = attachId;
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkHubFileLogger.Warn("IMPROVEMENT", "DeleteAttachment failed: " + SafeError(ex));
                return false;
            }
        }

        // ------------------------------------------------------------------
        // 조회용 헬퍼
        // ------------------------------------------------------------------

        private static ImprovementRequestDto ReadRequest(OracleDataReader reader)
        {
            return new ImprovementRequestDto
            {
                ReqId = Convert.ToInt64(reader.GetValue(0)),
                RequestType = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                Title = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                Description = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3)),
                ReproductionSteps = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4)),
                AppVersion = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                SiteCode = reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6)),
                PcName = reader.IsDBNull(7) ? null : Convert.ToString(reader.GetValue(7)),
                UserId = Convert.ToInt64(reader.GetValue(8)),
                AuthorName = reader.IsDBNull(9) ? null : Convert.ToString(reader.GetValue(9)),
                TeamName = reader.IsDBNull(10) ? null : Convert.ToString(reader.GetValue(10)),
                Status = reader.IsDBNull(11) ? null : Convert.ToString(reader.GetValue(11)),
                ResolvedVersion = reader.IsDBNull(12) ? null : Convert.ToString(reader.GetValue(12)),
                AdminNote = reader.IsDBNull(13) ? null : Convert.ToString(reader.GetValue(13)),
                CreatedAt = reader.IsDBNull(14) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(14)),
                UpdatedAt = reader.IsDBNull(15) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(15)),
                ResolvedAt = reader.IsDBNull(16) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(16)),
                ReproducedCount = reader.IsDBNull(17) ? 0 : Convert.ToInt32(reader.GetValue(17)),
                NotReproducedCount = reader.IsDBNull(18) ? 0 : Convert.ToInt32(reader.GetValue(18)),
                CommentCount = reader.IsDBNull(19) ? 0 : Convert.ToInt32(reader.GetValue(19)),
                MyReaction = reader.IsDBNull(20) ? null : Convert.ToString(reader.GetValue(20)),
                IsAuthorDeleted = !reader.IsDBNull(21) && Convert.ToInt32(reader.GetValue(21)) != 0
            };
        }

        private static string ResolveSortExpr(string sortMode)
        {
            if (string.Equals(sortMode, "UNRESOLVED_FIRST", StringComparison.OrdinalIgnoreCase))
                return "CASE WHEN r.STATUS = 'RESOLVED' THEN 1 ELSE 0 END ASC, r.CREATED_AT DESC";
            if (string.Equals(sortMode, "MOST_REPRODUCED", StringComparison.OrdinalIgnoreCase))
                return "(SELECT COUNT(*) FROM " + ReactionTable + " x WHERE x.REQ_ID = r.REQ_ID AND x.REACTION_TYPE = 'REPRODUCED') DESC, r.CREATED_AT DESC";
            return "r.CREATED_AT DESC, r.REQ_ID DESC";
        }

        private void BuildListFilter(ImprovementListQuery query, out string whereSql, out Action<OracleCommand> bindFilters)
        {
            var sql = new StringBuilder();
            string type = string.IsNullOrWhiteSpace(query.RequestType) || IsAllSentinel(query.RequestType) ? null : query.RequestType.Trim();
            string status = string.IsNullOrWhiteSpace(query.Status) || IsAllSentinel(query.Status) ? null : query.Status.Trim();
            string site = string.IsNullOrWhiteSpace(query.SiteCode) || IsAllSentinel(query.SiteCode) ? null : query.SiteCode.Trim();
            string search = string.IsNullOrWhiteSpace(query.SearchText) ? null : query.SearchText.Trim();

            if (type != null) sql.Append(" AND UPPER(TRIM(r.REQUEST_TYPE)) = UPPER(:reqType)");
            if (status != null) sql.Append(" AND UPPER(TRIM(r.STATUS)) = UPPER(:status)");
            if (site != null) sql.Append(" AND UPPER(TRIM(r.SITE_CD)) = UPPER(:siteCd)");
            if (search != null)
                sql.Append(" AND (UPPER(r.TITLE) LIKE :q ESCAPE '\\' OR UPPER(r.DESCRIPTION) LIKE :q ESCAPE '\\')");

            whereSql = sql.ToString();
            bindFilters = cmd =>
            {
                if (type != null) cmd.Parameters.Add("reqType", OracleDbType.Varchar2).Value = type;
                if (status != null) cmd.Parameters.Add("status", OracleDbType.Varchar2).Value = status;
                if (site != null) cmd.Parameters.Add("siteCd", OracleDbType.Varchar2).Value = site;
                if (search != null) cmd.Parameters.Add("q", OracleDbType.NVarchar2).Value = "%" + EscapeLike(search.ToUpperInvariant()) + "%";
            };
        }

        private static bool IsAllSentinel(string value)
        {
            return string.Equals(value, "전체", StringComparison.Ordinal) || string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeLike(string value)
        {
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private void EnsureReqTable(OracleConnection conn)
        {
            if (_reqProbed)
                return;
            _reqProbed = true;
            try
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "SELECT 1 FROM " + ReqTable + " WHERE ROWNUM = 0";
                    cmd.ExecuteScalar();
                }
                _hasReqTable = true;
            }
            catch (OracleException ex)
            {
                if (ex.Number == OracleTableMissing)
                {
                    _hasReqTable = false;
                    WorkHubFileLogger.Warn("IMPROVEMENT",
                        "MSDWHTKD_REQ missing; skip. Apply sql/24_MSDWHTKD_REQ_DDL.sql when DBA can.");
                }
                else
                {
                    throw;
                }
            }
        }

        // ------------------------------------------------------------------
        // 공용 헬퍼 (OracleDirectoryRepository/OracleWorkLogRepository와 동일 관례)
        // ------------------------------------------------------------------

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

        private static long NextVal(OracleConnection conn, string sequenceName)
        {
            using (var cmd = CreateCommand(conn))
            {
                cmd.CommandText = "SELECT " + sequenceName + ".NEXTVAL FROM DUAL";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
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
            return upper.Contains("YOUR_") || upper.Contains("CHANGE_ME") || upper.Contains("TODO") || upper.Contains("PLACEHOLDER");
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
