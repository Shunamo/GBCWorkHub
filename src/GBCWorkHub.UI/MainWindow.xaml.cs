using System;
using System.Windows;
using System.Windows.Input;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.BIZ;
using GBCWorkHub.UI.ViewModels;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel = new MainViewModel();
        private readonly PopupService _popupService = new PopupService();
        private ClipboardMonitorService _clipboardMonitor;
        private bool _clipboardStarted;

        public System.Windows.Controls.Panel PopupLayer
        {
            get { return PopupOverlayHost; }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            _popupService.Owner = this;
            _viewModel.InitializeUiServices(_popupService);
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_clipboardStarted)
            {
                _clipboardMonitor = new ClipboardMonitorService(this);
                _clipboardMonitor.GbcClipboardReceived += OnGbcClipboardReceived;
                _clipboardMonitor.TfsClipboardReceived += OnTfsClipboardReceived;
                _clipboardMonitor.SessionChangeResultReceived += OnSessionChangeResultReceived;
                _clipboardMonitor.Start();
                _clipboardStarted = true;
                DiagnosticLogger.Info("MainWindow", "클립보드 모니터 시작 완료 (RDP/TFS Prefix 분기)");
            }

            await OccupancyNamePrompt.EnsureAsync(_popupService);
            try
            {
                await new DirectoryBiz().SyncPcMapFromConfigAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("DIRECTORY", "PC 별명 동기화 실패: " + ex.Message);
            }
            _viewModel.RemoteWorkspace.RefreshLocalIdentity();
            _viewModel.NotifyIdentityChanged();

            TfsCheckinInboxStore.PurgeDummyRows();
            if (_viewModel.WorkLogList != null && _viewModel.WorkLogList.Inbox != null)
                _viewModel.WorkLogList.Inbox.Reload();

            await _viewModel.InitializeCentralShareAsync();

            try
            {
                await _viewModel.CheckForUpdatesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn("UPDATE", "Update check failed: " + ex.Message);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                _viewModel.HandleAppClosingAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("MainWindow", "HandleAppClosing: " + ex.Message);
            }

            if (_clipboardMonitor != null)
            {
                _clipboardMonitor.GbcClipboardReceived -= OnGbcClipboardReceived;
                _clipboardMonitor.TfsClipboardReceived -= OnTfsClipboardReceived;
                _clipboardMonitor.SessionChangeResultReceived -= OnSessionChangeResultReceived;
                _clipboardMonitor.Dispose();
                _clipboardMonitor = null;
            }

            _clipboardStarted = false;
            _viewModel.Dispose();
            DiagnosticLogger.Info("MainWindow", "창 종료 — 클립보드/RDP 추적 정리");
        }

        private void OnGbcClipboardReceived(string text)
        {
            ProcessClipboardText(text);
        }

        private void OnTfsClipboardReceived(string text)
        {
            if (Dispatcher.CheckAccess())
                _viewModel.HandleTfsClipboardText(text);
            else
                Dispatcher.Invoke(new Action(() => _viewModel.HandleTfsClipboardText(text)));
        }

        private void OnSessionChangeResultReceived(string text)
        {
            if (Dispatcher.CheckAccess())
                _viewModel.HandleSessionChangeResultText(text);
            else
                Dispatcher.Invoke(new Action(() => _viewModel.HandleSessionChangeResultText(text)));
        }

        private void ProcessClipboardText(string text)
        {
            if (Dispatcher.CheckAccess())
                _viewModel.HandleClipboardText(text, true);
            else
                Dispatcher.Invoke(new Action(() => _viewModel.HandleClipboardText(text, true)));
        }

        private async void BtnReprocessClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (_clipboardMonitor == null)
            {
                await _popupService.ShowResultAsync(new PopupRequest
                {
                    Title = "진단",
                    Message = "클립보드 모니터가 아직 시작되지 않았습니다.",
                    Icon = PopupIconKind.Warning,
                    Kind = PopupKind.Result,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true)
                    }
                });
                return;
            }

            DiagnosticLogger.Info("MainWindow", "버튼: 현재 클립보드 다시 처리");
            _clipboardMonitor.ReprocessCurrentClipboard();
        }
    }
}
