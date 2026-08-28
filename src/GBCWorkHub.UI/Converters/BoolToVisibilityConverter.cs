using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GBCWorkHub.UI.Converters
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            bool invert = parameter != null && string.Equals(parameter.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
            if (invert)
                flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
