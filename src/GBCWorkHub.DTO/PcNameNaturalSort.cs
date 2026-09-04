using System;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// PC명 자연 정렬. 이름 안의 숫자는 1,2,10 순. IP는 쓰지 않음.
    /// </summary>
    public static class PcNameNaturalSort
    {
        public static int Compare(string left, string right)
        {
            string a = left ?? string.Empty;
            string b = right ?? string.Empty;
            int i = 0;
            int j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i];
                char cb = b[j];
                bool da = char.IsDigit(ca);
                bool db = char.IsDigit(cb);
                if (da && db)
                {
                    long na = 0;
                    long nb = 0;
                    while (i < a.Length && char.IsDigit(a[i]))
                    {
                        na = (na * 10) + (a[i] - '0');
                        i++;
                    }
                    while (j < b.Length && char.IsDigit(b[j]))
                    {
                        nb = (nb * 10) + (b[j] - '0');
                        j++;
                    }
                    if (na != nb)
                        return na < nb ? -1 : 1;
                    continue;
                }

                char la = char.ToUpperInvariant(ca);
                char lb = char.ToUpperInvariant(cb);
                if (la != lb)
                    return la < lb ? -1 : 1;
                i++;
                j++;
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>갤러리 그룹 순서. 진료지원 → 진료간호 → 원무 → 배포서버 → ETC → 그 외 → 미지정.</summary>
        public static int CompareGroup(string left, string right)
        {
            int a = GroupRank(left);
            int b = GroupRank(right);
            if (a != b)
                return a.CompareTo(b);
            return string.Compare(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static int GroupRank(string group)
        {
            if (string.IsNullOrWhiteSpace(group))
                return 100;
            string g = group.Trim();
            if (string.Equals(g, "진료지원", StringComparison.Ordinal))
                return 0;
            if (string.Equals(g, "진료간호", StringComparison.Ordinal))
                return 1;
            if (string.Equals(g, "원무", StringComparison.Ordinal))
                return 2;
            if (string.Equals(g, "배포서버", StringComparison.Ordinal))
                return 3;
            if (string.Equals(g, "ETC", StringComparison.OrdinalIgnoreCase))
                return 4;
            return 50;
        }
    }
}
