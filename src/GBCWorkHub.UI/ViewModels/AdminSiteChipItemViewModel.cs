namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminSiteChipItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public string Code { get; private set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value, "IsSelected"); }
        }

        public AdminSiteChipItemViewModel(string code)
        {
            Code = code ?? string.Empty;
        }
    }
}
