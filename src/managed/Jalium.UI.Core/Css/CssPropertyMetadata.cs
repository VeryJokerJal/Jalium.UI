using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>CSS defaults and shorthand identity, independent of native property storage.</summary>
internal static class CssPropertyMetadata
{
    private static readonly Dictionary<string, string[]> s_shorthands = new(StringComparer.Ordinal)
    {
        ["margin"] = ["margin-top", "margin-right", "margin-bottom", "margin-left"],
        ["list-style"] = ["list-style-type", "list-style-position", "list-style-image"],
        ["padding"] = ["padding-top", "padding-right", "padding-bottom", "padding-left"],
        ["margin-inline"] = ["margin-inline-start", "margin-inline-end"],
        ["margin-block"] = ["margin-block-start", "margin-block-end"],
        ["padding-inline"] = ["padding-inline-start", "padding-inline-end"],
        ["padding-block"] = ["padding-block-start", "padding-block-end"],
        ["border-width"] = ["border-top-width", "border-right-width", "border-bottom-width", "border-left-width"],
        ["border-inline-width"] = ["border-inline-start-width", "border-inline-end-width"],
        ["border-block-width"] = ["border-block-start-width", "border-block-end-width"],
        ["border-color"] = ["border-top-color", "border-right-color", "border-bottom-color", "border-left-color"],
        ["border-inline-color"] = ["border-inline-start-color", "border-inline-end-color"],
        ["border-block-color"] = ["border-block-start-color", "border-block-end-color"],
        ["border-style"] = ["border-top-style", "border-right-style", "border-bottom-style", "border-left-style"],
        ["border-inline-style"] = ["border-inline-start-style", "border-inline-end-style"],
        ["border-block-style"] = ["border-block-start-style", "border-block-end-style"],
        ["border-radius"] = ["border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius"],
        ["border"] = ["border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
            "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
            "border-image-source", "border-image-slice", "border-image-width", "border-image-outset", "border-image-repeat"],
        ["border-image"] = ["border-image-source", "border-image-slice", "border-image-width", "border-image-outset", "border-image-repeat"],
        ["border-top"] = ["border-top-width", "border-top-color", "border-top-style"],
        ["border-right"] = ["border-right-width", "border-right-color", "border-right-style"],
        ["border-bottom"] = ["border-bottom-width", "border-bottom-color", "border-bottom-style"],
        ["border-left"] = ["border-left-width", "border-left-color", "border-left-style"],
        ["border-inline-start"] = ["border-inline-start-width", "border-inline-start-color", "border-inline-start-style"],
        ["border-inline-end"] = ["border-inline-end-width", "border-inline-end-color", "border-inline-end-style"],
        ["border-block-start"] = ["border-block-start-width", "border-block-start-color", "border-block-start-style"],
        ["border-block-end"] = ["border-block-end-width", "border-block-end-color", "border-block-end-style"],
        ["border-inline"] = ["border-inline-start-width", "border-inline-start-color", "border-inline-start-style",
            "border-inline-end-width", "border-inline-end-color", "border-inline-end-style"],
        ["border-block"] = ["border-block-start-width", "border-block-start-color", "border-block-start-style",
            "border-block-end-width", "border-block-end-color", "border-block-end-style"],
        ["background"] = ["background-color", "background-image", "background-size",
            "background-position", "background-repeat", "background-origin", "background-clip"],
        ["mask"] = [.. CssCoreProperties.MaskNames, .. CssCoreProperties.MaskBorderNames],
        ["mask-border"] = CssCoreProperties.MaskBorderNames,
        ["overflow"] = ["overflow-x", "overflow-y"],
        ["font-variant"] = CssFontVariant.Longhands,
        ["font"] = ["font-style", "font-weight", "font-stretch", "font-size", "line-height", "font-family", "font-variant-caps", "font-variant-position", "font-variant-emoji", "font-variant-numeric", "font-variant-east-asian", "font-variant-alternates", "font-feature-settings", "font-variation-settings", "font-optical-sizing", "font-size-adjust", "font-kerning", "font-variant-ligatures"],
        ["text-align"] = ["text-align-all", "text-align-last"],
        ["white-space"] = ["white-space-collapse", "text-wrap-mode", "white-space-trim"],
        ["text-wrap"] = ["text-wrap-mode", "text-wrap-style"],
        ["line-clamp"] = ["max-lines", "block-ellipsis", "continue"],
        ["-webkit-line-clamp"] = ["max-lines", "block-ellipsis", "continue"],
        ["text-decoration"] = ["text-decoration-line", "text-decoration-style",
            "text-decoration-color", "text-decoration-thickness"],
        ["text-emphasis"] = ["text-emphasis-style", "text-emphasis-color"],
        ["caret"] = ["caret-color", "caret-animation", "caret-shape"],
        ["outline"] = ["outline-width", "outline-style", "outline-color"],
        ["inset"] = ["top", "right", "bottom", "left"],
        ["inset-inline"] = ["inset-inline-start", "inset-inline-end"],
        ["inset-block"] = ["inset-block-start", "inset-block-end"],
        ["flex"] = ["flex-grow", "flex-shrink", "flex-basis"],
        ["flex-flow"] = ["flex-direction", "flex-wrap"],
        ["transition"] = ["transition-property", "transition-duration", "transition-delay", "transition-timing-function", "transition-behavior"],
        ["animation"] = ["animation-name", "animation-duration", "animation-timing-function", "animation-delay",
            "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state"],
        ["grid-row"] = ["grid-row-start", "grid-row-end"],
        ["grid-column"] = ["grid-column-start", "grid-column-end"],
        ["grid-area"] = ["grid-row-start", "grid-column-start", "grid-row-end", "grid-column-end"],
        ["grid-template"] = ["grid-template-rows", "grid-template-columns", "grid-template-areas"],
        ["grid"] = ["grid-template-rows", "grid-template-columns", "grid-template-areas", "grid-auto-rows", "grid-auto-columns", "grid-auto-flow"],
        ["place-items"] = ["align-items", "justify-items"],
        ["place-self"] = ["align-self", "justify-self"],
        ["place-content"] = ["align-content", "justify-content"],
        ["gap"] = ["row-gap", "column-gap"],
        ["columns"] = ["column-width", "column-count"],
        ["column-rule"] = ["column-rule-width", "column-rule-style", "column-rule-color"],
        ["container"] = ["container-name", "container-type"],
        ["page-break-before"] = ["break-before"],
        ["page-break-after"] = ["break-after"],
        ["page-break-inside"] = ["break-inside"],
        ["position-try"] = ["position-try-order", "position-try-fallbacks"],
        ["scroll-timeline"] = ["scroll-timeline-name", "scroll-timeline-axis"],
        ["view-timeline"] = ["view-timeline-name", "view-timeline-axis", "view-timeline-inset"],
        ["animation-range"] = ["animation-range-start", "animation-range-end"],
        ["offset"] = ["offset-position", "offset-path", "offset-distance", "offset-rotate", "offset-anchor"],
        ["scroll-margin"] = ["scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom", "scroll-margin-left"],
        ["scroll-margin-inline"] = ["scroll-margin-inline-start", "scroll-margin-inline-end"],
        ["scroll-margin-block"] = ["scroll-margin-block-start", "scroll-margin-block-end"],
        ["scroll-padding"] = ["scroll-padding-top", "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left"],
        ["scroll-padding-inline"] = ["scroll-padding-inline-start", "scroll-padding-inline-end"],
        ["scroll-padding-block"] = ["scroll-padding-block-start", "scroll-padding-block-end"],
        ["overscroll-behavior"] = ["overscroll-behavior-x", "overscroll-behavior-y"],
        ["contain-intrinsic-size"] = ["contain-intrinsic-width", "contain-intrinsic-height"],
    };

