using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GBCWorkHub.UI.ViewModels.Improvement;

namespace GBCWorkHub.UI.Views.Improvement
{
    /// <summary>
    /// 사이트 선택 / 유형·상태 필터 팝업 열림 상태는 이 View의 로컬 상태로 관리한다(DataContext인
    /// ImprovementListViewModel에 두지 않음) — 그 ViewModel은 메인 탭과 관리자 화면 양쪽에 동시에
    /// 임베드되어 재사용되는데, 팝업 열림 여부를 공유 ViewModel에 두면 한쪽에서 열 때 화면에 보이지
    /// 않는 다른 쪽 사본의 팝업도 같이 열려서 그 사본의 레이아웃 위치에 엉뚱하게 나타난다.
    /// </summary>
    public partial class ImprovementListView : UserControl
    {
        public static readonly DependencyProperty IsSitePickerOpenProperty =
            DependencyProperty.Register("IsSitePickerOpen", typeof(bool), typeof(ImprovementListView), new PropertyMetadata(false));

        public static readonly DependencyProperty IsFilterExpandedProperty =
            DependencyProperty.Register("IsFilterExpanded", typeof(bool), typeof(ImprovementListView), new PropertyMetadata(false));

        public static readonly DependencyProperty IsTypeMenuOpenProperty =
            DependencyProperty.Register("IsTypeMenuOpen", typeof(bool), typeof(ImprovementListView), new PropertyMetadata(false));

        public static readonly DependencyProperty IsStatusMenuOpenProperty =
            DependencyProperty.Register("IsStatusMenuOpen", typeof(bool), typeof(ImprovementListView), new PropertyMetadata(false));

        /// <summary>사이트/유형/상태 팝업 중 하나라도 열려 있는지 — 전체 화면 딤 처리에 사용.</summary>
        public static readonly DependencyProperty IsAnyPickerOpenProperty =
            DependencyProperty.Register("IsAnyPickerOpen", typeof(bool), typeof(ImprovementListView), new PropertyMetadata(false));

        public ImprovementListView()
        {
            InitializeComponent();
        }

        public bool IsSitePickerOpen
        {
            get { return (bool)GetValue(IsSitePickerOpenProperty); }
            set { SetValue(IsSitePickerOpenProperty, value); }
        }

        public bool IsFilterExpanded
        {
            get { return (bool)GetValue(IsFilterExpandedProperty); }
            set { SetValue(IsFilterExpandedProperty, value); }
        }

        public bool IsTypeMenuOpen
        {
            get { return (bool)GetValue(IsTypeMenuOpenProperty); }
            set { SetValue(IsTypeMenuOpenProperty, value); }
        }

        public bool IsStatusMenuOpen
        {
            get { return (bool)GetValue(IsStatusMenuOpenProperty); }
            set { SetValue(IsStatusMenuOpenProperty, value); }
        }

        public bool IsAnyPickerOpen
        {
            get { return (bool)GetValue(IsAnyPickerOpenProperty); }
            set { SetValue(IsAnyPickerOpenProperty, value); }
        }

        private void UpdateAnyPickerOpen()
        {
            IsAnyPickerOpen = IsSitePickerOpen || IsTypeMenuOpen || IsStatusMenuOpen;
        }

        private void SiteLabelButton_Click(object sender, RoutedEventArgs e)
        {
            IsTypeMenuOpen = false;
            IsStatusMenuOpen = false;
            IsSitePickerOpen = !IsSitePickerOpen;
            UpdateAnyPickerOpen();
        }

        private void SiteChip_Click(object sender, RoutedEventArgs e)
        {
            IsSitePickerOpen = false;
            UpdateAnyPickerOpen();
        }

        /// <summary>업무일지 ToggleFilterBar()와 동일: 필터 펼칠 때 사이트 팝업은 닫는다.</summary>
        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            IsSitePickerOpen = false;
            IsFilterExpanded = !IsFilterExpanded;
            UpdateAnyPickerOpen();
        }

        private void TypeFilterPill_Click(object sender, RoutedEventArgs e)
        {
            IsSitePickerOpen = false;
            IsStatusMenuOpen = false;
            IsTypeMenuOpen = !IsTypeMenuOpen;
            UpdateAnyPickerOpen();
        }

        private void StatusFilterPill_Click(object sender, RoutedEventArgs e)
        {
            IsSitePickerOpen = false;
            IsTypeMenuOpen = false;
            IsStatusMenuOpen = !IsStatusMenuOpen;
            UpdateAnyPickerOpen();
        }

        private void TypeChip_Click(object sender, RoutedEventArgs e)
        {
            IsTypeMenuOpen = false;
            UpdateAnyPickerOpen();
        }

        private void StatusChip_Click(object sender, RoutedEventArgs e)
        {
            IsStatusMenuOpen = false;
            UpdateAnyPickerOpen();
        }

        private void PickerScrim_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            IsSitePickerOpen = false;
            IsTypeMenuOpen = false;
            IsStatusMenuOpen = false;
            UpdateAnyPickerOpen();
        }

        /// <summary>
        /// 목록 행 클릭은 MouseBinding(InputBindings) 대신 이 이벤트로 직접 처리한다.
        /// MouseBinding은 CommandManager가 키보드 포커스를 기준으로 커맨드를 탐색하기 때문에,
        /// 화면 전환 직후처럼 트리 안에 포커스가 전혀 없는 상태에서는 첫 클릭이 포커스 확보 용도로만
        /// 소모되고 커맨드가 실행되지 않는다 — 다른 버튼을 한 번 눌러 포커스가 생긴 뒤에야 항목
        /// 클릭이 먹히는 것처럼 보였던 원인. MouseLeftButtonUp은 포커스와 무관하게 항상 발동한다.
        /// </summary>
        private void ListItemRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var row = sender as FrameworkElement;
            var item = row != null ? row.DataContext as ImprovementListItemViewModel : null;
            var vm = DataContext as ImprovementListViewModel;
            if (item == null || vm == null || vm.SelectItemCommand == null)
                return;
            if (vm.SelectItemCommand.CanExecute(item))
                vm.SelectItemCommand.Execute(item);
        }

        /// <summary>
        /// 업무일지 WorkLogListGlassView와 동일한 팝업 등장 애니메이션. 이걸 안 걸면 팝업 컨텐츠가
        /// XAML에 적어둔 초기값(Opacity=0.5, Scale=0.96)에서 멈춰 있어서 계속 흐릿하게 보인다 —
        /// 그림자·배경이 안 먹은 것처럼 보였던 원인이 바로 이것.
        /// </summary>
        private void Popup_Opened(object sender, EventArgs e)
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
