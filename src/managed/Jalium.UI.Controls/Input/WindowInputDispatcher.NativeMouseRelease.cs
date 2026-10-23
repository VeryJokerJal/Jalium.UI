using Jalium.UI.Input;

namespace Jalium.UI.Controls;

internal sealed partial class WindowInputDispatcher
{
    public void HandleMouseUp(MouseButton button, Point position, MouseButtonStates buttons,
        ModifierKeys modifiers, int timestamp)
    {
        long press = _mousePressEpoch, globalPress = Mouse.PressEpoch;
        var capture = Mouse.Captured as UIElement;
        long captureRevision = UIElement.MouseCaptureRevision;
        _activePointerTargets.TryGetValue(MousePointerId, out var pointerTarget);
        _lastPointerPoints.TryGetValue(MousePointerId, out var contact);
        bool hadToken = _pointerSessionTokens.TryGetValue(MousePointerId, out long pointerToken);
        bool nativeMouse = OperatingSystem.IsMacOS() && !SuppressMouseToPointerPromotion;
        Exception? failure = null; bool completed = false;
        try { completed = HandleMouseUpCore(button, position, buttons, modifiers, timestamp); }
        catch (Exception error) { failure = error; }
        // A move/wheel may supersede this packet without starting another
        // press. That cannot turn the already released press back into a live
        // gesture. A genuinely new press, including in another window, owns
        // its own cleanup instead.
        bool SamePress() => _mousePressEpoch == press && Mouse.PressEpoch == globalPress;
        if (nativeMouse && !completed && _mousePressEpoch == press)
        {
            void Notify(Action action)
            {
                try { action(); } catch (Exception error) { failure ??= error; }
            }
            Action? cancel = null;
            bool stillHasToken = _pointerSessionTokens.TryGetValue(MousePointerId, out long currentToken);
            _activePointerTargets.TryGetValue(MousePointerId, out var currentTarget);
            if ((!stillHasToken || hadToken && currentToken == pointerToken) &&
                (currentTarget is null || ReferenceEquals(currentTarget, pointerTarget)))
                Notify(() => cancel = PrepareNativeMouseDragCompletion(
                    CreateMousePointerPoint(position, buttons, MouseInputModifiers.ToLogical(modifiers), timestamp,
                        MapMouseButtonToPointerUpdateKind(button, isPressed: false)), contact?.IsInContact == true, SamePress));
            if (buttons.Left == MouseButtonState.Pressed) SuppressMousePointerRelease(MouseButton.Left);
            if (buttons.Middle == MouseButtonState.Pressed) SuppressMousePointerRelease(MouseButton.Middle);
            if (buttons.Right == MouseButtonState.Pressed) SuppressMousePointerRelease(MouseButton.Right);
            if (buttons.XButton1 == MouseButtonState.Pressed) SuppressMousePointerRelease(MouseButton.XButton1);
            if (buttons.XButton2 == MouseButtonState.Pressed) SuppressMousePointerRelease(MouseButton.XButton2);
            var pressed = DetachPressedChain(_mousePressedChain);
            bool SameCapture() => SamePress() && UIElement.MouseCaptureRevision == captureRevision && ReferenceEquals(Mouse.Captured, capture);
            if (SameCapture() && (capture is null || ReferenceEquals(Window.GetMacOSPhysicalWindow(capture), _host.Self)))
            {
                // Release the native owner before managed property/capture
                // callbacks can install a successor. Some platforms echo the
                // loss synchronously; the revision rejects that successor.
                Notify(_host.Self.ReleaseNativeCaptureIfOwned);
                if (capture is not null && SameCapture())
                    Notify(() => HandleNativeCaptureChangedIfCurrent(0, _host.Self.Handle, SameCapture));
            }
            ClearDetachedPressedChain(pressed, _mousePressedChain, _keyboardPressedChain, Notify);
            if (cancel is not null) Notify(cancel);
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal bool OwnsPressedState(UIElement element) => _mousePressedChain.Contains(element) ||
        _keyboardPressedChain.Contains(element) || KeyboardPressState.CurrentChain?.Contains(element) == true;
}

