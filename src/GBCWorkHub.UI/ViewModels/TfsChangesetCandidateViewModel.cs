using System;
using System.Collections.Generic;
using System.Linq;
using GBCWorkHub.BIZ;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    public class TfsChangesetCandidateViewModel : ViewModelBase
    {
        private bool _isSelected;
        private string _workTitle;
        private string _workContent;
        private string _note;
        private string _saveStatus = "미저장";
        private bool _isSaved;
        private string _authorDisplay;
        private string _checkedInAtDisplay;
        private string _originalComment;
        private string _fileSummary;
        private int _changedFileCount;
        private string _collectionUrl;
        private string _serverPath;
        private int _changesetId;
        private string _authorName;
        private string _authorId;
        private string _queryMode;
        private string _remoteComputerName;
        private string _sourceClientName;
        private string _sessionToken;
        private string _sessionStartAt;
        private string _sessionEndAt;
        private DateTime? _checkedInAt;
        private List<TfsChangedFileItem> _sourceFiles = new List<TfsChangedFileItem>();

        /// <summary>파서용 원본 파일 목록 (UI 표시와 별도 보존).</summary>
        public IList<TfsChangedFileItem> SourceFiles
        {
            get { return _sourceFiles; }
        }

        public string CandidateKey
        {
            get { return TfsClipboardPayloadService.MakeCandidateKey(_collectionUrl, _changesetId); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public int ChangesetId
        {
            get { return _changesetId; }
            set { SetProperty(ref _changesetId, value); }
        }

        public string CollectionUrl
        {
            get { return _collectionUrl; }
            set { SetProperty(ref _collectionUrl, value); }
        }

        public string ServerPath
        {
            get { return _serverPath; }
            set { SetProperty(ref _serverPath, value); }
        }

        public string AuthorDisplay
        {
            get { return _authorDisplay; }
            set { SetProperty(ref _authorDisplay, value); }
        }

        public string AuthorName
        {
            get { return _authorName; }
            set { SetProperty(ref _authorName, value); }
        }

        public string AuthorId
        {
            get { return _authorId; }
            set { SetProperty(ref _authorId, value); }
        }

        public string CheckedInAtDisplay
        {
            get { return _checkedInAtDisplay; }
            set { SetProperty(ref _checkedInAtDisplay, value); }
        }

        public DateTime? CheckedInAt
        {
            get { return _checkedInAt; }
            set { SetProperty(ref _checkedInAt, value); }
        }

        public string OriginalComment
        {
            get { return _originalComment; }
            set { SetProperty(ref _originalComment, value); }
        }

        public string FileSummary
        {
            get { return _fileSummary; }
            set { SetProperty(ref _fileSummary, value); }
        }

        public int ChangedFileCount
        {
            get { return _changedFileCount; }
            set { SetProperty(ref _changedFileCount, value); }
        }

        public string WorkTitle
        {
            get { return _workTitle; }
            set { SetProperty(ref _workTitle, value); }
        }

        public string WorkContent
        {
            get { return _workContent; }
            set { SetProperty(ref _workContent, value); }
        }

        public string Note
        {
            get { return _note; }
            set { SetProperty(ref _note, value); }
        }

        public string SaveStatus
        {
            get { return _saveStatus; }
            set { SetProperty(ref _saveStatus, value); }
        }

        public bool IsSaved
        {
            get { return _isSaved; }
            set { SetProperty(ref _isSaved, value); }
        }

        public string QueryMode
        {
            get { return _queryMode; }
            set { SetProperty(ref _queryMode, value); }
        }

        public string RemoteComputerName
        {
            get { return _remoteComputerName; }
            set { SetProperty(ref _remoteComputerName, value); }
        }

        public string SourceClientName
        {
            get { return _sourceClientName; }
            set { SetProperty(ref _sourceClientName, value); }
        }

        public string SessionToken
        {
            get { return _sessionToken; }
            set { SetProperty(ref _sessionToken, value); }
        }

        public string SessionStartAt
        {
            get { return _sessionStartAt; }
            set { SetProperty(ref _sessionStartAt, value); }
        }

        public string SessionEndAt
        {
            get { return _sessionEndAt; }
            set { SetProperty(ref _sessionEndAt, value); }
        }

        /// <summary>
        /// 신규 후보 생성. 사용자 편집값은 초기화.
        /// </summary>
        public static TfsChangesetCandidateViewModel FromPayloadItem(
            TfsRecentChangesetsPayload payload,
            TfsChangesetItem item)
        {
            var vm = new TfsChangesetCandidateViewModel();
            vm.ApplySourceInfo(payload, item, preserveUserEdits: false);
            return vm;
        }

        /// <summary>
        /// 중복 재수신 시 원본 체크인 정보만 갱신하고 사용자 편집값/선택/저장상태 유지.
        /// </summary>
        public void ApplySourceInfo(
            TfsRecentChangesetsPayload payload,
            TfsChangesetItem item,
            bool preserveUserEdits)
        {
            if (payload != null)
            {
                CollectionUrl = payload.CollectionUrl;
                ServerPath = payload.ServerPath;
                QueryMode = payload.QueryMode ?? "RECENT_COUNT";
                RemoteComputerName = payload.RemoteComputerName;
                SourceClientName = payload.SourceClientName;
                SessionToken = payload.SessionToken;
                SessionStartAt = payload.SessionStartAt;
                SessionEndAt = payload.SessionEndAt;
            }

            if (item == null)
                return;

            ChangesetId = item.ChangesetId;
            AuthorName = item.AuthorName;
            AuthorId = item.AuthorId;
            AuthorDisplay = BuildAuthorDisplay(item.AuthorName, item.AuthorId);
            OriginalComment = item.Comment ?? string.Empty;
            CheckedInAtDisplay = item.CheckedInAt ?? "-";
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(item.CheckedInAt) && DateTime.TryParse(item.CheckedInAt, out parsed))
                CheckedInAt = parsed;
            else
                CheckedInAt = null;

            ChangedFileCount = item.ChangedFileCount.HasValue
                ? item.ChangedFileCount.Value
                : (item.Files != null ? item.Files.Count : 0);
            FileSummary = TfsClipboardPayloadService.BuildFileSummary(item.Files, item.ChangedFileCount);

            _sourceFiles = new List<TfsChangedFileItem>();
            if (item.Files != null)
            {
                foreach (var f in item.Files)
                {
                    if (f == null)
                        continue;
                    _sourceFiles.Add(new TfsChangedFileItem
                    {
                        ServerPath = f.ServerPath,
                        FileName = f.FileName,
                        ChangeType = f.ChangeType,
                        Version = f.Version
                    });
                }
            }

            if (!preserveUserEdits)
            {
                WorkTitle = TfsClipboardPayloadService.BuildDefaultWorkTitle(item);
                WorkContent = TfsClipboardPayloadService.BuildDefaultWorkContent(item);
                Note = string.Empty;
                IsSelected = false;
                IsSaved = false;
                SaveStatus = "미저장";
            }
        }

        public TfsChangesetItem ToChangesetItem()
        {
            return new TfsChangesetItem
            {
                ChangesetId = ChangesetId,
                AuthorName = AuthorName,
                AuthorId = AuthorId,
                CheckedInAt = CheckedInAt.HasValue
                    ? CheckedInAt.Value.ToString("o")
                    : CheckedInAtDisplay,
                Comment = OriginalComment,
                ChangedFileCount = ChangedFileCount,
                Files = new List<TfsChangedFileItem>(_sourceFiles)
            };
        }

        public TfsWorkLogRecord ToWorkLogRecord(string createdBy)
        {
            DateTime? sessionStart = null;
            DateTime? sessionEnd = null;
            DateTime tmp;
            if (!string.IsNullOrWhiteSpace(SessionStartAt) && DateTime.TryParse(SessionStartAt, out tmp))
                sessionStart = tmp;
            if (!string.IsNullOrWhiteSpace(SessionEndAt) && DateTime.TryParse(SessionEndAt, out tmp))
                sessionEnd = tmp;

            return new TfsWorkLogRecord
            {
                CollectionUrl = CollectionUrl,
                ServerPath = ServerPath,
                ChangesetId = ChangesetId,
                AuthorName = AuthorName,
                AuthorId = AuthorId,
                CheckedInAt = CheckedInAt,
                OriginalComment = OriginalComment,
                WorkTitle = WorkTitle,
                WorkContent = WorkContent,
                Note = Note,
                ChangedFileCount = ChangedFileCount,
                ChangedFileSummary = FileSummary,
                RemoteComputerName = RemoteComputerName,
                SourceClientName = SourceClientName,
                QueryMode = QueryMode,
                SessionStartAt = sessionStart,
                SessionEndAt = sessionEnd,
                SessionToken = SessionToken,
                CreatedBy = createdBy,
                CreatedAt = DateTime.Now
            };
        }

        private static string BuildAuthorDisplay(string name, string id)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                return name + " (" + id + ")";
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            if (!string.IsNullOrWhiteSpace(id))
                return id;
            return "-";
        }
    }
}
