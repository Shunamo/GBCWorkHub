using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// 기존 관리자/마이페이지 달력 팝업과 같은 스타일의 재사용 가능한 단일 날짜 선택 버튼.
    /// 아이콘 버튼을 누르면 달력이 뜨고, 날짜를 클릭하면 SelectedDate에 반영되고 닫힌다.
    /// </summary>
    public partial class DatePickerButton : UserControl
    {
        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(
                "SelectedDate",
                typeof(DateTime?),
                typeof(DatePickerButton),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

        private readonly ObservableCollection<CalendarDayItem> _days = new ObservableCollection<CalendarDayItem>();
        private DateTime _calendarMonth;

        public DatePickerButton()
        {
            InitializeComponent();
            DaysItemsControl.ItemsSource = _days;
            _calendarMonth = new DateTime(KoreaTime.Today.Year, KoreaTime.Today.Month, 1);
            Toggle.Checked += Toggle_Checked;
            RebuildDays();
        }

        public DateTime? SelectedDate
        {
            get { return (DateTime?)GetValue(SelectedDateProperty); }
            set { SetValue(SelectedDateProperty, value); }
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (DatePickerButton)d;
            control.RebuildDays();
        }

        private void Toggle_Checked(object sender, RoutedEventArgs e)
        {
            DateTime anchor = SelectedDate ?? KoreaTime.Today;
            _calendarMonth = new DateTime(anchor.Year, anchor.Month, 1);
            RebuildDays();
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _calendarMonth = _calendarMonth.AddMonths(-1);
            RebuildDays();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _calendarMonth = _calendarMonth.AddMonths(1);
            RebuildDays();
        }

        private void Today_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = KoreaTime.Today;
            Toggle.IsChecked = false;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SelectedDate = null;
            Toggle.IsChecked = false;
        }

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || !(button.Tag is DateTime))
                return;
            SelectedDate = (DateTime)button.Tag;
            Toggle.IsChecked = false;
        }

        private void RebuildDays()
        {
            if (MonthTitleText == null)
                return;

            MonthTitleText.Text = _calendarMonth.ToString("yyyy년 M월");

            int startOffset = (int)_calendarMonth.DayOfWeek;
            DateTime gridStart = _calendarMonth.AddDays(-startOffset);
            DateTime today = KoreaTime.Today;
            DateTime? selected = SelectedDate;

            _days.Clear();
            for (int i = 0; i < 42; i++)
            {
                DateTime d = gridStart.AddDays(i);
                bool isSelected = selected.HasValue && d.Date == selected.Value.Date;
                _days.Add(new CalendarDayItem
                {
                    Date = d,
                    DayText = d.Day.ToString(),
                    IsCurrentMonth = d.Month == _calendarMonth.Month,
                    IsToday = d.Date == today,
                    IsRangeStart = isSelected,
                    IsRangeEnd = isSelected,
                    IsSunday = d.DayOfWeek == DayOfWeek.Sunday,
                    IsSaturday = d.DayOfWeek == DayOfWeek.Saturday
                });
            }
        }
    }
}
