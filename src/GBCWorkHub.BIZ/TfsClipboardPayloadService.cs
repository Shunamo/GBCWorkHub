using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GBCWorkHub.DTO;
using Newtonsoft.Json;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// GBCWORKHUB_TFS:: 클립보드 페이로드 파싱/검증
    /// </summary>
    public class TfsClipboardPayloadService
    {
        public const string ClipboardPrefix = "GBCWORKHUB_TFS::";
        public const string ExpectedType = "GBC_TFS_RECENT_CHANGESETS";

        public enum HandleResult
        {
            Ignored,
            ParseFailed,
            InvalidPayload,
            Success
        }

        public class ParseResult
        {
            public HandleResult Result { get; set; }
            public string Message { get; set; }
            public TfsRecentChangesetsPayload Payload { get; set; }
        }

        public ParseResult TryHandleClipboardText(string clipboardText)
        {
            var result = new ParseResult { Result = HandleResult.Ignored };

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                result.Message = "empty";
                return result;
            }

            string text = clipboardText.TrimStart();
            if (!text.StartsWith(ClipboardPrefix, StringComparison.Ordinal))
            {
                result.Message = "not tfs prefix";
                return result;
            }

            string json = text.Substring(ClipboardPrefix.Length).Trim();
            if (string.IsNullOrEmpty(json))
            {
                result.Result = HandleResult.ParseFailed;
                result.Message = "empty json";
                return result;
            }

            try
            {
                var payload = JsonConvert.DeserializeObject<TfsRecentChangesetsPayload>(json);
                if (payload == null)
                {
                    result.Result = HandleResult.ParseFailed;
                    result.Message = "null payload";
                    return result;
                }

                if (!string.Equals(payload.Type, ExpectedType, StringComparison.OrdinalIgnoreCase))
                {
                    result.Result = HandleResult.InvalidPayload;
                    result.Message = "unexpected type: " + (payload.Type ?? "null");
                    return result;
                }

                if (!payload.Success)
                {
                    result.Result = HandleResult.InvalidPayload;
                    result.Message = "success=false: " + (payload.Message ?? "");
                    return result;
                }

                if (payload.Changesets == null)
                {
                    result.Result = HandleResult.InvalidPayload;
                    result.Message = "changesets is null";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(payload.QueryMode))
                    payload.QueryMode = "RECENT_COUNT";

                result.Result = HandleResult.Success;
                result.Payload = payload;
                result.Message = "ok count=" + payload.Changesets.Count;
                return result;
            }
            catch (Exception ex)
            {
                result.Result = HandleResult.ParseFailed;
                result.Message = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        public static string BuildFileSummary(IList<TfsChangedFileItem> files, int? changedFileCount)
        {
            var list = files ?? new List<TfsChangedFileItem>();
            int total = changedFileCount.HasValue && changedFileCount.Value > 0
                ? changedFileCount.Value
                : list.Count;

            if (total <= 0 && list.Count == 0)
                return "(변경 파일 없음)";

            var names = list
                .Select(f => ResolveFileName(f))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (names.Count == 0)
                return total > 0 ? total + "개 파일" : "(변경 파일 없음)";

            if (names.Count <= 3 && total <= 3)
                return string.Join(", ", names);

            int shown = Math.Min(3, names.Count);
            int remaining = Math.Max(total - shown, names.Count - shown);
            if (remaining < 0)
                remaining = 0;

            return string.Join(", ", names.Take(shown)) + " 외 " + remaining + "개";
        }

        public static string BuildDefaultWorkTitle(TfsChangesetItem item)
        {
            if (item == null)
                return "Changeset 작업";

            if (!string.IsNullOrWhiteSpace(item.Comment))
                return item.Comment.Trim();

            return "Changeset " + item.ChangesetId + " 작업";
        }

        public static string BuildDefaultWorkContent(TfsChangesetItem item)
        {
            if (item == null || item.Files == null || item.Files.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var f in item.Files)
            {
                string name = ResolveFileName(f);
                string action = MapChangeTypeToKorean(f != null ? f.ChangeType : null);
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append("- ").Append(name).Append(" ").Append(action);
            }

            return sb.ToString();
        }

        public static string BuildQueryModeDisplay(TfsRecentChangesetsPayload payload)
        {
            if (payload == null)
                return "조회 기준: -";

            string mode = payload.QueryMode ?? "RECENT_COUNT";
            if (string.Equals(mode, "SESSION_WINDOW", StringComparison.OrdinalIgnoreCase))
            {
                string start = string.IsNullOrWhiteSpace(payload.SessionStartAt) ? "?" : payload.SessionStartAt;
                string end = string.IsNullOrWhiteSpace(payload.SessionEndAt) ? "?" : payload.SessionEndAt;
                return "조회 기준: 원격 접속 " + start + " ~ " + end;
            }

            if (string.Equals(mode, "TODAY_FALLBACK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "RECENT_FALLBACK", StringComparison.OrdinalIgnoreCase))
            {
                int fallbackCount = payload.Changesets != null ? payload.Changesets.Count : 0;
                return "조회 기준: 세션 구간 없음 → 오늘 동일 계정 " + fallbackCount + "건";
            }

            int count = payload.Changesets != null ? payload.Changesets.Count : 0;
            return "조회 기준: 최근 " + count + "건 · 테스트";
        }

        public static string MakeCandidateKey(string collectionUrl, int changesetId)
        {
            return (collectionUrl ?? string.Empty).Trim().ToLowerInvariant() + "|" + changesetId;
        }

        private static string ResolveFileName(TfsChangedFileItem f)
        {
            if (f == null)
                return "unknown";
            if (!string.IsNullOrWhiteSpace(f.FileName))
                return f.FileName.Trim();
            if (!string.IsNullOrWhiteSpace(f.ServerPath))
            {
                string path = f.ServerPath.Replace('\\', '/');
                int idx = path.LastIndexOf('/');
                return idx >= 0 && idx < path.Length - 1 ? path.Substring(idx + 1) : path;
            }
            return "unknown";
        }

        private static string MapChangeTypeToKorean(string changeType)
        {
            if (string.IsNullOrWhiteSpace(changeType))
                return "변경";

            string t = changeType.Trim().ToLowerInvariant();
            if (t.Contains("add") || t.Contains("추가"))
                return "추가";
            if (t.Contains("delete") || t.Contains("삭제") || t.Contains("remove"))
                return "삭제";
            if (t.Contains("rename") || t.Contains("이름"))
                return "이름변경";
            if (t.Contains("edit") || t.Contains("modify") || t.Contains("변경") || t.Contains("수정"))
                return "수정";
            return changeType.Trim();
        }
    }
}
