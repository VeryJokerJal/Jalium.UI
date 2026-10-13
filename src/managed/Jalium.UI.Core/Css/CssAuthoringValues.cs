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
            foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries)) result[name] = values;
        }
        Add("display", "none", "contents", "block", "inline", "inline-block", "flow-root", "flex", "inline-flex", "grid", "inline-grid", "list-item", "table", "table-row", "table-cell");
        Add("position", "static", "relative", "absolute", "fixed", "sticky");
        Add("visibility", "visible", "hidden", "collapse");
        Add("overflow overflow-x overflow-y overflow-inline overflow-block", "visible", "hidden", "clip", "auto", "scroll");
        Add("box-sizing", "content-box", "border-box");
        Add("width height inline-size block-size min-width min-height min-inline-size min-block-size max-width max-height max-inline-size max-block-size flex-basis",
            "auto", "none", "min-content", "max-content", "fit-content", "stretch", "fit-content(100px)");
        Add("margin margin-top margin-right margin-bottom margin-left margin-inline margin-block margin-inline-start margin-inline-end margin-block-start margin-block-end top right bottom left inset inset-inline inset-block inset-inline-start inset-inline-end inset-block-start inset-block-end", "auto");
        Add("color background-color border-color border-top-color border-right-color border-bottom-color border-left-color border-inline-color border-block-color border-inline-start-color border-inline-end-color border-block-start-color border-block-end-color outline-color text-decoration-color caret-color",
            "transparent", "currentcolor", "black", "white", "red", "green", "blue", "rebeccapurple", "#000000", "rgb(0 0 0)", "hsl(0 0% 0%)", "oklch(50% 0.1 120)", "color-mix(in srgb, red, blue)", "auto");
        Add("background-image", "none", "url(\"image.png\")", "linear-gradient(red, blue)", "radial-gradient(red, blue)", "conic-gradient(red, blue)");
        Add("background-repeat", "repeat", "repeat-x", "repeat-y", "no-repeat", "space", "round");
        Add("background-size", "auto", "cover", "contain");
        Add("background-origin background-clip", "border-box", "padding-box", "content-box", "text");
        Add("background-position object-position transform-origin", "center", "left", "right", "top", "bottom", "left top", "50% 50%");
        Add("border-style border-top-style border-right-style border-bottom-style border-left-style border-inline-style border-block-style border-inline-start-style border-inline-end-style border-block-start-style border-block-end-style outline-style text-decoration-style",
            "none", "hidden", "solid", "double", "dotted", "dashed", "groove", "ridge", "inset", "outset", "wavy", "auto");
        Add("border-width border-top-width border-right-width border-bottom-width border-left-width border-inline-width border-block-width border-inline-start-width border-inline-end-width border-block-start-width border-block-end-width outline-width", "thin", "medium", "thick");
        Add("border outline", "none", "1px solid currentcolor");
        Add("flex-direction", "row", "row-reverse", "column", "column-reverse");
        Add("flex-wrap", "nowrap", "wrap", "wrap-reverse");
        Add("flex", "none", "auto", "initial", "1 1 0%", "0 1 auto");
        Add("flex-flow", "row nowrap", "column wrap");
        Add("align-items align-self align-content justify-items justify-self justify-content place-items place-self place-content",
            "normal", "auto", "stretch", "center", "start", "end", "self-start", "self-end", "flex-start", "flex-end", "baseline", "first baseline", "last baseline", "space-between", "space-around", "space-evenly", "safe center", "unsafe center", "left", "right");
        Add("gap row-gap column-gap letter-spacing word-spacing", "normal");
        Add("grid-auto-flow", "row", "column", "dense", "row dense", "column dense");
        Add("grid-template-columns grid-template-rows grid-auto-columns grid-auto-rows", "none", "auto", "min-content", "max-content", "1fr", "minmax(0, 1fr)", "repeat(2, 1fr)");
        Add("grid-row grid-column grid-row-start grid-row-end grid-column-start grid-column-end grid-area", "auto", "span 2");
        Add("font-family", "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "emoji", "math", "fangsong");
        Add("font-size", "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "larger", "smaller", "math");
        Add("font-weight", "normal", "bold", "bolder", "lighter", "100", "200", "300", "400", "500", "600", "700", "800", "900");
        Add("font-style", "normal", "italic", "oblique", "oblique 15deg");
        Add("font-stretch", "normal", "ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "semi-expanded", "expanded", "extra-expanded", "ultra-expanded");
        Add("line-height", "normal");
        Add("text-align text-align-all text-align-last text-group-align", "auto", "none", "start", "end", "left", "right", "center", "justify", "justify-all", "match-parent");
        Add("text-justify", "auto", "none", "inter-word", "inter-character");
        Add("direction", "ltr", "rtl");
        Add("white-space", "normal", "nowrap", "pre", "pre-wrap", "pre-line", "break-spaces");
        Add("white-space-collapse", "collapse", "preserve", "preserve-breaks", "preserve-spaces", "break-spaces");
        Add("white-space-trim", "none", "discard-before", "discard-after", "discard-inner");
        Add("text-wrap text-wrap-mode text-wrap-style", "auto", "wrap", "nowrap", "balance", "stable", "pretty");
        Add("text-transform", "none", "capitalize", "uppercase", "lowercase", "full-width", "full-size-kana", "math-auto");
        Add("text-overflow", "clip", "ellipsis");
        Add("text-decoration text-decoration-line", "none", "underline", "overline", "line-through", "underline overline");
        Add("text-decoration-thickness", "auto", "from-font");
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
        Add("transform", "none", "translate(10px, 10px)", "scale(1)", "rotate(45deg)", "skew(10deg)", "matrix(1, 0, 0, 1, 0, 0)");
        Add("transform-box", "content-box", "border-box", "fill-box", "stroke-box", "view-box");
        Add("box-shadow text-shadow", "none", "1px 1px 2px black");
        Add("filter backdrop-filter", "none", "blur(4px)", "brightness(1)", "contrast(1)", "grayscale(1)", "opacity(0.5)", "saturate(1)", "sepia(1)", "hue-rotate(45deg)", "drop-shadow(1px 1px 2px black)");
        Add("clip-path", "none", "circle(50%)", "ellipse(50% 50%)", "inset(10px)", "polygon(0 0, 100% 0, 50% 100%)");
        Add("transition-property", "all", "none", "opacity", "transform", "color");
        Add("transition-duration transition-delay animation-duration animation-delay", "0s", "100ms", "1s");
        Add("transition-timing-function animation-timing-function", "ease", "linear", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end", "steps(2, end)", "cubic-bezier(0.25, 0.1, 0.25, 1)");
        Add("transition-behavior", "normal", "allow-discrete");
        Add("animation-direction", "normal", "reverse", "alternate", "alternate-reverse");
        Add("animation-fill-mode", "none", "forwards", "backwards", "both");
        Add("animation-play-state", "running", "paused");
        Add("animation-iteration-count", "infinite");
        return result;
    }
}
