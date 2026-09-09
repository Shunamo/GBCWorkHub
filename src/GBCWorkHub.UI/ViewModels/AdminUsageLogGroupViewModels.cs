using System;
using System.Collections.ObjectModel;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class AdminUsageLogDateGroupViewModel
    {
        public AdminUsageLogDateGroupViewModel(DateTime? date)
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
            Items = new ObservableCollection<AdminUsageLogItemViewModel>();
        }

        public DateTime Date { get; private set; }
        public bool IsUnknownDate { get; private set; }
        public ObservableCollection<AdminUsageLogItemViewModel> Items { get; private set; }

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

    public sealed class AdminUsageLogSiteGroupViewModel
    {
        public AdminUsageLogSiteGroupViewModel(string siteCode)
        {
            SiteCode = string.IsNullOrWhiteSpace(siteCode) ? "미지정" : siteCode.Trim().ToUpperInvariant();
            DateGroups = new ObservableCollection<AdminUsageLogDateGroupViewModel>();
        }

        public string SiteCode { get; private set; }
        public ObservableCollection<AdminUsageLogDateGroupViewModel> DateGroups { get; private set; }

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
