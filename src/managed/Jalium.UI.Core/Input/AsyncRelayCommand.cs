using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// An asynchronous command that wraps a <see cref="Task"/>-returning delegate.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _cts;
    private bool _isExecuting;

    /// <summary>
    /// Occurs when changes occur that affect whether the command should execute.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Gets a value indicating whether the command is currently executing.
    /// </summary>
    public bool IsExecuting => _isExecuting;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<Task> execute)
        : this(_ => execute(), null)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic with cancellation support.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<CancellationToken, Task> execute)
        : this(execute, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute)
        : this(_ => execute(), canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic with cancellation support.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    /// <inheritdoc />
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
        if (!CanExecute(null))
            return;

        _isExecuting = true;
        _cts = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        try
        {
            await _execute(_cts.Token);
        }
        finally
        {
            _isExecuting = false;
            _cts.Dispose();
            _cts = null;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Cancels the currently executing command.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Raises the <see cref="CanExecuteChanged"/> event.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// An asynchronous command that wraps a <see cref="Task"/>-returning delegate with a parameter.
/// </summary>
/// <typeparam name="T">The type of the command parameter.</typeparam>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, CancellationToken, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private CancellationTokenSource? _cts;
    private bool _isExecuting;

    /// <summary>
    /// Occurs when changes occur that affect whether the command should execute.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Gets a value indicating whether the command is currently executing.
    /// </summary>
    public bool IsExecuting => _isExecuting;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<T?, Task> execute)
        : this((p, _) => execute(p), null)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic with cancellation support.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<T?, CancellationToken, Task> execute)
        : this(execute, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute)
        : this((p, _) => execute(p), canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous execution logic with cancellation support.</param>
    /// <param name="canExecute">The execution status logic.</param>
    /// <exception cref="ArgumentNullException">Thrown if execute is null.</exception>
    public AsyncRelayCommand(Func<T?, CancellationToken, Task> execute, Func<T?, bool>? canExecute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
            return false;

        if (parameter is null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
        {
            return false;
        }

        return _canExecute?.Invoke((T?)parameter) ?? true;
    }

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        await ExecuteAsync((T?)parameter);
    }

    /// <summary>
    /// Executes the command asynchronously.
    /// </summary>
    /// <param name="parameter">The command parameter.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteAsync(T? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        _cts = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter, _cts.Token);
        }
        finally
        {
            _isExecuting = false;
            _cts.Dispose();
            _cts = null;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Cancels the currently executing command.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Raises the <see cref="CanExecuteChanged"/> event.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
