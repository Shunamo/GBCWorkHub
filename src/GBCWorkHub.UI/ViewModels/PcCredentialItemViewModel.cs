using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>Domain / VPN 섹션. ID+PW 한 세트씩(Sets) 묶어서 보여준다 — 세트 단위로
    /// 추가/삭제해야 두 목록의 줄 수가 어긋나는 일이 없다.</summary>
    public sealed class PcAccessSectionViewModel : ViewModelBase
    {
        public PcAccessSectionViewModel(string title)
        {
            Title = title ?? string.Empty;
            Sets = new ObservableCollection<PcCredentialSetViewModel>();
        }

        public string Title { get; private set; }
        public ObservableCollection<PcCredentialSetViewModel> Sets { get; private set; }

        public void SetEditing(bool editing)
        {
            if (Sets == null)
                return;
            for (int i = 0; i < Sets.Count; i++)
            {
                if (Sets[i] != null)
                    Sets[i].SetEditing(editing);
            }
        }
    }

    /// <summary>섹션 안 ID+PW 한 세트.</summary>
    public sealed class PcCredentialSetViewModel : ViewModelBase
    {
        public PcCredentialSetViewModel(PcAccessSectionViewModel owner, PcCredentialLineViewModel id, PcCredentialLineViewModel pw)
        {
            OwnerSection = owner;
            Id = id;
            Pw = pw;
        }

        public PcAccessSectionViewModel OwnerSection { get; private set; }
        public PcCredentialLineViewModel Id { get; private set; }
        public PcCredentialLineViewModel Pw { get; private set; }

        public void SetEditing(bool editing)
        {
            if (Id != null)
                Id.IsEditing = editing;
            if (Pw != null)
                Pw.IsEditing = editing;
        }
    }

    /// <summary>세트 안 한 칸(ID 또는 PW) 값. 복사 피드백 + 편집.</summary>
    public sealed class PcCredentialLineViewModel : ViewModelBase
    {
        private string _value;
        private bool _isCopied;
        private bool _isEditing;
        private DispatcherTimer _copiedTimer;

        public PcCredentialLineViewModel(string value, ICommand copyCommand)
        {
            _value = value ?? string.Empty;
            CopyCommand = copyCommand;
        }

        public ICommand CopyCommand { get; private set; }

        public string Value
        {
            get { return _value; }
            set { SetProperty(ref _value, value ?? string.Empty); }
        }

        public bool IsCopied
        {
            get { return _isCopied; }
            private set
            {
                if (SetProperty(ref _isCopied, value))
                    RaisePropertyChanged("ShowCopyIcon");
            }
        }

        public bool ShowCopyIcon
        {
            get { return !IsCopied; }
        }

        public bool IsEditing
        {
            get { return _isEditing; }
            set
            {
                if (SetProperty(ref _isEditing, value))
                    RaisePropertyChanged("IsReadOnly");
            }
        }

        public bool IsReadOnly
        {
            get { return !IsEditing; }
        }

        public void MarkCopied()
        {
            IsCopied = true;
            if (_copiedTimer == null)
            {
                _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                _copiedTimer.Tick += (s, e) =>
                {
                    _copiedTimer.Stop();
                    IsCopied = false;
                };
            }
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
    }
}
