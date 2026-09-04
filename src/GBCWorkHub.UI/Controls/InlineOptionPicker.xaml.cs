using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// 항상 보이는 가로 나열형 옵션 칩. 클릭해서 펼칠 필요 없음. ComboBox/Popup 미사용.
    /// </summary>
    public partial class InlineOptionPicker : UserControl
    {
        private static readonly Brush AccentBg = FreezeBrush("#EFF6FF");
        private static readonly Brush AccentBorder = FreezeBrush("#2563EB");
        private static readonly Brush AccentFg = FreezeBrush("#1D4ED8");
        private static readonly Brush NormalBorder = FreezeBrush("#E2E8F0");
        private static readonly Brush NormalFg = FreezeBrush("#334155");
        private static readonly Brush HoverBg = FreezeBrush("#F8FAFC");
        private static readonly Brush PlaceholderBg = FreezeBrush("#F8FAFC");
        private static readonly Brush PlaceholderBorder = FreezeBrush("#CBD5E1");
        private static readonly Brush PlaceholderFg = FreezeBrush("#94A3B8");
        private static readonly Brush PlaceholderSelectedBg = FreezeBrush("#F1F5F9");
        private static readonly Brush PlaceholderSelectedBorder = FreezeBrush("#94A3B8");
        private static readonly Brush PlaceholderSelectedFg = FreezeBrush("#64748B");

        // Glass list filter chips: idle gray / selected white
        private static readonly Brush GlassIdleBg = FreezeBrush("#E5E7EB");
        private static readonly Brush GlassSelectedBg = FreezeBrush("#FFFFFF");
        private static readonly Brush GlassHoverBg = FreezeBrush("#D1D5DB");
        private static readonly Brush GlassFg = FreezeBrush("#334155");
        private static readonly Brush GlassMutedFg = FreezeBrush("#64748B");
        private static readonly Brush GlassTransparent = Brushes.Transparent;

        private static Brush FreezeBrush(string hex)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(hex);
            if (brush != null && brush.CanFreeze)
                brush.Freeze();
            return brush;
        }

        private INotifyCollectionChanged _itemsNotify;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(InlineOptionPicker),
                new PropertyMetadata(null, OnItemsChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(InlineOptionPicker),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedChanged));

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(InlineOptionPicker),
                new PropertyMetadata("선택"));

        public static readonly DependencyProperty EmptyItemDisplayProperty =
            DependencyProperty.Register("EmptyItemDisplay", typeof(string), typeof(InlineOptionPicker),
                new PropertyMetadata("미선택", OnSelectedChanged));

        // 호환용 (더 이상 Popup 미사용)
        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register("IsDropDownOpen", typeof(bool), typeof(InlineOptionPicker),
                new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty MaxDropDownHeightProperty =
            DependencyProperty.Register("MaxDropDownHeight", typeof(double), typeof(InlineOptionPicker),
                new PropertyMetadata(280.0));

        public static readonly DependencyProperty UsePopupChipsProperty =
            DependencyProperty.Register("UsePopupChips", typeof(bool), typeof(InlineOptionPicker),
                new PropertyMetadata(false, OnSelectedChanged));

        public InlineOptionPicker()
        {
            InitializeComponent();
            Loaded += (s, e) => RebuildOptions();
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

        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        public string EmptyItemDisplay
        {
            get { return (string)GetValue(EmptyItemDisplayProperty); }
            set { SetValue(EmptyItemDisplayProperty, value); }
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

        public bool UsePopupChips
        {
            get { return (bool)GetValue(UsePopupChipsProperty); }
            set { SetValue(UsePopupChipsProperty, value); }
        }

        public event EventHandler SelectionCommitted;

        private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (InlineOptionPicker)d;
            c.DetachItemsNotify(e.OldValue as INotifyCollectionChanged);
            c.AttachItemsNotify(e.NewValue as INotifyCollectionChanged);
            c.RebuildOptions();
        }

        private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InlineOptionPicker)d).RebuildOptions();
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

        private void RebuildOptions()
        {
            if (PART_Options == null)
                return;
            PART_Options.Children.Clear();
            if (ItemsSource == null)
                return;

            bool popup = UsePopupChips;
            bool glass = !popup && WorkLogUiOptions.UseGlassmorphism;

            foreach (var item in ItemsSource)
            {
                string label = FormatItem(item);
                bool selected = AreEqual(item, SelectedItem);
                bool isEmptyOption = item is string && string.IsNullOrEmpty((string)item);

                Brush fg;
                Brush bg;
                Brush borderBrush;
                Thickness borderThickness;
                CornerRadius radius;
                Thickness padding;
                double fontSize;
                double minHeight;

                if (popup)
                {
                    fg = GlassFg;
                    bg = selected ? GlassSelectedBg : GlassIdleBg;
                    borderBrush = GlassTransparent;
                    borderThickness = new Thickness(0);
                    radius = new CornerRadius(8);
                    padding = new Thickness(12, 6, 12, 6);
                    fontSize = 12.5;
                    minHeight = 28;
                }
                else if (glass)
                {
                    fg = isEmptyOption
                        ? (selected ? GlassMutedFg : PlaceholderFg)
                        : GlassFg;
                    bg = selected ? GlassSelectedBg : GlassIdleBg;
                    borderBrush = GlassTransparent;
                    borderThickness = new Thickness(0);
                    radius = new CornerRadius(6);
                    padding = new Thickness(10, 4, 10, 4);
                    fontSize = 11;
                    minHeight = 22;
                }
                else
                {
                    fg = isEmptyOption
                        ? (selected ? PlaceholderSelectedFg : PlaceholderFg)
                        : (selected ? AccentFg : NormalFg);
                    bg = isEmptyOption
                        ? (selected ? PlaceholderSelectedBg : PlaceholderBg)
                        : (selected ? AccentBg : Brushes.Transparent);
                    borderBrush = isEmptyOption
                        ? (selected ? PlaceholderSelectedBorder : PlaceholderBorder)
                        : (selected ? AccentBorder : NormalBorder);
                    borderThickness = new Thickness(1);
                    radius = new CornerRadius(14);
                    padding = new Thickness(10, 5, 10, 5);
                    fontSize = 12.5;
                    minHeight = 0;
                }

                var text = new TextBlock
                {
                    Text = label,
                    FontSize = fontSize,
                    FontWeight = selected || glass || popup ? FontWeights.SemiBold : FontWeights.Normal,
                    FontStyle = FontStyles.Normal,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                var content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    IsHitTestVisible = false
                };
                if (selected && !glass && !popup)
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
                    Background = bg,
                    BorderBrush = borderBrush,
                    BorderThickness = borderThickness,
                    CornerRadius = radius,
                    Padding = padding,
                    Margin = new Thickness(0, 0, 6, 6),
                    MinHeight = minHeight,
                    Cursor = Cursors.Hand,
                    Child = content,
                    Tag = item,
                    Opacity = isEmptyOption && !selected ? 0.85 : 1.0
                };

                bool isSelected = selected;
                bool empty = isEmptyOption;
                border.MouseEnter += (s, ev) =>
                {
                    if (isSelected)
                        return;
                    if (popup)
                        border.Background = GlassHoverBg;
                    else if (glass)
                        border.Background = GlassHoverBg;
                    else
                        border.Background = empty ? PlaceholderSelectedBg : HoverBg;
                };
                border.MouseLeave += (s, ev) =>
                {
                    if (popup)
                        border.Background = isSelected ? GlassSelectedBg : GlassIdleBg;
                    else if (glass)
                        border.Background = isSelected ? GlassSelectedBg : GlassIdleBg;
                    else if (empty)
                        border.Background = isSelected ? PlaceholderSelectedBg : PlaceholderBg;
                    else
                        border.Background = isSelected ? AccentBg : Brushes.Transparent;
                };
                border.MouseLeftButtonUp += (s, ev) =>
                {
                    SelectedItem = item is string ? (string)item : item;
                    RebuildOptions();
                    if (SelectionCommitted != null)
                        SelectionCommitted(this, EventArgs.Empty);
                    ev.Handled = true;
                };

                PART_Options.Children.Add(border);
            }
        }

        private string FormatItem(object item)
        {
            if (item == null)
                return Placeholder ?? string.Empty;
            if (item is string)
            {
                var s = (string)item;
                if (string.IsNullOrEmpty(s))
                    return EmptyItemDisplay ?? "미선택";
                return s;
            }
            return item.ToString();
        }

        private static bool AreEqual(object a, object b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;
            if (a is string || b is string)
                return string.Equals(Convert.ToString(a), Convert.ToString(b), StringComparison.Ordinal);
            return Equals(a, b);
        }
    }
}
