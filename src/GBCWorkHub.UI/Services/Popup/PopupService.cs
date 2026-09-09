using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services;
using GBCWorkHub.UI.ViewModels;
using GBCWorkHub.UI.ViewModels.Popup;
using GBCWorkHub.UI.Views.Popup;

using GBCWorkHub.UI;

namespace GBCWorkHub.UI.Services.Popup
{
    public interface IPopupService
    {
        Window Owner { get; set; }

        /// <summary>진행 팝업에서 사용자가 취소를 눌렀을 때.</summary>
        event Action ProgressCancelled;

        Task<PopupResult> ShowConfirmAsync(PopupRequest request);
        Task<PopupResult> ShowPromptAsync(PopupRequest request);
        Task ShowProgressAsync(PopupRequest request);
        Task UpdateProgressAsync(string stepText);
        Task CloseProgressAsync();
        Task ShowResultAsync(PopupRequest request);
        Task<PopupResult> ShowDialogAsync(PopupRequest request);
    }

    public sealed class PopupService : IPopupService
    {
        private PopupHostWindow _host;
        private PopupHostViewModel _vm;
        private TaskCompletionSource<PopupResult> _tcs;
        private bool _progressOpen;
        private string _openDedupKey;
        private bool _resultSet;
        private Func<PopupHostViewModelSnapshot, Task<string>> _primaryValidator;
        private bool _primaryValidating;

        public Window Owner { get; set; }

        public event Action ProgressCancelled;

        public Task<PopupResult> ShowConfirmAsync(PopupRequest request)
        {
            if (request == null)
                request = new PopupRequest();
            request.Kind = PopupKind.Confirm;
            return ShowDialogAsync(request);
        }

        public Task<PopupResult> ShowPromptAsync(PopupRequest request)
        {
            if (request == null)
                request = new PopupRequest();
            request.Kind = PopupKind.Prompt;
            request.ShowInput = true;
            return ShowDialogAsync(request);
        }

        public async Task ShowResultAsync(PopupRequest request)
        {
            if (request == null)
                request = new PopupRequest();
            request.Kind = PopupKind.Result;
            if (request.Buttons == null || request.Buttons.Count == 0)
            {
                request.Buttons = new List<PopupButtonDefinition>
                {
                    new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true)
                };
            }
            await ShowDialogAsync(request).ConfigureAwait(true);
        }

