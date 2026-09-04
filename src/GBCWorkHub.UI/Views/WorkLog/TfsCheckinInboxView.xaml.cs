using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GBCWorkHub.UI.ViewModels;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.Views.WorkLog
{
    public partial class TfsCheckinInboxView : UserControl
    {
        private TfsCheckinInboxViewModel _inboxHooked;

        public static readonly DependencyProperty CompactListProperty =
            DependencyProperty.Register(
                "CompactList",
                typeof(bool),
                typeof(TfsCheckinInboxView),
                new PropertyMetadata(false, OnCompactListChanged));

        public TfsCheckinInboxView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                HookInbox();
                ApplyCompactList();
            };
            DataContextChanged += (s, e) =>
            {
                HookInbox();
                ApplyCompactList();
            };
        }

        public bool CompactList
        {
            get { return (bool)GetValue(CompactListProperty); }
            set { SetValue(CompactListProperty, value); }
        }

        private static void OnCompactListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = d as TfsCheckinInboxView;
            if (view != null)
                view.ApplyCompactList();
        }

        private void HookInbox()
        {
            if (_inboxHooked != null)
                _inboxHooked.PropertyChanged -= OnInboxPropertyChanged;
            _inboxHooked = ResolveInbox();
            if (_inboxHooked != null)
                _inboxHooked.PropertyChanged += OnInboxPropertyChanged;
        }

        private void OnInboxPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName == "HasRows")
                ApplyCompactList();
        }

        private TfsCheckinInboxViewModel ResolveInbox()
        {
            var myPage = DataContext as MyPageViewModel;
            if (myPage != null)
                return myPage.Inbox;
            var workLog = DataContext as WorkLogListViewModel;
            if (workLog != null)
                return workLog.Inbox;
            return null;
        }

        private void ApplyCompactList()
        {
            if (InboxListRow == null)
                return;
            bool hasRows = _inboxHooked != null && _inboxHooked.HasRows;
            if (CompactList && hasRows)
            {
                InboxListRow.Height = GridLength.Auto;
                if (InboxListScroll != null)
                    InboxListScroll.MaxHeight = 252;
            }
            else
            {
                InboxListRow.Height = new GridLength(1, GridUnitType.Star);
                if (InboxListScroll != null)
                    InboxListScroll.MaxHeight = double.PositiveInfinity;
            }
        }
    }
}