    private static readonly Dictionary<string, string> s_initial = new(StringComparer.Ordinal)
    {
        ["margin-inline-start"] = "0", ["margin-inline-end"] = "0",
        ["margin-block-start"] = "0", ["margin-block-end"] = "0",
        ["padding-inline-start"] = "0", ["padding-inline-end"] = "0",
        ["padding-block-start"] = "0", ["padding-block-end"] = "0",
        ["border-inline-start-width"] = "medium", ["border-inline-end-width"] = "medium",
        ["border-block-start-width"] = "medium", ["border-block-end-width"] = "medium",
        ["border-inline-start-color"] = "currentcolor", ["border-inline-end-color"] = "currentcolor",
        ["border-block-start-color"] = "currentcolor", ["border-block-end-color"] = "currentcolor",
        ["border-top-color"] = "currentcolor", ["border-right-color"] = "currentcolor",
        ["border-bottom-color"] = "currentcolor", ["border-left-color"] = "currentcolor",
        ["border-inline-start-style"] = "none", ["border-inline-end-style"] = "none",
        ["border-block-start-style"] = "none", ["border-block-end-style"] = "none",
        ["border-top-style"] = "none", ["border-right-style"] = "none",
        ["border-bottom-style"] = "none", ["border-left-style"] = "none",
        ["border-start-start-radius"] = "0", ["border-start-end-radius"] = "0",
        ["border-end-start-radius"] = "0", ["border-end-end-radius"] = "0",
        ["inset-inline-start"] = "auto", ["inset-inline-end"] = "auto",
        ["inset-block-start"] = "auto", ["inset-block-end"] = "auto",
        ["left"] = "auto", ["top"] = "auto", ["right"] = "auto", ["bottom"] = "auto",
        ["inline-size"] = "auto", ["block-size"] = "auto",
        ["min-inline-size"] = "auto", ["min-block-size"] = "auto",
        ["max-inline-size"] = "none", ["max-block-size"] = "none",
        ["width"] = "auto", ["height"] = "auto", ["min-width"] = "auto", ["min-height"] = "auto",
        ["max-width"] = "none", ["max-height"] = "none", ["opacity"] = "1", ["visibility"] = "visible",
        ["interpolate-size"] = "numeric-only",
        ["display"] = "inline", ["position"] = "static", ["box-sizing"] = "content-box", ["aspect-ratio"] = "auto",
        ["column-count"] = "auto", ["column-width"] = "auto", ["column-fill"] = "balance", ["column-span"] = "none",
        ["column-rule-width"] = "medium", ["column-rule-style"] = "none", ["column-rule-color"] = "currentcolor",
        ["content"] = "normal",
        ["break-before"] = "auto", ["break-after"] = "auto", ["break-inside"] = "auto",
        ["orphans"] = "2", ["widows"] = "2",
        ["page"] = "auto", ["size"] = "auto", ["page-orientation"] = "upright",
        ["table-layout"] = "auto", ["border-spacing"] = "0px 0px", ["empty-cells"] = "show", ["caption-side"] = "top", ["border-collapse"] = "separate",
        ["counter-reset"] = "none", ["counter-increment"] = "none", ["counter-set"] = "none", ["quotes"] = "auto",
        ["list-style-type"] = "disc", ["list-style-position"] = "outside", ["list-style-image"] = "none",
        ["color"] = "black", ["background-color"] = "transparent", ["background-image"] = "none",
        ["background-size"] = "auto", ["background-position"] = "0% 0%",
        ["background-repeat"] = "repeat", ["background-origin"] = "padding-box",
        ["background-clip"] = "border-box",
        ["mask-image"] = "none", ["mask-mode"] = "match-source", ["mask-repeat"] = "repeat",
        ["mask-position"] = "0% 0%", ["mask-origin"] = "border-box", ["mask-clip"] = "border-box",
        ["mask-size"] = "auto", ["mask-composite"] = "add",
        ["mask-type"] = "luminance",
        ["mask-border-source"] = "none", ["mask-border-slice"] = "0", ["mask-border-width"] = "auto",
        ["mask-border-outset"] = "0", ["mask-border-repeat"] = "stretch", ["mask-border-mode"] = "alpha",
        ["border-image-source"] = "none", ["border-image-slice"] = "100%", ["border-image-width"] = "1",
        ["border-image-outset"] = "0", ["border-image-repeat"] = "stretch",
        ["object-fit"] = "fill", ["object-position"] = "50% 50%", ["image-rendering"] = "auto",
        ["font-family"] = "sans-serif", ["font-size"] = "medium", ["font-weight"] = "normal",
        ["font-synthesis-small-caps"] = "auto", ["font-style"] = "normal", ["font-stretch"] = "normal", ["line-height"] = "normal", ["font-variant-caps"] = "normal", ["font-variant-position"] = "normal", ["font-variant-emoji"] = "normal", ["font-synthesis-position"] = "auto", ["font-variant-numeric"] = "normal", ["font-variant-east-asian"] = "normal", ["font-variant-alternates"] = "normal", ["font-feature-settings"] = "normal", ["font-variation-settings"] = "normal", ["font-optical-sizing"] = "auto", ["font-size-adjust"] = "none", ["font-kerning"] = "auto", ["font-variant-ligatures"] = "normal",
        ["math-depth"] = "0", ["math-style"] = "normal",
        ["text-align-all"] = "start", ["text-align-last"] = "auto", ["text-justify"] = "auto",
        ["text-group-align"] = "none",
        ["line-padding"] = "0",
        ["text-indent"] = "0", ["overflow-wrap"] = "normal", ["word-break"] = "normal", ["line-break"] = "auto",
        ["word-spacing"] = "normal", ["word-space-transform"] = "none",
        ["letter-spacing"] = "normal", ["text-autospace"] = "normal",
        ["hanging-punctuation"] = "none",
        ["tab-size"] = "8", ["hyphens"] = "manual", ["hyphenate-character"] = "auto",
        ["hyphenate-limit-chars"] = "auto", ["hyphenate-limit-lines"] = "no-limit",
        ["hyphenate-limit-last"] = "none",
        ["hyphenate-limit-zone"] = "0",
        ["caret-color"] = "auto",
        ["caret-animation"] = "auto",
        ["caret-shape"] = "auto",
        ["white-space-collapse"] = "collapse", ["text-wrap-mode"] = "wrap",
        ["text-wrap-style"] = "auto",
        ["line-clamp"] = "none", ["-webkit-line-clamp"] = "none",
        ["max-lines"] = "auto", ["block-ellipsis"] = "no-ellipsis", ["continue"] = "normal",
        ["-webkit-box-orient"] = "horizontal",
        ["white-space-trim"] = "none",
        ["text-transform"] = "none", ["text-overflow"] = "clip",
        ["text-decoration"] = "none", ["text-decoration-line"] = "none",
        ["text-decoration-style"] = "solid", ["text-decoration-color"] = "currentcolor",
        ["text-decoration-thickness"] = "auto",
        ["text-underline-offset"] = "auto",
        ["text-underline-position"] = "auto",
        ["text-decoration-skip-inset"] = "none",
        ["text-decoration-inset"] = "0",
        ["box-decoration-break"] = "slice",
        ["text-shadow"] = "none",
        ["text-emphasis"] = "none", ["text-emphasis-style"] = "none",
        ["text-emphasis-color"] = "currentcolor", ["text-emphasis-position"] = "over right",
        ["box-shadow"] = "none", ["filter"] = "none", ["backdrop-filter"] = "none",
        ["mix-blend-mode"] = "normal", ["background-blend-mode"] = "normal", ["isolation"] = "auto",
        ["transform"] = "none", ["transform-origin"] = "50% 50%", ["transform-box"] = "view-box",
        ["perspective"] = "none", ["perspective-origin"] = "50% 50%", ["transform-style"] = "flat", ["backface-visibility"] = "visible",
        ["outline-width"] = "medium", ["outline-style"] = "none", ["outline-color"] = "auto", ["outline-offset"] = "0",
        ["cursor"] = "auto", ["pointer-events"] = "auto", ["user-select"] = "auto", ["overflow"] = "visible",
        ["overflow-inline"] = "visible", ["overflow-block"] = "visible",
        ["overflow-x"] = "visible", ["overflow-y"] = "visible",
        ["overflow-clip-margin"] = "0px",
        ["scrollbar-width"] = "auto", ["scrollbar-color"] = "auto",
        ["scrollbar-gutter"] = "auto",
        ["clip-path"] = "none",
        ["shape-outside"] = "none", ["shape-margin"] = "0", ["shape-image-threshold"] = "0",
        ["flex-grow"] = "0", ["flex-shrink"] = "1", ["flex-basis"] = "auto", ["flex-direction"] = "row", ["flex-wrap"] = "nowrap",
        ["align-items"] = "stretch", ["align-self"] = "auto", ["align-content"] = "stretch", ["justify-content"] = "flex-start",
        ["order"] = "0", ["gap"] = "normal", ["row-gap"] = "normal", ["column-gap"] = "normal", ["z-index"] = "auto",
        ["transition-property"] = "all", ["transition-duration"] = "0s", ["transition-timing-function"] = "ease",
        ["transition-delay"] = "0s", ["transition-behavior"] = "normal",
        ["animation-name"] = "none", ["animation-duration"] = "0s",
        ["animation-timing-function"] = "ease", ["animation-delay"] = "0s",
        ["animation-iteration-count"] = "1", ["animation-direction"] = "normal",
        ["animation-fill-mode"] = "none", ["animation-play-state"] = "running",
        ["grid-template-columns"] = "none", ["grid-template-rows"] = "none", ["grid-template-areas"] = "none",
        ["grid-auto-columns"] = "auto", ["grid-auto-rows"] = "auto", ["grid-auto-flow"] = "row",
        ["grid-row-start"] = "auto", ["grid-row-end"] = "auto", ["grid-column-start"] = "auto", ["grid-column-end"] = "auto",
        ["justify-items"] = "normal", ["justify-self"] = "auto",
        ["vertical-align"] = "baseline",
        ["initial-letter"] = "normal", ["initial-letter-align"] = "alphabetic", ["initial-letter-wrap"] = "none",
        ["float"] = "none", ["clear"] = "none",
        ["container-type"] = "normal", ["container-name"] = "none",
        ["writing-mode"] = "horizontal-tb", ["text-orientation"] = "mixed", ["text-combine-upright"] = "none",
        ["ruby-position"] = "alternate", ["ruby-align"] = "space-around", ["ruby-merge"] = "separate",
        ["anchor-name"] = "none", ["anchor-scope"] = "none", ["position-anchor"] = "auto",
        ["position-area"] = "none", ["inset-area"] = "none",
        ["position-try-fallbacks"] = "none", ["position-try-order"] = "normal",
        ["position-visibility"] = "anchors-valid",
        ["animation-timeline"] = "auto", ["animation-composition"] = "replace",
        ["scroll-timeline-name"] = "none", ["scroll-timeline-axis"] = "block",
        ["view-timeline-name"] = "none", ["view-timeline-axis"] = "block", ["view-timeline-inset"] = "auto",
        ["timeline-scope"] = "none",
        ["animation-range-start"] = "normal", ["animation-range-end"] = "normal",
        ["offset-path"] = "none", ["offset-distance"] = "0px", ["offset-rotate"] = "auto",
        ["offset-anchor"] = "auto", ["offset-position"] = "normal",
        ["view-transition-name"] = "none", ["view-transition-class"] = "none",
        ["scroll-snap-type"] = "none", ["scroll-snap-align"] = "none", ["scroll-snap-stop"] = "normal",
        ["scroll-behavior"] = "auto", ["overflow-anchor"] = "auto",
        ["overscroll-behavior-x"] = "auto", ["overscroll-behavior-y"] = "auto",
        ["overscroll-behavior-inline"] = "auto", ["overscroll-behavior-block"] = "auto",
        ["contain"] = "none", ["content-visibility"] = "visible",
        ["contain-intrinsic-width"] = "none", ["contain-intrinsic-height"] = "none",
        ["contain-intrinsic-inline-size"] = "none", ["contain-intrinsic-block-size"] = "none",
        ["appearance"] = "none", ["accent-color"] = "auto", ["resize"] = "none", ["touch-action"] = "auto",
        ["color-scheme"] = "normal", ["forced-color-adjust"] = "auto", ["print-color-adjust"] = "economy",
        ["text-size-adjust"] = "auto", ["zoom"] = "1",
        ["vector-effect"] = "none",
    };

