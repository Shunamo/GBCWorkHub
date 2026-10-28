namespace GBCWorkHub.BIZ.WorkLog
{
    /// <summary>
    /// TfsPathClassifier / TfsWorkLogCandidateClassifier / TfsWorkLogParser가 각자 들고 있던
    /// 경로 유틸을 하나로 모은 것(동작 변경 없음 — 기존 3곳의 구현과 결과가 동일함을 확인 후 통합).
    /// </summary>
    internal static class TfsPathUtils
    {
        public static string ExtractFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            string n = path.Replace('\\', '/').TrimEnd('/');
            int idx = n.LastIndexOf('/');
            return idx >= 0 && idx < n.Length - 1 ? n.Substring(idx + 1) : n;
        }

        /// <summary>path는 이미 소문자 + '/' 정규화된 상태여야 한다(호출부 선조건 동일하게 유지).</summary>
        public static bool HasSegment(string pathLower, string segment)
        {
            if (string.IsNullOrEmpty(pathLower) || string.IsNullOrEmpty(segment))
                return false;
            string s = segment.ToLowerInvariant();
            foreach (var part in pathLower.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(part, s, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
