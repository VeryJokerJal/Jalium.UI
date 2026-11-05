using System.Runtime.CompilerServices;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Semantic category of a custom highlight registered in <see cref="CssHighlightRegistry"/>.
/// </summary>
public enum CssHighlightType
{
    Highlight,
    SpellingError,
    GrammarError,
}

/// <summary>
/// Represents a character offset range <c>[StartOffset, EndOffset)</c> scoped to a <see cref="DependencyObject"/>
/// for CSS custom or built-in highlight pseudo-elements.
/// </summary>
public readonly record struct CssHighlightRange(
    DependencyObject Scope,
    int StartOffset,
    int EndOffset,
    bool IsCurrent = false)
{
    public int Length => Math.Max(0, EndOffset - StartOffset);
    public bool IsCollapsed => EndOffset <= StartOffset;
}

/// <summary>
/// A collection of <see cref="CssHighlightRange"/> instances associated with a custom highlight name,
/// priority, and semantic <see cref="CssHighlightType"/>.
/// </summary>
public sealed class CssHighlight
{
    private readonly List<CssHighlightRange> _ranges = [];
    private int _priority;
    private CssHighlightType _type = CssHighlightType.Highlight;

    public CssHighlight(params CssHighlightRange[] ranges)
    {
        if (ranges is { Length: > 0 })
            _ranges.AddRange(ranges);
    }

    public CssHighlight(int priority, CssHighlightType type = CssHighlightType.Highlight, params CssHighlightRange[] ranges)
    {
        _priority = priority;
        _type = type;
        if (ranges is { Length: > 0 })
            _ranges.AddRange(ranges);
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority == value) return;
            _priority = value;
            CssHighlightRegistry.NotifyHighlightChanged(this);
        }
    }

    public CssHighlightType Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            CssHighlightRegistry.NotifyHighlightChanged(this);
        }
    }

    public int Size => _ranges.Count;

    public IReadOnlyList<CssHighlightRange> Ranges => _ranges;

    public CssHighlight Add(CssHighlightRange range)
    {
        _ranges.Add(range);
        CssHighlightRegistry.InvalidateScope(range.Scope);
        CssSelection.BumpRevision();
        return this;
    }

    public CssHighlight Add(DependencyObject scope, int startOffset, int endOffset, bool isCurrent = false)
        => Add(new CssHighlightRange(scope, startOffset, endOffset, isCurrent));

    public bool Remove(CssHighlightRange range)
    {
        if (!_ranges.Remove(range)) return false;
        CssHighlightRegistry.InvalidateScope(range.Scope);
        CssSelection.BumpRevision();
        return true;
    }

    public void Clear()
    {
        if (_ranges.Count == 0) return;
        var scopes = _ranges.Select(r => r.Scope).Distinct().ToArray();
        _ranges.Clear();
        foreach (var scope in scopes)
            CssHighlightRegistry.InvalidateScope(scope);
        CssSelection.BumpRevision();
    }

    public IEnumerable<CssHighlightRange> GetRangesForScope(DependencyObject scope)
    {
        for (int i = 0; i < _ranges.Count; i++)
        {
            var range = _ranges[i];
            if (!range.IsCollapsed && ReferenceEquals(range.Scope, scope))
                yield return range;
        }
    }
}

/// <summary>
/// Managed equivalent of the CSS Custom Highlight API <c>CSS.highlights</c> registry plus
/// built-in highlight range storage (<c>::search-text</c>, <c>::target-text</c>,
/// <c>::spelling-error</c>, <c>::grammar-error</c>) and SVG highlight paint properties.
/// </summary>
public static class CssHighlightRegistry
{
    public readonly record struct Entry(string Name, CssHighlight Highlight, long RegistrationOrder);

