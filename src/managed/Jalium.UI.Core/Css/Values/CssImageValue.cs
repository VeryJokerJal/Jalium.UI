using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>A parsed CSS image. Selection precedes fetching and is shared by paint and shape layout.</summary>
internal sealed partial class CssImageValue : IEquatable<CssImageValue>
{
    private string _syntax = "";
    private Uri? _baseUri;
    private ICssResourceResolver? _resolverIdentity;
    public bool Equals(CssImageValue? other) => other is not null && _syntax == other._syntax &&
        Equals(_baseUri, other._baseUri) && ReferenceEquals(_resolverIdentity, other._resolverIdentity);
    public override bool Equals(object? other) => other is CssImageValue image && Equals(image);
    public override int GetHashCode() => HashCode.Combine(_syntax, _baseUri,
        _resolverIdentity is null ? 0 : RuntimeHelpers.GetHashCode(_resolverIdentity));

    internal bool EqualsForStyleQuery(CssImageValue other) => _syntax == other._syntax &&
        (!UsesUrl && !other.UsesUrl || Equals(_baseUri, other._baseUri));
    private bool UsesUrl => _background is not null || _options?.Any(option => option.Image.UsesUrl) == true ||
        _annotation?.Image?.UsesUrl == true || _crossFade?.Any(option => option.Image?.UsesUrl == true) == true;
    private readonly Brush? _gradient;
    private readonly CssBackgroundImageResource? _background;
    private readonly CssBackgroundImageResource? _mask;
    private readonly CssBackgroundImageResource? _maskReference;
    internal bool DirectUrl => _background is not null;
    private readonly CssShapeImageResource? _shape;
    private readonly Option[]? _options;
    private sealed record Option(CssImageValue Image, CssMathExpression? Resolution, double Density, bool Explicit, string? Type);
    internal readonly record struct Selection(CssImageValue Image, double? Resolution);

    private CssImageValue(Brush gradient)
    {
        _gradient = gradient;
        if (gradient.CanFreeze) gradient.Freeze();
    }

    private CssImageValue(Uri? uri, CssCompileContext context)
    {
        var resolver = context.ResourceResolver ?? CssStyleSheet.DefaultResolver;
        _background = new(uri, resolver);
        _mask = new(uri, resolver, context.BaseUri, requirePixelAccess: true);
        _maskReference = new(uri, resolver, context.BaseUri, requirePixelAccess: true, allowSvgMaskReference: true);
        _shape = new(uri, context.BaseUri, resolver);
    }

    private CssImageValue(Option[] options) => _options = options;

    internal static bool TryRead(ref CssTokenReader reader, CssCompileContext context,
        out CssImageValue? image, bool insideSet = false, int depth = 0)
    {
        image = null;
        if (depth > 32) return false;
        var original = reader.Remaining; var start = reader.Position;
        var probe = reader;
        if (insideSet && probe.TryReadString(out var reference))
        {
            if (!TryUrl(reference, context, out image)) return false;
            image!.Identity(original[..(probe.Position - start)].ToString(), context);
            reader = probe; return true;
        }
        probe = reader;
        if (!probe.TryReadFunction(out var name, out var args)) return false;
        if (name.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            if (!CssShapeImageValue.TryReadUrl(ref args, out reference) || !TryUrl(reference, context, out image)) return false;
        }
        else if (name.Equals("image-set", StringComparison.OrdinalIgnoreCase) || name.Equals("-webkit-image-set", StringComparison.OrdinalIgnoreCase))
        {
            if (insideSet) return false;
            var options = new List<Option>();
            do
            {
                if (!args.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty) return false;
                var option = new CssTokenReader(segment, reader.NumericContext);
                if (!TryRead(ref option, context, out var candidate, insideSet: true, depth: depth + 1)) return false;
                CssMathExpression? resolution = null; var density = 1d; var explicitResolution = false; string? type = null;
                while (!option.AtEnd)
                {
                    var descriptor = option;
                    if (descriptor.TryReadFunction(out var function, out var arguments))
                    {
                        if (function.Equals("type", StringComparison.OrdinalIgnoreCase))
                        {
                            if (type is not null || !arguments.TryReadString(out type) || !arguments.AtEnd) return false;
                            option = descriptor; continue;
                        }
                        var expression = CssMathExpression.Parse(function.ToString(), arguments.Remaining);
                        if (explicitResolution || expression?.Kind != CssNumericKind.Resolution || expression.Type.PercentHint is not null) return false;
                        resolution = expression; explicitResolution = true; option = descriptor; continue;
                    }
                    if (explicitResolution || !option.TryReadNumber(out var number, out var unit) ||
                        !CssUnitConversion.TryToDppx(number, unit, out density)) return false;
                    explicitResolution = true;
                }
                options.Add(new(candidate!, resolution, density, explicitResolution, type));
                if (!args.TryReadComma()) break;
                if (args.AtEnd) return false;
            } while (true);
            if (!args.AtEnd || options.Count == 0) return false;
            image = new(options.ToArray());
        }
        else if (name.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadAnnotated(ref args, context, out image)) return false;
        }
        else if (name.Equals("cross-fade", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("-webkit-cross-fade", StringComparison.OrdinalIgnoreCase))
        {
            var crossFadeArgs = args;
            if (!TryReadCrossFade(ref crossFadeArgs, context, insideSet, depth + 1, out image) &&
                !TryReadLegacyCrossFade(ref args, context, insideSet, depth + 1, out image))
            {
                return false;
            }
        }
        else if (name.Equals("element", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("-moz-element", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadElementFunction(ref args, out image)) return false;
        }
        else if (name.Equals("paint", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadPaintFunction(ref args, out image)) return false;
        }
        else
        {
            if (!CssGradientParser.TryParseGradientFunction(name, ref args, out var gradient) || gradient is null) return false;
            image = new(gradient);
        }
        image!.Identity(original[..(probe.Position - start)].ToString(), context);
        reader = probe; return true;
    }

    private void Identity(string syntax, CssCompileContext context)
    { _syntax = syntax; _baseUri = context.BaseUri; _resolverIdentity = context.ResourceResolver ?? CssStyleSheet.DefaultResolver; }

    private static bool TryUrl(string reference, CssCompileContext context, out CssImageValue? image)
    {
        image = null;
        var hashIndex = reference.IndexOf('#');
        if (hashIndex >= 0 && reference[(hashIndex + 1)..].StartsWith("xywh=", StringComparison.OrdinalIgnoreCase))
        {
            if (CssImageFragment.Read(reference, out var baseUrl, out var fragment) && fragment is not null)
            {
                if (!TryUrl(baseUrl, context, out var baseSource)) return false;
                image = new(new Annotation(baseSource, null, fragment, null));
                return true;
            }
            image = new(new Annotation(null, null, null, null));
            return true;
        }
        try
        {
            var uri = reference.Length == 0 ? null : context.BaseUri is { } baseUri
                ? CssStyleSheet.ResolveReference(baseUri, reference) : new Uri(reference, UriKind.RelativeOrAbsolute);
            image = new(uri, context); return true;
        }
        catch (UriFormatException) { return false; }
    }

    internal Selection? Select(in CssApplyContext context)
    {
        if (_options is null) return new(this, null);
        // BuildLengthContext already observes host DPI transitions, including reparenting.
        var root = context.Element.VisualOwner;
        var scale = root is null ? 1 : VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(root) as Visual ?? root).DpiScaleY;
        if (!(scale > 0 && double.IsFinite(scale))) scale = 1;
        var seen = new HashSet<double>();
        Option? chosen = null; var chosenDensity = 0d;
        foreach (var option in _options)
        {
            if (option.Type is { } type && !CssImageTypeSupport.IsSupported(type)) continue;
            var density = option.Density;
            if (option.Resolution is { } expression)
            {
                expression.ObserveContainerDependencies(context.Lengths);
                if (!expression.TryEvaluate(context.Lengths, 0, out density)) return null;
                density = Math.Max(0, density);
            }
            // Type filtering happens before first-resolution-wins de-duplication.
            if (!seen.Add(density)) continue;
            if (chosen is null || density >= scale && (chosenDensity < scale || density < chosenDensity) ||
                density < scale && chosenDensity < scale && density > chosenDensity)
            { chosen = option; chosenDensity = density; }
        }
        // A zero resolution is valid syntax but represents an invalid image.
        return chosen is null || chosenDensity == 0 ? null : new(chosen.Image, chosen.Explicit ? chosenDensity : null);
    }

