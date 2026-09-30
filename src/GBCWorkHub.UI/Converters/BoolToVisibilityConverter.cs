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
            string param = parameter != null ? parameter.ToString() : string.Empty;
            bool invert = param.IndexOf("Invert", StringComparison.OrdinalIgnoreCase) >= 0;
            // "Hidden": false 값은 Collapsed(자리 안 차지) 대신 Hidden(자리는 차지, 안 보임)으로 —
            // 편집 모드 토글처럼 버튼이 나타났다 사라졌다 할 때 옆 레이아웃이 출렁이지 않게.
            bool useHidden = param.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0;
            if (invert)
                flag = !flag;
            if (flag)
                return Visibility.Visible;
            return useHidden ? Visibility.Hidden : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
