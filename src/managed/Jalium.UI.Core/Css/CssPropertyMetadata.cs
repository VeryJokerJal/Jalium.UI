using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>CSS defaults and shorthand identity, independent of native property storage.</summary>
internal static class CssPropertyMetadata
{
    private static readonly Dictionary<string, string[]> s_shorthands = new(StringComparer.Ordinal)
    {
        ["margin"] = ["margin-top", "margin-right", "margin-bottom", "margin-left"],
        ["padding"] = ["padding-top", "padding-right", "padding-bottom", "padding-left"],
        ["border-width"] = ["border-top-width", "border-right-width", "border-bottom-width", "border-left-width"],
        ["border-radius"] = ["border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius"],
        ["border"] = ["border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "border-color", "border-style"],
        ["background"] = ["background-color", "background-image"],
        ["font"] = ["font-style", "font-weight", "font-size", "line-height", "font-family"],
        ["outline"] = ["outline-width", "outline-style", "outline-color"],
        ["inset"] = ["top", "right", "bottom", "left"],
        ["flex"] = ["flex-grow", "flex-shrink", "flex-basis"],
        ["flex-flow"] = ["flex-direction", "flex-wrap"],
        ["transition"] = ["transition-property", "transition-duration", "transition-delay", "transition-timing-function"],
        ["grid-row"] = ["grid-row-start", "grid-row-end"],
        ["grid-column"] = ["grid-column-start", "grid-column-end"],
        ["grid-area"] = ["grid-row-start", "grid-column-start", "grid-row-end", "grid-column-end"],
        ["grid-template"] = ["grid-template-rows", "grid-template-columns", "grid-template-areas"],
        ["grid"] = ["grid-template-rows", "grid-template-columns", "grid-template-areas", "grid-auto-rows", "grid-auto-columns", "grid-auto-flow"],
        ["place-items"] = ["align-items", "justify-items"],
        ["place-self"] = ["align-self", "justify-self"],
        ["place-content"] = ["align-content", "justify-content"],
        ["gap"] = ["row-gap", "column-gap"],
        ["container"] = ["container-name", "container-type"],
    };

    private static readonly Dictionary<string, string> s_initial = new(StringComparer.Ordinal)
    {
        ["width"] = "auto", ["height"] = "auto", ["min-width"] = "auto", ["min-height"] = "auto",
        ["max-width"] = "none", ["max-height"] = "none", ["opacity"] = "1", ["visibility"] = "visible",
        ["display"] = "inline", ["position"] = "static", ["box-sizing"] = "content-box", ["aspect-ratio"] = "auto",
        ["color"] = "black", ["background-color"] = "transparent", ["background-image"] = "none",
        ["background-size"] = "auto", ["border-color"] = "currentcolor", ["border-style"] = "none",
        ["font-family"] = "sans-serif", ["font-size"] = "medium", ["font-weight"] = "normal",
        ["font-style"] = "normal", ["font-stretch"] = "normal", ["line-height"] = "normal",
        ["text-align"] = "start", ["white-space"] = "normal", ["text-overflow"] = "clip",
        ["text-decoration"] = "none", ["text-decoration-line"] = "none",
        ["box-shadow"] = "none", ["filter"] = "none", ["transform"] = "none", ["transform-origin"] = "50% 50%",
        ["outline-width"] = "medium", ["outline-style"] = "none", ["outline-color"] = "currentcolor", ["outline-offset"] = "0",
        ["cursor"] = "auto", ["pointer-events"] = "auto", ["overflow"] = "visible", ["overflow-x"] = "visible", ["overflow-y"] = "visible",
        ["flex-grow"] = "0", ["flex-shrink"] = "1", ["flex-basis"] = "auto", ["flex-direction"] = "row", ["flex-wrap"] = "nowrap",
        ["align-items"] = "stretch", ["align-self"] = "auto", ["align-content"] = "stretch", ["justify-content"] = "flex-start",
        ["order"] = "0", ["gap"] = "normal", ["row-gap"] = "normal", ["column-gap"] = "normal", ["z-index"] = "0",
        ["transition-property"] = "all", ["transition-duration"] = "0s", ["transition-timing-function"] = "ease",
        ["transition-delay"] = "0s",
        ["grid-template-columns"] = "none", ["grid-template-rows"] = "none", ["grid-template-areas"] = "none",
        ["grid-auto-columns"] = "auto", ["grid-auto-rows"] = "auto", ["grid-auto-flow"] = "row",
        ["grid-row-start"] = "auto", ["grid-row-end"] = "auto", ["grid-column-start"] = "auto", ["grid-column-end"] = "auto",
        ["justify-items"] = "normal", ["justify-self"] = "auto",
        ["vertical-align"] = "baseline",
        ["float"] = "none", ["clear"] = "none",
        ["container-type"] = "normal", ["container-name"] = "none",
    };

    public static IEnumerable<string> Longhands(string property)
        => property == "all" ? s_initial.Keys.Concat(s_shorthands.Values.SelectMany(names => names)).Distinct(StringComparer.Ordinal)
            : s_shorthands.TryGetValue(property, out var names) ? names : [property];

    public static bool IsInherited(string name) => name is "color" or "font-family" or "font-size" or "font-weight"
        or "font-style" or "font-stretch" or "line-height" or "text-align" or "white-space" or "visibility" or "cursor";

    public static bool IsWideKeyword(string value)
        => WideKeyword(value) is not null;

    internal static string? WideKeyword(string value)
    {
        var reader = new CssTokenReader(value);
        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
        var keyword = ident.ToString().ToLowerInvariant();
        return keyword is "initial" or "inherit" or "unset" or "revert" or "revert-layer" ? keyword : null;
    }

    public static string? Initial(string name)
    {
        if (s_initial.TryGetValue(name, out var value)) return value;
        if (name.StartsWith("margin-", StringComparison.Ordinal) || name.StartsWith("padding-", StringComparison.Ordinal) ||
            name.EndsWith("-radius", StringComparison.Ordinal)) return "0";
        if (name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-width", StringComparison.Ordinal)) return "medium";
        return null;
    }
}

// With no named layer, reverting the author origin removes its contribution and
// reveals the native host's inherited/theme/default value.
internal sealed class CssRevertValue(bool layer = false) : CssCompiledValue
{
    public bool Layer { get; } = layer;
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink) => true;
}

internal sealed class CssWideValue(string name, string keyword) : CssCompiledValue
{
    private CssCompiledDeclaration[]? _initialDeclarations;
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var inherit = keyword.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
            !keyword.Equals("initial", StringComparison.OrdinalIgnoreCase) && CssPropertyMetadata.IsInherited(name);
        var parent = CssMatcher.CssAncestor(context.Element);
        if (inherit && parent is not null)
        {
            if (name == "line-height")
                return new CssComputedLineHeightValue(CssComputedLineHeightValue.Inherited(parent)).TryApply(context, sink);
            if (name == "white-space")
            {
                var property = Jalium.UI.Controls.TextBlock.TextWrappingProperty;
                var wrapping = parent.Target.GetValueSourceInternal(property).BaseValueSource == BaseValueSource.Default
                    ? TextWrapping.Wrap : (TextWrapping)parent.GetValue(property)!;
                return new CssNamedValue(name, "TextWrapping", wrapping).TryApply(context, sink);
            }
            if (name == "text-align" && parent.Target is FrameworkElement textParent)
                return new CssFlowTextAlignmentValue(CssFlowProperties.TextAlignment(textParent)).TryApply(context, sink);
            if (name == "display" && parent.GetValue(CssDisplayProperties.SpecificationProperty) is CssDisplaySpecification display)
                return new CssDisplayValue(display).TryApply(context, sink);
            if (name == "visibility")
            {
                var visibility = parent.HasLocalOrAnimatedValue(UIElement.VisibilityProperty)
                    ? parent.GetValue(UIElement.VisibilityProperty) : parent.GetValue(CssDisplayProperties.VisibilityProperty);
                return new CssVisibilityValue((Visibility)visibility!).TryApply(context, sink);
            }
            if (CssPropertyRegistry.Lookup(name)?.StorageProperty is { } storage)
            {
                sink.Set(storage, parent.GetValue(storage));
                if (parent.GetValue(storage) is CssGridLine line)
                    (context.Slots.GridPlacement ??= new()).Set(name, line, context.Slots.CurrentContributionIsState);
                if (name is "row-gap" or "column-gap" && parent.GetValue(storage) is CssLayoutLength gap)
                    (context.Slots.Gaps ??= new()).Set(name == "row-gap", gap, context.Slots.CurrentContributionIsState);
                return true;
            }
            if (CssTransitions.InheritedPart(parent, name) is { } transition)
                return new CssTransitionPartValue(name, transition).TryApply(in context, sink);
            if (TryInheritEdge(parent, in context)) return true;
            var dpName = name switch
            {
                "color" => "Foreground", "background-color" or "background-image" => "Background",
                "border-color" => "BorderBrush", "transform" => "RenderTransform", "transform-origin" => "RenderTransformOrigin",
                "outline-color" => "OutlineBrush", "outline-width" => "OutlineThickness", _ => null,
            };
            if (dpName is null) CssKebabCase.TryToPascal(name, out dpName);
            var dp = dpName is null ? null : CssDependencyPropertyLookup.Find(context.Element.GetType(), dpName);
            var parentDp = dpName is null ? null : CssDependencyPropertyLookup.Find(parent.GetType(), dpName);
            if (dp is not null && parentDp is not null)
            {
                sink.Set(dp, parent.GetValue(parentDp));
                return true;
            }
        }
        var initial = CssPropertyMetadata.Initial(name);
        if (initial is null) return false;
        var declarations = _initialDeclarations ??= CssEngine.CompileDeclarations(
            [new CssDeclaration { PropertyName = name, RawValue = initial }]);
        foreach (var declaration in declarations)
        {
            if (declaration.Value is not CssWideValue) declaration.Value.TryApply(in context, sink);
        }
        return true;
    }

    private bool TryInheritEdge(CssNode parent, in CssApplyContext context)
    {
        var slot = name.StartsWith("margin-", StringComparison.Ordinal) ? CssSlot.Margin
            : name.StartsWith("padding-", StringComparison.Ordinal) ? CssSlot.Padding
            : name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-width", StringComparison.Ordinal) ? CssSlot.BorderWidth : CssSlot.None;
        if (slot == CssSlot.None) return false;
        var dpName = slot == CssSlot.Margin ? "Margin" : slot == CssSlot.Padding ? "Padding" : "BorderThickness";
        var dp = CssDependencyPropertyLookup.Find(parent.GetType(), dpName);
        if (dp is null || parent.GetValue(dp) is not Thickness thickness) return false;
        var edge = name.Contains("left", StringComparison.Ordinal) ? 0 : name.Contains("top", StringComparison.Ordinal) ? 1
            : name.Contains("right", StringComparison.Ordinal) ? 2 : 3;
        var value = edge switch { 0 => thickness.Left, 1 => thickness.Top, 2 => thickness.Right, _ => thickness.Bottom };
        context.Slots.SetThicknessEdge(slot, edge, new CssLength(value, CssUnit.Px));
        return true;
    }
}
