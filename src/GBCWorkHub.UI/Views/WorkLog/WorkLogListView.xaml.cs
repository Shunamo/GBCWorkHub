using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.Views.WorkLog
{
    public partial class WorkLogListView : UserControl
    {
        private WorkLogListViewModel _wiredVm;

        public WorkLogListView()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
            DataContextChanged += OnDataContextChanged;
        }

        private void ImportCardBorder_SizeChanged(object sender, RoutedEventArgs e)
        {
            ApplyImportCardClip();
        }

        private void ApplyImportCardClip()
        {
            if (ImportCardBorder == null || ImportCardBorder.ActualWidth <= 0 || ImportCardBorder.ActualHeight <= 0)
                return;

            // 헤더/푸터 사각 배경이 라운드 밖으로 삐져나오지 않게 클립
            double r = ImportCardBorder.CornerRadius.TopLeft;
            ImportCardBorder.Clip = new RectangleGeometry(
                new Rect(0, 0, ImportCardBorder.ActualWidth, ImportCardBorder.ActualHeight),
                r, r);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_wiredVm != null)
                _wiredVm.PropertyChanged -= OnVmPropertyChanged;
            _wiredVm = e.NewValue as WorkLogListViewModel;
            if (_wiredVm != null)
                _wiredVm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsImportOpen" && _wiredVm != null && _wiredVm.IsImportOpen)
                Focus();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            var vm = DataContext as WorkLogListViewModel;
            if (vm == null)
                return;
            if (TryCloseCommand(vm.CloseImportCommand) || TryCloseCommand(vm.CloseInboxCommand))
                e.Handled = true;
        }

        private void ImportOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as WorkLogListViewModel;
            if (vm != null)
                TryCloseCommand(vm.CloseImportCommand);
        }

        private void InboxOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as WorkLogListViewModel;
            if (vm != null)
                TryCloseCommand(vm.CloseInboxCommand);
        }

        private void OverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private static bool TryCloseCommand(ICommand command)
        {
            if (command == null || !command.CanExecute(null))
                return false;
            command.Execute(null);
            return true;
        }
    }
}
