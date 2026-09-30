namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>Admin PC 편집 화면의 Domain/VPN/Auth ID+PW 한 세트. ID/PW가 항상 짝으로
    /// 추가·삭제되도록 하기 위한 행 단위 모델 — 두 목록을 줄 번호로만 짝짓던
    /// 예전 방식은 한쪽만 편집하면 개수가 어긋나는 문제가 있었다.</summary>
    public sealed class AdminCredSetItem
    {
        public string Id { get; set; } = string.Empty;
        public string Pw { get; set; } = string.Empty;
    }
}
