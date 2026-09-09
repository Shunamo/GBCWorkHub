using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GBCWorkHub.UI.Converters;
using GBCWorkHub.UI.ViewModels.Improvement;

namespace GBCWorkHub.UI.Views.Improvement
{
    /// <summary>
    /// "설명" 입력을 블로그 글쓰기처럼 만드는 캐럿 기반 에디터. RichTextBox의 FlowDocument가 편집 중
    /// 실제 진실의 소스이고, ViewModel.ContentBlocks는 로드 시(문서를 처음 구성할 때)와 저장 직전
    /// (문서를 다시 읽어들일 때)에만 동기화되는 스냅샷이다 — 타이핑 한 글자마다 동기화하지 않는다.
    /// </summary>
    public partial class ImprovementEditDialog : UserControl
    {
        public static readonly DependencyProperty IsPcPickerOpenProperty =
            DependencyProperty.Register("IsPcPickerOpen", typeof(bool), typeof(ImprovementEditDialog), new PropertyMetadata(false));

        private ImprovementEditDialogViewModel _vm;
        private readonly Dictionary<ImprovementContentBlockViewModel, BlockUIContainer> _imageContainers
            = new Dictionary<ImprovementContentBlockViewModel, BlockUIContainer>();

        public bool IsPcPickerOpen
        {
            get { return (bool)GetValue(IsPcPickerOpenProperty); }
            set { SetValue(IsPcPickerOpenProperty, value); }
        }

        public ImprovementEditDialog()
        {
            InitializeComponent();
            if (GBCWorkHub.UI.ViewModels.WorkLog.WorkLogUiOptions.UseGlassmorphism)
                ApplyGlassOverrides();

            DataContextChanged += OnDataContextChanged;
        }

        private void ApplyGlassOverrides()
        {
            var overrides = new ResourceDictionary
            {
                Source = new Uri(
                    "/GBCWorkHub.UI;component/Assets/WorkLogEditGlassOverrides.xaml",
                    UriKind.Relative)
            };

            Resources.MergedDictionaries.Add(overrides);

            var keys = new object[overrides.Count];
            overrides.Keys.CopyTo(keys, 0);
            foreach (var key in keys)
                Resources[key] = overrides[key];
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.ContentReloaded -= OnContentReloaded;

            _vm = e.NewValue as ImprovementEditDialogViewModel;
            if (_vm != null)
            {
                _vm.ContentReloaded += OnContentReloaded;
                RebuildDocumentFromViewModel();
            }
        }

        private void OnContentReloaded()
        {
            RebuildDocumentFromViewModel();
        }

        /// <summary>ViewModel.ContentBlocks를 기준으로 FlowDocument를 처음부터 다시 구성한다.</summary>
        private void RebuildDocumentFromViewModel()
        {
            if (_vm == null)
                return;

            _imageContainers.Clear();
            var document = new FlowDocument { PagePadding = new Thickness(0) };
            bool editable = _vm.IsNewRecord;

            foreach (var block in _vm.ContentBlocks)
            {
                if (block.IsText)
                    document.Blocks.Add(new Paragraph(new Run(block.Text ?? string.Empty)));
                else
                    document.Blocks.Add(BuildImageBlock(block, editable));
            }
            if (document.Blocks.Count == 0)
                document.Blocks.Add(new Paragraph());

            ContentRichTextBox.Document = document;
        }

        private BlockUIContainer BuildImageBlock(ImprovementContentBlockViewModel block, bool editable)
        {
            var converter = new ByteArrayToImageSourceConverter();
            var source = converter.Convert(block.ImageData, typeof(ImageSource), null, CultureInfo.InvariantCulture) as ImageSource;

            var image = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                MaxWidth = 520,
                MaxHeight = 360,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.Children.Add(image);

            if (editable)
            {
                var removeButton = new Button
                {
                    Content = "✕",
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Tag = block
                };
                removeButton.Click += RemoveImageButton_Click;
                grid.Children.Add(removeButton);
            }

            var container = new BlockUIContainer(grid);
            _imageContainers[block] = container;
            return container;
        }

        private void RemoveImageButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var block = button != null ? button.Tag as ImprovementContentBlockViewModel : null;
            if (block == null)
                return;

