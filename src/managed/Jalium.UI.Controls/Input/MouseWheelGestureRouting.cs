using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Keeps release and momentum routed to the subtree where the gesture began.</summary>
internal sealed class MouseWheelGestureRouting
{
    private WeakReference<UIElement>? _target;

    internal UIElement ResolveTarget(UIElement proposed, UIElement root, MouseWheelPhase phase, MouseWheelPhase momentumPhase)
    {
        if (phase == MouseWheelPhase.None && momentumPhase == MouseWheelPhase.None)
        {
            _target = null;
            return proposed;
        }

        if ((phase & (MouseWheelPhase.Began | MouseWheelPhase.MayBegin)) != 0 ||
            _target == null || !_target.TryGetTarget(out var target) || !IsWithin(target, root))
        {
            target = proposed;
            _target = new WeakReference<UIElement>(target);
        }

        if ((phase & MouseWheelPhase.Cancelled) != 0 ||
            (momentumPhase & (MouseWheelPhase.Ended | MouseWheelPhase.Cancelled)) != 0)
            _target = null;

        // A direct Ended can be followed immediately by native momentum.
        return target;
    }

    internal void Cancel()
    {
        var previous = _target;
        _target = null;
        if (previous != null && previous.TryGetTarget(out var target))
        {
            target.RaiseEvent(new MouseWheelEventArgs(UIElement.PreviewMouseWheelEvent, new Point(),
                0, 0, true, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, Environment.TickCount,
                MouseWheelPhase.Cancelled, MouseWheelPhase.None));
        }
    }

    internal void ClearWithin(UIElement root)
    {
        if (_target != null && _target.TryGetTarget(out var target) && IsWithin(target, root))
            _target = null;
    }

    private static bool IsWithin(Visual element, Visual root)
    {
        for (Visual? current = element; current != null; current = current.VisualParent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }
}
