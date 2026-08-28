using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GBCWorkHub.BIZ;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>
    /// App shell: tab navigation, WorkLog/TFS wiring, RemoteWorkspace composition.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly TfsWorkLogViewModel _tfsWorkLog = new TfsWorkLogViewModel();
        private readonly WorkLog.WorkLogListViewModel _workLogList = new WorkLog.WorkLogListViewModel();
        private readonly RemoteWorkspaceViewModel _remoteWorkspace;

        private IPopupService _popup;
        private TfsSyncCoordinator _tfsSync;
        private string _selectedMainTab = "Remote";
        private int _pendingTfsCount;
        private bool _disposed;

        public MainViewModel()
        {
            _remoteWorkspace = new RemoteWorkspaceViewModel(() =>
            {
                RaisePropertyChanged("HeaderTitle");
                RaisePropertyChanged("IsGalleryVisible");
                RaisePropertyChanged("IsSitePickerVisible");
            });
            _remoteWorkspace.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsRefreshing")
                {
                    RaisePropertyChanged("IsRefreshing");
                    var refresh = RefreshCommand as RelayCommand;
                    if (refresh != null)
                        refresh.RaiseCanExecuteChanged();
                }
            };

            WorkLogBackCommand = new RelayCommand(WorkLogBack);
            SelectMainTabCommand = new RelayCommand<string>(SelectMainTab);
            RetryPendingTfsCommand = new RelayCommand(() => { var _ = RetryPendingTfsAsync(); }, () => PendingTfsCount > 0);
            RefreshCommand = new RelayCommand(() => { var _ = RefreshFromDbAsync(); }, () => !IsRefreshing);
            BackToSitesCommand = new RelayCommand(() => _remoteWorkspace.BackToSitesCommand.Execute(null));
        }

        public RemoteWorkspaceViewModel RemoteWorkspace
        {
            get { return _remoteWorkspace; }
        }

        public TfsWorkLogViewModel TfsWorkLog
        {
            get { return _tfsWorkLog; }
        }

        public WorkLog.WorkLogListViewModel WorkLogList
        {
            get { return _workLogList; }
        }

        public void InitializeUiServices(IPopupService popup)
        {
            _popup = popup;
            if (_tfsWorkLog != null)
                _tfsWorkLog.AttachPopup(popup);
            if (_workLogList != null)
                _workLogList.AttachPopup(popup);

            _tfsSync = new TfsSyncCoordinator(
                popup,
                _remoteWorkspace.RdpTracker,
                _tfsWorkLog,
                tab =>
                {
                    SelectMainTab("WorkLog");
                    if (_workLogList != null)
                        _workLogList.ShowImportDialog();
                },
                ip =>
                {
                    var handler = _remoteWorkspace.UpdateGalleryAvailableHandler;
                    if (handler != null)
                        handler(ip);
                },
                () =>
                {
                    RefreshPendingTfsBadge();
                    return PendingTfsCount;
                },
                () => _workLogList != null
                    ? (System.Collections.Generic.ISet<int>)_workLogList.GetImportedChangesetIds()
                    : null);

            _remoteWorkspace.AttachShellServices(
                popup,
                _tfsWorkLog,
                _workLogList,
                _tfsSync,
                SelectMainTab,
                RefreshPendingTfsBadge);

            RefreshPendingTfsBadge();

            _workLogList.AttachRuntimeSources(_remoteWorkspace.RemoteComputers, () =>
            {
                _remoteWorkspace.ApplyWorkHubUserToSessionPublic();
                return _tfsWorkLog.CurrentSessionContext;
            });

            _workLogList.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsEditOpen" || e.PropertyName == "IsImportOpen" || e.PropertyName == "HasNewCandidates"
                    || e.PropertyName == "HeaderSiteName")
                {
                    RaisePropertyChanged("HeaderTitle");
                    RaisePropertyChanged("IsWorkLogGlassOverlay");
                    RaisePropertyChanged("IsMainGlassOverlay");
                }
            };

            _tfsWorkLog.CandidatesChanged -= OnTfsCandidatesChangedForWorkLog;
            _tfsWorkLog.CandidatesChanged += OnTfsCandidatesChangedForWorkLog;
            OnTfsCandidatesChangedForWorkLog();
        }

        private void OnTfsCandidatesChangedForWorkLog()
        {
            if (_workLogList == null || _tfsWorkLog == null)
                return;
            _workLogList.NotifyTfsCandidatesUpdated(
                _tfsWorkLog.Candidates,
                _tfsWorkLog.CurrentSessionContext,
                _tfsWorkLog.LastReceiveMessage,
                openImportIfNew: false);
        }

        public int PendingTfsCount
        {
            get { return _pendingTfsCount; }
            private set
            {
                if (SetProperty(ref _pendingTfsCount, value))
                {
                    RaisePropertyChanged("PendingTfsBadgeText");
                    RaisePropertyChanged("HasPendingTfs");
                    var cmd = RetryPendingTfsCommand as RelayCommand;
                    if (cmd != null)
                        cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasPendingTfs
        {
            get { return PendingTfsCount > 0; }
        }

        public string PendingTfsBadgeText
        {
            get
            {
                return PendingTfsCount <= 0
                    ? string.Empty
                    : "미확인 원격 작업 " + PendingTfsCount + "건";
            }
        }

        public ICommand RetryPendingTfsCommand { get; private set; }
        public ICommand WorkLogBackCommand { get; private set; }
        public ICommand SelectMainTabCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand BackToSitesCommand { get; private set; }

        private void RefreshPendingTfsBadge()
        {
            PendingTfsCount = TfsPendingSessionStore.Count;
        }

        private async Task RetryPendingTfsAsync()
        {
            if (_tfsSync == null)
                return;
            var list = TfsPendingSessionStore.Load();
            var first = list.FirstOrDefault();
            if (first == null)
                return;
            await _tfsSync.RetryPendingAsync(first).ConfigureAwait(true);
            RefreshPendingTfsBadge();
        }

        public string SelectedMainTab
        {
            get { return _selectedMainTab; }
            set
            {
                if (SetProperty(ref _selectedMainTab, value ?? "Remote"))
                {
                    RaisePropertyChanged("IsRemoteTab");
                    RaisePropertyChanged("IsTfsTab");
                    RaisePropertyChanged("IsWorkLogTab");
                    RaisePropertyChanged("IsWorkLogGlassUi");
                    RaisePropertyChanged("IsWorkLogGlassOverlay");
                    RaisePropertyChanged("IsMainGlassOverlay");
                    RaisePropertyChanged("IsSitePickerVisible");
                    RaisePropertyChanged("IsGalleryVisible");
                    RaisePropertyChanged("HeaderTitle");
                }
            }
        }

        public bool IsRemoteTab
        {
            get { return string.Equals(SelectedMainTab, "Remote", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsTfsTab
        {
            get { return string.Equals(SelectedMainTab, "Tfs", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsWorkLogTab
        {
            get { return string.Equals(SelectedMainTab, "WorkLog", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsWorkLogGlassUi
        {
            get
            {
                return IsWorkLogTab
                    && WorkLogList != null
                    && WorkLogList.UseGlassmorphism;
            }
        }

        public bool IsWorkLogGlassOverlay
        {
            get
            {
                return IsWorkLogGlassUi
                    && WorkLogList != null
                    && !WorkLogList.IsImportOpen;
            }
        }

        public bool IsMainGlassOverlay
        {
            get { return IsRemoteTab || IsWorkLogGlassOverlay; }
        }

        /// <summary>헤더 뒤로가기/가시성 — RemoteWorkspace와 동기.</summary>
        public bool IsGalleryVisible
        {
            get { return IsRemoteTab && _remoteWorkspace != null && _remoteWorkspace.IsGalleryVisible; }
        }

        public bool IsSitePickerVisible
        {
            get { return IsRemoteTab && _remoteWorkspace != null && _remoteWorkspace.IsSitePickerVisible; }
        }

        public bool IsRefreshing
        {
            get { return _remoteWorkspace != null && _remoteWorkspace.IsRefreshing; }
        }

        public string HeaderTitle
        {
            get
            {
                if (IsWorkLogTab)
                {
                    if (WorkLogList != null && WorkLogList.IsEditOpen
                        && !string.IsNullOrWhiteSpace(WorkLogList.HeaderSiteName)
                        && !string.Equals(WorkLogList.HeaderSiteName, "ALL", StringComparison.OrdinalIgnoreCase))
                        return WorkLogList.HeaderSiteName;
                    return string.Empty;
                }
                if (_remoteWorkspace != null && !string.IsNullOrWhiteSpace(_remoteWorkspace.SelectedSiteCode))
                    return _remoteWorkspace.SelectedSiteCode;
                return string.Empty;
            }
        }

        public async Task InitializeCentralShareAsync()
        {
            await _remoteWorkspace.InitializeCentralShareAsync().ConfigureAwait(true);
        }

        public async Task RefreshFromDbAsync()
        {
            await _remoteWorkspace.RefreshFromDbAsync().ConfigureAwait(true);
        }

        public void HandleClipboardText(string clipboardText, bool usedDispatcher)
        {
            if (string.IsNullOrWhiteSpace(clipboardText))
                return;
            string text = clipboardText.TrimStart();
            if (text.StartsWith(TfsClipboardPayloadService.ClipboardPrefix, StringComparison.Ordinal))
            {
                HandleTfsClipboardText(text);
                return;
            }
            _remoteWorkspace.HandleClipboardText(clipboardText, usedDispatcher);
        }

        public void HandleSessionChangeResultText(string clipboardText)
        {
            _remoteWorkspace.HandleSessionChangeResultText(clipboardText);
        }

        public void HandleTfsClipboardText(string clipboardText)
        {
            if (TfsWorkLog == null)
                return;

            if (_tfsSync != null && _tfsSync.IsWaitingForPayload)
            {
                var parseWait = new TfsClipboardPayloadService().TryHandleClipboardText(clipboardText);
                if (parseWait.Result == TfsClipboardPayloadService.HandleResult.Success
                    && parseWait.Payload != null)
                {
                    _tfsSync.TryAcceptTfsPayload(parseWait.Payload, clipboardText);
                }
                return;
            }

            if (_tfsSync != null && _tfsSync.IsSyncInFlight)
                return;

            TfsSyncRequest active = null;
            if (_tfsSync != null)
                active = _tfsSync.GetActiveRequestSnapshot();

            TfsPayloadIngestService.Ingest(
                clipboardText,
                TfsWorkLog,
                active,
                writeAck: true);
        }

        private void WorkLogBack()
        {
            if (WorkLogList != null && WorkLogList.IsEditOpen)
            {
                if (WorkLogList.CloseEditCommand != null && WorkLogList.CloseEditCommand.CanExecute(null))
                    WorkLogList.CloseEditCommand.Execute(null);
                return;
            }
            SelectMainTab("Remote");
        }

        private void SelectMainTab(string tab)
        {
            if (string.Equals(tab, "Tfs", StringComparison.OrdinalIgnoreCase))
                tab = "WorkLog";
            SelectedMainTab = string.IsNullOrWhiteSpace(tab) ? "Remote" : tab;
        }

        public string GetAppCloseWarning()
        {
            return _remoteWorkspace.GetAppCloseWarning();
        }

        public async Task HandleAppClosingAsync()
        {
            await _remoteWorkspace.HandleAppClosingAsync().ConfigureAwait(true);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_tfsWorkLog != null)
                _tfsWorkLog.CandidatesChanged -= OnTfsCandidatesChangedForWorkLog;
            if (_remoteWorkspace != null)
                _remoteWorkspace.Dispose();
        }
    }
}
