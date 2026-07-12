using System.Runtime.CompilerServices;
using Jalium.UI;

namespace Jalium.UI.Threading;

/// <summary>
/// Represents an operation that has been posted to the <see cref="Dispatcher"/> queue.
/// </summary>
public class DispatcherOperation
{
    private readonly Action _action;
    private readonly bool _critical;
    private readonly bool _observed;
    private Exception? _exception;
    private ManualResetEventSlim? _completedEvent;
    private DispatcherPriority _priority;
    private readonly TaskCompletionSource _taskSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 当前在调度队列链表中的节点（用于 O(1) 中止移除）；出队后置 null。
    internal LinkedListNode<DispatcherOperation>? Node { get; set; }

    internal DispatcherOperation(
        Jalium.UI.Dispatcher dispatcher,
        Jalium.UI.DispatcherPriority priority,
        Action action,
        bool critical,
        bool observed)
    {
        Dispatcher = Jalium.UI.Threading.Dispatcher.FromLegacy(dispatcher);
        _priority = Jalium.UI.Threading.Dispatcher.FromLegacyPriority(priority);
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
            Jalium.UI.Threading.Dispatcher.ValidatePriority(value, nameof(value));
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

    /// <summary>Gets the task representing this operation's completion.</summary>
    public Task Task => _taskSource.Task;

    /// <summary>Occurs when the operation is aborted.</summary>
    public event EventHandler? Aborted;

    /// <summary>Occurs when the operation has completed (successfully or with an exception).</summary>
    public event EventHandler? Completed;

    internal Exception? OperationException => _exception;
    internal bool IsCritical => _critical;

    /// <summary>Gets an awaiter so the operation can be <see langword="await"/>ed.</summary>
    public TaskAwaiter GetAwaiter() => Task.GetAwaiter();

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
        _taskSource.TrySetCanceled();
        Aborted?.Invoke(this, EventArgs.Empty);
        Dispatcher.HooksInternal?.RaiseOperationAborted(Dispatcher.LegacyDispatcher, this);
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
                    try { Jalium.UI.Threading.Dispatcher.PushFrame(frame); }
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
            Result = InvokeDelegateCore();
            Status = DispatcherOperationStatus.Completed;
            _completedEvent?.Set();
            _taskSource.TrySetResult();
            Completed?.Invoke(this, EventArgs.Empty);
            Dispatcher.HooksInternal?.RaiseOperationCompleted(Dispatcher.LegacyDispatcher, this);
        }
        catch (Exception ex)
        {
            _exception = ex;
            // The delegate ran to a faulted completion; observers (Invoke/InvokeAsync) read
            // OperationException via their Completed handler.
            Status = DispatcherOperationStatus.Completed;
            _completedEvent?.Set();
            _taskSource.TrySetException(ex);
            Completed?.Invoke(this, EventArgs.Empty);
            Dispatcher.HooksInternal?.RaiseOperationCompleted(Dispatcher.LegacyDispatcher, this);

            // Fire-and-forget (and critical) operations have no observer to surface the error to,
            // so propagate to the dispatcher pump (critical => crash; otherwise UnhandledException).
            if (_critical || !_observed)
                throw;
        }
    }

    /// <summary>
    /// Invokes the delegate represented by this operation and returns its result.
    /// </summary>
    protected virtual object? InvokeDelegateCore()
    {
        _action();
        return null;
    }
}

/// <summary>
/// Represents a dispatcher operation whose callback produces a strongly typed result.
/// </summary>
public class DispatcherOperation<TResult> : DispatcherOperation
{
    private readonly Func<TResult> _callback;
    private readonly Task<TResult> _typedTask;

    internal DispatcherOperation(
        Jalium.UI.Dispatcher dispatcher,
        Jalium.UI.DispatcherPriority priority,
        Func<TResult> callback)
        : base(
            dispatcher,
            priority,
            static () => { },
            critical: false,
            observed: true)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _typedTask = AwaitResultAsync(this);
    }

    /// <summary>Gets the result after the operation completes.</summary>
    public new TResult Result => (TResult)base.Result!;

    /// <summary>Gets the task representing this operation.</summary>
    public new Task<TResult> Task => _typedTask;

    /// <summary>Gets an awaiter for the typed operation.</summary>
    public new TaskAwaiter<TResult> GetAwaiter() => Task.GetAwaiter();

    /// <inheritdoc />
    protected override object? InvokeDelegateCore() => _callback();

    private static async Task<TResult> AwaitResultAsync(DispatcherOperation<TResult> operation)
    {
        await ((DispatcherOperation)operation).Task.ConfigureAwait(false);
        return operation.Result;
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
    private Jalium.UI.Dispatcher? _dispatcher;

    internal DispatcherProcessingDisabled(Jalium.UI.Dispatcher dispatcher)
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

    public override bool Equals(object? obj) =>
        obj is DispatcherProcessingDisabled other && ReferenceEquals(_dispatcher, other._dispatcher);

    public override int GetHashCode() => _dispatcher?.GetHashCode() ?? 0;

    public static bool operator ==(
        DispatcherProcessingDisabled left,
        DispatcherProcessingDisabled right) => left.Equals(right);

    public static bool operator !=(
        DispatcherProcessingDisabled left,
        DispatcherProcessingDisabled right) => !left.Equals(right);
}

/// <summary>
/// An awaitable that re-schedules its continuation on the dispatcher at a specified priority.
/// Returned by <see cref="Dispatcher.Yield()"/>.
/// </summary>
public readonly struct DispatcherPriorityAwaitable
{
    private readonly Jalium.UI.Dispatcher _dispatcher;
    private readonly Jalium.UI.DispatcherPriority _priority;

    internal DispatcherPriorityAwaitable(
        Jalium.UI.Dispatcher dispatcher,
        Jalium.UI.DispatcherPriority priority)
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
    private readonly Jalium.UI.Dispatcher _dispatcher;
    private readonly Jalium.UI.DispatcherPriority _priority;

    internal DispatcherPriorityAwaiter(
        Jalium.UI.Dispatcher dispatcher,
        Jalium.UI.DispatcherPriority priority)
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

    internal DispatcherUnhandledExceptionEventArgs(
        Jalium.UI.Dispatcher dispatcher,
        Exception exception)
        : this(Jalium.UI.Threading.Dispatcher.FromLegacy(dispatcher), exception)
    {
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

    internal DispatcherUnhandledExceptionFilterEventArgs(
        Jalium.UI.Dispatcher dispatcher,
        Exception exception)
        : this(Jalium.UI.Threading.Dispatcher.FromLegacy(dispatcher), exception)
    {
    }

    /// <summary>Gets the dispatcher that caught the exception.</summary>
    public Dispatcher Dispatcher { get; }

    /// <summary>Gets the exception that was raised.</summary>
    public Exception Exception { get; }

    /// <summary>Gets or sets whether the exception should be caught and routed to <see cref="Dispatcher.UnhandledException"/>.</summary>
    public bool RequestCatch { get; set; } = true;
}

/// <summary>
/// Represents the method that handles <see cref="Dispatcher.UnhandledException"/>.
/// </summary>
public delegate void DispatcherUnhandledExceptionEventHandler(
    object sender,
    DispatcherUnhandledExceptionEventArgs e);

/// <summary>
/// Represents the method that handles <see cref="Dispatcher.UnhandledExceptionFilter"/>.
/// </summary>
public delegate void DispatcherUnhandledExceptionFilterEventHandler(
    object sender,
    DispatcherUnhandledExceptionFilterEventArgs e);
