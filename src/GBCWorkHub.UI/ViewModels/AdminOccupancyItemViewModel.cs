using System;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminOccupancyItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public string SiteCode { get; set; }
        public string TeamName { get; set; }
        public string RemotePcName { get; set; }
        public string RemoteAccessIp { get; set; }
        public string Occupant { get; set; }
        public string AccessPcName { get; set; }
        public string StatusCode { get; set; }
        public string StatusLabel { get; set; }
        public string SessionToken { get; set; }
        public string StartedAtText { get; set; }
        public bool CanForceRelease { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value, "IsSelected"); }
        }

        public bool IsInUse
        {
            get
            {
                return string.Equals(StatusCode, "IN_USE", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StatusCode, "CONNECTING", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StatusCode, "CHECK_REQUIRED", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string Title
        {
            get
            {
                string site = string.IsNullOrWhiteSpace(SiteCode) ? "-" : SiteCode.Trim();
                string pc = string.IsNullOrWhiteSpace(RemotePcName) ? "-" : RemotePcName.Trim();
                return site + " · " + pc;
            }
        }

        public string Subtitle
        {
            get
            {
                string team = string.IsNullOrWhiteSpace(TeamName) ? "-" : TeamName.Trim();
                string occupant = string.IsNullOrWhiteSpace(Occupant) ? "-" : Occupant.Trim();
                string accessPc = string.IsNullOrWhiteSpace(AccessPcName) ? "-" : AccessPcName.Trim();
                return team + " · " + StatusLabel + " · " + occupant + " @ " + accessPc;
            }
        }

        public bool MatchesSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return true;
            return Contains(Title, q)
                || Contains(TeamName, q)
                || Contains(RemoteAccessIp, q)
                || Contains(Occupant, q)
                || Contains(AccessPcName, q)
                || Contains(StatusLabel, q);
        }

        private static bool Contains(string hay, string needle)
        {
            return !string.IsNullOrWhiteSpace(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static AdminOccupancyItemViewModel FromStatus(RemotePcStatus s)
        {
            if (s == null)
                return null;
            bool canForce = !string.IsNullOrWhiteSpace(s.SessionToken)
                && !string.IsNullOrWhiteSpace(s.RemoteAccessIpAddress)
                && !string.Equals(s.AccessStatusCode, "AVAILABLE", StringComparison.OrdinalIgnoreCase);
            string started = s.AccessStartDateTime.HasValue
                ? s.AccessStartDateTime.Value.ToString("MM-dd HH:mm")
                : "-";
            return new AdminOccupancyItemViewModel
            {
                SiteCode = s.SiteCode,
                RemotePcName = s.RemotePcName,
                RemoteAccessIp = s.RemoteAccessIpAddress,
                Occupant = s.AccessUserId,
                AccessPcName = s.AccessPcName,
                StatusCode = s.AccessStatusCode,
                StatusLabel = s.DisplayStatus,
                SessionToken = s.SessionToken,
                StartedAtText = started,
                CanForceRelease = canForce
            };
        }
    }
}
