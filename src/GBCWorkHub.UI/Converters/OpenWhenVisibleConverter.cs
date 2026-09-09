using System;
using System.Globalization;
using System.Windows.Data;

namespace GBCWorkHub.UI.Converters
{
    /// <summary>
    /// values[0]: 팝업을 열어야 하는지(ViewModel의 IsXxxOpen). values[1]: 이 뷰 인스턴스가 실제로
    /// 화면에 보이는지(UIElement.IsVisible). 같은 ViewModel을 동시에 여러 화면(메인 탭 + 관리자 화면 등)에
    /// 임베드해서 재사용하는 경우, IsXxxOpen 하나만 보고 팝업을 열면 화면에 안 보이는 다른 사본의 팝업까지
    /// 같이 열려서 그 사본의 레이아웃 위치에 엉뚱하게 나타난다 — 그래서 "보일 때만" 조건을 더한다.
    /// 팝업이 스스로 닫힐 때(바깥쪽 클릭 등)는 ConvertBack에서 IsXxxOpen 쪽에만 false를 반영한다.
    /// </summary>
    public sealed class OpenWhenVisibleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;
            bool wantsOpen = values[0] is bool && (bool)values[0];
            bool isVisible = values[1] is bool && (bool)values[1];
            return wantsOpen && isVisible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { value, Binding.DoNothing };
        }
    }
}
