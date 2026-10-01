using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace GBCWorkHub.UI.ViewModels
{
    /// <summary>Domain / VPN / Auth 섹션. ID+PW 한 세트씩(Sets) 묶어서 보여준다 — 세트는
    /// 완전히 다른 계정을 시각적으로 떼어서 구분하고 싶을 때 "+"로 늘린다(세트 자체를
    /// 지우는 기능은 없음 — 빈 세트는 각 줄의 ×로 안의 줄들을 비우면 되고, 저장 시 빈
    /// 세트는 어차피 반영되지 않는다). 세트 안에서는 ID·PW 각각 줄 단위로 추가/삭제 가능해서
    /// "계정 1개에 비번 여러 개" 같은 다대다 관계도 표현할 수 있다.</summary>
    public sealed class PcAccessSectionViewModel : ViewModelBase
    {
        public PcAccessSectionViewModel(string title)
        {
            Title = title ?? string.Empty;
            Sets = new ObservableCollection<PcCredentialSetViewModel>();
        }

        public string Title { get; set; }
        public ObservableCollection<PcCredentialSetViewModel> Sets { get; private set; }

        public void SetEditing(bool editing)
        {
            for (int i = 0; i < Sets.Count; i++)
            {
                if (Sets[i] != null)
                    Sets[i].SetEditing(editing);
            }
        }
    }

    /// <summary>섹션 안 ID+PW 한 세트. ID·PW 둘 다 보통 1줄이지만, 과거 데이터 중엔 계정
    /// 1개에 비밀번호가 여러 개(비번 변경 이력)이거나 반대로 계정이 여러 개에 비밀번호가
    /// 1개인 경우가 있어 — 각 줄이 독립적으로 추가·삭제·복사 가능해야 하므로 한 문자열에
    /// 개행으로 합치지 않고 별도 줄(컬렉션)로 둔다. ID·PW는 완전히 대칭 구조다.</summary>
    public sealed class PcCredentialSetViewModel : ViewModelBase
    {
        public PcCredentialSetViewModel(PcAccessSectionViewModel owner, IEnumerable<string> idValues, IEnumerable<string> pwValues, ICommand copyCommand)
        {
            OwnerSection = owner;
            Id = new ObservableCollection<PcCredentialLineViewModel>();
            Pw = new ObservableCollection<PcCredentialLineViewModel>();

            bool anyId = false;
            if (idValues != null)
            {
                foreach (string v in idValues)
                {
                    Id.Add(new PcCredentialLineViewModel(v ?? string.Empty, copyCommand, this, true));
                    anyId = true;
                }
            }
            if (!anyId)
                Id.Add(new PcCredentialLineViewModel(string.Empty, copyCommand, this, true));

            bool anyPw = false;
            if (pwValues != null)
            {
                foreach (string v in pwValues)
                {
                    Pw.Add(new PcCredentialLineViewModel(v ?? string.Empty, copyCommand, this, false));
                    anyPw = true;
                }
            }
            if (!anyPw)
                Pw.Add(new PcCredentialLineViewModel(string.Empty, copyCommand, this, false));
        }

        public PcAccessSectionViewModel OwnerSection { get; private set; }
        public ObservableCollection<PcCredentialLineViewModel> Id { get; private set; }
        public ObservableCollection<PcCredentialLineViewModel> Pw { get; private set; }

        public PcCredentialLineViewModel AddIdLine(ICommand copyCommand, string value = null)
        {
            var line = new PcCredentialLineViewModel(value ?? string.Empty, copyCommand, this, true);
            Id.Add(line);
            return line;
        }

        public PcCredentialLineViewModel AddPwLine(ICommand copyCommand, string value = null)
        {
            var line = new PcCredentialLineViewModel(value ?? string.Empty, copyCommand, this, false);
            Pw.Add(line);
            return line;
        }

        /// <summary>줄 하나 제거. 그 줄이 속한 컬렉션(ID/PW)의 마지막 하나 남은 줄이면
        /// 지우지 않고 빈 값으로만 되돌린다 — 칸 자체가 사라지면 다시 추가하기 번거로우므로.</summary>
        public void RemoveLine(PcCredentialLineViewModel line)
        {
            if (line == null)
                return;
            ObservableCollection<PcCredentialLineViewModel> target = line.IsIdLine ? Id : Pw;
            if (target.Count <= 1)
            {
                line.Value = string.Empty;
                return;
            }
            target.Remove(line);
        }

        public void SetEditing(bool editing)
        {
            for (int i = 0; i < Id.Count; i++)
            {
                if (Id[i] != null)
                    Id[i].IsEditing = editing;
            }
            for (int i = 0; i < Pw.Count; i++)
            {
                if (Pw[i] != null)
                    Pw[i].IsEditing = editing;
            }
        }
    }

    /// <summary>세트 안 한 칸(ID 또는 PW) 값. 복사 피드백 + 편집 + 자기 소속(세트/종류) 정보.</summary>
    public sealed class PcCredentialLineViewModel : ViewModelBase
    {
        private string _value;
        private bool _isCopied;
        private bool _isEditing;
        private DispatcherTimer _copiedTimer;

        public PcCredentialLineViewModel(string value, ICommand copyCommand, PcCredentialSetViewModel ownerSet, bool isIdLine)
        {
            _value = value ?? string.Empty;
            CopyCommand = copyCommand;
            OwnerSet = ownerSet;
            IsIdLine = isIdLine;
        }

        /// <summary>이 줄이 속한 세트 — 줄 삭제/추가 시 어느 컬렉션(ID/PW)에서 빼고 넣어야 하는지 알기 위함.</summary>
        public PcCredentialSetViewModel OwnerSet { get; private set; }

        /// <summary>true면 ID 줄, false면 PW 줄.</summary>
        public bool IsIdLine { get; private set; }

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
