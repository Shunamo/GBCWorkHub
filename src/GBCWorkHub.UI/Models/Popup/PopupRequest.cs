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
        public string InfoIp { get; set; }
        public string InfoWindowsAccount { get; set; }

        public PopupRequest()
        {
            Icon = PopupIconKind.Info;
            Kind = PopupKind.Confirm;
            Buttons = new List<PopupButtonDefinition>();
        }
    }
}
