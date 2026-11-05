using Jalium.UI.Controls;
using Jalium.UI.Documents;

namespace Jalium.UI.Styling;

internal sealed record CssListStyleType(string? Reference = "disc", string? Literal = null, bool None = false);

/// <summary>List declarations and marker layout reuse the generated-content and counter trees.</summary>
internal static class CssListProperties
{
    internal static readonly DependencyProperty TypeProperty = DependencyProperty.RegisterAttached(
        "CssListStyleType", typeof(CssListStyleType), typeof(CssListProperties),
        new FrameworkPropertyMetadata(new CssListStyleType(), FrameworkPropertyMetadataOptions.Inherits, Changed));
    internal static readonly DependencyProperty InsideProperty = DependencyProperty.RegisterAttached(
        "CssListStyleInside", typeof(bool), typeof(CssListProperties),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, Changed));
    internal static readonly DependencyProperty ImageProperty = DependencyProperty.RegisterAttached(
        "CssListStyleImage", typeof(object), typeof(CssListProperties),
        new FrameworkPropertyMetadata("none", FrameworkPropertyMetadataOptions.Inherits, Changed));
    internal static readonly DependencyProperty MarkerSideMatchParentProperty = DependencyProperty.RegisterAttached(
        "CssMarkerSideMatchParent", typeof(bool), typeof(CssListProperties),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, Changed));
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        CssCounters.Invalidate(CssNode.Get(target));
        if (target is UIElement element) { element.InvalidateMeasure(); element.InvalidateVisual(); }
    }

    internal static FlowDirection MarkerDirection(DependencyObject origin)
    {
        var source = (bool)origin.GetValue(MarkerSideMatchParentProperty)!
            ? CssNode.Get(origin).FrameworkParent?.Target ?? origin : origin;
        return (FlowDirection)source.GetValue(FrameworkElement.FlowDirectionProperty)!;
    }

    internal static bool IsItem(CssNode node) => node.Target.GetEffectiveValueLayer(CssDisplayProperties.SpecificationProperty) is null &&
        node.Target is ListItem { List: not null } ||
        node.GetValue(CssDisplayProperties.SpecificationProperty) is CssDisplaySpecification { ListItem: true, None: false };
    internal static bool Inside(DependencyObject origin) => (bool)origin.GetValue(InsideProperty)! ||
        CssDisplayProperties.Computed(CssNode.Get(origin)) is { ListItem: true, Inline: true, Mode: CssDisplayMode.Block };

    internal static CssRule MarkerDefaults { get; } = new()
    {
        Selectors = [], RuleIndex = -1,
        // Markers isolate their contents by default; author rules can override this value.
        Declarations = CssParser.ParseInlineDeclarations("white-space: pre; text-transform: none; font-variant-numeric: tabular-nums; unicode-bidi: isolate;", null).ToArray(),
    };

    internal static bool AppliesToMarker(string name) => name.StartsWith("--", StringComparison.Ordinal) ||
        name.StartsWith("font-", StringComparison.Ordinal) || name.StartsWith("hyphenate-", StringComparison.Ordinal) ||
        name.StartsWith("animation", StringComparison.Ordinal) || name.StartsWith("transition", StringComparison.Ordinal) ||
        name is "color" or "content" or "direction" or "unicode-bidi" or "line-height" or "text-combine-upright"
            or "initial-letter" or "initial-letter-align" or "initial-letter-wrap"
            or "text-transform" or "text-autospace" or "text-shadow" or "text-wrap-mode" or "text-wrap-style"
            or "text-underline-offset" or "text-underline-position" or "text-decoration-skip-inset"
            or "white-space-collapse" or "word-spacing" or "word-space-transform" or "letter-spacing"
            or "word-break" or "line-break" or "overflow-wrap" or "tab-size" or "hyphens" or "hanging-punctuation";

    internal static void Register()
    {
        CssPropertyRegistry.Register(new()
        {
            Name = "list-style-type", Kind = CssPropertyKind.Longhand, StorageProperty = TypeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => ReadType(ref reader, out var type) && reader.AtEnd
                ? new CssImmediateValue(TypeProperty, type) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "list-style-position", Kind = CssPropertyKind.Longhand, StorageProperty = InsideProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => ReadPosition(ref reader, out var inside) && reader.AtEnd
                ? new CssImmediateValue(InsideProperty, inside) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "list-style-image", Kind = CssPropertyKind.Longhand, StorageProperty = ImageProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext context) => ReadImage(ref reader, context, out var image) && reader.AtEnd
                ? new CssImmediateValue(ImageProperty, image) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "marker-side", Kind = CssPropertyKind.Longhand, StorageProperty = MarkerSideMatchParentProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                reader.TryReadIdent(out var ident) && reader.AtEnd &&
                (ident.Equals("match-self", StringComparison.OrdinalIgnoreCase) || ident.Equals("match-parent", StringComparison.OrdinalIgnoreCase))
                ? new CssImmediateValue(MarkerSideMatchParentProperty, ident.Equals("match-parent", StringComparison.OrdinalIgnoreCase)) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "list-style", Kind = CssPropertyKind.Shorthand,
            Expand = static (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
            {
                CssListStyleType? type = null; bool? inside = null; object? image = null; var none = 0; var count = 0;
                while (!reader.AtEnd)
                {
                    count++;
                    var probe = reader;
                    if (ReadNone(ref probe)) { reader = probe; if (++none > 2) return false; continue; }
                    probe = reader;
                    if (ReadPosition(ref probe, out var position))
                    { if (inside is not null) return false; inside = position; reader = probe; continue; }
                    probe = reader;
                    if (CssImageValue.TryRead(ref probe, context, out var parsedImage))
                    { if (image is not null) return false; image = parsedImage; reader = probe; continue; }
                    if (type is not null || !ReadType(ref reader, out type)) return false;
                }
                if (count == 0 || none > (type is null ? 1 : 0) + (image is null ? 1 : 0)) return false;
                type ??= none > 0 ? new(None: true) : new();
                output.Add(new("list-style-type", new CssImmediateValue(TypeProperty, type), false));
                output.Add(new("list-style-position", new CssImmediateValue(InsideProperty, inside ?? false), false));
                output.Add(new("list-style-image", new CssImmediateValue(ImageProperty, image ?? "none"), false));
                return true;
            },
        });
    }

    private static bool ReadImage(ref CssTokenReader reader, CssCompileContext context, out object? image)
    {
        if (ReadNone(ref reader)) { image = "none"; return true; }
        if (CssImageValue.TryRead(ref reader, context, out var parsed)) { image = parsed; return true; }
        image = null; return false;
    }

    private static bool ReadType(ref CssTokenReader reader, out CssListStyleType type)
    {
        type = new();
        if (ReadNone(ref reader)) { type = new(None: true); return true; }
        if (reader.TryReadString(out var literal)) { type = new(Literal: literal); return true; }
        if (!CssCounterStyles.ReadReference(ref reader, out var reference)) return false;
        type = new(reference); return true;
    }
    private static bool ReadNone(ref CssTokenReader reader)
    {
        var probe = reader;
        if (!probe.TryReadIdent(out var name) || !name.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
        reader = probe; return true;
    }
    private static bool ReadPosition(ref CssTokenReader reader, out bool inside)
    {
        inside = false; var probe = reader;
        if (!probe.TryReadIdent(out var name) || !(name.Equals("inside", StringComparison.OrdinalIgnoreCase) || name.Equals("outside", StringComparison.OrdinalIgnoreCase))) return false;
        inside = name.Equals("inside", StringComparison.OrdinalIgnoreCase); reader = probe; return true;
    }

    internal static string? DefaultText(CssGeneratedText marker, CssCounterStyleMap styles, int value)
        => DefaultMarker(marker, styles, value, out _, out _);

    internal static string? DefaultMarker(CssGeneratedText marker, CssCounterStyleMap styles, int value,
        out CssResolvedContent[]? symbolParts, out string? spokenText)
    {
        symbolParts = null;
        spokenText = null;
        if (marker.Origin.Target is ListItem { List: { } list } && !HasAuthoredType(marker.Origin))
        {
            var native = list.MarkerStyle switch
            {
                TextMarkerStyle.None => null, TextMarkerStyle.Decimal => "decimal", TextMarkerStyle.LowerLatin => "lower-alpha",
                TextMarkerStyle.UpperLatin => "upper-alpha", TextMarkerStyle.LowerRoman => "lower-roman",
                TextMarkerStyle.UpperRoman => "upper-roman", TextMarkerStyle.Circle => "circle", TextMarkerStyle.Square => "square",
                _ => "disc",
            };
            if (native is null) return null;
            spokenText = styles.FormatSpoken(value, native, includeAffixes: true);
            return styles.FormatWithImages(value, native, includeAffixes: true, out symbolParts);
        }
        var type = (CssListStyleType)marker.Origin.GetValue(TypeProperty)!;
        if (type.None) return null;
        if (type.Literal is not null) return type.Literal;
        spokenText = styles.FormatSpoken(value, type.Reference!, includeAffixes: true);
        return styles.FormatWithImages(value, type.Reference!, includeAffixes: true, out symbolParts);
    }

    private static bool HasAuthoredType(CssNode node)
    {
        for (var current = node; current is not null; current = current.FrameworkParent)
            if (current.Target.GetEffectiveValueLayer(TypeProperty) is not null) return true;
        return false;
    }

    internal static void MeasureOutside(FrameworkElement owner, ref Size desired)
    {
        if (CssGeneratedContent.Get(owner)?.Marker is not { Visibility: not Visibility.Collapsed } marker || Inside(owner)) return;
        var direction = MarkerDirection(marker.Origin.Target);
        marker.SetLayerValue(FrameworkElement.FlowDirectionProperty, direction, DependencyObject.LayerValueSource.StyleSetter);
        marker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        desired = new(desired.Width, Math.Max(desired.Height, marker.DesiredSize.Height));
    }
    internal static void ArrangeOutside(FrameworkElement owner, Size size)
    {
        if (CssGeneratedContent.Get(owner)?.Marker is not { Visibility: not Visibility.Collapsed } marker || Inside(owner)) return;
        var direction = MarkerDirection(marker.Origin.Target);
        marker.SetLayerValue(FrameworkElement.FlowDirectionProperty, direction, DependencyObject.LayerValueSource.StyleSetter);
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? size.Width);
        var baseline = CssDisplayLayout.TryGetBaseline(owner, size.Height, false, out var flowBaseline)
            ? flowBaseline : insets.Top + CssFlowProperties.LineMetrics(owner).Baseline;
        var markerBaseline = CssDisplayLayout.TryGetBaseline(marker, marker.DesiredSize.Height, last: true, out var measuredBaseline)
            ? measuredBaseline : CssFlowProperties.LineMetrics(marker).Baseline;
        var x = direction == FlowDirection.RightToLeft ? size.Width - insets.Right : insets.Left - marker.DesiredSize.Width;
        if (marker.Origin.Target is ListItem { List: { MarkerOffset: var offset } } && double.IsFinite(offset))
            x += direction == FlowDirection.RightToLeft ? offset : -offset;
        marker.Arrange(new Rect(x, baseline - markerBaseline, marker.DesiredSize.Width, marker.DesiredSize.Height));
    }
}
