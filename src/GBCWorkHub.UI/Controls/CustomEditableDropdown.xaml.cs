using System;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// 직접 입력 + 후보 Popup. 기본 ComboBox 미사용.
    /// Text TwoWay, 후보 SelectedItem은 선택 시 Text에 반영.
    /// </summary>
    public partial class CustomEditableDropdown : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(CustomEditableDropdown),
                new PropertyMetadata(null, OnItemsChanged));

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(CustomEditableDropdown),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(CustomEditableDropdown),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty EmptyTextProperty =
            DependencyProperty.Register("EmptyText", typeof(string), typeof(CustomEditableDropdown),
                new PropertyMetadata("추천할 Project Name이 없습니다. 직접 입력해 주세요."));

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register("IsDropDownOpen", typeof(bool), typeof(CustomEditableDropdown),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MaxDropDownHeightProperty =
            DependencyProperty.Register("MaxDropDownHeight", typeof(double), typeof(CustomEditableDropdown),
                new PropertyMetadata(200.0));

        public CustomEditableDropdown()
        {
            InitializeComponent();
            Loaded += (s, e) => RefreshEmpty();
        }

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        public string EmptyText
        {
            get { return (string)GetValue(EmptyTextProperty); }
            set { SetValue(EmptyTextProperty, value); }
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

        private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CustomEditableDropdown)d).RefreshEmpty();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (CustomEditableDropdown)d;
            if (c.PART_TextBox != null && c.PART_TextBox.Text != (e.NewValue as string ?? string.Empty))
                c.PART_TextBox.Text = e.NewValue as string ?? string.Empty;
        }

        private void Arrow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Text = PART_TextBox != null ? PART_TextBox.Text : string.Empty;
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // 후보가 있으면 포커스 시 열어 보여줄 수 있음 — 강제 오픈은 하지 않음
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PART_ListBox == null || PART_ListBox.SelectedItem == null)
                return;
            Text = PART_ListBox.SelectedItem.ToString();
            IsDropDownOpen = false;
        }

        private void Popup_Closed(object sender, EventArgs e)
        {
            IsDropDownOpen = false;
        }

        private void RefreshEmpty()
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
