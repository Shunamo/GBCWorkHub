using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GBCWorkHub.DTO
{
    /// <summary>PC_NOTE의 [ID]/[Password]/[VPN ID]/[VPN Password] 행. 각각 복사용.</summary>
    public sealed class PcAccessCredential
    {
        public string Kind { get; set; }
        public string Value { get; set; }

        public string Label
        {
            get
            {
                if (string.Equals(Kind, "PW", StringComparison.OrdinalIgnoreCase))
                    return "PW";
                if (string.Equals(Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase))
                    return "VPN";
                if (string.Equals(Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase))
                    return "VPW";
                return "ID";
            }
        }
    }

    /// <summary>PC_NOTE([ID]/[Password]/[VPN ID]/[VPN Password]/[Comment]) 파싱.</summary>
    public static class PcAccessNoteParser
    {
        private static readonly Regex CredToken = new Regex(
            @"(?<!\S)([^\s\\/]+\\[^\s\\/]+|[^\s@]+@[^\s@]+)(?!\S)",
            RegexOptions.Compiled);

        private static readonly Regex SimpleUser = new Regex(
            @"^[A-Za-z][A-Za-z0-9._-]{1,40}$",
            RegexOptions.Compiled);

        private static readonly Regex SectionHeader = new Regex(
            @"^\s*\[([^\]]+)\]\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex NumberedLine = new Regex(
            @"^\s*\d+\s*[\.\)]\s*(?:[^:：]*)[:：]\s*(.+)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex ArrowSplit = new Regex(
            @"\s*(?:->|→|->)\s*",
            RegexOptions.Compiled);

        public static IList<PcAccessCredential> ParseCredentials(string note)
        {
            var list = new List<PcAccessCredential>();
            if (string.IsNullOrWhiteSpace(note))
                return list;

            Dictionary<string, string> sections = SplitSections(note);
            string idBody;
            string pwBody;
            string vpnIdBody;
            string vpnPwBody;
            sections.TryGetValue("id", out idBody);
            if (!sections.TryGetValue("password", out pwBody))
                sections.TryGetValue("pw", out pwBody);
            sections.TryGetValue("vpn_id", out vpnIdBody);
            if (!sections.TryGetValue("vpn_password", out vpnPwBody))
                sections.TryGetValue("vpn_pw", out vpnPwBody);

            foreach (string id in ExtractIds(idBody))
                list.Add(new PcAccessCredential { Kind = "ID", Value = id });
            foreach (string pw in ExtractPasswords(pwBody))
                list.Add(new PcAccessCredential { Kind = "PW", Value = pw });
            foreach (string id in ExtractVpnIds(vpnIdBody))
                list.Add(new PcAccessCredential { Kind = "VPN_ID", Value = id });
            foreach (string pw in ExtractPasswords(vpnPwBody))
                list.Add(new PcAccessCredential { Kind = "VPN_PW", Value = pw });

            return list;
        }

        public static string ParseCommentFromNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return null;

            Dictionary<string, string> sections = SplitSections(note);
            string body;
            if (sections.TryGetValue("comment", out body) || sections.TryGetValue("comments", out body))
            {
                body = (body ?? string.Empty).Trim();
                return body.Length == 0 ? null : body;
            }
            return null;
        }

        private static Dictionary<string, string> SplitSections(string note)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string text = note.Replace("\r\n", "\n").Replace('\r', '\n');
            MatchCollection headers = SectionHeader.Matches(text);
            if (headers.Count == 0)
                return map;

            for (int i = 0; i < headers.Count; i++)
            {
                Match h = headers[i];
                int start = h.Index + h.Length;
                int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                if (start < 0 || end < start)
                    continue;
                string key = NormalizeSectionKey(h.Groups[1].Value);
                if (key == null)
                    continue;
                string body = text.Substring(start, end - start).Trim();
                if (!map.ContainsKey(key))
                    map[key] = body;
            }
            return map;
        }

        private static string NormalizeSectionKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            string t = raw.Trim().ToLowerInvariant();
            if (t == "id" || t == "계정" || t == "account")
                return "id";
            if (t == "password" || t == "pw" || t == "pwd" || t == "비번" || t == "비밀번호")
                return "password";
            if (t == "vpn id" || t == "vpnid" || t == "vpn_id" || t == "vpn계정" || t == "vpn 계정")
                return "vpn_id";
            if (t == "vpn password" || t == "vpn pw" || t == "vpn_pw" || t == "vpn_password"
                || t == "vpw" || t == "vpn비번" || t == "vpn 비번" || t == "vpn 비밀번호")
                return "vpn_password";
            if (t == "comment" || t == "comments" || t == "비고" || t == "메모")
                return "comment";
            return null;
        }

        private static IList<string> ExtractVpnIds(string body)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(body))
                return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in body.Split('\n'))
            {
                string line = (rawLine ?? string.Empty).Trim().Trim('"');
                if (line.Length == 0 || IsInstructionLine(line))
                    continue;

                int paren = line.IndexOf('(');
                if (paren < 0)
                    paren = line.IndexOf('（');
                if (paren > 0)
                    line = line.Substring(0, paren).Trim();

                Match numbered = NumberedLine.Match(line);
                string candidate = numbered.Success ? numbered.Groups[1].Value.Trim() : line;

                bool added = false;
                foreach (Match m in CredToken.Matches(candidate))
                {
                    string tok = CleanToken(m.Groups[1].Value);
                    if (tok == null || !seen.Add(tok))
                        continue;
                    list.Add(tok);
                    added = true;
                }
                if (added)
                    continue;

                string token = candidate.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length > 0
                    ? candidate.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0].Trim().TrimEnd('.', ',', ';')
                    : string.Empty;
                if (SimpleUser.IsMatch(token) && seen.Add(token))
                    list.Add(token);
            }
            return list;
        }

        private static IList<string> ExtractIds(string body)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(body))
                return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in body.Split('\n'))
            {
                string line = (rawLine ?? string.Empty).Trim().Trim('"');
                if (line.Length == 0)
                    continue;
                if (IsInstructionLine(line))
                    continue;

                Match numbered = NumberedLine.Match(line);
                string candidate = numbered.Success ? numbered.Groups[1].Value.Trim() : line;
                foreach (Match m in CredToken.Matches(candidate))
                {
                    string tok = CleanToken(m.Groups[1].Value);
                    if (tok == null || !seen.Add(tok))
                        continue;
                    list.Add(tok);
                }

                // bare domain\user line without extra text
                if (!numbered.Success && CredToken.IsMatch(line) == false
                    && line.IndexOf(' ') < 0 && (line.IndexOf('\\') >= 0 || line.IndexOf('@') >= 0))
                {
                    string tok = CleanToken(line);
                    if (tok != null && seen.Add(tok))
                        list.Add(tok);
                }
            }

            if (list.Count == 0)
            {
                foreach (Match m in CredToken.Matches(body))
                {
                    string tok = CleanToken(m.Groups[1].Value);
                    if (tok == null || !seen.Add(tok))
                        continue;
                    list.Add(tok);
                }
            }
            return list;
        }

        private static IList<string> ExtractPasswords(string body)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(body))
                return list;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = body.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = (lines[i] ?? string.Empty).Trim().Trim('"');
                if (line.Length == 0)
                    continue;
                if (IsInstructionLine(line))
                    continue;

                Match numbered = NumberedLine.Match(line);
                string payload;
                if (numbered.Success)
                {
                    payload = numbered.Groups[1].Value.Trim();
                    // absorb following arrow-only continuation lines into this password
                    while (i + 1 < lines.Length)
                    {
                        string next = (lines[i + 1] ?? string.Empty).Trim();
                        if (next.Length == 0)
                            break;
                        if (NumberedLine.IsMatch(next))
                            break;
                        if (next.StartsWith("->", StringComparison.Ordinal)
                            || next.StartsWith("→", StringComparison.Ordinal))
                        {
                            payload = payload + " " + next;
                            i++;
                            continue;
                        }
                        break;
                    }
                }
                else if (line.StartsWith("->", StringComparison.Ordinal)
                    || line.StartsWith("→", StringComparison.Ordinal))
                {
                    continue;
                }
                else
                {
                    payload = line;
                }

                string pw = FinalPassword(payload);
                if (string.IsNullOrWhiteSpace(pw) || !seen.Add(pw))
                    continue;
                list.Add(pw);
            }
            return list;
        }

        private static string FinalPassword(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;
            string[] parts = ArrowSplit.Split(payload);
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                string p = (parts[i] ?? string.Empty).Trim().Trim('"');
                if (p.Length == 0)
                    continue;
                return p;
            }
            return null;
        }

        private static bool IsInstructionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;
            string t = line.Trim();
            if (t.StartsWith("순서는", StringComparison.Ordinal))
                return true;
            if (t.IndexOf("진행한다", StringComparison.Ordinal) >= 0 && t.IndexOf(':') < 0)
                return true;
            return false;
        }

        private static string CleanToken(string tok)
        {
            if (string.IsNullOrWhiteSpace(tok))
                return null;
            string t = tok.Trim().TrimEnd('.', ',', ';', ')', ']');
            if (t.Length < 3)
                return null;
            if (t.IndexOf('@') >= 0)
            {
                string host = t.Substring(t.IndexOf('@') + 1);
                if (host.IndexOf('.') < 0)
                    return null;
            }
            return t;
        }
    }
}
