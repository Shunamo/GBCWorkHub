using System;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class RemotePcUsageLogItemViewModel : ViewModelBase
    {
        public long LogId { get; set; }
        public string RemoteAccessIpAddress { get; set; }
        public string RemotePcName { get; set; }
        public string SiteCode { get; set; }
        public string AccessUserId { get; set; }
        public string AccessPcName { get; set; }
        public string AccessIpAddress { get; set; }
        public string SessionStatus { get; set; }
        public DateTime? RequestedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        private DateTime? KoreaOccupancyStart
        {
            get { return ToKoreaOccupancy(RequestedAt); }
        }

        private DateTime? KoreaOccupancyEnd
        {
            get { return ToKoreaOccupancy(EndedAt); }
        }

        private DateTime? ToKoreaOccupancy(DateTime? value)
        {
            return KoreaTime.ToKorea(value);
        }

        public string OccupantNameDisplay
        {
            get { return OccupancyNameStore.ToNameOnly(AccessUserId, AccessPcName); }
        }

        public string AffiliationDisplay
        {
            get { return OccupancyNameStore.ToAffiliation(AccessUserId, AccessPcName); }
        }

        public string LocalPcDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AccessPcName))
                    return AccessPcName.Trim();
                if (!string.IsNullOrWhiteSpace(AccessIpAddress))
                    return AccessIpAddress.Trim();
                return "-";
            }
        }

        public string UserDisplay
        {
            get { return OccupancyNameStore.ToDisplayName(AccessUserId, AccessPcName); }
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
                return KoreaTime.Format(KoreaOccupancyStart, "MM-dd HH:mm");
            }
        }

        public string EndedAtDisplay
        {
            get
            {
                return KoreaTime.Format(KoreaOccupancyEnd, "MM-dd HH:mm");
            }
        }

        public string OccupancyDateDisplay
        {
            get
            {
                return KoreaTime.Format(KoreaOccupancyStart, "yy.MM.dd");
            }
        }

        public string OccupancySpanDisplay
        {
            get
            {
                DateTime? startAt = KoreaOccupancyStart;
                if (!startAt.HasValue)
                    return "-";
                string start = startAt.Value.ToString("HH:mm");
                DateTime? endAt = KoreaOccupancyEnd;
                if (!endAt.HasValue)
                    return start + " ~";
                if (endAt.Value.Date == startAt.Value.Date)
                    return start + " ~" + endAt.Value.ToString("HH:mm");
                return start + " ~" + endAt.Value.ToString("yy.MM.dd HH:mm");
            }
        }

        public string WindowDisplay
        {
            get { return RequestedAtDisplay + " ~ " + EndedAtDisplay; }
        }

        public string RemotePcDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(RemotePcName) && !string.IsNullOrWhiteSpace(RemoteAccessIpAddress))
                    return RemotePcName.Trim() + " (" + RemoteAccessIpAddress.Trim() + ")";
                if (!string.IsNullOrWhiteSpace(RemotePcName))
                    return RemotePcName.Trim();
                if (!string.IsNullOrWhiteSpace(RemoteAccessIpAddress))
                    return RemoteAccessIpAddress.Trim();
                return "-";
            }
        }

        public double? DurationMinutes
        {
            get
            {
                if (!RequestedAt.HasValue)
                    return null;
                DateTime end = KoreaOccupancyEnd ?? KoreaTime.Now;
                DateTime start = KoreaOccupancyStart ?? RequestedAt.Value;
                TimeSpan span = end - start;
                if (span < TimeSpan.Zero)
                    return 0;
                return span.TotalMinutes;
            }
        }

        public string DurationDisplay
        {
            get
            {
                if (!RequestedAt.HasValue)
                    return "-";
                if (!EndedAt.HasValue)
                    return "진행 중";
                double? minutes = DurationMinutes;
                if (!minutes.HasValue)
                    return "-";
                TimeSpan span = TimeSpan.FromMinutes(minutes.Value);
                if (span.TotalMinutes < 1)
                    return "1분 미만";
                if (span.TotalHours < 1)
                    return ((int)span.TotalMinutes) + "분";
                if (span.TotalHours < 24)
                    return ((int)span.TotalHours) + "시간 " + span.Minutes + "분";
                return ((int)span.TotalDays) + "일";
            }
        }

        /// <summary>종료된 내 점유 구간만 갤러리에서 TFS 가져오기.</summary>
        public bool CanFetchTfs
        {
            get
            {
                if (!RequestedAt.HasValue || !EndedAt.HasValue)
                    return false;
                if (!OccupancyNameStore.IsLocalOccupant(AccessUserId, AccessPcName))
                    return false;
                return string.Equals(SessionStatus, RemotePcDbStatuses.SessionEnded, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SessionStatus, "ENDED", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static RemotePcUsageLogItemViewModel FromDto(RemotePcUsageLogDto dto)
        {
            if (dto == null)
                return null;
            return new RemotePcUsageLogItemViewModel
            {
                LogId = dto.LogId,
                RemoteAccessIpAddress = dto.RemoteAccessIpAddress,
                RemotePcName = dto.RemotePcName,
                AccessUserId = dto.AccessUserId,
                AccessPcName = dto.AccessPcName,
                AccessIpAddress = dto.AccessIpAddress,
                SessionStatus = dto.SessionStatus,
                RequestedAt = KoreaTime.FromOccupancyDb(dto.RequestedAt),
                EndedAt = KoreaTime.FromOccupancyDb(dto.EndedAt),
                SiteCode = dto.SiteCode
            };
        }
    }
}
