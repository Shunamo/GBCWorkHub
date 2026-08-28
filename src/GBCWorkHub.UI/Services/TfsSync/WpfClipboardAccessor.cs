using System;
using System.Windows;

namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>
    /// System.Windows.Clipboard 실제 구현. UI 스레드가 아니면 Dispatcher로 넘긴다.
    /// </summary>
    public sealed class WpfClipboardAccessor : IClipboardAccessor
    {
        public bool TryGetText(out string text)
        {
            string captured = null;
            bool found = false;
            try
            {
                InvokeOnUi(() =>
                {
                    if (Clipboard.ContainsText())
                    {
                        captured = Clipboard.GetText();
                        found = true;
                    }
                });
            }
            catch
            {
                found = false;
                captured = null;
            }

            text = captured;
            return found;
        }

        public void SetText(string text)
        {
            InvokeOnUi(() => Clipboard.SetText(text));
        }

        public void Clear()
        {
            InvokeOnUi(() => Clipboard.Clear());
        }

        private static void InvokeOnUi(Action action)
        {
            var app = Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(action);
            else
                action();
        }
    }
}
