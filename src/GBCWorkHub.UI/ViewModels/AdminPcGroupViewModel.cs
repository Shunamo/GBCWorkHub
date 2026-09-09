using System.Collections.ObjectModel;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminPcGroupViewModel
    {
        public string Name { get; private set; }

        public bool HasName
        {
            get { return !string.IsNullOrWhiteSpace(Name); }
        }

        public ObservableCollection<AdminPcItemViewModel> Computers { get; private set; }

        public AdminPcGroupViewModel(string name)
        {
            Name = name ?? string.Empty;
            Computers = new ObservableCollection<AdminPcItemViewModel>();
        }
    }
}
