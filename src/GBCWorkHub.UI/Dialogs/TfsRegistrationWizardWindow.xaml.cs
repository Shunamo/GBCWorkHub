using System.Windows;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Dialogs
{
    public partial class TfsRegistrationWizardWindow : Window
    {
        private readonly TfsRegistrationWizardViewModel _viewModel;

        public TfsRegistrationWizardWindow(TfsRegistrationWizardViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        public TfsRegistrationWizardViewModel ViewModel
        {
            get { return _viewModel; }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Persist는 OnClosing에서만 (DialogResult != true) — 여기서 호출하면 중복
            DialogResult = false;
            Close();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
                return;

            if (_viewModel.NextCommand.CanExecute(null))
                _viewModel.NextCommand.Execute(null);

            if (_viewModel.ConfirmedSave)
            {
                _viewModel.PersistDraftsToCandidates();
                DialogResult = true;
                Close();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel != null && DialogResult != true)
                _viewModel.PersistDraftsToCandidates();
            base.OnClosing(e);
        }
    }
}
