using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Weakly observes mutable foreground brushes used by contextual CSS colors.</summary>
internal sealed class CssColorBrushObserver : IDisposable
{
    private readonly WeakReference<CssNode> _element;
    private readonly WeakReference<SolidColorBrush> _brush;
    private readonly EventHandler _handler;
    private bool _disposed;
    internal bool Seen;

    private CssColorBrushObserver(CssNode element, SolidColorBrush brush)
    {
        _element = new(element);
        _brush = new(brush);
        _handler = Changed;
        Seen = true;
        brush.Changed += _handler;
    }

    internal static void Begin(CssElementState state)
    {
        if (state.ColorBrushObservers is not { } observers) return;
        foreach (var observer in observers) observer.Seen = false;
    }

    internal static void Observe(CssNode element, SolidColorBrush? brush)
    {
        if (brush is null || brush.IsFrozen || element.CssRuntimeState is not { } state) return;
        var observers = state.ColorBrushObservers ??= [];
        foreach (var observer in observers)
        {
            if (observer._disposed || !observer._brush.TryGetTarget(out var existing) ||
                !ReferenceEquals(existing, brush)) continue;
            observer.Seen = true;
            return;
        }
        observers.Add(new CssColorBrushObserver(element, brush));
    }

    internal static void Finish(CssElementState state)
    {
        if (state.ColorBrushObservers is not { } observers) return;
        for (var i = observers.Count - 1; i >= 0; i--)
        {
            if (observers[i].Seen && !observers[i]._disposed) continue;
            observers[i].Dispose();
            observers.RemoveAt(i);
        }
    }

    private void Changed(object? _, EventArgs __)
    {
        if (_disposed) return;
        if (_element.TryGetTarget(out var element))
            CssEvaluationScheduler.InvalidateSubtree(element);
        else Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_brush.TryGetTarget(out var brush)) brush.Changed -= _handler;
    }
}