            BlockUIContainer container;
            if (_imageContainers.TryGetValue(block, out container) && ContentRichTextBox.Document != null)
                ContentRichTextBox.Document.Blocks.Remove(container);

            _imageContainers.Remove(block);
        }

        /// <summary>"+ 이미지 삽입" — 캐럿이 있던 문단을 캐럿 지점에서 앞/뒤로 나누고 그 사이에 이미지를 끼운 뒤,
        /// 캐럿을 이미지 다음 문단으로 옮겨 바로 이어서 타이핑할 수 있게 한다. 여러 장을 고르면 순서대로 반복한다.</summary>
        private void InsertImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null)
                return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "이미지 파일 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                Multiselect = true
            };
            bool? picked = dialog.ShowDialog();
            if (picked != true || dialog.FileNames == null || dialog.FileNames.Length == 0)
                return;

            foreach (var path in dialog.FileNames)
            {
                int currentImageCount = _imageContainers.Count;
                var block = _vm.TryStageImageBlock(path, currentImageCount);
                if (block == null)
                    continue;

                InsertImageBlockAtCaret(block);
            }
        }

        private void InsertImageBlockAtCaret(ImprovementContentBlockViewModel block)
        {
            var document = ContentRichTextBox.Document;
            var caret = ContentRichTextBox.CaretPosition;
            var currentParagraph = caret != null ? caret.Paragraph : null;
            var imageContainer = BuildImageBlock(block, editable: true);

            Paragraph trailingParagraph;
            if (currentParagraph == null)
            {
                document.Blocks.Add(imageContainer);
                trailingParagraph = new Paragraph();
                document.Blocks.Add(trailingParagraph);
            }
            else
            {
                var afterRange = new TextRange(caret, currentParagraph.ContentEnd);
                string afterText = afterRange.Text;
                afterRange.Text = string.Empty;

                document.Blocks.InsertAfter(currentParagraph, imageContainer);
                trailingParagraph = new Paragraph(new Run(afterText));
                document.Blocks.InsertAfter(imageContainer, trailingParagraph);
            }

            ContentRichTextBox.CaretPosition = trailingParagraph.ContentStart;
            ContentRichTextBox.Focus();
        }

        /// <summary>등록 직전(SaveCommand 실행 전) FlowDocument를 순서대로 읽어 ViewModel에 반영한다.</summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || !_vm.IsNewRecord)
                return;

            var blocks = new List<ImprovementContentBlockViewModel>();
            foreach (Block b in ContentRichTextBox.Document.Blocks)
            {
                var paragraph = b as Paragraph;
                if (paragraph != null)
                {
                    string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                    blocks.Add(new ImprovementContentBlockViewModel(text));
                    continue;
                }

                var container = b as BlockUIContainer;
                if (container != null)
                {
                    ImprovementContentBlockViewModel matched = null;
                    foreach (var pair in _imageContainers)
                    {
                        if (ReferenceEquals(pair.Value, container))
                        {
                            matched = pair.Key;
                            break;
                        }
                    }
                    if (matched != null)
                        blocks.Add(matched);
                }
            }

            _vm.SetContentBlocks(blocks);
        }

        private void PcPickerButton_Click(object sender, RoutedEventArgs e)
        {
            IsPcPickerOpen = !IsPcPickerOpen;
        }

        private void PcChip_Click(object sender, RoutedEventArgs e)
        {
            IsPcPickerOpen = false;
        }

        private void PcPickerScrim_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            IsPcPickerOpen = false;
        }

        /// <summary>일반 채팅처럼 Enter는 등록, Shift+Enter는 줄바꿈.</summary>
        private void CommentInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                return;

            e.Handled = true;
            if (_vm != null && _vm.AddCommentCommand != null && _vm.AddCommentCommand.CanExecute(null))
                _vm.AddCommentCommand.Execute(null);
        }

        /// <summary>목록 화면의 팝업 등장 애니메이션과 동일 — 안 걸면 팝업이 흐릿/작게 멈춰 보인다.</summary>
        private void PcPickerPopup_Opened(object sender, EventArgs e)
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
