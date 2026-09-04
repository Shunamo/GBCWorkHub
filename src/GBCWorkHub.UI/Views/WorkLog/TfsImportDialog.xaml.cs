using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.Views.WorkLog
{
    public partial class TfsImportDialog : UserControl
    {
        private TfsImportDialogViewModel _wiredVm;

        public TfsImportDialog()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => ApplyBodyRow();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_wiredVm != null)
                _wiredVm.PropertyChanged -= OnVmPropertyChanged;
            _wiredVm = e.NewValue as TfsImportDialogViewModel;
            if (_wiredVm != null)
                _wiredVm.PropertyChanged += OnVmPropertyChanged;
            ApplyBodyRow();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "HasCandidates" || e.PropertyName == "IsEmpty")
                ApplyBodyRow();
        }

        private void ApplyBodyRow()
        {
            if (ImportBodyRow == null)
                return;
            bool list = _wiredVm != null && _wiredVm.HasCandidates;
            ImportBodyRow.Height = list
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;
        }
    }
}
