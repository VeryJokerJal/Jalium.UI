using System.Runtime.CompilerServices;
using Jalium.UI;

namespace Jalium.UI.Threading;

/// <summary>
/// Represents an operation that has been posted to the <see cref="Dispatcher"/> queue.
/// </summary>
public sealed class DispatcherOperation
{
    private readonly Action _action;
    private readonly bool _critical;
    private readonly bool _observed;
    private Exception? _exception;
    private ManualResetEventSlim? _completedEvent;
    private DispatcherPriority _priority;

    // 当前在调度队列链表中的节点（用于 O(1) 中止移除）；出队后置 null。
    internal LinkedListNode<DispatcherOperation>? Node { get; set; }

    internal DispatcherOperation(Dispatcher dispatcher, DispatcherPriority priority, Action action, bool critical, bool observed)
    {
        Dispatcher = dispatcher;
        _priority = priority;
        _action = action;
        _critical = critical;
        _observed = observed;
        Status = DispatcherOperationStatus.Pending;
    }

    /// <summary>Gets the <see cref="Dispatcher"/> that this operation was posted to.</summary>
    public Dispatcher Dispatcher { get; }

    /// <summary>Gets or sets the priority of this operation within the dispatcher queue.</summary>
    public DispatcherPriority Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                Dispatcher.OnOperationPriorityChanged(this);
            }
        }
    }

    /// <summary>Gets the current status of this operation.</summary>
    public DispatcherOperationStatus Status { get; internal set; }

    /// <summary>Gets the result of the operation after it has completed.</summary>
    public object? Result { get; private set; }

    /// <summary>Occurs when the operation is aborted.</summary>
    public event EventHandler? Aborted;

    /// <summary>Occurs when the operation has completed (successfully or with an exception).</summary>
    public event EventHandler? Completed;

    internal Exception? OperationException => _exception;
    internal bool IsCritical => _critical;

    /// <summary>Gets an awaiter so the operation can be <see langword="await"/>ed.</summary>
    public TaskAwaiter GetAwaiter() => ToTask().GetAwaiter();

    private Task ToTask()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        switch (Status)
        {
            case DispatcherOperationStatus.Completed:
                if (_exception != null) tcs.TrySetException(_exception);
                else tcs.TrySetResult();
                break;
            case DispatcherOperationStatus.Aborted:
                tcs.TrySetCanceled();
                break;
            default:
                Completed += (_, _) =>
                {
                    if (_exception != null) tcs.TrySetException(_exception);
                    else tcs.TrySetResult();
                };
                Aborted += (_, _) => tcs.TrySetCanceled();
                break;
        }
        return tcs.Task;
    }

    /// <summary>
    /// Aborts the operation if it has not yet started executing.
    /// </summary>
    /// <returns><see langword="true"/> if the operation was aborted; otherwise <see langword="false"/>.</returns>
    public bool Abort()
    {
        if (Status == DispatcherOperationStatus.Pending)
        {
            AbortInternal();
            return true;
        }
        return false;
    }

    internal void AbortInternal()
    {
        Status = DispatcherOperationStatus.Aborted;
        Dispatcher.RemoveOperation(this);
        _completedEvent?.Set();
        Aborted?.Invoke(this, EventArgs.Empty);
        Dispatcher.HooksInternal?.RaiseOperationAborted(this, EventArgs.Empty);
    }

    /// <summary>Waits indefinitely for the operation to complete.</summary>
    public DispatcherOperationStatus Wait() => Wait(TimeSpan.FromMilliseconds(-1));

    /// <summary>
    /// Waits for the operation to complete, up to the specified timeout. When called on the dispatcher
    /// thread for a still-pending operation, a nested frame is pumped to avoid self-deadlock.
    /// </summary>
    public DispatcherOperationStatus Wait(TimeSpan timeout)
    {
        if (Status is DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing)
        {
            if (Dispatcher.CheckAccess())
            {
                if (Status == DispatcherOperationStatus.Pending && timeout.TotalMilliseconds != 0)
                {
                    var frame = new DispatcherFrame();
                    void Done(object? s, EventArgs e) => frame.Continue = false;
                    Completed += Done;
                    Aborted += Done;
                    try { Dispatcher.PushFrame(frame); }
                    finally { Completed -= Done; Aborted -= Done; }
                }
            }
            else
            {
                var ev = EnsureCompletedEvent();
                if (timeout.TotalMilliseconds < 0) ev.Wait();
                else ev.Wait(timeout);
            }
        }
        return Status;
    }

    private ManualResetEventSlim EnsureCompletedEvent()
    {
        var existing = _completedEvent;
        if (existing != null) return existing;

        var created = new ManualResetEventSlim(false);
        var prior = Interlocked.CompareExchange(ref _completedEvent, created, null);
        if (prior != null)
        {
            created.Dispose();
            return prior;
        }

        if (Status is DispatcherOperationStatus.Completed or DispatcherOperationStatus.Aborted)
            _completedEvent!.Set();
        return _completedEvent!;
    }

    internal void InvokeInternal()
    {
        if (Status == DispatcherOperationStatus.Aborted)
            return;

        Status = DispatcherOperationStatus.Executing;
        try
        {
            _action();
            Status = DispatcherOperationStatus.Completed;
            _completedEvent?.Set();
            Completed?.Invoke(this, EventArgs.Empty);
            Dispatcher.HooksInternal?.RaiseOperationCompleted(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _exception = ex;
            // The delegate ran to a faulted completion; observers (Invoke/InvokeAsync) read
            // OperationException via their Completed handler.
            Status = DispatcherOperationStatus.Completed;
            _completedEvent?.Set();
            Completed?.Invoke(this, EventArgs.Empty);
            Dispatcher.HooksInternal?.RaiseOperationCompleted(this, EventArgs.Empty);

            // Fire-and-forget (and critical) operations have no observer to surface the error to,
            // so propagate to the dispatcher pump (critical => crash; otherwise UnhandledException).
            if (_critical || !_observed)
                throw;
        }
    }
}

