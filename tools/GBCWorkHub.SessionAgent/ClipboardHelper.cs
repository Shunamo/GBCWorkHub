using System;
using System.Threading;
using System.Windows.Forms;

namespace GBCWorkHub.SessionAgent
{
    /// <summary>
    /// RDP 클립보드 = 제어 채널 + 사용자 복붙 공유.
    /// 짧은 수명 에이전트이므로 hold+복원은 반드시 동기(STA)로 수행한다.
    /// </summary>
    internal static class ClipboardHelper
    {
        private const int DefaultHoldMs = 1500;
        private const int TfsPayloadHoldMs = 4000;

        private static string _userClipboardBackup;

        public static bool IsProtocolText(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.StartsWith("GBCWORKHUB", StringComparison.Ordinal);
        }

        public static string TryReadClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryCopyToClipboard(string text, int? holdMs = null)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            CaptureUserBackup();

            bool written = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    written = true;
                    break;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            if (!written)
                return false;

            bool isLargeStructuredPayload =
                text.StartsWith("GBCWORKHUB_TFS::", StringComparison.Ordinal)
                || text.StartsWith("GBCWORKHUB_SESSION_RESULT::", StringComparison.Ordinal);
            int hold = holdMs ?? (isLargeStructuredPayload ? TfsPayloadHoldMs : DefaultHoldMs);

            Thread.Sleep(hold);
            TryRestoreUserClipboard();
            return true;
        }

        public static bool TryRestoreUserClipboard()
        {
            string backup = _userClipboardBackup;
            // 백업이 없으면 GBCWORKHUB* 가 클립보드에 고착됨 → 로컬 복사가 와도
            // 붙여넣기 시 프로토콜 문구만 나오는 증상. 프로토콜이면 Clear.
            if (string.IsNullOrEmpty(backup))
                return TryClearProtocolClipboard();

            try
            {
                string current = null;
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                }

                if (!string.IsNullOrEmpty(current)
                    && !IsProtocolText(current)
                    && !string.Equals(current, backup, StringComparison.Ordinal))
                    return false;

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(backup);
                        return true;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }

                // SetText 실패 시에도 프로토콜 고착 방지
                return TryClearProtocolClipboard();
            }
            catch
            {
            }

            return false;
        }

        private static bool TryClearProtocolClipboard()
        {
            try
            {
                string current = null;
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                }

                if (string.IsNullOrEmpty(current) || !IsProtocolText(current))
                    return false;

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Clipboard.Clear();
                        return true;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void CaptureUserBackup()
        {
            try
            {
                if (!Clipboard.ContainsText())
                    return;
                string current = Clipboard.GetText();
                if (string.IsNullOrEmpty(current) || IsProtocolText(current))
                    return;
                _userClipboardBackup = current;
            }
            catch
            {
            }
        }
    }
}
