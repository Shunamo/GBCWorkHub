namespace GBCWorkHub.DTO
{
    /// <summary>
    /// 여러 BIZ/UI 클래스가 각자 들고 있던 IPv4 판별 로직을 하나로 모은 것
    /// (동작 변경 없음 — 기존 5곳의 구현과 결과가 동일함을 확인 후 통합).
    /// </summary>
    public static class NetworkAddressUtils
    {
        public static bool LooksLikeIpv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            foreach (string p in parts)
            {
                int n;
                if (!int.TryParse(p, out n) || n < 0 || n > 255)
                    return false;
            }
            return true;
        }
    }
}