    private static readonly object s_gate = new();
    private static readonly Dictionary<string, Entry> s_entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<CssPseudoElement, List<CssHighlightRange>> s_builtInRanges = new();
    private static long s_nextOrder;
    private static int s_registered;

    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill",
        typeof(Brush),
        typeof(CssHighlightRegistry),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.RegisterAttached(
        "Stroke",
        typeof(Brush),
        typeof(CssHighlightRegistry),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.RegisterAttached(
        "StrokeWidth",
        typeof(double),
        typeof(CssHighlightRegistry),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static Brush? GetFill(DependencyObject element) => (Brush?)element.GetValue(FillProperty);
    public static void SetFill(DependencyObject element, Brush? value) => element.SetValue(FillProperty, value);

    public static Brush? GetStroke(DependencyObject element) => (Brush?)element.GetValue(StrokeProperty);
    public static void SetStroke(DependencyObject element, Brush? value) => element.SetValue(StrokeProperty, value);

    public static double GetStrokeWidth(DependencyObject element) => (double)(element.GetValue(StrokeWidthProperty) ?? 1.0);
    public static void SetStrokeWidth(DependencyObject element, double value) => element.SetValue(StrokeWidthProperty, value);

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void InitializeExtension()
    {
        CssPropertyRegistry.RegisterExtension(Register);
    }
#pragma warning restore CA2255

    internal static void Register()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "fill",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = FillProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseSvgPaint("fill", FillProperty, ref reader, out var value) && reader.AtEnd ? value : null,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "stroke",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = StrokeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseSvgPaint("stroke", StrokeProperty, ref reader, out var value) && reader.AtEnd ? value : null,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "stroke-width",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = StrokeWidthProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseStrokeWidth(ref reader, out var value) && reader.AtEnd ? value : null,
        });
    }

    public static void Set(string name, CssHighlight highlight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(highlight);
        Register();

        HashSet<DependencyObject> scopes = [];
        lock (s_gate)
        {
            long order;
            if (s_entries.TryGetValue(name, out var existing))
            {
                order = existing.RegistrationOrder;
                foreach (var r in existing.Highlight.Ranges)
                    scopes.Add(r.Scope);
            }
            else
            {
                order = ++s_nextOrder;
            }
            s_entries[name] = new Entry(name, highlight, order);
            foreach (var r in highlight.Ranges)
                scopes.Add(r.Scope);
        }

        foreach (var scope in scopes)
            InvalidateScope(scope);
        CssSelection.BumpRevision();
    }

    public static bool Remove(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        Entry removed;
        lock (s_gate)
        {
            if (!s_entries.Remove(name, out removed))
                return false;
        }
        foreach (var range in removed.Highlight.Ranges)
            InvalidateScope(range.Scope);
        CssSelection.BumpRevision();
        return true;
    }

    public static void Clear()
    {
        HashSet<DependencyObject> scopes = [];
        lock (s_gate)
        {
            foreach (var entry in s_entries.Values)
                foreach (var range in entry.Highlight.Ranges)
                    scopes.Add(range.Scope);
            s_entries.Clear();
            s_nextOrder = 0;
        }
        ClearBuiltInRanges();
        foreach (var scope in scopes)
            InvalidateScope(scope);
        CssSelection.BumpRevision();
    }

    public static bool TryGet(string name, out CssHighlight? highlight)
    {
        lock (s_gate)
        {
            if (s_entries.TryGetValue(name, out var entry))
            {
                highlight = entry.Highlight;
                return true;
            }
        }
        highlight = null;
        return false;
    }

    public static CssHighlight? Get(string name)
        => TryGet(name, out var highlight) ? highlight : null;

    public static int Count
    {
        get
        {
            lock (s_gate) return s_entries.Count;
        }
    }

    /// <summary>
    /// Returns all registered custom highlights ordered from bottom-most to top-most painting layer:
    /// ascending by <see cref="CssHighlight.Priority"/>, then ascending by registration order.
    /// </summary>
    public static IReadOnlyList<Entry> GetOrderedEntries()
    {
        lock (s_gate)
        {
            if (s_entries.Count == 0) return [];
            return s_entries.Values
                .OrderBy(e => e.Highlight.Priority)
                .ThenBy(e => e.RegistrationOrder)
                .ToArray();
        }
    }

    public static void SetSearchRanges(params CssHighlightRange[] ranges)
        => SetBuiltInRanges(CssPseudoElement.SearchText, ranges);

    public static void SetSearchRanges(DependencyObject scope, IEnumerable<(int Start, int End, bool IsCurrent)> ranges)
        => SetBuiltInRanges(CssPseudoElement.SearchText, ranges.Select(r => new CssHighlightRange(scope, r.Start, r.End, r.IsCurrent)));

    public static void SetTargetRanges(params CssHighlightRange[] ranges)
        => SetBuiltInRanges(CssPseudoElement.TargetText, ranges);

    public static void SetTargetRanges(DependencyObject scope, IEnumerable<(int Start, int End)> ranges)
        => SetBuiltInRanges(CssPseudoElement.TargetText, ranges.Select(r => new CssHighlightRange(scope, r.Start, r.End)));

    public static void SetSpellingErrorRanges(params CssHighlightRange[] ranges)
        => SetBuiltInRanges(CssPseudoElement.SpellingError, ranges);

    public static void SetSpellingErrorRanges(DependencyObject scope, IEnumerable<(int Start, int End)> ranges)
        => SetBuiltInRanges(CssPseudoElement.SpellingError, ranges.Select(r => new CssHighlightRange(scope, r.Start, r.End)));

    public static void SetGrammarErrorRanges(params CssHighlightRange[] ranges)
        => SetBuiltInRanges(CssPseudoElement.GrammarError, ranges);

    public static void SetGrammarErrorRanges(DependencyObject scope, IEnumerable<(int Start, int End)> ranges)
        => SetBuiltInRanges(CssPseudoElement.GrammarError, ranges.Select(r => new CssHighlightRange(scope, r.Start, r.End)));

    internal static void SetBuiltInRanges(CssPseudoElement kind, IEnumerable<CssHighlightRange> ranges)
    {
        Register();
        HashSet<DependencyObject> scopes = [];
        lock (s_gate)
        {
            if (s_builtInRanges.TryGetValue(kind, out var existing))
            {
                foreach (var r in existing) scopes.Add(r.Scope);
                existing.Clear();
            }
            else
            {
                existing = [];
                s_builtInRanges[kind] = existing;
            }
            foreach (var r in ranges)
            {
                existing.Add(r);
                scopes.Add(r.Scope);
            }
        }
        foreach (var scope in scopes)
            InvalidateScope(scope);
        CssSelection.BumpRevision();
    }

    public static void ClearBuiltInRanges()
    {
        HashSet<DependencyObject> scopes = [];
        lock (s_gate)
        {
            foreach (var list in s_builtInRanges.Values)
                foreach (var r in list)
                    scopes.Add(r.Scope);
            s_builtInRanges.Clear();
        }
        foreach (var scope in scopes)
            InvalidateScope(scope);
        CssSelection.BumpRevision();
    }

    internal static IReadOnlyList<CssHighlightRange> GetBuiltInRanges(CssPseudoElement kind, DependencyObject? scope = null)
    {
        lock (s_gate)
        {
            if (!s_builtInRanges.TryGetValue(kind, out var list) || list.Count == 0)
                return [];
            return scope is null
                ? list.Where(r => !r.IsCollapsed).ToArray()
                : list.Where(r => !r.IsCollapsed && ReferenceEquals(r.Scope, scope)).ToArray();
        }
    }

    internal static void NotifyHighlightChanged(CssHighlight highlight)
    {
        foreach (var range in highlight.Ranges)
            InvalidateScope(range.Scope);
        CssSelection.BumpRevision();
    }

    internal static void InvalidateScope(DependencyObject? scope)
    {
        if (scope is UIElement ui)
        {
            ui.InvalidateVisual();
        }
        else if (scope is TextElement text)
        {
            text.NotifyTextContentChanged();
            (CssNode.Get(scope).VisualOwner as UIElement)?.InvalidateVisual();
        }
    }

    private static bool TryParseSvgPaint(string propertyName, DependencyProperty property, ref CssTokenReader reader, out CssCompiledValue? result)
    {
        result = null;
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            result = new SvgPaintColorValue(propertyName, property, Brushes.Transparent, null);
            reader = probe;
            return true;
        }
        probe = reader;
        if (!CssColorParser.TryParseContextual(ref probe, out var color, out var current, out var deferred))
            return false;
        if (deferred is not null)
        {
            result = new SvgPaintColorValue(propertyName, property, null, deferred);
        }
        else if (current)
        {
            result = new SvgPaintColorValue(propertyName, property, null, "currentColor");
        }
        else
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            result = new SvgPaintColorValue(propertyName, property, brush, null);
        }
        reader = probe;
        return true;
    }

    private static bool TryParseStrokeWidth(ref CssTokenReader reader, out CssCompiledValue? result)
    {
        result = null;
        var probe = reader;
        if (probe.TryReadNumber(out var unitless, out var unit) && unit == CssUnit.None && probe.AtEnd && unitless >= 0)
        {
            result = new CssImmediateValue(StrokeWidthProperty, unitless);
            reader = probe;
            return true;
        }
        probe = reader;
        if (!probe.TryReadLength(out var length) ||
            (length.Expression is null && length.Value < 0))
            return false;
        if (length.IsAbsolute)
        {
            result = new CssImmediateValue(StrokeWidthProperty, length.ToPxAbsolute());
        }
        else
        {
            result = new CssDeferredValue("stroke-width", StrokeWidthProperty,
                (in CssApplyContext context, out object? value) =>
                {
                    var ok = length.TryResolve(context.Lengths, CssPercentBasis.ElementFontSize, out var pixels) && pixels >= 0;
                    value = pixels;
                    return ok;
                });
        }
        reader = probe;
        return true;
    }

    private sealed class SvgPaintColorValue(string propertyName, DependencyProperty targetProperty, Brush? fixedBrush, string? expression) : CssCompiledValue
    {
        private readonly CssContextualColorValue? _contextual = expression is not null ? new CssContextualColorValue(propertyName, expression) : null;

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            if (_contextual is null)
            {
                sink.Set(targetProperty, fixedBrush);
                return true;
            }
            var source = context.CurrentColor as SolidColorBrush;
            CssColorBrushObserver.Observe(context.Element, source);
            var baseColor = source?.Color ?? Colors.Black;
            if (!CssColorParser.TryResolveCurrentColor(expression!, baseColor, out var color))
                return false;
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            sink.Set(targetProperty, brush);
            CssContextualColorValue.Remember(context.Element, propertyName, _contextual);
            return true;
        }
    }
}
