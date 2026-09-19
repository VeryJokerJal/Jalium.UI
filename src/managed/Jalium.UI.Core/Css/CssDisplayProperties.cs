namespace Jalium.UI.Styling;

internal readonly record struct CssDisplaySpecification(CssDisplayMode Mode, bool Inline = false, bool None = false);

internal static class CssDisplayProperties
{
    internal static readonly DependencyProperty SpecificationProperty = DependencyProperty.RegisterAttached(
        "CssDisplay", typeof(CssDisplaySpecification), typeof(CssDisplayProperties), new PropertyMetadata(default(CssDisplaySpecification)));
    internal static readonly DependencyProperty VisibilityProperty = DependencyProperty.RegisterAttached(
        "CssVisibility", typeof(Visibility), typeof(CssDisplayProperties), new PropertyMetadata(Visibility.Visible));

    internal static void Register() => CssPropertyRegistry.Register(new()
    {
        Name = "display", Kind = CssPropertyKind.Longhand, StorageProperty = SpecificationProperty,
        Parse = (ref CssTokenReader reader, CssCompileContext _) =>
        {
            if (!reader.TryReadIdent(out var first)) return null;
            var token = first.ToString().ToLowerInvariant();
            if (reader.AtEnd)
            {
                CssDisplaySpecification? display = token switch
                {
                    "none" => new(CssDisplayMode.Native, None: true),
                    "block" => new(CssDisplayMode.Block), "flow-root" => new(CssDisplayMode.FlowRoot),
                    "inline" => new(CssDisplayMode.Native, true), "inline-block" => new(CssDisplayMode.FlowRoot, true),
                    "flex" => new(CssDisplayMode.Flex), "inline-flex" => new(CssDisplayMode.Flex, true),
                    "grid" => new(CssDisplayMode.Grid), "inline-grid" => new(CssDisplayMode.Grid, true),
                    _ => null,
                };
                return display is { } value ? new CssDisplayValue(value) : null;
            }
            if (!reader.TryReadIdent(out var second) || !reader.AtEnd) return null;
            var inside = second.ToString().ToLowerInvariant();
            if (inside is "block" or "inline") (token, inside) = (inside, token);
            if (token is not ("block" or "inline")) return null;
            var inline = token == "inline";
            CssDisplayMode? mode = inside switch
            {
                "flow" => inline ? CssDisplayMode.Native : CssDisplayMode.Block,
                "flow-root" => CssDisplayMode.FlowRoot, "flex" => CssDisplayMode.Flex, "grid" => CssDisplayMode.Grid, _ => null,
            };
            return mode is { } parsed ? new CssDisplayValue(new(parsed, inline)) : null;
        },
    });
}

internal sealed class CssDisplayValue(CssDisplaySpecification display) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssDisplayProperties.SpecificationProperty, display);
        sink.Set(CssDisplayLayout.ModeProperty, display.Mode);
        sink.Set(CssDisplayLayout.InlineProperty, display.Inline);
        var visibility = context.Slots.Visibility ??= new();
        visibility.DisplayNone = display.None;
        visibility.FromState |= context.Slots.CurrentContributionIsState;
        return true;
    }
}

internal sealed class CssVisibilityValue(Visibility value) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssDisplayProperties.VisibilityProperty, value);
        var visibility = context.Slots.Visibility ??= new();
        visibility.Value = value;
        visibility.FromState |= context.Slots.CurrentContributionIsState;
        return true;
    }
}

internal sealed class CssVisibilityParts
{
    public bool? DisplayNone;
    public Visibility? Value;
    public bool FromState;
    internal void Flush(ICssSetterSink sink)
    {
        sink.CurrentValueIsState = FromState;
        sink.Set(UIElement.VisibilityProperty, DisplayNone == true ? Visibility.Collapsed : Value ?? Visibility.Visible);
    }
}
