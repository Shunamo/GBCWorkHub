using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// TFS candidate → confirm → 목록 반영 → 순차 편집 큐 workflow.
    /// Dialog open/UI refresh는 ViewModel 책임. ViewModel 참조를 갖지 않는다.
    /// </summary>
    public sealed class WorkLogTfsImportCoordinator
    {
        private readonly Dictionary<string, WorkLogListItemViewModel> _importSessionBaselines
            = new Dictionary<string, WorkLogListItemViewModel>(StringComparer.Ordinal);
        private readonly ObservableCollection<ImportGroupSlot> _importGroups
            = new ObservableCollection<ImportGroupSlot>();
        private int _importEditTotal;
        private int _importEditIndex;
        private bool _isConfirmingImport;
        private bool _openingFromImportQueue;

        public bool IsConfirmingImport
        {
            get { return _isConfirmingImport; }
        }

        public bool OpeningFromImportQueue
        {
            get { return _openingFromImportQueue; }
        }

        public int ImportEditTotal
        {
            get { return _importEditTotal; }
        }

        public int ImportEditIndex
        {
            get { return _importEditIndex; }
        }

        public ObservableCollection<ImportGroupSlot> ImportGroups
        {
            get { return _importGroups; }
        }

        public bool HasImportGroups
        {
            get { return _importGroups.Count > 1; }
        }

        public bool HasUnfinishedImportGroups
        {
            get
            {
                return HasImportGroups
                    && _importGroups.Any(s => s != null && s.IsUnfinished);
            }
        }

        public ImportGroupSlot CurrentSlot
        {
            get { return _importGroups.FirstOrDefault(s => s != null && s.IsCurrent); }
        }

        public void RememberBaseline(WorkLogListItemViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id))
                return;
            if (_importSessionBaselines.ContainsKey(item.Id))
                return;
            _importSessionBaselines[item.Id] = WorkLogDraftMapper.CloneListItemBaseline(item);
        }

        public WorkLogListItemViewModel GetBaseline(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            WorkLogListItemViewModel baseline;
            return _importSessionBaselines.TryGetValue(itemId, out baseline) ? baseline : null;
        }

        public void RemoveBaseline(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return;
            _importSessionBaselines.Remove(itemId);
        }

        public void ClearBaselines()
        {
            _importSessionBaselines.Clear();
        }

        public void ClearEditQueue()
        {
            _importGroups.Clear();
            _importEditTotal = 0;
            _importEditIndex = 0;
        }

        public void ClearSession()
        {
            ClearEditQueue();
            ClearBaselines();
        }

        /// <summary>
        /// ImportDialog 선택 배치를 목록에 반영(DB 저장 없음).
        /// UI CloseImport / StartEditQueue / NewCandidateCount는 호출측.
        /// </summary>
        public WorkLogTfsImportApplyResult ApplySelectedBatches(
            IList<IList<TfsImportCandidateRow>> batches,
            bool isAppendExistingMode,
            WorkLogListItemViewModel appendTargetHint,
            WorkSessionContext sessionContext,
            ObservableCollection<WorkLogListItemViewModel> items,
            Action<WorkLogListItemViewModel> ensureItemSiteCode)
        {
            var result = new WorkLogTfsImportApplyResult();
            if (batches == null || batches.Count == 0 || items == null)
            {
                result.Aborted = true;
                return result;
            }

            if (ensureItemSiteCode == null)
                ensureItemSiteCode = _ => { };

            var ctx = sessionContext ?? new WorkSessionContext();
            EnsureSessionHasWorkHubUser(ctx);
            result.SessionContext = ctx;

            var toEdit = new List<WorkLogListItemViewModel>();
            int addedOrUpdated = 0;

            if (isAppendExistingMode)
            {
                var target = ResolveAppendTargetItem(items, appendTargetHint);
                if (target == null || !target.IsOwnedByCurrentUser)
                {
                    result.Aborted = true;
                    result.ErrorMessage =
                        target == null
                            ? "기존 업무기록 대상을 찾을 수 없습니다. 목록을 새로고침한 뒤 다시 시도하세요."
                            : "본인 이름으로 작성된 기록만 병합할 수 있습니다.";
                    return result;
                }

                var batch = batches[0];
                WorkLogListItemViewModel mapped = batch.Count == 1
                    ? WorkLogDraftMapper.FromImportRow(batch[0], ctx)
                    : WorkLogDraftMapper.FromImportRowsMerged(batch, ctx);
                if (mapped == null)
                {
                    result.Aborted = true;
                    return result;
                }

                ensureItemSiteCode(mapped);
                RememberBaseline(target);
                int appended = WorkLogDraftMapper.AppendIncomingToExisting(target, mapped);
                addedOrUpdated = 1;
                toEdit.Add(target);
                result.WasAppendMode = true;

                DiagnosticLogger.Info(
                    "WORKLOG_IMPORT_APPEND",
                    "targetId=" + (target.Id ?? "-")
                    + " dbLogId=" + target.DbLogId
                    + " ticket=" + (target.TicketNo ?? "-")
                    + " appendedSources=" + appended
                    + " selectedCs=" + batch.Count
                    + " pendingSave=True");
            }
            else
            {
                foreach (var batch in batches)
                {
                    if (batch == null || batch.Count == 0)
                        continue;

                    WorkLogListItemViewModel mapped;
                    if (batch.Count == 1)
                        mapped = WorkLogDraftMapper.FromImportRow(batch[0], ctx);
                    else
                        mapped = WorkLogDraftMapper.FromImportRowsMerged(batch, ctx);

                    if (mapped == null)
                        continue;

                    ensureItemSiteCode(mapped);

                    var existingDraft = FindDraftWithSameChangesets(items, mapped.GetEffectiveChangesetIds());
                    if (existingDraft != null)
                    {
                        toEdit.Add(existingDraft);
                        addedOrUpdated++;
                        continue;
                    }

                    items.Add(mapped);
                    addedOrUpdated++;
                    toEdit.Add(mapped);
                }
            }

            result.AddedOrUpdated = addedOrUpdated;
            result.ItemsToEdit = toEdit;
            result.Success = true;
            return result;
        }

        public void MarkPromptHandledIfNeeded(WorkLogTfsImportApplyResult applyResult, string reason)
        {
            if (applyResult == null || applyResult.AddedOrUpdated <= 0)
                return;
            var ctx = applyResult.SessionContext;
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.RemoteIp))
                return;
            TfsSyncPromptGate.MarkHandled(
                ctx.RemoteIp,
                ctx.SessionStartedAt ?? DateTime.Now,
                reason ?? "worklog_import_confirmed");
        }

        public bool TryBeginConfirm()
        {
            if (_isConfirmingImport)
                return false;
            _isConfirmingImport = true;
            return true;
        }

        public void EndConfirm()
        {
            _isConfirmingImport = false;
        }

        /// <summary>그룹 슬롯을 채우고 첫 미작성 항목을 반환.</summary>
        public WorkLogListItemViewModel StartEditQueue(
            IList<WorkLogListItemViewModel> items,
            ObservableCollection<WorkLogListItemViewModel> listItems)
        {
            ClearEditQueue();
            if (items == null || items.Count == 0)
                return null;

            int n = 0;
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                n++;
                _importGroups.Add(new ImportGroupSlot(n, item));
            }

            _importEditTotal = _importGroups.Count;
            _importEditIndex = 0;
            return TryTakeNextForEdit(listItems);
        }

        /// <summary>다음 Pending 그룹. 없으면 세션을 정리하고 null.</summary>
        public WorkLogListItemViewModel TryTakeNextForEdit(
            ObservableCollection<WorkLogListItemViewModel> listItems)
        {
            var current = CurrentSlot;
            int after = current != null ? current.GroupNumber : 0;
            ImportGroupSlot next = FindPendingAfter(after, listItems)
                ?? FindPendingAfter(0, listItems);
            if (next == null)
            {
                ClearEditQueue();
                ClearBaselines();
                return null;
            }

            ActivateSlot(next);
            return next.Item;
        }

        public void ActivateSlot(ImportGroupSlot slot)
        {
            if (slot == null)
                return;
            foreach (var s in _importGroups)
            {
                if (s != null)
                    s.IsCurrent = s == slot;
            }
            _importEditIndex = slot.GroupNumber;
        }

        public void MarkSlot(WorkLogListItemViewModel item, ImportGroupSlotStatus status)
        {
            var slot = FindSlot(item);
            if (slot == null)
                return;
            slot.Status = status;
            if (status == ImportGroupSlotStatus.Deleted)
                slot.IsCurrent = false;
        }

        public ImportGroupSlot FindSlot(WorkLogListItemViewModel item)
        {
            if (item == null)
                return null;
            return _importGroups.FirstOrDefault(s => s != null && ReferenceEquals(s.Item, item));
        }

        public IList<ImportGroupSlot> CollectUnfinishedSlots()
        {
            return _importGroups.Where(s => s != null && s.IsUnfinished).ToList();
        }

        public IList<ImportGroupSlot> CollectPendingSlots()
        {
            return _importGroups
                .Where(s => s != null && s.Status == ImportGroupSlotStatus.Pending)
                .ToList();
        }

        private ImportGroupSlot FindPendingAfter(
            int afterGroupNumber,
            ObservableCollection<WorkLogListItemViewModel> listItems)
        {
            foreach (var slot in _importGroups.OrderBy(s => s.GroupNumber))
            {
                if (slot == null || slot.GroupNumber <= afterGroupNumber)
                    continue;
                if (slot.Status != ImportGroupSlotStatus.Pending)
                    continue;
                if (listItems != null && !listItems.Contains(slot.Item))
                    continue;
                return slot;
            }
            return null;
        }

        public IDisposable EnterOpeningFromQueueScope()
        {
            _openingFromImportQueue = true;
            return new OpeningScope(this);
        }

        /// <summary>남은 Pending 항목을 목록에서 철회(미DB 제거 / baseline 복원).</summary>
        public void DiscardRemainingQueueItems(
            ObservableCollection<WorkLogListItemViewModel> items,
            Action afterListChanged)
        {
            foreach (var rem in CollectPendingSlots())
            {
                if (rem == null || rem.Item == null)
                    continue;
                var item = rem.Item;

                if (item.DbLogId <= 0 && items != null && items.Contains(item))
                {
                    items.Remove(item);
                    if (!string.IsNullOrEmpty(item.Id))
                        _importSessionBaselines.Remove(item.Id);
                    rem.Status = ImportGroupSlotStatus.Deleted;
                    continue;
                }

                var baseline = GetBaseline(item.Id);
                if (baseline != null)
                {
                    WorkLogDraftMapper.RestoreListItemFromBaseline(item, baseline);
                    if (!string.IsNullOrEmpty(item.Id))
                        _importSessionBaselines.Remove(item.Id);
                }
            }

            ClearEditQueue();
            ClearBaselines();
            if (afterListChanged != null)
                afterListChanged();
        }

        public static WorkLogListItemViewModel ResolveAppendTargetItem(
            ObservableCollection<WorkLogListItemViewModel> items,
            WorkLogListItemViewModel target)
        {
            if (target == null || items == null)
                return null;
            if (items.Contains(target))
                return target;

            string id = target.Id;
            long dbId = target.DbLogId;
            return items.FirstOrDefault(i =>
                i != null
                && ((!string.IsNullOrEmpty(id)
                        && string.Equals(i.Id, id, StringComparison.Ordinal))
                    || (dbId > 0 && i.DbLogId == dbId)));
        }

        public static WorkLogListItemViewModel FindDraftWithSameChangesets(
            ObservableCollection<WorkLogListItemViewModel> items,
            IList<int> changesetIds)
        {
            var want = NormalizeChangesetSet(changesetIds);
            if (want.Count == 0 || items == null)
                return null;

            foreach (var item in items)
            {
                if (item == null || item.IsCompleted)
                    continue;
                var have = NormalizeChangesetSet(item.GetEffectiveChangesetIds());
                if (have.Count != want.Count)
                    continue;
                bool same = true;
                for (int i = 0; i < want.Count; i++)
                {
                    if (have[i] != want[i])
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                    return item;
            }
            return null;
        }

        private static List<int> NormalizeChangesetSet(IList<int> ids)
        {
            var list = new List<int>();
            if (ids == null)
                return list;
            foreach (int id in ids)
            {
                if (id > 0 && !list.Contains(id))
                    list.Add(id);
            }
            list.Sort();
            return list;
        }

        public static WorkLogListItemViewModel FindExistingItemForImport(
            ObservableCollection<WorkLogListItemViewModel> items,
            WorkLogListItemViewModel mapped)
        {
            if (mapped == null || mapped.ChangesetId <= 0 || items == null)
                return null;

            string site = mapped.SiteCode ?? string.Empty;
            int csId = mapped.ChangesetId;
            return items.FirstOrDefault(i =>
            {
                if (i == null)
                    return false;
                if (!string.Equals(i.SiteCode ?? string.Empty, site, StringComparison.OrdinalIgnoreCase))
                    return false;
                var ids = i.GetEffectiveChangesetIds();
                return ids != null && ids.Contains(csId);
            });
        }

        public static void EnsureSessionHasWorkHubUser(WorkSessionContext ctx)
        {
            if (ctx == null)
                return;
            string localIp = WorkHubUserProfile.LocalIp;
            if (string.IsNullOrWhiteSpace(localIp))
                return;
            ctx.ClientLocalIp = localIp;
            if (string.IsNullOrWhiteSpace(ctx.CurrentUserName))
                ctx.CurrentUserName = WorkHubUserProfile.OccupancyName;
        }

        private sealed class OpeningScope : IDisposable
        {
            private readonly WorkLogTfsImportCoordinator _owner;
            private bool _disposed;

            public OpeningScope(WorkLogTfsImportCoordinator owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _owner._openingFromImportQueue = false;
            }
        }
    }

    public enum ImportGroupSlotStatus
    {
        Pending = 0,
        Draft = 1,
        Saved = 2,
        Deleted = 3
    }

    public sealed class ImportGroupSlot : ViewModelBase
    {
        private ImportGroupSlotStatus _status;
        private bool _isCurrent;

        public ImportGroupSlot(int groupNumber, WorkLogListItemViewModel item)
        {
            GroupNumber = groupNumber;
            Item = item;
        }

        public int GroupNumber { get; private set; }
        public WorkLogListItemViewModel Item { get; private set; }

        public string Label
        {
            get { return "그룹" + GroupNumber; }
        }

        public ImportGroupSlotStatus Status
        {
            get { return _status; }
            set
            {
                if (SetProperty(ref _status, value))
                {
                    RaisePropertyChanged("ShowGreenCheck");
                    RaisePropertyChanged("ShowGrayCheck");
                    RaisePropertyChanged("IsVisible");
                }
            }
        }

        public bool IsCurrent
        {
            get { return _isCurrent; }
            set { SetProperty(ref _isCurrent, value); }
        }

        public bool ShowGreenCheck
        {
            get { return Status == ImportGroupSlotStatus.Saved; }
        }

        public bool ShowGrayCheck
        {
            get { return Status == ImportGroupSlotStatus.Draft; }
        }

        public bool IsVisible
        {
            get { return Status != ImportGroupSlotStatus.Deleted; }
        }

        public bool IsUnfinished
        {
            get
            {
                return Status == ImportGroupSlotStatus.Pending
                    || Status == ImportGroupSlotStatus.Draft;
            }
        }
    }

    /// <summary>ApplySelectedBatches 결과. UI 반영은 ViewModel.</summary>
    public sealed class WorkLogTfsImportApplyResult
    {
        public bool Success { get; set; }
        public bool Aborted { get; set; }
        public string ErrorMessage { get; set; }
        public int AddedOrUpdated { get; set; }
        public bool WasAppendMode { get; set; }
        public IList<WorkLogListItemViewModel> ItemsToEdit { get; set; }
        public WorkSessionContext SessionContext { get; set; }
    }
}
