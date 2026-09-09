using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Views
{
    public partial class AdminView : UserControl
    {
        public AdminView()
        {
            InitializeComponent();
        }

        private void AdminPcCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            var item = border != null ? border.DataContext as AdminPcItemViewModel : null;
            var vm = DataContext as AdminViewModel;
            if (item == null || vm == null || vm.SelectAdminPcCommand == null)
                return;
            if (vm.SuppressPcListSelection)
                return;
            if (vm.SelectAdminPcCommand.CanExecute(item))
                vm.SelectAdminPcCommand.Execute(item);
        }

        private void AdminUserCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            var item = border != null ? border.DataContext as AdminUserItemViewModel : null;
            var vm = DataContext as AdminViewModel;
            if (item == null || vm == null || vm.SelectAdminUserCommand == null)
                return;
            if (vm.SelectAdminUserCommand.CanExecute(item))
                vm.SelectAdminUserCommand.Execute(item);
        }

        private void AdminOccupancyCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            var item = border != null ? border.DataContext as AdminOccupancyItemViewModel : null;
            var vm = DataContext as AdminViewModel;
            if (item == null || vm == null || vm.SelectOccupancyCommand == null)
                return;
            if (vm.SelectOccupancyCommand.CanExecute(item))
                vm.SelectOccupancyCommand.Execute(item);
        }

        private void AdminUsageLogCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            var item = border != null ? border.DataContext as AdminUsageLogItemViewModel : null;
            var vm = DataContext as AdminViewModel;
            if (item == null || vm == null || vm.SelectUsageLogCommand == null)
                return;
            if (vm.SelectUsageLogCommand.CanExecute(item))
                vm.SelectUsageLogCommand.Execute(item);
        }

        private void AdminWorkLogCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            var item = border != null ? border.DataContext as AdminWorkLogItemViewModel : null;
            var vm = DataContext as AdminViewModel;
            if (item == null || vm == null || vm.SelectWorkLogCommand == null)
                return;
            if (vm.SelectWorkLogCommand.CanExecute(item))
                vm.SelectWorkLogCommand.Execute(item);
        }

        private void WorkLogDatePickerScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as AdminViewModel;
            if (vm == null || vm.CloseWorkLogDatePickerCommand == null)
                return;
            if (vm.CloseWorkLogDatePickerCommand.CanExecute(null))
                vm.CloseWorkLogDatePickerCommand.Execute(null);
            e.Handled = true;
        }
    }
}
