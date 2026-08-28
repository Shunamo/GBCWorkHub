using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.Services.Popup;
using GBCWorkHub.UI.Services.TfsSync;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// UI sink for session orchestration. Implemented by RemoteWorkspace (nested).
    /// </summary>
    public abstract class RemoteSessionUi
    {
        public abstract void RunOnUi(Action action);
        public abstract bool IsGalleryVisible { get; }
        public abstract bool IsCentralShareEnabled { get; set; }
        public abstract bool IsCentralDbConnected { get; set; }
        public abstract string CentralDbStatus { get; set; }
        public abstract string SelectedSiteCode { get; }
        public abstract RemoteComputerItemViewModel SelectedRemoteComputer { get; }

        public abstract void UpdateGalleryLocalInUse(string ip, string userId, string clientPc, string sessionToken);
        public abstract void UpdateGalleryLocalAvailable(string ip);
        public abstract void UpdateGalleryFromStatus(RemotePcStatus status);
        public abstract void UpdateGalleryStatusCode(string ip, string statusCode);
        public abstract void RecalculateStatusCounts();
        public abstract void RefreshGalleryFilter();
        public abstract void MergeRemoteComputersFromDb(IList<RemotePcStatus> list, Func<string, bool> isLocalActive);
        public abstract RemoteComputerItemViewModel FindGalleryItemByShareKey(string shareKey);
        public abstract RemoteComputerItemViewModel FindGalleryItemByIp(string ip);

        public abstract void ApplySharedStatusToUi(RemotePcStatus status, bool overwriteLocalStatus, bool isTracking);
        public abstract void AssignDetailField(string propertyName, string value);
        public abstract void SetRdpSessionConfirmed(bool value);
        public abstract void SetConnectionConfirmedAt(string value);
        public abstract void SetConnectionEndedAt(string value);
        public abstract void SetConnectionRequestedAt(string value);
        public abstract void SetSessionTokenDisplay(string value);
        public abstract void SetLocalAccessIp(string value);
        public abstract void SetCurrentStatus(string value);
        public abstract void SetStatusSource(string value);
        public abstract void SetLastReceiveMessage(string value);
        public abstract void ClearSessionRaw();
        public abstract void SetWorkHubUserAccount(string value);
        public abstract void SetWorkHubClientPc(string value);
        public abstract void SetRemoteComputerName(string value);
        public abstract void SetTrackedProcessIds(string value);
        public abstract void SetRdpLaunchTime(string value);
        public abstract void SetLastRefreshedAt(DateTime value);

        public abstract int MergeEvents(RdpStatusPayload payload);
        public abstract void RefreshEventListBinding();
        public abstract void AddLocalEndEvent(RdpSessionTrackingService.RdpSessionEndInfo info);

        public abstract Task LoadRecentUsageLogsAsync(RemoteComputerItemViewModel item);
        public abstract void RefreshRcVpnBadges();
        public abstract Func<Task> RefreshPcListRequested { get; }

        public abstract IPopupService Popup { get; }
        public abstract TfsSyncCoordinator TfsSync { get; }
        public abstract TfsWorkLogViewModel TfsWorkLog { get; }
        public abstract void RefreshPendingTfsBadge();
        public abstract Task PopupShowInfoAsync(string title, string message);
    }
}
