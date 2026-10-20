namespace Jalium.UI;

public partial class Application
{
    private int _windowCloseShutdownDeferrals;
    private bool _handlingMacOSSessionEnding;

    internal bool IsHandlingMacOSSessionEnding => _handlingMacOSSessionEnding;

    // A session-manager quit must notify application code before starting the
    // ordinary Closing negotiation. A nested Quit cannot overtake these handlers.
    internal bool RaiseMacOSSessionEnding(ReasonSessionEnding reason)
    {
        if (_handlingMacOSSessionEnding) return false;
        _handlingMacOSSessionEnding = true;
        using var shutdownDeferral = DeferWindowCloseShutdown();
        try
        {
            bool cancelled = false;
            foreach (var window in Windows.Cast<Window>().ToArray())
            {
                // Earlier callbacks can close later windows in this snapshot.
                if (window.IsCloseRequestedForPlatformTermination) continue;
                var args = new SessionEndingCancelEventArgs(reason);
                window.RaisePlatformSessionEnding(args);
                cancelled |= args.Cancel;
            }
            // Each scope owns its decision; a later window or the application
            // cannot accidentally clear another window's cancellation.
            var applicationArgs = new SessionEndingCancelEventArgs(reason);
            return RaiseSessionEnding(applicationArgs) && !cancelled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session-ending callback failed during macOS quit: {ex.Message}");
            return false;
        }
        finally { _handlingMacOSSessionEnding = false; }
    }

    // AppKit negotiates every window close before replying to a quit request.
    // Defer automatic shutdown without changing the application's public policy
    // or preventing an explicit Shutdown call made by application code.
    internal IDisposable DeferWindowCloseShutdown()
    {
        Interlocked.Increment(ref _windowCloseShutdownDeferrals);
        return new WindowCloseShutdownDeferral(this);
    }

    private sealed class WindowCloseShutdownDeferral(Application application) : IDisposable
    {
        private Application? _application = application;

        public void Dispose()
        {
            var application = Interlocked.Exchange(ref _application, null);
            if (application != null)
                Interlocked.Decrement(ref application._windowCloseShutdownDeferrals);
        }
    }
}
