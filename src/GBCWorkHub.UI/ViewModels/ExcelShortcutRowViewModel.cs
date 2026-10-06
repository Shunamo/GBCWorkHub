using System.ComponentModel;
using System.Runtime.CompilerServices;
using GBCWorkHub.DTO;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>"엑셀 시트" 리스트 한 행 — 전체 편집 모드일 때 쓰는 임시 입력값(파일명/URL)을 들고 있는다.
    /// 편집 모드 on/off 자체는 MainViewModel.IsExcelShortcutsEditMode(전체 단위)가 관리한다.</summary>
    public class ExcelShortcutRowViewModel : INotifyPropertyChanged
    {
        public ExcelShortcutRowViewModel(ExcelShortcutDto dto)
        {
            Dto = dto;
        }

        public ExcelShortcutDto Dto { get; }

        public long ShortcutId { get { return Dto.ShortcutId; } }
        public string Name { get { return Dto.Name; } }
        public string Url { get { return Dto.Url; } }
        public string IconKey { get { return Dto.IconKey; } }
        public int SortOrder { get { return Dto.SortOrder; } }

        private bool _isPinned;
        /// <summary>DB에 저장 안 되는 개인별 로컬 핀 — <see cref="Services.ExcelShortcutPinStore"/>가 관리.</summary>
        public bool IsPinned
        {
            get { return _isPinned; }
            set { if (_isPinned == value) return; _isPinned = value; OnPropertyChanged(); }
        }

        private string _editName;
        public string EditName
        {
            get { return _editName; }
            set { if (_editName == value) return; _editName = value; OnPropertyChanged(); }
        }

        private string _editUrl;
        public string EditUrl
        {
            get { return _editUrl; }
            set { if (_editUrl == value) return; _editUrl = value; OnPropertyChanged(); }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get { return _errorMessage; }
            set { if (_errorMessage == value) return; _errorMessage = value; OnPropertyChanged(); }
        }

        public bool IsChanged
        {
            get { return EditName != Name || EditUrl != Url; }
        }

        public void SeedEdit()
        {
            EditName = Name;
            EditUrl = Url;
            ErrorMessage = null;
        }

        public void Apply(string name, string url)
        {
            Dto.Name = name;
            Dto.Url = url;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Url));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
