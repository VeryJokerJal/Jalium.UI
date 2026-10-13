namespace Jalium.UI;

public partial class Application
{
    private int _windowCloseShutdownDeferrals;

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
