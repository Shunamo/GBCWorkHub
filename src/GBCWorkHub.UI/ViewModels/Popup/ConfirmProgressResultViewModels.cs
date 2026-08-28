using GBCWorkHub.UI.Models.Popup;

namespace GBCWorkHub.UI.ViewModels.Popup
{
    public sealed class ConfirmPopupViewModel : ViewModelBase
    {
        public PopupRequest Request { get; set; }
    }

    public sealed class ProgressPopupViewModel : ViewModelBase
    {
        private string _stepText;
        public string StepText
        {
            get { return _stepText; }
            set { SetProperty(ref _stepText, value); }
        }
    }

    public sealed class ResultPopupViewModel : ViewModelBase
    {
        public PopupRequest Request { get; set; }
    }
}
