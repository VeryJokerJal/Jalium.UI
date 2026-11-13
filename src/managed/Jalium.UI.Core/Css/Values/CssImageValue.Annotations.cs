namespace Jalium.UI.Styling;

internal sealed partial class CssImageValue
{
    private sealed record Annotation(CssImageValue? Image, CssImageColor? Fallback, CssImageFragment? Fragment, FlowDirection? Direction);
    private readonly Annotation? _annotation;
    private CssImageValue(Annotation annotation) => _annotation = annotation;

    private static bool TryReadAnnotated(ref CssTokenReader reader, CssCompileContext context, out CssImageValue? image)
    {
        image = null;
        FlowDirection? direction = null;
        var probe = reader;
        if (probe.TryReadIdent(out var tag) && (tag.Equals("ltr", StringComparison.OrdinalIgnoreCase) || tag.Equals("rtl", StringComparison.OrdinalIgnoreCase)))
        {
            direction = tag.Equals("ltr", StringComparison.OrdinalIgnoreCase) ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            reader = probe;
        }
        probe = reader;
        string? reference = null;
        if (probe.TryReadString(out var text)) reference = text;
        else
        {
            probe = reader;
            if (probe.TryReadFunction(out var function, out var args) && function.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                CssShapeImageValue.TryReadUrl(ref args, out text)) reference = text;
        }
        if (reference is null)
        {
            if (!CssImageColor.TryRead(ref reader, out var color) || !reader.AtEnd) return false;
            image = new(color!); return true;
        }
        reader = probe;
        CssImageColor? fallback = null;
        if (!reader.AtEnd && (!reader.TryReadComma() || !CssImageColor.TryRead(ref reader, out fallback))) return false;
        if (!reader.AtEnd) return false;
        var valid = CssImageFragment.Read(reference, out var url, out var fragment);
        CssImageValue? source = null;
        // Invalid fragment syntax is a valid CSS image value, but represents an invalid image.
        if (valid && !TryUrl(url, context, out source)) return false;
        image = new(new Annotation(source, fallback, fragment, direction)); return true;
    }
}
