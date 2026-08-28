namespace GBCWorkHub.UI.Models.Popup
{
    public enum PopupResultType
    {
        None = 0,
        Primary = 1,
        Secondary = 2,
        Tertiary = 3,
        Cancel = 4,
        Closed = 5
    }

    public enum PopupIconKind
    {
        None = 0,
        Info = 1,
        Question = 2,
        Success = 3,
        Warning = 4,
        Error = 5
    }

    public enum PopupKind
    {
        Confirm = 0,
        Progress = 1,
        Result = 2
    }
}
