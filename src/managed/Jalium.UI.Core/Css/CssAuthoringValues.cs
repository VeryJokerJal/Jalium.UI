namespace Jalium.UI.Styling;

/// <summary>Framework-owned completion vocabulary; candidates are always checked by the runtime parser.</summary>
internal static class CssAuthoringValues
{
    private static readonly string[] Wide = ["initial", "inherit", "unset", "revert", "revert-layer", "revert-rule"];
    private static readonly Dictionary<string, string[]> Keywords = BuildKeywords();

    internal static IEnumerable<string> Candidates(string name, string? initial)
    {
        if (initial is not null) yield return initial;
        foreach (var value in Wide) yield return value;
        if (Keywords.TryGetValue(name, out var values)) foreach (var value in values) yield return value;
        // These examples also expose supported functions/units for open-ended value grammars.
        foreach (var value in new[] { "0", "1", "0.5", "100%", "1px", "1em", "1rem", "1vw", "1vh",
            "calc(100% - 1px)", "min(100%, 10px)", "max(0px, 1em)", "clamp(1px, 1vw, 10px)" }) yield return value;
    }

    private static Dictionary<string, string[]> BuildKeywords()
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string names, params string[] values)
        {
            foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                result[name] = result.TryGetValue(name, out var existing)
                    ? existing.Concat(values).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : values;
            }
        }
        Add("display", "none", "contents", "block", "inline", "inline-block", "flow-root", "flex", "inline-flex", "grid", "inline-grid", "list-item", "table", "inline-table", "table-row", "table-cell", "table-row-group", "table-header-group", "table-footer-group", "table-column", "table-column-group", "table-caption", "ruby", "ruby-base", "ruby-text", "ruby-base-container", "ruby-text-container");
        Add("position", "static", "relative", "absolute", "fixed", "sticky");
        Add("mask-image", "none", "url(\"mask.png\")", "linear-gradient(white, transparent)", "image(black)", "image-set(\"mask.png\" 1x, \"mask@2x.png\" 2x)");
        Add("mask-mode", "match-source", "alpha", "luminance");
        Add("mask-repeat", "repeat", "repeat-x", "repeat-y", "no-repeat", "space", "round", "space round");
        Add("mask-position", "left top", "center", "right bottom", "right 4px bottom 4px", "50% 50%");
        Add("mask-origin", "border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box");
        Add("mask-clip", "border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box", "no-clip");
        Add("mask-size", "auto", "cover", "contain", "50% auto", "20px 30px");
        Add("mask-composite", "add", "subtract", "intersect", "exclude");
        Add("mask-type", "luminance", "alpha");
        Add("mask-border", "none", "url(\"frame.png\") 30 / 8px round alpha", "linear-gradient(white, black) 20 fill / 8px / 2px luminance");
        Add("mask-border-source", "none", "url(\"frame.png\")", "image(white)", "linear-gradient(white, transparent)");
        Add("mask-border-slice", "0", "30", "10% 20%", "fill 30", "calc(100% / 3) fill");
        Add("mask-border-width", "auto", "8px", "1 2 3 4", "calc(10% + 2px)");
        Add("mask-border-outset", "0", "2", "4px", "1em 0");
        Add("mask-border-repeat", "stretch", "repeat", "round", "space", "round space");
        Add("mask-border-mode", "alpha", "luminance");
        Add("mask", "none", "linear-gradient(white, transparent)", "image(black) center / 50% no-repeat alpha");
        Add("break-before break-after", "auto", "avoid", "avoid-page", "page", "left", "right", "recto", "verso", "avoid-column", "column", "avoid-region", "region");
        Add("break-inside", "auto", "avoid", "avoid-page", "avoid-column", "avoid-region");
        Add("page-break-before page-break-after", "auto", "avoid", "always", "left", "right");
        Add("page-break-inside", "auto", "avoid");
        Add("orphans widows", "1", "2", "3", "4");
        Add("page", "auto", "landscape", "chapter");
        Add("size", "auto", "portrait", "landscape", "a4", "a4 landscape", "letter", "letter portrait", "210mm 297mm", "8.5in 11in");
        Add("page-orientation", "upright", "rotate-left", "rotate-right");
        Add("table-layout", "auto", "fixed");
        Add("caption-side", "top", "bottom");
        Add("border-collapse", "separate", "collapse");
        Add("display", "table", "inline-table", "table-row", "table-cell", "table-row-group", "table-header-group", "table-footer-group", "table-column", "table-column-group");
        Add("border-spacing", "0", "2px", "4px 8px");
        Add("empty-cells", "show", "hide");
        Add("visibility", "visible", "hidden", "collapse");
        Add("column-count column-width columns", "auto");
        Add("column-fill", "auto", "balance", "balance-all");
        Add("column-span", "none", "all");
        Add("column-rule-style", "none", "hidden", "solid", "double", "dotted", "dashed", "groove", "ridge", "inset", "outset");
        Add("column-rule-width", "thin", "medium", "thick");
        Add("overflow overflow-x overflow-y overflow-inline overflow-block", "visible", "hidden", "clip", "auto", "scroll");
        Add("box-sizing", "content-box", "border-box");
        Add("content", "normal", "none", "\"\"", "\"前缀\"", "\"[\" attr(Tag) \"]\"", "counter(item)", "counters(item, \".\")", "open-quote", "close-quote", "no-open-quote", "no-close-quote", "counter(item, symbols(cyclic \"●\" \"○\"))", "counter(item, symbols(numeric \"〇\" \"一\"))", "url(\"image.png\") / \"图片说明\"", "\"前\" url(\"image.png\") \"后\"", "image-set(\"image.png\" 1x, \"image@2x.png\" 2x)", "linear-gradient(currentcolor, transparent)", "image(currentcolor)", "image(\"image.png\", currentcolor)", "image(ltr \"image.png#xywh=0,0,32,32\", transparent)", "cross-fade(url(\"first.png\") 25%, url(\"second.png\"))");
        Add("list-style-type", "disc", "circle", "square", "decimal", "decimal-leading-zero", "lower-alpha", "upper-alpha", "lower-roman", "upper-roman", "none", "\"→ \"", "symbols(cyclic \"●\" \"○\")");
        Add("list-style-position", "outside", "inside");
        Add("list-style-image", "none", "url(\"marker.png\")", "image-set(\"marker.png\" 1x, \"marker@2x.png\" 2x)", "linear-gradient(currentcolor, transparent)", "image(currentcolor)", "image(\"image.png\", currentcolor)", "image(ltr \"image.png#xywh=0,0,32,32\", transparent)", "cross-fade(url(\"first.png\") 25%, url(\"second.png\"))");
        Add("list-style", "disc outside", "decimal inside", "none", "\"→ \" outside", "symbols(cyclic \"●\" \"○\") inside", "decimal outside url(\"marker.png\")");
        Add("counter-reset", "none", "item", "item 0", "reversed(item)");
        Add("counter-increment", "none", "item", "item 1", "item -1");
        Add("counter-set", "none", "item", "item 1");
        Add("quotes", "auto", "none", "match-parent", "\"“\" \"”\" \"‘\" \"’\"");
        Add("width height inline-size block-size min-width min-height min-inline-size min-block-size max-width max-height max-inline-size max-block-size flex-basis",
            "auto", "none", "min-content", "max-content", "fit-content", "stretch", "fit-content(100px)",
            "calc-size(auto, size + 20px)", "calc-size(min-content, size * 2)", "calc-size(any, 100px)");
        Add("interpolate-size", "numeric-only", "allow-keywords");
        Add("margin margin-top margin-right margin-bottom margin-left margin-inline margin-block margin-inline-start margin-inline-end margin-block-start margin-block-end top right bottom left inset inset-inline inset-block inset-inline-start inset-inline-end inset-block-start inset-block-end", "auto");
        Add("color background-color border-color border-top-color border-right-color border-bottom-color border-left-color border-inline-color border-block-color border-inline-start-color border-inline-end-color border-block-start-color border-block-end-color outline-color text-decoration-color caret-color",
            "transparent", "currentcolor", "black", "white", "red", "green", "blue", "rebeccapurple", "#000000", "rgb(0 0 0)", "hsl(0 0% 0%)", "oklch(50% 0.1 120)", "color-mix(in srgb, red, blue)", "auto");
        Add("background-image", "none", "url(\"image.png\")", "linear-gradient(red, blue)", "radial-gradient(red, blue)", "conic-gradient(red, blue)", "image-set(url(\"image.png\") 1x, url(\"image@2x.png\") 2x)", "image(currentcolor)", "image(\"image.png\", currentcolor)", "image(ltr \"image.png#xywh=0,0,32,32\", transparent)", "cross-fade(url(\"first.png\") 25%, url(\"second.png\"))");
        Add("background-repeat", "repeat", "repeat-x", "repeat-y", "no-repeat", "space", "round");
        Add("background-size", "auto", "cover", "contain");
        Add("background-origin", "border-box", "padding-box", "content-box");
        Add("background-clip", "border-box", "padding-box", "content-box", "text", "border-area", "border-area text");
        Add("background-position object-position transform-origin perspective-origin", "center", "left", "right", "top", "bottom", "left top", "50% 50%");
        Add("border-style border-top-style border-right-style border-bottom-style border-left-style border-inline-style border-block-style border-inline-start-style border-inline-end-style border-block-start-style border-block-end-style outline-style text-decoration-style",
            "none", "hidden", "solid", "double", "dotted", "dashed", "groove", "ridge", "inset", "outset", "wavy", "auto");
        Add("border-width border-top-width border-right-width border-bottom-width border-left-width border-inline-width border-block-width border-inline-start-width border-inline-end-width border-block-start-width border-block-end-width outline-width", "thin", "medium", "thick");
        Add("border outline", "none", "1px solid currentcolor");
        Add("flex-direction", "row", "row-reverse", "column", "column-reverse");
        Add("flex-wrap", "nowrap", "wrap", "wrap-reverse");
        Add("flex", "none", "auto", "initial", "1 1 0%", "0 1 auto");
        Add("flex-basis", "content");
        Add("flex-flow", "row nowrap", "column wrap");
        Add("align-items align-self align-content justify-items justify-self justify-content place-items place-self place-content",
            "normal", "auto", "stretch", "center", "start", "end", "self-start", "self-end", "flex-start", "flex-end", "baseline", "first baseline", "last baseline", "space-between", "space-around", "space-evenly", "safe center", "unsafe center", "left", "right");
        Add("gap row-gap column-gap letter-spacing word-spacing", "normal");
        Add("grid-auto-flow", "row", "column", "dense", "row dense", "column dense");
        Add("grid-template-columns grid-template-rows grid-auto-columns grid-auto-rows", "none", "auto", "min-content", "max-content", "1fr", "minmax(0, 1fr)", "repeat(2, 1fr)");
        Add("grid-row grid-column grid-row-start grid-row-end grid-column-start grid-column-end grid-area", "auto", "span 2");
        Add("font-family", "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "emoji", "math", "fangsong");
        Add("font", "caption", "icon", "menu", "message-box", "small-caption", "status-bar", "small-caps 16px serif");
        Add("font-size", "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "larger", "smaller", "math");
        Add("font-weight", "normal", "bold", "bolder", "lighter", "100", "200", "300", "400", "500", "600", "700", "800", "900");
        Add("font-style", "normal", "italic", "oblique", "oblique 15deg");
        Add("font-stretch", "normal", "ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "semi-expanded", "expanded", "extra-expanded", "ultra-expanded");
        Add("line-height", "normal");
        Add("text-align text-align-all text-align-last text-group-align", "auto", "none", "start", "end", "left", "right", "center", "justify", "justify-all", "match-parent");
        Add("text-justify", "auto", "none", "inter-word", "inter-character");
        Add("direction", "ltr", "rtl");
        Add("unicode-bidi", "normal", "embed", "isolate", "bidi-override", "isolate-override", "plaintext");
        Add("font-variant-numeric", "normal", "lining-nums", "oldstyle-nums", "proportional-nums", "tabular-nums",
            "diagonal-fractions", "stacked-fractions", "ordinal", "slashed-zero");
        Add("font-variant-east-asian", "normal", "jis78", "jis83", "jis90", "jis04", "simplified", "traditional",
            "full-width", "proportional-width", "ruby");
        Add("font-variant-alternates", "normal", "historical-forms", "stylistic(alternate)", "styleset(set-one, set-two)",
            "character-variant(alternate)", "swash(flowing)", "ornaments(ornate)", "annotation(circled)");
        Add("font-kerning", "auto", "normal", "none");
        Add("font-synthesis-small-caps", "auto", "none");
        Add("font-variant", "normal", "none", "small-caps oldstyle-nums", "super tabular-nums", "text");
        Add("font-variant-emoji", "normal", "text", "emoji", "unicode");
        Add("font-variant-position", "normal", "sub", "super");
        Add("font-synthesis-position", "auto", "none");
        Add("font-variant-caps", "normal", "small-caps", "all-small-caps", "petite-caps", "all-petite-caps", "unicase", "titling-caps");
        Add("font-variant-ligatures", "normal", "none", "common-ligatures", "no-common-ligatures",
            "discretionary-ligatures", "no-discretionary-ligatures", "historical-ligatures", "no-historical-ligatures", "contextual", "no-contextual");
        Add("font-size-adjust", "none", "0.5", "from-font", "cap-height 0.7", "ch-width 0.5", "ic-width 1", "ic-height 1");
        Add("font-optical-sizing", "auto", "none");
        Add("font-variation-settings", "normal", "\"wght\" 650", "\"wdth\" 87.5", "\"slnt\" -12");
        Add("font-feature-settings", "normal", "\"kern\"", "\"liga\" off", "\"tnum\" 1", "\"cv01\" 2");
        Add("white-space", "normal", "nowrap", "pre", "pre-wrap", "pre-line", "break-spaces");
        Add("white-space-collapse", "collapse", "preserve", "preserve-breaks", "preserve-spaces", "break-spaces");
        Add("white-space-trim", "none", "discard-before", "discard-after", "discard-inner");
        Add("text-wrap text-wrap-mode text-wrap-style", "auto", "wrap", "nowrap", "balance", "stable", "pretty");
        Add("line-clamp", "none", "auto", "2", "2 auto", "2 no-ellipsis", "2 \"… 阅读更多\"");
        Add("-webkit-line-clamp", "none", "2");
        Add("max-lines", "auto", "2", "2 auto");
        Add("block-ellipsis", "no-ellipsis", "ellipsis", "\"… 阅读更多\"");
        Add("continue", "normal", "collapse", "discard", "-webkit-legacy");
        Add("-webkit-box-orient", "horizontal", "vertical", "inline-axis", "block-axis");
        Add("text-transform", "none", "capitalize", "uppercase", "lowercase", "full-width", "full-size-kana", "math-auto");
        Add("text-overflow", "clip", "ellipsis");
        Add("text-decoration text-decoration-line", "none", "underline", "overline", "line-through", "underline overline");
        Add("text-decoration-thickness", "auto", "from-font");
        Add("text-emphasis text-emphasis-style", "none", "filled", "open", "dot", "filled circle", "open double-circle", "triangle", "sesame", "\"※\"");
        Add("text-emphasis-color", "currentColor", "red", "color-mix(in srgb,currentColor 50%,white 50%)");
        Add("text-emphasis-position", "over", "under", "over right", "under left");
        Add("text-underline-position", "auto", "under", "left", "right");
        Add("text-underline-offset", "auto");
        Add("word-break", "normal", "break-all", "keep-all", "break-word", "auto-phrase");
        Add("overflow-wrap", "normal", "break-word", "anywhere");
        Add("line-break", "auto", "loose", "normal", "strict", "anywhere");
        Add("hyphens", "none", "manual", "auto");
        Add("hyphenate-character hyphenate-limit-chars", "auto");
        Add("hyphenate-limit-lines", "no-limit");
        Add("hyphenate-limit-last", "none", "always", "column", "page", "spread");
        Add("hanging-punctuation", "none", "first", "last", "allow-end", "force-end");
        Add("text-autospace", "normal", "no-autospace", "ideograph-alpha", "ideograph-numeric", "ideograph-punctuation", "insert", "replace");
        Add("word-space-transform", "none", "space", "ideographic-space");
        Add("box-decoration-break", "slice", "clone");
        Add("float clear", "none", "left", "right", "both", "inline-start", "inline-end");
        Add("vertical-align", "baseline", "sub", "super", "text-top", "text-bottom", "middle", "top", "bottom");
        Add("border-image", "none", "url(\"frame.png\") 30 / 8px round", "linear-gradient(red, blue) 20 fill / 8px / 2px stretch");
        Add("border-image-source", "none", "url(\"frame.png\")", "image-set(\"frame.png\" 1x, \"frame@2x.png\" 2x)", "image(\"frame.png\", currentcolor)", "cross-fade(url(\"frame.png\"), currentcolor)", "linear-gradient(red, blue)");
        Add("border-image-slice", "100%", "30", "10% 20%", "fill 30", "calc(100% / 3) fill");
        Add("border-image-width", "1", "auto", "8px", "1 2 3 4", "calc(10% + 2px)");
        Add("border-image-outset", "0", "2", "4px", "1em 0");
        Add("border-image-repeat", "stretch", "repeat", "round", "space", "round space");
        Add("object-fit", "fill", "contain", "cover", "none", "scale-down");
        Add("image-rendering", "auto", "smooth", "high-quality", "crisp-edges", "pixelated");
        Add("cursor", "auto", "default", "none", "pointer", "text", "wait", "progress", "crosshair", "move", "help", "not-allowed", "grab", "grabbing", "col-resize", "row-resize", "n-resize", "s-resize", "e-resize", "w-resize", "ne-resize", "nw-resize", "se-resize", "sw-resize", "ew-resize", "ns-resize", "nesw-resize", "nwse-resize", "zoom-in", "zoom-out");
        Add("pointer-events", "auto", "none", "visiblePainted", "visibleFill", "visibleStroke", "visible", "painted", "fill", "stroke", "all", "bounding-box");
        Add("user-select", "auto", "none", "text", "all", "contain");
        Add("scrollbar-width", "auto", "thin", "none");
        Add("scrollbar-color", "auto", "red blue");
        Add("scrollbar-gutter", "auto", "stable", "stable both-edges");
        Add("caret-animation", "auto", "manual");
        Add("caret-shape", "auto", "bar", "block", "underscore");
        Add("math-style", "normal", "compact");
        Add("container-type", "normal", "size", "inline-size", "scroll-state");
        Add("container-name animation-name", "none");
        Add("transform", "none", "translate(10px, 10px)", "translate3d(10px, 10px, 20px)", "translateZ(20px)", "scale(1)", "scale3d(1, 1, 1)", "scaleZ(1)", "rotate(45deg)", "rotate3d(0, 1, 0, 45deg)", "rotateX(45deg)", "rotateY(45deg)", "rotateZ(45deg)", "skew(10deg)", "perspective(500px)", "matrix(1, 0, 0, 1, 0, 0)", "matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)");
        Add("transform-box", "content-box", "border-box", "fill-box", "stroke-box", "view-box");
        Add("perspective", "none", "500px", "1000px");
        Add("transform-style", "flat", "preserve-3d");
        Add("backface-visibility", "visible", "hidden");
        Add("box-shadow text-shadow", "none", "1px 1px 2px black");
        Add("filter backdrop-filter", "none", "blur(4px)", "brightness(1)", "contrast(1)", "grayscale(1)", "opacity(0.5)", "saturate(1)", "sepia(1)", "hue-rotate(45deg)", "drop-shadow(1px 1px 2px black)");
        Add("shape-outside", "none", "margin-box", "border-box", "padding-box", "content-box", "circle(50%)", "ellipse(50% 50%)", "inset(10px)", "polygon(0 0, 100% 0, 50% 100%)", "path('M0 0 L100 100 L0 100 Z')", "shape(from 0 0, line to 100% 0, line to 0 100%, close)", "url(\"image.png\")", "linear-gradient(black, transparent)", "radial-gradient(black, transparent)", "conic-gradient(black, transparent)", "image-set(url(\"image.png\") 1x, url(\"image@2x.png\") 2x)", "image(currentcolor)", "image(\"image.png\", currentcolor)", "image(ltr \"image.png#xywh=0,0,32,32\", transparent)", "cross-fade(url(\"first.png\") 25%, url(\"second.png\"))");
        Add("shape-image-threshold", "0", "0.5", "50%", "1", "calc(25% + .25)");
        Add("shape-margin", "0", "8px", "10%", "calc(1em + 2px)");
        Add("clip-path", "none", "circle(50%)", "ellipse(50% 50%)", "inset(10px)", "polygon(0 0, 100% 0, 50% 100%)");
        Add("transition-property", "all", "none", "opacity", "transform", "color");
        Add("transition-duration transition-delay animation-duration animation-delay", "0s", "100ms", "1s");
        Add("transition-timing-function animation-timing-function", "ease", "linear", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end", "steps(2, end)", "cubic-bezier(0.25, 0.1, 0.25, 1)");
        Add("transition-behavior", "normal", "allow-discrete");
        Add("animation-direction", "normal", "reverse", "alternate", "alternate-reverse");
        Add("animation-fill-mode", "none", "forwards", "backwards", "both");
        Add("animation-play-state", "running", "paused");
        Add("animation-iteration-count", "infinite");
        Add("animation-composition", "replace", "add", "accumulate");
        Add("animation-timeline", "auto", "none", "--main-timeline", "scroll()", "scroll(nearest block)", "view()", "view(block)");
        Add("scroll-timeline-name view-timeline-name timeline-scope", "none", "--main-timeline");
        Add("scroll-timeline-axis view-timeline-axis", "block", "inline", "x", "y");
        Add("view-timeline-inset", "auto", "10px", "10% 20%");
        Add("scroll-timeline", "none", "--main-timeline block", "--main-timeline inline");
        Add("view-timeline", "none", "--main-timeline block", "--main-timeline inline");
        Add("animation-range animation-range-start animation-range-end", "normal", "cover", "contain", "entry", "exit", "entry 0%", "exit 100%");
        Add("offset-path", "none", "path(\"M 0 0 L 100 100\")", "ray(45deg closest-side)", "circle(50px at 50% 50%)");
        Add("offset-distance", "0px", "50%", "100%");
        Add("offset-rotate", "auto", "reverse", "0deg", "45deg", "auto 90deg");
        Add("offset-anchor offset-position", "auto", "normal", "center", "50% 50%");
        Add("view-transition-name view-transition-class", "none", "hero-card");
        Add("writing-mode", "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr");
        Add("text-orientation", "mixed", "upright", "sideways", "sideways-right");
        Add("text-combine-upright", "none", "all", "digits", "digits 2", "digits 4");
        Add("ruby-position", "over", "under", "alternate", "alternate over", "alternate under", "inter-character");
        Add("ruby-align", "space-around", "center", "start", "space-between");
        Add("ruby-merge", "separate", "merge", "auto");
        Add("marker-side", "match-self", "match-parent");
        Add("anchor-name anchor-scope", "none", "--my-anchor");
        Add("position-anchor", "auto", "none", "--my-anchor");
        Add("position-area inset-area", "none", "top", "bottom", "left", "right", "center", "top center", "bottom span-all", "block-start inline-end");
        Add("position-try-order", "normal", "most-width", "most-height", "most-block-size", "most-inline-size");
        Add("position-try-fallbacks", "none", "flip-block", "flip-inline", "flip-start", "--try-below");
        Add("position-try", "normal flip-block", "most-height --try-below, flip-block");
        Add("position-visibility", "always", "anchors-valid", "anchors-visible", "no-overflow");
        Add("scroll-snap-type", "none", "x mandatory", "y mandatory", "both proximity", "block mandatory", "inline proximity");
        Add("scroll-snap-align", "none", "start", "end", "center", "start end");
        Add("scroll-snap-stop", "normal", "always");
        Add("scroll-behavior", "auto", "smooth");
        Add("overflow-anchor", "auto", "none");
        Add("overscroll-behavior overscroll-behavior-x overscroll-behavior-y overscroll-behavior-inline overscroll-behavior-block", "auto", "contain", "none");
        Add("contain", "none", "strict", "content", "size", "inline-size", "layout", "style", "paint", "layout paint");
        Add("content-visibility", "visible", "auto", "hidden");
        Add("contain-intrinsic-size contain-intrinsic-width contain-intrinsic-height contain-intrinsic-inline-size contain-intrinsic-block-size", "none", "100px", "auto 100px");
        Add("appearance -webkit-appearance", "none", "auto", "button", "textfield");
        Add("accent-color", "auto", "#10b981", "currentcolor");
        Add("color-scheme", "normal", "light", "dark", "light dark", "only dark", "only light");
        Add("resize", "none", "both", "horizontal", "vertical", "block", "inline");
        Add("touch-action", "auto", "none", "pan-x", "pan-y", "pinch-zoom", "manipulation", "pan-x pinch-zoom");
        Add("forced-color-adjust", "auto", "none", "preserve-parent-color");
        Add("print-color-adjust -webkit-print-color-adjust", "economy", "exact");
        Add("text-size-adjust", "auto", "none", "100%", "125%");
        Add("zoom", "normal", "reset", "1", "1.25", "125%");
        Add("bleed", "auto", "6pt", "3mm");
        Add("marks", "none", "crop", "cross", "crop cross");
        return result;
    }
}
