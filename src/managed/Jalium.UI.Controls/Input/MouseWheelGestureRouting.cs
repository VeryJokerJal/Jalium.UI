using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Keeps release and momentum routed to the subtree where the gesture began.</summary>
internal sealed class MouseWheelGestureRouting
{
    private WeakReference<UIElement>? _target;
    private readonly List<WeakReference<UIElement>> _ancestors = [];
    private bool _retired;

    internal UIElement? ResolveTarget(UIElement proposed, UIElement root, MouseWheelPhase phase, MouseWheelPhase momentumPhase)
    {
        if (phase == MouseWheelPhase.None && momentumPhase == MouseWheelPhase.None)
        {
            Clear();
            _retired = false;
            return proposed;
        }

        bool begins = (phase & (MouseWheelPhase.Began | MouseWheelPhase.MayBegin)) != 0;
        if (begins)
        {
            _retired = false;
            Remember(proposed, root);
        }
        if (_retired) return null;

        UIElement? target = null;
        if (_target != null && _target.TryGetTarget(out var previous) && IsWithin(previous, root))
            target = previous;
        if (target == null && _target != null)
        {
            // Virtualization can recycle the hit leaf while its viewer remains.
            // Resume at the closest surviving ancestor of the original route,
            // never at an unrelated control now beneath the mouse pointer.
            foreach (var ancestor in _ancestors)
                if (ancestor.TryGetTarget(out var candidate) && IsWithin(candidate, root))
                { target = candidate; break; }
            if (target == null) { Clear(); _retired = true; return null; }
            _target = new WeakReference<UIElement>(target);
        }
        if (target == null) { target = proposed; Remember(target, root); }

        if ((phase & MouseWheelPhase.Cancelled) != 0 ||
            (momentumPhase & (MouseWheelPhase.Ended | MouseWheelPhase.Cancelled)) != 0)
        { Clear(); _retired = true; }

        // A direct Ended can be followed immediately by native momentum.
        return target;
    }

    internal void Cancel()
    {
        if (DetachCancellationTarget() is { } target)
            RaiseCancellation(target);
    }

    internal UIElement? DetachCancellationTarget()
    {
        var previous = _target;
        Clear();
        _retired = true;
        return previous != null && previous.TryGetTarget(out var target) ? target : null;
    }

    internal static void RaiseCancellation(UIElement target, Func<bool>? current = null) =>
        InputRoute.Raise(target,new MouseWheelEventArgs(UIElement.PreviewMouseWheelEvent, new Point(),
            0, 0, true, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, Environment.TickCount,
            MouseWheelPhase.Cancelled, MouseWheelPhase.None), current);

    internal void ClearWithin(UIElement root)
    {
        if (_target != null && _target.TryGetTarget(out var target) && IsWithin(target, root))
        { Clear(); _retired = true; }
    }

    private void Remember(UIElement target, UIElement root)
    {
        Clear();
        _target = new WeakReference<UIElement>(target);
        for (Visual? current = target.VisualParent; current != null; current = current.VisualParent)
        {
            if (current is UIElement element) _ancestors.Add(new WeakReference<UIElement>(element));
            if (ReferenceEquals(current, root)) break;
        }
    }

    private void Clear() { _target = null; _ancestors.Clear(); }

    private static bool IsWithin(Visual element, Visual root)
    {
        for (Visual? current = element; current != null; current = current.VisualParent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }
}