/// <summary>
/// Describes the possible values for the status of a <see cref="DispatcherOperation"/>.
/// </summary>
public enum DispatcherOperationStatus
{
    /// <summary>The operation is pending and is still in the queue.</summary>
    Pending,

    /// <summary>The operation was aborted.</summary>
    Aborted,

    /// <summary>The operation has completed.</summary>
    Completed,

    /// <summary>The operation has started executing but has not completed.</summary>
    Executing
}

/// <summary>
/// Represents a disposable that re-enables <see cref="Dispatcher"/> processing when disposed.
/// </summary>
public struct DispatcherProcessingDisabled : IDisposable
{
    private Dispatcher? _dispatcher;

    internal DispatcherProcessingDisabled(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Re-enables dispatcher processing.</summary>
    public void Dispose()
    {
        var dispatcher = _dispatcher;
        _dispatcher = null;
        dispatcher?.EnableProcessing();
    }
}

/// <summary>
/// An awaitable that re-schedules its continuation on the dispatcher at a specified priority.
/// Returned by <see cref="Dispatcher.Yield()"/>.
/// </summary>
public readonly struct DispatcherPriorityAwaitable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherPriority _priority;

    internal DispatcherPriorityAwaitable(Dispatcher dispatcher, DispatcherPriority priority)
    {
        _dispatcher = dispatcher;
        _priority = priority;
    }

    /// <summary>Gets the awaiter for this awaitable.</summary>
    public DispatcherPriorityAwaiter GetAwaiter() => new(_dispatcher, _priority);
}

/// <summary>
/// The awaiter for <see cref="DispatcherPriorityAwaitable"/>.
/// </summary>
public readonly struct DispatcherPriorityAwaiter : INotifyCompletion
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherPriority _priority;

    internal DispatcherPriorityAwaiter(Dispatcher dispatcher, DispatcherPriority priority)
    {
        _dispatcher = dispatcher;
        _priority = priority;
    }

    /// <summary>Always returns <see langword="false"/> so the continuation is re-scheduled.</summary>
    public bool IsCompleted => false;

    /// <summary>No result to return.</summary>
    public void GetResult() { }

    /// <summary>Schedules the continuation at the configured priority.</summary>
    public void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        _dispatcher.BeginInvoke(_priority, continuation);
    }
}

/// <summary>
/// Provides data for Dispatcher unhandled exception events.
/// </summary>
public sealed class DispatcherUnhandledExceptionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance.</summary>
    public DispatcherUnhandledExceptionEventArgs(Dispatcher dispatcher, Exception exception)
    {
        Dispatcher = dispatcher;
        Exception = exception;
    }

    /// <summary>Gets the dispatcher that caught the exception.</summary>
    public Dispatcher Dispatcher { get; }

    /// <summary>Gets the exception that was raised.</summary>
    public Exception Exception { get; }

    /// <summary>Gets or sets whether the exception has been handled.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Provides data for Dispatcher unhandled exception filter events.
/// </summary>
public sealed class DispatcherUnhandledExceptionFilterEventArgs : EventArgs
{
    /// <summary>Initializes a new instance.</summary>
    public DispatcherUnhandledExceptionFilterEventArgs(Dispatcher dispatcher, Exception exception)
    {
        Dispatcher = dispatcher;
        Exception = exception;
    }

    /// <summary>Gets the dispatcher that caught the exception.</summary>
    public Dispatcher Dispatcher { get; }

    /// <summary>Gets the exception that was raised.</summary>
    public Exception Exception { get; }

    /// <summary>Gets or sets whether the exception should be caught and routed to <see cref="Dispatcher.UnhandledException"/>.</summary>
    public bool RequestCatch { get; set; } = true;
}
