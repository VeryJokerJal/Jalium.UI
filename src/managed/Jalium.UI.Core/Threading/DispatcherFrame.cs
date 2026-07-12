namespace Jalium.UI.Threading;

/// <summary>
/// Represents an execution loop in the <see cref="Dispatcher"/>.
/// DispatcherFrame objects can be used to create nested pumping loops.
/// </summary>
public sealed class DispatcherFrame : DispatcherObject
{
    private bool _continue = true;
    private bool _exitWhenRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherFrame"/> class.
    /// </summary>
    public DispatcherFrame() : this(true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherFrame"/> class,
    /// optionally specifying whether to exit when all frames are requested to exit.
    /// </summary>
    /// <param name="exitWhenRequested">
    /// true if this frame should exit when all frames are requested to exit; otherwise, false.
    /// </param>
    public DispatcherFrame(bool exitWhenRequested)
    {
        _exitWhenRequested = exitWhenRequested;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this frame should continue.
    /// </summary>
    public bool Continue
    {
        get => _continue &&
               !(_exitWhenRequested &&
                 (Dispatcher.HasShutdownStarted || Dispatcher.ExitAllFramesRequested));
        set => _continue = value;
    }
}

/// <summary>
/// Provides event hooks for the <see cref="Dispatcher"/>.
/// </summary>
public sealed class DispatcherHooks
{
    internal DispatcherHooks()
    {
    }

    /// <summary>
    /// Occurs when an operation is posted to the dispatcher.
    /// </summary>
    public event DispatcherHookEventHandler? OperationPosted;

    /// <summary>
    /// Occurs immediately before an operation begins executing.
    /// </summary>
    public event DispatcherHookEventHandler? OperationStarted;

    /// <summary>
    /// Occurs when an operation completes.
    /// </summary>
    public event DispatcherHookEventHandler? OperationCompleted;

    /// <summary>
    /// Occurs when an operation is aborted.
    /// </summary>
    public event DispatcherHookEventHandler? OperationAborted;

    /// <summary>
    /// Occurs when the priority of an operation changes.
    /// </summary>
    public event DispatcherHookEventHandler? OperationPriorityChanged;

    /// <summary>
    /// Occurs when the dispatcher has no pending operation to process.
    /// </summary>
    public event EventHandler? DispatcherInactive;

    internal void RaiseOperationPosted(Jalium.UI.Dispatcher dispatcher, DispatcherOperation operation) =>
        OperationPosted?.Invoke(Dispatcher.FromLegacy(dispatcher), new DispatcherHookEventArgs(operation));

    internal void RaiseOperationStarted(Jalium.UI.Dispatcher dispatcher, DispatcherOperation operation) =>
        OperationStarted?.Invoke(Dispatcher.FromLegacy(dispatcher), new DispatcherHookEventArgs(operation));

    internal void RaiseOperationCompleted(Jalium.UI.Dispatcher dispatcher, DispatcherOperation operation) =>
        OperationCompleted?.Invoke(Dispatcher.FromLegacy(dispatcher), new DispatcherHookEventArgs(operation));

    internal void RaiseOperationAborted(Jalium.UI.Dispatcher dispatcher, DispatcherOperation operation) =>
        OperationAborted?.Invoke(Dispatcher.FromLegacy(dispatcher), new DispatcherHookEventArgs(operation));

    internal void RaiseOperationPriorityChanged(Jalium.UI.Dispatcher dispatcher, DispatcherOperation operation) =>
        OperationPriorityChanged?.Invoke(Dispatcher.FromLegacy(dispatcher), new DispatcherHookEventArgs(operation));

    internal void RaiseDispatcherInactive(Jalium.UI.Dispatcher dispatcher) =>
        DispatcherInactive?.Invoke(Dispatcher.FromLegacy(dispatcher), EventArgs.Empty);
}

/// <summary>
/// Provides the dispatcher operation associated with a hooks notification.
/// </summary>
public sealed class DispatcherHookEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance for the specified operation.
    /// </summary>
    public DispatcherHookEventArgs(DispatcherOperation operation)
    {
        Operation = operation;
    }

    /// <summary>Gets the dispatcher affected by the operation.</summary>
    public Dispatcher Dispatcher => Operation?.Dispatcher!;

    /// <summary>Gets the operation associated with this notification.</summary>
    public DispatcherOperation Operation { get; }
}

/// <summary>
/// Represents a handler for dispatcher operation lifecycle notifications.
/// </summary>
public delegate void DispatcherHookEventHandler(object sender, DispatcherHookEventArgs e);

/// <summary>
/// Provides a <see cref="SynchronizationContext"/> for the <see cref="Dispatcher"/>.
/// Enables async/await to resume on the UI thread.
/// </summary>
public sealed class DispatcherSynchronizationContext : SynchronizationContext
{
    private readonly Dispatcher _dispatcher;
    private readonly Jalium.UI.DispatcherPriority _priority;

