using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

internal sealed class CssFontDependency(CssNode node)
{
    private readonly WeakReference<CssNode> _node = new(node);
    private volatile bool _observed;
    private bool _registered;
    private static readonly object s_gate = new();
    private static readonly List<WeakReference<CssFontDependency>> s_dependents = [];

    internal void Observe()
    {
        _observed = true;
        if (_registered) return;
        lock (s_gate)
        {
            if (_registered) return;
            _registered = true;
            if (s_dependents.Count % 256 == 0) s_dependents.RemoveAll(item => !item.TryGetTarget(out _));
            s_dependents.Add(new(this));
        }
    }

    internal void Reset() => _observed = false;

    internal static void FontsChanged()
    {
        CssFontDependency[] dependents;
        lock (s_gate)
        {
            s_dependents.RemoveAll(item => !item.TryGetTarget(out _));
            dependents = s_dependents.Select(item => item.TryGetTarget(out var target) ? target : null)
                .OfType<CssFontDependency>().ToArray();
        }
        foreach (var dependent in dependents)
        {
            if (!dependent._observed || !dependent._node.TryGetTarget(out var node) || node.Dispatcher.HasShutdownStarted) continue;
            if (node.Dispatcher.CheckAccess()) dependent.Invalidate();
            else node.Dispatcher.BeginInvoke(DispatcherPriority.Render, dependent.Invalidate);
        }
    }

    private void Invalidate()
    {
        if (_node.TryGetTarget(out var node))
        {
            node.InvalidateMeasure();
            CssEvaluationScheduler.InvalidateSubtree(node);
        }
    }
}

internal readonly record struct CssFontInfo(string Family, int Weight = 400, int Style = 0)
{
    internal static CssFontInfo Initial => new(SystemFonts.MessageFontFamily.Source);
    internal FontUnitMetrics Metrics(double size) => TextMeasurement.GetFontUnitMetrics(Family, size, Weight, Style);

    internal static CssFontInfo Read(CssNode node)
    {
        object? Value(string name) => CssDependencyPropertyLookup.Find(node.GetType(), name) is { } property ? node.GetValue(property) : null;
        var family = Value("FontFamily") as FontFamily;
        var weight = Value("FontWeight") is FontWeight w ? w.ToOpenTypeWeight() : 400;
        var style = Value("FontStyle") is FontStyle s ? s : FontStyles.Normal;
        return new(family?.Source ?? Initial.Family, weight, style == FontStyles.Italic ? 1 : style == FontStyles.Oblique ? 2 : 0);
    }

    internal CssFontInfo With(string property, object? value) => property switch
    {
        "FontFamily" when value is FontFamily family => this with { Family = family.Source },
        "FontWeight" when value is FontWeight weight => this with { Weight = weight.ToOpenTypeWeight() },
        "FontStyle" when value is FontStyle style => this with { Style = style == FontStyles.Italic ? 1 : style == FontStyles.Oblique ? 2 : 0 },
        _ => this,
    };
}

internal sealed record CssFontContext(
    CssFontInfo Element, CssFontInfo Parent, CssFontInfo Root,
    CssLineHeight ElementLine, CssLineHeight ParentLine, CssLineHeight RootLine,
    bool IsRoot, CssFontDependency? Dependency = null, long MetricsEpoch = 0, bool UseParentLine = false)
{
    internal static CssFontContext Initial => new(CssFontInfo.Initial, CssFontInfo.Initial, CssFontInfo.Initial, default, default, default, false);

    internal double Resolve(CssUnit unit, in CssLengthContext lengths)
    {
        var root = unit is CssUnit.Rex or CssUnit.Rcap or CssUnit.Rch or CssUnit.Ric or CssUnit.Rlh;
        var font = root ? Root : Element;
        var size = root ? lengths.RootFontSize : lengths.ElementFontSize;
        var line = root ? RootLine : ElementLine;
        if (unit is CssUnit.Lh or CssUnit.Rlh)
        {
            if (UseParentLine && (!root || IsRoot))
            {
                font = Parent; size = lengths.InheritedFontSize; line = ParentLine;
            }
            if (line.Kind == CssLineHeightKind.Number) return line.Value * size;
            if (line.Kind == CssLineHeightKind.Pixels) return line.Value;
        }
        Dependency?.Observe();
        var metrics = font.Metrics(size);
        return unit switch
        {
            CssUnit.Ex or CssUnit.Rex => metrics.XHeight,
            CssUnit.Cap or CssUnit.Rcap => metrics.CapHeight,
            CssUnit.Ch or CssUnit.Rch => metrics.ZeroAdvance,
            CssUnit.Ic or CssUnit.Ric => metrics.IdeographicAdvance,
            _ => metrics.LineHeight,
        };
    }

    internal CssFontContext ForFontProperty() => this with
    {
        Element = Parent, ElementLine = ParentLine,
        Root = IsRoot ? CssFontInfo.Initial : Root, RootLine = IsRoot ? default : RootLine,
        UseParentLine = false,
    };

    internal CssFontContext ForLineHeight() => this with
    {
        UseParentLine = true,
    };
}
