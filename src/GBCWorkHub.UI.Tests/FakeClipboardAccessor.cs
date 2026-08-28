using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.Tests
{
    /// <summary>
    /// In-memory 클립보드 fake. 실제 System.Windows.Clipboard를 건드리지 않고
    /// ClipboardProtocolLeaseManager의 상태 전이를 결정론적으로 검증하기 위한 것.
    /// </summary>
    internal sealed class FakeClipboardAccessor : IClipboardAccessor
    {
        public string Text;
        public int SetTextCallCount;
        public int ClearCallCount;

        public bool TryGetText(out string text)
        {
            text = Text;
            return Text != null;
        }

        public void SetText(string text)
        {
            SetTextCallCount++;
            Text = text;
        }

        public void Clear()
        {
            ClearCallCount++;
            Text = null;
        }
    }
}
