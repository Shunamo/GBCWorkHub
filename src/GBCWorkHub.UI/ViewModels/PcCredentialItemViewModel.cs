using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>Domain / VPN 섹션.</summary>
    public sealed class PcAccessSectionViewModel : ViewModelBase
    {
        public PcAccessSectionViewModel(string title)
        {
            Title = title ?? string.Empty;
            Fields = new ObservableCollection<PcCredentialFieldViewModel>();
        }

        public string Title { get; private set; }
        public ObservableCollection<PcCredentialFieldViewModel> Fields { get; private set; }

        public void SetEditing(bool editing)
        {
            if (Fields == null)
                return;
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i] != null)
                    Fields[i].SetEditing(editing);
            }
        }
    }

    /// <summary>섹션 안 ID 또는 PW 필드(복수 줄).</summary>
    public sealed class PcCredentialFieldViewModel : ViewModelBase
    {
        public PcCredentialFieldViewModel(string kind, string label)
        {
            Kind = kind ?? "ID";
            Label = label ?? "ID";
            Lines = new ObservableCollection<PcCredentialLineViewModel>();
        }

        public string Kind { get; private set; }
        public string Label { get; private set; }
        public ObservableCollection<PcCredentialLineViewModel> Lines { get; private set; }

        public void SetEditing(bool editing)
        {
            if (Lines == null)
                return;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i] != null)
                    Lines[i].IsEditing = editing;
            }
        }
    }

    /// <summary>필드 안 한 줄 값. 복사 피드백 + 편집.</summary>
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
