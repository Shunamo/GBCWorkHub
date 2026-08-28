using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GBCWorkHub.UI.Views.WorkLog
{
    public partial class WorkLogListGlassView : UserControl
    {
        public WorkLogListGlassView()
        {
            InitializeComponent();
        }

        private void SitePickerPopup_Opened(object sender, EventArgs e)
        {
            PlayPopIn(SiteBubbleHost);
        }

        private void FilterMenuPopup_Opened(object sender, EventArgs e)
        {
            var popup = sender as System.Windows.Controls.Primitives.Popup;
            if (popup == null)
                return;
            PlayPopIn(popup.Child as FrameworkElement);
        }

        private static void PlayPopIn(FrameworkElement host)
        {
            if (host == null)
                return;

            var scale = host.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(0.96, 0.96);
                host.RenderTransform = scale;
                host.RenderTransformOrigin = new Point(0, 0);
            }

            host.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            // Start near-final so open feels instant; short ease only.
            host.Opacity = 0.5;
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            host.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(70)));

            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
        }
    }
}
