using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Jalium.UI;

public partial class Window
{
    private WeakReference<Application>? _hostedApplicationOwner;
    internal void SetHostedApplicationOwner(Application application) => _hostedApplicationOwner = new(application);
    internal bool IsOwnedByHostedApplication(Application application) =>
        _hostedApplicationOwner?.TryGetTarget(out var owner) == true && ReferenceEquals(owner, application);
    private bool IsHostedApplicationStopping => OperatingSystem.IsMacOS() &&
        ((Application.Current?.IsHostedStopping ?? false) ||
         (_hostedApplicationOwner?.TryGetTarget(out var owner) == true && owner.IsHostedStopping));

    internal bool HasPendingHostedTeardown => !_managedTeardownCompleted || Handle != 0 ||
        HasRenderFlag(RenderFlag_Rendering) || _renderThread?.IsAlive == true;

    private void EnsureHostedApplicationCanDisplay()
    {
        if (IsHostedApplicationStopping)
            throw new InvalidOperationException("A stopping application cannot display a window.");
    }

    internal void CloseForHostedApplicationShutdown()
    {
        if (!OperatingSystem.IsMacOS() || _managedTeardownCompleted) return;
        ExceptionDispatchInfo? failure = null;
        try
        {
            // StopHosted finalizes the host lifetime. Closing is a notification at
            // this point; cancellation cannot leave a window using disposed services.
            if (!_isClosing && !_managedTeardownStarted)
            {
                _isClosing = true;
                OnClosing(new CancelEventArgs());
            }
        }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        finally { HostedLifecycleCleanup.Attempt(CloseOwnedMacOSWindow, ref failure); }
        failure?.Throw();
    }
}
