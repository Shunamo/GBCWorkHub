using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.Services.Update;

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
        private readonly MyPageViewModel _myPage;

        private IPopupService _popup;
        private TfsSyncCoordinator _tfsSync;
        private string _selectedMainTab = "Remote";
        private string _workLogBackTab;
        private int _pendingTfsCount;
        private bool _disposed;
        private bool _isUpdateAvailable;
        private bool _isUpdateBusy;
        private string _latestVersion;
        private string _updateBannerText;
        private UpdateManifest _pendingUpdate;
        private CancellationTokenSource _updateCts;

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
            RefreshCommand = new RelayCommand(() => { var _ = RefreshFromDbAsync(); }, () => !IsRefreshing);
            BackToSitesCommand = new RelayCommand(() => _remoteWorkspace.BackToSitesCommand.Execute(null));
            UpdateCommand = new RelayCommand(() => { var _ = ApplyUpdateAsync(); }, () => IsUpdateAvailable && !IsUpdateBusy);
            DismissUpdateCommand = new RelayCommand(DismissUpdate, () => IsUpdateAvailable && !IsUpdateBusy);
            _myPage = new MyPageViewModel(_workLogList, OpenFromMyPage, ChangeOccupancyNameAsync);
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

        public TfsSyncCoordinator TfsSync
        {
            get { return _tfsSync; }
        }

        public MyPageViewModel MyPage
        {
            get { return _myPage; }
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
                    if (string.Equals(tab, "Me", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_workLogList != null && _workLogList.Inbox != null)
                            _workLogList.Inbox.Reload();
                        SelectMainTab("Me");
                        return;
                    }
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
                    if (_workLogList != null && _workLogList.Inbox != null)
                        _workLogList.Inbox.Reload();
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
            _workLogList.FetchTfsForWindow = (ip, name, from, to, cmc) => FetchTfsForWindowAsync(ip, name, from, to, cmc);

            if (_workLogList.Inbox != null)
            {
                var originalWrite = _workLogList.Inbox.WriteRequested;
                _workLogList.Inbox.WriteRequested = records =>
                {
                    if (IsMeTab)
                        _workLogBackTab = "Me";
                    SelectMainTab("WorkLog");
                    if (originalWrite != null)
                        originalWrite(records);
                };
            }

            _workLogList.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsEditOpen" || e.PropertyName == "IsImportOpen" || e.PropertyName == "IsInboxOpen" || e.PropertyName == "HasNewCandidates"
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

        public ICommand WorkLogBackCommand { get; private set; }
        public ICommand SelectMainTabCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand BackToSitesCommand { get; private set; }
        public ICommand UpdateCommand { get; private set; }
        public ICommand DismissUpdateCommand { get; private set; }

        public bool IsUpdateAvailable
        {
            get { return _isUpdateAvailable; }
            private set
            {
                if (SetProperty(ref _isUpdateAvailable, value))
                {
                    var cmd = UpdateCommand as RelayCommand;
                    if (cmd != null)
                        cmd.RaiseCanExecuteChanged();
                    var dismiss = DismissUpdateCommand as RelayCommand;
                    if (dismiss != null)
                        dismiss.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsUpdateBusy
        {
            get { return _isUpdateBusy; }
            private set
            {
                if (SetProperty(ref _isUpdateBusy, value))
                {
                    var cmd = UpdateCommand as RelayCommand;
                    if (cmd != null)
                        cmd.RaiseCanExecuteChanged();
                    var dismiss = DismissUpdateCommand as RelayCommand;
                    if (dismiss != null)
                        dismiss.RaiseCanExecuteChanged();
                }
            }
        }

        public string LatestVersion
        {
            get { return _latestVersion; }
            private set { SetProperty(ref _latestVersion, value); }
        }

        public string UpdateBannerText
        {
            get { return _updateBannerText; }
            private set { SetProperty(ref _updateBannerText, value); }
        }

        public string CurrentAppVersion
        {
            get { return AppVersion.Current; }
        }

        /// <summary>Best-effort update check. Never throws to caller; never blocks app startup.</summary>
        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var source = UpdateService.CreateSourceFromConfig();
                if (source == null)
                    return;

                if (_updateCts != null)
                    _updateCts.Cancel();
                _updateCts = new CancellationTokenSource();
                var cts = _updateCts;

                var service = new UpdateService(source);
                var latest = await service.CheckForUpdateAsync(cts.Token).ConfigureAwait(true);
                if (cts.IsCancellationRequested || latest == null)
                    return;

                _pendingUpdate = latest;
                LatestVersion = latest.Version;
                UpdateBannerText = "새로운 버전이 있습니다. GBCWorkHub v" + latest.Version;
                IsUpdateAvailable = true;
                DiagnosticLogger.Info("UPDATE", "Update available: " + latest.Version);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("UPDATE", "Update check skipped: " + ex.Message);
            }
        }

        private async Task ApplyUpdateAsync()
        {
            if (_pendingUpdate == null || IsUpdateBusy)
                return;

            IsUpdateBusy = true;
            try
            {
                var source = UpdateService.CreateSourceFromConfig();
                if (source == null)
                    throw new InvalidOperationException("Update source is not configured.");

                var service = new UpdateService(source);
                await service.ApplyUpdateAsync(_pendingUpdate, CancellationToken.None).ConfigureAwait(true);
                DiagnosticLogger.Info("UPDATE", "Updater launched; shutting down for apply");
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                IsUpdateBusy = false;
                DiagnosticLogger.Warn("UPDATE", "Update apply failed: " + ex.Message);
                if (_popup != null)
                {
                    await _popup.ShowResultAsync(new PopupRequest
                    {
                        Title = "업데이트",
                        Message = "업데이트를 시작하지 못했습니다.\n" + ex.Message,
                        Icon = PopupIconKind.Warning,
                        Kind = PopupKind.Result,
                        Buttons = new[]
                        {
                            new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true)
                        }
                    }).ConfigureAwait(true);
                }
            }
        }

        private void DismissUpdate()
        {
            IsUpdateAvailable = false;
            _pendingUpdate = null;
            LatestVersion = null;
            UpdateBannerText = null;
        }

        private void RefreshPendingTfsBadge()
        {
            PendingTfsCount = TfsPendingSessionStore.Count;
        }

        private async Task FetchTfsForWindowAsync(
            string shareKey, string pcName, DateTime fromAt, DateTime toAt, bool clipboardOnly)
        {
            if (_tfsSync == null || string.IsNullOrWhiteSpace(shareKey))
                return;
            if (!clipboardOnly)
                clipboardOnly = IsCmcShareKey(shareKey);
            await _tfsSync.FetchForSessionWindowAsync(shareKey, pcName, fromAt, toAt, clipboardOnly)
                .ConfigureAwait(true);
        }

        private bool IsCmcShareKey(string shareKey)
        {
            if (string.IsNullOrWhiteSpace(shareKey) || _remoteWorkspace == null || _remoteWorkspace.RemoteComputers == null)
                return false;
            foreach (var pc in _remoteWorkspace.RemoteComputers)
            {
                if (pc == null || string.IsNullOrWhiteSpace(pc.IpAddress))
                    continue;
                if (string.Equals(pc.IpAddress.Trim(), shareKey.Trim(), StringComparison.OrdinalIgnoreCase))
                    return string.Equals(pc.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);
            }
            return false;
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
                    RaisePropertyChanged("IsMeTab");
                    RaisePropertyChanged("IsHeaderBackVisible");
                    RaisePropertyChanged("IsWorkLogGlassUi");
                    RaisePropertyChanged("IsWorkLogGlassOverlay");
                    RaisePropertyChanged("IsMainGlassOverlay");
                    RaisePropertyChanged("IsSitePickerVisible");
                    RaisePropertyChanged("IsGalleryVisible");
                    RaisePropertyChanged("HeaderTitle");
                    RaisePropertyChanged("OccupancyDisplayName");
                    if (IsMeTab && _myPage != null)
                    {
                        var ignored = _myPage.ReloadAsync();
                    }
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

        public bool IsMeTab
        {
            get { return string.Equals(SelectedMainTab, "Me", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsHeaderBackVisible
        {
            get { return IsWorkLogTab || IsMeTab; }
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
                    && !WorkLogList.IsImportOpen
                    && !WorkLogList.IsInboxOpen;
            }
        }

        public bool IsMainGlassOverlay
        {
            get { return IsRemoteTab || IsWorkLogGlassOverlay || IsMeTab; }
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
                if (IsMeTab)
                    return string.Empty;
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

        public string OccupancyDisplayName
        {
            get
            {
                string name = OccupancyNameStore.HeaderName;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
                return WorkHubUserProfile.HasDisplayName
                    ? WorkHubUserProfile.OccupancyName
                    : "나";
            }
        }

        /// <summary>원격 종료 후 체크인 가져오기 자동 팝업. 수동 가져오기는 유지.</summary>
        public bool IsCheckinFetchEnabled
        {
            get { return TfsCheckinFetchSettings.IsEnabled; }
            set
            {
                if (TfsCheckinFetchSettings.IsEnabled == value)
                    return;
                TfsCheckinFetchSettings.IsEnabled = value;
                RaisePropertyChanged("IsCheckinFetchEnabled");
            }
        }

        public string CheckinFetchToggleLabel
        {
            get { return "Fetch TFS"; }
        }

        public string OccupancyInitial
        {
            get { return WorkHubUserProfile.OccupancyInitial; }
        }

        public void NotifyIdentityChanged()
        {
            RaisePropertyChanged("OccupancyDisplayName");
            RaisePropertyChanged("OccupancyInitial");
            RaisePropertyChanged("HeaderTitle");
            if (_myPage != null)
                _myPage.RefreshIdentity();
        }

        private async Task ChangeOccupancyNameAsync()
        {
            if (_popup == null)
                return;

            string oldName = OccupancyNameStore.TryGet();
            bool changed = await OccupancyNamePrompt.ChangeAsync(_popup).ConfigureAwait(true);
            if (!changed)
                return;

            string newName = OccupancyNameStore.TryGet();
            string newTeam = OccupancyNameStore.TryGetAffiliation();
            var workLogs = new WorkLogBiz();
            bool nameChanged = !string.IsNullOrWhiteSpace(oldName)
                && !string.IsNullOrWhiteSpace(newName)
                && !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);

            if (nameChanged)
            {
                var share = new RemotePcShareBiz();
                int workLogRows = 0;
                int occupancyRows = 0;
                try
                {
                    workLogRows = await workLogs.RenameAuthorAsync(
                        oldName, newName, WorkHubUserProfile.LocalIp).ConfigureAwait(true);
                    occupancyRows = await share.RenameOccupantAsync(oldName, newName).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    workLogRows = -1;
                    occupancyRows = -1;
                }

                TfsCheckinInboxStore.RenameOwner(oldName, newName);

                if (workLogRows < 0 || occupancyRows < 0)
                {
                    await _popup.ShowConfirmAsync(new PopupRequest
                    {
                        Kind = PopupKind.Result,
                        Icon = PopupIconKind.Error,
                        Title = "이름 변경",
                        Message = "로컬 이름은 바꿨지만 DB 반영에 실패했습니다. 연결을 확인한 뒤 다시 저장해 주세요.",
                        Buttons = new[]
                        {
                            new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true, isCancel: true)
                        }
                    }).ConfigureAwait(true);
                }
            }

            if (!string.IsNullOrWhiteSpace(newName))
            {
                int teamRows = 0;
                try
                {
                    teamRows = await workLogs.RenameTeamAsync(
                        newName, newTeam, WorkHubUserProfile.LocalIp).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    teamRows = -1;
                }

                if (teamRows < 0)
                {
                    await _popup.ShowConfirmAsync(new PopupRequest
                    {
                        Kind = PopupKind.Result,
                        Icon = PopupIconKind.Error,
                        Title = "소속 변경",
                        Message = "로컬 소속은 바꿨지만 DB 반영에 실패했습니다. 연결을 확인한 뒤 다시 저장해 주세요.",
                        Buttons = new[]
                        {
                            new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true, isCancel: true)
                        }
                    }).ConfigureAwait(true);
                }
            }

            NotifyIdentityChanged();
            if (_remoteWorkspace != null)
                _remoteWorkspace.RefreshLocalIdentity();
            if (_workLogList != null)
            {
                if (_workLogList.Inbox != null)
                {
                    _workLogList.Inbox.Reload();
                    _workLogList.NotifyInboxBadge();
                }
                await _workLogList.ReloadFromDbAsync().ConfigureAwait(true);
            }
            if (_myPage != null)
                await _myPage.ReloadAsync().ConfigureAwait(true);
        }

        public async Task InitializeCentralShareAsync()
        {
            await _remoteWorkspace.InitializeCentralShareAsync().ConfigureAwait(true);
        }

        public async Task RefreshFromDbAsync()
        {
            if (IsMeTab && _myPage != null)
            {
                await _myPage.ReloadAsync().ConfigureAwait(true);
                return;
            }
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
                if (IsWorkLogTab)
                    PopWorkLogBackTab();
                return;
            }
            if (IsWorkLogTab && PopWorkLogBackTab())
                return;
            SelectMainTab("Remote");
        }

        private void OpenFromMyPage(WorkLog.WorkLogListItemViewModel item)
        {
            _workLogBackTab = "Me";
            if (WorkLogList != null)
            {
                if (WorkLogList.IsInboxOpen
                    && WorkLogList.CloseInboxCommand != null
                    && WorkLogList.CloseInboxCommand.CanExecute(null))
                    WorkLogList.CloseInboxCommand.Execute(null);
                if (WorkLogList.IsImportOpen
                    && WorkLogList.CloseImportCommand != null
                    && WorkLogList.CloseImportCommand.CanExecute(null))
                    WorkLogList.CloseImportCommand.Execute(null);
            }
            SelectMainTab("WorkLog");
            if (item == null || WorkLogList == null)
                return;
            if (WorkLogList.EditWorkLogCommand != null
                && WorkLogList.EditWorkLogCommand.CanExecute(item))
                WorkLogList.EditWorkLogCommand.Execute(item);
        }

        private bool PopWorkLogBackTab()
        {
            if (string.IsNullOrWhiteSpace(_workLogBackTab))
                return false;
            string tab = _workLogBackTab;
            _workLogBackTab = null;
            SelectedMainTab = tab;
            return true;
        }

        private void SelectMainTab(string tab)
        {
            if (string.Equals(tab, "Tfs", StringComparison.OrdinalIgnoreCase))
                tab = "WorkLog";
            tab = string.IsNullOrWhiteSpace(tab) ? "Remote" : tab;
            if (!string.Equals(tab, "WorkLog", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tab, _workLogBackTab, StringComparison.OrdinalIgnoreCase))
                _workLogBackTab = null;
            SelectedMainTab = tab;
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
            if (_updateCts != null)
            {
                try { _updateCts.Cancel(); } catch { }
                try { _updateCts.Dispose(); } catch { }
                _updateCts = null;
            }
            if (_tfsWorkLog != null)
                _tfsWorkLog.CandidatesChanged -= OnTfsCandidatesChangedForWorkLog;
            if (_remoteWorkspace != null)
                _remoteWorkspace.Dispose();
        }
    }
}