    /// <summary>
    /// Initializes a new instance using the current dispatcher.
    /// </summary>
    public DispatcherSynchronizationContext()
        : this(Dispatcher.CurrentDispatcher, DispatcherPriority.Normal)
    {
    }

    /// <summary>
    /// Initializes a new instance using the specified dispatcher.
    /// </summary>
    public DispatcherSynchronizationContext(Dispatcher dispatcher)
        : this(dispatcher, DispatcherPriority.Normal)
    {
    }

    /// <summary>
    /// Initializes a new instance using the specified dispatcher and post priority.
    /// </summary>
    public DispatcherSynchronizationContext(
        Dispatcher dispatcher,
        DispatcherPriority priority)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Dispatcher.ValidatePriority(priority, nameof(priority));
        _priority = Dispatcher.ToLegacyPriority(priority);
        SetWaitNotificationRequired();
    }

    /// <summary>
    /// Compatibility overload for applications compiled against early Jalium builds.
    /// </summary>
    public DispatcherSynchronizationContext(Jalium.UI.Dispatcher dispatcher)
        : this(Dispatcher.FromLegacy(dispatcher), DispatcherPriority.Normal)
    {
    }

    /// <summary>
    /// Compatibility overload for applications compiled against early Jalium builds.
    /// </summary>
    public DispatcherSynchronizationContext(
        Jalium.UI.Dispatcher dispatcher,
        Jalium.UI.DispatcherPriority priority)
    {
        _dispatcher = Dispatcher.FromLegacy(dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)));
        Jalium.UI.Dispatcher.ValidatePriority(priority, nameof(priority));
        _priority = priority;
        SetWaitNotificationRequired();
    }

    /// <inheritdoc />
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        Jalium.UI.DispatcherPriority priority = BaseCompatibilityPreferences.GetInlineDispatcherSynchronizationContextSend()
            && _dispatcher.CheckAccess()
            ? Jalium.UI.DispatcherPriority.Send
            : _priority;

        _dispatcher.LegacyDispatcher.Invoke(() => d(state), priority);
    }

    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        // Post has no returned Task on which an exception can be observed.  Use a regular
        // DispatcherOperation so failures continue through the dispatcher's unhandled-exception
        // path, matching WPF's SynchronizationContext contract.
        _dispatcher.LegacyDispatcher.BeginInvoke(_priority, () => d(state));
    }

    /// <inheritdoc />
    public override SynchronizationContext CreateCopy()
    {
        if (BaseCompatibilityPreferences.GetReuseDispatcherSynchronizationContextInstance())
        {
            return this;
        }

        Jalium.UI.DispatcherPriority priority = BaseCompatibilityPreferences.GetFlowDispatcherSynchronizationContextPriority()
            ? _priority
            : Jalium.UI.DispatcherPriority.Normal;
        return new DispatcherSynchronizationContext(_dispatcher.LegacyDispatcher, priority);
    }

    /// <inheritdoc />
    public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
    {
        return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
    }
}
