using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI;

public partial class Window : IKeyboardFocusHost
{
    private int _macOSKeyboardFocusAcquisitionDepth;
    // The logical scope retains its history while a native child owns input.
    // A new explicit focus intent invalidates this one-shot restoration.
    private Action<int>? _macOSResponderFocusRestore;
    private int _macOSResponderFocusEpoch;

    internal bool IsMacOSKeyboardInputTargetEligible(UIElement target)
    {
        var route = InputRoute.Capture(NormalizeMacOSAccessibilityTarget(target), MacOSInputOwner);
        // An overlay shares its owner's native window, so AppKit cannot reject
        // tooltip focus for us. Keep the same policy for both popup hosts.
        return route.Count != 0 && !route.Any(node => node is Controls.ToolTip ||
            node is Controls.Primitives.PopupRoot { OwnerPopup.Child: Controls.ToolTip }) &&
            InputRoute.IsCurrent(route);
    }

    bool IKeyboardFocusHost.IsKeyboardFocusTargetEligible(UIElement target) =>
        !OperatingSystem.IsMacOS() || IsMacOSKeyboardInputTargetEligible(target);

    bool IKeyboardFocusHost.TryAcquireKeyboardFocus(UIElement target)
    {
        if (!OperatingSystem.IsMacOS()) return true;
        if (!IsMacOSKeyboardInputTargetEligible(target)) return false;
        if (_platformWindow is not IPlatformKeyboardFocus platform) return true;
        if (!Dispatcher.CheckAccess()) return false;
        var owner = MacOSInputOwner;
        var current = CaptureMacOSAccessibilityLifetime(target);
        var activation = CaptureMacOSAccessibilityActivationLifetime();
        var provider = FocusService.Provider;
        long intent = FocusService.FocusIntentRevision;
        bool IsCurrent(bool after) => target.Focusable && target.IsEnabled && current() &&
            activation(after) && intent == FocusService.FocusIntentRevision &&
            ReferenceEquals(provider, FocusService.Provider) && IsMacOSKeyboardInputTargetEligible(target);
        if (!IsCurrent(false)) return false;
        owner._macOSKeyboardFocusAcquisitionDepth++;
        try { return platform.TrySetKeyboardFocus() && IsCurrent(true); }
        finally { owner._macOSKeyboardFocusAcquisitionDepth--; }
    }

    private void HandleMacOSResponderFocusChanged(bool gained)
    {
        int epoch = unchecked(++_macOSResponderFocusEpoch);
        if (_isClosing || _managedTeardownStarted || !IsMacOSPopupHostCurrent) return;
        if (gained)
        {
            var restore = _macOSResponderFocusRestore;
            _macOSResponderFocusRestore = null;
            var owner = MacOSInputOwner;
            if (owner._macOSKeyboardFocusAcquisitionDepth == 0 && owner._macOSAccessibilityFocusActivationDepth == 0)
                restore?.Invoke(epoch);
        }
        else if (FocusService.FocusedElement is UIElement target && ReferenceEquals(GetMacOSPhysicalWindow(target), this))
        {
            var provider = FocusService.Provider;
            long intent = FocusService.FocusIntentRevision;
            _inputDispatcher.ClearKeyboardPressedChain();
            if (epoch != _macOSResponderFocusEpoch || intent != FocusService.FocusIntentRevision ||
                !ReferenceEquals(provider, FocusService.Provider) || !ReferenceEquals(target, FocusService.FocusedElement)) return;
            var scope = new WeakReference<DependencyObject>(FocusManager.GetFocusScope(target));
            var platform = _platformWindow;
            nint handle = Handle;
            int lifecycle = System.Threading.Volatile.Read(ref _renderLifecycleGeneration);
            var owner = MacOSInputOwner;
            var dialog = owner.ActiveContentDialog;
            int presentation = _macOSAccessibilityPresentationGeneration;
            int ownerPresentation = owner._macOSAccessibilityPresentationGeneration;
            long restoreIntent = unchecked(intent + 1);
            if (epoch != _macOSResponderFocusEpoch || intent != FocusService.FocusIntentRevision ||
                !ReferenceEquals(provider, FocusService.Provider) || !ReferenceEquals(target, FocusService.FocusedElement)) return;
            _macOSResponderFocusRestore = restoreEpoch =>
            {
                bool Current() => restoreEpoch == _macOSResponderFocusEpoch && !_isClosing && !_managedTeardownStarted &&
                    IsMacOSPopupHostCurrent && IsEnabled && Visibility == Visibility.Visible &&
                    lifecycle == System.Threading.Volatile.Read(ref _renderLifecycleGeneration) && handle == Handle &&
                    presentation == _macOSAccessibilityPresentationGeneration && ownerPresentation == owner._macOSAccessibilityPresentationGeneration &&
                    ReferenceEquals(platform, _platformWindow) && ReferenceEquals(provider, FocusService.Provider) &&
                    restoreIntent == FocusService.FocusIntentRevision && FocusService.FocusedElement == null &&
                    ReferenceEquals(dialog, owner.ActiveContentDialog);
                if (!Current() || !scope.TryGetTarget(out var focusScope) ||
                    FocusManager.GetFocusedElement(focusScope) is not UIElement previous) return;
                if (previous.Focusable && IsMacOSInputTargetValid(previous) &&
                    ReferenceEquals(FocusManager.GetFocusScope(previous), focusScope) &&
                    ReferenceEquals(GetMacOSPhysicalWindow(previous), this) &&
                    InputRoute.IsWithin(previous, dialog ?? (UIElement)owner) && Current()) previous.Focus();
            };
            try { FocusService.ClearFocus(); }
            catch
            {
                if (epoch == _macOSResponderFocusEpoch) _macOSResponderFocusRestore = null;
                throw;
            }
        }
        if (epoch == _macOSResponderFocusEpoch && !_isClosing && !_managedTeardownStarted)
        {
            UpdateInputMethodAssociation();
            WakeRenderPipeline();
        }
    }
}
