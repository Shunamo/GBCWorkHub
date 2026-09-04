using System.Threading.Tasks;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.WorkLog;
using GBCWorkHub.UI.Models.Popup;
using GBCWorkHub.UI.Services.Popup;

namespace GBCWorkHub.UI.Services
{
    public static class OccupancyNamePrompt
    {
        public static async Task EnsureAsync(IPopupService popup)
        {
            if (popup == null)
                return;

            if (!OccupancyNameStore.HasName || !OccupancyNameStore.HasAffiliation)
            {
                bool nameOnly = OccupancyNameStore.HasName;
                string ip = RemotePcShareBiz.LocalAccessIp;
                if (string.IsNullOrWhiteSpace(ip))
                    ip = "-";

                var result = await popup.ShowPromptAsync(new PopupRequest
                {
                    Title = "사용자 정보",
                    Icon = PopupIconKind.Info,
                    InputText = OccupancyNameStore.TryGet() ?? string.Empty,
                    ShowAffiliationInput = true,
                    RequireAffiliation = true,
                    AffiliationText = OccupancyNameStore.TryGetAffiliation() ?? string.Empty,
                    InfoIp = ip,
                    InfoWindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                    Buttons = new[]
                    {
                        new PopupButtonDefinition(nameOnly ? "저장" : "시작하기", PopupResultType.Primary, isDefault: true)
                    }
                }).ConfigureAwait(true);

                if (result == null || !result.IsPrimary || string.IsNullOrWhiteSpace(result.InputText))
                    return;

                OccupancyNameStore.Save(result.InputText.Trim(), TrimAffiliation(result.AffiliationText));
            }

            await StampTeamOnLocalLogsAsync().ConfigureAwait(true);
            await UpsertDirectoryUserAsync().ConfigureAwait(true);
        }

        public static async Task<bool> ChangeAsync(IPopupService popup)
        {
            if (popup == null)
                return false;

            string current = OccupancyNameStore.TryGet() ?? string.Empty;
            string currentAffiliation = OccupancyNameStore.TryGetAffiliation() ?? string.Empty;
            string ip = RemotePcShareBiz.LocalAccessIp;
            if (string.IsNullOrWhiteSpace(ip))
                ip = "-";

            var result = await popup.ShowPromptAsync(new PopupRequest
            {
                Title = "사용자 정보",
                Icon = PopupIconKind.Info,
                InputText = current,
                ShowAffiliationInput = true,
                AffiliationText = currentAffiliation,
                InfoIp = ip,
                InfoWindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                Buttons = new[]
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("저장", PopupResultType.Primary, isDefault: true)
                }
            }).ConfigureAwait(true);

            if (result == null || !result.IsPrimary || string.IsNullOrWhiteSpace(result.InputText))
                return false;

            string next = result.InputText.Trim();
            string nextAffiliation = TrimAffiliation(result.AffiliationText) ?? string.Empty;
            bool nameSame = string.Equals(current, next, System.StringComparison.Ordinal);
            bool affiliationSame = string.Equals(currentAffiliation, nextAffiliation, System.StringComparison.Ordinal);
            if (nameSame && affiliationSame)
                return false;

            OccupancyNameStore.Save(next, string.IsNullOrWhiteSpace(nextAffiliation) ? null : nextAffiliation);
            await UpsertDirectoryUserAsync().ConfigureAwait(true);
            return true;
        }

        public static async Task UpsertDirectoryUserAsync()
        {
            try
            {
                var biz = new DirectoryBiz();
                await biz.UpsertLocalUserAsync().ConfigureAwait(true);
            }
            catch (System.Exception ex)
            {
                DiagnosticLogger.Warn("OCCUPANCY", "사용자 마스터 반영 실패: " + ex.Message);
            }
        }

        public static async Task StampTeamOnLocalLogsAsync()
        {
            string name = OccupancyNameStore.TryGet();
            string team = OccupancyNameStore.TryGetAffiliation();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(team))
                return;

            try
            {
                var biz = new WorkLogBiz();
                string ip = WorkHubUserProfile.LocalIp;
                await biz.FillMissingTeamAsync(name, team, ip).ConfigureAwait(true);

                string windows = RemotePcShareBiz.LocalWindowsAccount;
                if (!string.IsNullOrWhiteSpace(windows)
                    && !string.Equals(windows.Trim(), name.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    await biz.FillMissingTeamAsync(windows.Trim(), team, ip).ConfigureAwait(true);
            }
            catch (System.Exception ex)
            {
                DiagnosticLogger.Warn("OCCUPANCY", "소속 DB 반영 실패: " + ex.Message);
            }
        }

        private static string TrimAffiliation(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
