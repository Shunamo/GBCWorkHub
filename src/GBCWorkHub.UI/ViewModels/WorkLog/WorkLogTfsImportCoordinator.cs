using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.TfsSync;

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
        private readonly Queue<WorkLogListItemViewModel> _importEditQueue
            = new Queue<WorkLogListItemViewModel>();
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

        public bool HasPendingEditQueue
        {
            get { return _importEditQueue.Count > 0; }
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
            _importEditQueue.Clear();
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
                if (target == null)
                {
                    result.Aborted = true;
                    result.ErrorMessage =
                        "기존 업무기록 대상을 찾을 수 없습니다. 목록을 새로고침한 뒤 다시 시도하세요.";
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

                    if (batch.Count == 1 && mapped.ChangesetId > 0)
                    {
                        var existing = FindExistingItemForImport(items, mapped);
                        if (existing != null)
                        {
                            if (existing.IsCompleted)
                                continue;

                            RememberBaseline(existing);
                            WorkLogDraftMapper.AppendIncomingToExisting(existing, mapped);
                            toEdit.Add(existing);
                            addedOrUpdated++;
                            continue;
                        }
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

        /// <summary>큐를 채우고 첫 항목을 반환(목록에 있는 것만). 없으면 null.</summary>
        public WorkLogListItemViewModel StartEditQueue(
            IList<WorkLogListItemViewModel> items,
            ObservableCollection<WorkLogListItemViewModel> listItems)
        {
            ClearEditQueue();
            if (items == null || items.Count == 0)
                return null;

            foreach (var item in items)
            {
                if (item != null)
                    _importEditQueue.Enqueue(item);
            }

            _importEditTotal = _importEditQueue.Count;
            _importEditIndex = 0;
            return TryTakeNextForEdit(listItems);
        }

        /// <summary>다음 편집 대상. 큐가 비면 baseline도 정리하고 null.</summary>
        public WorkLogListItemViewModel TryTakeNextForEdit(
            ObservableCollection<WorkLogListItemViewModel> listItems)
        {
            while (_importEditQueue.Count > 0)
            {
                var next = _importEditQueue.Dequeue();
                _importEditIndex++;
                if (next == null || listItems == null || !listItems.Contains(next))
                    continue;
                return next;
            }

            ClearEditQueue();
            ClearBaselines();
            return null;
        }

        public IDisposable EnterOpeningFromQueueScope()
        {
            _openingFromImportQueue = true;
            return new OpeningScope(this);
        }

        /// <summary>남은 큐 항목을 목록에서 철회(미DB 제거 / baseline 복원).</summary>
        public void DiscardRemainingQueueItems(
            ObservableCollection<WorkLogListItemViewModel> items,
            Action afterListChanged)
        {
            while (_importEditQueue.Count > 0)
            {
                var rem = _importEditQueue.Dequeue();
                if (rem == null)
                    continue;

                if (rem.DbLogId <= 0 && items != null && items.Contains(rem))
                {
                    items.Remove(rem);
                    if (!string.IsNullOrEmpty(rem.Id))
                        _importSessionBaselines.Remove(rem.Id);
                    continue;
                }

                var baseline = GetBaseline(rem.Id);
                if (baseline != null)
                {
                    WorkLogDraftMapper.RestoreListItemFromBaseline(rem, baseline);
                    if (!string.IsNullOrEmpty(rem.Id))
                        _importSessionBaselines.Remove(rem.Id);
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
                ctx.CurrentUserName = localIp;
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
