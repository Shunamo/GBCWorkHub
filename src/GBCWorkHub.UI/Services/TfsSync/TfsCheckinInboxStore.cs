using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using Newtonsoft.Json;

namespace GBCWorkHub.UI.Services.TfsSync
{
    public static class TfsCheckinInboxStatus
    {
        public const string Waiting = "waiting";
        public const string Skipped = "skipped";
        public const string Reported = "reported";

        public static string ToLabel(string status)
        {
            if (string.Equals(status, Reported, StringComparison.OrdinalIgnoreCase))
                return "완료";
            if (string.Equals(status, Skipped, StringComparison.OrdinalIgnoreCase))
                return "스킵";
            return "대기";
        }

        public static bool IsReported(string status)
        {
            return string.Equals(status, Reported, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesStatus(string status, string expected)
        {
            string actual = string.IsNullOrWhiteSpace(status) ? Waiting : status;
            if (string.Equals(expected, Reported, StringComparison.OrdinalIgnoreCase))
                return IsReported(actual);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class TfsCheckinInboxRecord
    {
        public int ChangesetId { get; set; }
        public string Status { get; set; }
        public string OwnerKey { get; set; }
        public string SiteCode { get; set; }
        public string PcName { get; set; }
        public string CollectionUrl { get; set; }
        public string AuthorName { get; set; }
        public string AuthorId { get; set; }
        public string Comment { get; set; }
        public string CheckedInAt { get; set; }
        public int ChangedFileCount { get; set; }
        public List<TfsChangedFileItem> Files { get; set; }
        public DateTime FetchedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public TfsChangesetItem ToChangesetItem()
        {
            return new TfsChangesetItem
            {
                ChangesetId = ChangesetId,
                AuthorName = AuthorName,
                AuthorId = AuthorId,
                CheckedInAt = CheckedInAt,
                Comment = Comment,
                ChangedFileCount = ChangedFileCount,
                Files = Files != null
                    ? new List<TfsChangedFileItem>(Files)
                    : new List<TfsChangedFileItem>()
            };
        }
    }

    /// <summary>
    /// 가져오기로 받은 체크인 보관함. 로컬 %LocalAppData%\GBCWorkHub.
    /// reported / waiting / skipped. reported는 내리지 않는다.
    /// </summary>
    public static class TfsCheckinInboxStore
    {
        private static readonly object Sync = new object();

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GBCWorkHub");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "TfsCheckinInbox.json");
            }
        }

        public static string CurrentOwnerKey()
        {
            string occupancy = OccupancyNameStore.TryGet();
            if (!string.IsNullOrWhiteSpace(occupancy))
                return occupancy.Trim();
            string windows = RemotePcShareBiz.LocalWindowsAccount;
            if (!string.IsNullOrWhiteSpace(windows))
                return windows.Trim();
            return (Environment.UserName ?? string.Empty).Trim();
        }

        public static List<TfsCheckinInboxRecord> LoadMine()
        {
            string owner = CurrentOwnerKey();
            var all = LoadAll();
            var mine = new List<TfsCheckinInboxRecord>();
            foreach (var row in all)
            {
                if (row == null || row.ChangesetId <= 0)
                    continue;
                if (!string.IsNullOrWhiteSpace(row.OwnerKey)
                    && !string.Equals(row.OwnerKey, owner, StringComparison.OrdinalIgnoreCase))
                    continue;
                mine.Add(row);
            }
            mine.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            return mine;
        }

        public static void RenameOwner(string oldOwner, string newOwner)
        {
            if (string.IsNullOrWhiteSpace(oldOwner) || string.IsNullOrWhiteSpace(newOwner))
                return;
            if (string.Equals(oldOwner.Trim(), newOwner.Trim(), StringComparison.OrdinalIgnoreCase))
                return;

            string from = oldOwner.Trim();
            string to = newOwner.Trim();
            lock (Sync)
            {
                var list = LoadAllUnlocked();
                bool dirty = false;
                foreach (var row in list)
                {
                    if (row == null)
                        continue;
                    if (!OwnerMatches(row, from))
                        continue;
                    row.OwnerKey = to;
                    dirty = true;
                }
                if (dirty)
                    SaveUnlocked(list);
            }
        }

        public static void EnsureSiteCodes()
        {
            lock (Sync)
            {
                var list = LoadAllUnlocked();
                bool dirty = false;
                foreach (var row in list)
                {
                    if (row == null)
                        continue;
                    string site = ResolveSiteCode(row);
                    if (string.Equals(row.SiteCode, OtherSite, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(row.SiteCode, "OTHER", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SiteCode = site;
                        dirty = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(site)
                        && !string.Equals(row.SiteCode, site, StringComparison.OrdinalIgnoreCase))
                    {
                        row.SiteCode = site;
                        dirty = true;
                    }
                }
                if (dirty)
                    SaveUnlocked(list);
            }
        }

        public static string ResolveSiteCode(TfsCheckinInboxRecord row)
        {
            string known = NormalizeKnownSite(row != null ? row.SiteCode : null);
            if (known != null)
                return known;
            return InferSiteCode(
                row != null ? row.PcName : null,
                row != null ? row.CollectionUrl : null);
        }

        public const string OtherSite = "기타";
        public const string AllSites = "전체";

        public static readonly string[] FolderSiteCodes =
        {
            WorkLogSiteCodes.Aurora,
            WorkLogSiteCodes.Rc,
            WorkLogSiteCodes.Cmc,
            WorkLogSiteCodes.Mngha
        };

        public static string ResolveFolderSite(string siteCode, string pcName)
        {
            string known = NormalizeKnownSite(siteCode);
            if (known != null && known != OtherSite)
                return known;
            return InferSiteCode(pcName, null);
        }

        public static string InferSiteCode(string pcName, string collectionUrl)
        {
            string fromPc = InferFromConfiguredPcs(pcName);
            if (fromPc != null)
                return fromPc;

            string blob = ((pcName ?? string.Empty) + " " + (collectionUrl ?? string.Empty))
                .Trim()
                .ToUpperInvariant();
            if (blob.IndexOf("AURORA", StringComparison.Ordinal) >= 0)
                return WorkLogSiteCodes.Aurora;
            if (blob.IndexOf("MNGHA", StringComparison.Ordinal) >= 0
                || blob.IndexOf("MNG-HA", StringComparison.Ordinal) >= 0)
                return WorkLogSiteCodes.Mngha;
            if (blob.IndexOf("CMC", StringComparison.Ordinal) >= 0)
                return WorkLogSiteCodes.Cmc;
            if (blob.IndexOf("RC", StringComparison.Ordinal) >= 0)
                return WorkLogSiteCodes.Rc;

            return InferFromConfiguredPcs(collectionUrl);
        }

        private static string InferFromConfiguredPcs(string pcName)
        {
            if (string.IsNullOrWhiteSpace(pcName))
                return null;
            string needle = pcName.Trim();
            for (int i = 0; i < FolderSiteCodes.Length; i++)
            {
                string site = FolderSiteCodes[i];
                string namesRaw = ConfigurationManager.AppSettings[site + ".PcNames"] ?? string.Empty;
                string[] names = namesRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int n = 0; n < names.Length; n++)
                {
                    string pc = names[n].Trim();
                    if (pc.Length == 0)
                        continue;
                    if (string.Equals(pc, needle, StringComparison.OrdinalIgnoreCase))
                        return site;
                    if (needle.IndexOf(pc, StringComparison.OrdinalIgnoreCase) >= 0)
                        return site;
                }

                string host = ConfigurationManager.AppSettings[site + "." + needle + ".Host"];
                if (!string.IsNullOrWhiteSpace(host))
                    return site;
            }
            return null;
        }

        private static string NormalizeKnownSite(string site)
        {
            if (string.IsNullOrWhiteSpace(site))
                return null;
            string t = site.Trim().ToUpperInvariant();
            if (t == WorkLogSiteCodes.Aurora
                || t == WorkLogSiteCodes.Rc
                || t == WorkLogSiteCodes.Cmc
                || t == WorkLogSiteCodes.Mngha)
                return t;
            return null;
        }

        /// <summary>로컬 더미 체크인(900001~900005)을 보관함 JSON에서 지운다.</summary>
        public static void PurgeDummyRows()
        {
            var dummyIds = new HashSet<int>
            {
                TfsLocalDummyPayload.NewChangesetId,
                TfsLocalDummyPayload.ExistingInboxChangesetId,
                TfsLocalDummyPayload.AlreadyInWorkLogChangesetId,
                TfsLocalDummyPayload.NewChangesetId2,
                TfsLocalDummyPayload.NewChangesetId3
            };
            lock (Sync)
            {
                var list = LoadAllUnlocked();
                int before = list.Count;
                list.RemoveAll(r => r != null && dummyIds.Contains(r.ChangesetId));
                if (list.Count != before)
                    SaveUnlocked(list);
            }
        }

        public static int CountWaiting()
        {
            int n = 0;
            foreach (var row in LoadMine())
            {
                if (row != null
                    && string.Equals(row.Status, TfsCheckinInboxStatus.Waiting, StringComparison.OrdinalIgnoreCase))
                    n++;
            }
            return n;
        }

        public static string GetStatus(int changesetId)
        {
            if (changesetId <= 0)
                return null;
            foreach (var row in LoadMine())
            {
                if (row != null && row.ChangesetId == changesetId)
                    return row.Status;
            }
            return null;
        }

        /// <summary>가져오기 수신분. reported는 유지, skipped는 유지, 그 외는 waiting.</summary>
        public static void UpsertFetched(
            IEnumerable<TfsChangesetItem> items,
            TfsRecentChangesetsPayload payload,
            ISet<int> alreadyReportedIds)
        {
            if (items == null)
                return;

            string owner = CurrentOwnerKey();
            string pc = payload != null ? payload.ResolveComputerName() : null;
            string collection = payload != null ? payload.CollectionUrl : null;

            lock (Sync)
            {
                var list = LoadAllUnlocked();
                DateTime now = DateTime.Now;
                foreach (var item in items)
                {
                    if (item == null || item.ChangesetId <= 0)
                        continue;

                    TfsCheckinInboxRecord existing = Find(list, owner, item.ChangesetId);
                    bool reported = alreadyReportedIds != null
                        && alreadyReportedIds.Contains(item.ChangesetId);
                    if (existing != null && TfsCheckinInboxStatus.IsReported(existing.Status))
                        reported = true;

                    if (existing == null)
                    {
                        existing = new TfsCheckinInboxRecord
                        {
                            ChangesetId = item.ChangesetId,
                            OwnerKey = owner,
                            FetchedAt = now
                        };
                        list.Add(existing);
                    }

                    CopySnapshot(existing, item, pc, collection, now);
                    if (reported)
                        existing.Status = TfsCheckinInboxStatus.Reported;
                    else if (string.Equals(existing.Status, TfsCheckinInboxStatus.Skipped, StringComparison.OrdinalIgnoreCase))
                    {
                        // 이미 안 씀으로 둔 CS는 다시 받아도 유지
                    }
                    else
                        existing.Status = TfsCheckinInboxStatus.Waiting;
                }

                SaveUnlocked(list);
            }
        }

        public static void ApplyImportDecision(IEnumerable<int> selectedIds, IEnumerable<int> skippedIds)
        {
            string owner = CurrentOwnerKey();
            var selected = ToSet(selectedIds);
            var skipped = ToSet(skippedIds);

            lock (Sync)
            {
                var list = LoadAllUnlocked();
                DateTime now = DateTime.Now;
                foreach (var row in list)
                {
                    if (row == null || row.ChangesetId <= 0)
                        continue;
                    if (!OwnerMatches(row, owner))
                        continue;
                    if (TfsCheckinInboxStatus.IsReported(row.Status))
                        continue;

                    if (selected.Contains(row.ChangesetId))
                    {
                        row.Status = TfsCheckinInboxStatus.Waiting;
                        row.UpdatedAt = now;
                    }
                    else if (skipped.Contains(row.ChangesetId))
                    {
                        row.Status = TfsCheckinInboxStatus.Skipped;
                        row.UpdatedAt = now;
                    }
                }
                SaveUnlocked(list);
            }
        }

        public static void MarkReported(IEnumerable<int> changesetIds)
        {
            SetStatus(changesetIds, TfsCheckinInboxStatus.Reported, overwriteReported: true);
        }

        public static void MarkWaiting(IEnumerable<int> changesetIds)
        {
            SetStatus(changesetIds, TfsCheckinInboxStatus.Waiting, overwriteReported: false);
        }

        public static void MarkSkipped(IEnumerable<int> changesetIds)
        {
            SetStatus(changesetIds, TfsCheckinInboxStatus.Skipped, overwriteReported: false);
        }

        public static void ReconcileReported(ISet<int> registeredIds)
        {
            if (registeredIds == null || registeredIds.Count == 0)
                return;
            MarkReported(registeredIds);
        }

        private static void SetStatus(IEnumerable<int> changesetIds, string status, bool overwriteReported)
        {
            var ids = ToSet(changesetIds);
            if (ids.Count == 0)
                return;

            string owner = CurrentOwnerKey();
            lock (Sync)
            {
                var list = LoadAllUnlocked();
                DateTime now = DateTime.Now;
                foreach (var row in list)
                {
                    if (row == null || !ids.Contains(row.ChangesetId))
                        continue;
                    if (!OwnerMatches(row, owner))
                        continue;
                    if (!overwriteReported && TfsCheckinInboxStatus.IsReported(row.Status))
                        continue;
                    row.Status = status;
                    row.UpdatedAt = now;
                }
                SaveUnlocked(list);
            }
        }

        private static void CopySnapshot(
            TfsCheckinInboxRecord row,
            TfsChangesetItem item,
            string pc,
            string collection,
            DateTime now)
        {
            row.PcName = string.IsNullOrWhiteSpace(pc) ? row.PcName : pc;
            row.CollectionUrl = string.IsNullOrWhiteSpace(collection) ? row.CollectionUrl : collection;
            row.AuthorName = item.AuthorName;
            row.AuthorId = item.AuthorId;
            row.Comment = item.Comment;
            row.CheckedInAt = item.CheckedInAt;
            row.ChangedFileCount = item.ChangedFileCount.HasValue
                ? item.ChangedFileCount.Value
                : (item.Files != null ? item.Files.Count : 0);
            row.Files = item.Files != null
                ? new List<TfsChangedFileItem>(item.Files)
                : new List<TfsChangedFileItem>();
            if (string.IsNullOrWhiteSpace(row.SiteCode))
                row.SiteCode = InferSiteCode(pc, collection);
            if (row.FetchedAt == default(DateTime))
                row.FetchedAt = now;
            row.UpdatedAt = now;
            if (string.IsNullOrWhiteSpace(row.OwnerKey))
                row.OwnerKey = CurrentOwnerKey();
        }

        private static TfsCheckinInboxRecord Find(List<TfsCheckinInboxRecord> list, string owner, int changesetId)
        {
            foreach (var row in list)
            {
                if (row != null && row.ChangesetId == changesetId && OwnerMatches(row, owner))
                    return row;
            }
            return null;
        }

        private static bool OwnerMatches(TfsCheckinInboxRecord row, string owner)
        {
            if (row == null)
                return false;
            if (string.IsNullOrWhiteSpace(row.OwnerKey))
                return true;
            return string.Equals(row.OwnerKey, owner, StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<int> ToSet(IEnumerable<int> ids)
        {
            var set = new HashSet<int>();
            if (ids == null)
                return set;
            foreach (int id in ids)
            {
                if (id > 0)
                    set.Add(id);
            }
            return set;
        }

        private static List<TfsCheckinInboxRecord> LoadAll()
        {
            lock (Sync)
                return LoadAllUnlocked();
        }

        private static List<TfsCheckinInboxRecord> LoadAllUnlocked()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<TfsCheckinInboxRecord>();
                string json = File.ReadAllText(StorePath);
                var list = JsonConvert.DeserializeObject<List<TfsCheckinInboxRecord>>(json)
                    ?? new List<TfsCheckinInboxRecord>();
                if (StripLeftoverPreviewRows(list))
                    SaveUnlocked(list);
                return list;
            }
            catch
            {
                return new List<TfsCheckinInboxRecord>();
            }
        }

        private static bool StripLeftoverPreviewRows(List<TfsCheckinInboxRecord> list)
        {
            if (list == null || list.Count == 0)
                return false;
            bool dirty = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var row = list[i];
                if (row == null)
                    continue;
                if (!string.Equals(row.AuthorName, "preview", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.RemoveAt(i);
                dirty = true;
            }
            return dirty;
        }

        private static void SaveUnlocked(List<TfsCheckinInboxRecord> list)
        {
            try
            {
                string json = JsonConvert.SerializeObject(
                    list ?? new List<TfsCheckinInboxRecord>(),
                    Formatting.Indented);
                File.WriteAllText(StorePath, json);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("TFS_INBOX", "save failed: " + ex.Message);
            }
        }
    }
}
