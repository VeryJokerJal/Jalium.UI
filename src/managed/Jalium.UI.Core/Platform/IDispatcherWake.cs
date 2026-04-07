namespace Jalium.UI.Core.Platform;

/// <summary>
/// Abstraction for the platform-specific mechanism to wake the dispatcher's
/// event loop from another thread. On Windows, this uses a Win32 message window
/// (PostMessage). On Linux, this uses an eventfd. On Android, this uses an ALooper.
/// </summary>
internal interface IDispatcherWake : IDisposable
{
    /// <summary>
    /// Wakes the dispatcher's event loop from any thread.
    /// Thread-safe.
    /// </summary>
    void Wake();

    /// <summary>
    /// Sets the callback to invoke when the dispatcher is woken.
    /// Called on the dispatcher's thread.
    /// </summary>
    void SetCallback(Action callback);
}
