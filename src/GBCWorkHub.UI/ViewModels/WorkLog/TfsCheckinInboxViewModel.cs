using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.ViewModels.WorkLog
{
    public sealed class TfsCheckinInboxRowViewModel : ViewModelBase
    {
        private bool _isChecked;

        public TfsCheckinInboxRecord Source { get; set; }
        public int ChangesetId { get; set; }
        public string Status { get; set; }
        public string StatusLabel { get; set; }
        public string SiteCode { get; set; }
        public string PcName { get; set; }
        public string AuthorName { get; set; }
        public string Comment { get; set; }
        public string CheckedInAtDisplay { get; set; }
        public string FetchedAtDisplay { get; set; }
        public int ChangedFileCount { get; set; }
        public Action CheckChanged { get; set; }

        public bool IsChecked
        {
            get { return _isChecked; }
            set
            {
                if (SetProperty(ref _isChecked, value) && CheckChanged != null)
                    CheckChanged();
            }
        }

        public bool IsWaiting
        {
            get { return string.Equals(Status, TfsCheckinInboxStatus.Waiting, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSkipped
        {
            get { return string.Equals(Status, TfsCheckinInboxStatus.Skipped, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsReported
        {
            get { return TfsCheckinInboxStatus.IsReported(Status); }
        }

        public static TfsCheckinInboxRowViewModel FromRecord(TfsCheckinInboxRecord record)
        {
            if (record == null)
                return null;
            return new TfsCheckinInboxRowViewModel
            {
                Source = record,
                ChangesetId = record.ChangesetId,
                Status = record.Status ?? TfsCheckinInboxStatus.Waiting,
                StatusLabel = TfsCheckinInboxStatus.ToLabel(record.Status),
                SiteCode = TfsCheckinInboxStore.ResolveSiteCode(record),
                PcName = record.PcName ?? string.Empty,
                AuthorName = record.AuthorName ?? record.AuthorId ?? string.Empty,
                Comment = record.Comment ?? string.Empty,
                CheckedInAtDisplay = FormatCheckedInAt(record),
                FetchedAtDisplay = record.UpdatedAt == default(DateTime)
                    ? "-"
                    : KoreaTime.Format(record.UpdatedAt, "MM-dd HH:mm"),
                ChangedFileCount = record.ChangedFileCount
            };
        }

        private static string FormatCheckedInAt(TfsCheckinInboxRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.CheckedInAt))
                return "-";
            string site = TfsCheckinInboxStore.ResolveFolderSite(record.SiteCode, record.PcName);
            DateTime? korea = KoreaTime.ParseToKorea(record.CheckedInAt, site);
            return korea.HasValue ? korea.Value.ToString("yyyy-MM-dd HH:mm") : record.CheckedInAt;
        }
    }

    public sealed class TfsCheckinInboxSiteFolderViewModel : ViewModelBase
    {
        public string SiteCode { get; set; }
        public int WaitingCount { get; set; }
        public int TotalCount { get; set; }
        public bool IsSelected { get; set; }

        public string CountLabel
        {
            get { return WaitingCount > 0 ? WaitingCount.ToString() : string.Empty; }
        }
    }

    public sealed class TfsCheckinInboxViewModel : ViewModelBase
    {
        public const string FilterAll = "all";

        private string _filterStatus = TfsCheckinInboxStatus.Waiting;
        private string _selectedSiteCode = TfsCheckinInboxStore.AllSites;
        private TfsCheckinInboxRowViewModel _selected;
        private bool _isFilterMenuOpen;
        private string _searchText = string.Empty;
        private int _checkedCount;
        private int _checkedWaitingCount;
        private int _checkedSkippedCount;
        private int _checkedWritableCount;

        public TfsCheckinInboxViewModel()
        {
            Rows = new ObservableCollection<TfsCheckinInboxRowViewModel>();
            Sites = new ObservableCollection<TfsCheckinInboxSiteFolderViewModel>();
            SetFilterCommand = new RelayCommand<string>(SetFilter);
            SelectSiteCommand = new RelayCommand<string>(SelectSite);
            SkipCheckedCommand = new RelayCommand(SkipChecked, () => HasCheckedWaiting);
            WaitCheckedCommand = new RelayCommand(WaitChecked, () => HasCheckedSkipped);
            WriteCheckedCommand = new RelayCommand(WriteChecked, () => HasCheckedWritable);
            ToggleFilterMenuCommand = new RelayCommand(ToggleFilterMenu);
        }

        public ObservableCollection<TfsCheckinInboxRowViewModel> Rows { get; private set; }
        public ObservableCollection<TfsCheckinInboxSiteFolderViewModel> Sites { get; private set; }

        public ICommand SetFilterCommand { get; private set; }
        public ICommand SelectSiteCommand { get; private set; }
        public ICommand SkipCheckedCommand { get; private set; }
        public ICommand WaitCheckedCommand { get; private set; }
        public ICommand WriteCheckedCommand { get; private set; }
        public ICommand ToggleFilterMenuCommand { get; private set; }

        public Action<IList<TfsCheckinInboxRecord>> WriteRequested { get; set; }

        public string FilterStatus
        {
            get { return _filterStatus; }
            set
            {
                if (SetProperty(ref _filterStatus, value ?? TfsCheckinInboxStatus.Waiting))
                {
                    RaisePropertyChanged("IsFilterAll");
                    RaisePropertyChanged("IsFilterWaiting");
                    RaisePropertyChanged("IsFilterSkipped");
                    RaisePropertyChanged("IsFilterReported");
                    RaisePropertyChanged("HasNonDefaultFilter");
                    Reload();
                }
            }
        }

        public string SelectedSiteCode
        {
            get { return _selectedSiteCode; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value)
                    ? TfsCheckinInboxStore.AllSites
                    : value.Trim();
                if (string.Equals(next, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(next, "OTHER", StringComparison.OrdinalIgnoreCase))
                    next = TfsCheckinInboxStore.AllSites;
                if (SetProperty(ref _selectedSiteCode, next))
                    Reload();
            }
        }

        public bool IsFilterAll { get { return FilterStatus == FilterAll; } }
        public bool IsFilterWaiting { get { return FilterStatus == TfsCheckinInboxStatus.Waiting; } }
        public bool IsFilterSkipped { get { return FilterStatus == TfsCheckinInboxStatus.Skipped; } }
        public bool IsFilterReported { get { return FilterStatus == TfsCheckinInboxStatus.Reported; } }
        public bool HasNonDefaultFilter { get { return !IsFilterWaiting; } }

        public bool IsFilterMenuOpen
        {
            get { return _isFilterMenuOpen; }
            set { SetProperty(ref _isFilterMenuOpen, value); }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    Reload();
            }
        }

        public int WaitingCount { get; private set; }
        public int SkippedCount { get; private set; }
        public int ReportedCount { get; private set; }
        public int SiteWaitingCount { get; private set; }
        public int SiteSkippedCount { get; private set; }
        public int SiteReportedCount { get; private set; }
        public bool HasRows { get { return Rows.Count > 0; } }

        public bool HasCheckedRows { get { return _checkedCount > 0; } }
        public bool HasCheckedWaiting { get { return _checkedWaitingCount > 0; } }
        public bool HasCheckedSkipped { get { return _checkedSkippedCount > 0; } }
        public bool HasCheckedWritable { get { return _checkedWritableCount > 0; } }

        public TfsCheckinInboxRowViewModel Selected
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); }
        }

        public void Reload()
        {
            var keepChecked = new HashSet<int>();
            foreach (var row in Rows)
            {
                if (row != null && row.IsChecked && row.ChangesetId > 0)
                    keepChecked.Add(row.ChangesetId);
            }

            TfsCheckinInboxStore.EnsureSiteCodes();
            var all = TfsCheckinInboxStore.LoadMine();
            WaitingCount = CountByStatus(all, TfsCheckinInboxStatus.Waiting);
            SkippedCount = CountByStatus(all, TfsCheckinInboxStatus.Skipped);
            ReportedCount = CountByStatus(all, TfsCheckinInboxStatus.Reported);

            if (string.IsNullOrWhiteSpace(_selectedSiteCode)
                || string.Equals(_selectedSiteCode, TfsCheckinInboxStore.OtherSite, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_selectedSiteCode, "OTHER", StringComparison.OrdinalIgnoreCase))
                _selectedSiteCode = TfsCheckinInboxStore.AllSites;

            RebuildSites(all);

            var inSite = new List<TfsCheckinInboxRecord>();
            bool allSites = string.Equals(
                _selectedSiteCode, TfsCheckinInboxStore.AllSites, StringComparison.OrdinalIgnoreCase);
            foreach (var record in all)
            {
                if (record == null)
                    continue;
                if (!allSites
                    && !string.Equals(
                        TfsCheckinInboxStore.ResolveSiteCode(record),
                        _selectedSiteCode,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                inSite.Add(record);
            }

            SiteWaitingCount = CountByStatus(inSite, TfsCheckinInboxStatus.Waiting);
            SiteSkippedCount = CountByStatus(inSite, TfsCheckinInboxStatus.Skipped);
            SiteReportedCount = CountByStatus(inSite, TfsCheckinInboxStatus.Reported);

            var filtered = new List<TfsCheckinInboxRecord>();
            string search = (_searchText ?? string.Empty).Trim();
            foreach (var record in inSite)
            {
                if (FilterStatus != FilterAll
                    && !TfsCheckinInboxStatus.MatchesStatus(record.Status, FilterStatus))
                    continue;
                if (search.Length > 0 && !MatchesInboxSearch(record, search))
                    continue;
                filtered.Add(record);
            }
            filtered.Sort(CompareInboxOrder);

            Rows.Clear();
            foreach (var record in filtered)
            {
                var row = TfsCheckinInboxRowViewModel.FromRecord(record);
                if (row == null)
                    continue;
                row.CheckChanged = RefreshCheckedState;
                if (keepChecked.Contains(row.ChangesetId))
                    row.IsChecked = true;
                Rows.Add(row);
            }

            RaisePropertyChanged("WaitingCount");
            RaisePropertyChanged("SkippedCount");
            RaisePropertyChanged("ReportedCount");
            RaisePropertyChanged("SiteWaitingCount");
            RaisePropertyChanged("SiteSkippedCount");
            RaisePropertyChanged("SiteReportedCount");
            RaisePropertyChanged("HasRows");
            RaisePropertyChanged("SelectedSiteCode");
            RefreshCheckedState();
        }

        private void RebuildSites(IList<TfsCheckinInboxRecord> all)
        {
            Sites.Clear();
            Sites.Add(new TfsCheckinInboxSiteFolderViewModel
            {
                SiteCode = TfsCheckinInboxStore.AllSites,
                WaitingCount = CountByStatus(all, TfsCheckinInboxStatus.Waiting),
                TotalCount = all != null ? all.Count : 0,
                IsSelected = string.Equals(_selectedSiteCode, TfsCheckinInboxStore.AllSites, StringComparison.OrdinalIgnoreCase)
            });
            foreach (string site in TfsCheckinInboxStore.FolderSiteCodes)
                Sites.Add(BuildFolder(site, all));
        }

        private TfsCheckinInboxSiteFolderViewModel BuildFolder(string site, IList<TfsCheckinInboxRecord> all)
        {
            int waiting = 0;
            int total = 0;
            foreach (var record in all)
            {
                if (record == null)
                    continue;
                if (!string.Equals(TfsCheckinInboxStore.ResolveSiteCode(record), site, StringComparison.OrdinalIgnoreCase))
                    continue;
                total++;
                if (TfsCheckinInboxStatus.MatchesStatus(record.Status, TfsCheckinInboxStatus.Waiting))
                    waiting++;
            }
            return new TfsCheckinInboxSiteFolderViewModel
            {
                SiteCode = site,
                WaitingCount = waiting,
                TotalCount = total,
                IsSelected = string.Equals(_selectedSiteCode, site, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static int CountByStatus(IList<TfsCheckinInboxRecord> records, string status)
        {
            int n = 0;
            if (records == null)
                return 0;
            foreach (var record in records)
            {
                if (record == null)
                    continue;
                if (TfsCheckinInboxStatus.MatchesStatus(record.Status, status))
                    n++;
            }
            return n;
        }

        private static int CompareInboxOrder(TfsCheckinInboxRecord a, TfsCheckinInboxRecord b)
        {
            int sa = StatusRank(a != null ? a.Status : null);
            int sb = StatusRank(b != null ? b.Status : null);
            if (sa != sb)
                return sa.CompareTo(sb);
            return RecentStamp(b).CompareTo(RecentStamp(a));
        }

        private static int StatusRank(string status)
        {
            if (string.Equals(status, TfsCheckinInboxStatus.Waiting, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (TfsCheckinInboxStatus.IsReported(status))
                return 2;
            if (string.Equals(status, TfsCheckinInboxStatus.Skipped, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 3;
        }

        private static DateTime RecentStamp(TfsCheckinInboxRecord record)
        {
            if (record == null)
                return DateTime.MinValue;
            if (record.UpdatedAt != default(DateTime))
                return record.UpdatedAt;
            return DateTime.MinValue;
        }

        private static bool MatchesInboxSearch(TfsCheckinInboxRecord record, string query)
        {
            if (record == null || string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return ContainsIgnoreCase(record.Comment, q)
                || ContainsIgnoreCase(record.PcName, q)
                || ContainsIgnoreCase(record.AuthorName, q)
                || ContainsIgnoreCase(record.AuthorId, q)
                || record.ChangesetId.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsIgnoreCase(string hay, string needle)
        {
            return !string.IsNullOrEmpty(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetFilter(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                FilterStatus = TfsCheckinInboxStatus.Waiting;
            else
                FilterStatus = status.Trim();
        }

        private void ToggleFilterMenu()
        {
            IsFilterMenuOpen = !IsFilterMenuOpen;
        }

        private void SelectSite(string site)
        {
            SelectedSiteCode = string.IsNullOrWhiteSpace(site)
                ? TfsCheckinInboxStore.AllSites
                : site;
        }

        private void RefreshCheckedState()
        {
            int checkedCount = 0;
            int waiting = 0;
            int skipped = 0;
            int writable = 0;
            foreach (var row in Rows)
            {
                if (row == null || !row.IsChecked)
                    continue;
                checkedCount++;
                if (row.IsWaiting)
                    waiting++;
                if (row.IsSkipped)
                    skipped++;
                if (!row.IsReported)
                    writable++;
            }
            _checkedCount = checkedCount;
            _checkedWaitingCount = waiting;
            _checkedSkippedCount = skipped;
            _checkedWritableCount = writable;
            RaisePropertyChanged("HasCheckedRows");
            RaisePropertyChanged("HasCheckedWaiting");
            RaisePropertyChanged("HasCheckedSkipped");
            RaisePropertyChanged("HasCheckedWritable");
            var skipCmd = SkipCheckedCommand as RelayCommand;
            var waitCmd = WaitCheckedCommand as RelayCommand;
            var writeCmd = WriteCheckedCommand as RelayCommand;
            if (skipCmd != null)
                skipCmd.RaiseCanExecuteChanged();
            if (waitCmd != null)
                waitCmd.RaiseCanExecuteChanged();
            if (writeCmd != null)
                writeCmd.RaiseCanExecuteChanged();
        }

        private List<TfsCheckinInboxRowViewModel> GetCheckedRows()
        {
            var list = new List<TfsCheckinInboxRowViewModel>();
            foreach (var row in Rows)
            {
                if (row != null && row.IsChecked)
                    list.Add(row);
            }
            return list;
        }

        private void SkipChecked()
        {
            var ids = new List<int>();
            foreach (var row in GetCheckedRows())
            {
                if (row.IsWaiting && row.ChangesetId > 0)
                    ids.Add(row.ChangesetId);
            }
            if (ids.Count == 0)
                return;
            TfsCheckinInboxStore.MarkSkipped(ids);
            Reload();
        }

        private void WaitChecked()
        {
            var ids = new List<int>();
            foreach (var row in GetCheckedRows())
            {
                if (row.IsSkipped && row.ChangesetId > 0)
                    ids.Add(row.ChangesetId);
            }
            if (ids.Count == 0)
                return;
            TfsCheckinInboxStore.MarkWaiting(ids);
            Reload();
        }

        private void WriteChecked()
        {
            if (WriteRequested == null)
                return;
            var records = new List<TfsCheckinInboxRecord>();
            foreach (var row in GetCheckedRows())
            {
                if (row == null || row.Source == null || row.IsReported)
                    continue;
                records.Add(row.Source);
            }
            if (records.Count == 0)
                return;
            WriteRequested(records);
        }
    }
}
