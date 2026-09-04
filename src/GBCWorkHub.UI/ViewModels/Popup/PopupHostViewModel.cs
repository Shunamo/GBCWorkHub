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
        private bool _showInput;
        private string _inputText;
        private bool _showAffiliationInput;
        private bool _requireAffiliation;
        private string _affiliationText;
        private string _inputError;
        private string _infoIp;
        private string _infoWindowsAccount;
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

        public bool ShowInput
        {
            get { return _showInput; }
            set
            {
                if (SetProperty(ref _showInput, value))
                {
                    RaisePropertyChanged("HasPromptInfo");
                    RaisePropertyChanged("HasInputSection");
                }
            }
        }

        public string InputText
        {
            get { return _inputText; }
            set
            {
                if (SetProperty(ref _inputText, value))
                    InputError = null;
            }
        }

        public bool ShowAffiliationInput
        {
            get { return _showAffiliationInput; }
            set { SetProperty(ref _showAffiliationInput, value); }
        }

        public bool RequireAffiliation
        {
            get { return _requireAffiliation; }
            set { SetProperty(ref _requireAffiliation, value); }
        }

        public string AffiliationText
        {
            get { return _affiliationText; }
            set
            {
                if (SetProperty(ref _affiliationText, value))
                    InputError = null;
            }
        }

        public string InputError
        {
            get { return _inputError; }
            set
            {
                if (SetProperty(ref _inputError, value))
                    RaisePropertyChanged("HasInputError");
            }
        }

        public bool HasInputError
        {
            get { return !string.IsNullOrWhiteSpace(InputError); }
        }

        public string InfoIp
        {
            get { return _infoIp; }
            set
            {
                if (SetProperty(ref _infoIp, value))
                {
                    RaisePropertyChanged("HasPromptInfo");
                    RaisePropertyChanged("HasInputSection");
                }
            }
        }

        public string InfoWindowsAccount
        {
            get { return _infoWindowsAccount; }
            set
            {
                if (SetProperty(ref _infoWindowsAccount, value))
                {
                    RaisePropertyChanged("HasPromptInfo");
                    RaisePropertyChanged("HasInputSection");
                }
            }
        }

        public bool HasPromptInfo
        {
            get
            {
                return !string.IsNullOrWhiteSpace(InfoIp)
                    || !string.IsNullOrWhiteSpace(InfoWindowsAccount);
            }
        }

        public bool HasInputSection
        {
            get { return ShowInput || HasPromptInfo; }
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

        public bool TryAcceptInput()
        {
            if (!ShowInput)
                return true;
            if (ShowAffiliationInput)
            {
                string affiliation = AffiliationText != null ? AffiliationText.Trim() : string.Empty;
                if (RequireAffiliation && string.IsNullOrWhiteSpace(affiliation))
                {
                    InputError = "소속을 입력해 주세요.";
                    return false;
                }
                AffiliationText = affiliation;
            }
            string name = InputText != null ? InputText.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                InputError = "사용자명을 입력해 주세요.";
                return false;
            }
            InputText = name;
            return true;
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
            ShowInput = request.ShowInput || request.Kind == PopupKind.Prompt;
            InputText = request.InputText ?? string.Empty;
            ShowAffiliationInput = request.ShowAffiliationInput;
            RequireAffiliation = request.RequireAffiliation;
            AffiliationText = request.AffiliationText ?? string.Empty;
            InputError = null;
            InfoIp = request.InfoIp ?? string.Empty;
            InfoWindowsAccount = request.InfoWindowsAccount ?? string.Empty;

            Buttons.Clear();
            if (request.Buttons != null)
            {
                foreach (var b in request.Buttons)
                {
                    if (b != null)
                        Buttons.Add(b);
                }
            }
            if (Buttons.Count == 0 && request.Kind != PopupKind.Progress)
            {
                Buttons.Add(new PopupButtonDefinition("확인", PopupResultType.Primary, isDefault: true));
                if (request.Kind == PopupKind.Confirm)
                    Buttons.Add(new PopupButtonDefinition("취소", PopupResultType.Secondary, isCancel: true));
            }
        }
    }
}
