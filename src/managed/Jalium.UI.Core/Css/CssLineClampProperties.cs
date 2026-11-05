using Jalium.UI.Controls;
using Jalium.UI.Documents;

namespace Jalium.UI.Styling;

internal enum CssContinue { Normal, Collapse, Discard, WebkitLegacy }
internal enum CssBlockEllipsisKind { None, Ellipsis, String }
internal readonly record struct CssBlockEllipsis(CssBlockEllipsisKind Kind, string? Text = null)
{
    internal string Value => Kind == CssBlockEllipsisKind.Ellipsis ? "…" : Text ?? "";
    internal bool HasContent => Kind != CssBlockEllipsisKind.None && Value.Length > 0;
}
internal readonly record struct CssMaxLines(int Count, bool Auto);
internal enum CssLegacyBoxOrient { Horizontal, Vertical, InlineAxis, BlockAxis }

internal static class CssLineClampProperties
{
    internal static readonly DependencyProperty MaxLinesProperty = DependencyProperty.RegisterAttached(
        "CssMaxLines", typeof(CssMaxLines), typeof(CssLineClampProperties), new PropertyMetadata(new CssMaxLines(0, true), Changed));
    internal static readonly DependencyProperty ContinueProperty = DependencyProperty.RegisterAttached(
        "CssContinue", typeof(CssContinue), typeof(CssLineClampProperties), new PropertyMetadata(CssContinue.Normal, Changed));
    internal static readonly DependencyProperty EllipsisProperty = DependencyProperty.RegisterAttached(
        "CssBlockEllipsis", typeof(CssBlockEllipsis), typeof(CssLineClampProperties),
        new PropertyMetadata(default(CssBlockEllipsis), Changed, null, inherits: true));
    internal static readonly DependencyProperty OrientProperty = DependencyProperty.RegisterAttached(
        "CssWebkitBoxOrient", typeof(CssLegacyBoxOrient), typeof(CssLineClampProperties),
        new PropertyMetadata(CssLegacyBoxOrient.Horizontal, Changed));

