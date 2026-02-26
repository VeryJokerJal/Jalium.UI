using Jalium.UI;

namespace Jalium.UI.Threading;

/// <summary>
/// Represents an operation that has been posted to the Dispatcher queue.
/// </summary>
public sealed class DispatcherOperation
{
    private readonly Delegate _method;
    private readonly object? _args;
    private object? _result;

    internal DispatcherOperation(Dispatcher dispatcher, Delegate method, object? args, DispatcherPriority priority)
    {
        Dispatcher = dispatcher;
        _method = method;
        _args = args;
        Priority = priority;
        Status = DispatcherOperationStatus.Pending;
    }

    public Dispatcher Dispatcher { get; }
    public DispatcherPriority Priority { get; set; }
    public DispatcherOperationStatus Status { get; internal set; }
    public object? Result => _result;

    public event EventHandler? Aborted;
    public event EventHandler? Completed;

    public bool Abort()
    {
        if (Status == DispatcherOperationStatus.Pending)
        {
            Status = DispatcherOperationStatus.Aborted;
            Aborted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public DispatcherOperationStatus Wait() => Wait(TimeSpan.FromMilliseconds(-1));

    public DispatcherOperationStatus Wait(TimeSpan timeout)
    {
        return Status;
    }

    internal void Invoke()
    {
        Status = DispatcherOperationStatus.Executing;
        try
        {
            _result = _method.DynamicInvoke(_args is object[] arr ? arr : (_args != null ? new[] { _args } : null));
            Status = DispatcherOperationStatus.Completed;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            Status = DispatcherOperationStatus.Aborted;
            throw;
        }
    }
}

/// <summary>
/// Describes the possible values for the status of a DispatcherOperation.
/// </summary>
public enum DispatcherOperationStatus
{
    Pending,
    Aborted,
    Completed,
    Executing
}

/// <summary>
/// Represents a disposable that re-enables Dispatcher processing when disposed.
/// </summary>
public struct DispatcherProcessingDisabled : IDisposable
{
    private Dispatcher? _dispatcher;

    internal DispatcherProcessingDisabled(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Dispose()
    {
        _dispatcher = null;
    }
}

/// <summary>
/// Provides data for Dispatcher unhandled exception events.
/// </summary>
public sealed class DispatcherUnhandledExceptionEventArgs : EventArgs
{
    public DispatcherUnhandledExceptionEventArgs(Dispatcher dispatcher, Exception exception)
    {
        Dispatcher = dispatcher;
        Exception = exception;
    }

    public Dispatcher Dispatcher { get; }
    public Exception Exception { get; }
    public bool Handled { get; set; }
}

/// <summary>
/// Provides data for Dispatcher unhandled exception filter events.
/// </summary>
public sealed class DispatcherUnhandledExceptionFilterEventArgs : EventArgs
{
    public DispatcherUnhandledExceptionFilterEventArgs(Dispatcher dispatcher, Exception exception)
    {
        Dispatcher = dispatcher;
        Exception = exception;
    }

    public Dispatcher Dispatcher { get; }
    public Exception Exception { get; }
    public bool RequestCatch { get; set; } = true;
}
