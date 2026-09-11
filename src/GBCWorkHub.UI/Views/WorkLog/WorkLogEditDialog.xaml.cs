using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.Views.WorkLog
{
    public partial class WorkLogEditDialog : UserControl
    {
        private WorkLogEditDialogViewModel _wiredVm;

        public WorkLogEditDialog()
        {
            InitializeComponent();
            if (WorkLogUiOptions.UseGlassmorphism)
                ApplyGlassOverrides();

            DataContextChanged += OnDataContextChanged;
        }

        private void StartDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            StartDateCalendarToggle.IsChecked = false;
        }

        private void EndDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            EndDateCalendarToggle.IsChecked = false;
        }

        private void ApplyGlassOverrides()
        {
            var overrides = new ResourceDictionary
            {
                Source = new Uri(
                    "/GBCWorkHub;component/Assets/WorkLogEditGlassOverrides.xaml",
                    UriKind.Relative)
            };

            // Last merged dictionary wins for keys not defined locally.
            Resources.MergedDictionaries.Add(overrides);

            // Local tree styles still shadow merges — promote override keys onto Resources.
            var keys = new object[overrides.Count];
            overrides.Keys.CopyTo(keys, 0);
            foreach (var key in keys)
                Resources[key] = overrides[key];
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_wiredVm != null)
                _wiredVm.RequestScrollToWorkForm -= OnRequestScrollToWorkForm;

            _wiredVm = e.NewValue as WorkLogEditDialogViewModel;
            if (_wiredVm != null)
                _wiredVm.RequestScrollToWorkForm += OnRequestScrollToWorkForm;
        }

        private void OnRequestScrollToWorkForm()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WorkFormStackPanel != null)
                    WorkFormStackPanel.BringIntoView();
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Category 칩 선택(빈 Category 포함) 시 아래 필드 공개.</summary>
        private void WorkCategoryPicker_SelectionCommitted(object sender, EventArgs e)
        {
            var vm = DataContext as WorkLogEditDialogViewModel;
            if (vm == null || vm.UnlockWorkCategoryCommand == null)
                return;
            if (vm.UnlockWorkCategoryCommand.CanExecute(null))
                vm.UnlockWorkCategoryCommand.Execute(null);
        }
    }
}
