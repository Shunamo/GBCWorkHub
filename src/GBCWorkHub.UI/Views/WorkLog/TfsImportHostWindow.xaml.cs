using System.Windows;
using System.Windows.Media;

namespace GBCWorkHub.UI.Views.WorkLog
{
    public partial class TfsImportHostWindow : Window
    {
        public TfsImportHostWindow()
        {
            InitializeComponent();
        }

        private void CardBorder_SizeChanged(object sender, RoutedEventArgs e)
        {
            if (CardBorder == null || CardBorder.ActualWidth <= 0 || CardBorder.ActualHeight <= 0)
                return;

            double r = CardBorder.CornerRadius.TopLeft;
            CardBorder.Clip = new RectangleGeometry(
                new Rect(0, 0, CardBorder.ActualWidth, CardBorder.ActualHeight),
                r, r);
        }
    }
}
