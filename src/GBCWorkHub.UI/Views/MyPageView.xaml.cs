using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Views
{
    public partial class MyPageView : UserControl
    {
        public MyPageView()
        {
            InitializeComponent();
        }

        private void SearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var box = sender as TextBox;
            if (box != null && box.IsVisible)
                box.Focus();
        }

        private void SessionPickerScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as MyPageViewModel;
            if (vm == null || vm.ToggleSessionSearchCommand == null)
                return;
            if (vm.ToggleSessionSearchCommand.CanExecute(null))
                vm.ToggleSessionSearchCommand.Execute(null);
        }
    }
}
