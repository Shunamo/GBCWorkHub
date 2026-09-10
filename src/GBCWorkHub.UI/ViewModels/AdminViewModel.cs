using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.WorkLog;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.ViewModels.WorkLog;

namespace GBCWorkHub.UI.ViewModels
{
public sealed class AdminViewModel : ViewModelBase
{
	private IPopupService _popup;
	private Action<WorkLogListItemViewModel> _openWorkLog;
	private WorkLogListViewModel _workLogListHost;

	private DateTime _calendarMonth = new DateTime(KoreaTime.Today.Year, KoreaTime.Today.Month, 1);
	private DateTime? _draftStartDate;
	private DateTime? _draftEndDate;
	private bool _isWorkLogDatePickerOpen;

	private DateTime _usageLogCalendarMonth = new DateTime(KoreaTime.Today.Year, KoreaTime.Today.Month, 1);
	private DateTime? _usageLogDraftStartDate;
	private DateTime? _usageLogDraftEndDate;
	private DateTime? _usageLogFilterFrom;
	private DateTime? _usageLogFilterTo;
	private bool _isUsageLogDatePickerOpen;
	/// <summary>접속 이력 탭을 보고 있는 동안만 주기적으로 새로고침 — "사용 중" 상태가
	/// 실시간으로 갱신되고, 새로 시작된 세션도 곧바로 목록에 나타나게 한다.</summary>
	private DispatcherTimer _usageLogRefreshTimer;

	private string _selectedSection = "Pcs";

	private string _adminStatusMessage = string.Empty;

	private string _adminPcStatusMessage = string.Empty;

	private string _occupancyStatusMessage = string.Empty;

	private string _usageLogStatusMessage = string.Empty;

	private string _userSearchText = string.Empty;

	private string _pcSearchText = string.Empty;

	private string _occupancySearchText = string.Empty;

	private string _usageLogSearchText = string.Empty;

	private string _usageLogSiteFilter = "ALL";

	private string _workLogSearchText = string.Empty;

	private string _workLogStatusMessage = string.Empty;

	private string _workLogSiteFilter = "ALL";

	private string _adminPcSiteFilter = "ALL";

	private string _adminDraftSite = "AURORA";

	private string _adminDraftPcName = string.Empty;

	private string _adminDraftPcIp = string.Empty;

	private string _adminDraftShareKey = string.Empty;

	private string _adminDraftTeamName = string.Empty;

	private string _adminDraftPcDomain = string.Empty;

	private string _adminDraftDomainId = string.Empty;

	private string _adminDraftDomainPw = string.Empty;

	private string _adminDraftExtraId = string.Empty;

	private string _adminDraftExtraPw = string.Empty;

	private string _adminDraftPcComment = string.Empty;

	private bool _adminDraftAgentInstalled;

	private string _adminOriginalPcName;

	private AdminPcItemViewModel _selectedAdminPc;

	private AdminUserItemViewModel _selectedAdminUser;

	private AdminOccupancyItemViewModel _selectedOccupancy;

	private AdminUsageLogItemViewModel _selectedUsageLog;

	private AdminWorkLogItemViewModel _selectedWorkLog;

	private bool _suppressPcListSelection;

	private bool _hasPcDetail;

	private int _userActiveCount;

	private int _userInactiveCount;

	private int _pcCount;

	private int _occupancyBusyCount;

	private int _usageLogCount;

	private int _workLogCount;

	private readonly List<AdminUserItemViewModel> _allUsers = new List<AdminUserItemViewModel>();

	private readonly List<AdminPcItemViewModel> _allPcs = new List<AdminPcItemViewModel>();

	private readonly List<AdminOccupancyItemViewModel> _allOccupancies = new List<AdminOccupancyItemViewModel>();

	private readonly List<AdminUsageLogItemViewModel> _allUsageLogs = new List<AdminUsageLogItemViewModel>();

	private readonly List<AdminWorkLogItemViewModel> _allWorkLogs = new List<AdminWorkLogItemViewModel>();

	private static readonly string[] HistorySites = new string[4] { "AURORA", "CMC", "RC", "MNGHA" };

	private static readonly string[] OccupancyStatSites = new string[4] { "AURORA", "CMC", "RC", "MNGHA" };

	public ObservableCollection<AdminUserItemViewModel> AdminUsers { get; private set; }

	public ObservableCollection<AdminPcItemViewModel> AdminPcs { get; private set; }

	public ObservableCollection<AdminPcGroupViewModel> AdminPcGroups { get; private set; }

	public ObservableCollection<AdminOccupancyItemViewModel> Occupancies { get; private set; }

	public ObservableCollection<AdminOccupancyStatRowViewModel> OccupancyStats { get; private set; }

	public ObservableCollection<AdminUsageLogItemViewModel> UsageLogs { get; private set; }

	public ObservableCollection<AdminUsageLogSiteGroupViewModel> UsageLogSiteGroups { get; private set; }

	public ObservableCollection<AdminWorkLogItemViewModel> WorkLogs { get; private set; }

	public ObservableCollection<AdminWorkLogSiteGroupViewModel> WorkLogSiteGroups { get; private set; }

	public ObservableCollection<AdminSiteChipItemViewModel> UsageLogSites { get; private set; }

	public ObservableCollection<AdminSiteChipItemViewModel> WorkLogSites { get; private set; }

	public ObservableCollection<AdminSiteChipItemViewModel> AdminPcSites { get; private set; }

	public ICommand SelectSectionCommand { get; private set; }

	public ICommand ReloadAdminUsersCommand { get; private set; }

	public ICommand ManageAdminUserCommand { get; private set; }

	public ICommand SelectAdminUserCommand { get; private set; }

	public ICommand ReloadAdminPcsCommand { get; private set; }

	public ICommand SelectAdminPcCommand { get; private set; }

	public ICommand NewAdminPcCommand { get; private set; }

	public ICommand SaveAdminPcCommand { get; private set; }

	public ICommand DeleteAdminPcCommand { get; private set; }

	public ICommand SelectAdminPcSiteCommand { get; private set; }

	public ICommand ReloadOccupancyCommand { get; private set; }

	public ICommand SelectOccupancyCommand { get; private set; }

	public ICommand ForceReleaseOccupancyCommand { get; private set; }

	public ICommand ReloadUsageLogsCommand { get; private set; }

	public ICommand SelectUsageLogCommand { get; private set; }

	public ICommand EditUsageLogCommand { get; private set; }

	public ICommand DeleteUsageLogCommand { get; private set; }

	public ICommand SelectUsageLogSiteCommand { get; private set; }

	public ICommand ReloadWorkLogsCommand { get; private set; }

	public ICommand SelectWorkLogCommand { get; private set; }

	public ICommand ViewWorkLogCommand { get; private set; }

	public ICommand DeleteWorkLogCommand { get; private set; }

	public ICommand SelectWorkLogSiteCommand { get; private set; }

	public ICommand OpenWorkLogDatePickerCommand { get; private set; }

	public ICommand CloseWorkLogDatePickerCommand { get; private set; }

	public ICommand PrevWorkLogCalendarMonthCommand { get; private set; }

	public ICommand NextWorkLogCalendarMonthCommand { get; private set; }

	public ICommand SelectWorkLogCalendarDayCommand { get; private set; }

	public ICommand ApplyWorkLogDateFilterCommand { get; private set; }

	public ICommand ClearWorkLogDateFilterCommand { get; private set; }

	public ICommand OpenUsageLogDatePickerCommand { get; private set; }

	public ICommand CloseUsageLogDatePickerCommand { get; private set; }

	public ICommand PrevUsageLogCalendarMonthCommand { get; private set; }

	public ICommand NextUsageLogCalendarMonthCommand { get; private set; }

	public ICommand SelectUsageLogCalendarDayCommand { get; private set; }

	public ICommand ApplyUsageLogDateFilterCommand { get; private set; }

	public ICommand ClearUsageLogDateFilterCommand { get; private set; }

	public ObservableCollection<CalendarDayItem> UsageLogCalendarDays { get; private set; }

	public WorkLogListViewModel WorkLogListHost
	{
		get { return _workLogListHost; }
		private set { SetProperty(ref _workLogListHost, value, "WorkLogListHost"); }
	}

	public ObservableCollection<CalendarDayItem> WorkLogCalendarDays { get; private set; }

	public string DisplayName
	{
		get
		{
			string displayName = OccupancyNameStore.DisplayName;
			return string.IsNullOrWhiteSpace(displayName) ? AuthBiz.AdminDisplayName : displayName;
		}
	}

	public string SelectedSection
	{
		get
		{
			return _selectedSection;
		}
		private set
		{
			if (SetProperty(ref _selectedSection, value, "SelectedSection"))
			{
				RaisePropertyChanged("IsUsersSection");
				RaisePropertyChanged("IsPcsSection");
				RaisePropertyChanged("IsOccupancySection");
				RaisePropertyChanged("IsUsageLogsSection");
				RaisePropertyChanged("IsWorkLogsSection");
				RaisePropertyChanged("IsRequestsSection");
				RaisePropertyChanged("SectionTitle");
			}
		}
	}

	public bool IsUsersSection => string.Equals(SelectedSection, "Users", StringComparison.OrdinalIgnoreCase);

	public bool IsPcsSection => string.Equals(SelectedSection, "Pcs", StringComparison.OrdinalIgnoreCase);

	public bool IsOccupancySection => string.Equals(SelectedSection, "Occupancy", StringComparison.OrdinalIgnoreCase);

	public bool IsUsageLogsSection => string.Equals(SelectedSection, "UsageLogs", StringComparison.OrdinalIgnoreCase);

	public bool IsWorkLogsSection => string.Equals(SelectedSection, "WorkLogs", StringComparison.OrdinalIgnoreCase);

	public bool IsRequestsSection => string.Equals(SelectedSection, "Requests", StringComparison.OrdinalIgnoreCase);

	public string SectionTitle
	{
		get
		{
			if (IsPcsSection)
			{
				return "원격 PC";
			}
			if (IsOccupancySection)
			{
				return "점유 현황";
			}
			if (IsUsageLogsSection)
			{
				return "접속 이력";
			}
			if (IsWorkLogsSection)
			{
				return "업무 기록";
			}
			if (IsRequestsSection)
			{
				return "개선사항 관리";
			}
			return "사용자";
		}
	}

	public string UserSearchText
	{
		get
		{
			return _userSearchText;
		}
		set
		{
			if (SetProperty(ref _userSearchText, value ?? string.Empty, "UserSearchText"))
			{
				ApplyUserFilter();
			}
		}
	}

	public string PcSearchText
	{
		get
		{
			return _pcSearchText;
		}
		set
		{
			if (SetProperty(ref _pcSearchText, value ?? string.Empty, "PcSearchText"))
			{
				ApplyPcFilter();
			}
		}
	}

	public string OccupancySearchText
	{
		get
		{
			return _occupancySearchText;
		}
		set
		{
			if (SetProperty(ref _occupancySearchText, value ?? string.Empty, "OccupancySearchText"))
			{
				ApplyOccupancyFilter();
			}
		}
	}

	public string UsageLogSearchText
	{
		get
		{
			return _usageLogSearchText;
		}
		set
		{
			if (SetProperty(ref _usageLogSearchText, value ?? string.Empty, "UsageLogSearchText"))
			{
				ApplyUsageLogFilter();
			}
		}
	}

	public string UsageLogSiteFilter
	{
		get
		{
			return _usageLogSiteFilter;
		}
		private set
		{
			if (SetProperty(ref _usageLogSiteFilter, string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant(), "UsageLogSiteFilter"))
			{
				SyncSiteChipSelection(UsageLogSites, UsageLogSiteFilter);
				ApplyUsageLogFilter();
			}
		}
	}

	public string WorkLogSearchText
	{
		get
		{
			return _workLogSearchText;
		}
		set
		{
			if (SetProperty(ref _workLogSearchText, value ?? string.Empty, "WorkLogSearchText"))
			{
				ApplyWorkLogFilter();
			}
		}
	}

	public string WorkLogSiteFilter
	{
		get
		{
			return _workLogSiteFilter;
		}
		private set
		{
			if (SetProperty(ref _workLogSiteFilter, string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant(), "WorkLogSiteFilter"))
			{
				SyncSiteChipSelection(WorkLogSites, WorkLogSiteFilter);
			}
		}
	}

