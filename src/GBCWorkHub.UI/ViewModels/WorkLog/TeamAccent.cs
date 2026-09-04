using System;
using System.Windows.Media;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Services.TfsSync;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    /// <summary>
    /// 소속 색. 엑셀 범례(보라/파랑/초록/노랑)를 WorkHub 유리톤으로 낮춘 값.
    /// </summary>
    public static class TeamAccent
    {
        public const string Support = "진료지원";
        public const string Nursing = "진료간호";
        public const string Admin = "원무";
        public const string Etc = "ETC";

        private static readonly Brush SupportWash = Freeze("#E4DCF0");
        private static readonly Brush SupportBar = Freeze("#7B6EAD");
        private static readonly Brush SupportFg = Freeze("#5C5280");
        private static readonly Brush NursingWash = Freeze("#D9E4F7");
        private static readonly Brush NursingBar = Freeze("#4F67D8");
        private static readonly Brush NursingFg = Freeze("#405CC3");
        private static readonly Brush AdminWash = Freeze("#D5EBDD");
        private static readonly Brush AdminBar = Freeze("#3D8A64");
        private static readonly Brush AdminFg = Freeze("#246B4D");
        private static readonly Brush EtcWash = Freeze("#F3E9B8");
        private static readonly Brush EtcBar = Freeze("#C4A832");
        private static readonly Brush EtcFg = Freeze("#8A6B18");
        private static readonly Brush NeutralWash = Freeze("#C8FFFFFF");
        private static readonly Brush NeutralBar = Brushes.Transparent;
        private static readonly Brush NeutralFg = Freeze("#64748B");

        public static string MatchKnown(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (WorkLogTeamNames.ContainsKey(value, Support))
                return Support;
            if (WorkLogTeamNames.ContainsKey(value, Nursing))
                return Nursing;
            if (WorkLogTeamNames.ContainsKey(value, Admin))
                return Admin;
            if (WorkLogTeamNames.EqualsKey(value, Etc)
                || WorkLogTeamNames.ContainsKey(value, "PIS")
                || WorkLogTeamNames.ContainsKey(value, "고객지원")
                || WorkLogTeamNames.ContainsKey(value, "디자인")
                || WorkLogTeamNames.EqualsKey(value, "DA")
                || WorkLogTeamNames.EqualsKey(value, "TA"))
                return Etc;
            return null;
        }

        public static string Resolve(
            string teamName,
            string authorName,
            string authorId,
            string pcName,
            string siteCode)
        {
            string known = MatchKnown(teamName)
                ?? MatchKnown(authorName)
                ?? MatchKnown(authorId);
            if (known != null)
                return known;

            known = MatchKnown(DirectoryBiz.LookupTeam(authorName, null))
                ?? MatchKnown(DirectoryBiz.LookupTeam(StripDomain(authorId), null))
                ?? MatchKnown(DirectoryBiz.LookupTeam(authorId, null));
            if (known != null)
                return known;

            if (OccupancyNameStore.IsLocalOccupant(authorName)
                || OccupancyNameStore.IsLocalOccupant(authorId))
            {
                known = MatchKnown(OccupancyNameStore.TryGetAffiliation());
                if (known != null)
                    return known;
            }

            string site = string.IsNullOrWhiteSpace(siteCode)
                ? TfsCheckinInboxStore.InferSiteCode(pcName, null)
                : siteCode.Trim();
            known = MatchKnown(DirectoryBiz.LookupPcTeam(site, pcName));
            if (known != null)
                return known;
            return MatchKnown(PcTeamCatalog.Find(pcName));
        }

        public static Brush Wash(string team)
        {
            string kind = MatchKnown(team);
            if (kind == Support) return SupportWash;
            if (kind == Nursing) return NursingWash;
            if (kind == Admin) return AdminWash;
            if (kind == Etc) return EtcWash;
            return NeutralWash;
        }

        public static Brush Bar(string team)
        {
            string kind = MatchKnown(team);
            if (kind == Support) return SupportBar;
            if (kind == Nursing) return NursingBar;
            if (kind == Admin) return AdminBar;
            if (kind == Etc) return EtcBar;
            return NeutralBar;
        }

        public static Brush Foreground(string team)
        {
            string kind = MatchKnown(team);
            if (kind == Support) return SupportFg;
            if (kind == Nursing) return NursingFg;
            if (kind == Admin) return AdminFg;
            if (kind == Etc) return EtcFg;
            return NeutralFg;
        }

        public static bool HasAccent(string team)
        {
            return MatchKnown(team) != null;
        }

        private static string StripDomain(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
                return null;
            string raw = account.Trim();
            int slash = raw.LastIndexOf('\\');
            if (slash >= 0 && slash < raw.Length - 1)
                return raw.Substring(slash + 1);
            return raw;
        }

        private static Brush Freeze(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            if (brush != null && brush.CanFreeze)
                brush.Freeze();
            return brush;
        }
    }
}
