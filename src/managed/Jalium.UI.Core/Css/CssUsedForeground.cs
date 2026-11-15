using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Native text hosts whose default ink is resolved outside Foreground metadata.</summary>
internal interface ICssUsedForegroundSource
{
    Brush GetUsedForeground();
}

/// <summary>Reads the same ink as the native text host, without authoring a source value.</summary>
internal static class CssUsedForeground
{
    internal static Brush? Get(DependencyObject source, CssNode? dependent = null)
    {
        for (var node = CssNode.Get(source); node is not null; node = node.FrameworkParent)
        {
            var property = CssDependencyPropertyLookup.Find(node.GetType(), "Foreground");
            var brush = property is null ? null : node.GetValue(property) as Brush;
            if (node.Target is ICssUsedForegroundSource native)
            {
                // Explicit values already use the DP precedence shared by CSS
                // and native controls. Only the native theme fallback needs an
                // extra resource dependency; it is invisible to that DP.
                if (dependent?.CssRuntimeState is { } state && node.Target is FrameworkElement host &&
                    (property is null || brush is null ||
                     !host.HasValueAboveInherited(property) &&
                     host.GetUncoercedBaseValueInternal(property).source == BaseValueSource.Default))
                {
                    if (state.NativeForegroundObserver is not { } observer || !observer.Observes(host))
                    {
                        state.NativeForegroundObserver?.Dispose();
                        state.NativeForegroundObserver = new Observer(dependent, host);
                    }
                    state.NativeForegroundObserver.Seen = true;
                }
                return native.GetUsedForeground();
            }
            if (brush is not null) return brush;
        }
        return null;
    }

    internal static void Begin(CssElementState state)
    {
        if (state.NativeForegroundObserver is { } observer) observer.Seen = false;
    }

    internal static void Finish(CssElementState state)
    {
        if (state.NativeForegroundObserver is not { Seen: false } observer) return;
        observer.Dispose();
        state.NativeForegroundObserver = null;
    }

    internal sealed class Observer : IDisposable
    {
        private readonly WeakReference<CssNode> _dependent;
        private readonly WeakReference<FrameworkElement> _host;
        private bool _disposed;
        internal bool Seen;

        internal Observer(CssNode dependent, FrameworkElement host)
        {
            _dependent = new(dependent);
            _host = new(host);
            host.ResourcesChanged += Changed;
        }

        internal bool Observes(FrameworkElement host)
            => !_disposed && _host.TryGetTarget(out var previous) && ReferenceEquals(previous, host);

        private void Changed(object? sender, EventArgs args)
        {
            if (_disposed) return;
            if (_dependent.TryGetTarget(out var dependent))
                CssEvaluationScheduler.InvalidateSubtree(dependent);
            else Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_host.TryGetTarget(out var host)) host.ResourcesChanged -= Changed;
        }
    }
}