	public bool IsWorkLogDatePickerOpen
	{
		get { return _isWorkLogDatePickerOpen; }
		private set
		{
			if (SetProperty(ref _isWorkLogDatePickerOpen, value, "IsWorkLogDatePickerOpen"))
				RaisePropertyChanged("WorkLogDateChipText");
		}
	}

	public string WorkLogCalendarMonthTitle
	{
		get
		{
			if (!_draftStartDate.HasValue)
				return _calendarMonth.ToString("yyyy년 M월");
			return _calendarMonth.ToString("yyyy년 M월");
		}
	}

	public string WorkLogDateChipText
	{
		get
		{
			if (WorkLogListHost != null
				&& !string.IsNullOrWhiteSpace(WorkLogListHost.FilterFromText)
				&& !string.IsNullOrWhiteSpace(WorkLogListHost.FilterToText)
				&& WorkLogListHost.FilterFromText.Trim().Length == 10
				&& WorkLogListHost.FilterToText.Trim().Length == 10)
				return WorkLogListHost.FilterFromText.Trim() + " ~ " + WorkLogListHost.FilterToText.Trim();
			return "기간";
		}
	}

	public bool HasWorkLogDateFilter
	{
		get
		{
			return WorkLogListHost != null
				&& !string.IsNullOrWhiteSpace(WorkLogListHost.FilterFromText)
				&& !string.IsNullOrWhiteSpace(WorkLogListHost.FilterToText);
		}
	}

	public bool IsUsageLogDatePickerOpen
	{
		get { return _isUsageLogDatePickerOpen; }
		private set
		{
			if (SetProperty(ref _isUsageLogDatePickerOpen, value, "IsUsageLogDatePickerOpen"))
				RaisePropertyChanged("UsageLogDateChipText");
		}
	}

	public string UsageLogCalendarMonthTitle
	{
		get { return _usageLogCalendarMonth.ToString("yyyy년 M월"); }
	}

	public string UsageLogDateChipText
	{
		get
		{
			if (_usageLogFilterFrom.HasValue && _usageLogFilterTo.HasValue)
				return _usageLogFilterFrom.Value.ToString("yyyy-MM-dd") + " ~ " + _usageLogFilterTo.Value.ToString("yyyy-MM-dd");
			return "기간";
		}
	}

	public bool HasUsageLogDateFilter
	{
		get { return _usageLogFilterFrom.HasValue && _usageLogFilterTo.HasValue; }
	}

	public string AdminStatusMessage
	{
		get
		{
			return _adminStatusMessage;
		}
		private set
		{
			if (SetProperty(ref _adminStatusMessage, value ?? string.Empty, "AdminStatusMessage"))
			{
				RaisePropertyChanged("HasAdminStatusMessage");
			}
		}
	}

	public bool HasAdminStatusMessage => !string.IsNullOrWhiteSpace(AdminStatusMessage);

	public string AdminPcStatusMessage
	{
		get
		{
			return _adminPcStatusMessage;
		}
		private set
		{
			if (SetProperty(ref _adminPcStatusMessage, value ?? string.Empty, "AdminPcStatusMessage"))
			{
				RaisePropertyChanged("HasAdminPcStatusMessage");
			}
		}
	}

	public bool HasAdminPcStatusMessage => !string.IsNullOrWhiteSpace(AdminPcStatusMessage);

	public string OccupancyStatusMessage
	{
		get
		{
			return _occupancyStatusMessage;
		}
		private set
		{
			if (SetProperty(ref _occupancyStatusMessage, value ?? string.Empty, "OccupancyStatusMessage"))
			{
				RaisePropertyChanged("HasOccupancyStatusMessage");
			}
		}
	}

	public bool HasOccupancyStatusMessage => !string.IsNullOrWhiteSpace(OccupancyStatusMessage);

	public string UsageLogStatusMessage
	{
		get
		{
			return _usageLogStatusMessage;
		}
		private set
		{
			if (SetProperty(ref _usageLogStatusMessage, value ?? string.Empty, "UsageLogStatusMessage"))
			{
				RaisePropertyChanged("HasUsageLogStatusMessage");
			}
		}
	}

	public bool HasUsageLogStatusMessage => !string.IsNullOrWhiteSpace(UsageLogStatusMessage);

	public string WorkLogStatusMessage
	{
		get
		{
			return _workLogStatusMessage;
		}
		private set
		{
			if (SetProperty(ref _workLogStatusMessage, value ?? string.Empty, "WorkLogStatusMessage"))
			{
				RaisePropertyChanged("HasWorkLogStatusMessage");
			}
		}
	}

	public bool HasWorkLogStatusMessage => !string.IsNullOrWhiteSpace(WorkLogStatusMessage);

	public string AdminPcSiteFilter
	{
		get
		{
			return _adminPcSiteFilter;
		}
		private set
		{
			if (SetProperty(ref _adminPcSiteFilter, string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim(), "AdminPcSiteFilter"))
			{
				RaisePropertyChanged("PcFilterSummary");
				SyncAdminSiteChipSelection();
			}
		}
	}

	public string PcFilterSummary
	{
		get
		{
			string text = (string.Equals(AdminPcSiteFilter, "ALL", StringComparison.OrdinalIgnoreCase) ? "전체 사이트" : AdminPcSiteFilter);
			return text + " · " + PcCount + "대";
		}
	}

	public int UserActiveCount
	{
		get
		{
			return _userActiveCount;
		}
		private set
		{
			SetProperty(ref _userActiveCount, value, "UserActiveCount");
		}
	}

	public int UserInactiveCount
	{
		get
		{
			return _userInactiveCount;
		}
		private set
		{
			SetProperty(ref _userInactiveCount, value, "UserInactiveCount");
		}
	}

	public int PcCount
	{
		get
		{
			return _pcCount;
		}
		private set
		{
			if (SetProperty(ref _pcCount, value, "PcCount"))
			{
				RaisePropertyChanged("PcFilterSummary");
			}
		}
	}

	public int OccupancyBusyCount
	{
		get
		{
			return _occupancyBusyCount;
		}
		private set
		{
			if (SetProperty(ref _occupancyBusyCount, value, "OccupancyBusyCount"))
			{
				RaisePropertyChanged("HasOccupancyStats");
			}
		}
	}

	public int UsageLogCount
	{
		get
		{
			return _usageLogCount;
		}
		private set
		{
			SetProperty(ref _usageLogCount, value, "UsageLogCount");
		}
	}

	public int WorkLogCount
	{
		get
		{
			return _workLogCount;
		}
		private set
		{
			SetProperty(ref _workLogCount, value, "WorkLogCount");
		}
	}

	public bool HasOccupancyStats => OccupancyStats != null && OccupancyStats.Count > 0;

	public AdminPcItemViewModel SelectedAdminPc
	{
		get
		{
			return _selectedAdminPc;
		}
		private set
		{
			if (SetProperty(ref _selectedAdminPc, value, "SelectedAdminPc"))
			{
				RaisePropertyChanged("CanDeleteAdminPc");
			}
		}
	}

	public bool CanDeleteAdminPc => SelectedAdminPc != null;

	public AdminUserItemViewModel SelectedAdminUser
	{
		get
		{
			return _selectedAdminUser;
		}
		private set
		{
			SetProperty(ref _selectedAdminUser, value, "SelectedAdminUser");
		}
	}

	public AdminOccupancyItemViewModel SelectedOccupancy
	{
		get
		{
			return _selectedOccupancy;
		}
		private set
		{
			SetProperty(ref _selectedOccupancy, value, "SelectedOccupancy");
		}
	}

	public AdminUsageLogItemViewModel SelectedUsageLog
	{
		get
		{
			return _selectedUsageLog;
		}
		private set
		{
			if (SetProperty(ref _selectedUsageLog, value, "SelectedUsageLog"))
			{
				RaisePropertyChanged("HasSelectedUsageLog");
			}
		}
	}

	public bool HasSelectedUsageLog => SelectedUsageLog != null;

	public AdminWorkLogItemViewModel SelectedWorkLog
	{
		get
		{
			return _selectedWorkLog;
		}
		private set
		{
			if (SetProperty(ref _selectedWorkLog, value, "SelectedWorkLog"))
			{
				RaisePropertyChanged("HasSelectedWorkLog");
			}
		}
	}

	public bool HasSelectedWorkLog => SelectedWorkLog != null;

	public bool HasPcDetail
	{
		get
		{
			return _hasPcDetail;
		}
		private set
		{
			SetProperty(ref _hasPcDetail, value, "HasPcDetail");
		}
	}

	public string AdminDraftSite
	{
		get
		{
			return _adminDraftSite;
		}
		set
		{
			if (SetProperty(ref _adminDraftSite, value ?? string.Empty, "AdminDraftSite"))
			{
				RaisePropertyChanged("ShowAdminVpnFields");
				RaisePropertyChanged("ShowAdminAuthFields");
				RaisePropertyChanged("ShowAdminExtraCredFields");
				RaisePropertyChanged("AdminExtraCredTitle");
			}
		}
	}

	public string AdminDraftPcName
	{
		get
		{
			return _adminDraftPcName;
		}
		set
		{
			SetProperty(ref _adminDraftPcName, value ?? string.Empty, "AdminDraftPcName");
		}
	}

	public string AdminDraftPcIp
	{
		get
		{
			return _adminDraftPcIp;
		}
		set
		{
			SetProperty(ref _adminDraftPcIp, value ?? string.Empty, "AdminDraftPcIp");
		}
	}

	public string AdminDraftShareKey
	{
		get
		{
			return _adminDraftShareKey;
		}
		set
		{
			SetProperty(ref _adminDraftShareKey, value ?? string.Empty, "AdminDraftShareKey");
		}
	}

	public string AdminDraftTeamName
	{
		get
		{
			return _adminDraftTeamName;
		}
		set
		{
			SetProperty(ref _adminDraftTeamName, value ?? string.Empty, "AdminDraftTeamName");
		}
	}

	public string AdminDraftPcDomain
	{
		get
		{
			return _adminDraftPcDomain;
		}
		set
		{
			SetProperty(ref _adminDraftPcDomain, value ?? string.Empty, "AdminDraftPcDomain");
		}
	}

	public string AdminDraftDomainId
	{
		get
		{
			return _adminDraftDomainId;
		}
		set
		{
			SetProperty(ref _adminDraftDomainId, value ?? string.Empty, "AdminDraftDomainId");
		}
	}

	public string AdminDraftDomainPw
	{
		get
		{
			return _adminDraftDomainPw;
		}
		set
		{
			SetProperty(ref _adminDraftDomainPw, value ?? string.Empty, "AdminDraftDomainPw");
		}
	}

	public string AdminDraftExtraId
	{
		get
		{
			return _adminDraftExtraId;
		}
		set
		{
			SetProperty(ref _adminDraftExtraId, value ?? string.Empty, "AdminDraftExtraId");
		}
	}

	public string AdminDraftExtraPw
	{
		get
		{
			return _adminDraftExtraPw;
		}
		set
		{
			SetProperty(ref _adminDraftExtraPw, value ?? string.Empty, "AdminDraftExtraPw");
		}
	}

	public bool ShowAdminVpnFields => string.Equals(AdminDraftSite, "RC", StringComparison.OrdinalIgnoreCase) || string.Equals(AdminDraftSite, "MNGHA", StringComparison.OrdinalIgnoreCase);

	public bool ShowAdminAuthFields => string.Equals(AdminDraftSite, "CMC", StringComparison.OrdinalIgnoreCase);

	public bool ShowAdminExtraCredFields => ShowAdminVpnFields || ShowAdminAuthFields;

	public string AdminExtraCredTitle => ShowAdminAuthFields ? "Auth" : "VPN";

	public string AdminDraftPcComment
	{
		get
		{
			return _adminDraftPcComment;
		}
		set
		{
			SetProperty(ref _adminDraftPcComment, value ?? string.Empty, "AdminDraftPcComment");
		}
	}

	public bool AdminDraftAgentInstalled
	{
		get
		{
			return _adminDraftAgentInstalled;
		}
		set
		{
			SetProperty(ref _adminDraftAgentInstalled, value, "AdminDraftAgentInstalled");
		}
	}

	public bool SuppressPcListSelection => _suppressPcListSelection;

