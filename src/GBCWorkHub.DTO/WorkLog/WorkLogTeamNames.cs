using System;
using System.Text;

namespace GBCWorkHub.DTO.WorkLog
{
    /// <summary>소속 필터. 공백·가운뎃점(·)을 무시하고 비교한다.</summary>
    public static class WorkLogTeamNames
    {
        public static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                if (c == ' ' || c == '\t' || c == '\u00B7' || c == '\u2022' || c == '\u30FB')
                    continue;
                sb.Append(c);
            }

            return sb.ToString();
        }

        public static bool EqualsKey(string a, string b)
        {
            string left = NormalizeKey(a);
            string right = NormalizeKey(b);
            if (left.Length == 0 || right.Length == 0)
                return false;
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsKey(string haystack, string needle)
        {
            string n = NormalizeKey(needle);
            if (n.Length == 0)
                return false;
            return NormalizeKey(haystack).IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
