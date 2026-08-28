namespace GBCWorkHub.UI.ViewModels
{
    public class RdpEventViewModel : ViewModelBase
    {
        private long _recordId;
        private int _eventId;
        private string _eventTime;
        private string _user;
        private string _sessionId;
        private string _sourceIp;
        private string _eventDescription;

        public long RecordId
        {
            get { return _recordId; }
            set { SetProperty(ref _recordId, value); }
        }

        public int EventId
        {
            get { return _eventId; }
            set { SetProperty(ref _eventId, value); }
        }

        public string EventTime
        {
            get { return _eventTime; }
            set { SetProperty(ref _eventTime, value); }
        }

        public string User
        {
            get { return _user; }
            set { SetProperty(ref _user, value); }
        }

        public string SessionId
        {
            get { return _sessionId; }
            set { SetProperty(ref _sessionId, value); }
        }

        public string SourceIp
        {
            get { return _sourceIp; }
            set { SetProperty(ref _sourceIp, value); }
        }

        public string EventDescription
        {
            get { return _eventDescription; }
            set { SetProperty(ref _eventDescription, value); }
        }
    }
}
