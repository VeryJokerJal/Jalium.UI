using System.Runtime.CompilerServices;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Styling;

/// <summary>Highlight pseudo-elements' independent inheritance trees, without changing authored text or styles.</summary>
internal static class CssSelection
{
    internal static readonly string[] Properties =
    [
        "color", "background-color", "text-shadow",
        "text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness",
        "text-decoration-skip-ink", "text-decoration-skip-inset", "text-decoration-inset", "text-underline-position", "text-underline-offset",
        "text-emphasis-color", "fill", "stroke", "stroke-width"
    ];

    internal static bool Applies(string name) => name.StartsWith("--", StringComparison.Ordinal) || Properties.Contains(name);

    internal readonly record struct StyleKey(CssPseudoElement Kind, string Name, bool SearchCurrent);

    internal sealed class Style(CssNode original, StyleKey key)
    {
        internal readonly CssNode Original = original;
        internal readonly StyleKey Key = key;
        internal CssPseudoElement Kind => Key.Kind;
        internal string HighlightName => Key.Name;
        internal bool SearchCurrent => Key.SearchCurrent;
        internal readonly Run Values = new();
        internal Style? Parent;
        internal int Generation = -1;
        internal bool Refreshing;
        internal bool AuthorColors;
        internal bool HasPaint;
        internal bool HasExplicitDecorationLine;
        internal bool SuppressDefaultDecorations;
        internal bool CurrentColor = true;
        internal Func<Color, Effect?>? ShadowFormula;
        internal Dictionary<uint, Effect?>? PaintedShadows;
        internal Dictionary<(string Property, uint Argb), Brush>? PaintedColors;
    }

    private sealed class Target
    {
        internal CssPseudoElement Targets;
        internal bool Potential => Targets != CssPseudoElement.None;
    }

    private sealed class StyleSet
    {
        internal readonly Dictionary<StyleKey, Style> Map = new();
    }

    private static readonly ConditionalWeakTable<DependencyObject, Target> s_targets = new();
    private static readonly ConditionalWeakTable<DependencyObject, StyleSet> s_styles = new();
    private static readonly ConditionalWeakTable<DependencyObject, Style> s_projections = new();
    [ThreadStatic] private static StyleKey? s_activeQuery;
    private static bool s_active;
    private static int s_revision;

    internal static int Revision => Volatile.Read(ref s_revision);
    internal static void BumpRevision() => Interlocked.Increment(ref s_revision);

    internal static bool IsProjection(CssNode node) => s_active && s_projections.TryGetValue(node.Target, out _);
    internal static CssNode? Origin(CssNode node) => s_active && s_projections.TryGetValue(node.Target, out var style) ? style.Original : null;
    internal static DependencyObject? StyleParent(DependencyObject target) => s_active && s_projections.TryGetValue(target, out var style)
        ? style.Parent?.Values ?? style.Original.Target : null;

    internal static CssPseudoElement EffectivePseudoElement(CssCompound compound)
    {
        var declared = compound.PseudoElement;
        if (s_activeQuery is not { } query || !declared.IsHighlight())
            return declared;

        var matches = query.Kind switch
        {
            CssPseudoElement.Highlight => declared == CssPseudoElement.Highlight &&
                string.Equals(compound.HighlightName, query.Name, StringComparison.Ordinal),
            CssPseudoElement.SearchText => declared == CssPseudoElement.SearchText &&
                (!compound.SearchCurrent || query.SearchCurrent),
            _ => declared == query.Kind,
        };

        // CssGeneratedContent.Kind(projection) returns CssPseudoElement.Selection for all
        // CssSelection projections. Return Selection when the selector matches the active
        // highlight query, or a non-Selection highlight bit when it must be rejected.
        return matches ? CssPseudoElement.Selection : CssPseudoElement.Highlight;
    }

    internal static void Update(CssNode origin, CssPseudoElement targets)
    {
        if (IsProjection(origin) || CssFirstLine.IsProjection(origin) || origin.Target is ICssAnonymousBox or ICssStyledBox) return;
        var highlightTargets = targets & CssPseudoElementExtensions.HighlightMask;
        if (highlightTargets == CssPseudoElement.None &&
            (!s_active || !s_targets.TryGetValue(origin.Target, out var previous) || !previous.Potential))
            return;
        s_active = true;
        s_targets.GetOrCreateValue(origin.Target).Targets = highlightTargets;
        Interlocked.Increment(ref s_revision);
        (origin.VisualOwner as UIElement)?.InvalidateVisual();
    }

