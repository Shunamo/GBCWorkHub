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
        private string _selectedAffiliationOption;
        private string _affiliationCustomOptionLabel = "기타";
        private bool _showSecondaryInput;
        private bool _requireSecondaryInput;
        private string _secondaryInputLabel;
        private string _secondaryInputText;
        private bool _showPasswordInput;
        private bool _requirePassword;
        private string _passwordText;
        private bool _showCurrentPasswordInput;
        private string _currentPasswordText;
        private bool _showPasswordConfirm;
        private string _passwordConfirmText;
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

        /// <summary>비어 있으면(기존 호출자) 자유 입력만 보이고, 값이 있으면 칩 목록 + "기타" 직접입력을 보여준다.</summary>
        public ObservableCollection<string> AffiliationOptions { get; private set; }
            = new ObservableCollection<string>();

        public bool HasAffiliationOptions
        {
            get { return AffiliationOptions != null && AffiliationOptions.Count > 0; }
        }

        public string SelectedAffiliationOption
        {
            get { return _selectedAffiliationOption; }
            set
            {
                if (!SetProperty(ref _selectedAffiliationOption, value))
                    return;
                RaisePropertyChanged("IsAffiliationCustom");
                if (!IsAffiliationCustom)
                    AffiliationText = value ?? string.Empty;
                else if (string.Equals(AffiliationText, value, System.StringComparison.Ordinal))
                    AffiliationText = string.Empty;
            }
        }

        /// <summary>지금 선택된 칩이 "기타"라서 별도 자유 입력란을 보여줘야 하는지.</summary>
        public bool IsAffiliationCustom
        {
            get
            {
                return HasAffiliationOptions
                    && string.Equals(SelectedAffiliationOption, _affiliationCustomOptionLabel, System.StringComparison.Ordinal);
            }
        }

        public bool ShowSecondaryInput
        {
            get { return _showSecondaryInput; }
            set { SetProperty(ref _showSecondaryInput, value); }
        }

        public bool RequireSecondaryInput
        {
            get { return _requireSecondaryInput; }
            set { SetProperty(ref _requireSecondaryInput, value); }
        }

        public string SecondaryInputLabel
        {
            get { return _secondaryInputLabel; }
            set { SetProperty(ref _secondaryInputLabel, value); }
        }

        public string SecondaryInputText
        {
            get { return _secondaryInputText; }
            set
            {
                if (SetProperty(ref _secondaryInputText, value))
                    InputError = null;
            }
        }

        public bool ShowPasswordInput
        {
            get { return _showPasswordInput; }
            set
            {
                if (SetProperty(ref _showPasswordInput, value))
                {
                    RaisePropertyChanged("HasInputSection");
                    RaisePropertyChanged("ShowPromptFields");
                    RaisePropertyChanged("PasswordFieldLabel");
                    RaisePropertyChanged("ShowPasswordOptionalHint");
                }
            }
        }

        public bool RequirePassword
        {
            get { return _requirePassword; }
            set
            {
                if (SetProperty(ref _requirePassword, value))
                    RaisePropertyChanged("ShowPasswordOptionalHint");
            }
        }

        /// <summary>선택 입력(비밀번호 미변경 허용)일 때만 "비워두면 변경 안 함" 안내를 보여준다.</summary>
        public bool ShowPasswordOptionalHint
        {
            get { return ShowPasswordInput && !RequirePassword; }
        }

        /// <summary>PasswordBox is synced from code-behind (not two-way bindable).</summary>
        public string PasswordText
        {
            get { return _passwordText; }
            set
            {
                if (SetProperty(ref _passwordText, value))
                    InputError = null;
            }
        }

        public bool ShowCurrentPasswordInput
        {
            get { return _showCurrentPasswordInput; }
            set
            {
                if (SetProperty(ref _showCurrentPasswordInput, value))
                {
                    RaisePropertyChanged("HasInputSection");
                    RaisePropertyChanged("ShowPromptFields");
                    RaisePropertyChanged("PasswordFieldLabel");
                }
            }
        }

        public string CurrentPasswordText
        {
            get { return _currentPasswordText; }
            set
            {
                if (SetProperty(ref _currentPasswordText, value))
                    InputError = null;
            }
        }

        public string PasswordFieldLabel
        {
            get { return ShowCurrentPasswordInput ? "새 비밀번호" : "비밀번호"; }
        }

        public bool ShowPasswordConfirm
        {
            get { return _showPasswordConfirm; }
            set
            {
                if (SetProperty(ref _showPasswordConfirm, value))
                {
                    RaisePropertyChanged("HasInputSection");
                    RaisePropertyChanged("ShowPromptFields");
                }
            }
        }

        public string PasswordConfirmText
        {
            get { return _passwordConfirmText; }
            set
            {
                if (SetProperty(ref _passwordConfirmText, value))
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
            get
            {
                return ShowInput
                    || ShowPasswordInput
                    || ShowCurrentPasswordInput
                    || ShowPasswordConfirm
                    || HasPromptInfo;
            }
        }

        public bool ShowPromptFields
        {
            get
            {
                return ShowInput
                    || ShowPasswordInput
                    || ShowCurrentPasswordInput
                    || ShowPasswordConfirm;
            }
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
            if (ShowCurrentPasswordInput)
            {
                if (string.IsNullOrEmpty(CurrentPasswordText))
                {
                    InputError = "현재 비밀번호를 입력해 주세요.";
                    return false;
                }
            }

            bool passwordProvided = ShowPasswordInput && !string.IsNullOrEmpty(PasswordText);

            if (ShowPasswordInput && RequirePassword)
            {
                if (!passwordProvided)
                {
                    InputError = ShowCurrentPasswordInput
                        ? "새 비밀번호를 입력해 주세요."
                        : "비밀번호를 입력해 주세요.";
                    return false;
                }
            }

            if (passwordProvided && !IsAsciiLetterOrDigitOnly(PasswordText))
            {
                InputError = "비밀번호는 영문과 숫자만 사용할 수 있습니다.";
                return false;
            }

            if (ShowPasswordConfirm && (RequirePassword || passwordProvided))
            {
                if (string.IsNullOrEmpty(PasswordConfirmText))
                {
                    InputError = "비밀번호 확인을 입력해 주세요.";
                    return false;
                }
                if (!string.Equals(PasswordText ?? string.Empty, PasswordConfirmText ?? string.Empty, System.StringComparison.Ordinal))
                {
                    InputError = "비밀번호가 일치하지 않습니다.";
                    return false;
                }
            }

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
            if (ShowSecondaryInput)
            {
                string secondary = SecondaryInputText != null ? SecondaryInputText.Trim() : string.Empty;
                if (RequireSecondaryInput && string.IsNullOrWhiteSpace(secondary))
                {
                    InputError = (SecondaryInputLabel ?? "값") + "을(를) 입력해 주세요.";
                    return false;
                }
                SecondaryInputText = secondary;
            }
            string name = InputText != null ? InputText.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                InputError = "이름을 입력해 주세요.";
                return false;
            }
            InputText = name;
            return true;
        }

        private static bool IsAsciiLetterOrDigitOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                    return false;
            }
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
            _affiliationCustomOptionLabel = string.IsNullOrWhiteSpace(request.AffiliationCustomOptionLabel)
                ? "기타"
                : request.AffiliationCustomOptionLabel.Trim();
            AffiliationOptions.Clear();
            if (request.AffiliationOptions != null)
            {
                foreach (string option in request.AffiliationOptions)
                {
                    if (!string.IsNullOrWhiteSpace(option))
                        AffiliationOptions.Add(option.Trim());
                }
            }
            RaisePropertyChanged("HasAffiliationOptions");
            AffiliationText = request.AffiliationText ?? string.Empty;
            if (HasAffiliationOptions)
            {
                bool matchesOption = false;
                foreach (string option in AffiliationOptions)
                {
                    if (string.Equals(option, AffiliationText, System.StringComparison.Ordinal))
                    {
                        matchesOption = true;
                        break;
                    }
                }
                if (matchesOption)
                    SelectedAffiliationOption = AffiliationText;
                else if (!string.IsNullOrWhiteSpace(AffiliationText))
                    SelectedAffiliationOption = _affiliationCustomOptionLabel;
                else
                    SelectedAffiliationOption = null;
            }
            else
            {
                SelectedAffiliationOption = null;
            }
            ShowSecondaryInput = request.ShowSecondaryInput;
            RequireSecondaryInput = request.RequireSecondaryInput;
            SecondaryInputLabel = request.SecondaryInputLabel ?? string.Empty;
            SecondaryInputText = request.SecondaryInputText ?? string.Empty;
            ShowPasswordInput = request.ShowPasswordInput;
            RequirePassword = request.RequirePassword;
            PasswordText = request.PasswordText ?? string.Empty;
            ShowCurrentPasswordInput = request.ShowCurrentPasswordInput;
            CurrentPasswordText = request.CurrentPasswordText ?? string.Empty;
            ShowPasswordConfirm = request.ShowPasswordConfirm;
            PasswordConfirmText = request.PasswordConfirmText ?? string.Empty;
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
