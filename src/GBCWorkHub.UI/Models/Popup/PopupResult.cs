namespace GBCWorkHub.UI.Models.Popup
{
    public sealed class PopupResult
    {
        public PopupResultType ResultType { get; set; }
        public string ButtonText { get; set; }

        public static PopupResult From(PopupResultType type, string buttonText = null)
        {
            return new PopupResult { ResultType = type, ButtonText = buttonText };
        }

        public bool IsPrimary
        {
            get { return ResultType == PopupResultType.Primary; }
        }

        public bool IsSecondary
        {
            get { return ResultType == PopupResultType.Secondary; }
        }

        public bool IsTertiary
        {
            get { return ResultType == PopupResultType.Tertiary; }
        }

        public bool IsCancelOrClosed
        {
            get
            {
                return ResultType == PopupResultType.Cancel
                    || ResultType == PopupResultType.Closed
                    || ResultType == PopupResultType.None;
            }
        }
    }
}
