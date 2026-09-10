using System;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminUsageLogItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public long LogId { get; set; }
        public string RemoteAccessIpAddress { get; set; }
        public string RemotePcName { get; set; }
        public string SiteCode { get; set; }
        public string AccessUserId { get; set; }
        public string AccessPcName { get; set; }
        public string AccessIpAddress { get; set; }
        public string SessionStatus { get; set; }
        public string ResultMessage { get; set; }
        public DateTime? RequestedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value, "IsSelected"); }
        }

        public bool ShowSiteBadge { get; set; }

        public string SiteDisplay
        {
            get { return string.IsNullOrWhiteSpace(SiteCode) ? "미지정" : SiteCode.Trim().ToUpperInvariant(); }
        }

        public string Title
        {
            get
            {
                string user = string.IsNullOrWhiteSpace(AccessUserId) ? "-" : AccessUserId.Trim();
                string pc = string.IsNullOrWhiteSpace(RemotePcName) ? "-" : RemotePcName.Trim();
                return user + " · " + pc;
            }
        }

        public string Subtitle
        {
            get
            {
                string status = StatusDisplay;
                string span = OccupancySpanDisplay;
                return status + "  ·  " + span;
            }
        }

        public string StatusDisplay
        {
            get
            {
                if (string.Equals(SessionStatus, RemotePcDbStatuses.SessionEnded, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SessionStatus, "ENDED", StringComparison.OrdinalIgnoreCase))
                    return "종료";
                if (string.Equals(SessionStatus, RemotePcDbStatuses.SessionCancelled, StringComparison.OrdinalIgnoreCase))
                    return "취소";
                if (string.Equals(SessionStatus, RemotePcDbStatuses.SessionFailed, StringComparison.OrdinalIgnoreCase))
                    return "실패";
                if (string.Equals(SessionStatus, RemotePcDbStatuses.InUse, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SessionStatus, RemotePcDbStatuses.SessionInUse, StringComparison.OrdinalIgnoreCase))
                    return "사용 중";
                if (string.Equals(SessionStatus, RemotePcDbStatuses.Connecting, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SessionStatus, RemotePcDbStatuses.SessionConnecting, StringComparison.OrdinalIgnoreCase))
                    return "연결 중";
                return string.IsNullOrWhiteSpace(SessionStatus) ? "-" : SessionStatus;
            }
        }

        public string OccupancySpanDisplay
        {
            get
            {
                if (!RequestedAt.HasValue)
                    return "-";
                string start = RequestedAt.Value.ToString("MM-dd HH:mm");
                if (!EndedAt.HasValue)
                    return start + " ~";
                return start + " ~ " + EndedAt.Value.ToString("MM-dd HH:mm");
            }
        }

        public bool MatchesSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return true;
            return Contains(AccessUserId, q)
                || Contains(RemotePcName, q)
                || Contains(RemoteAccessIpAddress, q)
                || Contains(AccessPcName, q)
                || Contains(AccessIpAddress, q)
                || Contains(SiteCode, q)
                || Contains(SessionStatus, q)
                || Contains(StatusDisplay, q);
        }

        private static bool Contains(string hay, string needle)
        {
            return !string.IsNullOrWhiteSpace(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static AdminUsageLogItemViewModel FromDto(RemotePcUsageLogDto dto)
        {
            if (dto == null)
                return null;
            return new AdminUsageLogItemViewModel
            {
                LogId = dto.LogId,
                RemoteAccessIpAddress = dto.RemoteAccessIpAddress,
                RemotePcName = dto.RemotePcName,
                SiteCode = dto.SiteCode,
                AccessUserId = dto.AccessUserId,
                AccessPcName = dto.AccessPcName,
                AccessIpAddress = dto.AccessIpAddress,
                SessionStatus = dto.SessionStatus,
                ResultMessage = dto.ResultMessage,
                RequestedAt = dto.RequestedAt,
                EndedAt = dto.EndedAt
            };
        }
    }
}
