using System;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class RemotePcUsageLogItemViewModel : ViewModelBase
    {
        public long LogId { get; set; }
        public string AccessUserId { get; set; }
        public string AccessPcName { get; set; }
        public string AccessIpAddress { get; set; }
        public string SessionStatus { get; set; }
        public DateTime? RequestedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public string UserDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AccessUserId))
                    return AccessUserId.Trim();
                return "-";
            }
        }

        public string ClientDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AccessPcName) && !string.IsNullOrWhiteSpace(AccessIpAddress))
                    return AccessPcName.Trim() + " (" + AccessIpAddress.Trim() + ")";
                if (!string.IsNullOrWhiteSpace(AccessPcName))
                    return AccessPcName.Trim();
                if (!string.IsNullOrWhiteSpace(AccessIpAddress))
                    return AccessIpAddress.Trim();
                return "-";
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

        public string RequestedAtDisplay
        {
            get
            {
                return RequestedAt.HasValue
                    ? RequestedAt.Value.ToString("MM-dd HH:mm")
                    : "-";
            }
        }

        public string EndedAtDisplay
        {
            get
            {
                return EndedAt.HasValue
                    ? EndedAt.Value.ToString("MM-dd HH:mm")
                    : "-";
            }
        }

        public static RemotePcUsageLogItemViewModel FromDto(RemotePcUsageLogDto dto)
        {
            if (dto == null)
                return null;
            return new RemotePcUsageLogItemViewModel
            {
                LogId = dto.LogId,
                AccessUserId = dto.AccessUserId,
                AccessPcName = dto.AccessPcName,
                AccessIpAddress = dto.AccessIpAddress,
                SessionStatus = dto.SessionStatus,
                RequestedAt = dto.RequestedAt,
                EndedAt = dto.EndedAt
            };
        }
    }
}