    internal static void Register()
    {
        CssPropertyRegistry.Register(new() { Name = "max-lines", Kind = CssPropertyKind.Longhand, StorageProperty = MaxLinesProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                ReadMaxLines(ref reader) is { } value && reader.AtEnd ? new CssImmediateValue(MaxLinesProperty, value) : null });
        CssPropertyRegistry.Register(new() { Name = "block-ellipsis", Kind = CssPropertyKind.Longhand, StorageProperty = EllipsisProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                ReadEllipsis(ref reader) is { } value && reader.AtEnd ? new CssImmediateValue(EllipsisProperty, value) : null });
        CssPropertyRegistry.Register(new() { Name = "continue", Kind = CssPropertyKind.Longhand, StorageProperty = ContinueProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var token) || !reader.AtEnd) return null;
                CssContinue? value = token.ToString().ToLowerInvariant() switch
                { "normal" => CssContinue.Normal, "collapse" => CssContinue.Collapse, "discard" => CssContinue.Discard,
                  "-webkit-legacy" => CssContinue.WebkitLegacy, _ => null };
                return value is { } item ? new CssImmediateValue(ContinueProperty, item) : null;
            } });
        CssPropertyRegistry.Register(new() { Name = "-webkit-box-orient", Kind = CssPropertyKind.Longhand, StorageProperty = OrientProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var token) || !reader.AtEnd) return null;
                CssLegacyBoxOrient? value = token.ToString().ToLowerInvariant() switch
                { "horizontal" => CssLegacyBoxOrient.Horizontal, "vertical" => CssLegacyBoxOrient.Vertical,
                  "inline-axis" => CssLegacyBoxOrient.InlineAxis, "block-axis" => CssLegacyBoxOrient.BlockAxis, _ => null };
                return value is { } item ? new CssImmediateValue(OrientProperty, item) : null;
            } });
        CssPropertyRegistry.Register(new() { Name = "line-clamp", Kind = CssPropertyKind.Shorthand,
            Expand = static (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
                Expand(ref reader, output, false) });
        CssPropertyRegistry.Register(new() { Name = "-webkit-line-clamp", Kind = CssPropertyKind.Shorthand,
            Expand = static (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
                Expand(ref reader, output, true) });
    }

    private static CssMaxLines? ReadMaxLines(ref CssTokenReader reader)
    {
        var count = 0; var automatic = false; var read = false;
        for (var i = 0; i < 2; i++)
        {
            var probe = reader;
            if (!automatic && probe.TryReadIdent(out var token) && token.Equals("auto", StringComparison.OrdinalIgnoreCase))
            { reader = probe; automatic = true; read = true; continue; }
            probe = reader;
            if (count == 0 && probe.TryReadInteger(out var number, minimum: 1))
            { reader = probe; count = number; read = true; continue; }
            break;
        }
        return read ? new(count, automatic) : null;
    }

    private static CssBlockEllipsis? ReadEllipsis(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadString(out var text)) { reader = probe; return new(CssBlockEllipsisKind.String, text); }
        probe = reader;
        if (!probe.TryReadIdent(out var token)) return null;
        CssBlockEllipsisKind? kind = token.ToString().ToLowerInvariant() switch
        { "no-ellipsis" => CssBlockEllipsisKind.None, "ellipsis" => CssBlockEllipsisKind.Ellipsis, _ => null };
        if (kind is null) return null;
        reader = probe; return new(kind.Value);
    }

    private static bool Expand(ref CssTokenReader reader, List<CssCompiledDeclaration> output, bool legacy)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var none) && none.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        {
            reader = probe;
            Add(output, new(0, true), legacy ? new(CssBlockEllipsisKind.Ellipsis) : default, CssContinue.Normal);
            return true;
        }
        if (legacy)
        {
            if (!reader.TryReadInteger(out var number, minimum: 1) || !reader.AtEnd) return false;
            Add(output, new(number, false), new(CssBlockEllipsisKind.Ellipsis), CssContinue.WebkitLegacy); return true;
        }
        CssMaxLines? lines = null; CssBlockEllipsis? ellipsis = null; var webkit = false;
        for (var i = 0; !reader.AtEnd && i < 4; i++)
        {
            probe = reader;
            if (!webkit && probe.TryReadIdent(out var token) && token.Equals("-webkit-legacy", StringComparison.OrdinalIgnoreCase))
            { if (!probe.AtEnd || lines is null && ellipsis is null) return false; webkit = true; reader = probe; continue; }
            probe = reader;
            if (lines is null && ReadMaxLines(ref probe) is { } maximum) { lines = maximum; reader = probe; continue; }
            probe = reader;
            if (ellipsis is null && ReadEllipsis(ref probe) is { } marker) { ellipsis = marker; reader = probe; continue; }
            // max-lines is itself an unordered pair. A block-ellipsis token may
            // separate its two components in the enclosing unordered shorthand.
            if (lines is { } prior)
            {
                probe = reader;
                if (ReadMaxLines(ref probe) is { } extra && (!prior.Auto || !extra.Auto) &&
                    (prior.Count == 0 || extra.Count == 0))
                { lines = new(prior.Count + extra.Count, prior.Auto || extra.Auto); reader = probe; continue; }
            }
            return false;
        }
        if (!reader.AtEnd || lines is null && ellipsis is null) return false;
        Add(output, lines ?? new(0, true), ellipsis ?? new(CssBlockEllipsisKind.Ellipsis),
            webkit ? CssContinue.WebkitLegacy : CssContinue.Collapse); return true;
    }

    private static void Add(List<CssCompiledDeclaration> output, CssMaxLines lines, CssBlockEllipsis marker, CssContinue continuation)
    {
        output.Add(new("max-lines", new CssImmediateValue(MaxLinesProperty, lines), false));
        output.Add(new("block-ellipsis", new CssImmediateValue(EllipsisProperty, marker), false));
        output.Add(new("continue", new CssImmediateValue(ContinueProperty, continuation), false));
    }

    internal static bool LegacyVertical(DependencyObject element) =>
        element.GetValue(CssDisplayProperties.SpecificationProperty) is CssDisplaySpecification { LegacyBox: true } &&
        element.GetValue(OrientProperty) is CssLegacyBoxOrient.Vertical;

    internal static CssDisplayMode UsedMode(FrameworkElement element, CssDisplayMode mode) => mode == CssDisplayMode.Flex &&
        LegacyVertical(element) && element.GetValue(ContinueProperty) is not CssContinue.Normal ? CssDisplayMode.FlowRoot : mode;

    internal static void RefreshMode(FrameworkElement element)
    {
        var mode = UsedMode(element, (CssDisplayMode)element.GetValue(CssDisplayLayout.ModeProperty)!);
        if (element.CssDisplayMode == mode) return;
        element.CssDisplayMode = mode; CssTableLayout.DisplayChanged(element);
        element.InvalidateMeasure();
        if (element is Panel panel) panel.InvalidateCssFlowOrder();
    }

    internal static bool Active(FrameworkElement element) => element.IsCssContentHost &&
        (CssContentsLayout.IsRoot(element) || element.CssDisplayMode is CssDisplayMode.Native or CssDisplayMode.Block or CssDisplayMode.FlowRoot or CssDisplayMode.TableCell or CssDisplayMode.TableCaption) &&
        (element.GetValue(ContinueProperty) is CssContinue.Discard ||
         !CssMultiColumnProperties.Active(element) && (element.GetValue(ContinueProperty) is CssContinue.Collapse ||
             element.GetValue(ContinueProperty) is CssContinue.WebkitLegacy && LegacyVertical(element)));

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is TextElement text) { text.NotifyTextContentChanged(); return; }
        if (target is FlowDocument document) { document.NotifyPaginationStyleChanged(); return; }
        if (target is not FrameworkElement element) return;
        RefreshMode(element);
        CssLineClampLayout.PropertiesChanged(element);
        CssFloatProperties.Changed(target, _);
    }
}
