using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// 기존 관리자 페이지 기간 선택 달력과 같은 스타일의 재사용 가능한 단일 달력 기간 선택 버튼.
    /// 시작일을 클릭하고 종료일을 클릭하면(관리자 페이지와 동일한 클릭 규칙) 자동으로 닫힌다.
    /// </summary>
    public partial class DateRangePickerButton : UserControl
    {
        public static readonly DependencyProperty StartDateProperty =
            DependencyProperty.Register(
                "StartDate",
                typeof(DateTime?),
                typeof(DateRangePickerButton),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangeChanged));

        public static readonly DependencyProperty EndDateProperty =
            DependencyProperty.Register(
                "EndDate",
                typeof(DateTime?),
                typeof(DateRangePickerButton),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangeChanged));

        private readonly ObservableCollection<CalendarDayItem> _days = new ObservableCollection<CalendarDayItem>();
        private DateTime _calendarMonth;

        public DateRangePickerButton()
        {
            InitializeComponent();
            DaysItemsControl.ItemsSource = _days;
            _calendarMonth = new DateTime(KoreaTime.Today.Year, KoreaTime.Today.Month, 1);
            Toggle.Checked += Toggle_Checked;
            RebuildDays();
        }

        public DateTime? StartDate
        {
            get { return (DateTime?)GetValue(StartDateProperty); }
            set { SetValue(StartDateProperty, value); }
        }

        public DateTime? EndDate
        {
            get { return (DateTime?)GetValue(EndDateProperty); }
            set { SetValue(EndDateProperty, value); }
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (DateRangePickerButton)d;
            control.RebuildDays();
        }

        private void Toggle_Checked(object sender, RoutedEventArgs e)
        {
            DateTime anchor = StartDate ?? KoreaTime.Today;
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

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || !(button.Tag is DateTime))
                return;

            DateTime day = ((DateTime)button.Tag).Date;
            if (!StartDate.HasValue || (StartDate.HasValue && EndDate.HasValue))
            {
                StartDate = day;
                EndDate = null;
            }
            else if (day < StartDate.Value.Date)
            {
                EndDate = StartDate;
                StartDate = day;
            }
            else
            {
                EndDate = day;
            }

            if (StartDate.HasValue && EndDate.HasValue)
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
            DateTime? rangeStart = StartDate;
            DateTime? rangeEnd = EndDate ?? StartDate;
            bool multi = rangeStart.HasValue && rangeEnd.HasValue && rangeStart.Value.Date != rangeEnd.Value.Date;

            _days.Clear();
            for (int i = 0; i < 42; i++)
            {
                DateTime d = gridStart.AddDays(i);
                bool inRange = rangeStart.HasValue && rangeEnd.HasValue
                    && d.Date >= rangeStart.Value.Date && d.Date <= rangeEnd.Value.Date;
                bool isStart = rangeStart.HasValue && d.Date == rangeStart.Value.Date;
                bool isEnd = rangeEnd.HasValue && d.Date == rangeEnd.Value.Date;
                _days.Add(new CalendarDayItem
                {
                    Date = d,
                    DayText = d.Day.ToString(),
                    IsCurrentMonth = d.Month == _calendarMonth.Month,
                    IsToday = d.Date == today,
                    IsRangeStart = isStart,
                    IsRangeEnd = isEnd,
                    IsInRange = inRange,
                    RangeFillMid = inRange && multi && !isStart && !isEnd,
                    RangeFillFromStart = inRange && multi && isStart && !isEnd,
                    RangeFillToEnd = inRange && multi && isEnd && !isStart,
                    IsSunday = d.DayOfWeek == DayOfWeek.Sunday,
                    IsSaturday = d.DayOfWeek == DayOfWeek.Saturday
                });
            }
        }
    }
}
