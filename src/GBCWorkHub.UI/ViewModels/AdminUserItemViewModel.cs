using System;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminUserItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public long UserId { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string TeamName { get; set; }
        public string LocalPcName { get; set; }
        public string LocalPcIp { get; set; }
        public string WindowsAccount { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsBuiltInAdmin { get; set; }

        /// <summary>원본 이름/소속/로그인ID — 표시용 Title/TeamName은 "-" 채움/조합 표기라 편집 팝업에는 이 값들을 쓴다.</summary>
        public string RawUserName { get; set; }
        public string RawTeamName { get; set; }
        public string RawLoginId { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value, "IsSelected"); }
        }

        public bool CanManageProfile
        {
            get { return !IsBuiltInAdmin; }
        }

        public string ActionLabel
        {
            get { return IsActive ? "비활성" : "활성"; }
        }

        public string StatusLabel
        {
            get { return IsActive ? "활성" : "비활성"; }
        }

        public bool MatchesSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return true;
            return Contains(Title, q)
                || Contains(Key, q)
                || Contains(TeamName, q)
                || Contains(LocalPcName, q)
                || Contains(LocalPcIp, q)
                || Contains(WindowsAccount, q);
        }

        private static bool Contains(string hay, string needle)
        {
            return !string.IsNullOrWhiteSpace(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static AdminUserItemViewModel FromDto(DirectoryUserDto dto)
        {
            string login = dto.LoginId;
            string name = dto.UserName;
            string key = !string.IsNullOrWhiteSpace(login) ? login.Trim() : (name ?? string.Empty).Trim();
            bool isAdmin = AuthBiz.IsAdminLoginId(login)
                || string.Equals(login, AuthBiz.AdminDisplayName, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(login)
                    && (AuthBiz.IsAdminLoginId(name)
                        || string.Equals(name, AuthBiz.AdminDisplayName, StringComparison.Ordinal)));
            string title = string.IsNullOrWhiteSpace(name) ? key : name.Trim();
            if (!string.IsNullOrWhiteSpace(login) && !string.Equals(login, name, StringComparison.OrdinalIgnoreCase))
                title = title + " (" + login.Trim() + ")";

            string team = string.IsNullOrWhiteSpace(dto.TeamName) ? "-" : dto.TeamName.Trim();
            string pc = string.IsNullOrWhiteSpace(dto.LocalPcName) ? "-" : dto.LocalPcName.Trim();
            string ip = string.IsNullOrWhiteSpace(dto.LocalPcIp) ? "-" : dto.LocalPcIp.Trim();
            string status = dto.IsActive ? "활성" : "비활성";
            return new AdminUserItemViewModel
            {
                UserId = dto.UserId.GetValueOrDefault(),
                Key = key,
                Title = title,
                TeamName = team,
                LocalPcName = pc,
                LocalPcIp = ip,
                WindowsAccount = dto.WindowsAccount,
                Subtitle = team + " · " + pc + " · " + ip + " · " + status,
                IsActive = dto.IsActive,
                IsDeleted = dto.IsDeleted,
                IsBuiltInAdmin = isAdmin,
                RawUserName = dto.UserName,
                RawTeamName = dto.TeamName,
                RawLoginId = dto.LoginId
            };
        }
    }
}
