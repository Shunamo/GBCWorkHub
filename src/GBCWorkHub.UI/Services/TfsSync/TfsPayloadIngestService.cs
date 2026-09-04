using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>
    /// TFS 클립보드 Payload 수신 → 검증 → Author/중복 필터 → 후보 반영 → ACK
    /// </summary>
    public sealed class TfsPayloadIngestResult
    {
        public bool PrefixDetected { get; set; }
        public bool Parsed { get; set; }
        public bool CorrelationAccepted { get; set; }
        public bool Applied { get; set; }
        public bool AckWritten { get; set; }
        public string ErrorMessage { get; set; }
        public TfsRecentChangesetsPayload Payload { get; set; }

        public string RequestId { get; set; }
        public string DeliveryId { get; set; }
        public string ComputerName { get; set; }
        public string AuthorizedUserId { get; set; }
        public int ReturnedItemCount { get; set; }
        public int AuthorMatchedCount { get; set; }
        public int DuplicateExcludedCount { get; set; }
        public int AlreadyImportedExcludedCount { get; set; }
        public int FinalCandidateCount { get; set; }
        public int NewUnimportedCount { get; set; }
        public bool AuthorFilterSkipped { get; set; }
        public bool IsRecentFallback { get; set; }
        public string StatusMessage { get; set; }
    }

    public static class TfsPayloadIngestService
    {
        public const string DeliveryModePendingReconnect = "PENDING_RECONNECT";

        public static bool SkipAuthorFilter
        {
            get
            {
                try
                {
                    string raw = ConfigurationManager.AppSettings["Tfs.SkipAuthorFilter"];
                    if (string.IsNullOrWhiteSpace(raw))
                        return false;
                    return string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                        || raw.Trim() == "1";
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string FormatContext(TfsPayloadIngestResult r)
        {
            if (r == null)
                return "requestId=- deliveryId=- computerName=- authorizedUserId=- returnedItemCount=0 authorMatchedCount=0 duplicateExcludedCount=0 finalCandidateCount=0 errorMessage=-";

            return "requestId=" + (r.RequestId ?? "-")
                + " deliveryId=" + (r.DeliveryId ?? "-")
                + " computerName=" + (r.ComputerName ?? "-")
                + " authorizedUserId=" + (r.AuthorizedUserId ?? "-")
                + " returnedItemCount=" + r.ReturnedItemCount
                + " authorMatchedCount=" + r.AuthorMatchedCount
                + " duplicateExcludedCount=" + r.DuplicateExcludedCount
                + " finalCandidateCount=" + r.FinalCandidateCount
                + " errorMessage=" + (string.IsNullOrEmpty(r.ErrorMessage) ? "-" : r.ErrorMessage);
        }

        /// <summary>
        /// 파싱·상관검증·Author/중복 필터·후보 반영·ACK까지 수행.
        /// Payload가 유효하면 후보 0건이어도 ACK를 쓴다.
        /// </summary>
        public static TfsPayloadIngestResult Ingest(
            string clipboardText,
            TfsWorkLogViewModel tfsVm,
            TfsSyncRequest activeRequest,
            bool writeAck,
            ISet<int> excludeImportedChangesetIds = null)
        {
            var result = new TfsPayloadIngestResult();

            if (string.IsNullOrWhiteSpace(clipboardText)
                || !clipboardText.TrimStart().StartsWith(TfsClipboardPayloadService.ClipboardPrefix, StringComparison.Ordinal))
            {
                result.ErrorMessage = "not tfs prefix";
                return result;
            }

            result.PrefixDetected = true;
            // TFS_PREFIX_DETECTED는 ClipboardMonitor에서 이미 기록

            var parse = new TfsClipboardPayloadService().TryHandleClipboardText(clipboardText);
            if (parse.Result != TfsClipboardPayloadService.HandleResult.Success || parse.Payload == null)
            {
                result.ErrorMessage = parse.Message ?? parse.Result.ToString();
                DiagnosticLogger.Warn("TFS_PAYLOAD_REJECTED", FormatContext(result) + " reason=parse_failed");
                DiagnosticLogger.Error("TFS_PAYLOAD_PARSED", FormatContext(result) + " ok=False");
                return result;
            }

            var payload = parse.Payload;
            result.Parsed = true;
            result.Payload = payload;
            result.RequestId = payload.RequestId;
            result.DeliveryId = payload.DeliveryId;
            result.ComputerName = payload.ResolveComputerName();
            result.AuthorizedUserId = payload.AuthorizedUserId;
            result.ReturnedItemCount = payload.ReturnedItemCount.HasValue
                ? payload.ReturnedItemCount.Value
                : (payload.Changesets != null ? payload.Changesets.Count : 0);
            result.IsRecentFallback =
                string.Equals(payload.QueryMode, "TODAY_FALLBACK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(payload.QueryMode, "RECENT_FALLBACK", StringComparison.OrdinalIgnoreCase);

            DiagnosticLogger.Info("TFS_PAYLOAD_PARSED", FormatContext(result) + " ok=True success=" + payload.Success
                + " queryMode=" + (payload.QueryMode ?? "-"));

            if (!ValidateCorrelation(payload, activeRequest, result))
            {
                result.CorrelationAccepted = false;
                DiagnosticLogger.Warn("TFS_PAYLOAD_REJECTED", FormatContext(result));
                return result;
            }

            result.CorrelationAccepted = true;
            DiagnosticLogger.Info("TFS_DELIVERY_MODE_MATCHED", FormatContext(result)
                + " deliveryMode=" + (payload.DeliveryMode ?? "(empty-allowed)"));
            DiagnosticLogger.Info("TFS_PAYLOAD_ITEM_COUNT", FormatContext(result));

            bool skipAuthor = SkipAuthorFilter;
            result.AuthorFilterSkipped = skipAuthor;

            var sourceItems = payload.Changesets ?? new List<TfsChangesetItem>();
            string siteCode = TfsCheckinInboxStore.InferSiteCode(
                payload.ResolveComputerName(), payload.CollectionUrl);
            foreach (var item in sourceItems)
                NormalizeCheckedInAtToKorea(item, siteCode);
            string authorKey = ResolveAuthorFilterKey(payload);
            var authorMatched = new List<TfsChangesetItem>();

            if (skipAuthor)
            {
                authorMatched.AddRange(sourceItems.Where(x => x != null));
                result.AuthorMatchedCount = authorMatched.Count;
                DiagnosticLogger.Info("TFS_AUTHOR_FILTER_RESULT", FormatContext(result)
                    + " skipped=True filterKey=" + (authorKey ?? "-"));
                LogChangesetFileStats(clipboardText, authorMatched);
            }
            else
            {
                foreach (var item in sourceItems)
                {
                    if (item == null)
                        continue;
                    if (IsAuthorMatch(item, authorKey))
                        authorMatched.Add(item);
                }
                result.AuthorMatchedCount = authorMatched.Count;
                DiagnosticLogger.Info("TFS_AUTHOR_FILTER_RESULT", FormatContext(result)
                    + " skipped=False filterKey=" + (authorKey ?? "-"));
                LogChangesetFileStats(clipboardText, authorMatched);
            }

            // 오늘 폴백(및 구 RECENT_FALLBACK): 로컬 오늘 체크인만 유지
            if (result.IsRecentFallback)
            {
                int beforeToday = authorMatched.Count;
                authorMatched = authorMatched.Where(item => IsCheckedInTodayKorea(item, siteCode)).ToList();
                DiagnosticLogger.Info("TFS_TODAY_FILTER", FormatContext(result)
                    + " before=" + beforeToday + " after=" + authorMatched.Count);
            }

            int alreadyImportedExcluded = 0;
            var toApply = authorMatched;
            if (excludeImportedChangesetIds != null && excludeImportedChangesetIds.Count > 0)
            {
                toApply = new List<TfsChangesetItem>();
                foreach (var item in authorMatched)
                {
                    if (item == null)
                        continue;
                    if (excludeImportedChangesetIds.Contains(item.ChangesetId))
                    {
                        alreadyImportedExcluded++;
                        continue;
                    }
                    toApply.Add(item);
                }
            }
            result.AlreadyImportedExcludedCount = alreadyImportedExcluded;
            result.NewUnimportedCount = toApply.Count;

            try
            {
                TfsCheckinInboxStore.UpsertFetched(authorMatched, payload, excludeImportedChangesetIds);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_INBOX", "upsert fetched failed: " + ex.Message);
            }

            int beforeCount = tfsVm != null ? tfsVm.Candidates.Count : 0;
            int added = 0;
            int duplicates = 0;

            if (tfsVm != null)
            {
                // 폴백은 이전 누적 후보를 비우고 이번 결과만 반영 (예전 20건 잔존 방지)
                var apply = tfsVm.ApplyFilteredChangesets(payload, toApply, replaceExisting: result.IsRecentFallback);
                added = apply.Added;
                duplicates = apply.Duplicates;
                result.FinalCandidateCount = tfsVm.Candidates.Count;
            }
            else
            {
                result.FinalCandidateCount = 0;
            }

            result.DuplicateExcludedCount = duplicates;
            DiagnosticLogger.Info("TFS_DUPLICATE_FILTER_RESULT", FormatContext(result)
                + " added=" + added + " beforeCount=" + beforeCount
                + " alreadyImportedExcluded=" + alreadyImportedExcluded
                + " newUnimported=" + result.NewUnimportedCount);

            if (result.AuthorMatchedCount == 0 && !skipAuthor && result.ReturnedItemCount > 0)
            {
                result.StatusMessage = "현재 연결된 TFS 사용자가 작성한 체크인이 없습니다.";
            }
            else if (result.IsRecentFallback && result.NewUnimportedCount == 0)
            {
                result.StatusMessage = alreadyImportedExcluded > 0
                    ? "접속 구간에 체크인이 없고, 오늘 체크인은 모두 업무기록에 있습니다."
                    : "접속 구간에 체크인이 없고, 오늘 미등록 체크인도 없습니다.";
            }
            else if (result.FinalCandidateCount == 0 && result.ReturnedItemCount == 0)
            {
                result.StatusMessage = "수신된 체크인이 없습니다.";
            }
            else if (result.IsRecentFallback)
            {
                result.StatusMessage = "오늘 미등록 후보 " + result.NewUnimportedCount + "건"
                    + (alreadyImportedExcluded > 0 ? " (등록됨 " + alreadyImportedExcluded + "건 제외)" : "");
            }
            else
            {
                result.StatusMessage = "후보 " + result.FinalCandidateCount + "건 (신규 " + added
                    + ", 중복제외 " + duplicates + ")";
            }

            if (tfsVm != null)
            {
                tfsVm.StatusMessage = result.StatusMessage;
                tfsVm.LastReceiveMessage = result.StatusMessage;
                DiagnosticLogger.Info("TFS_PAYLOAD_SAVED", FormatContext(result));
                DiagnosticLogger.Info("TFS_UI_UPDATED", FormatContext(result));
            }

            result.Applied = true;

            if (writeAck && !string.IsNullOrWhiteSpace(payload.DeliveryId))
            {
                result.AckWritten = TfsClipboardAckService.TryWriteAck(payload.DeliveryId);
                DiagnosticLogger.Info("TFS_ACK_WRITTEN", FormatContext(result)
                    + " ok=" + result.AckWritten);
            }
            else if (writeAck)
            {
                result.ErrorMessage = "deliveryId empty — ACK skipped";
                DiagnosticLogger.Warn("TFS_ACK_WRITTEN", FormatContext(result) + " ok=False");
            }

            return result;
        }

        private static bool ValidateCorrelation(
            TfsRecentChangesetsPayload payload,
            TfsSyncRequest activeRequest,
            TfsPayloadIngestResult result)
        {
            if (payload == null)
            {
                result.ErrorMessage = "payload null";
                return false;
            }

            if (!payload.Success)
            {
                result.ErrorMessage = "success=false";
                return false;
            }

            // requestId: 동기화 대기 중이면 필수 일치
            if (activeRequest != null && !string.IsNullOrWhiteSpace(activeRequest.RequestId))
            {
                if (string.IsNullOrWhiteSpace(payload.RequestId)
                    || !string.Equals(activeRequest.RequestId.Trim(), payload.RequestId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result.ErrorMessage = "requestId mismatch local=" + activeRequest.RequestId
                        + " remote=" + (payload.RequestId ?? "-");
                    DiagnosticLogger.Warn("TFS_REQUEST_ID_MISMATCH", FormatContext(result));
                    return false;
                }
                DiagnosticLogger.Info("TFS_REQUEST_ID_MATCHED", FormatContext(result));
            }
            else if (!string.IsNullOrWhiteSpace(payload.RequestId) && activeRequest != null
                && !string.IsNullOrWhiteSpace(activeRequest.RequestId)
                && !string.Equals(activeRequest.RequestId.Trim(), payload.RequestId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "requestId mismatch local=" + activeRequest.RequestId
                    + " remote=" + payload.RequestId;
                DiagnosticLogger.Warn("TFS_REQUEST_ID_MISMATCH", FormatContext(result));
                return false;
            }
            else
            {
                DiagnosticLogger.Info("TFS_REQUEST_ID_MATCHED", FormatContext(result)
                    + " note=skip_missing");
            }

            string pc = payload.ResolveComputerName();
            if (activeRequest != null
                && !string.IsNullOrWhiteSpace(activeRequest.RemoteComputerName)
                && !string.IsNullOrWhiteSpace(pc)
                && !RdpStatusBiz.ComputerNamesLooselyMatch(pc, activeRequest.RemoteComputerName))
            {
                result.ErrorMessage = "computerName mismatch local=" + activeRequest.RemoteComputerName
                    + " remote=" + pc;
                DiagnosticLogger.Warn("TFS_COMPUTER_MISMATCH", FormatContext(result));
                return false;
            }

            DiagnosticLogger.Info("TFS_COMPUTER_MATCHED", FormatContext(result));

            // deliveryMode: 활성 요청 중에는 PENDING_RECONNECT 또는 DISCONNECT(RC) 허용. 없으면 직접 수신.
            if (activeRequest != null
                && !string.IsNullOrWhiteSpace(payload.DeliveryMode)
                && !string.Equals(payload.DeliveryMode, DeliveryModePendingReconnect, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(payload.DeliveryMode, "DISCONNECT", StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "deliveryMode mismatch: " + payload.DeliveryMode;
                DiagnosticLogger.Warn("TFS_DELIVERY_MODE_MATCHED", FormatContext(result) + " ok=False");
                return false;
            }

            return true;
        }

        private static string ResolveAuthorFilterKey(TfsRecentChangesetsPayload payload)
        {
            string fromPayload = payload != null
                ? NormalizeUserId(payload.AuthorizedUserId)
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(fromPayload) && !IsOccupancyIdentity(fromPayload))
                return fromPayload;
            return NormalizeUserId(Environment.UserDomainName + "\\" + Environment.UserName);
        }

        private static bool IsOccupancyIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string occupancy = OccupancyNameStore.TryGet();
            if (!string.IsNullOrWhiteSpace(occupancy)
                && string.Equals(value.Trim(), occupancy, StringComparison.OrdinalIgnoreCase))
                return true;
            string display = OccupancyNameStore.DisplayName;
            return !string.IsNullOrWhiteSpace(display)
                && string.Equals(value.Trim(), display, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>한국 날짜 기준 오늘 체크인만 (오늘 폴백 안전망).</summary>
        public static bool IsCheckedInTodayLocal(TfsChangesetItem item)
        {
            return IsCheckedInTodayKorea(item, null);
        }

        public static bool IsCheckedInTodayKorea(TfsChangesetItem item, string siteCode)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.CheckedInAt))
                return false;
            DateTime? korea = KoreaTime.ParseToKorea(item.CheckedInAt, siteCode);
            return korea.HasValue && korea.Value.Date == KoreaTime.Today;
        }

        private static void NormalizeCheckedInAtToKorea(TfsChangesetItem item, string siteCode)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.CheckedInAt))
                return;
            DateTime? korea = KoreaTime.ParseToKorea(item.CheckedInAt, siteCode);
            if (!korea.HasValue)
                return;
            item.CheckedInAt = KoreaTime.ToRoundTripUtc(korea.Value);
        }

        public static bool IsAuthorMatch(TfsChangesetItem item, string filterKey)
        {
            if (item == null || string.IsNullOrWhiteSpace(filterKey))
                return false;

            string a = NormalizeUserId(item.AuthorId);
            string b = NormalizeUserId(item.AuthorName);
            if (!string.IsNullOrEmpty(a) && string.Equals(a, filterKey, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(b) && string.Equals(b, filterKey, StringComparison.OrdinalIgnoreCase))
                return true;

            // DOMAIN\user vs user
            string filterUser = filterKey;
            int slash = filterKey.LastIndexOf('\\');
            if (slash >= 0 && slash < filterKey.Length - 1)
                filterUser = filterKey.Substring(slash + 1);

            if (!string.IsNullOrEmpty(a))
            {
                int aslash = a.LastIndexOf('\\');
                string aUser = aslash >= 0 ? a.Substring(aslash + 1) : a;
                if (string.Equals(aUser, filterUser, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string NormalizeUserId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        /// <summary>
        /// files 배열 유무 진단 (경로/파일명 본문은 로그하지 않음).
        /// </summary>
        private static void LogChangesetFileStats(string clipboardText, IList<TfsChangesetItem> items)
        {
            string raw = clipboardText ?? string.Empty;
            bool hasFilesKey = raw.IndexOf("\"files\"", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("\"changedFiles\"", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasServerPathKey = raw.IndexOf("\"serverPath\"", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase) >= 0;

            int totalFiles = 0;
            int emptyFileLists = 0;
            var parts = new List<string>();
            if (items != null)
            {
                foreach (var cs in items)
                {
                    if (cs == null)
                        continue;
                    int n = cs.Files != null ? cs.Files.Count : 0;
                    totalFiles += n;
                    if (n == 0)
                        emptyFileLists++;
                    parts.Add("cs=" + cs.ChangesetId
                        + ":files=" + n
                        + ":countProp=" + (cs.ChangedFileCount.HasValue ? cs.ChangedFileCount.Value.ToString() : "-"));
                }
            }

            DiagnosticLogger.Info("TFS_CHANGESET_FILE_STATS",
                "rawHasFilesKey=" + hasFilesKey
                + " rawHasPathKey=" + hasServerPathKey
                + " changesetCount=" + (items != null ? items.Count : 0)
                + " totalParsedFiles=" + totalFiles
                + " emptyFileLists=" + emptyFileLists
                + " detail=[" + string.Join("; ", parts) + "]");
        }

        /// <summary>
        /// 운영 저장 시 타 사용자 Changeset 자동 등록 금지 (SkipAuthorFilter=false일 때).
        /// </summary>
        public static bool CanSaveAsCurrentUserWorkLog(TfsChangesetCandidateViewModel candidate, string authorizedUserId)
        {
            if (SkipAuthorFilter)
                return true;
            if (candidate == null)
                return false;

            string key = !string.IsNullOrWhiteSpace(authorizedUserId)
                ? NormalizeUserId(authorizedUserId)
                : NormalizeUserId(Environment.UserDomainName + "\\" + Environment.UserName);

            var fake = new TfsChangesetItem
            {
                AuthorId = candidate.AuthorId,
                AuthorName = candidate.AuthorName
            };
            return IsAuthorMatch(fake, key);
        }
    }

    public sealed class ApplyChangesetsResult
    {
        public int Added { get; set; }
        public int Duplicates { get; set; }
    }
}
