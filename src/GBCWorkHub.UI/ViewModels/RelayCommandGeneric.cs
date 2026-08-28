using System;
using System.Windows.Input;

namespace GBCWorkHub.UI.ViewModels
{
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            if (execute == null)
                throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
                return true;
            if (parameter == null && typeof(T).IsValueType)
                return _canExecute(default(T));
            return parameter is T && _canExecute((T)parameter);
        }

        public void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;
            if (parameter is T)
                _execute((T)parameter);
            else if (parameter == null && !typeof(T).IsValueType)
                _execute(default(T));
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public enum RemoteComputerViewMode
    {
        Card = 0,
        List = 1
    }
}
