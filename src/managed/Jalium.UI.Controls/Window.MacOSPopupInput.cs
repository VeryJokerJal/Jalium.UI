using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Threading;

namespace Jalium.UI;

public partial class Window
{
    internal bool IsMacOSPopupInputOwnerAlive => !_isClosing && !_managedTeardownStarted && IsMacOSPopupHostCurrent;

    internal event Action<UIElement>? MacOSInputSubtreeDetached;
    internal event EventHandler? MacOSInputLayoutUpdated;

    internal void RestoreMacOSPopupInputFocus(UIElement? previous, UIElement anchor, UIElement retiredFocus)
    {
        bool CanRestore() => IsMacOSPopupInputOwnerAlive && IsEnabled && Visibility == Visibility.Visible &&
            ReferenceEquals(retiredFocus, Keyboard.FocusedElement);
        bool IsValid(UIElement element) => element.Focusable && IsMacOSInputTargetValid(element) &&
            InputRoute.IsWithin(element, ActiveContentDialog ?? (UIElement)this);
        if (!CanRestore()) return;
        if (previous != null && IsValid(previous))
        {
            if (previous.Focus() || !CanRestore()) return;
        }
        var route = InputRoute.Capture(anchor, this);
        if (route.Count == 0)
        {
            // Let the outer close operation restore its original focus after
            // a nested popup has removed its own focused subtree.
            int depth = 0;
            for (Visual? node = anchor; node != null && depth++ < 4096; node = InputRoute.GetParent(node))
                if (node is PopupRoot root && !root.OwnerPopup.IsOpen) return;
        }
        foreach (var node in route)
        {
            if (!CanRestore()) return;
            if (node is UIElement candidate && !ReferenceEquals(candidate, previous) && IsValid(candidate) && candidate.Focus()) return;
        }
        if (CanRestore()) Keyboard.ClearFocus();
    }

    private bool TryGetMacOSOwnedPopupInputTarget(out UIElement? target)
    {
        target = Keyboard.FocusedElement as UIElement;
        if (target == null || !IsMacOSInputTargetValid(target)) return false;
        bool inPopup = false;
        int depth = 0;
        for (Visual? current = target; current != null && depth++ < 4096; current = current.VisualParent)
            if (current is PopupRoot) { inPopup = true; break; }
        return inPopup && InputRoute.IsWithin(target, ActiveContentDialog ?? (UIElement)this);
    }

    Func<bool>? IInputDispatcherHost.CaptureNativeInputLifetime(UIElement target, bool textInput) =>
        OperatingSystem.IsMacOS() ? CaptureMacOSInputLifetime(target, textInput) : null;

    MouseCommandInput? IInputDispatcherHost.CaptureNativeMouseCommandInput(UIElement target, Func<bool>? isPacketCurrent)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var owner = MacOSInputOwner;
        var dialog = owner.ActiveContentDialog;
        var platform = _platformWindow;
        nint handle = Handle;
        int generation = Volatile.Read(ref _renderLifecycleGeneration);
        int activationGeneration = _macOSActivationGeneration;
        int nativeActivationGeneration = _macOSNativeActivationGeneration;
        return new MouseCommandInput(target, this, dialog ?? (UIElement)owner, dialog,
            () => isPacketCurrent?.Invoke() != false && !_isClosing && !_managedTeardownStarted && IsMacOSPopupHostCurrent && IsEnabled && Visibility == Visibility.Visible &&
                generation == Volatile.Read(ref _renderLifecycleGeneration) && activationGeneration == _macOSActivationGeneration && nativeActivationGeneration == _macOSNativeActivationGeneration && ReferenceEquals(platform, _platformWindow) &&
                handle == Handle && ReferenceEquals(dialog, owner.ActiveContentDialog));
    }

    private Func<bool> CaptureMacOSInputLifetime(UIElement target, bool textInput)
    {
        var route = InputRoute.Capture(target, MacOSInputOwner);
        var focus = Keyboard.FocusedElement;
        var owner = MacOSInputOwner;
        var dialog = owner.ActiveContentDialog;
        var platform = _platformWindow;
        nint handle = Handle;
        int generation = Volatile.Read(ref _renderLifecycleGeneration);
        int activationGeneration = _macOSActivationGeneration;
        int nativeActivationGeneration = _macOSNativeActivationGeneration;
        return () => !_isClosing && !_managedTeardownStarted && IsMacOSPopupHostCurrent && IsEnabled && Visibility == Visibility.Visible &&
            generation == Volatile.Read(ref _renderLifecycleGeneration) && activationGeneration == _macOSActivationGeneration && nativeActivationGeneration == _macOSNativeActivationGeneration && ReferenceEquals(platform, _platformWindow) &&
            handle == Handle && ReferenceEquals(focus, Keyboard.FocusedElement) && ReferenceEquals(dialog, owner.ActiveContentDialog) &&
            InputRoute.IsCurrent(route) && IsMacOSInputTargetValid(target) &&
            ReferenceEquals(target, textInput ? GetTextInputTarget() : GetKeyboardEventTarget());
    }
}
