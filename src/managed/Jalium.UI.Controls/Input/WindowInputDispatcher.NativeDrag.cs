using Jalium.UI.Input;

namespace Jalium.UI.Controls;

internal sealed partial class WindowInputDispatcher
{
    internal Action PrepareNativeMouseDragCompletion()
    {
        // Tracking may finish inside MouseDown/Move. Retire that packet before
        // application callbacks can return to its mouse-to-pointer promotion.
        unchecked { _mouseInputGeneration++; }
        _activePointerTargets.TryGetValue(MousePointerId, out var target);
        _lastPointerPoints.TryGetValue(MousePointerId, out var point);
        long press = _mousePressEpoch, globalPress = Mouse.PressEpoch;
        InvalidatePointerSession(MousePointerId);
        _gestureTrackers.Remove(MousePointerId);
        CleanupPointerSession(MousePointerId);
        // All old id-owned state is gone before Cancel can start a successor.
        return () =>
        {
            if (_mousePressEpoch == press && Mouse.PressEpoch == globalPress &&
                target is not null && point?.IsInContact == true && IsDescendantOf(target, _host.Self))
            {
                // A capture callback may have installed a new press, including
                // on this same target. Never cancel that replacement gesture.
                RaisePointerCancelPipeline(target, point, ModifierKeys.None, Environment.TickCount,
                    () => _mousePressEpoch == press && Mouse.PressEpoch == globalPress && IsDescendantOf(target, _host.Self));
            }
        };
    }
}