    internal static Style? For(DependencyObject source)
        => For(source, CssPseudoElement.Selection, null, false);

    internal static Style? For(DependencyObject source, CssPseudoElement kind, string? highlightName = null, bool searchCurrent = false)
    {
        if (!s_active || !kind.IsHighlight()) return null;
        if (kind == CssPseudoElement.SearchText && string.Equals(highlightName, "current", StringComparison.OrdinalIgnoreCase))
        {
            searchCurrent = true;
            highlightName = null;
        }
        var normalizedName = kind == CssPseudoElement.Highlight ? (highlightName ?? string.Empty) : string.Empty;
        if (kind == CssPseudoElement.Highlight && normalizedName.Length == 0)
            return null;

        var nodes = new List<CssNode>();
        var potential = false;
        var node = CssNode.Get(source);
        node = CssFirstLine.RuleOrigin(node) ?? CssGeneratedContent.Origin(node) ?? node;
        for (var current = node; current is not null && nodes.Count < 4096; current = CssMatcher.CssAncestor(current))
        {
            nodes.Add(current);
            potential |= s_targets.TryGetValue(current.Target, out var target) && (target.Targets & kind) != 0;
        }
        if (!potential) return null;

        var key = new StyleKey(kind, normalizedName, kind == CssPseudoElement.SearchText && searchCurrent);
        var previousQuery = s_activeQuery;
        s_activeQuery = key;
        try
        {
            Style? parent = null;
            for (var index = nodes.Count - 1; index >= 0; index--)
            {
                var original = nodes[index];
                var styleSet = s_styles.GetValue(original.Target, _ =>
                {
                    var createdSet = new StyleSet();
                    original.Target.PropertyChangedInternal += (property, _, _) =>
                    {
                        if (!ContextProperty(property)) return;
                        lock (createdSet.Map)
                        {
                            foreach (var entry in createdSet.Map.Values)
                                if (entry.Refreshing) return;
                        }
                        Interlocked.Increment(ref s_revision);
                        (original.VisualOwner as UIElement)?.InvalidateVisual();
                    };
                    return createdSet;
                });

                Style style;
                lock (styleSet.Map)
                {
                    if (!styleSet.Map.TryGetValue(key, out style!))
                    {
                        style = new Style(original, key);
                        s_projections.Add(style.Values, style);
                        styleSet.Map[key] = style;
                    }
                }

                if (!ReferenceEquals(style.Parent, parent))
                {
                    style.Parent = parent;
                    style.Generation = -1;
                }
                if (style.Generation != Revision)
                {
                    style.Refreshing = true;
                    try
                    {
                        CopyContext(style);
                        CssEngine.EvaluateElement(style.Values);
                        if (style.HasExplicitDecorationLine)
                        {
                            var lines = (CssTextDecorationLine)style.Values.GetValue(CssTextDecorationProperties.LineProperty)!;
                            style.SuppressDefaultDecorations = lines == CssTextDecorationLine.None;
                        }
                        else
                        {
                            style.SuppressDefaultDecorations = style.Parent?.SuppressDefaultDecorations == true;
                        }
                        style.Generation = Revision;
                    }
                    finally { style.Refreshing = false; }
                }
                parent = style;
            }
            return parent;
        }
        finally
        {
            s_activeQuery = previousQuery;
        }
    }

    private static bool ContextProperty(DependencyProperty property) => property.Name is "Foreground" or "FlowDirection" or "Language" or "LineHeight"
        || property.Name.Contains("Font", StringComparison.Ordinal) || property.Name == "CssComputedLineHeight";

