using System.Buffers.Binary;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>Live URL fallback, spatial cropping and bidi annotations, independent of the consuming property.</summary>
internal sealed class CssAnnotatedImageLayout(Brush? source, double density, SolidColorBrush? fallback,
    CssImageFragment? fragment, FlowDirection? direction, CssNode owner, CssBackgroundImageResource[] resources) : CssImageLayout
{
    private readonly object _gate = new();
    private Size _lastSize;
    private bool _lastFlip;
    private BitmapScalingMode _lastScaling;
    private double _lastDpi;
    private long[]? _lastRevisions;
    private Brush? _lastBrush;
    internal override bool IsValid => SourceRect(CssImageIntrinsicSize.ReplacedDefault) is not null || fallback is not null;
    internal override Size? NaturalSize => IntrinsicSize(CssImageIntrinsicSize.ReplacedDefault).CompleteSize;
    internal override CssImageIntrinsicSize IntrinsicSize(Size context)
    {
        if (SourceRect(context) is not { } rect) return default;
        return fragment is null ? CssImageIntrinsicSize.FromBrush(source, context, density)
            : CssImageIntrinsicSize.FromSize(new(rect.Width / density, rect.Height / density));
    }
    private static bool Flip(FlowDirection? direction, CssNode owner) => direction is { } tag &&
        owner.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection actual && actual != tag;

    private Rect? SourceRect(Size context)
    {
        var dimensions = CssImageIntrinsicSize.FromBrush(source, context);
        Size? natural = dimensions.HasAny ? dimensions.DefaultSize(context) : null;
        if (source is ImageBrush { ImageSource: BitmapImage bitmap })
        {
            bitmap.RequestMetadata();
            // An annotated image with no fallback must also start decoding while its content box is invalid.
            // Wait for dimensions before requesting a raster: a one-pixel seed decode can blend an
            // opaque sprite with its transparent neighbor and publish an incorrect first visible crop.
            if (natural is not { } size) return null;
            var scale = Math.Min(1, 2048 / Math.Max(size.Width, size.Height));
            bitmap.RequestDecode(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)), false);
            if (!bitmap.TryGetPixelSnapshot(out _) && bitmap.AnimatedSubstitute is null) return null;
        }
        // A successfully parsed dimensionless SVG is a valid image, including an empty transparent SVG.
        // It keeps dimensionless sizing, but needs the default object viewport for spatial coordinates.
        if (natural is null && source is ImageBrush { ImageSource: SvgImage } svg && CssImageIntrinsicSize.IsAvailable(svg.ImageSource!)) natural = context;
        if (natural is not { } sourceSize) return null;
        return fragment is { } crop && (crop.Percent || !resources.Any(resource => resource.HasMultipleResolutions))
            ? crop.Resolve(sourceSize) : new Rect(sourceSize);
    }

    internal override Brush? Resolve(double width, double height)
    {
        lock (_gate) return ResolveCore(width, height);
    }

    private Brush? ResolveCore(double width, double height)
    {
        if (!(width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))) return null;
        if (SourceRect(new(width, height)) is not { } crop) return fallback;
        var flip = Flip(direction, owner);
        var scaling = CssImageRenderingProperties.Resolve(owner.Target, BitmapScalingMode.Unspecified);
        var paint = CssImageRenderingProperties.ResolveBrush(owner.Target, source);
        if (fragment is null && !flip) return paint;
        var visual = owner.VisualOwner;
        var dpi = visual is null ? 1 : VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visual) as Visual ?? visual).DpiScaleY;
        var revisions = resources.Select(resource => resource.Revision).ToArray();
        if (_lastBrush is not null && _lastSize == new Size(width, height) && _lastFlip == flip && _lastScaling == scaling && _lastDpi == dpi &&
            _lastRevisions is not null && _lastRevisions.AsSpan().SequenceEqual(revisions)) return _lastBrush;
        var natural = CssImageIntrinsicSize.FromBrush(source, new(width, height)).DefaultSize(new(width, height));
        if (fragment is not null && paint is ImageBrush { ImageSource: SvgImage svg } vector)
        {
            // A spatial fragment selects source coordinates before the final
            // nonuniform scale. Do not negotiate a second, differently shaped
            // viewBox merely because the selected fragment is being stretched.
            var copy = (ImageBrush)vector.CloneCurrentValue();
            copy.ImageSource = svg.ImageForViewport(natural);
            paint = copy;
        }
        var scale = Math.Min(double.IsFinite(dpi) && dpi > 0 ? dpi : 1, 2048 / Math.Max(width, height));
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale)); var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var target = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * pixelWidth / width, 96 * pixelHeight / height, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(target);
        if (flip) drawing.PushTransform(new MatrixTransform(new Matrix(-1, 0, 0, 1, width, 0)));
        drawing.DrawRectangle(paint, null, fragment is null ? new Rect(0, 0, width, height)
            : new(-crop.X * width / crop.Width, -crop.Y * height / crop.Height,
                natural.Width * width / crop.Width, natural.Height * height / crop.Height));
        if (flip) drawing.Pop();
        drawing.Close();
        var result = new ImageBrush(target) { Stretch = Stretch.Fill, ScalingMode = scaling };
        if (result.CanFreeze) result.Freeze();
        _lastSize = new(width, height); _lastFlip = flip; _lastScaling = scaling; _lastDpi = dpi; _lastRevisions = revisions;
        return _lastBrush = result;
    }

    internal static CssShapeImageValue Shape(CssShapeImageValue? source, SolidColorBrush? fallback,
        CssImageFragment? fragment, FlowDirection? direction, CssNode owner)
    {
        CssShapeImagePixels? previousSource = null, previous = null;
        Size previousSize = default; bool previousFlip = false;
        CssShapeImagePixels? Resolve(Size size)
        {
            if (!(size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width) && double.IsFinite(size.Height))) return null;
            var pixels = source?.ResolvePixels(size);
            var flip = Flip(direction, owner);
            if (previous is not null && previousSize == size && previousFlip == flip && ReferenceEquals(previousSource, pixels)) return previous;
            if (pixels is null) return fallback is null ? null : CssShapeImageValue.Rasterize(fallback, new Size(1, 1));
            if (fragment is null && !flip) return pixels;
            var original = pixels;
            // Spatial fragments address the source before image-set density
            // correction and before the final, possibly nonuniform output fit.
            var natural = pixels.RawIntrinsic.DefaultSize(size);
            var cropped = fragment is { } spatial && (spatial.Percent || !source!.Resources.Any(resource => resource.HasMultipleResolutions));
            var crop = cropped ? fragment!.Resolve(natural) : new Rect(natural);
            if (cropped)
            {
                if (!(natural.Width > 0 && natural.Height > 0 && crop.Width > 0 && crop.Height > 0))
                    return new(new(new byte[4], 1, 1, 4, PixelFormats.Bgra32, null), size, CssImageIntrinsicSize.FromSize(default));
                pixels = source!.ResolvePixels(natural);
                if (pixels is null) return fallback is null ? null : CssShapeImageValue.Rasterize(fallback, new Size(1, 1));
            }
            var scale = Math.Min(1, 2048 / Math.Max(size.Width, size.Height));
            var width = Math.Max(1, (int)Math.Ceiling(size.Width * scale)); var height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
            var bytes = new byte[checked(width * height * 16)];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var alpha = cropped
                    ? pixels.AlphaAt((crop.X + (flip ? width - x - .5 : x + .5) * crop.Width / width) / natural.Width,
                        (crop.Y + (y + .5) * crop.Height / height) / natural.Height)
                    : pixels.Alpha(flip ? width - 1 - x : x, y, width, height);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((y * width + x) * 16 + 12), (float)alpha);
            }
            previousSource = original; previousSize = size; previousFlip = flip;
            return previous = new(new(bytes, width, height, width * 16, PixelFormats.Rgba128Float, null), size,
                cropped ? CssImageIntrinsicSize.FromSize(crop.Size) : original.RawIntrinsic, original.RasterDensity);
        }
        return new(null, null, Resolve, source?.Resources ?? []);
    }
}
