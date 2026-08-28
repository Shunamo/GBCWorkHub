namespace GBCWorkHub.UI.Services.TfsSync
{
    /// <summary>
    /// 클립보드 읽기/쓰기 추상화. 실제 구현은 <see cref="WpfClipboardAccessor"/>,
    /// 테스트에서는 in-memory fake로 교체해 결정론적으로 검증한다.
    /// </summary>
    public interface IClipboardAccessor
    {
        /// <summary>현재 클립보드가 텍스트를 담고 있으면 text에 채우고 true.</summary>
        bool TryGetText(out string text);

        void SetText(string text);

        void Clear();
    }
}