    private static void CopyContext(Style style)
    {
        foreach (var property in CssPropertyRegistry.GetInheritedStorageProperties().Where(ContextProperty)
            .Concat(new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
                TextElement.FontStyleProperty, TextElement.FontStretchProperty, FrameworkElement.FlowDirectionProperty }).Distinct())
        {
            if (property.ReadOnly || property.Name == "Foreground") continue;
            style.Values.SetLayerValue(property, style.Original.GetValue(property), DependencyObject.LayerValueSource.StyleSetter);
        }
        var foreground = style.Parent is { CurrentColor: false } parent ? parent.Values.Foreground : SourceForeground(style.Original);
        style.Values.SetLayerValue(TextElement.ForegroundProperty, foreground, DependencyObject.LayerValueSource.StyleSetter);
        style.Values.SetValue(CssComputedLineHeightValue.Property, CssComputedLineHeightValue.Inherited(style.Original));
    }

    internal static Brush? SourceForeground(CssNode original)
        => CssUsedForeground.Get(original.Target);
    internal static SolidColorBrush? CurrentColorSource(CssNode projection)
        => Origin(projection) is { } origin ? SourceForeground(origin) as SolidColorBrush : null;

    internal static void BeginCascade(CssNode node, IEnumerable<CssCompiledDeclaration> declarations)
    {
        if (!s_projections.TryGetValue(node.Target, out var style)) return;
        var list = declarations as IList<CssCompiledDeclaration> ?? declarations.ToList();
        style.AuthorColors = style.Parent?.AuthorColors == true || list.Any(d => d.Name is "color" or "background-color" or "fill" or "stroke");
        style.HasPaint = style.Parent?.HasPaint == true || list.Any(d => !d.Name.StartsWith("--", StringComparison.Ordinal));
        style.HasExplicitDecorationLine = style.Parent?.HasExplicitDecorationLine == true || list.Any(d => d.Name == "text-decoration-line");
        style.SuppressDefaultDecorations = false;
        style.CurrentColor = false;
        style.ShadowFormula = null; style.PaintedShadows?.Clear(); style.PaintedColors?.Clear();
    }

    // currentColor in highlights remains relative to the next painted layer,
    // including intervening first-line/first-letter ink. Computed CSS storage
    // continues to describe the originating element without mutating its values.
    internal static Brush? Foreground(Style style, Brush? underlying)
        => style.CurrentColor ? null : UsedForeground(style, underlying);
    private static Brush? UsedForeground(Style style, Brush? underlying)
        => style.CurrentColor ? underlying : PaintColor(style, "color", underlying, style.Values.Foreground);
    internal static Brush? Background(Style style, Brush? underlying)
        => PaintColor(style, "background-color", UsedForeground(style, underlying), style.Values.Background);
    internal static Brush? DecorationColor(Style style, Brush? underlying)
    {
        var foreground = UsedForeground(style, underlying);
        return PaintColor(style, "text-decoration-color", foreground,
            CssTextDecorationProperties.Color(style.Values) ?? foreground);
    }
    internal static Brush? EmphasisColor(Style style, Brush? underlying)
    {
        var foreground = UsedForeground(style, underlying);
        return PaintColor(style, "text-emphasis-color", foreground,
            (style.Values.GetValue(CssTextEmphasis.ColorProperty) as Brush) ?? foreground);
    }
    internal static Brush? SvgFill(Style style, Brush? underlying)
    {
        var foreground = UsedForeground(style, underlying);
        return PaintColor(style, "fill", foreground, CssHighlightRegistry.GetFill(style.Values));
    }
    internal static Brush? SvgStroke(Style style, Brush? underlying)
    {
        var foreground = UsedForeground(style, underlying);
        return PaintColor(style, "stroke", foreground, CssHighlightRegistry.GetStroke(style.Values));
    }
    internal static double SvgStrokeWidth(Style style)
        => CssHighlightRegistry.GetStrokeWidth(style.Values);

    internal static Brush ReplacedOverlayBrush(DependencyObject source, Brush? nativeBackground = null, Brush? underlying = null)
    {
        var style = For(source, CssPseudoElement.Selection);
        if (style is { AuthorColors: true } && Background(style, underlying) is SolidColorBrush { Color.A: > 0 } authored)
        {
            var alpha = authored.Color.A == 255 ? (byte)128 : authored.Color.A;
            var wash = new SolidColorBrush(Color.FromArgb(alpha, authored.Color.R, authored.Color.G, authored.Color.B));
            wash.Freeze();
            return wash;
        }
        if (nativeBackground is SolidColorBrush { Color.A: > 0 } nativeSolid)
        {
            var alpha = Math.Min((byte)112, nativeSolid.Color.A);
            var wash = new SolidColorBrush(Color.FromArgb(alpha, nativeSolid.Color.R, nativeSolid.Color.G, nativeSolid.Color.B));
            wash.Freeze();
            return wash;
        }
        var fallback = new SolidColorBrush(Color.FromArgb(96, 56, 189, 248));
        fallback.Freeze();
        return fallback;
    }

    private static Brush? PaintColor(Style style, string property, Brush? current, Brush? fallback)
    {
        if (current is not SolidColorBrush solid ||
            CssNode.Get(style.Values).CssRuntimeState?.ContextualColors?.TryGetValue(property, out var value) != true ||
            !CssColorParser.TryResolveCurrentColor(value.Expression, solid.Color, out var color)) return fallback;
        var key = (property, color.ToArgb());
        var cache = style.PaintedColors ??= [];
        if (cache.TryGetValue(key, out var cached)) return cached;
        if (cache.Count >= 16) cache.Clear();
        var brush = new SolidColorBrush(color); brush.Freeze(); cache[key] = brush; return brush;
    }

    internal static void RememberShadow(CssNode projection, Func<Color, Effect?> formula)
    {
        if (s_active && s_projections.TryGetValue(projection.Target, out var style)) style.ShadowFormula = formula;
    }

    internal static Effect? Shadow(Style style, Brush? underlying)
    {
        if (style.ShadowFormula is not { } formula || UsedForeground(style, underlying) is not SolidColorBrush foreground)
            return CssTextShadowProperties.Value(style.Values);
        var cache = style.PaintedShadows ??= []; var key = foreground.Color.ToArgb();
        if (cache.TryGetValue(key, out var effect)) return effect;
        if (cache.Count >= 8) cache.Clear();
        return cache[key] = formula(foreground.Color);
    }

    private static void InheritColorFormula(Style style, string property)
    {
        if (style.Parent is { } parent && CssNode.Get(parent.Values).CssRuntimeState?.ContextualColors?.TryGetValue(property, out var value) == true)
            CssContextualColorValue.Remember(CssNode.Get(style.Values), property, value);
    }

    internal static void MarkCurrentColor(CssNode node)
    {
        if (s_active && s_projections.TryGetValue(node.Target, out var style)) style.CurrentColor = true;
    }

    internal static bool ApplyWide(CssNode node, string name, string keyword, in CssApplyContext context, ICssSetterSink sink)
    {
        if (!s_active || !s_projections.TryGetValue(node.Target, out var style) || !Applies(name) ||
            keyword is not ("inherit" or "unset")) return false;
        if (name == "color")
        {
            style.CurrentColor = style.Parent?.CurrentColor != false;
            sink.Set(TextElement.ForegroundProperty, style.CurrentColor ? SourceForeground(style.Original) : style.Parent!.Values.Foreground);
            if (!style.CurrentColor) InheritColorFormula(style, name);
            return true;
        }
        if (style.Parent is null) return new CssWideValue(name, "initial").TryApply(context, sink);
        if (name == "text-shadow")
        {
            // Lengths have already computed on the parent; only the symbolic
            // color is evaluated against this descendant's painted foreground.
            style.ShadowFormula = style.Parent.ShadowFormula;
            sink.Set(CssTextShadowProperties.ValueProperty, CssTextShadowProperties.Value(style.Parent.Values));
            return true;
        }
        if (name == "text-decoration-color")
        {
            // currentColor remains symbolic on this property when inherited.
            sink.Set(CssTextDecorationProperties.ColorProperty, style.Parent.Values.GetValue(CssTextDecorationProperties.ColorProperty));
            InheritColorFormula(style, name);
            return true;
        }
        if (name == "text-emphasis-color")
        {
            sink.Set(CssTextEmphasis.ColorProperty, style.Parent.Values.GetValue(CssTextEmphasis.ColorProperty));
            sink.Set(CssTextEmphasis.ColorExpressionProperty, style.Parent.Values.GetValue(CssTextEmphasis.ColorExpressionProperty));
            InheritColorFormula(style, name);
            return true;
        }
        if (name == "fill")
        {
            sink.Set(CssHighlightRegistry.FillProperty, style.Parent.Values.GetValue(CssHighlightRegistry.FillProperty));
            InheritColorFormula(style, name);
            return true;
        }
        if (name == "stroke")
        {
            sink.Set(CssHighlightRegistry.StrokeProperty, style.Parent.Values.GetValue(CssHighlightRegistry.StrokeProperty));
            InheritColorFormula(style, name);
            return true;
        }
        if (name == "stroke-width")
        {
            sink.Set(CssHighlightRegistry.StrokeWidthProperty, style.Parent.Values.GetValue(CssHighlightRegistry.StrokeWidthProperty));
            return true;
        }
        return false;
    }
}
