namespace Jalium.UI;

public partial class Window
{
    private int _pendingCloseDecisions;
    internal event EventHandler? PlatformCloseRequestCompleted;

    internal bool IsCloseDecisionPendingForPlatformTermination =>
        Volatile.Read(ref _pendingCloseDecisions) != 0 || _isClosing && !_managedTeardownStarted;

    /// <summary>Keeps a cancelled close pending while an asynchronous decision or save completes.</summary>
    protected IDisposable DeferCloseDecision()
    {
        ObjectDisposedException.ThrowIf(_managedTeardownCompleted, this);
        Interlocked.Increment(ref _pendingCloseDecisions);
        return new CloseDecisionDeferral(this);
    }

    private void CompleteCloseDecision()
    {
        if (Interlocked.Decrement(ref _pendingCloseDecisions) != 0) return;
        if (Dispatcher.CheckAccess()) NotifyCloseDecisionCompleted();
        else Dispatcher.BeginInvoke(NotifyCloseDecisionCompleted);
    }

    private void NotifyCloseDecisionCompleted()
        => PlatformCloseRequestCompleted?.Invoke(this, EventArgs.Empty);

    private sealed class CloseDecisionDeferral(Window owner) : IDisposable
    {
        private Window? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.CompleteCloseDecision();
    }
}
