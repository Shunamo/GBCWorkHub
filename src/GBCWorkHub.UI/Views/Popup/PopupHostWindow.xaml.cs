using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GBCWorkHub.UI.ViewModels.Popup;

namespace GBCWorkHub.UI.Views.Popup
{
    public partial class PopupHostWindow : UserControl
    {
        public PopupHostWindow()
        {
            InitializeComponent();
        }

        private void Host_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as PopupHostViewModel;
            if (vm == null || !vm.ShowInput)
                return;
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                TextBox box = PromptInputBox;
                if (vm.ShowAffiliationInput
                    && AffiliationInputBox != null
                    && string.IsNullOrWhiteSpace(vm.AffiliationText))
                    box = AffiliationInputBox;
                if (box == null)
                    return;
                box.Focus();
                Keyboard.Focus(box);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
