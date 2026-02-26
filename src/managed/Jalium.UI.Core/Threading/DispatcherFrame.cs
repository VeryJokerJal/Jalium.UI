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
        get => _continue && !(_exitWhenRequested && Dispatcher.HasShutdownStarted);
        set => _continue = value;
    }
}

/// <summary>
/// Provides event hooks for the <see cref="Dispatcher"/>.
/// </summary>
public sealed class DispatcherHooks
{
    /// <summary>
    /// Occurs when an operation is posted to the dispatcher.
    /// </summary>
    public event EventHandler? OperationPosted;

    /// <summary>
    /// Occurs when an operation completes.
    /// </summary>
    public event EventHandler? OperationCompleted;

    /// <summary>
    /// Occurs when an operation is aborted.
    /// </summary>
    public event EventHandler? OperationAborted;

    /// <summary>
    /// Occurs when the priority of an operation changes.
    /// </summary>
    public event EventHandler? OperationPriorityChanged;

    internal void RaiseOperationPosted(object? sender, EventArgs e) =>
        OperationPosted?.Invoke(sender, e);

    internal void RaiseOperationCompleted(object? sender, EventArgs e) =>
        OperationCompleted?.Invoke(sender, e);

    internal void RaiseOperationAborted(object? sender, EventArgs e) =>
        OperationAborted?.Invoke(sender, e);

    internal void RaiseOperationPriorityChanged(object? sender, EventArgs e) =>
        OperationPriorityChanged?.Invoke(sender, e);
}

/// <summary>
/// Provides a <see cref="SynchronizationContext"/> for the <see cref="Dispatcher"/>.
/// Enables async/await to resume on the UI thread.
/// </summary>
public sealed class DispatcherSynchronizationContext : SynchronizationContext
{
    private readonly Jalium.UI.Dispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance using the current dispatcher.
    /// </summary>
    public DispatcherSynchronizationContext()
    {
        _dispatcher = Jalium.UI.Dispatcher.GetForCurrentThread();
    }

    /// <summary>
    /// Initializes a new instance using the specified dispatcher.
    /// </summary>
    public DispatcherSynchronizationContext(Jalium.UI.Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public override void Send(SendOrPostCallback d, object? state)
    {
        _dispatcher.Invoke(() => d(state));
    }

    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        _dispatcher.InvokeAsync(() => d(state));
    }

    /// <inheritdoc />
    public override SynchronizationContext CreateCopy()
    {
        return new DispatcherSynchronizationContext(_dispatcher);
    }

    /// <inheritdoc />
    public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
    {
        return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
    }
}
