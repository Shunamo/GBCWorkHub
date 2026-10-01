using System;
using System.Collections.Generic;
using System.Windows.Input;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>PC_NOTE/PC_DOMAIN ↔ Domain/VPN/Auth 편집용 섹션(Sets) 모델 간 변환을 한 곳에
    /// 모은다. 일반 사용자 "PC 접속정보 수정" 패널(RemoteWorkspaceViewModel)과 Admin PC 편집
    /// 패널(AdminViewModel)이 이 로직을 공유해서, 양쪽에서 저장/검증 동작이 어긋나지 않게 한다.</summary>
    public static class PcAccessSectionBuilder
    {
        /// <summary>PC_NOTE(구조화된 원본, 없으면 PC_DOMAIN 레거시 필드)에서 Domain/VPN/Auth
        /// 섹션을 만든다. ID/PW 개수가 같은 줄끼리는 각자 완결된 세트로, 개수가 안 맞아 남는
        /// 줄은 마지막 세트의 해당 컬렉션(ID 또는 PW)에 별도 줄로 덧붙인다(병합 문자열 아님 —
        /// 각 줄이 독립적으로 복사/삭제 가능해야 함).</summary>
        public static List<PcAccessSectionViewModel> BuildSections(string pcNote, string pcDomain, string siteCode, ICommand copyCommand)
        {
            var ids = new List<string>();
            var pws = new List<string>();
            var vpnIds = new List<string>();
            var vpnPws = new List<string>();
            var authIds = new List<string>();
            var authPws = new List<string>();

            IList<PcAccessCredential> creds = PcAccessNoteParser.ParseCredentials(pcNote);
            if ((creds == null || creds.Count == 0) && !string.IsNullOrWhiteSpace(pcDomain))
            {
                string[] parts = pcDomain.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string t = parts[i].Trim();
                    if (t.Length > 0)
                        ids.Add(t);
                }
            }
            else if (creds != null)
            {
                bool cmc = string.Equals(siteCode, "CMC", StringComparison.OrdinalIgnoreCase);
                for (int i = 0; i < creds.Count; i++)
                {
                    PcAccessCredential c = creds[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.Value))
                        continue;
                    string v = c.Value.Trim();
                    if (string.Equals(c.Kind, "PW", StringComparison.OrdinalIgnoreCase))
                        pws.Add(v);
                    else if (string.Equals(c.Kind, "AUTH_ID", StringComparison.OrdinalIgnoreCase)
                        || (cmc && string.Equals(c.Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase)))
                        authIds.Add(v);
                    else if (string.Equals(c.Kind, "AUTH_PW", StringComparison.OrdinalIgnoreCase)
                        || (cmc && string.Equals(c.Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase)))
                        authPws.Add(v);
                    else if (string.Equals(c.Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase))
                        vpnIds.Add(v);
                    else if (string.Equals(c.Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase))
                        vpnPws.Add(v);
                    else
                        ids.Add(v);
                }
            }

            var sections = new List<PcAccessSectionViewModel>();
            if (ids.Count > 0 || pws.Count > 0)
                sections.Add(CreateSection("Domain", ids, pws, copyCommand));
            if (vpnIds.Count > 0 || vpnPws.Count > 0)
                sections.Add(CreateSection("VPN", vpnIds, vpnPws, copyCommand));
            if (authIds.Count > 0 || authPws.Count > 0)
                sections.Add(CreateSection("Auth", authIds, authPws, copyCommand));
            return sections;
        }

        /// <summary>ID/PW flat 리스트로 섹션 하나를 만든다. 짝이 맞는 만큼은 별개 세트로,
        /// 남는 줄은 마지막 세트에 같은 종류(ID/PW) 줄로 덧붙인다.</summary>
        public static PcAccessSectionViewModel CreateSection(string title, IList<string> idValues, IList<string> pwValues, ICommand copyCommand)
        {
            var section = new PcAccessSectionViewModel(title);
            int idCount = idValues != null ? idValues.Count : 0;
            int pwCount = pwValues != null ? pwValues.Count : 0;
            int pairCount = Math.Min(idCount, pwCount);

            for (int i = 0; i < pairCount; i++)
            {
                section.Sets.Add(new PcCredentialSetViewModel(
                    section, new[] { idValues[i] }, new[] { pwValues[i] }, copyCommand));
            }

            int idRemaining = idCount - pairCount;
            int pwRemaining = pwCount - pairCount;

            if (section.Sets.Count == 0)
            {
                // pairCount == 0: one side (or both) is empty — seed the lone set directly
                // instead of creating a blank placeholder set and then appending to it.
                IEnumerable<string> seedIds = idRemaining > 0 ? Slice(idValues, pairCount, idRemaining) : null;
                IEnumerable<string> seedPws = pwRemaining > 0 ? Slice(pwValues, pairCount, pwRemaining) : null;
                section.Sets.Add(new PcCredentialSetViewModel(section, seedIds, seedPws, copyCommand));
                return section;
            }

            PcCredentialSetViewModel last = section.Sets[section.Sets.Count - 1];
            for (int i = pairCount; i < idCount; i++)
                last.AddIdLine(copyCommand, idValues[i]);
            for (int i = pairCount; i < pwCount; i++)
                last.AddPwLine(copyCommand, pwValues[i]);

            return section;
        }

        private static IEnumerable<string> Slice(IList<string> list, int start, int count)
        {
            for (int i = 0; i < count; i++)
                yield return list[start + i];
        }

        public static PcCredentialSetViewModel CreateEmptySet(PcAccessSectionViewModel owner, ICommand copyCommand)
        {
            return new PcCredentialSetViewModel(owner, null, null, copyCommand);
        }

        public static PcAccessSectionViewModel CreateEmptySection(string title, ICommand copyCommand)
        {
            var section = new PcAccessSectionViewModel(title);
            section.Sets.Add(CreateEmptySet(section, copyCommand));
            return section;
        }

        /// <summary>섹션의 모든 세트를 순회하며 공백 아닌 ID/PW 줄 전부를 순서대로 모은다.
        /// 세트 경계는 무시한다 — PC_NOTE는 세트 구분 없이 플랫한 줄 목록이라 저장 시 손실 없음.</summary>
        public static void Collect(PcAccessSectionViewModel section, IList<string> idsTarget, IList<string> pwsTarget)
        {
            if (section == null || section.Sets == null)
                return;
            foreach (PcCredentialSetViewModel set in section.Sets)
            {
                if (set == null)
                    continue;
                if (idsTarget != null)
                {
                    foreach (PcCredentialLineViewModel line in set.Id)
                    {
                        string v = line != null ? (line.Value ?? string.Empty).Trim() : string.Empty;
                        if (v.Length > 0)
                            idsTarget.Add(v);
                    }
                }
                if (pwsTarget != null)
                {
                    foreach (PcCredentialLineViewModel line in set.Pw)
                    {
                        string v = line != null ? (line.Value ?? string.Empty).Trim() : string.Empty;
                        if (v.Length > 0)
                            pwsTarget.Add(v);
                    }
                }
            }
        }

        /// <summary>PC_DOMAIN 요약 필드용 — Domain 섹션의 ID만(중복 제거) 모은다. VPN/Auth는
        /// 일부러 포함하지 않는다 — 과거 여기 섞여서 PC 카드에 VPN 계정이 같이 뜨던 버그가 있었다.</summary>
        public static string BuildPcDomainSummary(IEnumerable<PcAccessSectionViewModel> sections)
        {
            var ids = new List<string>();
            PcAccessSectionViewModel domain = FindSection(sections, "Domain");
            if (domain != null)
                Collect(domain, ids, null);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (seen.Add(ids[i]))
                    unique.Add(ids[i]);
            }
            return unique.Count == 0 ? null : string.Join("\n", unique.ToArray());
        }

        public static string BuildPcNote(IEnumerable<PcAccessSectionViewModel> sections)
        {
            var ids = new List<string>();
            var pws = new List<string>();
            var vpnIds = new List<string>();
            var vpnPws = new List<string>();
            var authIds = new List<string>();
            var authPws = new List<string>();

            PcAccessSectionViewModel domain = FindSection(sections, "Domain");
            if (domain != null)
                Collect(domain, ids, pws);
            PcAccessSectionViewModel vpn = FindSection(sections, "VPN");
            if (vpn != null)
                Collect(vpn, vpnIds, vpnPws);
            PcAccessSectionViewModel auth = FindSection(sections, "Auth");
            if (auth != null)
                Collect(auth, authIds, authPws);

            var parts = new List<string>();
            if (ids.Count > 0)
                parts.Add("[ID]\n" + string.Join("\n", ids.ToArray()));
            if (pws.Count > 0)
                parts.Add("[Password]\n" + string.Join("\n", pws.ToArray()));
            if (vpnIds.Count > 0)
                parts.Add("[VPN ID]\n" + string.Join("\n", vpnIds.ToArray()));
            if (vpnPws.Count > 0)
                parts.Add("[VPN Password]\n" + string.Join("\n", vpnPws.ToArray()));
            if (authIds.Count > 0)
                parts.Add("[Auth ID]\n" + string.Join("\n", authIds.ToArray()));
            if (authPws.Count > 0)
                parts.Add("[Auth Password]\n" + string.Join("\n", authPws.ToArray()));
            return parts.Count == 0 ? null : string.Join("\n\n", parts.ToArray());
        }

        /// <summary>섹션 안에 ID 줄은 있는데 PW 줄이 하나도 없거나(또는 반대) 하면 미완성으로 본다.
        /// 빈 줄(추가했지만 아직 입력 안 한 줄)은 저장 시 조용히 무시되므로 막을 필요 없다.</summary>
        public static bool TryValidate(IEnumerable<PcAccessSectionViewModel> sections, out string errorSectionTitle)
        {
            errorSectionTitle = null;
            if (sections == null)
                return true;
            foreach (PcAccessSectionViewModel section in sections)
            {
                if (section == null)
                    continue;
                var ids = new List<string>();
                var pws = new List<string>();
                Collect(section, ids, pws);
                if ((ids.Count > 0) != (pws.Count > 0))
                {
                    errorSectionTitle = section.Title;
                    return false;
                }
            }
            return true;
        }

        private static PcAccessSectionViewModel FindSection(IEnumerable<PcAccessSectionViewModel> sections, string title)
        {
            if (sections == null)
                return null;
            foreach (PcAccessSectionViewModel s in sections)
            {
                if (s != null && string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }
    }
}
