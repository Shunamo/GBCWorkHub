using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Views
{
    public partial class RemoteWorkspaceView : System.Windows.Controls.UserControl
    {
        private const double BottomSheetMaxHeightRatio = 0.58;

        public RemoteWorkspaceView()
        {
            InitializeComponent();
        }

        private void GalleryBodyGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (UsageHistoryHost == null)
                return;
            double h = e.NewSize.Height;
            if (h <= 0)
                return;
            UsageHistoryHost.MaxHeight = Math.Max(280, h * BottomSheetMaxHeightRatio);
        }

        private void RemoteComputerCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as RemoteWorkspaceViewModel;
            if (vm == null)
                return;

            var border = sender as FrameworkElement;
            var item = border != null ? border.DataContext as RemoteComputerItemViewModel : null;
            if (item == null)
                return;

            if (vm.ActivateRemoteComputerCommand != null && vm.ActivateRemoteComputerCommand.CanExecute(item))
                vm.ActivateRemoteComputerCommand.Execute(item);

            e.Handled = true;
        }

        private void GalleryRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as RemoteWorkspaceViewModel;
            if (vm == null || !vm.HasSelectedRemoteComputer)
                return;

            var source = e.OriginalSource as DependencyObject;
            if (IsInsidePcCard(source) || IsInsideNamed(source, "UsageHistoryHost"))
                return;

            vm.ClearRemoteComputerSelection();
        }

        private static bool IsInsidePcCard(DependencyObject start)
        {
            DependencyObject current = start;
            while (current != null)
            {
                var fe = current as FrameworkElement;
                if (fe != null && fe.DataContext is RemoteComputerItemViewModel)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private static bool IsInsideNamed(DependencyObject start, string name)
        {
            DependencyObject current = start;
            while (current != null)
            {
                var fe = current as FrameworkElement;
                if (fe != null && string.Equals(fe.Name, name, StringComparison.Ordinal))
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