    public static IEnumerable<string> Longhands(string property)
        => property == "all" ? s_initial.Keys.Concat(s_shorthands.Values.SelectMany(names => names)).Distinct(StringComparer.Ordinal)
            : s_shorthands.TryGetValue(property, out var names) ? names : [property];

    public static bool IsInherited(string name) => name is "color" or "font-family" or "font-size" or "font-weight"
        or "math-depth" or "math-style" or "orphans" or "widows" or "border-spacing" or "empty-cells" or "caption-side" or "border-collapse"
        or "font-synthesis-small-caps" or "font-style" or "font-stretch" or "font-variant-caps" or "font-variant-position" or "font-variant-emoji" or "font-synthesis-position" or "font-variant-numeric" or "font-variant-east-asian" or "font-variant-alternates" or "font-feature-settings" or "font-variation-settings" or "font-optical-sizing" or "font-size-adjust" or "font-kerning" or "font-variant-ligatures" or "line-height" or "text-align" or "text-align-all"
        or "text-align-last" or "text-justify" or "text-indent" or "line-padding" or "initial-letter-align" or "initial-letter-wrap"
        or "overflow-wrap" or "word-break" or "line-break"
        or "word-spacing" or "word-space-transform" or "letter-spacing" or "text-autospace"
        or "hanging-punctuation" or "tab-size"
        or "hyphens" or "hyphenate-character"
        or "hyphenate-limit-chars" or "hyphenate-limit-lines" or "hyphenate-limit-last"
        or "hyphenate-limit-zone"
        or "white-space-collapse" or "text-wrap-mode" or "text-wrap-style" or "text-transform" or "block-ellipsis"
        or "text-underline-offset" or "text-underline-position"
        or "text-decoration-skip-inset" or "text-shadow"
        or "text-emphasis" or "text-emphasis-style" or "text-emphasis-color" or "text-emphasis-position"
        or "visibility" or "cursor" or "caret-color" or "caret-animation" or "caret-shape"
        or "scrollbar-color" or "interpolate-size"
        or "writing-mode" or "text-orientation" or "text-combine-upright"
        or "ruby-position" or "ruby-align" or "ruby-merge"
        or "accent-color" or "color-scheme" or "forced-color-adjust" or "print-color-adjust" or "text-size-adjust"
        or "pointer-events" or "direction" or "image-rendering" or "quotes" or "list-style" or "list-style-type" or "list-style-position" or "list-style-image";

