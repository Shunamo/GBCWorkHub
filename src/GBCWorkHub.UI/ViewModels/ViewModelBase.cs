using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GBCWorkHub.UI.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 값이 같아도 setter를 통과하고 PropertyChanged를 발생시킨다 (진단/강제 UI 갱신).
        /// </summary>
        protected bool SetPropertyAlways<T>(ref T field, T value, out bool propertyChangedRaised, [CallerMemberName] string propertyName = null)
        {
            field = value;
            RaisePropertyChanged(propertyName);
            propertyChangedRaised = true;
            return true;
        }
    }
}
