using System;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>
    /// 갤러리 카드용 원격 PC 항목
    /// </summary>
    public class RemoteComputerItemViewModel : ViewModelBase
    {
        private string _pcName;
        private string _ipAddress;
        private string _siteCode;
        private string _statusCode = RemotePcDbStatuses.Available;
        private string _accessUserId;
        private string _accessPcName;
        private string _sessionToken;
        private DateTime? _accessStartAt;
        private DateTime? _remoteAccessAt;
        private DateTime? _updatedAt;
        private bool _isSelected;
        private string _currentLocalUser;
        private bool _isVpnConnected;

        public string PcName
        {
            get { return _pcName; }
            set
            {
                if (SetProperty(ref _pcName, value))
                    RaisePropertyChanged("DisplayTitle");
            }
        }

        public string IpAddress
        {
            get { return _ipAddress; }
            set
            {
                if (SetProperty(ref _ipAddress, value))
                    RaisePropertyChanged("DisplaySubtitle");
            }
        }

        public string SiteCode
        {
            get { return _siteCode; }
            set
            {
                if (SetProperty(ref _siteCode, value))
                    RaisePropertyChanged("HasVpnReadyBadge");
            }
        }

        public string StatusCode
        {
            get { return _statusCode; }
            set
            {
                if (SetProperty(ref _statusCode, value))
                    RaiseComputed();
            }
        }

        public string AccessUserId
        {
            get { return _accessUserId; }
            set
            {
                if (SetProperty(ref _accessUserId, value))
                    RaiseComputed();
            }
        }

        public string AccessPcName
        {
            get { return _accessPcName; }
            set
            {
                if (SetProperty(ref _accessPcName, value))
                    RaiseComputed();
            }
        }

        public string SessionToken
        {
            get { return _sessionToken; }
            set
            {
                if (SetProperty(ref _sessionToken, value))
                    RaiseComputed();
            }
        }

        public DateTime? AccessStartAt
        {
            get { return _accessStartAt; }
            set
            {
                if (SetProperty(ref _accessStartAt, value))
                {
                    RaisePropertyChanged("ConnectionStartedDisplayText");
                    RaisePropertyChanged("UserDisplayText");
                }
            }
        }

        public DateTime? RemoteAccessAt
        {
            get { return _remoteAccessAt; }
            set { SetProperty(ref _remoteAccessAt, value); }
        }

        public DateTime? UpdatedAt
        {
            get { return _updatedAt; }
            set { SetProperty(ref _updatedAt, value); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        /// <summary>RC: Forti SSL VPN 연결됨 — 파란 체크 표시, 클릭 시 Forti 생략하고 RDP.</summary>
        public bool IsVpnConnected
        {
            get { return _isVpnConnected; }
            set
            {
                if (SetProperty(ref _isVpnConnected, value))
                    RaisePropertyChanged("HasVpnReadyBadge");
            }
        }

        public bool HasVpnReadyBadge
        {
            get
            {
                return IsVpnConnected
                    && string.Equals(SiteCode, "RC", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string CurrentLocalUser
        {
            get { return _currentLocalUser; }
            set
            {
                if (SetProperty(ref _currentLocalUser, value))
                    RaiseComputed();
            }
        }

        public string DisplayTitle
        {
            get { return string.IsNullOrWhiteSpace(PcName) ? (IpAddress ?? "-") : PcName; }
        }

        public string DisplaySubtitle
        {
            get { return IpAddress ?? "-"; }
        }

        public string StatusDisplayName
        {
            get { return RemotePcStatusMapper.ToDisplayStatus(StatusCode); }
        }

        public bool IsAvailable
        {
            get { return string.Equals(StatusCode, RemotePcDbStatuses.Available, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsConnecting
        {
            get { return string.Equals(StatusCode, RemotePcDbStatuses.Connecting, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsInUse
        {
            get { return string.Equals(StatusCode, RemotePcDbStatuses.InUse, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsCheckRequired
        {
            get { return string.Equals(StatusCode, RemotePcDbStatuses.CheckRequired, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsOwnedByCurrentUser
        {
            get
            {
                if (string.IsNullOrWhiteSpace(AccessUserId) || string.IsNullOrWhiteSpace(CurrentLocalUser))
                    return false;
                return string.Equals(AccessUserId.Trim(), CurrentLocalUser.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsOwnedByOtherUser
        {
            get
            {
                if (IsAvailable || IsCheckRequired)
                    return false;
                if (string.IsNullOrWhiteSpace(AccessUserId))
                    return IsInUse || IsConnecting;
                return !IsOwnedByCurrentUser;
            }
        }

        public bool CanConnect
        {
            get { return IsAvailable && !IsOwnedByOtherUser; }
        }

        public string OwnershipBadgeText
        {
            get
            {
                if (IsOwnedByCurrentUser && (IsInUse || IsConnecting))
                    return "내가 사용 중";
                if (IsOwnedByOtherUser)
                    return "다른 사용자 사용 중";
                return null;
            }
        }

        public bool HasOwnershipBadge
        {
            get { return !string.IsNullOrWhiteSpace(OwnershipBadgeText); }
        }

        public string PrimaryActionText
        {
            get
            {
                if (IsAvailable)
                    return "원격 접속";
                if (IsConnecting && IsOwnedByCurrentUser)
                    return "연결 중...";
                if (IsInUse && IsOwnedByCurrentUser)
                    return "사용 중";
                if (IsOwnedByOtherUser)
                    return "다른 사용자가 사용 중";
                if (IsCheckRequired)
                    return "상태 확인";
                return StatusDisplayName;
            }
        }

        public bool IsPrimaryActionEnabled
        {
            get
            {
                if (CanConnect || IsCheckRequired)
                    return true;
                return false;
            }
        }

        public string UserDisplayText
        {
            get
            {
                if (IsAvailable || string.IsNullOrWhiteSpace(AccessUserId))
                    return "사용자 없음";
                return "사용자: " + FormatUserId(AccessUserId);
            }
        }

        /// <summary>카드용: 사용 중일 때만 사용자명 (연결중은 사용 중으로 표시).</summary>
        public string OccupantText
        {
            get
            {
                if (IsAvailable || IsCheckRequired)
                    return string.Empty;
                if (string.IsNullOrWhiteSpace(AccessUserId))
                    return string.Empty;
                return FormatUserId(AccessUserId);
            }
        }

        public bool HasOccupant
        {
            get { return !string.IsNullOrWhiteSpace(OccupantText); }
        }

        /// <summary>카드용 상태: 사용 가능 / 사용 중 / 확인 필요.</summary>
        public string SimpleStatusText
        {
            get
            {
                if (IsAvailable)
                    return "사용 가능";
                if (IsCheckRequired)
                    return "확인 필요";
                return "사용 중";
            }
        }

        public string ClientPcDisplayText
        {
            get
            {
                if (IsAvailable || string.IsNullOrWhiteSpace(AccessPcName))
                    return IsAvailable ? "지금 접속 가능" : "접속 PC: -";
                return "접속 PC: " + AccessPcName;
            }
        }

        public string ConnectionStartedDisplayText
        {
            get
            {
                if (!AccessStartAt.HasValue)
                    return IsAvailable ? "" : "시작: -";
                return "시작: " + AccessStartAt.Value.ToString("HH:mm");
            }
        }

        public string PreviewStyleKey
        {
            get
            {
                if (IsAvailable)
                    return "PreviewAvailable";
                if (IsConnecting)
                    return "PreviewConnecting";
                if (IsInUse)
                    return "PreviewInUse";
                return "PreviewCheckRequired";
            }
        }

        public string StatusBadgeStyleKey
        {
            get
            {
                if (IsAvailable)
                    return "BadgeAvailable";
                if (IsConnecting)
                    return "BadgeConnecting";
                if (IsInUse)
                    return "BadgeInUse";
                return "BadgeCheckRequired";
            }
        }

        public static string FormatUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return "-";

            string raw = userId.Trim();
            int slash = raw.LastIndexOf('\\');
            if (slash >= 0 && slash < raw.Length - 1)
                raw = raw.Substring(slash + 1);

            if (raw.Length <= 3)
                return raw;

            // domain\user 형태에서 user 일부 마스킹
            if (raw.Length <= 6)
                return raw.Substring(0, 2) + "***";

            return raw.Substring(0, 3) + "***" + raw.Substring(raw.Length - 1);
        }

        public void ApplyFromDb(RemotePcStatus status, string currentLocalUser)
        {
            CurrentLocalUser = currentLocalUser;
            if (status == null)
                return;

            if (!string.IsNullOrWhiteSpace(status.RemotePcName))
                PcName = status.RemotePcName;
            if (!string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress))
                IpAddress = status.RemoteAccessIpAddress;
            if (!string.IsNullOrWhiteSpace(status.SiteCode))
                SiteCode = status.SiteCode.Trim().ToUpperInvariant();

            StatusCode = string.IsNullOrWhiteSpace(status.AccessStatusCode)
                ? RemotePcDbStatuses.Available
                : status.AccessStatusCode;
            AccessUserId = status.AccessUserId;
            AccessPcName = status.AccessPcName;
            SessionToken = status.SessionToken;
            AccessStartAt = status.AccessStartDateTime;
            RemoteAccessAt = status.RemoteAccessDateTime;
            UpdatedAt = status.UpdatedDateTime;
        }

        public void ApplyLocalInUse(string userId, string clientPc, string sessionToken)
        {
            CurrentLocalUser = userId;
            StatusCode = RemotePcDbStatuses.InUse;
            AccessUserId = userId;
            AccessPcName = clientPc;
            SessionToken = sessionToken;
            AccessStartAt = DateTime.Now;
            RemoteAccessAt = DateTime.Now;
        }

        public void ApplyLocalAvailable()
        {
            StatusCode = RemotePcDbStatuses.Available;
            AccessUserId = null;
            AccessPcName = null;
            SessionToken = null;
            AccessStartAt = null;
            RemoteAccessAt = null;
        }

        public RemotePcDto ToDto()
        {
            return new RemotePcDto
            {
                PcName = PcName,
                IpAddress = IpAddress,
                HospitalCode = SiteCode,
                HospitalName = SiteCode,
                RdpPort = 3389
            };
        }

        public static RemoteComputerItemViewModel FromDto(RemotePcDto dto, string currentLocalUser)
        {
            var item = new RemoteComputerItemViewModel
            {
                PcName = dto != null ? dto.PcName : null,
                IpAddress = dto != null ? dto.IpAddress : null,
                SiteCode = dto != null ? dto.HospitalCode : null,
                CurrentLocalUser = currentLocalUser,
                StatusCode = RemotePcDbStatuses.Available
            };
            return item;
        }

        private void RaiseComputed()
        {
            RaisePropertyChanged("StatusDisplayName");
            RaisePropertyChanged("IsAvailable");
            RaisePropertyChanged("IsConnecting");
            RaisePropertyChanged("IsInUse");
            RaisePropertyChanged("IsCheckRequired");
            RaisePropertyChanged("IsOwnedByCurrentUser");
            RaisePropertyChanged("IsOwnedByOtherUser");
            RaisePropertyChanged("CanConnect");
            RaisePropertyChanged("OwnershipBadgeText");
            RaisePropertyChanged("HasOwnershipBadge");
            RaisePropertyChanged("PrimaryActionText");
            RaisePropertyChanged("IsPrimaryActionEnabled");
            RaisePropertyChanged("UserDisplayText");
            RaisePropertyChanged("OccupantText");
            RaisePropertyChanged("HasOccupant");
            RaisePropertyChanged("SimpleStatusText");
            RaisePropertyChanged("ClientPcDisplayText");
            RaisePropertyChanged("ConnectionStartedDisplayText");
            RaisePropertyChanged("PreviewStyleKey");
            RaisePropertyChanged("StatusBadgeStyleKey");
        }
    }
}
