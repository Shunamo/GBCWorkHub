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

            // 이미 로그인(점유명)된 PC만 조용히 DB 동기화. 없으면 로그인 버튼에서 진행.
            if (!OccupancyNameStore.HasName || !OccupancyNameStore.HasAffiliation)
                return;

            await StampTeamOnLocalLogsAsync().ConfigureAwait(true);
            await UpsertDirectoryUserAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// 로그인 팝업(ADMIN 포함). 이 PC 미가입이면 회원가입 버튼으로 전환.
        /// </summary>
        public static async Task<bool> LoginAsync(IPopupService popup)
        {
            if (popup == null)
                return false;

            AuthBiz authBiz = new AuthBiz();
            LocalAuthMode mode = await Task.Run(() => authBiz.ResolveLocalAuthMode()).ConfigureAwait(true);
            // 항상 로그인 우선(다른 PC에서도 ADMIN 로그인 가능). 미가입 PC는 회원가입 버튼 제공.
            bool ok = await ShowLoginPopupAsync(popup, authBiz, offerRegister: mode == LocalAuthMode.Register)
                .ConfigureAwait(true);
            return ok;
        }

        private static async Task<bool> ShowLoginPopupAsync(IPopupService popup, AuthBiz authBiz, bool offerRegister)
        {
            string ip = RemotePcShareBiz.LocalAccessIp;
            if (string.IsNullOrWhiteSpace(ip))
                ip = "-";

            var existing = await Task.Run(() => authBiz.FindLocalPcAccount()).ConfigureAwait(true);

            var buttons = new System.Collections.Generic.List<PopupButtonDefinition>
            {
                new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                new PopupButtonDefinition("비밀번호 재설정", PopupResultType.Tertiary)
            };
            if (offerRegister)
                buttons.Add(new PopupButtonDefinition("회원가입", PopupResultType.Secondary));
            buttons.Add(new PopupButtonDefinition("로그인", PopupResultType.Primary, isDefault: true));

            var result = await popup.ShowPromptAsync(new PopupRequest
            {
                Title = "로그인",
                Icon = PopupIconKind.Info,
                Message = string.Empty,
                InputText = existing != null ? (existing.UserName ?? string.Empty) : string.Empty,
                ShowAffiliationInput = true,
                RequireAffiliation = false,
                ShowPasswordInput = true,
                RequirePassword = true,
                AffiliationText = existing != null ? (existing.TeamName ?? string.Empty) : string.Empty,
                InfoIp = ip,
                InfoWindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                Buttons = buttons,
                PrimaryValidator = snap => CompleteAuthInPopupAsync(
                    "로그인",
                    () => new AuthBiz().Login(
                        snap.InputText,
                        snap.PasswordText,
                        TrimAffiliation(snap.AffiliationText)))
            }).ConfigureAwait(true);

            if (result != null && result.IsTertiary)
                return await ResetPasswordAsync(popup).ConfigureAwait(true);

            if (result != null && result.IsSecondary)
                return await RegisterAsync(popup).ConfigureAwait(true);

            return result != null && result.IsPrimary && !result.IsCancelOrClosed;
        }

        /// <summary>이 PC 계정만 현재 비번 없이 새 비번 설정 후 로그인.</summary>
        public static async Task<bool> ResetPasswordAsync(IPopupService popup)
        {
            if (popup == null)
                return false;

            string ip = RemotePcShareBiz.LocalAccessIp;
            if (string.IsNullOrWhiteSpace(ip))
                ip = "-";

            var existing = await Task.Run(() => new AuthBiz().FindLocalPcAccount()).ConfigureAwait(true);

            var result = await popup.ShowPromptAsync(new PopupRequest
            {
                Title = "비밀번호 재설정",
                Icon = PopupIconKind.Info,
                Message = "이 PC에 등록된 계정만 가능합니다.",
                InputText = existing != null ? (existing.UserName ?? string.Empty) : string.Empty,
                ShowAffiliationInput = false,
                ShowPasswordInput = true,
                RequirePassword = true,
                ShowPasswordConfirm = true,
                InfoIp = ip,
                InfoWindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                Buttons = new[]
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("재설정", PopupResultType.Primary, isDefault: true)
                },
                PrimaryValidator = snap =>
                {
                    string err = new AuthBiz().ResetPasswordOnThisPc(snap.InputText, snap.PasswordText);
                    if (!string.IsNullOrWhiteSpace(err))
                        return System.Threading.Tasks.Task.FromResult(err);

                    // 재설정 성공 → 바로 로그인 세션
                    return CompleteAuthInPopupAsync(
                        "로그인",
                        () => new AuthBiz().Login(
                            snap.InputText,
                            snap.PasswordText,
                            TrimAffiliation(existing != null ? existing.TeamName : null)));
                }
            }).ConfigureAwait(true);

            return result != null && result.IsPrimary && !result.IsCancelOrClosed;
        }

        /// <summary>신규 회원가입 후 바로 로그인 세션.</summary>
        public static async Task<bool> RegisterAsync(IPopupService popup)
        {
            if (popup == null)
                return false;

            string ip = RemotePcShareBiz.LocalAccessIp;
            if (string.IsNullOrWhiteSpace(ip))
                ip = "-";

            var result = await popup.ShowPromptAsync(new PopupRequest
            {
                Title = "회원가입",
                Icon = PopupIconKind.Info,
                InputText = string.Empty,
                ShowAffiliationInput = true,
                RequireAffiliation = true,
                AffiliationOptions = new System.Collections.Generic.List<string> { "진료지원", "진료간호", "원무", "기타", "직접입력" },
                AffiliationCustomOptionLabel = "직접입력",
                ShowPasswordInput = true,
                RequirePassword = true,
                ShowPasswordConfirm = true,
                AffiliationText = string.Empty,
                InfoIp = ip,
                InfoWindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                Buttons = new[]
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("가입하기", PopupResultType.Primary, isDefault: true)
                },
                PrimaryValidator = snap => CompleteAuthInPopupAsync(
                    "회원가입",
                    () => new AuthBiz().Register(
                        snap.InputText,
                        snap.PasswordText,
                        TrimAffiliation(snap.AffiliationText)))
            }).ConfigureAwait(true);

            return result != null && result.IsPrimary && !result.IsCancelOrClosed;
        }

        /// <summary>성공 시 세션 저장 후 null, 실패 시 팝업 내 오류 문구.</summary>
        private static async Task<string> CompleteAuthInPopupAsync(
            string title,
            System.Func<AuthLoginResult> run)
        {
            AuthLoginResult auth;
            try
            {
                auth = await Task.Run(run).ConfigureAwait(true);
            }
            catch (System.Exception ex)
            {
                DiagnosticLogger.Warn("AUTH", title + " failed: " + ex.Message);
                return title + " 중 오류가 발생했습니다. " + ex.Message;
            }

            if (auth == null || !auth.Success)
            {
                return auth != null && !string.IsNullOrWhiteSpace(auth.ErrorMessage)
                    ? auth.ErrorMessage
                    : title + "에 실패했습니다.";
            }

            OccupancyNameStore.Save(auth.UserName, auth.TeamName, auth.IsAdmin, auth.UserId);
            await StampTeamOnLocalLogsAsync().ConfigureAwait(true);
            return null;
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

        public static async Task<bool> ChangePasswordAsync(IPopupService popup)
        {
            if (popup == null)
                return false;
            if (!OccupancyNameStore.HasName)
                return false;

            var result = await popup.ShowPromptAsync(new PopupRequest
            {
                Title = "비밀번호 변경",
                Icon = PopupIconKind.Info,
                ShowInput = false,
                ShowCurrentPasswordInput = true,
                ShowPasswordInput = true,
                RequirePassword = true,
                ShowPasswordConfirm = true,
                Buttons = new[]
                {
                    new PopupButtonDefinition("취소", PopupResultType.Cancel, isCancel: true),
                    new PopupButtonDefinition("변경", PopupResultType.Primary, isDefault: true)
                },
                PrimaryValidator = snap =>
                {
                    string err = new AuthBiz().ChangePassword(
                        snap.CurrentPasswordText,
                        snap.PasswordText);
                    return System.Threading.Tasks.Task.FromResult(err);
                }
            }).ConfigureAwait(true);

            return result != null && result.IsPrimary && !result.IsCancelOrClosed;
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
