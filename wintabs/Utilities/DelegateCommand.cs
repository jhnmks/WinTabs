using System;
using System.Windows.Input;

namespace wintabs.Utilities
{

    internal class DelegateCommand<TParam> : ICommand
    {
        private readonly Action<TParam, Action> m_execute;
        private readonly Predicate<TParam> m_canExecute;

        public event EventHandler? CanExecuteChanged;

        public DelegateCommand(Action<TParam> execute)
        {
            m_execute = (parameter, _) => execute(parameter);
            m_canExecute = _ => true;
        }

        public DelegateCommand(Action<TParam, Action> execute, Predicate<TParam> canExecute)
        {
            m_execute = execute;
            m_canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return parameter is TParam value && m_canExecute(value);
        }

        public void Execute(object? parameter)
        {
            if (parameter is TParam value)
                m_execute(value, () => CanExecuteChanged?.Invoke(this, new EventArgs()));
        }
    }
}
