using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GBCWorkHub.UI.Controls
{
    public partial class WheelPicker : UserControl
    {
        private const int VisibleSlots = 3;
        private const double ItemHeight = 24;

        private bool _dragging;
        private bool _dragMoved;
        private double _dragOriginY;
        private int _dragOriginValue;
        private bool _suppress;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                "Value",
                typeof(int),
                typeof(WheelPicker),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                "Minimum",
                typeof(int),
                typeof(WheelPicker),
                new PropertyMetadata(0, OnVisualChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                "Maximum",
                typeof(int),
                typeof(WheelPicker),
                new PropertyMetadata(23, OnVisualChanged));

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(
                "Step",
                typeof(int),
                typeof(WheelPicker),
                new PropertyMetadata(1, OnVisualChanged));

        public WheelPicker()
        {
            InitializeComponent();
            Loaded += (s, e) => Rebuild();
        }

        public int Value
        {
            get { return (int)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public int Minimum
        {
            get { return (int)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public int Maximum
        {
            get { return (int)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public int Step
        {
            get { return (int)GetValue(StepProperty); }
            set { SetValue(StepProperty, value); }
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = d as WheelPicker;
            if (picker == null || picker._suppress)
                return;
            picker.NormalizeValue();
            picker.Rebuild();
        }

        private void NormalizeValue()
        {
            int step = Step <= 0 ? 1 : Step;
            int min = Minimum;
            int max = Maximum;
            if (max < min)
                max = min;
            int value = Value;
            if (value < min)
                value = min;
            if (value > max)
                value = max;
            int offset = value - min;
            value = min + (int)Math.Round(offset / (double)step) * step;
            if (value > max)
                value = max - ((max - min) % step);
            if (value < min)
                value = min;
            if (value != Value)
            {
                _suppress = true;
                Value = value;
                _suppress = false;
            }
        }

        private void Rebuild()
        {
            if (ItemHost == null)
                return;
            int step = Step <= 0 ? 1 : Step;
            int center = Value;
            var rows = new ObservableCollection<WheelRow>();
            int half = VisibleSlots / 2;
            for (int i = -half; i <= half; i++)
            {
                int raw = center + (i * step);
                bool inRange = raw >= Minimum && raw <= Maximum;
                double opacity = 1.0;
                FontWeight weight = FontWeights.Normal;
                double fontSize = 13;
                if (i == 0)
                {
                    opacity = 1.0;
                    weight = FontWeights.SemiBold;
                    fontSize = 15;
                }
                else if (Math.Abs(i) == 1)
                {
                    opacity = 0.42;
                    fontSize = 13;
                }
                else
                {
                    opacity = 0.18;
                    fontSize = 12;
                }

                rows.Add(new WheelRow
                {
                    Value = raw,
                    Text = inRange ? raw.ToString("00") : string.Empty,
                    Opacity = inRange ? opacity : 0,
                    Weight = weight,
                    FontSize = fontSize
                });
            }
            ItemHost.ItemsSource = rows;
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            int dir = e.Delta > 0 ? -1 : 1;
            Nudge(dir);
            e.Handled = true;
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragMoved = false;
            _dragOriginY = e.GetPosition(this).Y;
            _dragOriginValue = Value;
            CaptureMouse();
            Focus();
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;
            double dy = e.GetPosition(this).Y - _dragOriginY;
            if (Math.Abs(dy) >= 4)
                _dragMoved = true;
            int steps = (int)Math.Round(dy / ItemHeight);
            SetFromIndexOffset(_dragOriginValue, steps);
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;
            EndDrag();
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_dragging)
                EndDrag();
        }

        private void EndDrag()
        {
            _dragging = false;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            NormalizeValue();
            Rebuild();
        }

        private void OnItemClick(object sender, RoutedEventArgs e)
        {
            if (_dragMoved)
                return;
            var button = sender as Button;
            if (button == null || !(button.Tag is int))
                return;
            int next = (int)button.Tag;
            if (next < Minimum || next > Maximum)
                return;
            Value = next;
        }

        private void Nudge(int direction)
        {
            int step = Step <= 0 ? 1 : Step;
            int next = Value + (direction * step);
            if (next < Minimum)
                next = Minimum;
            if (next > Maximum)
                next = Maximum;
            Value = next;
        }

        private void SetFromIndexOffset(int origin, int steps)
        {
            int step = Step <= 0 ? 1 : Step;
            int next = origin + (steps * step);
            if (next < Minimum)
                next = Minimum;
            if (next > Maximum)
                next = Maximum;
            if (next == Value)
                return;
            _suppress = true;
            Value = next;
            _suppress = false;
            Rebuild();
        }

        private sealed class WheelRow
        {
            public int Value { get; set; }
            public string Text { get; set; }
            public double Opacity { get; set; }
            public FontWeight Weight { get; set; }
            public double FontSize { get; set; }
        }
    }
}
