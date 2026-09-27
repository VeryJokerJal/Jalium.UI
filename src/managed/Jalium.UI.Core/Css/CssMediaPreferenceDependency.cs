using Jalium.UI;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>Weakly tracks CSS nodes whose media conditions read system preferences.</summary>
internal sealed class CssMediaPreferenceDependency(CssNode node)
{
    private readonly WeakReference<CssNode> _node = new(node);
    private volatile bool _observed;
    private bool _registered;
    private static readonly object s_gate = new();
    private static readonly List<WeakReference<CssMediaPreferenceDependency>> s_dependents = [];

    static CssMediaPreferenceDependency()
    {
        SystemParameters.StaticPropertyChanged += static (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or
                nameof(SystemParameters.UIEffects) or nameof(SystemParameters.PrefersDarkColorScheme) or
                nameof(SystemParameters.HighContrast))
                SettingsChanged();
        };
    }

    internal void Observe()
    {
        _observed = true;
        if (_registered) return;
        lock (s_gate)
        {
            if (_registered) return;
            _registered = true;
            if (s_dependents.Count % 256 == 0)
                s_dependents.RemoveAll(item => !item.TryGetTarget(out _));
            s_dependents.Add(new(this));
        }
    }

    internal void Reset() => _observed = false;

    internal static void PointingDevicesChanged() => SettingsChanged();

    private static void SettingsChanged()
    {
        CssMediaPreferenceDependency[] dependents;
        lock (s_gate)
        {
            s_dependents.RemoveAll(item => !item.TryGetTarget(out _));
            dependents = s_dependents.Select(item => item.TryGetTarget(out var target) ? target : null)
                .OfType<CssMediaPreferenceDependency>().ToArray();
        }

        foreach (var dependent in dependents)
        {
            if (!dependent._observed || !dependent._node.TryGetTarget(out var node) ||
                node.Dispatcher.HasShutdownStarted) continue;
            if (node.Dispatcher.CheckAccess()) dependent.Invalidate();
            else node.Dispatcher.BeginInvoke(DispatcherPriority.Render, dependent.Invalidate);
        }
    }

    private void Invalidate()
    {
        if (_observed && _node.TryGetTarget(out var node))
            CssEvaluationScheduler.InvalidateSubtree(node);
    }
}
