using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GBCWorkHub.UI.ViewModels.Popup;

namespace GBCWorkHub.UI.Views.Popup
{
    public partial class PopupHostWindow : UserControl
    {
        private bool _syncingPassword;
        private static readonly MethodInfo PasswordBoxSelectMethod =
            typeof(PasswordBox).GetMethod(
                "Select",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public PopupHostWindow()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                SyncPasswordBoxes();
                UpdatePasswordPlaceholders();
            };
        }

        private void PromptCurrentPasswordBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // KeyDown에서 영문·숫자만 넣음. IME/한글 조합 문자는 막음.
            e.Handled = true;
        }

        private void PromptCurrentPasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandlePasswordKeyDown(PromptCurrentPasswordBox, e, text =>
            {
                var vm = DataContext as PopupHostViewModel;
                if (vm != null)
                    vm.CurrentPasswordText = text;
            });
        }

        private void PromptCurrentPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            OnPasswordChanged(PromptCurrentPasswordBox, text =>
            {
                var vm = DataContext as PopupHostViewModel;
                if (vm != null)
                    vm.CurrentPasswordText = text;
            });
        }

        private void PromptPasswordBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
        }

        private void PromptPasswordConfirmBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
        }

        private void PromptPasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandlePasswordKeyDown(PromptPasswordBox, e, text =>
            {
                var vm = DataContext as PopupHostViewModel;
                if (vm != null)
                    vm.PasswordText = text;
            });
        }

        private void PromptPasswordConfirmBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandlePasswordKeyDown(PromptPasswordConfirmBox, e, text =>
            {
                var vm = DataContext as PopupHostViewModel;
                if (vm != null)
                    vm.PasswordConfirmText = text;
            });
        }

        private void HandlePasswordKeyDown(PasswordBox box, KeyEventArgs e, System.Action<string> syncVm)
        {
            if (box == null || e == null)
                return;

            if (e.Key == Key.Tab || e.Key == Key.Enter || e.Key == Key.Escape)
                return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                || (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (e.Key == Key.V)
                {
                    e.Handled = true;
                    PasteAsciiPassword(box, syncVm);
                }
                return;
            }

            if (e.Key == Key.Back)
            {
                e.Handled = true;
                string cur = box.Password ?? string.Empty;
                if (cur.Length > 0)
                    SetPasswordKeepCaretEnd(box, cur.Substring(0, cur.Length - 1), syncVm);
                return;
            }

            if (e.Key == Key.Delete || e.Key == Key.Left || e.Key == Key.Right
                || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.Up || e.Key == Key.Down)
            {
                // PasswordBox caret APIs are limited; keep typing at end for stability.
                e.Handled = true;
                return;
            }

            char ch;
            if (!TryMapKeyToAscii(e, out ch))
            {
                if (e.Key != Key.LeftShift && e.Key != Key.RightShift
                    && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl
                    && e.Key != Key.LeftAlt && e.Key != Key.RightAlt
                    && e.Key != Key.LWin && e.Key != Key.RWin
                    && e.Key != Key.CapsLock && e.Key != Key.NumLock)
                    e.Handled = true;
                return;
            }

            e.Handled = true;
            SetPasswordKeepCaretEnd(box, (box.Password ?? string.Empty) + ch, syncVm);
        }

        private void PasteAsciiPassword(PasswordBox box, System.Action<string> syncVm)
        {
            if (box == null || !Clipboard.ContainsText())
                return;
            string clip;
            try
            {
                clip = Clipboard.GetText();
            }
            catch
            {
                return;
            }
            string filtered = FilterAsciiLetterOrDigit(clip);
            if (string.IsNullOrEmpty(filtered))
                return;
            SetPasswordKeepCaretEnd(box, (box.Password ?? string.Empty) + filtered, syncVm);
        }

        private void OnPasswordChanged(PasswordBox box, System.Action<string> syncVm)
        {
            if (_syncingPassword || box == null)
                return;

            string filtered = FilterAsciiLetterOrDigit(box.Password);
            if (filtered != box.Password)
            {
                SetPasswordKeepCaretEnd(box, filtered, syncVm);
                return;
            }

            if (syncVm != null)
                syncVm(box.Password);
            UpdatePasswordPlaceholder(box, "Placeholder");
        }

        private void SetPasswordKeepCaretEnd(PasswordBox box, string value, System.Action<string> syncVm)
        {
            if (box == null)
                return;
            string filtered = FilterAsciiLetterOrDigit(value ?? string.Empty);
            _syncingPassword = true;
            try
            {
                if (box.Password != filtered)
                    box.Password = filtered;
                // Password 재할당 후 커서가 맨 앞으로 가는 WPF 버릇 보정
                MovePasswordCaretToEnd(box);
            }
            finally
            {
                _syncingPassword = false;
            }
            if (syncVm != null)
                syncVm(box.Password);
            UpdatePasswordPlaceholder(box, "Placeholder");
        }

        private static void MovePasswordCaretToEnd(PasswordBox box)
        {
            if (box == null || PasswordBoxSelectMethod == null)
                return;
            try
            {
                int end = box.Password != null ? box.Password.Length : 0;
                PasswordBoxSelectMethod.Invoke(box, new object[] { end, 0 });
            }
            catch
            {
            }
        }

        private static bool TryMapKeyToAscii(KeyEventArgs e, out char ch)
        {
            ch = '\0';
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (key >= Key.A && key <= Key.Z)
            {
                int offset = key - Key.A;
                ch = (char)((shift ? 'A' : 'a') + offset);
                return true;
            }

            if (!shift && key >= Key.D0 && key <= Key.D9)
            {
                ch = (char)('0' + (key - Key.D0));
                return true;
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                ch = (char)('0' + (key - Key.NumPad0));
                return true;
            }

            return false;
        }

        private void PromptPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            OnPasswordChanged(PromptPasswordBox, text =>
            {
                var vm = DataContext as PopupHostViewModel;
                if (vm != null)
                    vm.PasswordText = text;
            });
        }

        private void PromptPasswordConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            OnPasswordChanged(PromptPasswordConfirmBox, text =>
            {
                var vm = DataContext as PopupHostViewModel;
                if (vm != null)
                    vm.PasswordConfirmText = text;
            });
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9');
        }

        private static string FilterAsciiLetterOrDigit(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (IsAsciiLetterOrDigit(value[i]))
                    sb.Append(value[i]);
            }
            return sb.ToString();
        }

        private void SyncPasswordBoxes()
        {
            var vm = DataContext as PopupHostViewModel;
            _syncingPassword = true;
            try
            {
                SyncOnePasswordBox(PromptPasswordBox, vm != null ? vm.PasswordText : null);
                SyncOnePasswordBox(PromptCurrentPasswordBox, vm != null ? vm.CurrentPasswordText : null);
                SyncOnePasswordBox(PromptPasswordConfirmBox, vm != null ? vm.PasswordConfirmText : null);
            }
            finally
            {
                _syncingPassword = false;
            }
            UpdatePasswordPlaceholders();
        }

        private static void SyncOnePasswordBox(PasswordBox box, string expectedRaw)
        {
            if (box == null)
                return;
            string expected = FilterAsciiLetterOrDigit(expectedRaw ?? string.Empty);
            if (box.Password == expected)
                return;
            box.Password = expected;
            MovePasswordCaretToEnd(box);
        }

        private void UpdatePasswordPlaceholders()
        {
            UpdatePasswordPlaceholder(PromptPasswordBox, "Placeholder");
            UpdatePasswordPlaceholder(PromptPasswordConfirmBox, "Placeholder");
            UpdatePasswordPlaceholder(PromptCurrentPasswordBox, "Placeholder");
        }

        private static void UpdatePasswordPlaceholder(PasswordBox box, string placeholderName)
        {
            if (box == null)
                return;
            var placeholder = FindDescendantByName(box, placeholderName) as TextBlock;
            if (placeholder == null)
                return;
            placeholder.Visibility = string.IsNullOrEmpty(box.Password)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static DependencyObject FindDescendantByName(DependencyObject root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var fe = child as FrameworkElement;
                if (fe != null && fe.Name == name)
                    return child;
                var nested = FindDescendantByName(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private void Host_Loaded(object sender, RoutedEventArgs e)
        {
            SyncPasswordBoxes();
            if (PromptPasswordBox != null)
                InputMethod.SetIsInputMethodEnabled(PromptPasswordBox, false);
            if (PromptPasswordConfirmBox != null)
                InputMethod.SetIsInputMethodEnabled(PromptPasswordConfirmBox, false);
            if (PromptCurrentPasswordBox != null)
                InputMethod.SetIsInputMethodEnabled(PromptCurrentPasswordBox, false);

            var vm = DataContext as PopupHostViewModel;
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                UpdatePasswordPlaceholders();
                if (vm != null && vm.ShowInput && PromptInputBox != null)
                {
                    PromptInputBox.Focus();
                    Keyboard.Focus(PromptInputBox);
                }
                else if (vm != null && vm.ShowCurrentPasswordInput && PromptCurrentPasswordBox != null)
                {
                    PromptCurrentPasswordBox.Focus();
                    Keyboard.Focus(PromptCurrentPasswordBox);
                    MovePasswordCaretToEnd(PromptCurrentPasswordBox);
                }
                else if (vm != null && vm.ShowPasswordInput && PromptPasswordBox != null)
                {
                    PromptPasswordBox.Focus();
                    Keyboard.Focus(PromptPasswordBox);
                    MovePasswordCaretToEnd(PromptPasswordBox);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
