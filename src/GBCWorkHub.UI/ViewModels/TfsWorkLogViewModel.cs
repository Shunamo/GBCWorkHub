using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>
    /// TFS 형상관리 탭 ViewModel
    /// </summary>
    public class TfsWorkLogViewModel : ViewModelBase
    {
        private readonly TfsClipboardPayloadService _payloadService = new TfsClipboardPayloadService();
        private readonly TfsWorkLogBiz _workLogBiz = new TfsWorkLogBiz();
        private readonly ITfsWorkLogParser _workLogParser = new TfsWorkLogParser();
        private IPopupService _popup;
        private string _lastAuthorizedUserId;
        private WorkSessionContext _sessionContext = new WorkSessionContext();

        private ObservableCollection<TfsChangesetCandidateViewModel> _candidates =
            new ObservableCollection<TfsChangesetCandidateViewModel>();
        private TfsChangesetCandidateViewModel _selectedCandidate;
        private string _queryModeDisplay = "조회 기준: (대기 중)";
        private string _statusMessage = "TFS Changeset 후보를 기다리는 중";
        private string _lastReceiveMessage = "-";
        private string _parsePreviewText = "Changeset을 선택하면 WorkLogDraft 파싱 미리보기가 표시됩니다.";
        private ObservableCollection<WorkLogExcelPreviewRow> _parsePreviewRows =
            new ObservableCollection<WorkLogExcelPreviewRow>();
        private bool _isSaving;

        public TfsWorkLogViewModel()
        {
            SaveSelectedCommand = new RelayCommand(
                async () => await SaveSelectedAsync(),
                () => !_isSaving && Candidates.Any(c => c.IsSelected));
        }

        public ObservableCollection<TfsChangesetCandidateViewModel> Candidates
        {
            get { return _candidates; }
            private set { SetProperty(ref _candidates, value); }
        }

        public TfsChangesetCandidateViewModel SelectedCandidate
        {
            get { return _selectedCandidate; }
            set
            {
                if (SetProperty(ref _selectedCandidate, value))
                    RefreshParsePreview();
            }
        }

        public string QueryModeDisplay
        {
            get { return _queryModeDisplay; }
            set { SetProperty(ref _queryModeDisplay, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        public string LastReceiveMessage
        {
            get { return _lastReceiveMessage; }
            set { SetProperty(ref _lastReceiveMessage, value); }
        }

        /// <summary>개발 확인용 Parsed Preview 텍스트.</summary>
        public string ParsePreviewText
        {
            get { return _parsePreviewText; }
            private set { SetProperty(ref _parsePreviewText, value); }
        }

        public ObservableCollection<WorkLogExcelPreviewRow> ParsePreviewRows
        {
            get { return _parsePreviewRows; }
            private set { SetProperty(ref _parsePreviewRows, value); }
        }

        public ICommand SaveSelectedCommand { get; private set; }

        public void AttachPopup(IPopupService popup)
        {
            _popup = popup;
        }

        /// <summary>
        /// MainViewModel 등에서 세션 컨텍스트(PC/담당자/시작·종료)를 갱신.
        /// </summary>
        public void UpdateSessionContext(WorkSessionContext context)
        {
            if (context != null)
                _sessionContext = context;
            RefreshParsePreview();
        }

        public void HandleTfsClipboardText(string clipboardText)
        {
            // MainViewModel에서 Ingest 파이프라인을 우선 사용. 직접 호출 시에도 동일 경로.
            TfsPayloadIngestService.Ingest(clipboardText, this, activeRequest: null, writeAck: true);
        }

        /// <summary>
        /// Author 필터를 통과한 Changeset만 후보에 반영 (UI 스레드 동기 실행).
        /// </summary>
        public ApplyChangesetsResult ApplyFilteredChangesets(
            TfsRecentChangesetsPayload payload,
            System.Collections.Generic.IList<TfsChangesetItem> items,
            bool replaceExisting = false)
        {
            var result = new ApplyChangesetsResult();
            if (payload == null)
                return result;

            _lastAuthorizedUserId = payload.AuthorizedUserId;
            MergeSessionFromPayload(payload);

            RunOnUiSync(() =>
            {
                QueryModeDisplay = TfsClipboardPayloadService.BuildQueryModeDisplay(payload);

                if (replaceExisting)
                {
                    Candidates.Clear();
                    SelectedCandidate = null;
                }

                if (items == null || items.Count == 0)
                {
                    ((RelayCommand)SaveSelectedCommand).RaiseCanExecuteChanged();
                    RaiseCandidatesChanged();
                    return;
                }

                foreach (var item in items)
                {
                    if (item == null)
                        continue;

                    string key = TfsClipboardPayloadService.MakeCandidateKey(payload.CollectionUrl, item.ChangesetId);
                    var existing = Candidates.FirstOrDefault(c =>
                        string.Equals(c.CandidateKey, key, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.ApplySourceInfo(payload, item, preserveUserEdits: true);
                        result.Duplicates++;
                    }
                    else
                    {
                        Candidates.Add(TfsChangesetCandidateViewModel.FromPayloadItem(payload, item));
                        result.Added++;
                    }
                }

                if (SelectedCandidate == null && Candidates.Count > 0)
                    SelectedCandidate = Candidates[0];
                else
                    RefreshParsePreview();

                ((RelayCommand)SaveSelectedCommand).RaiseCanExecuteChanged();
                RaiseCandidatesChanged();
            });

            return result;
        }

        public event Action CandidatesChanged;

        public WorkSessionContext CurrentSessionContext
        {
            get { return _sessionContext; }
        }

        private void RaiseCandidatesChanged()
        {
            var h = CandidatesChanged;
            if (h != null)
                h();
        }

        private void MergeSessionFromPayload(TfsRecentChangesetsPayload payload)
        {
            if (payload == null)
                return;

            if (string.IsNullOrWhiteSpace(_sessionContext.RemoteComputerName))
                _sessionContext.RemoteComputerName = payload.ResolveComputerName();

            DateTime tmp;
            if (!_sessionContext.SessionStartedAt.HasValue
                && !string.IsNullOrWhiteSpace(payload.SessionStartAt)
                && DateTime.TryParse(payload.SessionStartAt, out tmp))
                _sessionContext.SessionStartedAt = tmp;

            if (!_sessionContext.SessionEndedAt.HasValue
                && !string.IsNullOrWhiteSpace(payload.SessionEndAt)
                && DateTime.TryParse(payload.SessionEndAt, out tmp))
                _sessionContext.SessionEndedAt = tmp;

            if (string.IsNullOrWhiteSpace(_sessionContext.CurrentUserId))
                _sessionContext.CurrentUserId = RemotePcShareBiz.LocalUserAccount;
            if (string.IsNullOrWhiteSpace(_sessionContext.CurrentUserName))
                _sessionContext.CurrentUserName = RemotePcShareBiz.LocalUserAccount;
        }

        private void RefreshParsePreview()
        {
            if (_selectedCandidate == null)
            {
                ParsePreviewText = "Changeset을 선택하면 WorkLogDraft 파싱 미리보기가 표시됩니다.";
                ParsePreviewRows = new ObservableCollection<WorkLogExcelPreviewRow>();
                return;
            }

            try
            {
                var ctx = BuildContextForCandidate(_selectedCandidate);
                var draft = _workLogParser.Parse(_selectedCandidate.ToChangesetItem(), ctx);
                ParsePreviewText = WorkLogExcelPreviewMapper.FormatDebugPreview(draft);
                ParsePreviewRows = new ObservableCollection<WorkLogExcelPreviewRow>(
                    WorkLogExcelPreviewMapper.ToPreviewRows(draft));
            }
            catch (Exception ex)
            {
                ParsePreviewText = "파싱 오류: " + ex.Message;
                ParsePreviewRows = new ObservableCollection<WorkLogExcelPreviewRow>();
            }
        }

        private WorkSessionContext BuildContextForCandidate(TfsChangesetCandidateViewModel c)
        {
            var ctx = new WorkSessionContext
            {
                RemoteIp = _sessionContext.RemoteIp,
                RemoteComputerName = !string.IsNullOrWhiteSpace(_sessionContext.RemoteComputerName)
                    ? _sessionContext.RemoteComputerName
                    : c.RemoteComputerName,
                CurrentUserId = _sessionContext.CurrentUserId,
                CurrentUserName = _sessionContext.CurrentUserName,
                SessionStartedAt = _sessionContext.SessionStartedAt,
                SessionEndedAt = _sessionContext.SessionEndedAt
            };

            DateTime tmp;
            if (!ctx.SessionStartedAt.HasValue
                && !string.IsNullOrWhiteSpace(c.SessionStartAt)
                && DateTime.TryParse(c.SessionStartAt, out tmp))
                ctx.SessionStartedAt = tmp;
            if (!ctx.SessionEndedAt.HasValue
                && !string.IsNullOrWhiteSpace(c.SessionEndAt)
                && DateTime.TryParse(c.SessionEndAt, out tmp))
                ctx.SessionEndedAt = tmp;

            if (string.IsNullOrWhiteSpace(ctx.CurrentUserId))
                ctx.CurrentUserId = RemotePcShareBiz.LocalUserAccount;
            if (string.IsNullOrWhiteSpace(ctx.CurrentUserName))
                ctx.CurrentUserName = RemotePcShareBiz.LocalUserAccount;

            return ctx;
        }

        private async Task SaveSelectedAsync()
        {
            var selected = Candidates.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusMessage = "저장할 Changeset을 선택하세요.";
                return;
            }

            // 운영: 타 사용자 Changeset을 현재 사용자 업무기록으로 자동 등록하지 않음
            var allowed = selected.Where(c =>
                TfsPayloadIngestService.CanSaveAsCurrentUserWorkLog(c, _lastAuthorizedUserId)).ToList();
            var blocked = selected.Count - allowed.Count;
            if (allowed.Count == 0)
            {
                StatusMessage = "현재 사용자 작성 Changeset만 업무기록으로 저장할 수 있습니다.";
                await ShowPopupAsync(
                    "TFS 저장",
                    "다른 사용자의 Changeset은 현재 사용자 업무기록으로 등록할 수 없습니다.",
                    PopupIconKind.Warning).ConfigureAwait(true);
                return;
            }

            if (!_workLogBiz.IsConfigured)
            {
                StatusMessage = "이 경로는 폐기됨 — 체크인 보관함에서 업무기록으로 저장하세요";
                DiagnosticLogger.Error("TFS_SAVE_FAILED", "MSDWHTFS retired");
                await ShowPopupAsync(
                    "TFS 저장",
                    "MSDWHTFS는 사용하지 않습니다.\n체크인 보관함에서 업무기록으로 저장하세요.",
                    PopupIconKind.Warning).ConfigureAwait(true);
                return;
            }

            _isSaving = true;
            ((RelayCommand)SaveSelectedCommand).RaiseCanExecuteChanged();
            StatusMessage = "저장 중… (" + allowed.Count + "건"
                + (blocked > 0 ? ", 타사용자 " + blocked + "건 제외" : "") + ")";

            string createdBy = Environment.UserDomainName + "\\" + Environment.UserName;
            int ok = 0;
            int fail = 0;

            try
            {
                foreach (var c in allowed)
                {
                    bool saved = await _workLogBiz.SaveAsync(c.ToWorkLogRecord(createdBy)).ConfigureAwait(true);
                    if (saved)
                    {
                        c.IsSaved = true;
                        c.SaveStatus = "저장 완료";
                        ok++;
                    }
                    else
                    {
                        c.SaveStatus = "저장 실패";
                        fail++;
                    }
                }

                StatusMessage = "저장 결과: 성공 " + ok + " / 실패 " + fail
                    + (blocked > 0 ? " / 타사용자제외 " + blocked : "");
                if (fail > 0)
                {
                    await ShowPopupAsync(
                        "TFS 저장",
                        "일부 저장에 실패했습니다.\n" + (_workLogBiz.LastConnectionError ?? "로그를 확인하세요."),
                        PopupIconKind.Warning).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "저장 예외: " + ex.Message;
                DiagnosticLogger.Error("TFS_SAVE_FAILED", ex.Message);
            }
            finally
            {
                _isSaving = false;
                ((RelayCommand)SaveSelectedCommand).RaiseCanExecuteChanged();
            }
        }

        private async Task ShowPopupAsync(string title, string message, PopupIconKind icon)
        {
            if (_popup == null)
            {
                DiagnosticLogger.Warn("Popup", title + " | " + message);
                return;
            }

            await _popup.ShowResultAsync(new PopupRequest
            {
                Title = title,
                Message = message,
                Icon = icon,
                Kind = PopupKind.Result,
                Buttons = new System.Collections.Generic.List<PopupButtonDefinition>
                {
                    new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true)
                }
            }).ConfigureAwait(true);
        }

        private void RunOnUiSync(Action action)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher == null || app.Dispatcher.CheckAccess())
            {
                action();
                return;
            }
            app.Dispatcher.Invoke(action);
        }
    }
}
