using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>An image used for layout, without changing the float's displayed source or clip.</summary>
internal sealed class CssShapeImageValue(CssShapeImageResource? resource, Brush? gradient,
    Func<Size, CssShapeImagePixels?>? composite = null, CssShapeImageResource[]? resources = null, double? resolution = null)
{
    internal CssShapeImageResource? Resource => resource;
    internal CssShapeImageResource[] Resources { get; } = resources ?? (resource is null ? [] : [resource]);
    internal bool UsesResource(CssShapeImageResource value) => Resources.Contains(value);
    private readonly Dictionary<Size, CssShapeImagePixels> _gradients = [];
    private CssShapeImagePixels? _densitySource, _densityPixels;

    internal CssFloatShape? Resolve(UIElement element, Size borderSize, Thickness margin,
        double containingWidth, double distance, double threshold)
    {
        if (element is not FrameworkElement framework) return null;
        var insets = CssBoxMetrics.ContentInsets(framework, containingWidth);
        var content = new Rect(margin.Left + insets.Left, margin.Top + insets.Top,
            Math.Max(0, borderSize.Width - insets.Left - insets.Right),
            Math.Max(0, borderSize.Height - insets.Top - insets.Bottom));
        if (content.Width <= 0 || content.Height <= 0) return CssFloatShape.FromPixels(Empty, threshold, content, borderSize, distance);
        var pixels = ResolvePixels(content.Size);
        if (pixels is null) return null; // Pending or invalid images behave as shape-outside:none.
        var image = content;
        if (element is Image imageElement)
        {
            image = imageElement.TryGetSourceSize(out _, out var own)
                ? imageElement.ReplacedContentRect(own, content)
                : imageElement.ReplacedContentRect(pixels.Intrinsic.DefaultSize(content.Size), content, pixels.Intrinsic);
            // SVG media, viewBox and percentage coordinates use the actual
            // object viewport. Rasterizing at the content box and squeezing
            // those pixels into a contain rectangle would apply fitting twice.
            if (image.Size != content.Size && image.Width > 0 && image.Height > 0)
                pixels = ResolvePixels(image.Size);
            if (pixels is null) return null;
        }
        return CssFloatShape.FromPixels(pixels, threshold, image,
            new(Math.Max(0, borderSize.Width + margin.Left + margin.Right),
                Math.Max(0, borderSize.Height + margin.Top + margin.Bottom)), distance);
    }

    internal CssShapeImagePixels? ResolvePixels(Size size)
    {
        if (composite is not null) return composite(size);
        var pixels = resource?.Resolve(size);
        if (resource is null && gradient is not null)
        {
            if (!_gradients.TryGetValue(size, out pixels))
            {
                pixels = Rasterize(gradient, size);
                if (pixels is not null)
                {
                    if (_gradients.Count >= 8) _gradients.Clear();
                    _gradients[size] = pixels;
                }
            }
        }
        if (pixels is not null && resolution is { } density && pixels.RasterDensity.HasValue)
        {
            if (!ReferenceEquals(pixels, _densitySource))
            { _densitySource = pixels; _densityPixels = pixels.WithResolution(density); }
            return _densityPixels;
        }
        return pixels;
    }

    private static readonly CssShapeImagePixels Empty = new(
        new([], 0, 0, 0, PixelFormats.Bgra32, null), default);

    internal static CssShapeImagePixels? Rasterize(Brush brush, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || !double.IsFinite(size.Width) || !double.IsFinite(size.Height)) return null;
        var scale = Math.Min(1, 2048 / Math.Max(size.Width, size.Height));
        var width = (int)Math.Max(1, Math.Ceiling(size.Width * scale));
        var height = (int)Math.Max(1, Math.Ceiling(size.Height * scale));
        var target = new RenderTargetBitmap(width, height, 96 * width / size.Width, 96 * height / size.Height, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(target);
        drawing.DrawRectangle(brush, null, new Rect(size)); drawing.Close();
        return CssShapeImagePixels.FromBitmap(target).WithIntrinsic(CssImageIntrinsicSize.FromBrush(brush, size), size);
    }

    internal static CssCompiledValue? Parse(ref CssTokenReader reader, CssCompileContext context)
    {
        var probe = reader;
        if (!CssImageValue.TryRead(ref probe, context, out var image) || !probe.AtEnd) return null;
        reader = probe;
        return new Compiled(image!);
    }

    internal static bool TryReadUrl(ref CssTokenReader args, out string reference)
    {
        if (args.TryReadString(out reference!)) return args.AtEnd;
        var raw = args.Remaining.Trim(); var decoded = new StringBuilder(raw.Length);
        for (var index = 0; index < raw.Length;)
        {
            if (raw[index] == '\\')
            {
                if (!CssSyntax.ReadEscape(raw, ref index, out var escaped)) { reference = ""; return false; }
                decoded.Append(escaped); continue;
            }
            var character = raw[index++];
            if (char.IsWhiteSpace(character) || character < 0x20 || character is '(' or ')' or '\'' or '"')
            { reference = ""; return false; }
            decoded.Append(character);
        }
        reference = decoded.ToString(); return true;
    }

    internal static CssCompiledValue? ParseThreshold(ref CssTokenReader reader, CssCompileContext context)
    {
        var probe = reader;
        if (probe.TryReadFunction(out var name, out var args))
        {
            var expression = CssMathExpression.ParseNumberPercentage(name.ToString(), args.Remaining);
            if (!probe.AtEnd || expression?.Kind is not (CssNumericKind.Number or CssNumericKind.Percent)) return null;
            reader = probe; return new ThresholdValue(expression);
        }
        if (!reader.TryReadNumber(out var number, out var unit) || !reader.AtEnd ||
            unit is not (CssUnit.None or CssUnit.Percent) || !double.IsFinite(number)) return null;
        return new CssImmediateValue(CssShapeOutsideProperties.ThresholdProperty,
            Math.Clamp(unit == CssUnit.Percent ? number / 100 : number, 0, 1));
    }

    private sealed class ThresholdValue(CssMathExpression expression) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            expression.ObserveContainerDependencies(context.Lengths);
            if (!expression.TryEvaluate(context.Lengths, 1, out var value)) return false;
            sink.Set(CssShapeOutsideProperties.ThresholdProperty, Math.Clamp(value, 0, 1)); return true;
        }
    }

    private sealed class Compiled(CssImageValue image) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var selected = image.Select(context);
            sink.Set(CssShapeOutsideProperties.ValueProperty, selected?.Image.Shape(context, selected.Value.Resolution)); return true;
        }
    }
}
