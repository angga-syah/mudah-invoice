using System;
using System.Windows.Input;

namespace InvoiceApp.Helpers
{
    /// <summary>
    /// A command whose sole purpose is to relay its functionality to other
    /// objects by invoking delegates. The default return value for the CanExecute
    /// method is 'true'.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        /// <summary>
        /// Initializes a new instance of RelayCommand that can always execute.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        public RelayCommand(Action execute) : this(execute, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of RelayCommand.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        /// <param name="canExecute">The execution status logic.</param>
        public RelayCommand(Action execute, Func<bool>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// </summary>
        /// <param name="parameter">Data used by the command. If the command does not require data to be passed, this object can be set to null.</param>
        /// <returns>true if this command can be executed; otherwise, false.</returns>
        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked.
        /// </summary>
        /// <param name="parameter">Data used by the command. If the command does not require data to be passed, this object can be set to null.</param>
        public void Execute(object? parameter)
        {
            _execute();
        }

        /// <summary>
        /// Method used to raise the CanExecuteChanged event to indicate that the return value of the CanExecute method has changed.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// A generic command whose sole purpose is to relay its functionality to other
    /// objects by invoking delegates. The default return value for the CanExecute
    /// method is 'true'.
    /// </summary>
    /// <typeparam name="T">The type of the command parameter.</typeparam>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        /// <summary>
        /// Initializes a new instance of RelayCommand that can always execute.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        public RelayCommand(Action<T?> execute) : this(execute, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of RelayCommand.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        /// <param name="canExecute">The execution status logic.</param>
        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// </summary>
        /// <param name="parameter">Data used by the command. This parameter can be null if the command does not require data to be passed.</param>
        /// <returns>true if this command can be executed; otherwise, false.</returns>
        public bool CanExecute(object? parameter)
        {
            // If there's no canExecute delegate, assume can execute
            if (_canExecute == null)
                return true;

            // Handle null parameter
            if (parameter == null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                return false;

            // Try to convert parameter to T
            if (parameter is T t)
                return _canExecute(t);
            
            // Handle compatible types
            try
            {
                return _canExecute((T?)Convert.ChangeType(parameter, typeof(T)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked.
        /// </summary>
        /// <param name="parameter">Data used by the command. This parameter can be null if the command does not require data to be passed.</param>
        public void Execute(object? parameter)
        {
            // Handle null parameter for value types
            if (parameter == null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                return;

            // Try to convert parameter to T
            T? convertedParameter = default;
            if (parameter is T t)
            {
                convertedParameter = t;
            }
            else if (parameter != null)
            {
                try
                {
                    convertedParameter = (T?)Convert.ChangeType(parameter, typeof(T));
                }
                catch
                {
                    // If conversion fails, use default
                    convertedParameter = default;
                }
            }

            _execute(convertedParameter);
        }

        /// <summary>
        /// Method used to raise the CanExecuteChanged event to indicate that the return value of the CanExecute method has changed.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// An async command that can be used with async/await pattern
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        /// <summary>
        /// Initializes a new instance of AsyncRelayCommand that can always execute.
        /// </summary>
        /// <param name="execute">The async execution logic.</param>
        public AsyncRelayCommand(Func<Task> execute) : this(execute, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of AsyncRelayCommand.
        /// </summary>
        /// <param name="execute">The async execution logic.</param>
        /// <param name="canExecute">The execution status logic.</param>
        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Gets a value indicating whether the command is currently executing.
        /// </summary>
        public bool IsExecuting => _isExecuting;

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// </summary>
        /// <param name="parameter">Data used by the command.</param>
        /// <returns>true if this command can be executed; otherwise, false.</returns>
        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked.
        /// </summary>
        /// <param name="parameter">Data used by the command.</param>
        public async void Execute(object? parameter)
        {
            await ExecuteAsync();
        }

        /// <summary>
        /// Executes the command asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ExecuteAsync()
        {
            if (_isExecuting)
                return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Method used to raise the CanExecuteChanged event.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// An async command with parameter that can be used with async/await pattern
    /// </summary>
    /// <typeparam name="T">The type of the command parameter.</typeparam>
    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Predicate<T?>? _canExecute;
        private bool _isExecuting;

        /// <summary>
        /// Initializes a new instance of AsyncRelayCommand that can always execute.
        /// </summary>
        /// <param name="execute">The async execution logic.</param>
        public AsyncRelayCommand(Func<T?, Task> execute) : this(execute, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of AsyncRelayCommand.
        /// </summary>
        /// <param name="execute">The async execution logic.</param>
        /// <param name="canExecute">The execution status logic.</param>
        public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Gets a value indicating whether the command is currently executing.
        /// </summary>
        public bool IsExecuting => _isExecuting;

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// </summary>
        /// <param name="parameter">Data used by the command.</param>
        /// <returns>true if this command can be executed; otherwise, false.</returns>
        public bool CanExecute(object? parameter)
        {
            if (_isExecuting)
                return false;

            if (_canExecute == null)
                return true;

            if (parameter == null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                return false;

            if (parameter is T t)
                return _canExecute(t);

            try
            {
                return _canExecute((T?)Convert.ChangeType(parameter, typeof(T)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked.
        /// </summary>
        /// <param name="parameter">Data used by the command.</param>
        public async void Execute(object? parameter)
        {
            T? convertedParameter = default;
            if (parameter is T t)
            {
                convertedParameter = t;
            }
            else if (parameter != null)
            {
                try
                {
                    convertedParameter = (T?)Convert.ChangeType(parameter, typeof(T));
                }
                catch
                {
                    convertedParameter = default;
                }
            }

            await ExecuteAsync(convertedParameter);
        }

        /// <summary>
        /// Executes the command asynchronously.
        /// </summary>
        /// <param name="parameter">The command parameter.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ExecuteAsync(T? parameter)
        {
            if (_isExecuting)
                return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _execute(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Method used to raise the CanExecuteChanged event.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}