	public AdminViewModel()
	{
		AdminUsers = new ObservableCollection<AdminUserItemViewModel>();
		AdminPcs = new ObservableCollection<AdminPcItemViewModel>();
		AdminPcGroups = new ObservableCollection<AdminPcGroupViewModel>();
		Occupancies = new ObservableCollection<AdminOccupancyItemViewModel>();
		OccupancyStats = new ObservableCollection<AdminOccupancyStatRowViewModel>();
		UsageLogs = new ObservableCollection<AdminUsageLogItemViewModel>();
		UsageLogSiteGroups = new ObservableCollection<AdminUsageLogSiteGroupViewModel>();
		WorkLogs = new ObservableCollection<AdminWorkLogItemViewModel>();
		WorkLogSiteGroups = new ObservableCollection<AdminWorkLogSiteGroupViewModel>();
		WorkLogCalendarDays = new ObservableCollection<CalendarDayItem>();
		UsageLogCalendarDays = new ObservableCollection<CalendarDayItem>();
		UsageLogSites = CreateSiteChips();
		WorkLogSites = CreateSiteChips();
		AdminPcSites = new ObservableCollection<AdminSiteChipItemViewModel>
		{
			new AdminSiteChipItemViewModel("ALL"),
			new AdminSiteChipItemViewModel("AURORA"),
			new AdminSiteChipItemViewModel("CMC"),
			new AdminSiteChipItemViewModel("RC"),
			new AdminSiteChipItemViewModel("MNGHA")
		};
		SyncAdminSiteChipSelection();
		SyncSiteChipSelection(UsageLogSites, UsageLogSiteFilter);
		SyncSiteChipSelection(WorkLogSites, WorkLogSiteFilter);
		SelectSectionCommand = new RelayCommand<string>(SelectSection);
		ReloadAdminUsersCommand = new RelayCommand(delegate
		{
			Task task = ReloadAdminUsersAsync();
		});
		ManageAdminUserCommand = new RelayCommand<AdminUserItemViewModel>(delegate(AdminUserItemViewModel item)
		{
			Task task = ManageAdminUserAsync(item);
		});
		SelectAdminUserCommand = new RelayCommand<AdminUserItemViewModel>(SelectAdminUser);
		ReloadAdminPcsCommand = new RelayCommand(delegate
		{
			Task task = ReloadAdminPcsAsync();
		});
		SelectAdminPcCommand = new RelayCommand<AdminPcItemViewModel>(SelectAdminPc);
		NewAdminPcCommand = new RelayCommand(NewAdminPc);
		SaveAdminPcCommand = new RelayCommand(delegate
		{
			Task task = SaveAdminPcAsync();
		});
		DeleteAdminPcCommand = new RelayCommand(delegate
		{
			Task task = DeleteAdminPcAsync();
		});
		SelectAdminPcSiteCommand = new RelayCommand<string>(delegate(string site)
		{
			AdminPcSiteFilter = site;
			SyncAdminSiteChipSelection();
			if (!string.Equals(site, "ALL", StringComparison.OrdinalIgnoreCase))
			{
				AdminDraftSite = site;
			}
			else if (SelectedAdminPc == null)
			{
				AdminDraftSite = string.Empty;
			}
			if (SelectedAdminPc != null || !HasPcDetail)
			{
				SelectedAdminPc = null;
				HasPcDetail = false;
				_adminOriginalPcName = null;
				AdminDraftPcName = string.Empty;
				AdminDraftPcIp = string.Empty;
				AdminDraftShareKey = string.Empty;
				AdminDraftTeamName = string.Empty;
				AdminDraftDomainId = string.Empty;
				AdminDraftDomainPw = string.Empty;
				AdminDraftExtraId = string.Empty;
				AdminDraftExtraPw = string.Empty;
				AdminDraftPcDomain = string.Empty;
				AdminDraftPcComment = string.Empty;
				AdminDraftAgentInstalled = false;
			}
			AdminPcStatusMessage = string.Empty;
			ApplyPcFilter();
			Task task = ReloadAdminPcsAsync();
		});
		ReloadOccupancyCommand = new RelayCommand(delegate
		{
			Task task = ReloadOccupancyAsync();
		});
		SelectOccupancyCommand = new RelayCommand<AdminOccupancyItemViewModel>(SelectOccupancy);
		ForceReleaseOccupancyCommand = new RelayCommand<AdminOccupancyItemViewModel>(delegate(AdminOccupancyItemViewModel item)
		{
			Task task = ForceReleaseOccupancyAsync(item);
		});
		ReloadUsageLogsCommand = new RelayCommand(delegate
		{
			Task task = ReloadUsageLogsAsync();
		});
		SelectUsageLogCommand = new RelayCommand<AdminUsageLogItemViewModel>(SelectUsageLog);
		EditUsageLogCommand = new RelayCommand(delegate
		{
			Task task = EditUsageLogAsync();
		});
		DeleteUsageLogCommand = new RelayCommand(delegate
		{
			Task task = DeleteUsageLogAsync();
		});
		SelectUsageLogSiteCommand = new RelayCommand<string>(delegate(string site)
		{
			UsageLogSiteFilter = site;
		});
		ReloadWorkLogsCommand = new RelayCommand(delegate
		{
			Task task = ReloadWorkLogsAsync();
			if (WorkLogListHost != null)
			{
				Task task2 = WorkLogListHost.ReloadFromDbAsync(true);
			}
		});
		SelectWorkLogCommand = new RelayCommand<AdminWorkLogItemViewModel>(SelectWorkLog);
		ViewWorkLogCommand = new RelayCommand(delegate
		{
			Task task = ViewWorkLogAsync();
		});
		DeleteWorkLogCommand = new RelayCommand(delegate
		{
			Task task = DeleteWorkLogAsync();
		});
		SelectWorkLogSiteCommand = new RelayCommand<string>(delegate(string site)
		{
			WorkLogSiteFilter = site;
			if (WorkLogListHost != null)
				WorkLogListHost.FilterSite = string.Equals(site, "ALL", StringComparison.OrdinalIgnoreCase)
					? WorkLogSiteCodes.All
					: site;
		});
		OpenWorkLogDatePickerCommand = new RelayCommand(OpenWorkLogDatePicker);
		CloseWorkLogDatePickerCommand = new RelayCommand(() => IsWorkLogDatePickerOpen = false);
		PrevWorkLogCalendarMonthCommand = new RelayCommand(PrevWorkLogCalendarMonth);
		NextWorkLogCalendarMonthCommand = new RelayCommand(NextWorkLogCalendarMonth);
		SelectWorkLogCalendarDayCommand = new RelayCommand<DateTime>(SelectWorkLogCalendarDay);
		ApplyWorkLogDateFilterCommand = new RelayCommand(ApplyWorkLogDateFilter);
		ClearWorkLogDateFilterCommand = new RelayCommand(ClearWorkLogDateFilter);
		RebuildWorkLogCalendarDays();

		OpenUsageLogDatePickerCommand = new RelayCommand(OpenUsageLogDatePicker);
		CloseUsageLogDatePickerCommand = new RelayCommand(() => IsUsageLogDatePickerOpen = false);
		PrevUsageLogCalendarMonthCommand = new RelayCommand(PrevUsageLogCalendarMonth);
		NextUsageLogCalendarMonthCommand = new RelayCommand(NextUsageLogCalendarMonth);
		SelectUsageLogCalendarDayCommand = new RelayCommand<DateTime>(SelectUsageLogCalendarDay);
		ApplyUsageLogDateFilterCommand = new RelayCommand(ApplyUsageLogDateFilter);
		ClearUsageLogDateFilterCommand = new RelayCommand(ClearUsageLogDateFilter);
		RebuildUsageLogCalendarDays();

		_usageLogRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
		_usageLogRefreshTimer.Tick += delegate
		{
			Task task = ReloadUsageLogsAsync();
		};
	}

	private static ObservableCollection<AdminSiteChipItemViewModel> CreateSiteChips()
	{
		return new ObservableCollection<AdminSiteChipItemViewModel>
		{
			new AdminSiteChipItemViewModel("ALL"),
			new AdminSiteChipItemViewModel("AURORA"),
			new AdminSiteChipItemViewModel("CMC"),
			new AdminSiteChipItemViewModel("RC"),
			new AdminSiteChipItemViewModel("MNGHA")
		};
	}

	private static void SyncSiteChipSelection(ObservableCollection<AdminSiteChipItemViewModel> chips, string selected)
	{
		if (chips == null)
			return;
		string code = string.IsNullOrWhiteSpace(selected) ? "ALL" : selected.Trim();
		for (int i = 0; i < chips.Count; i++)
		{
			AdminSiteChipItemViewModel chip = chips[i];
			if (chip != null)
				chip.IsSelected = string.Equals(chip.Code, code, StringComparison.OrdinalIgnoreCase);
		}
	}

	public void AttachPopup(IPopupService popup)
	{
		_popup = popup;
	}

	public void AttachOpenWorkLog(Action<WorkLogListItemViewModel> openWorkLog)
	{
		_openWorkLog = openWorkLog;
	}

	public void AttachWorkLogList(WorkLogListViewModel workLogList)
	{
		if (_workLogListHost != null)
			_workLogListHost.PropertyChanged -= OnWorkLogListHostPropertyChanged;
		WorkLogListHost = workLogList;
		if (_workLogListHost != null)
			_workLogListHost.PropertyChanged += OnWorkLogListHostPropertyChanged;
		RaisePropertyChanged("WorkLogDateChipText");
		RaisePropertyChanged("HasWorkLogDateFilter");
	}

