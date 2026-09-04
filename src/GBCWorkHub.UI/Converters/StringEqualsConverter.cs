using System;
using System.Globalization;
using System.Windows.Data;

namespace GBCWorkHub.UI.Converters
{
    /// <summary>값이 ConverterParameter와 같은 문자열이면 true.</summary>
    public sealed class StringEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string a = value == null ? string.Empty : value.ToString();
            string b = parameter == null ? string.Empty : parameter.ToString();
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool && (bool)value && parameter != null)
                return parameter.ToString();
            return Binding.DoNothing;
        }
    }
}
