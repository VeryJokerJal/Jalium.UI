using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Controls;

internal sealed partial class WindowInputDispatcher
{
    private long _mousePressEpoch;
    private uint? _macOSTabletMousePointer;
    private uint _macOSTabletMouseButtons;
    private long _macOSTabletMouseEpoch;
    private UIElement? _macOSTabletMouseCapture;
    internal long MousePressEpoch => _mousePressEpoch;

    // AppKit mouse subtypes require a synchronous consumption decision. Reuse
    // the RTS processor (including its worker thread), then promote the actual
    // driver mouse packet only after the matching Stylus handlers decline it.
    internal bool HandleMacOSTabletInput(PointerInputData data, bool isDown, bool isUp,
        int timestamp, Func<bool> current, Action? mouse)
    {
        if (!current()) return true;
        uint id = data.PointerId;
        _activeStylusDevices.TryGetValue(id, out var device);
        UIElement? captured = device?.Captured as UIElement;
        UIElement? hit = RedirectInputBlocked(_host.HitTestElement(data.Position, "tablet-route"));
        UIElement target = captured ??
            (data.Point.IsInContact && !isDown && _activePointerTargets.TryGetValue(id, out var active) ? active : null)
            ?? hit ?? _host.Self;
        _activePointerTargets[id] = target;
        _lastPointerPoints[id] = data.Point;
        long token = GetPointerSessionToken(id, isDown);
        _pendingStylusTargets[id] = (token, target);
        bool Current() => IsPointerSessionCurrent(id, token) && current() && IsPointerSessionCurrent(id, token);
        if (device == null) _activeStylusDevices[id] = device = new StylusDevice(unchecked((int)id));
        var properties = data.Point.Properties;
        if (data.IsInRange) Tablet.CurrentStylusDevice = device;
        device.UpdateState(data.Position, data.StylusPoints, !data.Point.IsInContact,
            properties.IsInverted, data.IsInRange, properties.IsBarrelButtonPressed,
            properties.IsEraser, data.IsInRange ? target : null);
        RealTimeStylusProcessResult? result = null;
        try
        {
            StylusInputAction action = ResolveStylusInputAction(isDown, isUp, data.Point.IsInContact);
            if (isDown) { TrackGestureDown(data, timestamp); TryEmitDoubleTap(target, device, data, timestamp); }
            else if (!isUp && data.IsInRange) TrackGestureMove(data, timestamp);
            if (!Current()) return true;
            result = _host.RealTimeStylus.ProcessTablet(id, target, action,
                device.GetStylusPoints(target), timestamp, !data.Point.IsInContact,
                data.IsInRange, properties.IsBarrelButtonPressed, properties.IsEraser,
                properties.IsInverted, data.IsCanceled, Current);
            if (!Current()) return true;
            device.UpdateState(data.Position, result.RawStylusInput.GetStylusPoints(),
                !data.Point.IsInContact, properties.IsInverted, data.IsInRange,
                properties.IsBarrelButtonPressed, properties.IsEraser, data.IsInRange ? target : null);
            if (result.Canceled)
            {
                Action mouseCancel = PrepareMacOSTabletMouseCancellation(id);
                try { HandleMacOSTabletCancel(data with { IsCanceled = true }, timestamp); }
                finally { mouseCancel(); }
                return true;
            }
            // A compatibility click may replace a Button's text presenter.
            // Its detached old leaf cannot carry a later physical range exit
            // back to the window, so route that exit through the current hit.
            UIElement? rangeExitTarget = result.PreviousTarget is { } previousTarget &&
                !IsDescendantOf(previousTarget, _host.Self) ? target : null;
            bool handled = RaiseStylusExtendedEvents(target, device, timestamp, action, result, rangeExitTarget);
            if (!Current()) return true;
            if (isUp) TrackGestureUpAndEmit(target, device, data, timestamp);
            if (!Current()) return true;
            if (data.IsInRange)
            {
                RoutedEvent previewEvent = isDown ? UIElement.PreviewStylusDownEvent :
                    isUp ? UIElement.PreviewStylusUpEvent : UIElement.PreviewStylusMoveEvent;
                RoutedEvent bubbleEvent = isDown ? UIElement.StylusDownEvent :
                    isUp ? UIElement.StylusUpEvent : UIElement.StylusMoveEvent;
                var preview = CreateStylusEventArgs(device, timestamp, previewEvent, isDown);
                target.RaiseEvent(preview);
                if (!Current()) return true;
                handled |= preview.Handled;
                if (!preview.Handled && !result.Canceled)
                {
                    var bubble = CreateStylusEventArgs(device, timestamp, bubbleEvent, isDown);
                    target.RaiseEvent(bubble);
                    if (!Current()) return true;
                    handled |= bubble.Handled;
                }
            }
            if (data.IsInRange)
            {
                DispatchManipulationPipeline(target, data, isDown, isUp, handled, timestamp);
                if (!Current()) return true;
                if (isDown) RaisePointerDownPipeline(target, data.Point, data.Modifiers, timestamp, data.StylusPoints);
                else if (isUp) RaisePointerUpPipeline(target, data.Point, data.Modifiers, timestamp, data.StylusPoints);
                else RaisePointerMovePipeline(target, data.Point, data.Modifiers, timestamp, data.StylusPoints);
                if (!Current()) return true;
            }
            if (handled) CancelMacOSTabletMouse(id);
            else if (mouse != null && Current())
            {
                bool previous = SuppressMouseToPointerPromotion;
                SuppressMouseToPointerPromotion = true;
                try { mouse(); }
                finally { SuppressMouseToPointerPromotion = previous; }
            }
            return false;
        }
        finally
        {
            if (result != null && Current())
            {
                // A completed stroke must notify before another Down can reuse
                // this pen ID. The public async path keeps its existing policy.
                foreach (RawStylusInput.ProcessedCallback callback in result.RawStylusInput.DrainProcessedCallbacks())
                {
                    if (!Current()) break;
                    UIElement? element = callback.PlugIn.Element;
                    bool verified = element != null && IsDescendantOf(result.RawStylusInput.Target, element);
                    callback.PlugIn.InvokeProcessed(callback.Action, callback.CallbackData, verified);
                }
                if (Current())
                {
                    if (!data.IsInRange) CleanupPointerSession(id);
                    if (isUp || result.SessionEnded) CompletePointerSession(id, token);
                }
            }
        }
    }

