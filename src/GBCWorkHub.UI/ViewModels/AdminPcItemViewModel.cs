using System;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminPcItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public string SiteCode { get; set; }
        public string PcName { get; set; }
        public string PcIp { get; set; }
        public string ShareKey { get; set; }
        public string TeamName { get; set; }
        public string PcDomain { get; set; }
        public string PcNote { get; set; }
        public string PcComment { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value, "IsSelected"); }
        }

        public string Title
        {
            get { return string.IsNullOrWhiteSpace(PcName) ? "-" : PcName.Trim(); }
        }

        public string Subtitle
        {
            get { return string.IsNullOrWhiteSpace(PcIp) ? "-" : PcIp.Trim(); }
        }

        public string TeamLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TeamName)
                    || string.Equals(TeamName.Trim(), "미지정", StringComparison.Ordinal))
                    return string.Empty;
                return TeamName.Trim();
            }
        }

        public bool HasTeamLabel
        {
            get { return !string.IsNullOrWhiteSpace(TeamLabel); }
        }

        public bool MatchesSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return true;
            return Contains(PcName, q)
                || Contains(SiteCode, q)
                || Contains(PcIp, q)
                || Contains(ShareKey, q)
                || Contains(TeamName, q)
                || Contains(PcDomain, q)
                || Contains(PcComment, q);
        }

        private static bool Contains(string hay, string needle)
        {
            return !string.IsNullOrWhiteSpace(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static AdminPcItemViewModel FromDto(PcMapDto dto)
        {
            if (dto == null)
                return null;
            return new AdminPcItemViewModel
            {
                SiteCode = dto.SiteCode,
                PcName = dto.PcName,
                PcIp = dto.PcIp,
                ShareKey = dto.ShareKey,
                TeamName = dto.TeamName,
                PcDomain = dto.PcDomain,
                PcNote = dto.PcNote,
                PcComment = dto.PcComment
            };
        }
    }
}
