using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// TextBox 직접 입력 + 아래 항상 보이는 추천 칩. ComboBox/Popup 미사용.
    /// </summary>
    public partial class InlineSuggestionField : UserControl
    {
        private static readonly Brush AccentBg = (Brush)new BrushConverter().ConvertFromString("#EFF6FF");
        private static readonly Brush AccentBorder = (Brush)new BrushConverter().ConvertFromString("#2563EB");
        private static readonly Brush AccentFg = (Brush)new BrushConverter().ConvertFromString("#1D4ED8");
        private static readonly Brush NormalBorder = (Brush)new BrushConverter().ConvertFromString("#E2E8F0");
        private static readonly Brush NormalFg = (Brush)new BrushConverter().ConvertFromString("#334155");
        private INotifyCollectionChanged _itemsNotify;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(InlineSuggestionField),
                new PropertyMetadata(null, OnItemsChanged));

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(InlineSuggestionField),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(InlineSuggestionField),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty EmptyTextProperty =
            DependencyProperty.Register("EmptyText", typeof(string), typeof(InlineSuggestionField),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SuggestionHeaderProperty =
            DependencyProperty.Register("SuggestionHeader", typeof(string), typeof(InlineSuggestionField),
                new PropertyMetadata("최근 사용"));

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register("IsDropDownOpen", typeof(bool), typeof(InlineSuggestionField),
                new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty MaxDropDownHeightProperty =
            DependencyProperty.Register("MaxDropDownHeight", typeof(double), typeof(InlineSuggestionField),
                new PropertyMetadata(220.0));

        public InlineSuggestionField()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                SyncTextBox();
                RebuildOptions();
            };
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

        public string SuggestionHeader
        {
            get { return (string)GetValue(SuggestionHeaderProperty); }
            set { SetValue(SuggestionHeaderProperty, value); }
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
            var c = (InlineSuggestionField)d;
            c.DetachItemsNotify(e.OldValue as INotifyCollectionChanged);
            c.AttachItemsNotify(e.NewValue as INotifyCollectionChanged);
            c.RebuildOptions();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (InlineSuggestionField)d;
            c.SyncTextBox();
            c.RebuildOptions();
        }

        private void AttachItemsNotify(INotifyCollectionChanged notify)
        {
            _itemsNotify = notify;
            if (_itemsNotify != null)
                _itemsNotify.CollectionChanged += Items_CollectionChanged;
        }

        private void DetachItemsNotify(INotifyCollectionChanged notify)
        {
            if (notify != null)
                notify.CollectionChanged -= Items_CollectionChanged;
            if (ReferenceEquals(_itemsNotify, notify))
                _itemsNotify = null;
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildOptions();
        }

        private void SyncTextBox()
        {
            if (PART_TextBox == null)
                return;
            string value = Text ?? string.Empty;
            if (PART_TextBox.Text != value)
                PART_TextBox.Text = value;
            PART_TextBox.ToolTip = string.IsNullOrEmpty(Placeholder) ? null : Placeholder;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Text = PART_TextBox != null ? PART_TextBox.Text : string.Empty;
        }

        private void RebuildOptions()
        {
            if (PART_Options == null)
                return;
            PART_Options.Children.Clear();
            if (ItemsSource == null)
                return;

            foreach (var item in ItemsSource)
            {
                if (item == null)
                    continue;
                string label = item.ToString();
                if (string.IsNullOrWhiteSpace(label))
                    continue;
                bool selected = string.Equals(label, Text, StringComparison.OrdinalIgnoreCase);

                var text = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = selected ? AccentFg : NormalFg,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                var content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    IsHitTestVisible = false
                };
                if (selected)
                {
                    var check = new Image
                    {
                        Width = 12,
                        Height = 12,
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false
                    };
                    check.SetResourceReference(Image.SourceProperty, "DoneLightIcon");
                    content.Children.Add(check);
                }
                content.Children.Add(text);

                var border = new Border
                {
                    Background = selected ? AccentBg : Brushes.Transparent,
                    BorderBrush = selected ? AccentBorder : NormalBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 4),
                    Cursor = Cursors.Hand,
                    Child = content
                };

                string captured = label;
                border.MouseLeftButtonUp += (s, ev) =>
                {
                    Text = captured;
                    SyncTextBox();
                    RebuildOptions();
                    ev.Handled = true;
                };

                PART_Options.Children.Add(border);
            }
        }
    }
}