    internal Brush? Background(in CssApplyContext context, double? resolution, out CssBackgroundImageResource? resource,
        List<CssBackgroundImageResource>? resources = null, bool mask = false, bool maskReference = false)
    {
        var used = resources ?? [];
        var part = Materialize(context, context.Element, false, resolution, used, mask: mask, maskReference: maskReference);
        var definition = mask ? maskReference ? _maskReference : _mask : _background;
        resource = definition is null ? null : used.FirstOrDefault(definition.Matches);
        if (part.Brush is not ImageBrush brush) return part.Brush;
        // Procedural conic gradients are frozen by Gradient. Give the background
        // its own writable brush before attaching consumer-specific layout.
        // Resource-backed brushes stay live with their original image source.
        if (brush.IsFrozen) brush = brush.Clone();
        brush.CssBackgroundLayout = new(CssBackgroundSizeMode.Auto, CssBackgroundRepeat.Both, naturalResolution: part.Density);
        return brush;
    }

    private static double NaturalResolution(ImageSource image)
        => image is BitmapSource bitmap && bitmap.DpiX > 0 && double.IsFinite(bitmap.DpiX) ? bitmap.DpiX / 96 : 1;

    internal CssShapeImageValue Shape(in CssApplyContext context, double? resolution = null)
        => ShapeValue(context, resolution: resolution);

    internal CssBorderImageSource Border(in CssApplyContext context, double? resolution, List<CssBackgroundImageResource> resources, bool mask = false)
    {
        var materialized = Materialize(context, context.Element, false, resolution, resources, border: true, mask: mask);
        return new(materialized.Brush, materialized.Density, resources.ToArray(), this, resolution);
    }

    internal Brush? Content(in CssApplyContext context, CssGeneratedImage consumer, double? resolution,
        out CssBackgroundImageResource? resource, out double density, List<CssBackgroundImageResource>? resources = null)
    {
        var used = resources ?? [];
        var part = Materialize(context, CssNode.Get(consumer), true, resolution, used);
        resource = _background is null ? null : used.FirstOrDefault(_background.Matches);
        density = part.Density; return part.Brush;
    }

    private Brush Gradient(in CssApplyContext context)
    {
        if (_customImage is not null)
            return _customImage.MaterializeBrush(context);
        var brush = _gradient!.Clone();
        if (brush.CssGradientLayout is { } layout)
        {
            if (layout.DependsOnCurrentColor) CssColorBrushObserver.Observe(context.Element, context.CurrentColor as SolidColorBrush);
            brush.CssGradientLayout = layout.ForElement(context.Lengths, context.CurrentColorValue);
        }
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
