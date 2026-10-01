using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GBCWorkHub.UI.Converters
{
    /// <summary>문자열 리소스 키(예: "ExcelBrandIcon")를 App 리소스의 실제 ImageSource로 바꾼다
    /// — 데이터(바인딩된 모델)가 어떤 아이콘을 쓸지만 알고, XAML이 실제 그림을 아는 구조를 위함.</summary>
    public sealed class ResourceKeyToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = value as string;
            if (string.IsNullOrWhiteSpace(key))
                return null;
            return Application.Current.TryFindResource(key) as ImageSource;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
