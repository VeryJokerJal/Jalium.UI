namespace Jalium.UI;

public partial class Window
{
    private int _pendingCloseDecisions;
    internal event EventHandler? PlatformCloseRequestCompleted;

    internal bool IsCloseDecisionPendingForPlatformTermination =>
        !_managedTeardownStarted && (_isClosing || Volatile.Read(ref _pendingCloseDecisions) != 0);

    /// <summary>Keeps a cancelled close pending while an asynchronous decision or save completes.</summary>
    public IDisposable DeferCloseDecision()
    {
        _dispatcher?.VerifyAccess();
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
