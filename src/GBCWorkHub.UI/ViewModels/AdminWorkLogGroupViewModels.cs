using System;
using System.Collections.ObjectModel;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminWorkLogItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public long DbLogId { get; set; }
        public string SiteCode { get; set; }
        public string AuthorName { get; set; }
        public string TeamName { get; set; }
        public string TicketNo { get; set; }
        public string MenuName { get; set; }
        public string TitleText { get; set; }
        public string PcName { get; set; }
        public string PersonInCharge { get; set; }
        public DateTime? WorkDate { get; set; }
        public string WriteStatus { get; set; }
        public WorkLogListItemViewModel SourceItem { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value, "IsSelected"); }
        }

        public string AuthorDisplay
        {
            get
            {
                string person = string.IsNullOrWhiteSpace(PersonInCharge)
                    ? null
                    : WorkLogDraftMapper.FormatPersonDisplay(PersonInCharge);
                if (string.IsNullOrWhiteSpace(person))
                    person = string.IsNullOrWhiteSpace(AuthorName) ? "-" : AuthorName.Trim();
                if (!string.IsNullOrWhiteSpace(TeamName))
                    return person + " · " + TeamName.Trim();
                return person;
            }
        }

        public string DateDisplay
        {
            get { return WorkDate.HasValue ? WorkDate.Value.ToString("MM-dd") : "-"; }
        }

        public string SiteDisplay
        {
            get
            {
                return string.IsNullOrWhiteSpace(SiteCode) ? "미지정" : SiteCode.Trim().ToUpperInvariant();
            }
        }

        public string Title
        {
            get
            {
                string title = string.IsNullOrWhiteSpace(TitleText) ? "(제목 없음)" : TitleText.Trim();
                if (title.Length > 90)
                    title = title.Substring(0, 90) + "…";
                return title;
            }
        }

        public string MetaLine
        {
            get
            {
                string menu = string.IsNullOrWhiteSpace(MenuName) ? "-" : MenuName.Trim();
                string ticket = string.IsNullOrWhiteSpace(TicketNo) ? "-" : TicketNo.Trim();
                string status = string.IsNullOrWhiteSpace(WriteStatus) ? "-" : WriteStatus.Trim();
                string pc = string.IsNullOrWhiteSpace(PcName) ? "-" : PcName.Trim();
                return menu + "  ·  #" + ticket + "  ·  " + status + "  ·  " + pc;
            }
        }

        public string Subtitle
        {
            get { return MetaLine; }
        }

        public bool MatchesSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return true;
            return Contains(PersonInCharge, q)
                || Contains(AuthorName, q)
                || Contains(TeamName, q)
                || Contains(SiteCode, q)
                || Contains(TicketNo, q)
                || Contains(MenuName, q)
                || Contains(TitleText, q)
                || Contains(PcName, q)
                || Contains(WriteStatus, q);
        }

        public bool MatchesType(string type)
        {
            if (string.IsNullOrWhiteSpace(type) || IsFilterAll(type))
                return true;
            if (SourceItem == null || SourceItem.Groups == null)
                return false;
            for (int i = 0; i < SourceItem.Groups.Count; i++)
            {
                var g = SourceItem.Groups[i];
                if (g != null && string.Equals(g.Type ?? string.Empty, type, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool MatchesCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || IsFilterAll(category))
                return true;
            if (SourceItem == null || SourceItem.Groups == null)
                return false;
            for (int i = 0; i < SourceItem.Groups.Count; i++)
            {
                var g = SourceItem.Groups[i];
                if (g != null && string.Equals(g.Category ?? string.Empty, category, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool MatchesTeam(string team)
        {
            if (string.IsNullOrWhiteSpace(team) || IsFilterAll(team))
                return true;
            return Contains(TeamName, team)
                || Contains(PersonInCharge, team)
                || Contains(AuthorName, team);
        }

        private static bool IsFilterAll(string value)
        {
            return string.Equals(value, "전체", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, WorkLogFieldMasters.Unselected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string hay, string needle)
        {
            return !string.IsNullOrWhiteSpace(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static AdminWorkLogItemViewModel FromListItem(WorkLogListItemViewModel item)
        {
            if (item == null)
                return null;
            DateTime? workDate = item.StartDate ?? item.CheckedInAt;
            if (!workDate.HasValue && item.LastModifiedAt != default(DateTime))
                workDate = item.LastModifiedAt;
            return new AdminWorkLogItemViewModel
            {
                DbLogId = item.DbLogId,
                SiteCode = item.SiteCode,
                AuthorName = item.AuthorName,
                TeamName = item.TeamName,
                TicketNo = item.TicketNo,
                MenuName = item.MenuName,
                TitleText = item.ListTitleText,
                PcName = item.Pc,
                PersonInCharge = item.PersonInCharge,
                WorkDate = workDate,
                WriteStatus = item.WriteStatus,
                SourceItem = item
            };
        }
    }

    public sealed class AdminWorkLogDateGroupViewModel
    {
        public AdminWorkLogDateGroupViewModel(DateTime? date)
        {
            if (date.HasValue)
            {
                Date = date.Value.Date;
                IsUnknownDate = false;
            }
            else
            {
                Date = DateTime.MinValue;
                IsUnknownDate = true;
            }
            Items = new ObservableCollection<AdminWorkLogItemViewModel>();
        }

        public DateTime Date { get; private set; }
        public bool IsUnknownDate { get; private set; }
        public ObservableCollection<AdminWorkLogItemViewModel> Items { get; private set; }

        public string DateHeader
        {
            get
            {
                if (IsUnknownDate)
                    return "일시 미확인";
                DateTime today = DateTime.Today;
                if (Date.Date == today)
                    return "오늘";
                if (Date.Date == today.AddDays(-1))
                    return "어제";
                return Date.ToString("yyyy년 M월 d일");
            }
        }

        public int Count
        {
            get { return Items != null ? Items.Count : 0; }
        }
    }

    public sealed class AdminWorkLogSiteGroupViewModel
    {
        public AdminWorkLogSiteGroupViewModel(string siteCode)
        {
            SiteCode = string.IsNullOrWhiteSpace(siteCode) ? "미지정" : siteCode.Trim().ToUpperInvariant();
            DateGroups = new ObservableCollection<AdminWorkLogDateGroupViewModel>();
        }

        public string SiteCode { get; private set; }
        public ObservableCollection<AdminWorkLogDateGroupViewModel> DateGroups { get; private set; }

        public int Count
        {
            get
            {
                int n = 0;
                if (DateGroups == null)
                    return 0;
                for (int i = 0; i < DateGroups.Count; i++)
                {
                    if (DateGroups[i] != null)
                        n += DateGroups[i].Count;
                }
                return n;
            }
        }

        public string Header
        {
            get { return SiteCode + " · " + Count + "건"; }
        }
    }
}
