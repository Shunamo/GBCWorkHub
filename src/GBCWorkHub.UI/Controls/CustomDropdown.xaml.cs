using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// 기본 WPF ComboBox를 사용하지 않는 커스텀 Dropdown.
    /// ToggleButton + Popup + ListBox 조합.
    /// </summary>
    public partial class CustomDropdown : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(CustomDropdown),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(CustomDropdown),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register("DisplayMemberPath", typeof(string), typeof(CustomDropdown),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(CustomDropdown),
                new PropertyMetadata("선택하세요", OnDisplayChanged));

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register("IsDropDownOpen", typeof(bool), typeof(CustomDropdown),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

        public static readonly DependencyProperty MaxDropDownHeightProperty =
            DependencyProperty.Register("MaxDropDownHeight", typeof(double), typeof(CustomDropdown),
                new PropertyMetadata(240.0));

        public static readonly DependencyProperty IsSearchEnabledProperty =
            DependencyProperty.Register("IsSearchEnabled", typeof(bool), typeof(CustomDropdown),
                new PropertyMetadata(false));

        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register("SearchText", typeof(string), typeof(CustomDropdown),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ValidationMessageProperty =
            DependencyProperty.Register("ValidationMessage", typeof(string), typeof(CustomDropdown),
                new PropertyMetadata(string.Empty, OnValidationChanged));

        public static readonly DependencyProperty EmptyTextProperty =
            DependencyProperty.Register("EmptyText", typeof(string), typeof(CustomDropdown),
                new PropertyMetadata("선택 가능한 항목이 없습니다."));

        public CustomDropdown()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public object SelectedItem
        {
            get { return GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        public string DisplayMemberPath
        {
            get { return (string)GetValue(DisplayMemberPathProperty); }
            set { SetValue(DisplayMemberPathProperty, value); }
        }

        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        public bool IsDropDownOpen
        {
            get { return (bool)GetValue(IsDropDownOpenProperty); }
            set { SetValue(IsDropDownOpenProperty, value); }
        }

        public double MaxDropDownHeight
        {
            get { return (double)GetValue(MaxDropDownHeightProperty); }
            set { SetValue(MaxDropDownHeightProperty, value); }
        }

        public bool IsSearchEnabled
        {
            get { return (bool)GetValue(IsSearchEnabledProperty); }
            set { SetValue(IsSearchEnabledProperty, value); }
        }

        public string SearchText
        {
            get { return (string)GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }

        public string ValidationMessage
        {
            get { return (string)GetValue(ValidationMessageProperty); }
            set { SetValue(ValidationMessageProperty, value); }
        }

        public string EmptyText
        {
            get { return (string)GetValue(EmptyTextProperty); }
            set { SetValue(EmptyTextProperty, value); }
        }

        public bool HasValidationError
        {
            get { return !string.IsNullOrWhiteSpace(ValidationMessage); }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDisplayText();
            UpdateValidationVisual();
            RefreshEmptyState();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (CustomDropdown)d;
            c.RefreshEmptyState();
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CustomDropdown)d).UpdateDisplayText();
        }

        private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CustomDropdown)d).UpdateDisplayText();
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (CustomDropdown)d;
            if (c.PART_Popup != null)
                c.PART_Popup.IsOpen = c.IsDropDownOpen;
            c.UpdateArrow();
        }

        private static void OnValidationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CustomDropdown)d).UpdateValidationVisual();
        }

        private void RootBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled)
                return;
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }

        private void Popup_Closed(object sender, EventArgs e)
        {
            IsDropDownOpen = false;
            UpdateArrow();
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PART_ListBox == null || PART_ListBox.SelectedItem == null)
                return;
            SelectedItem = PART_ListBox.SelectedItem;
            IsDropDownOpen = false;
            UpdateDisplayText();
        }

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnabled)
                return;

            if (e.Key == Key.Escape && IsDropDownOpen)
            {
                IsDropDownOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (!IsDropDownOpen)
                {
                    IsDropDownOpen = true;
                    e.Handled = true;
                    return;
                }
                if (PART_ListBox != null && PART_ListBox.SelectedItem != null)
                {
                    SelectedItem = PART_ListBox.SelectedItem;
                    IsDropDownOpen = false;
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                if (!IsDropDownOpen)
                    IsDropDownOpen = true;
                MoveListSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
            }
        }

        private void MoveListSelection(int delta)
        {
            if (PART_ListBox == null || PART_ListBox.Items.Count == 0)
                return;
            int idx = PART_ListBox.SelectedIndex;
            if (idx < 0)
                idx = 0;
            else
                idx = Math.Max(0, Math.Min(PART_ListBox.Items.Count - 1, idx + delta));
            PART_ListBox.SelectedIndex = idx;
            PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
        }

        private void UpdateDisplayText()
        {
            if (PART_DisplayText == null)
                return;

            if (SelectedItem == null)
            {
                PART_DisplayText.Text = Placeholder ?? string.Empty;
                PART_DisplayText.Foreground = System.Windows.Media.Brushes.Gray;
                return;
            }

            string text = ResolveDisplay(SelectedItem);
            PART_DisplayText.Text = text;
            PART_DisplayText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B"));
        }

        private string ResolveDisplay(object item)
        {
            if (item == null)
                return string.Empty;
            if (string.IsNullOrWhiteSpace(DisplayMemberPath))
                return item.ToString();
            var prop = TypeDescriptor.GetProperties(item)[DisplayMemberPath];
            if (prop == null)
                return item.ToString();
            object val = prop.GetValue(item);
            return val != null ? val.ToString() : string.Empty;
        }

        private void UpdateArrow()
        {
            if (PART_Arrow == null)
                return;
            var key = IsDropDownOpen ? "ExpandUpIcon" : "ExpandDownIcon";
            var img = TryFindResource(key) as System.Windows.Media.ImageSource;
            if (img != null)
                PART_Arrow.Source = img;
        }

        private void UpdateValidationVisual()
        {
            if (PART_RootBorder == null)
                return;
            if (HasValidationError)
            {
                PART_RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F97316"));
            }
            else
            {
                PART_RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D8DEE8"));
            }
            if (PART_ValidationText != null)
            {
                PART_ValidationText.Text = ValidationMessage ?? string.Empty;
                PART_ValidationText.Visibility = HasValidationError ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshEmptyState()
        {
            if (PART_EmptyText == null || PART_ListBox == null)
                return;
            bool any = false;
            if (ItemsSource != null)
            {
                foreach (var _ in ItemsSource)
                {
                    any = true;
                    break;
                }
            }
            PART_EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            PART_ListBox.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
