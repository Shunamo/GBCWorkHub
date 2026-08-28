namespace GBCWorkHub.UI.Models.Popup
{
    public sealed class PopupButtonDefinition
    {
        public string Text { get; set; }
        public PopupResultType ResultType { get; set; }
        public bool IsDefault { get; set; }
        public bool IsCancel { get; set; }

        public PopupButtonDefinition()
        {
        }

        public PopupButtonDefinition(string text, PopupResultType resultType, bool isDefault = false, bool isCancel = false)
        {
            Text = text;
            ResultType = resultType;
            IsDefault = isDefault;
            IsCancel = isCancel;
        }
    }
}