    public static bool IsWideKeyword(string value)
        => WideKeyword(value) is not null;

    internal static string? WideKeyword(string value)
    {
        var reader = new CssTokenReader(value);
        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
        var keyword = ident.ToString().ToLowerInvariant();
        return keyword is "initial" or "inherit" or "unset" or "revert" or "revert-layer" or "revert-rule" ? keyword : null;
    }

    public static string? Initial(string name)
    {
        // CSS Cascade excludes direction from `all`, while the individual
        // property's `initial` keyword still resolves to ltr.
        if (name == "direction") return "ltr";
        if (name == "unicode-bidi") return "normal";
        if (s_initial.TryGetValue(name, out var value)) return value;
        if (name.StartsWith("margin-", StringComparison.Ordinal) || name.StartsWith("padding-", StringComparison.Ordinal) ||
            name.EndsWith("-radius", StringComparison.Ordinal)) return "0";
        if (name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-width", StringComparison.Ordinal)) return "medium";
        if (CssLogicalBoxEdges.IsBorderColor(name)) return "currentcolor";
        if (CssLogicalBoxEdges.IsBorderStyle(name)) return "none";
        return null;
    }
}

/// <summary>Rolls a declaration back by cascade origin, layer, or matched style rule.</summary>
internal enum CssRevertKind { Origin, Layer, Rule }

internal sealed class CssRevertValue(CssRevertKind kind) : CssCompiledValue
{
    public CssRevertKind Kind { get; } = kind;
    internal static CssRevertValue FromKeyword(string keyword) => new(keyword switch
    {
        "revert-layer" => CssRevertKind.Layer,
        "revert-rule" => CssRevertKind.Rule,
        _ => CssRevertKind.Origin,
    });
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink) => true;
}

