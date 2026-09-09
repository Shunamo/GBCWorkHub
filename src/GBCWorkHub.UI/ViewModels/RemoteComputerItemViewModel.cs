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
        private string _hostAddress;
        private string _groupName;
        private string _pcDomain;
        private string _pcNote;
        private string _pcComment;
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
                {
                    RaisePropertyChanged("DisplayTitle");
                    RaisePropertyChanged("DisplaySubtitle");
                    RaisePropertyChanged("HasDisplaySubtitle");
                }
            }
        }

        public string IpAddress
        {
            get { return _ipAddress; }
            set
            {
                if (SetProperty(ref _ipAddress, value))
                {
                    RaisePropertyChanged("DisplaySubtitle");
                    RaisePropertyChanged("HasDisplaySubtitle");
                }
            }
        }

        public string HostAddress
        {
            get { return _hostAddress; }
            set
            {
                if (SetProperty(ref _hostAddress, value))
                {
                    RaisePropertyChanged("DisplaySubtitle");
                    RaisePropertyChanged("HasDisplaySubtitle");
                }
            }
        }

        public string GroupName
        {
            get { return _groupName; }
            set { SetProperty(ref _groupName, value); }
        }

        /// <summary>원격 Windows 로그인(엑셀 ID). 예: dxbcmc\bcarep.admin</summary>
        public string PcDomain
        {
            get { return _pcDomain; }
            set
            {
                if (SetProperty(ref _pcDomain, value))
                {
                    RaisePropertyChanged("HasPcDomain");
                    RaisePropertyChanged("HasMultiplePcDomains");
                    RaisePropertyChanged("HasSinglePcDomain");
                    RaisePropertyChanged("PcDomainDisplay");
                }
            }
        }

        /// <summary>카드/목록용. 1개면 한 줄, 2개 이상이면 줄바꿈.</summary>
        public string PcDomainDisplay
        {
            get
            {
                System.Collections.Generic.List<string> list = GetPcDomainParts();
                if (list == null || list.Count == 0)
                    return null;
                if (list.Count == 1)
                    return list[0];
                return string.Join("\n", list.ToArray());
            }
        }

        public bool HasPcDomain
        {
            get { return !string.IsNullOrWhiteSpace(PcDomainDisplay); }
        }

        public bool HasMultiplePcDomains
        {
            get
            {
                System.Collections.Generic.List<string> list = GetPcDomainParts();
                return list != null && list.Count > 1;
            }
        }

        public bool HasSinglePcDomain
        {
            get
            {
                System.Collections.Generic.List<string> list = GetPcDomainParts();
                return list != null && list.Count == 1;
            }
        }

        private System.Collections.Generic.List<string> GetPcDomainParts()
        {
            if (string.IsNullOrWhiteSpace(PcDomain))
                return null;
            string[] parts = PcDomain.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim();
                if (t.Length > 0)
                    list.Add(t);
            }
            return list.Count == 0 ? null : list;
        }

        /// <summary>엑셀 ID/Password 섹션([ID]/[Password]).</summary>
        public string PcNote
        {
            get { return _pcNote; }
            set
            {
                if (SetProperty(ref _pcNote, value))
                    RaisePropertyChanged("HasPcNote");
            }
        }

        public bool HasPcNote
        {
            get { return !string.IsNullOrWhiteSpace(PcNote); }
        }

        /// <summary>접속 코멘트(사용자 수정 가능).</summary>
        public string PcComment
        {
            get { return _pcComment; }
            set { SetProperty(ref _pcComment, value); }
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
            get
            {
                if (string.IsNullOrWhiteSpace(IpAddress) && string.IsNullOrWhiteSpace(HostAddress))
                    return string.Empty;
                string ip = !string.IsNullOrWhiteSpace(IpAddress) ? IpAddress.Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(PcName)
                    && string.Equals(ip, PcName.Trim(), StringComparison.OrdinalIgnoreCase))
                    ip = string.Empty;
                if (!LooksLikeIpv4(ip) && LooksLikeIpv4(HostAddress))
                    ip = HostAddress.Trim();
                if (!LooksLikeIpv4(ip))
                    return string.Empty;
                return ip;
            }
        }

        public bool HasDisplaySubtitle
        {
            get { return !string.IsNullOrWhiteSpace(DisplaySubtitle); }
        }

        private static bool LooksLikeIpv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0 || n > 255)
                    return false;
            }
            return true;
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
                if (OccupancyNameStore.IsLocalOccupant(AccessUserId, AccessPcName))
                    return true;
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
                    return OccupancyNameStore.HasName ? "원격 접속" : "로그인 후 접속";
                if (IsConnecting && IsOwnedByCurrentUser)
                    return "연결 중...";
                if (IsInUse && IsOwnedByCurrentUser)
                    return "사용 중";
                if (IsOwnedByOtherUser)
                    return OccupancyNameStore.HasName ? "점유 가져가기" : "로그인 후 접속";
                if (IsCheckRequired)
                    return "상태 확인";
                return StatusDisplayName;
            }
        }

        public bool IsPrimaryActionEnabled
        {
            get
            {
                if (CanConnect || IsCheckRequired || IsOwnedByOtherUser)
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
                return "사용자: " + OccupancyNameStore.ToDisplayName(AccessUserId, AccessPcName);
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
                return OccupancyNameStore.ToDisplayName(AccessUserId, AccessPcName);
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
                return "시작: " + KoreaTime.Format(AccessStartAt, "HH:mm");
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
            return OccupancyNameStore.ToDisplayName(userId, null);
        }

        public static string FormatUserId(string userId, string accessPcName)
        {
            return OccupancyNameStore.ToDisplayName(userId, accessPcName);
        }

        public void ApplyFromDb(RemotePcStatus status, string currentLocalUser)
        {
            CurrentLocalUser = currentLocalUser;
            if (status == null)
                return;

            // 점유 행은 상태만 반영. PC명·점유키·사이트를 덮으면 다른 PC 카드로 잘못 매핑됨.
            if (string.IsNullOrWhiteSpace(PcName) && !string.IsNullOrWhiteSpace(status.RemotePcName))
                PcName = status.RemotePcName.Trim();
            if (string.IsNullOrWhiteSpace(IpAddress) && !string.IsNullOrWhiteSpace(status.RemoteAccessIpAddress))
                IpAddress = status.RemoteAccessIpAddress.Trim();
            if (string.IsNullOrWhiteSpace(SiteCode) && !string.IsNullOrWhiteSpace(status.SiteCode))
                SiteCode = status.SiteCode.Trim().ToUpperInvariant();

            StatusCode = string.IsNullOrWhiteSpace(status.AccessStatusCode)
                ? RemotePcDbStatuses.Available
                : status.AccessStatusCode;
            AccessUserId = status.AccessUserId;
            AccessPcName = status.AccessPcName;
            SessionToken = status.SessionToken;
            AccessStartAt = KoreaTime.FromOccupancyDb(status.AccessStartDateTime);
            RemoteAccessAt = KoreaTime.FromOccupancyDb(status.RemoteAccessDateTime);
            UpdatedAt = KoreaTime.FromOccupancyDb(status.UpdatedDateTime);
        }

        public void ApplyLocalInUse(string userId, string clientPc, string sessionToken)
        {
            CurrentLocalUser = userId;
            StatusCode = RemotePcDbStatuses.InUse;
            AccessUserId = userId;
            AccessPcName = clientPc;
            SessionToken = sessionToken;
            AccessStartAt = KoreaTime.Now;
            RemoteAccessAt = KoreaTime.Now;
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
                HostAddress = dto != null ? dto.HostAddress : null,
                GroupName = dto != null ? dto.GroupName : null,
                PcDomain = dto != null ? dto.PcDomain : null,
                PcNote = dto != null ? dto.PcNote : null,
                PcComment = dto != null ? dto.PcComment : null,
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

        public void RefreshComputedUi()
        {
            RaiseComputed();
        }
    }
}
