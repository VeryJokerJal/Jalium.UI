using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Jalium.UI.Threading;

/// <summary>
/// Specifies the priority at which dispatcher operations are processed.
/// </summary>
public enum DispatcherPriority
{
    Invalid = -1,
    Inactive = 0,
    SystemIdle = 1,
    ApplicationIdle = 2,
    ContextIdle = 3,
    Background = 4,
    Input = 5,
    Loaded = 6,
    Render = 7,
    DataBind = 8,
    Normal = 9,
    Send = 10,
}

/// <summary>
/// Provides the canonical WPF-shaped dispatcher surface while sharing Jalium's existing
/// platform queue, wake mechanism, priority ordering, and input-fair message pump.
/// </summary>
public sealed class Dispatcher
{
    private static readonly ConditionalWeakTable<DispatcherCore, Dispatcher> Wrappers = new();
    private readonly DispatcherCore _dispatcher;

    private Dispatcher(DispatcherCore dispatcher)
    {
        _dispatcher = dispatcher;
    }

    internal DispatcherCore CoreDispatcher => _dispatcher;

    internal static Dispatcher FromCore(DispatcherCore dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return Wrappers.GetValue(dispatcher, static value => new Dispatcher(value));
    }

    /// <summary>Gets or creates the dispatcher for the calling thread.</summary>
    public static Dispatcher CurrentDispatcher =>
        FromCore(DispatcherCore.GetForCurrentThread());

    /// <summary>Gets the dispatcher associated with a thread, if one has been created.</summary>
    public static Dispatcher? FromThread(Thread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        DispatcherCore? dispatcher = DispatcherCore.FromThread(thread);
        return dispatcher is null ? null : FromCore(dispatcher);
    }

    internal static Dispatcher? MainDispatcher =>
        DispatcherCore.MainDispatcher is { } dispatcher ? FromCore(dispatcher) : null;

    internal static Dispatcher GetForCurrentThread() => CurrentDispatcher;

    internal static void SetAsMainThread() => DispatcherCore.SetAsMainThread();

    public Thread Thread => _dispatcher.Thread;
    public bool HasShutdownStarted => _dispatcher.HasShutdownStarted;
    public bool HasShutdownFinished => _dispatcher.HasShutdownFinished;
    public DispatcherHooks Hooks => _dispatcher.Hooks;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool CheckAccess() => _dispatcher.CheckAccess();

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void VerifyAccess() => _dispatcher.VerifyAccess();

    internal void EnsureNativeWake() => _dispatcher.EnsureNativeWake();

    public event EventHandler? ShutdownStarted
    {
        add => _dispatcher.ShutdownStarted += value;
        remove => _dispatcher.ShutdownStarted -= value;
    }

    public event EventHandler? ShutdownFinished
    {
        add => _dispatcher.ShutdownFinished += value;
        remove => _dispatcher.ShutdownFinished -= value;
    }

    public event DispatcherUnhandledExceptionEventHandler? UnhandledException
    {
        add => _dispatcher.UnhandledException += value;
        remove => _dispatcher.UnhandledException -= value;
    }

    public event DispatcherUnhandledExceptionFilterEventHandler? UnhandledExceptionFilter
    {
        add => _dispatcher.UnhandledExceptionFilter += value;
        remove => _dispatcher.UnhandledExceptionFilter -= value;
    }

    public void BeginInvokeShutdown(DispatcherPriority priority)
    {
        ValidatePriority(priority, nameof(priority));
        _dispatcher.BeginInvoke(priority, _dispatcher.BeginInvokeShutdown);
    }

    public void InvokeShutdown() =>
        Invoke(_dispatcher.BeginInvokeShutdown, DispatcherPriority.Send, CancellationToken.None);

    public static void Run() => PushFrame(new DispatcherFrame());

    public static void PushFrame(DispatcherFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Dispatcher dispatcher = CurrentDispatcher;
        if (!ReferenceEquals(frame.Dispatcher, dispatcher))
        {
            throw new InvalidOperationException("The dispatcher frame belongs to a different dispatcher.");
        }

        dispatcher._dispatcher.PushFrame(frame);
    }

    public static void ExitAllFrames() => CurrentDispatcher._dispatcher.ExitAllFrames();

    internal DispatcherOperation BeginInvoke(Action callback) =>
        BeginInvoke(DispatcherPriority.Normal, callback);

