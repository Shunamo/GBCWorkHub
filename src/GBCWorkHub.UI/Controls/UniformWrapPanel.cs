using System;
using System.Windows;
using System.Windows.Controls;

namespace GBCWorkHub.UI.Controls
{
    /// <summary>
    /// Wraps like a WrapPanel, but every cell is the size of the widest/tallest child
    /// (or MinItemWidth / MinItemHeight). Long PC names widen the whole row evenly.
    /// </summary>
    public class UniformWrapPanel : Panel
    {
        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register(
                "MinItemWidth",
                typeof(double),
                typeof(UniformWrapPanel),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty MinItemHeightProperty =
            DependencyProperty.Register(
                "MinItemHeight",
                typeof(double),
                typeof(UniformWrapPanel),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinItemWidth
        {
            get { return (double)GetValue(MinItemWidthProperty); }
            set { SetValue(MinItemWidthProperty, value); }
        }

        public double MinItemHeight
        {
            get { return (double)GetValue(MinItemHeightProperty); }
            set { SetValue(MinItemHeightProperty, value); }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double itemW = Math.Max(0, MinItemWidth);
            double itemH = Math.Max(0, MinItemHeight);
            Size unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);

            foreach (UIElement child in InternalChildren)
            {
                if (child == null)
                    continue;
                child.Measure(unconstrained);
                itemW = Math.Max(itemW, child.DesiredSize.Width);
                itemH = Math.Max(itemH, child.DesiredSize.Height);
            }

            if (itemW <= 0)
                itemW = 1;
            if (itemH <= 0)
                itemH = 1;

            Size cell = new Size(itemW, itemH);
            foreach (UIElement child in InternalChildren)
            {
                if (child == null)
                    continue;
                child.Measure(cell);
            }

            double limit = availableSize.Width;
            if (double.IsInfinity(limit) || double.IsNaN(limit) || limit <= 0)
                limit = itemW * Math.Max(1, InternalChildren.Count);

            int columns = Math.Max(1, (int)(limit / itemW));
            int count = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child != null)
                    count++;
            }

            int rows = count == 0 ? 0 : (count + columns - 1) / columns;
            double usedWidth = count == 0 ? 0 : Math.Min(limit, itemW * Math.Min(columns, count));
            return new Size(usedWidth, itemH * rows);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double itemW = Math.Max(0, MinItemWidth);
            double itemH = Math.Max(0, MinItemHeight);

            foreach (UIElement child in InternalChildren)
            {
                if (child == null)
                    continue;
                itemW = Math.Max(itemW, child.DesiredSize.Width);
                itemH = Math.Max(itemH, child.DesiredSize.Height);
            }

            if (itemW <= 0)
                itemW = 1;
            if (itemH <= 0)
                itemH = 1;

            double limit = finalSize.Width;
            if (double.IsInfinity(limit) || double.IsNaN(limit) || limit <= 0)
                limit = itemW * Math.Max(1, InternalChildren.Count);

            double x = 0;
            double y = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child == null)
                    continue;
                if (x > 0 && x + itemW > limit + 0.5)
                {
                    x = 0;
                    y += itemH;
                }

                child.Arrange(new Rect(x, y, itemW, itemH));
                x += itemW;
            }

            int columns = Math.Max(1, (int)(limit / itemW));
            int count = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child != null)
                    count++;
            }

            int rows = count == 0 ? 0 : (count + columns - 1) / columns;
            return new Size(finalSize.Width, itemH * rows);
        }
    }
}
