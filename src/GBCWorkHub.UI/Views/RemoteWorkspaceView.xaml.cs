using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Views
{
    public partial class RemoteWorkspaceView : UserControl
    {
        public RemoteWorkspaceView()
        {
            InitializeComponent();
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

            if (vm.SelectRemoteComputerCommand != null && vm.SelectRemoteComputerCommand.CanExecute(item))
                vm.SelectRemoteComputerCommand.Execute(item);

            if (vm.PrimaryActionCommand != null && vm.PrimaryActionCommand.CanExecute(item))
                vm.PrimaryActionCommand.Execute(item);
        }
    }
}
