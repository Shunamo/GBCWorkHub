using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GBCWorkHub.UI.Converters
{
    /// <summary>문자열이 비어 있으면 Collapsed, 값이 있으면 Visible — 인라인 에러 메시지 표시용.</summary>
    public sealed class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            return string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
