using System;
using System.Globalization;
using System.Windows.Data;

namespace GBCWorkHub.UI.Converters
{
    /// <summary>MultiBinding 값 두 개가 같은 문자열인지 비교.</summary>
    public sealed class StringEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;

            string a = values[0] == null ? string.Empty : values[0].ToString();
            string b = values[1] == null ? string.Empty : values[1].ToString();
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