internal sealed class CssWideValue(string name, string keyword) : CssCompiledValue
{
    private CssCompiledDeclaration[]? _initialDeclarations;
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (CssSelection.ApplyWide(context.Element, name, keyword, context, sink)) return true;
        var inherit = keyword.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
            !keyword.Equals("initial", StringComparison.OrdinalIgnoreCase) &&
                (CssPropertyMetadata.IsInherited(name) || CssSelection.IsProjection(context.Element) && CssSelection.Applies(name));
        var parent = CssFirstLine.InheritanceParent(context.Element, name) ?? CssMatcher.CssAncestor(context.Element);
        if (inherit && parent is not null)
        {
            CssInheritedColorObserver.Observe(context.Element, parent, name);
            CssInheritedBoxObserver.Observe(context.Element, parent, name);
            if (name == "flex-basis")
            {
                var native = parent.Target.HasLocalOrAnimatedValue(Controls.FlexPanel.BasisProperty);
                sink.Set(Controls.FlexPanel.BasisProperty, parent.GetValue(Controls.FlexPanel.BasisProperty));
                sink.Set(Controls.FlexPanel.CssBasisProperty, native ? null : parent.GetValue(Controls.FlexPanel.CssBasisProperty));
                sink.Set(Controls.FlexPanel.CssContentBasisProperty, !native && (bool)parent.GetValue(Controls.FlexPanel.CssContentBasisProperty)!);
                return true;
            }
            if (name == "mask-image")
            {
                var inherited = parent.GetValue(CssMaskProperties.ImagesProperty) as CssMaskImages ?? CssMaskImages.None;
                sink.Set(CssMaskProperties.ImagesProperty, CssMaskImages.Materialize(inherited.Definitions, context));
                return true;
            }
            if (name == "mask-border-source")
            {
                var inherited = parent.GetValue(CssMaskBorderProperties.SourceProperty) as CssMaskBorderSource;
                sink.Set(CssMaskBorderProperties.SourceProperty, CssMaskBorderSource.Materialize(inherited?.Definition, context));
                return true;
            }
            if (name == "border-image-source")
            {
                var inherited = parent.GetValue(CssBorderImageProperties.SourceProperty) as CssBorderImageSource;
                var resources = inherited is null ? new List<CssBackgroundImageResource>() : new(inherited.Resources);
                sink.Set(CssBorderImageProperties.SourceProperty, inherited?.Definition?.Border(context, inherited.Resolution, resources));
                sink.Set(CssBorderImageProperties.ResourcesProperty, resources.ToArray());
                return true;
            }
            if (CssBackgroundPaintProperties.Get(parent.Target) is { } background)
            {
                if (name == "background-image")
                {
                    context.Slots.SetBackgroundImages(
                        background.Layers.Select(static layer => layer.Image?.Clone()).ToArray());
                    return true;
                }
                if (name == "background-size")
                {
                    context.Slots.SetBackgroundSizes(background.Sizes);
                    return true;
                }
                if (name == "background-position")
                {
                    context.Slots.SetBackgroundPositions(background.Positions);
                    return true;
                }
                if (name == "background-repeat")
                {
                    context.Slots.SetBackgroundRepeats(background.Repeats);
                    return true;
                }
                if (name == "background-origin")
                {
                    context.Slots.SetBackgroundOrigins(background.Origins);
                    return true;
                }
                if (name == "background-clip")
                {
                    context.Slots.SetBackgroundClips(background.Clips);
                    return true;
                }
            }
            if (name == "outline-color" && !HasNativeColorOverride(parent, name) &&
                CssOutlineAutoColorProperties.IsAuto(parent.Target))
            {
                // auto computes to auto only with auto style. Otherwise it computes
                // to currentColor, which stays symbolic when inherited.
                return CssOutlineAutoColorProperties.ApplyComputed(context, sink,
                    parent.GetValue(FrameworkElement.OutlineStyleProperty) is OutlineStyle.Auto);
            }
            if (name == "color")
            {
                var brush = CssUsedForeground.Get(parent.Target, context.Element);
                CssColorBrushObserver.Observe(context.Element, brush as SolidColorBrush);
                if (!new CssNamedValue(name, "Foreground", brush ?? Brushes.Black).TryApply(context, sink))
                    return false;
                if (!HasNativeColorOverride(parent, name) &&
                    parent.CssRuntimeState?.ContextualColors?.TryGetValue(name, out var inheritedColor) == true)
                    CssContextualColorValue.Remember(context.Element, name, inheritedColor);
                return true;
            }
            if (name == "font-stretch")
                return new CssFontStretchValue(CssFontStretchValue.Computed(parent)).TryApply(in context, sink);
            if (name == "font-style")
                return new CssFontStyleValue(CssFontStyleValue.Computed(parent)).TryApply(in context, sink);
            if (name == "font-family")
                return new CssFontFamilyValue(CssFontFamilyValue.Computed(parent)).TryApply(in context, sink);
            if ((name is "background-color" or "outline-color" or "column-rule-color" or "text-emphasis-color" ||
                 CssLogicalBoxEdges.IsBorderColor(name)) &&
                parent.CssRuntimeState?.ContextualColors?.TryGetValue(name, out var contextual) == true &&
                !HasNativeColorOverride(parent, name))
            {
                if (!contextual.TryApply(in context, sink)) return false;
                if (name == "outline-color") sink.Set(CssOutlineAutoColorProperties.AutoProperty, false);
                return true;
            }
            if (name == "background-color")
            {
                var native = CssDependencyPropertyLookup.Find(parent.Target.GetType(), "Background");
                var color = HasNativeColorOverride(parent, name)
                    ? native is null ? null : parent.GetValue(native) as Brush
                    : CssBackgroundPaintProperties.Get(parent.Target) is { } paint
                        ? paint.Color : native is null ? null : parent.GetValue(native) as Brush;
                context.Slots.SetBackgroundColor(color);
                return true;
            }
            if (name == "text-decoration-color")
            {
                var inherited = parent.GetValue(CssTextDecorationProperties.ColorProperty) as Brush;
                if (inherited is null &&
                    CssDependencyPropertyLookup.Find(parent.GetType(), "Foreground") is { } foreground)
                    inherited = parent.GetValue(foreground) as Brush;
                sink.Set(CssTextDecorationProperties.ColorProperty, inherited ?? Brushes.Black);
                return true;
            }
            if (name == "line-height")
                return new CssComputedLineHeightValue(CssComputedLineHeightValue.Inherited(parent)).TryApply(context, sink);
            if (name == "text-wrap-mode")
                return CssCoreProperties.ApplyTextWrapMode(
                    (CssTextWrapMode)parent.GetValue(CssFlowProperties.TextWrapModeProperty)!,
                    context, sink);
            if (name == "text-align-all" && parent.Target is FrameworkElement textParent)
                return new CssFlowTextAlignmentValue(CssFlowProperties.TextAlignment(textParent)).TryApply(context, sink);
            if (name == "display")
                return new CssDisplayValue(CssDisplayProperties.Computed(parent)).TryApply(context, sink);
            if (name == "visibility")
                return new CssInheritedVisibilityValue().TryApply(context, sink);
            if (name == "pointer-events")
            {
                sink.Set(CssPointerEventsProperties.ValueProperty, CssPointerEventsValue.Inherit);
                return true;
            }
            if (name is "overflow-x" or "overflow-y" or "overflow-inline" or "overflow-block" &&
                CssOverflowProperties.Get(parent.Target) is { } overflow)
            {
                var horizontal = name is "overflow-x" or "overflow-inline";
                context.Slots.SetOverflow(horizontal, horizontal ? overflow.X : overflow.Y);
                return true;
            }
            if (TryInheritSize(parent, in context, sink)) return true;
            if (name == "box-sizing")
            {
                context.Slots.SetBoxSizing(parent.CssLayout is { HasBoxSizing: true } box
                    ? box.BoxSizing : CssBoxSizing.ContentBox);
                return true;
            }
            if (name == "aspect-ratio")
            {
                if (parent.CssLayout is { HasAspectRatio: true } ratio)
                    context.Slots.SetAspectRatio(ratio.AspectRatioNumerator,
                        ratio.AspectRatioDenominator, ratio.AspectRatioAuto);
                return true;
            }
            if (name == "position")
            {
                context.Slots.SetPosition(parent.CssLayout?.ComputedPosition ?? CssPositionKeyword.Static);
                return true;
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
            if (CssAnimations.InheritedPart(parent, name) is { } animation)
                return new CssAnimationPartValue(name, animation).TryApply(in context, sink);
            if (TryInheritInset(parent, in context) || TryInheritEdge(parent, in context) ||
                CssBorderRadiusProperties.Inherit(name, parent, context.Slots) ||
                CssBorderPaintProperties.InheritColor(name, parent, in context) ||
                CssBorderStyleProperties.InheritStyle(name, parent, in context)) return true;
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
                if (name == "outline-color") sink.Set(CssOutlineAutoColorProperties.AutoProperty, false);
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

    private static bool HasNativeColorOverride(CssNode parent, string name)
    {
        var nativeName = name switch
        {
            "color" => "Foreground",
            "background-color" => "Background",
            "border-color" => "BorderBrush",
            _ when CssLogicalBoxEdges.IsBorderColor(name) => "BorderBrush",
            _ => "OutlineBrush",
        };
        var property = name == "column-rule-color" ? CssMultiColumnProperties.RuleColorProperty
            : CssDependencyPropertyLookup.Find(parent.Target.GetType(), nativeName);
        return property is not null &&
            (parent.HasLocalOrAnimatedValue(property) ||
             parent.Target.GetEffectiveValueLayer(property) is { } layer &&
             layer is not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState));
    }

    private bool TryInheritSize(CssNode parent, in CssApplyContext context, ICssSetterSink sink)
    {
        var (propertyName, field, sentinel) = name switch
        {
            "width" or "inline-size" => ("Width", CssLayoutSlotField.Width, double.NaN),
            "height" or "block-size" => ("Height", CssLayoutSlotField.Height, double.NaN),
            "min-width" or "min-inline-size" => ("MinWidth", CssLayoutSlotField.MinWidth, 0d),
            "min-height" or "min-block-size" => ("MinHeight", CssLayoutSlotField.MinHeight, 0d),
            "max-width" or "max-inline-size" => ("MaxWidth", CssLayoutSlotField.MaxWidth, double.PositiveInfinity),
            "max-height" or "max-block-size" => ("MaxHeight", CssLayoutSlotField.MaxHeight, double.PositiveInfinity),
            _ => (null, CssLayoutSlotField.Width, 0d),
        };
        if (propertyName is null ||
            CssDependencyPropertyLookup.Find(context.Element.GetType(), propertyName) is not { } childProperty ||
            CssDependencyPropertyLookup.Find(parent.GetType(), propertyName) is not { } parentProperty)
            return false;

        if (!parent.HasLocalOrAnimatedValue(parentProperty))
        {
            var length = CssSizingPresentation.Presented(parent.Target, (int)field,
                CssSizingPresentation.FromLayout(parent.CssLayout, field));
            if (length.IsSet)
            {
                context.Slots.SetLayoutLength(field, length);
                sink.Set(childProperty, sentinel);
                return true;
            }
        }
        if (field is CssLayoutSlotField.MinWidth or CssLayoutSlotField.MinHeight &&
            parent.Target.GetEffectiveValueLayer(parentProperty) is null)
        {
            context.Slots.SetLayoutLength(field, CssLayoutLength.Auto);
            sink.Set(childProperty, sentinel);
            return true;
        }
        sink.Set(childProperty, parent.GetValue(parentProperty));
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
        var targetEdge = CssLogicalBoxEdges.IsMappedProperty(name)
            ? CssLogicalBoxEdges.Edge(name, context.Slots.LogicalRightToLeft)
            : name.Contains("left", StringComparison.Ordinal) ? 0 : name.Contains("top", StringComparison.Ordinal) ? 1
                : name.Contains("right", StringComparison.Ordinal) ? 2 : 3;
        var sourceEdge = CssLogicalBoxEdges.IsMappedProperty(name)
            ? CssLogicalBoxEdges.Edge(name, parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft)
            : targetEdge;
        if ((slot is CssSlot.Margin or CssSlot.Padding) &&
            (dp is null || !parent.HasLocalOrAnimatedValue(dp)) && parent.CssLayout is { } layout)
        {
            var inherited = (slot, sourceEdge) switch
            {
                (CssSlot.Margin, 0) => layout.MarginLeft,
                (CssSlot.Margin, 1) => layout.MarginTop,
                (CssSlot.Margin, 2) => layout.MarginRight,
                (CssSlot.Margin, _) => layout.MarginBottom,
                (CssSlot.Padding, 0) => layout.PaddingLeft,
                (CssSlot.Padding, 1) => layout.PaddingTop,
                (CssSlot.Padding, 2) => layout.PaddingRight,
                _ => layout.PaddingBottom,
            };
            if (inherited.IsSet)
            {
                context.Slots.SetComputedThicknessEdge(slot, targetEdge, inherited);
                return true;
            }
        }
        if (dp is null || parent.GetValue(dp) is not Thickness thickness) return false;
        var value = sourceEdge switch { 0 => thickness.Left, 1 => thickness.Top, 2 => thickness.Right, _ => thickness.Bottom };
        context.Slots.SetThicknessEdge(slot, targetEdge, new CssLength(value, CssUnit.Px));
        return true;
    }

    private bool TryInheritInset(CssNode parent, in CssApplyContext context)
    {
        if (name is not ("left" or "top" or "right" or "bottom" or
            "inset-inline-start" or "inset-inline-end" or "inset-block-start" or "inset-block-end"))
            return false;

        var sourceEdge = CssLogicalBoxEdges.Edge(name,
            parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft);
        var targetEdge = CssLogicalBoxEdges.Edge(name, context.Slots.LogicalRightToLeft);
        var sourceProperty = CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(sourceEdge);
        CssLayoutLength value;
        if (parent.Target is FrameworkElement { CssLayout: { } layout } &&
            (sourceProperty is null || !parent.HasLocalOrAnimatedValue(sourceProperty)))
        {
            value = sourceEdge switch
            {
                0 => layout.InsetLeft,
                1 => layout.InsetTop,
                2 => layout.InsetRight,
                _ => layout.InsetBottom,
            };
            if (!value.IsSet) value = CssLayoutLength.Auto;
        }
        else if (sourceProperty is not null && parent.GetValue(sourceProperty) is double pixels &&
            double.IsFinite(pixels))
            value = CssLayoutLength.Px(pixels);
        else
            value = CssLayoutLength.Auto;

        context.Slots.SetInset(targetEdge, value, CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(targetEdge));
        return true;
    }
}
