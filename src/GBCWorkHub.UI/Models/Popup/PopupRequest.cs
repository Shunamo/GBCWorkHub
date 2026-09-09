using System.Collections.Generic;

namespace GBCWorkHub.UI.Models.Popup
{
    public sealed class PopupRequest
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string Detail { get; set; }
        public PopupIconKind Icon { get; set; }
        public PopupKind Kind { get; set; }
        public string ProgressStepText { get; set; }
        public bool ShowCancelOnProgress { get; set; }
        public IList<PopupButtonDefinition> Buttons { get; set; }
        public string DedupKey { get; set; }
        public bool ShowInput { get; set; }
        public string InputText { get; set; }
        public bool ShowAffiliationInput { get; set; }
        public bool RequireAffiliation { get; set; }
        public string AffiliationText { get; set; }
        /// <summary>범용 세 번째 텍스트 입력란(예: 로그인ID 수정) — 라벨은 호출자가 지정.</summary>
        public bool ShowSecondaryInput { get; set; }
        public bool RequireSecondaryInput { get; set; }
        public string SecondaryInputLabel { get; set; }
        public string SecondaryInputText { get; set; }
        public bool ShowPasswordInput { get; set; }
        public bool RequirePassword { get; set; }
        public string PasswordText { get; set; }
        /// <summary>비밀번호 변경: 현재 비밀번호 입력란.</summary>
        public bool ShowCurrentPasswordInput { get; set; }
        public string CurrentPasswordText { get; set; }
        public bool ShowPasswordConfirm { get; set; }
        public string PasswordConfirmText { get; set; }
        public string InfoIp { get; set; }
        public string InfoWindowsAccount { get; set; }

        /// <summary>
        /// Primary 클릭 후 추가 검증. null/빈문자면 닫기, 문자열이면 InputError로 표시하고 유지.
        /// </summary>
        public System.Func<PopupHostViewModelSnapshot, System.Threading.Tasks.Task<string>> PrimaryValidator { get; set; }

        public PopupRequest()
        {
            Icon = PopupIconKind.Info;
            Kind = PopupKind.Confirm;
            Buttons = new List<PopupButtonDefinition>();
        }
    }

    /// <summary>팝업 PrimaryValidator용 입력 스냅샷(UI 어셈블리 순환 참조 방지).</summary>
    public sealed class PopupHostViewModelSnapshot
    {
        public string InputText { get; set; }
        public string AffiliationText { get; set; }
        public string SecondaryInputText { get; set; }
        public string PasswordText { get; set; }
        public string CurrentPasswordText { get; set; }
        public string PasswordConfirmText { get; set; }
    }
}