    internal void HandleMacOSTabletCancel(PointerInputData data, int timestamp)
    {
        uint id = data.PointerId;
        _activePointerTargets.Remove(id, out var target);
        _lastPointerPoints.Remove(id, out var last);
        _activeStylusDevices.Remove(id, out var device);
        InvalidatePointerSession(id);
        bool Current() => !_activeStylusDevices.ContainsKey(id);
        bool RetiredAt(UIElement element) => !_activePointerTargets.TryGetValue(id, out var next) ||
            !IsDescendantOf(next, element);
        Action? notifyLeave = _host.RealTimeStylus.PrepareTabletCancellation(id, target,
            data.StylusPoints, timestamp, data.Point.Properties.IsInverted, RetiredAt);
        _gestureTrackers.Remove(id);
        bool wasInRange = device?.InRange == true;
        if (ReferenceEquals(Tablet.CurrentStylusDevice, device)) Tablet.CurrentStylusDevice = null;
        if (device != null)
            device.UpdateState(data.Position, data.StylusPoints, true, data.Point.Properties.IsInverted,
                false, false, false, null);
        // The old slots are detached before any user callback. A newly created
        // packet for this ID is never removed by the old cancellation.
        try
        {
            CancelManipulationSession(id, timestamp);
            device?.Capture(null);
            if (wasInRange && target != null && device != null && Current())
                RaiseStylusSimpleEventPair(target, device, timestamp,
                    UIElement.PreviewStylusOutOfRangeEvent, UIElement.StylusOutOfRangeEvent);
        }
        finally
        {
            try { notifyLeave?.Invoke(); }
            finally
            {
                if (target != null && Current())
                    RaisePointerCancelPipeline(target, last ?? data.Point, data.Modifiers, timestamp);
            }
        }
    }

    internal void TrackMacOSTabletMouse(uint id, int type, int button, long precedingEpoch)
    {
        if (button is < 0 or > 4) return;
        uint bit = 1u << button;
        if (type == 31 && _mousePressEpoch == precedingEpoch + 1)
        {
            if (_macOSTabletMousePointer != id || _macOSTabletMouseEpoch != precedingEpoch)
                _macOSTabletMouseButtons = 0;
            _macOSTabletMousePointer = id;
            _macOSTabletMouseEpoch = _mousePressEpoch;
            _macOSTabletMouseButtons |= bit;
            _macOSTabletMouseCapture = UIElement.MouseCapturedElement is { } capture &&
                _mousePressedChain.Contains(capture) && IsDescendantOf(capture, _host.Self)
                ? capture : null;
        }
        else if (type == 32 && _macOSTabletMousePointer == id && _macOSTabletMouseEpoch == precedingEpoch && _mousePressEpoch == precedingEpoch)
        {
            _macOSTabletMouseButtons &= ~bit;
            if (_macOSTabletMouseButtons == 0) { _macOSTabletMousePointer = null; _macOSTabletMouseCapture = null; }
        }
    }

    internal Action PrepareMacOSTabletMouseCancellation(uint id)
    {
        if (_macOSTabletMousePointer != id) return static () => { };
        uint buttons = _macOSTabletMouseButtons;
        long epoch = _macOSTabletMouseEpoch;
        UIElement? capture = _macOSTabletMouseCapture;
        _macOSTabletMousePointer = null; _macOSTabletMouseButtons = 0; _macOSTabletMouseCapture = null;
        if (epoch != _mousePressEpoch) return static () => { };
        long retirement = ++_mousePressEpoch;
        ++_mouseInputGeneration;
        UIElement[] pressed = DetachPressedChain(_mousePressedChain);
        Mouse.UpdateState(Mouse.Position, UIElement.MouseDirectlyOverElement, new MouseButtonStates
        {
            Left = (buttons & 1) != 0 ? MouseButtonState.Released : Mouse.LeftButton,
            Right = (buttons & 2) != 0 ? MouseButtonState.Released : Mouse.RightButton,
            Middle = (buttons & 4) != 0 ? MouseButtonState.Released : Mouse.MiddleButton,
            XButton1 = (buttons & 8) != 0 ? MouseButtonState.Released : Mouse.XButton1,
            XButton2 = (buttons & 16) != 0 ? MouseButtonState.Released : Mouse.XButton2
        });
        return () =>
        {
            if (_mousePressEpoch == retirement && capture != null && ReferenceEquals(capture, UIElement.MouseCapturedElement))
                capture.ReleaseMouseCapture();
            ClearDetachedPressedChain(pressed, _mousePressedChain, _keyboardPressedChain);
        };
    }

    internal void CancelMacOSTabletMouse(uint id) => PrepareMacOSTabletMouseCancellation(id)();
}