    internal DispatcherOperation BeginInvoke(DispatcherPriority priority, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority, nameof(priority));
        return _dispatcher.BeginInvoke(priority, callback);
    }

    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method) =>
        BeginInvoke(method, priority, Array.Empty<object>());

    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method, object? arg) =>
        BeginInvoke(method, priority, arg);

    public DispatcherOperation BeginInvoke(
        DispatcherPriority priority,
        Delegate method,
        object? arg,
        params object?[]? args) =>
        BeginInvoke(method, priority, CombineParameters(arg, args));

    public DispatcherOperation BeginInvoke(Delegate method, params object?[]? args) =>
        BeginInvoke(method, DispatcherPriority.Normal, args);

    public DispatcherOperation BeginInvoke(
        Delegate method,
        DispatcherPriority priority,
        params object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        ValidatePriority(priority, nameof(priority));
        return _dispatcher.BeginInvoke(priority, method, args);
    }

    internal DispatcherOperation BeginInvokeCritical(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _dispatcher.BeginInvokeCritical(callback);
    }

    public void Invoke(Action callback) =>
        Invoke(callback, DispatcherPriority.Send, CancellationToken.None, InfiniteTimeout);

    public void Invoke(Action callback, DispatcherPriority priority) =>
        Invoke(callback, priority, CancellationToken.None, InfiniteTimeout);

    public void Invoke(
        Action callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken) =>
        Invoke(callback, priority, cancellationToken, InfiniteTimeout);

    public void Invoke(
        Action callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(callback);
        InvokeCore(
            () =>
            {
                callback();
                return null;
            },
            priority,
            cancellationToken,
            timeout);
    }

    public TResult Invoke<TResult>(Func<TResult> callback) =>
        Invoke(callback, DispatcherPriority.Send, CancellationToken.None, InfiniteTimeout);

    public TResult Invoke<TResult>(Func<TResult> callback, DispatcherPriority priority) =>
        Invoke(callback, priority, CancellationToken.None, InfiniteTimeout);

    public TResult Invoke<TResult>(
        Func<TResult> callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken) =>
        Invoke(callback, priority, cancellationToken, InfiniteTimeout);

    public TResult Invoke<TResult>(
        Func<TResult> callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return (TResult)InvokeCore(() => callback(), priority, cancellationToken, timeout)!;
    }

    public object? Invoke(Delegate method, params object?[]? args) =>
        InvokeDelegate(method, DispatcherPriority.Normal, InfiniteTimeout, args);

    public object? Invoke(Delegate method, DispatcherPriority priority, params object?[]? args) =>
        InvokeDelegate(method, priority, InfiniteTimeout, args);

    public object? Invoke(Delegate method, TimeSpan timeout, params object?[]? args) =>
        InvokeDelegate(method, DispatcherPriority.Normal, timeout, args);

    public object? Invoke(
        Delegate method,
        TimeSpan timeout,
        DispatcherPriority priority,
        params object?[]? args) =>
        InvokeDelegate(method, priority, timeout, args);

    public object? Invoke(DispatcherPriority priority, Delegate method) =>
        InvokeDelegate(method, priority, InfiniteTimeout, Array.Empty<object>());

    public object? Invoke(DispatcherPriority priority, Delegate method, object? arg) =>
        InvokeDelegate(method, priority, InfiniteTimeout, [arg]);

    public object? Invoke(
        DispatcherPriority priority,
        Delegate method,
        object? arg,
        params object?[]? args) =>
        InvokeDelegate(method, priority, InfiniteTimeout, CombineParameters(arg, args));

    public object? Invoke(DispatcherPriority priority, TimeSpan timeout, Delegate method) =>
        InvokeDelegate(method, priority, timeout, Array.Empty<object>());

    public object? Invoke(
        DispatcherPriority priority,
        TimeSpan timeout,
        Delegate method,
        object? arg) =>
        InvokeDelegate(method, priority, timeout, [arg]);

    public object? Invoke(
        DispatcherPriority priority,
        TimeSpan timeout,
        Delegate method,
        object? arg,
        params object?[]? args) =>
        InvokeDelegate(method, priority, timeout, CombineParameters(arg, args));

    public DispatcherOperation InvokeAsync(Action callback) =>
        InvokeAsync(callback, DispatcherPriority.Normal, CancellationToken.None);

    public DispatcherOperation InvokeAsync(Action callback, DispatcherPriority priority) =>
        InvokeAsync(callback, priority, CancellationToken.None);

    public DispatcherOperation InvokeAsync(
        Action callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority, nameof(priority));
        return _dispatcher.CreateObservedOperation(
            callback,
            priority,
            cancellationToken);
    }

    public DispatcherOperation<TResult> InvokeAsync<TResult>(Func<TResult> callback) =>
        InvokeAsync(callback, DispatcherPriority.Normal, CancellationToken.None);

    public DispatcherOperation<TResult> InvokeAsync<TResult>(
        Func<TResult> callback,
        DispatcherPriority priority) =>
        InvokeAsync(callback, priority, CancellationToken.None);

    public DispatcherOperation<TResult> InvokeAsync<TResult>(
        Func<TResult> callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidatePriority(priority, nameof(priority));
        return _dispatcher.CreateObservedOperation(
            callback,
            priority,
            cancellationToken);
    }

    public DispatcherProcessingDisabled DisableProcessing() => _dispatcher.DisableProcessing();

    internal void ProcessQueue() => _dispatcher.ProcessQueue();

    internal void RunModalLoop(Func<bool> keepRunning) => _dispatcher.RunModalLoop(keepRunning);

    public static DispatcherPriorityAwaitable Yield() =>
        DispatcherCore.Yield();

    public static DispatcherPriorityAwaitable Yield(DispatcherPriority priority)
    {
        ValidatePriority(priority, nameof(priority));
        return DispatcherCore.Yield(priority);
    }

    public static void ValidatePriority(DispatcherPriority priority, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        if (priority < DispatcherPriority.Inactive || priority > DispatcherPriority.Send)
        {
            throw new InvalidEnumArgumentException(parameterName, (int)priority, typeof(DispatcherPriority));
        }
    }

    internal void RemoveOperation(DispatcherOperation operation) =>
        _dispatcher.RemoveOperation(operation);

    internal void OnOperationPriorityChanged(DispatcherOperation operation) =>
        _dispatcher.OnOperationPriorityChanged(operation);

    internal DispatcherHooks? HooksInternal => _dispatcher.HooksInternal;

    internal void EnableProcessing() => _dispatcher.EnableProcessing();

    internal bool ExitAllFramesRequested => _dispatcher.ExitAllFramesRequested;

    internal UIElement? LastLayoutExceptionElement
    {
        get => _dispatcher.LastLayoutExceptionElement;
        set => _dispatcher.LastLayoutExceptionElement = value;
    }

    internal void ClearAsMainThread() => _dispatcher.ClearAsMainThread();

    internal int RunMainMessageLoop() => _dispatcher.RunMainMessageLoop();

    internal void DisposeCore() => _dispatcher.Dispose();

    private static readonly TimeSpan InfiniteTimeout = TimeSpan.FromMilliseconds(-1);

    private object? InvokeDelegate(
        Delegate method,
        DispatcherPriority priority,
        TimeSpan timeout,
        object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        return InvokeCore(
            () =>
            {
                try
                {
                    return method.DynamicInvoke(args);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            },
            priority,
            CancellationToken.None,
            timeout);
    }

    private object? InvokeCore(
        Func<object?> callback,
        DispatcherPriority priority,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        ValidatePriority(priority, nameof(priority));
        ValidateTimeout(timeout);
        if (priority == DispatcherPriority.Inactive)
        {
            throw new ArgumentException("Inactive is not a valid priority for synchronous invocation.", nameof(priority));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (CheckAccess() && priority == DispatcherPriority.Send)
        {
            return callback();
        }

        DispatcherOperation<object?> operation = InvokeAsync(
            callback,
            priority,
            cancellationToken);
        DispatcherOperationStatus status = operation.Wait(timeout);
        if (status == DispatcherOperationStatus.Executing)
        {
            status = operation.Wait();
        }

        if (status == DispatcherOperationStatus.Pending)
        {
            operation.Abort();
            throw new TimeoutException("The dispatcher operation did not start within the specified timeout.");
        }

        if (status == DispatcherOperationStatus.Aborted)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (operation.OperationException is { } exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return operation.Result;
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != InfiniteTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    private static object?[] CombineParameters(object? arg, object?[]? args)
    {
        int extraCount = args?.Length ?? 0;
        var combined = new object?[extraCount + 1];
        combined[0] = arg;
        if (extraCount != 0)
        {
            Array.Copy(args!, 0, combined, 1, extraCount);
        }

        return combined;
    }
}

/// <summary>Provides event data for dispatcher-related events.</summary>
public class DispatcherEventArgs : EventArgs
{
    internal DispatcherEventArgs(Dispatcher dispatcher)
    {
        Dispatcher = dispatcher;
    }

    public Dispatcher Dispatcher { get; }
}

/// <summary>Represents a delegate used by legacy dispatcher operations.</summary>
public delegate object? DispatcherOperationCallback(object? arg);
