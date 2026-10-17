using Jalium.UI.Styling;
using System.Threading;

namespace Jalium.UI;

public partial class Window
{
    private int _macOSActivationGeneration;
    private static long _macOSPlatformActivationGeneration;

    private void HandleMacOSActivationChanged(bool active)
    {
        int generation = unchecked(++_macOSActivationGeneration);
        long platformGeneration = Interlocked.Increment(ref _macOSPlatformActivationGeneration);
        bool IsCurrent() => generation == _macOSActivationGeneration &&
            platformGeneration == Volatile.Read(ref _macOSPlatformActivationGeneration) &&
            !_isClosing && !_managedTeardownStarted;

        if (active)
        {
            RestoreMacOSKeyboardFocus();
            if (!IsCurrent()) return;
            CssMediaQuery.RefreshPointingDevices();
            if (!IsCurrent()) return;
            if (!IsActive)
            {
                SetIsActive(true);
                if (!IsCurrent()) return;
                OnActivated(EventArgs.Empty);
                if (!IsCurrent()) return;
            }
            Application.Current?.SetPlatformActivationState(true);
            if (IsCurrent()) UpdateInputMethodAssociation();
            return;
        }

        // Detach ownership before dependency-property and routed callbacks. They
        // can reactivate this window, press again, or activate another window.
        var completeRetiredInput = _inputDispatcher.PrepareMacOSWindowDeactivation();
        bool notify = IsActive;
        try { SetIsActive(false); }
        finally { completeRetiredInput(); }
        if (!IsCurrent()) return;
        _inputDispatcher.CompleteMacOSWindowDeactivation(IsCurrent);
        if (!IsCurrent()) return;
        if (notify)
        {
            OnDeactivated(EventArgs.Empty);
            if (!IsCurrent()) return;
        }
        if (!HasMacOSNativeKeyWindow) Application.Current?.SetPlatformActivationState(false);
        if (IsCurrent()) UpdateInputMethodAssociation();
    }
}