	private void OnWorkLogListHostPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e == null)
			return;
		if (e.PropertyName == "FilterFromText" || e.PropertyName == "FilterToText")
		{
			RaisePropertyChanged("WorkLogDateChipText");
			RaisePropertyChanged("HasWorkLogDateFilter");
		}
	}

	public void RefreshWorkLogsAfterEdit()
	{
		if (!OccupancyNameStore.IsAdmin || !IsWorkLogsSection)
			return;
		var ignored = ReloadWorkLogsAsync();
		if (WorkLogListHost != null)
		{
			var ignored2 = WorkLogListHost.ReloadFromDbAsync(true);
		}
	}

	private void SyncAdminSiteChipSelection()
	{
		if (AdminPcSites == null)
		{
			return;
		}
		string adminPcSiteFilter = AdminPcSiteFilter;
		for (int i = 0; i < AdminPcSites.Count; i++)
		{
			AdminSiteChipItemViewModel adminSiteChipItemViewModel = AdminPcSites[i];
			if (adminSiteChipItemViewModel != null)
			{
				adminSiteChipItemViewModel.IsSelected = string.Equals(adminSiteChipItemViewModel.Code, adminPcSiteFilter, StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	private void ClearAdminPcDraftKeepSite()
	{
		SelectedAdminPc = null;
		HasPcDetail = false;
		_adminOriginalPcName = null;
		AdminDraftPcName = string.Empty;
		AdminDraftPcIp = string.Empty;
		AdminDraftShareKey = string.Empty;
		AdminDraftTeamName = string.Empty;
		AdminDraftDomainId = string.Empty;
		AdminDraftDomainPw = string.Empty;
		AdminDraftExtraId = string.Empty;
		AdminDraftExtraPw = string.Empty;
		AdminDraftPcDomain = string.Empty;
		AdminDraftPcComment = string.Empty;
		AdminDraftAgentInstalled = false;
	}

	public void EnterShell()
	{
		RaisePropertyChanged("DisplayName");
		SelectedSection = "Pcs";
		Task task = ReloadAsync();
	}

	public void LeaveShell()
	{
		if (_usageLogRefreshTimer != null)
			_usageLogRefreshTimer.Stop();
		_allUsers.Clear();
		_allPcs.Clear();
		_allOccupancies.Clear();
		_allUsageLogs.Clear();
		_allWorkLogs.Clear();
		AdminUsers.Clear();
		AdminPcs.Clear();
		AdminPcGroups.Clear();
		Occupancies.Clear();
		UsageLogs.Clear();
		UsageLogSiteGroups.Clear();
		WorkLogs.Clear();
		WorkLogSiteGroups.Clear();
		if (OccupancyStats != null)
		{
			OccupancyStats.Clear();
		}
		AdminStatusMessage = string.Empty;
		AdminPcStatusMessage = string.Empty;
		OccupancyStatusMessage = string.Empty;
		UsageLogStatusMessage = string.Empty;
		WorkLogStatusMessage = string.Empty;
		ClearAdminPcDraft();
		SelectedAdminUser = null;
		SelectedOccupancy = null;
		SelectedUsageLog = null;
		SelectedWorkLog = null;
	}

	public async Task ReloadAsync()
	{
		if (!OccupancyNameStore.IsAdmin)
		{
			LeaveShell();
			return;
		}
		await ReloadAdminUsersAsync().ConfigureAwait(continueOnCapturedContext: true);
		await ReloadAdminPcsAsync().ConfigureAwait(continueOnCapturedContext: true);
		await ReloadOccupancyAsync().ConfigureAwait(continueOnCapturedContext: true);
		await ReloadUsageLogsAsync().ConfigureAwait(continueOnCapturedContext: true);
		await ReloadWorkLogsAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	private void SelectSection(string section)
	{
		if (!string.IsNullOrWhiteSpace(section))
		{
			SelectedSection = section.Trim();
			if (IsUsersSection)
			{
				Task task = ReloadAdminUsersAsync();
			}
			else if (IsPcsSection)
			{
				Task task2 = ReloadAdminPcsAsync();
			}
			else if (IsOccupancySection)
			{
				Task task3 = ReloadOccupancyAsync();
			}
			else if (IsUsageLogsSection)
			{
				Task task4 = ReloadUsageLogsAsync();
			}
			else if (IsWorkLogsSection)
			{
				Task task5 = ReloadWorkLogsAsync();
				if (WorkLogListHost != null)
				{
					Task task6 = WorkLogListHost.ReloadFromDbAsync(true);
				}
			}

			if (IsUsageLogsSection && _usageLogRefreshTimer != null)
				_usageLogRefreshTimer.Start();
			else if (_usageLogRefreshTimer != null)
				_usageLogRefreshTimer.Stop();
		}
	}

	private async Task ReloadAdminUsersAsync()
	{
		AdminStatusMessage = string.Empty;
		if (!OccupancyNameStore.IsAdmin)
		{
			_allUsers.Clear();
			AdminUsers.Clear();
			return;
		}
		IList<DirectoryUserDto> users;
		try
		{
			users = await Task.Run(() => new AuthBiz().ListUsersForAdmin()).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			AdminStatusMessage = "사용자 목록 조회 실패: " + ex2.Message;
			return;
		}
		_allUsers.Clear();
		int active = 0;
		int inactive = 0;
		if (users != null)
		{
			for (int i = 0; i < users.Count; i++)
			{
				DirectoryUserDto u = users[i];
				if (u != null)
				{
					AdminUserItemViewModel item = AdminUserItemViewModel.FromDto(u);
					_allUsers.Add(item);
					if (item.IsActive)
					{
						active++;
					}
					else
					{
						inactive++;
					}
				}
			}
		}
		UserActiveCount = active;
		UserInactiveCount = inactive;
		ApplyUserFilter();
	}

	private void ApplyUserFilter()
	{
		string text = (UserSearchText ?? string.Empty).Trim();
		AdminUsers.Clear();
		for (int i = 0; i < _allUsers.Count; i++)
		{
			AdminUserItemViewModel adminUserItemViewModel = _allUsers[i];
			if (adminUserItemViewModel != null && (text.Length <= 0 || adminUserItemViewModel.MatchesSearch(text)))
			{
				AdminUsers.Add(adminUserItemViewModel);
			}
		}
	}

	private void SelectAdminUser(AdminUserItemViewModel item)
	{
		if (item == null)
		{
			return;
		}
		SelectedAdminUser = item;
		for (int i = 0; i < AdminUsers.Count; i++)
		{
			AdminUserItemViewModel adminUserItemViewModel = AdminUsers[i];
			if (adminUserItemViewModel != null)
			{
				adminUserItemViewModel.IsSelected = adminUserItemViewModel == item;
			}
		}
	}

	private async Task ManageAdminUserAsync(AdminUserItemViewModel item)
	{
		if (item == null || !item.CanManageProfile || !OccupancyNameStore.IsAdmin)
		{
			return;
		}
		if (_popup == null)
		{
			AdminStatusMessage = "팝업을 사용할 수 없습니다.";
			return;
		}
		PopupResult result = await _popup.ShowPromptAsync(new PopupRequest
		{
			Title = "사용자 정보 관리",
			Icon = PopupIconKind.Info,
			Message = item.Title + " 계정의 정보를 수정하거나 계정을 삭제할 수 있습니다.",
			ShowInput = true,
			InputText = item.RawUserName,
			ShowAffiliationInput = true,
			RequireAffiliation = false,
			AffiliationText = item.RawTeamName,
			ShowSecondaryInput = true,
			RequireSecondaryInput = true,
			SecondaryInputLabel = "로그인 ID",
			SecondaryInputText = item.RawLoginId,
			ShowPasswordInput = true,
			RequirePassword = false,
			ShowPasswordConfirm = true,
			Buttons = new PopupButtonDefinition[4]
			{
				new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
				new PopupButtonDefinition(item.ActionLabel, PopupResultType.Secondary, isDefault: false),
				new PopupButtonDefinition("계정 삭제", PopupResultType.Tertiary, isDefault: false),
				new PopupButtonDefinition("저장", PopupResultType.Primary, isDefault: true)
			},
			PrimaryValidator = delegate(PopupHostViewModelSnapshot snap)
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				string result2 = new AuthBiz().UpdateUserProfileForAdmin(item.UserId, item.IsBuiltInAdmin, snap.InputText, snap.AffiliationText, snap.SecondaryInputText);
				if (string.IsNullOrWhiteSpace(result2) && !string.IsNullOrEmpty(snap.PasswordText))
				{
					result2 = new AuthBiz().ResetPasswordForAdmin(item.Key, snap.PasswordText);
				}
				return Task.FromResult(result2);
			}
		}).ConfigureAwait(continueOnCapturedContext: true);

		if (result == null || result.IsCancelOrClosed)
		{
			return;
		}

		if (result.IsSecondary)
		{
			bool next = !item.IsActive;
			string toggleErr = await Task.Run(() => new AuthBiz().SetUserActiveForAdmin(item.Key, next)).ConfigureAwait(continueOnCapturedContext: true);
			if (!string.IsNullOrWhiteSpace(toggleErr))
			{
				AdminStatusMessage = toggleErr;
				return;
			}
			AdminStatusMessage = string.Empty;
			await ReloadAdminUsersAsync().ConfigureAwait(continueOnCapturedContext: true);
			return;
		}

		if (result.IsTertiary)
		{
			PopupResult confirm = await _popup.ShowConfirmAsync(new PopupRequest
			{
				Title = "계정 삭제",
				Icon = PopupIconKind.Warning,
				Message = item.Title + " 계정을 삭제하시겠습니까?",
				Detail = "삭제해도 기존 요청사항·업무기록은 그대로 남고, 작성자 이름 옆에 \"(삭제된 계정)\"으로 표시됩니다.",
				Buttons = new PopupButtonDefinition[2]
				{
					new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
					new PopupButtonDefinition("삭제", PopupResultType.Primary, isDefault: true)
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
			if (confirm == null || !confirm.IsPrimary || confirm.IsCancelOrClosed)
			{
				return;
			}
			string delErr = await Task.Run(() => new AuthBiz().SetUserDeletedForAdmin(item.UserId, item.IsBuiltInAdmin, true)).ConfigureAwait(continueOnCapturedContext: true);
			if (!string.IsNullOrWhiteSpace(delErr))
			{
				AdminStatusMessage = delErr;
				return;
			}
			AdminStatusMessage = "계정을 삭제했습니다.";
			await ReloadAdminUsersAsync().ConfigureAwait(continueOnCapturedContext: true);
			return;
		}

		if (result.IsPrimary)
		{
			AdminStatusMessage = "사용자 정보를 저장했습니다.";
			await ReloadAdminUsersAsync().ConfigureAwait(continueOnCapturedContext: true);
		}
	}

	private async Task ReloadAdminPcsAsync()
	{
		if (!OccupancyNameStore.IsAdmin)
		{
			_allPcs.Clear();
			AdminPcs.Clear();
			AdminPcStatusMessage = string.Empty;
			return;
		}
		string site = AdminPcSiteFilter;
		_suppressPcListSelection = true;
		IList<PcMapDto> maps;
		try
		{
			maps = await Task.Run(() => new DirectoryBiz().ListPcMapsForAdmin(site)).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			AdminPcStatusMessage = "PC 목록 조회 실패: " + ex.Message;
			_suppressPcListSelection = false;
			return;
		}
		try
		{
			string selectedSite = ((SelectedAdminPc != null) ? SelectedAdminPc.SiteCode : null);
			string selectedPc = ((SelectedAdminPc != null) ? SelectedAdminPc.PcName : null);
			_allPcs.Clear();
			if (maps != null)
			{
				for (int i = 0; i < maps.Count; i++)
				{
					PcMapDto m = maps[i];
					if (m != null)
					{
						_allPcs.Add(AdminPcItemViewModel.FromDto(m));
					}
				}
			}
			ApplyPcFilter();
			if (string.IsNullOrWhiteSpace(selectedSite) || string.IsNullOrWhiteSpace(selectedPc))
			{
				return;
			}
			for (int i2 = 0; i2 < AdminPcs.Count; i2++)
			{
				AdminPcItemViewModel row = AdminPcs[i2];
				if (row != null && string.Equals(row.SiteCode, selectedSite, StringComparison.OrdinalIgnoreCase) && string.Equals(row.PcName, selectedPc, StringComparison.OrdinalIgnoreCase))
				{
					SelectAdminPc(row);
					break;
				}
			}
		}
		finally
		{
			_suppressPcListSelection = false;
		}
	}

	private void ApplyPcFilter()
	{
		string text = (PcSearchText ?? string.Empty).Trim();
		string adminPcSiteFilter = AdminPcSiteFilter;
		List<AdminPcItemViewModel> list = new List<AdminPcItemViewModel>();
		for (int i = 0; i < _allPcs.Count; i++)
		{
			AdminPcItemViewModel adminPcItemViewModel = _allPcs[i];
			if (adminPcItemViewModel != null && (string.Equals(adminPcSiteFilter, "ALL", StringComparison.OrdinalIgnoreCase) || string.Equals(adminPcItemViewModel.SiteCode, adminPcSiteFilter, StringComparison.OrdinalIgnoreCase)) && (text.Length <= 0 || adminPcItemViewModel.MatchesSearch(text)))
			{
				adminPcItemViewModel.IsSelected = SelectedAdminPc != null && string.Equals(adminPcItemViewModel.SiteCode, SelectedAdminPc.SiteCode, StringComparison.OrdinalIgnoreCase) && string.Equals(adminPcItemViewModel.PcName, SelectedAdminPc.PcName, StringComparison.OrdinalIgnoreCase);
				list.Add(adminPcItemViewModel);
			}
		}
		list.Sort(CompareAdminPc);
		AdminPcs.Clear();
		for (int j = 0; j < list.Count; j++)
		{
			AdminPcs.Add(list[j]);
		}
		PcCount = list.Count;
		RebuildAdminPcGroups(list);
	}

	private static int CompareAdminPc(AdminPcItemViewModel a, AdminPcItemViewModel b)
	{
		int num = PcNameNaturalSort.CompareGroup(a?.TeamName, b?.TeamName);
		if (num != 0)
		{
			return num;
		}
		return PcNameNaturalSort.Compare(a?.PcName, b?.PcName);
	}

	private void RebuildAdminPcGroups(IList<AdminPcItemViewModel> sorted)
	{
		AdminPcGroups.Clear();
		if (sorted == null || sorted.Count == 0)
		{
			return;
		}
		AdminPcGroupViewModel adminPcGroupViewModel = null;
		for (int i = 0; i < sorted.Count; i++)
		{
			AdminPcItemViewModel adminPcItemViewModel = sorted[i];
			if (adminPcItemViewModel != null)
			{
				string text = adminPcItemViewModel.TeamName ?? string.Empty;
				if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "미지정", StringComparison.Ordinal))
				{
					text = string.Empty;
				}
				if (adminPcGroupViewModel == null || !string.Equals(adminPcGroupViewModel.Name, text, StringComparison.Ordinal))
				{
					adminPcGroupViewModel = new AdminPcGroupViewModel(text);
					AdminPcGroups.Add(adminPcGroupViewModel);
				}
				adminPcGroupViewModel.Computers.Add(adminPcItemViewModel);
			}
		}
	}

	private void SelectAdminPc(AdminPcItemViewModel item)
	{
		if (item == null)
		{
			return;
		}
		SelectedAdminPc = item;
		HasPcDetail = true;
		_adminOriginalPcName = item.PcName;
		AdminDraftSite = item.SiteCode;
		AdminDraftPcName = item.PcName;
		AdminDraftPcIp = item.PcIp;
		AdminDraftShareKey = item.ShareKey;
		AdminDraftTeamName = item.TeamName;
		AdminDraftAgentInstalled = item.AgentInstalled;
		LoadAccessDraftFromPc(item);
		AdminPcStatusMessage = string.Empty;
		for (int i = 0; i < AdminPcs.Count; i++)
		{
			AdminPcItemViewModel adminPcItemViewModel = AdminPcs[i];
			if (adminPcItemViewModel != null)
			{
				adminPcItemViewModel.IsSelected = adminPcItemViewModel == item;
			}
		}
	}

	private void LoadAccessDraftFromPc(AdminPcItemViewModel item)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		List<string> list4 = new List<string>();
		IList<PcAccessCredential> list5 = PcAccessNoteParser.ParseCredentials(item?.PcNote);
		if ((list5 == null || list5.Count == 0) && item != null && !string.IsNullOrWhiteSpace(item.PcDomain))
		{
			string[] array = item.PcDomain.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.Length > 0)
				{
					list.Add(text);
				}
			}
		}
		else if (list5 != null)
		{
			bool flag = item != null && string.Equals(item.SiteCode, "CMC", StringComparison.OrdinalIgnoreCase);
			for (int j = 0; j < list5.Count; j++)
			{
				PcAccessCredential val = list5[j];
				if (val != null && !string.IsNullOrWhiteSpace(val.Value))
				{
					string item2 = val.Value.Trim();
					if (string.Equals(val.Kind, "PW", StringComparison.OrdinalIgnoreCase))
					{
						list2.Add(item2);
					}
					else if (string.Equals(val.Kind, "AUTH_ID", StringComparison.OrdinalIgnoreCase) || (flag && string.Equals(val.Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase)))
					{
						list3.Add(item2);
					}
					else if (string.Equals(val.Kind, "AUTH_PW", StringComparison.OrdinalIgnoreCase) || (flag && string.Equals(val.Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase)))
					{
						list4.Add(item2);
					}
					else if (string.Equals(val.Kind, "VPN_ID", StringComparison.OrdinalIgnoreCase))
					{
						list3.Add(item2);
					}
					else if (string.Equals(val.Kind, "VPN_PW", StringComparison.OrdinalIgnoreCase))
					{
						list4.Add(item2);
					}
					else
					{
						list.Add(item2);
					}
				}
			}
		}
		AdminDraftDomainId = JoinLines(list);
		AdminDraftDomainPw = JoinLines(list2);
		AdminDraftExtraId = JoinLines(list3);
		AdminDraftExtraPw = JoinLines(list4);
		AdminDraftPcDomain = AdminDraftDomainId;
		string text2 = item?.PcComment;
		if (string.IsNullOrWhiteSpace(text2) && item != null)
		{
			text2 = PcAccessNoteParser.ParseCommentFromNote(item.PcNote);
		}
		AdminDraftPcComment = text2 ?? string.Empty;
	}

	private static string JoinLines(IList<string> lines)
	{
		if (lines == null || lines.Count == 0)
		{
			return string.Empty;
		}
		return string.Join(Environment.NewLine, lines);
	}

	private static IList<string> SplitLines(string text)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return list;
		}
		string[] array = text.Replace("\r\n", "\n").Replace('\r', '\n').Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length > 0)
			{
				list.Add(text2);
			}
		}
		return list;
	}

	private string BuildAdminPcNote()
	{
		List<string> list = new List<string>();
		IList<string> list2 = SplitLines(AdminDraftDomainId);
		IList<string> list3 = SplitLines(AdminDraftDomainPw);
		IList<string> list4 = SplitLines(AdminDraftExtraId);
		IList<string> list5 = SplitLines(AdminDraftExtraPw);
		if (list2.Count > 0)
		{
			list.Add("[ID]\n" + string.Join("\n", list2));
		}
		if (list3.Count > 0)
		{
			list.Add("[Password]\n" + string.Join("\n", list3));
		}
		if (ShowAdminAuthFields)
		{
			if (list4.Count > 0)
			{
				list.Add("[Auth ID]\n" + string.Join("\n", list4));
			}
			if (list5.Count > 0)
			{
				list.Add("[Auth Password]\n" + string.Join("\n", list5));
			}
		}
		else if (ShowAdminVpnFields)
		{
			if (list4.Count > 0)
			{
				list.Add("[VPN ID]\n" + string.Join("\n", list4));
			}
			if (list5.Count > 0)
			{
				list.Add("[VPN Password]\n" + string.Join("\n", list5));
			}
		}
		return (list.Count == 0) ? null : string.Join("\n\n", list.ToArray());
	}

	private void NewAdminPc()
	{
		SelectedAdminPc = null;
		HasPcDetail = true;
		_adminOriginalPcName = null;
		AdminDraftSite = (string.Equals(AdminPcSiteFilter, "ALL", StringComparison.OrdinalIgnoreCase) ? string.Empty : AdminPcSiteFilter);
		AdminDraftPcName = string.Empty;
		AdminDraftPcIp = string.Empty;
		AdminDraftShareKey = string.Empty;
		AdminDraftTeamName = string.Empty;
		AdminDraftDomainId = string.Empty;
		AdminDraftDomainPw = string.Empty;
		AdminDraftExtraId = string.Empty;
		AdminDraftExtraPw = string.Empty;
		AdminDraftPcDomain = string.Empty;
		AdminDraftPcComment = string.Empty;
		AdminDraftAgentInstalled = false;
		AdminPcStatusMessage = string.Empty;
		for (int i = 0; i < AdminPcs.Count; i++)
		{
			if (AdminPcs[i] != null)
			{
				AdminPcs[i].IsSelected = false;
			}
		}
	}

	private void ClearAdminPcDraft()
	{
		SelectedAdminPc = null;
		HasPcDetail = false;
		_adminOriginalPcName = null;
		AdminDraftSite = string.Empty;
		AdminDraftPcName = string.Empty;
		AdminDraftPcIp = string.Empty;
		AdminDraftShareKey = string.Empty;
		AdminDraftTeamName = string.Empty;
		AdminDraftDomainId = string.Empty;
		AdminDraftDomainPw = string.Empty;
		AdminDraftExtraId = string.Empty;
		AdminDraftExtraPw = string.Empty;
		AdminDraftPcDomain = string.Empty;
		AdminDraftPcComment = string.Empty;
		AdminDraftAgentInstalled = false;
	}

	private async Task SaveAdminPcAsync()
	{
		if (!OccupancyNameStore.IsAdmin)
		{
			return;
		}
		string site = (AdminDraftSite ?? string.Empty).Trim();
		string pc = (AdminDraftPcName ?? string.Empty).Trim();
		string ip = (AdminDraftPcIp ?? string.Empty).Trim();
		string team = (AdminDraftTeamName ?? string.Empty).Trim();
		IList<string> domainIds = SplitLines(AdminDraftDomainId);
		IList<string> domainPws = SplitLines(AdminDraftDomainPw);
		IList<string> extraIds = SplitLines(AdminDraftExtraId);
		IList<string> extraPws = SplitLines(AdminDraftExtraPw);
		if (string.IsNullOrWhiteSpace(site) || string.Equals(site, "ALL", StringComparison.OrdinalIgnoreCase))
		{
			AdminPcStatusMessage = "사이트를 입력해 주세요.";
			return;
		}
		if (string.IsNullOrWhiteSpace(pc))
		{
			AdminPcStatusMessage = "PC 이름을 입력해 주세요.";
			return;
		}
		if (string.IsNullOrWhiteSpace(ip))
		{
			AdminPcStatusMessage = "IP를 입력해 주세요.";
			return;
		}
		if (string.IsNullOrWhiteSpace(team))
		{
			AdminPcStatusMessage = "팀을 입력해 주세요.";
			return;
		}
		if (domainIds.Count == 0 || domainPws.Count == 0)
		{
			AdminPcStatusMessage = "Domain ID/PW를 입력해 주세요.";
			return;
		}
		if (ShowAdminExtraCredFields && (extraIds.Count == 0 || extraPws.Count == 0))
		{
			AdminPcStatusMessage = AdminExtraCredTitle + " ID/PW를 입력해 주세요.";
			return;
		}
		AdminDraftSite = site;
		AdminDraftPcName = pc;
		AdminDraftPcIp = ip;
		AdminDraftTeamName = team;
		AdminDraftPcDomain = string.Join(Environment.NewLine, domainIds);
		string note = BuildAdminPcNote();
		PcMapDto map = new PcMapDto
		{
			SiteCode = site,
			PcName = pc,
			PcIp = ip,
			ShareKey = null,
			TeamName = team,
			PcDomain = AdminDraftPcDomain,
			PcNote = note,
			PcComment = AdminDraftPcComment,
			AgentInstalled = AdminDraftAgentInstalled
		};
		string err = await Task.Run(() => new DirectoryBiz().UpsertPcMapForAdmin(map, _adminOriginalPcName)).ConfigureAwait(continueOnCapturedContext: true);
		if (!string.IsNullOrWhiteSpace(err))
		{
			AdminPcStatusMessage = err;
			return;
		}
		AdminPcStatusMessage = "저장했습니다.";
		_adminOriginalPcName = map.PcName;
		await ReloadAdminPcsAsync().ConfigureAwait(continueOnCapturedContext: true);
		for (int i = 0; i < AdminPcs.Count; i++)
		{
			AdminPcItemViewModel row = AdminPcs[i];
			if (row != null && string.Equals(row.SiteCode, map.SiteCode, StringComparison.OrdinalIgnoreCase) && string.Equals(row.PcName, map.PcName, StringComparison.OrdinalIgnoreCase))
			{
				SelectAdminPc(row);
				break;
			}
		}
	}

	private async Task DeleteAdminPcAsync()
	{
		if (!OccupancyNameStore.IsAdmin)
		{
			return;
		}
		string site = ((!string.IsNullOrWhiteSpace(AdminDraftSite)) ? AdminDraftSite : null);
		string pc = ((!string.IsNullOrWhiteSpace(_adminOriginalPcName)) ? _adminOriginalPcName : AdminDraftPcName);
		if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(pc))
		{
			AdminPcStatusMessage = "삭제할 PC를 선택해 주세요.";
			return;
		}
		if (_popup != null)
		{
			PopupResult confirm = await _popup.ShowConfirmAsync(new PopupRequest
			{
				Title = "PC 삭제",
				Icon = PopupIconKind.Warning,
				Message = site + " · " + pc + " 을(를) 삭제할까요?",
				Buttons = new PopupButtonDefinition[2]
				{
					new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
					new PopupButtonDefinition("삭제", PopupResultType.Primary, isDefault: true)
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
			if (confirm == null || !confirm.IsPrimary || confirm.IsCancelOrClosed)
			{
				return;
			}
		}
		string err = await Task.Run(() => new DirectoryBiz().DeletePcMapForAdmin(site, pc)).ConfigureAwait(continueOnCapturedContext: true);
		if (!string.IsNullOrWhiteSpace(err))
		{
			AdminPcStatusMessage = err;
			return;
		}
		AdminPcStatusMessage = "삭제했습니다.";
		ClearAdminPcDraft();
		await ReloadAdminPcsAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	private async Task ReloadOccupancyAsync()
	{
		OccupancyStatusMessage = string.Empty;
		if (!OccupancyNameStore.IsAdmin)
		{
			_allOccupancies.Clear();
			Occupancies.Clear();
			if (OccupancyStats != null)
			{
				OccupancyStats.Clear();
			}
			OccupancyBusyCount = 0;
			RaisePropertyChanged("HasOccupancyStats");
			return;
		}
		IList<RemotePcStatus> rows;
		IList<PcMapDto> maps;
		IList<DirectoryUserDto> users;
		try
		{
			rows = await new RemotePcShareBiz().GetAllAsync().ConfigureAwait(continueOnCapturedContext: true);
			maps = await Task.Run(() => new DirectoryBiz().ListPcMapsForAdmin("ALL")).ConfigureAwait(continueOnCapturedContext: true);
			users = await Task.Run(() => new AuthBiz().ListUsersForAdmin()).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			OccupancyStatusMessage = "점유 현황 조회 실패: " + ex2.Message;
			return;
		}
		_allOccupancies.Clear();
		int busy = 0;
		if (rows != null)
		{
			for (int i = 0; i < rows.Count; i++)
			{
				RemotePcStatus s = rows[i];
				if (s == null || string.Equals(s.AccessStatusCode, "AVAILABLE", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				AdminOccupancyItemViewModel item = AdminOccupancyItemViewModel.FromStatus(s);
				if (item != null)
				{
					item.TeamName = ResolveOccupancyTeam(item, maps, users);
					_allOccupancies.Add(item);
					if (item.CanForceRelease)
					{
						busy++;
					}
				}
			}
		}
		OccupancyBusyCount = busy;
		RebuildOccupancyStats(maps, users);
		ApplyOccupancyFilter();
	}

	private void RebuildOccupancyStats(IList<PcMapDto> maps, IList<DirectoryUserDto> users)
	{
		if (OccupancyStats == null)
		{
			OccupancyStats = new ObservableCollection<AdminOccupancyStatRowViewModel>();
		}
		OccupancyStats.Clear();
		List<string> list = new List<string>();
		Dictionary<string, int[]> dictionary = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
		EnsureStatTeam(dictionary, list, "미지정");
		if (users != null)
		{
			for (int i = 0; i < users.Count; i++)
			{
				DirectoryUserDto val = users[i];
				if (val != null && !string.IsNullOrWhiteSpace(val.TeamName))
				{
					EnsureStatTeam(dictionary, list, val.TeamName.Trim());
				}
			}
		}
		if (maps != null)
		{
			for (int j = 0; j < maps.Count; j++)
			{
				PcMapDto val2 = maps[j];
				if (val2 != null && !string.IsNullOrWhiteSpace(val2.TeamName))
				{
					EnsureStatTeam(dictionary, list, val2.TeamName.Trim());
				}
			}
		}
		for (int k = 0; k < _allOccupancies.Count; k++)
		{
			AdminOccupancyItemViewModel adminOccupancyItemViewModel = _allOccupancies[k];
			if (adminOccupancyItemViewModel != null && adminOccupancyItemViewModel.IsInUse)
			{
				string text = (string.IsNullOrWhiteSpace(adminOccupancyItemViewModel.TeamName) ? "미지정" : adminOccupancyItemViewModel.TeamName.Trim());
				string site = (string.IsNullOrWhiteSpace(adminOccupancyItemViewModel.SiteCode) ? string.Empty : adminOccupancyItemViewModel.SiteCode.Trim().ToUpperInvariant());
				int num = IndexOfStatSite(site);
				if (num >= 0)
				{
					EnsureStatTeam(dictionary, list, text);
					dictionary[text][num]++;
				}
			}
		}
		if ((list.Count != 1 || !string.Equals(list[0], "미지정", StringComparison.Ordinal)) && dictionary.TryGetValue("미지정", out var value) && value != null && value[0] == 0 && value[1] == 0 && value[2] == 0 && value[3] == 0)
		{
			dictionary.Remove("미지정");
			list.RemoveAll((string t) => string.Equals(t, "미지정", StringComparison.Ordinal));
		}
		list.Sort(CompareTeamNamesForStats);
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			string text2 = list[num2];
			if (!dictionary.TryGetValue(text2, out var value2) || value2 == null)
			{
				value2 = new int[OccupancyStatSites.Length];
			}
			OccupancyStats.Add(new AdminOccupancyStatRowViewModel(text2, value2));
		}
		RaisePropertyChanged("HasOccupancyStats");
	}

	private static void EnsureStatTeam(Dictionary<string, int[]> counts, List<string> teamOrder, string team)
	{
		team = ((!string.IsNullOrWhiteSpace(team)) ? team.Trim() : "미지정");
		if (!counts.ContainsKey(team))
		{
			counts[team] = new int[OccupancyStatSites.Length];
			teamOrder.Add(team);
		}
	}

	private static int CompareTeamNamesForStats(string a, string b)
	{
		a = a ?? string.Empty;
		b = b ?? string.Empty;
		bool flag = IsEtcTeamName(a);
		bool flag2 = IsEtcTeamName(b);
		if (flag != flag2)
		{
			return flag ? 1 : (-1);
		}
		bool flag3 = StartsWithHangul(a);
		bool flag4 = StartsWithHangul(b);
		if (flag3 != flag4)
		{
			return (!flag3) ? 1 : (-1);
		}
		return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
	}

	private static bool IsEtcTeamName(string team)
	{
		if (string.IsNullOrWhiteSpace(team))
		{
			return false;
		}
		string a = team.Trim();
		return string.Equals(a, "ETC", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "E.T.C", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "기타", StringComparison.OrdinalIgnoreCase);
	}

	private static bool StartsWithHangul(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c))
			{
				return c >= '가' && c <= '힣';
			}
		}
		return false;
	}

	private static int IndexOfStatSite(string site)
	{
		for (int i = 0; i < OccupancyStatSites.Length; i++)
		{
			if (string.Equals(OccupancyStatSites[i], site, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	private static string ResolveOccupancyTeam(AdminOccupancyItemViewModel item, IList<PcMapDto> maps, IList<DirectoryUserDto> users)
	{
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_015c: Expected O, but got Unknown
		if (item == null)
		{
			return "미지정";
		}
		string occupant = item.Occupant;
		if (!string.IsNullOrWhiteSpace(occupant) && users != null)
		{
			for (int i = 0; i < users.Count; i++)
			{
				DirectoryUserDto val = users[i];
				if (val != null && (string.Equals(val.LoginId, occupant, StringComparison.OrdinalIgnoreCase) || string.Equals(val.UserName, occupant, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(val.TeamName))
				{
					return val.TeamName.Trim();
				}
			}
		}
		if (maps != null)
		{
			for (int j = 0; j < maps.Count; j++)
			{
				PcMapDto val2 = maps[j];
				if (val2 != null && (string.IsNullOrWhiteSpace(item.SiteCode) || string.Equals(val2.SiteCode, item.SiteCode, StringComparison.OrdinalIgnoreCase)) && RemotePcStatusMapper.StatusFitsPc(new RemotePcStatus
				{
					SiteCode = item.SiteCode,
					RemotePcName = item.RemotePcName,
					RemoteAccessIpAddress = item.RemoteAccessIp
				}, val2.PcName, val2.ShareKey, val2.PcIp) && !string.IsNullOrWhiteSpace(val2.TeamName))
				{
					return val2.TeamName.Trim();
				}
			}
		}
		return "미지정";
	}

	private void ApplyOccupancyFilter()
	{
		string text = (OccupancySearchText ?? string.Empty).Trim();
		Occupancies.Clear();
		for (int i = 0; i < _allOccupancies.Count; i++)
		{
			AdminOccupancyItemViewModel adminOccupancyItemViewModel = _allOccupancies[i];
			if (adminOccupancyItemViewModel != null && (text.Length <= 0 || adminOccupancyItemViewModel.MatchesSearch(text)))
			{
				Occupancies.Add(adminOccupancyItemViewModel);
			}
		}
	}

	private void SelectOccupancy(AdminOccupancyItemViewModel item)
	{
		if (item == null)
		{
			return;
		}
		SelectedOccupancy = item;
		for (int i = 0; i < Occupancies.Count; i++)
		{
			AdminOccupancyItemViewModel adminOccupancyItemViewModel = Occupancies[i];
			if (adminOccupancyItemViewModel != null)
			{
				adminOccupancyItemViewModel.IsSelected = adminOccupancyItemViewModel == item;
			}
		}
	}

	private async Task ReloadUsageLogsAsync()
	{
		UsageLogStatusMessage = string.Empty;
		if (!OccupancyNameStore.IsAdmin)
		{
			_allUsageLogs.Clear();
			UsageLogs.Clear();
			UsageLogCount = 0;
			SelectedUsageLog = null;
			return;
		}

		IList<RemotePcUsageLogDto> rows;
		try
		{
			rows = await new RemotePcShareBiz()
				.GetUsageLogsForAdminAsync(null, 500)
				.ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			UsageLogStatusMessage = "접속 이력 조회 실패: " + ex.Message;
			return;
		}

		_allUsageLogs.Clear();
		if (rows != null)
		{
			for (int i = 0; i < rows.Count; i++)
			{
				AdminUsageLogItemViewModel item = AdminUsageLogItemViewModel.FromDto(rows[i]);
				if (item != null)
					_allUsageLogs.Add(item);
			}
		}
		ApplyUsageLogFilter();
	}

	private void ApplyUsageLogFilter()
	{
		UsageLogs.Clear();
		UsageLogSiteGroups.Clear();
		string q = UsageLogSearchText;
		string siteFilter = UsageLogSiteFilter;
		var matched = new List<AdminUsageLogItemViewModel>();
		for (int i = 0; i < _allUsageLogs.Count; i++)
		{
			AdminUsageLogItemViewModel row = _allUsageLogs[i];
			if (row == null || !row.MatchesSearch(q))
				continue;
			if (!MatchesSiteFilter(row.SiteCode, siteFilter))
				continue;
			if (!MatchesUsageLogDateFilter(row))
				continue;
			matched.Add(row);
			UsageLogs.Add(row);
		}
		RebuildUsageLogSiteGroups(matched);
		UsageLogCount = matched.Count;
		if (SelectedUsageLog != null)
		{
			bool still = false;
			for (int i = 0; i < matched.Count; i++)
			{
				if (matched[i] != null && matched[i].LogId == SelectedUsageLog.LogId)
				{
					still = true;
					SelectUsageLog(matched[i]);
					break;
				}
			}
			if (!still)
				SelectedUsageLog = null;
		}
	}

	private void RebuildUsageLogSiteGroups(List<AdminUsageLogItemViewModel> matched)
	{
		UsageLogSiteGroups.Clear();
		if (matched == null || matched.Count == 0)
			return;

		bool isAllSites = string.IsNullOrWhiteSpace(UsageLogSiteFilter)
			|| string.Equals(UsageLogSiteFilter, "ALL", StringComparison.OrdinalIgnoreCase);

		if (isAllSites)
		{
			// "전체" 선택 시에는 사이트별로 묶지 않고 시간순 하나의 목록으로 보여준다 —
			// 각 항목의 사이트는 카드에 작은 라벨(ShowSiteBadge)로만 표시한다.
			for (int i = 0; i < matched.Count; i++)
			{
				if (matched[i] != null)
					matched[i].ShowSiteBadge = true;
			}
			var flatGroup = new AdminUsageLogSiteGroupViewModel(null) { ShowHeader = false };
			BuildUsageLogDateGroups(matched, flatGroup.DateGroups);
			if (flatGroup.DateGroups.Count > 0)
				UsageLogSiteGroups.Add(flatGroup);
			return;
		}

		var bySite = new Dictionary<string, List<AdminUsageLogItemViewModel>>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < matched.Count; i++)
		{
			AdminUsageLogItemViewModel row = matched[i];
			if (row == null)
				continue;
			row.ShowSiteBadge = false;
			string site = NormalizeSiteKey(row.SiteCode);
			List<AdminUsageLogItemViewModel> list;
			if (!bySite.TryGetValue(site, out list))
			{
				list = new List<AdminUsageLogItemViewModel>();
				bySite[site] = list;
			}
			list.Add(row);
		}

		List<string> sites = new List<string>(bySite.Keys);
		sites.Sort(CompareSiteKeys);
		for (int s = 0; s < sites.Count; s++)
		{
			string site = sites[s];
			var siteGroup = new AdminUsageLogSiteGroupViewModel(site);
			BuildUsageLogDateGroups(bySite[site], siteGroup.DateGroups);
			UsageLogSiteGroups.Add(siteGroup);
		}
	}

	private static void BuildUsageLogDateGroups(List<AdminUsageLogItemViewModel> rows, ObservableCollection<AdminUsageLogDateGroupViewModel> target)
	{
		var byDate = new Dictionary<DateTime, AdminUsageLogDateGroupViewModel>();
		AdminUsageLogDateGroupViewModel unknown = null;
		for (int i = 0; i < rows.Count; i++)
		{
			AdminUsageLogItemViewModel row = rows[i];
			if (row == null)
				continue;
			if (!row.RequestedAt.HasValue)
			{
				if (unknown == null)
					unknown = new AdminUsageLogDateGroupViewModel(null);
				unknown.Items.Add(row);
				continue;
			}
			DateTime day = row.RequestedAt.Value.Date;
			AdminUsageLogDateGroupViewModel dayGroup;
			if (!byDate.TryGetValue(day, out dayGroup))
			{
				dayGroup = new AdminUsageLogDateGroupViewModel(day);
				byDate[day] = dayGroup;
			}
			dayGroup.Items.Add(row);
		}

		List<DateTime> days = new List<DateTime>(byDate.Keys);
		days.Sort((a, b) => b.CompareTo(a));
		for (int d = 0; d < days.Count; d++)
		{
			AdminUsageLogDateGroupViewModel dayGroup = byDate[days[d]];
			List<AdminUsageLogItemViewModel> ordered = new List<AdminUsageLogItemViewModel>(dayGroup.Items);
			ordered.Sort((a, b) => Nullable.Compare(b.RequestedAt, a.RequestedAt));
			dayGroup.Items.Clear();
			for (int k = 0; k < ordered.Count; k++)
				dayGroup.Items.Add(ordered[k]);
			target.Add(dayGroup);
		}
		if (unknown != null && unknown.Items.Count > 0)
			target.Add(unknown);
	}

	private void SelectUsageLog(AdminUsageLogItemViewModel item)
	{
		SelectedUsageLog = item;
		for (int i = 0; i < UsageLogs.Count; i++)
		{
			AdminUsageLogItemViewModel row = UsageLogs[i];
			if (row != null)
				row.IsSelected = row == item;
		}
	}

	private async Task ReloadWorkLogsAsync()
	{
		WorkLogStatusMessage = string.Empty;
		if (!OccupancyNameStore.IsAdmin)
		{
			_allWorkLogs.Clear();
			WorkLogs.Clear();
			WorkLogSiteGroups.Clear();
			WorkLogCount = 0;
			SelectedWorkLog = null;
			return;
		}

		WorkLogPageResult page;
		try
		{
			var persistence = new WorkLogPersistenceService(new WorkLogBiz());
			var query = persistence.BuildListQuery(
				0,
				500,
				null,
				null,
				null,
				"ALL",
				null,
				null,
				null,
				null,
				null);
			page = await persistence.GetPageAsync(query).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			WorkLogStatusMessage = "업무기록 조회 실패: " + ex.Message;
			return;
		}

		_allWorkLogs.Clear();
		if (page != null && page.Items != null)
		{
			for (int i = 0; i < page.Items.Count; i++)
			{
				WorkLogListItemViewModel mapped = WorkLogDbMapper.FromDto(page.Items[i]);
				AdminWorkLogItemViewModel item = AdminWorkLogItemViewModel.FromListItem(mapped);
				if (item != null)
					_allWorkLogs.Add(item);
			}
		}
		ApplyWorkLogFilter();
	}

	private void ApplyWorkLogFilter()
	{
		WorkLogs.Clear();
		WorkLogSiteGroups.Clear();
		string q = WorkLogSearchText;
		string siteFilter = WorkLogSiteFilter;
		var matched = new List<AdminWorkLogItemViewModel>();
		for (int i = 0; i < _allWorkLogs.Count; i++)
		{
			AdminWorkLogItemViewModel row = _allWorkLogs[i];
			if (row == null || !row.MatchesSearch(q))
				continue;
			if (!MatchesSiteFilter(row.SiteCode, siteFilter))
				continue;
			matched.Add(row);
			WorkLogs.Add(row);
		}
		RebuildWorkLogSiteGroups(matched);
		WorkLogCount = matched.Count;
		if (SelectedWorkLog != null)
		{
			bool still = false;
			for (int i = 0; i < matched.Count; i++)
			{
				if (matched[i] != null && matched[i].DbLogId == SelectedWorkLog.DbLogId)
				{
					still = true;
					SelectWorkLog(matched[i]);
					break;
				}
			}
			if (!still)
				SelectedWorkLog = null;
		}
	}

	private void OpenWorkLogDatePicker()
	{
		_draftStartDate = null;
		_draftEndDate = null;
		if (WorkLogListHost != null)
		{
			DateTime? from = WorkLogPersistenceService.ParseFilterDateOrNull(WorkLogListHost.FilterFromText);
			DateTime? to = WorkLogPersistenceService.ParseFilterDateOrNull(WorkLogListHost.FilterToText);
			_draftStartDate = from;
			_draftEndDate = to ?? from;
			if (from.HasValue)
				_calendarMonth = new DateTime(from.Value.Year, from.Value.Month, 1);
		}
		RebuildWorkLogCalendarDays();
		IsWorkLogDatePickerOpen = true;
	}

	private void PrevWorkLogCalendarMonth()
	{
		_calendarMonth = _calendarMonth.AddMonths(-1);
		RaisePropertyChanged("WorkLogCalendarMonthTitle");
		RebuildWorkLogCalendarDays();
	}

	private void NextWorkLogCalendarMonth()
	{
		_calendarMonth = _calendarMonth.AddMonths(1);
		RaisePropertyChanged("WorkLogCalendarMonthTitle");
		RebuildWorkLogCalendarDays();
	}

	private void SelectWorkLogCalendarDay(DateTime day)
	{
		DateTime d = day.Date;
		if (!_draftStartDate.HasValue || (_draftStartDate.HasValue && _draftEndDate.HasValue))
		{
			_draftStartDate = d;
			_draftEndDate = null;
		}
		else if (d < _draftStartDate.Value.Date)
		{
			_draftEndDate = _draftStartDate;
			_draftStartDate = d;
		}
		else
		{
			_draftEndDate = d;
		}
		RebuildWorkLogCalendarDays();
	}

	private void ApplyWorkLogDateFilter()
	{
		if (!_draftStartDate.HasValue || WorkLogListHost == null)
			return;
		DateTime start = _draftStartDate.Value.Date;
		DateTime end = (_draftEndDate ?? _draftStartDate).Value.Date;
		if (end < start)
		{
			DateTime swap = start;
			start = end;
			end = swap;
		}
		WorkLogListHost.FilterFromText = start.ToString("yyyy-MM-dd");
		WorkLogListHost.FilterToText = end.ToString("yyyy-MM-dd");
		IsWorkLogDatePickerOpen = false;
		RaisePropertyChanged("WorkLogDateChipText");
		RaisePropertyChanged("HasWorkLogDateFilter");
	}

	private void ClearWorkLogDateFilter()
	{
		_draftStartDate = null;
		_draftEndDate = null;
		if (WorkLogListHost != null)
		{
			WorkLogListHost.FilterFromText = string.Empty;
			WorkLogListHost.FilterToText = string.Empty;
		}
		IsWorkLogDatePickerOpen = false;
		RebuildWorkLogCalendarDays();
		RaisePropertyChanged("WorkLogDateChipText");
		RaisePropertyChanged("HasWorkLogDateFilter");
	}

	private void RebuildWorkLogCalendarDays()
	{
		if (WorkLogCalendarDays == null)
			return;
		WorkLogCalendarDays.Clear();
		DateTime first = new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1);
		DateTime start = first.AddDays(-(int)first.DayOfWeek);
		int daysInMonth = DateTime.DaysInMonth(_calendarMonth.Year, _calendarMonth.Month);
		int cellCount = (((int)first.DayOfWeek + daysInMonth + 6) / 7) * 7;
		DateTime? rangeStart = _draftStartDate;
		DateTime? rangeEnd = _draftEndDate ?? _draftStartDate;
		for (int i = 0; i < cellCount; i++)
		{
			DateTime d = start.AddDays(i);
			bool currentMonth = d.Month == _calendarMonth.Month;
			bool inRange = rangeStart.HasValue
				&& rangeEnd.HasValue
				&& d.Date >= rangeStart.Value.Date
				&& d.Date <= rangeEnd.Value.Date;
			bool isStart = rangeStart.HasValue && d.Date == rangeStart.Value.Date;
			bool isEnd = rangeEnd.HasValue && d.Date == rangeEnd.Value.Date;
			bool multi = rangeStart.HasValue && rangeEnd.HasValue
				&& rangeStart.Value.Date != rangeEnd.Value.Date;
			WorkLogCalendarDays.Add(new CalendarDayItem
			{
				Date = d,
				DayText = currentMonth ? d.Day.ToString() : string.Empty,
				IsCurrentMonth = currentMonth,
				IsToday = currentMonth && d.Date == KoreaTime.Today,
				IsSunday = d.DayOfWeek == DayOfWeek.Sunday,
				IsSaturday = d.DayOfWeek == DayOfWeek.Saturday,
				IsRangeStart = isStart,
				IsRangeEnd = isEnd,
				IsInRange = inRange,
				RangeFillMid = inRange && multi && !isStart && !isEnd,
				RangeFillFromStart = inRange && multi && isStart && !isEnd,
				RangeFillToEnd = inRange && multi && isEnd && !isStart
			});
		}
		RaisePropertyChanged("WorkLogCalendarMonthTitle");
	}

	private void OpenUsageLogDatePicker()
	{
		_usageLogDraftStartDate = _usageLogFilterFrom;
		_usageLogDraftEndDate = _usageLogFilterTo ?? _usageLogFilterFrom;
		if (_usageLogDraftStartDate.HasValue)
			_usageLogCalendarMonth = new DateTime(_usageLogDraftStartDate.Value.Year, _usageLogDraftStartDate.Value.Month, 1);
		RebuildUsageLogCalendarDays();
		IsUsageLogDatePickerOpen = true;
	}

	private void PrevUsageLogCalendarMonth()
	{
		_usageLogCalendarMonth = _usageLogCalendarMonth.AddMonths(-1);
		RaisePropertyChanged("UsageLogCalendarMonthTitle");
		RebuildUsageLogCalendarDays();
	}

	private void NextUsageLogCalendarMonth()
	{
		_usageLogCalendarMonth = _usageLogCalendarMonth.AddMonths(1);
		RaisePropertyChanged("UsageLogCalendarMonthTitle");
		RebuildUsageLogCalendarDays();
	}

	private void SelectUsageLogCalendarDay(DateTime day)
	{
		DateTime d = day.Date;
		if (!_usageLogDraftStartDate.HasValue || (_usageLogDraftStartDate.HasValue && _usageLogDraftEndDate.HasValue))
		{
			_usageLogDraftStartDate = d;
			_usageLogDraftEndDate = null;
		}
		else if (d < _usageLogDraftStartDate.Value.Date)
		{
			_usageLogDraftEndDate = _usageLogDraftStartDate;
			_usageLogDraftStartDate = d;
		}
		else
		{
			_usageLogDraftEndDate = d;
		}
		RebuildUsageLogCalendarDays();
	}

	private void ApplyUsageLogDateFilter()
	{
		if (!_usageLogDraftStartDate.HasValue)
			return;
		DateTime start = _usageLogDraftStartDate.Value.Date;
		DateTime end = (_usageLogDraftEndDate ?? _usageLogDraftStartDate).Value.Date;
		if (end < start)
		{
			DateTime swap = start;
			start = end;
			end = swap;
		}
		_usageLogFilterFrom = start;
		_usageLogFilterTo = end;
		IsUsageLogDatePickerOpen = false;
		RaisePropertyChanged("UsageLogDateChipText");
		RaisePropertyChanged("HasUsageLogDateFilter");
		ApplyUsageLogFilter();
	}

	private void ClearUsageLogDateFilter()
	{
		_usageLogDraftStartDate = null;
		_usageLogDraftEndDate = null;
		_usageLogFilterFrom = null;
		_usageLogFilterTo = null;
		IsUsageLogDatePickerOpen = false;
		RebuildUsageLogCalendarDays();
		RaisePropertyChanged("UsageLogDateChipText");
		RaisePropertyChanged("HasUsageLogDateFilter");
		ApplyUsageLogFilter();
	}

	private void RebuildUsageLogCalendarDays()
	{
		if (UsageLogCalendarDays == null)
			return;
		UsageLogCalendarDays.Clear();
		DateTime first = new DateTime(_usageLogCalendarMonth.Year, _usageLogCalendarMonth.Month, 1);
		DateTime start = first.AddDays(-(int)first.DayOfWeek);
		int daysInMonth = DateTime.DaysInMonth(_usageLogCalendarMonth.Year, _usageLogCalendarMonth.Month);
		int cellCount = (((int)first.DayOfWeek + daysInMonth + 6) / 7) * 7;
		DateTime? rangeStart = _usageLogDraftStartDate;
		DateTime? rangeEnd = _usageLogDraftEndDate ?? _usageLogDraftStartDate;
		for (int i = 0; i < cellCount; i++)
		{
			DateTime d = start.AddDays(i);
			bool currentMonth = d.Month == _usageLogCalendarMonth.Month;
			bool inRange = rangeStart.HasValue
				&& rangeEnd.HasValue
				&& d.Date >= rangeStart.Value.Date
				&& d.Date <= rangeEnd.Value.Date;
			bool isStart = rangeStart.HasValue && d.Date == rangeStart.Value.Date;
			bool isEnd = rangeEnd.HasValue && d.Date == rangeEnd.Value.Date;
			bool multi = rangeStart.HasValue && rangeEnd.HasValue
				&& rangeStart.Value.Date != rangeEnd.Value.Date;
			UsageLogCalendarDays.Add(new CalendarDayItem
			{
				Date = d,
				DayText = currentMonth ? d.Day.ToString() : string.Empty,
				IsCurrentMonth = currentMonth,
				IsToday = currentMonth && d.Date == KoreaTime.Today,
				IsSunday = d.DayOfWeek == DayOfWeek.Sunday,
				IsSaturday = d.DayOfWeek == DayOfWeek.Saturday,
				IsRangeStart = isStart,
				IsRangeEnd = isEnd,
				IsInRange = inRange,
				RangeFillMid = inRange && multi && !isStart && !isEnd,
				RangeFillFromStart = inRange && multi && isStart && !isEnd,
				RangeFillToEnd = inRange && multi && isEnd && !isStart
			});
		}
		RaisePropertyChanged("UsageLogCalendarMonthTitle");
	}

	private void RebuildWorkLogSiteGroups(List<AdminWorkLogItemViewModel> matched)
	{
		WorkLogSiteGroups.Clear();
		if (matched == null || matched.Count == 0)
			return;

		var bySite = new Dictionary<string, List<AdminWorkLogItemViewModel>>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < matched.Count; i++)
		{
			AdminWorkLogItemViewModel row = matched[i];
			if (row == null)
				continue;
			string site = NormalizeSiteKey(row.SiteCode);
			List<AdminWorkLogItemViewModel> list;
			if (!bySite.TryGetValue(site, out list))
			{
				list = new List<AdminWorkLogItemViewModel>();
				bySite[site] = list;
			}
			list.Add(row);
		}

		List<string> sites = new List<string>(bySite.Keys);
		sites.Sort(CompareSiteKeys);
		for (int s = 0; s < sites.Count; s++)
		{
			string site = sites[s];
			var siteGroup = new AdminWorkLogSiteGroupViewModel(site);
			List<AdminWorkLogItemViewModel> rows = bySite[site];
			var byDate = new Dictionary<DateTime, AdminWorkLogDateGroupViewModel>();
			AdminWorkLogDateGroupViewModel unknown = null;
			for (int i = 0; i < rows.Count; i++)
			{
				AdminWorkLogItemViewModel row = rows[i];
				if (row == null)
					continue;
				if (!row.WorkDate.HasValue)
				{
					if (unknown == null)
						unknown = new AdminWorkLogDateGroupViewModel(null);
					unknown.Items.Add(row);
					continue;
				}
				DateTime day = row.WorkDate.Value.Date;
				AdminWorkLogDateGroupViewModel dayGroup;
				if (!byDate.TryGetValue(day, out dayGroup))
				{
					dayGroup = new AdminWorkLogDateGroupViewModel(day);
					byDate[day] = dayGroup;
				}
				dayGroup.Items.Add(row);
			}

			List<DateTime> days = new List<DateTime>(byDate.Keys);
			days.Sort((a, b) => b.CompareTo(a));
			for (int d = 0; d < days.Count; d++)
				siteGroup.DateGroups.Add(byDate[days[d]]);
			if (unknown != null && unknown.Items.Count > 0)
				siteGroup.DateGroups.Add(unknown);
			WorkLogSiteGroups.Add(siteGroup);
		}
	}

	private void SelectWorkLog(AdminWorkLogItemViewModel item)
	{
		SelectedWorkLog = item;
		for (int i = 0; i < WorkLogs.Count; i++)
		{
			AdminWorkLogItemViewModel row = WorkLogs[i];
			if (row != null)
				row.IsSelected = row == item;
		}
	}

	private Task ViewWorkLogAsync()
	{
		AdminWorkLogItemViewModel item = SelectedWorkLog;
		if (item == null)
			return Task.FromResult(0);

		WorkLogListItemViewModel source = item.SourceItem;
		if (source == null && item.DbLogId > 0)
		{
			// fallback: reload single via page cache only
			for (int i = 0; i < _allWorkLogs.Count; i++)
			{
				if (_allWorkLogs[i] != null && _allWorkLogs[i].DbLogId == item.DbLogId)
				{
					source = _allWorkLogs[i].SourceItem;
					break;
				}
			}
		}

		if (source == null)
		{
			WorkLogStatusMessage = "상세를 열 수 없습니다. 목록을 새로고침해 주세요.";
			return Task.FromResult(0);
		}

		if (_openWorkLog != null)
			_openWorkLog(source);
		else
			WorkLogStatusMessage = "상세 화면 연결이 없습니다.";
		return Task.FromResult(0);
	}

	private async Task DeleteWorkLogAsync()
	{
		AdminWorkLogItemViewModel item = SelectedWorkLog;
		if (item == null || !OccupancyNameStore.IsAdmin || item.DbLogId <= 0)
			return;

		if (_popup != null)
		{
			PopupResult confirm = await _popup.ShowConfirmAsync(new PopupRequest
			{
				Title = "업무기록 삭제",
				Icon = PopupIconKind.Warning,
				Message = item.Title + "\n" + item.Subtitle + "\n\n이 업무기록을 삭제할까요?",
				Buttons = new PopupButtonDefinition[2]
				{
					new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
					new PopupButtonDefinition("삭제", PopupResultType.Primary, isDefault: true, isCancel: false)
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
			if (confirm == null || !confirm.IsPrimary || confirm.IsCancelOrClosed)
				return;
		}

		try
		{
			bool ok = await new WorkLogBiz().DeleteAsync(item.DbLogId).ConfigureAwait(continueOnCapturedContext: true);
			if (!ok)
			{
				WorkLogStatusMessage = "업무기록 삭제에 실패했습니다.";
				return;
			}
		}
		catch (Exception ex)
		{
			WorkLogStatusMessage = "업무기록 삭제 실패: " + ex.Message;
			return;
		}

		SelectedWorkLog = null;
		WorkLogStatusMessage = "업무기록을 삭제했습니다.";
		await ReloadWorkLogsAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	private static string NormalizeSiteKey(string siteCode)
	{
		if (string.IsNullOrWhiteSpace(siteCode))
			return "미지정";
		return siteCode.Trim().ToUpperInvariant();
	}

	private static bool MatchesSiteFilter(string siteCode, string filter)
	{
		if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "ALL", StringComparison.OrdinalIgnoreCase))
			return true;
		return string.Equals(NormalizeSiteKey(siteCode), filter.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private bool MatchesUsageLogDateFilter(AdminUsageLogItemViewModel row)
	{
		if (!_usageLogFilterFrom.HasValue || !_usageLogFilterTo.HasValue)
			return true;
		if (row == null || !row.RequestedAt.HasValue)
			return false;
		DateTime d = row.RequestedAt.Value.Date;
		return d >= _usageLogFilterFrom.Value.Date && d <= _usageLogFilterTo.Value.Date;
	}

	private static int CompareSiteKeys(string a, string b)
	{
		int ia = SiteSortIndex(a);
		int ib = SiteSortIndex(b);
		if (ia != ib)
			return ia.CompareTo(ib);
		return string.Compare(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	private static int SiteSortIndex(string site)
	{
		if (string.IsNullOrWhiteSpace(site) || string.Equals(site, "미지정", StringComparison.Ordinal))
			return 100;
		for (int i = 0; i < HistorySites.Length; i++)
		{
			if (string.Equals(HistorySites[i], site, StringComparison.OrdinalIgnoreCase))
				return i;
		}
		return 50;
	}

	private async Task EditUsageLogAsync()
	{
		AdminUsageLogItemViewModel item = SelectedUsageLog;
		if (item == null || !OccupancyNameStore.IsAdmin || _popup == null)
			return;

		PopupResult prompt = await _popup.ShowPromptAsync(new PopupRequest
		{
			Title = "접속 이력 수정",
			Icon = PopupIconKind.Info,
			Message = item.Title + "\n접속자명을 수정합니다.",
			InputText = item.AccessUserId ?? string.Empty,
			Buttons = new PopupButtonDefinition[2]
			{
				new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
				new PopupButtonDefinition("저장", PopupResultType.Primary, isDefault: true, isCancel: false)
			}
		}).ConfigureAwait(continueOnCapturedContext: true);

		if (prompt == null || !prompt.IsPrimary || prompt.IsCancelOrClosed)
			return;

		string nextUser = (prompt.InputText ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(nextUser))
		{
			UsageLogStatusMessage = "접속자명을 입력해 주세요.";
			return;
		}

		string err = await new RemotePcShareBiz()
			.UpdateUsageLogForAdminAsync(item.LogId, nextUser, null, null, null)
			.ConfigureAwait(continueOnCapturedContext: true);
		if (!string.IsNullOrWhiteSpace(err))
		{
			UsageLogStatusMessage = err;
			return;
		}

		UsageLogStatusMessage = "접속 이력을 수정했습니다.";
		await ReloadUsageLogsAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	private async Task DeleteUsageLogAsync()
	{
		AdminUsageLogItemViewModel item = SelectedUsageLog;
		if (item == null || !OccupancyNameStore.IsAdmin)
			return;

		if (_popup != null)
		{
			PopupResult confirm = await _popup.ShowConfirmAsync(new PopupRequest
			{
				Title = "접속 이력 삭제",
				Icon = PopupIconKind.Warning,
				Message = item.Title + "\n" + item.Subtitle + "\n\n이 이력을 삭제할까요?",
				Buttons = new PopupButtonDefinition[2]
				{
					new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
					new PopupButtonDefinition("삭제", PopupResultType.Primary, isDefault: true, isCancel: false)
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
			if (confirm == null || !confirm.IsPrimary || confirm.IsCancelOrClosed)
				return;
		}

		string err = await new RemotePcShareBiz()
			.DeleteUsageLogForAdminAsync(item.LogId)
			.ConfigureAwait(continueOnCapturedContext: true);
		if (!string.IsNullOrWhiteSpace(err))
		{
			UsageLogStatusMessage = err;
			return;
		}

		SelectedUsageLog = null;
		UsageLogStatusMessage = "접속 이력을 삭제했습니다.";
		await ReloadUsageLogsAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	private async Task ForceReleaseOccupancyAsync(AdminOccupancyItemViewModel item)
	{
		if (item == null || !item.CanForceRelease || !OccupancyNameStore.IsAdmin)
		{
			return;
		}
		if (_popup != null)
		{
			PopupResult confirm = await _popup.ShowConfirmAsync(new PopupRequest
			{
				Title = "점유 강제 해제",
				Icon = PopupIconKind.Warning,
				Message = item.Title + "\n" + item.Subtitle + "\n\n강제 해제할까요?",
				Buttons = new PopupButtonDefinition[2]
				{
					new PopupButtonDefinition("취소", PopupResultType.Cancel, isDefault: false, isCancel: true),
					new PopupButtonDefinition("해제", PopupResultType.Primary, isDefault: true)
				}
			}).ConfigureAwait(continueOnCapturedContext: true);
			if (confirm == null || !confirm.IsPrimary || confirm.IsCancelOrClosed)
			{
				return;
			}
		}
		string err = await new RemotePcShareBiz().ForceReleaseForAdminAsync(item.RemoteAccessIp, item.SessionToken).ConfigureAwait(continueOnCapturedContext: true);
		if (!string.IsNullOrWhiteSpace(err))
		{
			OccupancyStatusMessage = err;
			return;
		}
		OccupancyStatusMessage = "점유를 해제했습니다.";
		await ReloadOccupancyAsync().ConfigureAwait(continueOnCapturedContext: true);
	}
}
}
