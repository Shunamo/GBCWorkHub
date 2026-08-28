using System.Collections.ObjectModel;
using System.Windows.Input;
using GBCWorkHub.UI.Models.Popup;

namespace GBCWorkHub.UI.ViewModels.Popup
{
    public sealed class PopupHostViewModel : ViewModelBase
    {
        private string _title;
        private string _message;
        private string _detail;
        private string _progressStepText;
        private PopupIconKind _icon;
        private PopupKind _kind;
        private bool _showCancelOnProgress;
        private ObservableCollection<PopupButtonDefinition> _buttons =
            new ObservableCollection<PopupButtonDefinition>();

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public string Message
        {
            get { return _message; }
            set { SetProperty(ref _message, value); }
        }

        public string Detail
        {
            get { return _detail; }
            set { SetProperty(ref _detail, value); }
        }

        public string ProgressStepText
        {
            get { return _progressStepText; }
            set { SetProperty(ref _progressStepText, value); }
        }

        public PopupIconKind Icon
        {
            get { return _icon; }
            set
            {
                if (SetProperty(ref _icon, value))
                    RaisePropertyChanged("IconGlyph");
            }
        }

        public PopupKind Kind
        {
            get { return _kind; }
            set
            {
                if (SetProperty(ref _kind, value))
                {
                    RaisePropertyChanged("IsConfirmOrResult");
                    RaisePropertyChanged("IsProgress");
                }
            }
        }

        public bool ShowCancelOnProgress
        {
            get { return _showCancelOnProgress; }
            set { SetProperty(ref _showCancelOnProgress, value); }
        }

        public ObservableCollection<PopupButtonDefinition> Buttons
        {
            get { return _buttons; }
        }

        public bool IsProgress
        {
            get { return Kind == PopupKind.Progress; }
        }

        public bool IsConfirmOrResult
        {
            get { return Kind != PopupKind.Progress; }
        }

        public string IconGlyph
        {
            get
            {
                switch (Icon)
                {
                    case PopupIconKind.Question: return "?";
                    case PopupIconKind.Success: return "OK";
                    case PopupIconKind.Warning: return "!";
                    case PopupIconKind.Error: return "X";
                    case PopupIconKind.Info: return "i";
                    default: return string.Empty;
                }
            }
        }

        public ICommand ButtonCommand { get; set; }
        public ICommand CancelProgressCommand { get; set; }

        public void Apply(PopupRequest request)
        {
            if (request == null)
                return;

            Title = request.Title ?? string.Empty;
            Message = request.Message ?? string.Empty;
            Detail = request.Detail ?? string.Empty;
            ProgressStepText = request.ProgressStepText ?? string.Empty;
            Icon = request.Icon;
            Kind = request.Kind;
            ShowCancelOnProgress = request.ShowCancelOnProgress;

            Buttons.Clear();
            if (request.Buttons != null)
            {
                foreach (var b in request.Buttons)
                {
                    if (b != null)
                        Buttons.Add(b);
                }
            }
        }
    }
}
