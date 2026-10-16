using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI;

public partial class Window : IKeyboardFocusHost
{
    private int _macOSKeyboardFocusAcquisitionDepth;

    bool IKeyboardFocusHost.TryAcquireKeyboardFocus(UIElement target)
    {
        if (!OperatingSystem.IsMacOS() || _platformWindow is not IPlatformKeyboardFocus platform) return true;
        if (!Dispatcher.CheckAccess()) return false;
        var owner = MacOSInputOwner;
        var current = CaptureMacOSAccessibilityLifetime(target);
        var activation = CaptureMacOSAccessibilityActivationLifetime();
        var provider = FocusService.Provider;
        long intent = FocusService.FocusIntentRevision;
        bool IsCurrent(bool after) => target.Focusable && target.IsEnabled && current() &&
            activation(after) && intent == FocusService.FocusIntentRevision &&
            ReferenceEquals(provider, FocusService.Provider);
        if (!IsCurrent(false)) return false;
        owner._macOSKeyboardFocusAcquisitionDepth++;
        try { return platform.TrySetKeyboardFocus() && IsCurrent(true); }
        finally { owner._macOSKeyboardFocusAcquisitionDepth--; }
    }
}