        public Task ShowProgressAsync(PopupRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();
            RunOnUi(() =>
            {
                try
                {
                    if (_host != null && _progressOpen)
                    {
                        if (_vm != null && request != null)
                        {
                            _vm.Title = request.Title ?? _vm.Title;
                            _vm.ProgressStepText = request.ProgressStepText ?? string.Empty;
                        }
                        tcs.TrySetResult(true);
                        return;
                    }

                    if (_host != null)
                    {
                        DiagnosticLogger.Warn("Popup", "Force-close stale host before ShowProgress");
                        CloseHost(PopupResult.From(PopupResultType.Closed));
                    }

                    if (request == null)
                        request = new PopupRequest();
                    request.Kind = PopupKind.Progress;
                    request.ShowCancelOnProgress = true;
                    OpenHost(request, modal: false);
                    _progressOpen = true;
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task UpdateProgressAsync(string stepText)
        {
            var tcs = new TaskCompletionSource<bool>();
            RunOnUi(() =>
            {
                if (_vm != null)
                    _vm.ProgressStepText = stepText ?? string.Empty;
                tcs.TrySetResult(true);
            });
            return tcs.Task;
        }

        public Task CloseProgressAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            RunOnUi(() =>
            {
                if (_progressOpen || (_host != null && _vm != null && _vm.IsProgress))
                    CloseHost(PopupResult.From(PopupResultType.Closed));
                tcs.TrySetResult(true);
            });
            return tcs.Task;
        }

        public Task<PopupResult> ShowDialogAsync(PopupRequest request)
        {
            var tcs = new TaskCompletionSource<PopupResult>();
            RunOnUi(() =>
            {
                try
                {
                    // Progress/이전 호스트가 남아 있으면 Confirm이 조용히 None으로 실패함 → 먼저 정리
                    if (_host != null)
                    {
                        if (!string.IsNullOrWhiteSpace(request != null ? request.DedupKey : null)
                            && string.Equals(_openDedupKey, request.DedupKey, StringComparison.Ordinal)
                            && !_progressOpen)
                        {
                            tcs.TrySetResult(PopupResult.From(PopupResultType.None));
                            return;
                        }

                        DiagnosticLogger.Warn("Popup", "Force-close stale host before ShowDialog kind="
                            + (request != null ? request.Kind.ToString() : "?")
                            + " progressOpen=" + _progressOpen);
                        CloseHost(PopupResult.From(PopupResultType.Closed));
                    }

                    if (request == null)
                        request = new PopupRequest();
                    _tcs = tcs;
                    _resultSet = false;
                    OpenHost(request, modal: true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        private void OpenHost(PopupRequest request, bool modal)
        {
            _vm = new PopupHostViewModel();
            _vm.Apply(request);
            _primaryValidator = request != null ? request.PrimaryValidator : null;
            _primaryValidating = false;
            _vm.ButtonCommand = new RelayCommand<object>(p =>
            {
                var btn = p as PopupButtonDefinition;
                if (btn == null || _primaryValidating)
                    return;
                if (btn.ResultType == PopupResultType.Primary)
                {
                    if (!_vm.TryAcceptInput())
                        return;
                    if (_primaryValidator != null)
                    {
                        _primaryValidating = true;
                        var ignored = RunPrimaryValidatorAsync(btn);
                        return;
                    }
                }
                CloseHost(PopupResult.From(btn.ResultType, btn.Text, _vm.InputText, _vm.AffiliationText, _vm.PasswordText));
            });
            _vm.CancelProgressCommand = new RelayCommand(() =>
            {
                try
                {
                    var h = ProgressCancelled;
                    if (h != null)
                        h();
                }
                catch
                {
                }
                CloseHost(PopupResult.From(PopupResultType.Cancel, "취소"));
            });

            _host = new PopupHostWindow
            {
                DataContext = _vm,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _host.PreviewKeyDown += Host_PreviewKeyDown;
            _openDedupKey = request.DedupKey;
            _progressOpen = request.Kind == PopupKind.Progress;

            var layer = FindOverlayLayer();
            if (layer == null)
                throw new InvalidOperationException("Popup overlay host is missing.");

            layer.Children.Clear();
            layer.Children.Add(_host);
            layer.Visibility = Visibility.Visible;
            _host.Focus();
        }

        private async Task RunPrimaryValidatorAsync(PopupButtonDefinition btn)
        {
            var validator = _primaryValidator;
            string error = null;
            try
            {
                if (validator != null && _vm != null)
                {
                    var snap = new PopupHostViewModelSnapshot
                    {
                        InputText = _vm.InputText,
                        AffiliationText = _vm.AffiliationText,
                        SecondaryInputText = _vm.SecondaryInputText,
                        PasswordText = _vm.PasswordText,
                        CurrentPasswordText = _vm.CurrentPasswordText,
                        PasswordConfirmText = _vm.PasswordConfirmText
                    };
                    error = await validator(snap).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                error = "처리 중 오류가 발생했습니다.\n" + ex.Message;
            }

            RunOnUi(() =>
            {
                _primaryValidating = false;
                if (_vm == null)
                    return;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    _vm.InputError = error.Trim();
                    return;
                }
                CloseHost(PopupResult.From(
                    btn != null ? btn.ResultType : PopupResultType.Primary,
                    btn != null ? btn.Text : "확인",
                    _vm.InputText,
                    _vm.AffiliationText,
                    _vm.PasswordText));
            });
        }

        private Panel FindOverlayLayer()
        {
            var main = Owner as MainWindow;
            if (main == null && Application.Current != null)
                main = Application.Current.MainWindow as MainWindow;
            return main != null ? main.PopupLayer : null;
        }

        private void Host_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_vm == null || _vm.IsProgress)
            {
                if (e.Key == System.Windows.Input.Key.Escape && _vm != null && _vm.ShowCancelOnProgress)
                {
                    try
                    {
                        var h = ProgressCancelled;
                        if (h != null)
                            h();
                    }
                    catch
                    {
                    }
                    CloseHost(PopupResult.From(PopupResultType.Cancel, "취소"));
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == System.Windows.Input.Key.Escape)
            {
                var cancel = FindCancelButton();
                if (cancel != null)
                    CloseHost(PopupResult.From(cancel.ResultType, cancel.Text));
                else
                    CloseHost(PopupResult.From(PopupResultType.Closed));
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                var def = FindDefaultButton();
                if (def != null)
                {
                    if (_primaryValidating)
                    {
                        e.Handled = true;
                        return;
                    }
                    if (def.ResultType == PopupResultType.Primary)
                    {
                        if (!_vm.TryAcceptInput())
                        {
                            e.Handled = true;
                            return;
                        }
                        if (_primaryValidator != null)
                        {
                            _primaryValidating = true;
                            var ignored = RunPrimaryValidatorAsync(def);
                            e.Handled = true;
                            return;
                        }
                    }
                    CloseHost(PopupResult.From(def.ResultType, def.Text, _vm.InputText, _vm.AffiliationText, _vm.PasswordText));
                    e.Handled = true;
                }
            }
        }

        private PopupButtonDefinition FindDefaultButton()
        {
            if (_vm == null)
                return null;
            foreach (var b in _vm.Buttons)
            {
                if (b != null && b.IsDefault)
                    return b;
            }
            return _vm.Buttons.Count > 0 ? _vm.Buttons[0] : null;
        }

        private PopupButtonDefinition FindCancelButton()
        {
            if (_vm == null)
                return null;
            foreach (var b in _vm.Buttons)
            {
                if (b != null && b.IsCancel)
                    return b;
            }
            return null;
        }

        private void CloseHost(PopupResult result)
        {
            var host = _host;
            var tcs = _tcs;
            if (!_resultSet && tcs != null)
            {
                _resultSet = true;
                tcs.TrySetResult(result ?? PopupResult.From(PopupResultType.Closed));
            }

            if (host != null)
            {
                try
                {
                    host.PreviewKeyDown -= Host_PreviewKeyDown;
                }
                catch
                {
                }
            }

            var layer = FindOverlayLayer();
            if (layer != null)
            {
                layer.Children.Clear();
                layer.Visibility = Visibility.Collapsed;
            }

            CleanupHostRefs();
        }

        private void CleanupHostRefs()
        {
            _host = null;
            _vm = null;
            _tcs = null;
            _progressOpen = false;
            _openDedupKey = null;
            _resultSet = false;
            _primaryValidator = null;
            _primaryValidating = false;
        }

        private static void RunOnUi(Action action)
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
