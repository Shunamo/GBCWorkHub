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
            DataObject.AddPastingHandler(ContentRichTextBox, OnContentPasting);
            ContentRichTextBox.PreviewKeyDown += ContentRichTextBox_PreviewKeyDown;
        }

        /// <summary>
        /// 이미지(BlockUIContainer)를 사이에 두고 위/아래로 방향키 이동하면 RichTextBox가 캐럿을
        /// 이미지 내부의 애매한 위치(타이핑도 안 되고, 시각적으로도 엉뚱한 곳)에 놓는 경우가 있다.
        /// 문단 경계(맨 끝/맨 앞)에서 이미지를 향해 이동하려는 순간만 가로채서, 이미지 반대편에 있는
        /// 실제 문단으로 캐럿을 직접 옮겨준다 — 문단 내부 이동은 건드리지 않는다.
        /// </summary>
        private void ContentRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_vm == null || _vm.IsContentReadOnly)
                return;

            var caret = ContentRichTextBox.CaretPosition;
            var paragraph = caret != null ? caret.Paragraph : null;
            if (paragraph == null)
                return;

            // Left/Right는 문자 단위 이동이라 "문단의 맨 끝/맨 앞 문자냐"만 보면 정확하다.
            if (e.Key == Key.Right && caret.CompareTo(paragraph.ContentEnd) == 0)
                e.Handled = JumpToParagraphAfterImage(paragraph.NextBlock as BlockUIContainer);
            else if (e.Key == Key.Left && caret.CompareTo(paragraph.ContentStart) == 0)
                e.Handled = JumpToParagraphBeforeImage(paragraph.PreviousBlock as BlockUIContainer);
            // Up/Down은 "문단의 마지막 줄이냐"를 봐야 한다 — 문단이 여러 줄로 줄바꿈됐을 때
            // 캐럿이 문단 끝 문자가 아니라 마지막 줄의 중간에 있어도 이미지로 내려가려는 시도이기
            // 때문에, 정확한 문자 위치가 아니라 같은 줄인지(화면 Y좌표)를 비교해서 판단한다.
            // TextPointer.GetLineStartPosition류 API는 이미지 경계 근처에서 이미 신뢰할 수 없다는
            // 게 이 버그의 원인이라, 여기서는 그 API를 쓰지 않고 순수 좌표 비교로만 판단한다.
            else if (e.Key == Key.Down && IsOnSameLine(caret, paragraph.ContentEnd))
                e.Handled = JumpToParagraphAfterImage(paragraph.NextBlock as BlockUIContainer);
            else if (e.Key == Key.Up && IsOnSameLine(caret, paragraph.ContentStart))
                e.Handled = JumpToParagraphBeforeImage(paragraph.PreviousBlock as BlockUIContainer);
        }

        private static bool IsOnSameLine(TextPointer a, TextPointer b)
        {
            double topA = a.GetCharacterRect(LogicalDirection.Forward).Top;
            double topB = b.GetCharacterRect(LogicalDirection.Backward).Top;
            return Math.Abs(topA - topB) < 0.5;
        }

        private bool JumpToParagraphAfterImage(BlockUIContainer imageBlock)
        {
            var afterImage = imageBlock != null ? imageBlock.NextBlock as Paragraph : null;
            if (afterImage == null)
                return false;
            ContentRichTextBox.CaretPosition = afterImage.ContentStart;
            return true;
        }

        private bool JumpToParagraphBeforeImage(BlockUIContainer imageBlock)
        {
            var beforeImage = imageBlock != null ? imageBlock.PreviousBlock as Paragraph : null;
            if (beforeImage == null)
                return false;
            ContentRichTextBox.CaretPosition = beforeImage.ContentEnd;
            return true;
        }

        /// <summary>이미지(사진 자체 또는 그 배경 프레임)를 마우스로 클릭했을 때도 캐럿이 그 안에
        /// 머물지 않도록, 클릭을 가로채서 이미지 바로 다음(없으면 이전) 문단으로 캐럿을 옮긴다.</summary>
        private void RedirectCaretAwayFromImage(BlockUIContainer container)
        {
            var target = (container.NextBlock as Paragraph) ?? (container.PreviousBlock as Paragraph);
            if (target != null)
                ContentRichTextBox.CaretPosition = target.ContentStart;
            ContentRichTextBox.Focus();
        }

        private void ApplyGlassOverrides()
        {
            var overrides = new ResourceDictionary
            {
                Source = new Uri(
                    "/GBCWorkHub;component/Assets/WorkLogEditGlassOverrides.xaml",
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
            bool editable = _vm.IsContentEditable;

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

            // 사진에 테두리를 둘러서 "사진 블록"의 경계를 눈에 보이게 하고, 위아래 여백을 넉넉히
            // 줘서 그 위/아래에 글을 쓸 수 있는 빈 줄이 실제로 존재한다는 걸 시각적으로 알 수 있게
            // 한다 — 안 그러면 사진과 그 앞뒤 빈 문단이 시각적으로 구분이 안 돼서 어디를 클릭해야
            // 위/아래에 타이핑되는지 애매해진다.
            var imageFrame = new Border
            {
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = image
            };

            // 클릭 시점에는 아직 container가 없으므로, 클로저로 나중에 채워질 변수를 참조하게 해두고
            // 실제 대입은 이 메서드 끝에서 한다 — 핸들러는 사용자가 클릭할 때(그 이후 시점)에만 실행되므로 안전하다.
            BlockUIContainer container = null;
            imageFrame.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (container != null)
                    RedirectCaretAwayFromImage(container);
            };

            // HorizontalAlignment.Left — 그리드가 텍스트 칸 전체 너비로 늘어나면 우측 정렬한
            // 삭제 버튼이 사진이 아니라 문서 오른쪽 끝에 붙어버린다. 사진 실제 너비만큼만 차지하게
            // 해야 버튼이 사진 우측 상단 모서리에 정확히 겹쳐 보인다.
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(imageFrame);

            if (editable)
            {
                var removeButton = new Button
                {
                    Content = new Image
                    {
                        Source = TryFindResource("ImageDeleteSquareIcon") as ImageSource,
                        Width = 20,
                        Height = 20
                    },
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 6, 0),
                    Tag = block,
                    // 이 버튼이 포커스를 받을 수 있으면 방향키로 커서를 위/아래로 옮길 때 텍스트 줄이
                    // 아니라 이 버튼에서 멈춰버린다 — 마우스 클릭 삭제는 Focusable과 무관하게 동작하므로
                    // 키보드 탐색 대상에서만 빼서 텍스트 이동이 이미지를 그냥 건너뛰게 한다.
                    Focusable = false,
                    IsTabStop = false
                };
                removeButton.Click += RemoveImageButton_Click;
                grid.Children.Add(removeButton);
            }

            container = new BlockUIContainer(grid);
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
                byte[] bytes;
                string fileName;
                try
                {
                    bytes = FitImageFileToAttachmentLimit(path, out fileName);
                }
                catch (Exception)
                {
                    bytes = null;
                    fileName = System.IO.Path.GetFileName(path);
                }
                if (bytes == null)
                    continue;

                var block = _vm.TryStagePastedImageBlock(bytes, fileName, currentImageCount);
                if (block == null)
                    continue;

                InsertImageBlockAtCaret(block);
            }
        }

        /// <summary>
        /// 파일로 고른 이미지도 붙여넣기와 동일하게 처리한다 — 이미 용량 제한 안쪽이면 원본 그대로
        /// 쓰고(불필요한 재인코딩으로 화질을 낮추지 않도록), 넘으면 붙여넣기와 같은 압축/축소 경로를 탄다.
        /// </summary>
        private static byte[] FitImageFileToAttachmentLimit(string path, out string fileName)
        {
            const long maxBytes = 5 * 1024 * 1024 - 64 * 1024;
            fileName = System.IO.Path.GetFileName(path);
            byte[] raw = System.IO.File.ReadAllBytes(path);
            if (raw.Length <= maxBytes)
                return raw;

            var decoded = new System.Windows.Media.Imaging.BitmapImage();
            decoded.BeginInit();
            decoded.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            decoded.UriSource = new Uri(path);
            decoded.EndInit();
            decoded.Freeze();

            fileName = System.IO.Path.GetFileNameWithoutExtension(path) + ".jpg";
            return EncodeImageForAttachment(decoded);
        }

        /// <summary>
        /// 클립보드에 있는 이미지(스크린샷 등)를 붙여넣을 때 가로챈다. RichTextBox의 기본 붙여넣기는
        /// 원본 비트맵을 그대로 문서에 꽂아 넣어 우리 첨부 추적(_imageContainers)에 잡히지 않고,
        /// 용량 제한(5MB)도 거치지 않아 저장 시 통째로 누락되거나 너무 커서 실패한다 — 그래서
        /// 여기서 직접 JPEG로 인코딩(필요하면 해상도까지 축소)해 파일 첨부와 동일한 경로로 태운다.
        /// </summary>
        private void OnContentPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (_vm == null || !_vm.IsContentEditable)
                return;
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Bitmap))
                return;

            var bitmapSource = e.SourceDataObject.GetData(DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource;
            if (bitmapSource == null)
                return;

            e.CancelCommand();

            byte[] encoded = EncodeImageForAttachment(bitmapSource);
            int currentImageCount = _imageContainers.Count;
            var block = _vm.TryStagePastedImageBlock(encoded, "clipboard.jpg", currentImageCount);
            if (block != null)
                InsertImageBlockAtCaret(block);
        }

        /// <summary>클립보드 비트맵을 JPEG로 인코딩하고, 화질을 낮춰도 5MB를 넘으면 해상도까지 줄여 재시도한다.</summary>
        private static byte[] EncodeImageForAttachment(System.Windows.Media.Imaging.BitmapSource source)
        {
            const long maxBytes = 5 * 1024 * 1024 - 64 * 1024;
            const int maxDimension = 2000;

            var working = DownscaleIfLarger(source, maxDimension);

            for (int quality = 90; quality >= 40; quality -= 12)
            {
                byte[] data = EncodeJpeg(working, quality);
                if (data.Length <= maxBytes)
                    return data;
            }

            byte[] last = EncodeJpeg(working, 40);
            for (int attempt = 0; attempt < 4 && last.Length > maxBytes; attempt++)
            {
                int nextDimension = (int)(Math.Max(working.PixelWidth, working.PixelHeight) * 0.7);
                working = DownscaleIfLarger(working, nextDimension);
                last = EncodeJpeg(working, 60);
            }

            return last;
        }

        private static System.Windows.Media.Imaging.BitmapSource DownscaleIfLarger(
            System.Windows.Media.Imaging.BitmapSource source, int maxDimension)
        {
            int longest = Math.Max(source.PixelWidth, source.PixelHeight);
            if (longest <= maxDimension)
                return source;

            double scale = (double)maxDimension / longest;
            var transformed = new System.Windows.Media.Imaging.TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();
            return transformed;
        }

        private static byte[] EncodeJpeg(System.Windows.Media.Imaging.BitmapSource source, int quality)
        {
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using (var ms = new System.IO.MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
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
            if (_vm == null || !(_vm.IsNewRecord || _vm.IsEditingContent))
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
