using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GBCWorkHub.UI.Converters
{
    /// <summary>
    /// ActualWidth가 ConverterParameter(최소 너비) 이상이면 Visible, 아니면 Collapsed.
    /// 예: parameter="1100"
    /// </summary>
    public sealed class MinWidthToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double width = 0;
            if (value is double)
                width = (double)value;
            else if (value != null)
                double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out width);

            double min = 1100;
            if (parameter != null)
                double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out min);

            return width >= min ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
