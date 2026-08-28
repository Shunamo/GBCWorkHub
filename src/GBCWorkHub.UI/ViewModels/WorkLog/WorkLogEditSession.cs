using System;
using System.Collections.ObjectModel;
using GBCWorkHub.UI.Services;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 목록 화면의 편집 세션 상태( basline / dirty / discard / restore ).
    /// EditDialog UI·Persist는 ViewModel이 담당하고, 여기에는 세션 스냅샷만 둔다.
    /// </summary>
    public sealed class WorkLogEditSession
    {
        public WorkLogListItemViewModel EditingItem { get; private set; }
        public WorkLogListItemViewModel Baseline { get; private set; }
        public bool IsNew { get; private set; }
        public bool Persisted { get; private set; }

        public bool IsActive
        {
            get { return EditingItem != null; }
        }

        /// <summary>신규 작성 세션 시작 (목록에 아직 없을 수 있음).</summary>
        public void BeginNew(WorkLogListItemViewModel blank)
        {
            EditingItem = blank;
            Baseline = null;
            IsNew = true;
            Persisted = false;
        }

        /// <summary>
        /// 기존/가져오기 항목 편집 시작.
        /// preferredBaseline: 가져오기 append 직전 스냅샷 등.
        /// </summary>
        public void Begin(
            WorkLogListItemViewModel item,
            WorkLogListItemViewModel preferredBaseline)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            // DB 미저장(가져오기만 한 상태)은 신규와 동일 — 취소 시 목록에서 제거
            IsNew = item.DbLogId <= 0;
            EditingItem = item;
            Persisted = false;

            if (preferredBaseline != null)
            {
                Baseline = preferredBaseline;
                return;
            }

            if (Baseline == null
                || !string.Equals(Baseline.Id, item.Id, StringComparison.Ordinal))
            {
                Baseline = WorkLogDraftMapper.CloneListItemBaseline(item);
            }
        }

        public void MarkPersisted()
        {
            Persisted = true;
            if (EditingItem != null)
                Baseline = WorkLogDraftMapper.CloneListItemBaseline(EditingItem);
        }

        public void Clear()
        {
            EditingItem = null;
            Baseline = null;
            Persisted = false;
            IsNew = false;
        }

        /// <summary>
        /// 미저장 편집을 목록에서 철회. 신규(미DB)는 제거, 기존 건은 스냅샷 복원.
        /// </summary>
        /// <param name="onItemDiscarded">항목 Id — import baseline 제거 등.</param>
        /// <returns>목록이 변경되었으면 true.</returns>
        public bool TryDiscardUnsaved(
            ObservableCollection<WorkLogListItemViewModel> items,
            Action afterListChanged,
            Action<string> onItemDiscarded)
        {
            var item = EditingItem;
            var baseline = Baseline;
            if (item == null || Persisted)
                return false;

            if (item.DbLogId <= 0 && items != null && items.Contains(item))
            {
                items.Remove(item);
                if (onItemDiscarded != null && !string.IsNullOrEmpty(item.Id))
                    onItemDiscarded(item.Id);
                if (afterListChanged != null)
                    afterListChanged();
                DiagnosticLogger.Info(
                    "WORKLOG_EDIT_DISCARD",
                    "removedPending id=" + (item.Id ?? "-")
                    + " ticket=" + (item.TicketNo ?? "-"));
                return true;
            }

            if (baseline != null
                && string.Equals(baseline.Id, item.Id, StringComparison.Ordinal))
            {
                WorkLogDraftMapper.RestoreListItemFromBaseline(item, baseline);
                if (onItemDiscarded != null && !string.IsNullOrEmpty(item.Id))
                    onItemDiscarded(item.Id);
                if (afterListChanged != null)
                    afterListChanged();
                DiagnosticLogger.Info(
                    "WORKLOG_EDIT_DISCARD",
                    "restoredBaseline id=" + (item.Id ?? "-")
                    + " dbLogId=" + item.DbLogId
                    + " ticket=" + (item.TicketNo ?? "-"));
                return true;
            }

            return false;
        }
    }
}
