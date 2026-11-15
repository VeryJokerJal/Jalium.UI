using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

internal enum CssBorderImageRepeat { Stretch, Repeat, Round, Space }
internal readonly record struct CssBorderImageTiling(CssBorderImageRepeat X, CssBorderImageRepeat Y);
internal readonly record struct CssBorderImageMeasure(CssLength Length, bool Number = false, bool Auto = false)
{
    internal double Resolve(in CssLengthContext context, double basis, double multiplier, double automatic)
    {
        if (Auto) return automatic;
        double value;
        if (Length.Expression is { } expression)
        {
            if (!expression.TryEvaluate(context, basis, out value)) return 0;
        }
        else if (Length.Unit == CssUnit.Percent) value = Length.Value * basis / 100;
        else if (Number) value = Length.Value;
        else if (!Length.TryResolve(context, CssPercentBasis.NotSupported, out value)) return 0;
        return CssMathExpression.Censor(Math.Max(0, value) * (Number ? multiplier : 1));
    }
}

internal sealed record CssBorderImageMeasures(CssBorderImageMeasure[] Sides, CssLengthContext Context, bool Fill = false)
{
    internal static CssBorderImageMeasures Uniform(double value, CssUnit unit, bool number = false)
        => new(Enumerable.Repeat(new CssBorderImageMeasure(new(value, unit), number), 4).ToArray(), default);
    internal double At(int side, double basis, double multiplier = 1, double automatic = 0)
        => Sides[side].Resolve(Context, basis, multiplier, automatic);
}

internal sealed record CssBorderImageSource(Brush? Brush, double Density, CssBackgroundImageResource[] Resources,
    CssImageValue? Definition = null, double? Resolution = null)
{
    private readonly object _patchGate = new();
    private readonly Dictionary<Rect, ImageSource> _patches = [];
    private Size _patchImage;
    private double _patchDpi;
    private BitmapScalingMode _patchScaling;
    private FlowDirection _patchDirection;
    private long[]? _patchRevisions;

    internal ImageSource Patch(FrameworkElement visualOwner, DependencyObject owner, Brush source, Size image, Rect crop)
    {
        lock (_patchGate)
        {
            var dpi = VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visualOwner) as Visual ?? visualOwner).DpiScaleY;
            var scaling = CssImageRenderingProperties.Resolve(owner, BitmapScalingMode.Unspecified);
            var direction = (FlowDirection)owner.GetValue(FrameworkElement.FlowDirectionProperty)!;
            var revisions = Resources.Select(resource => resource.Revision).ToArray();
            if (_patchImage != image || _patchDpi != dpi || _patchScaling != scaling || _patchDirection != direction ||
                _patchRevisions is null || !_patchRevisions.AsSpan().SequenceEqual(revisions))
            { _patches.Clear(); _patchImage = image; _patchDpi = dpi; _patchScaling = scaling; _patchDirection = direction; _patchRevisions = revisions; }
            if (_patches.TryGetValue(crop, out var cached)) return cached;
            ImageSource result;
            if (source is SolidColorBrush solid)
            {
                byte[] pixel = [solid.Color.B, solid.Color.G, solid.Color.R, (byte)Math.Clamp(Math.Round(solid.Color.A * solid.Opacity), 0, 255)];
                result = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixel, 4);
            }
            else
            {
                // Isolate raster pixels before magnifying them; otherwise bilinear filtering can
                // blend neighboring source cells across a nominally exact slice boundary.
                var rasterScale = source is ImageBrush { ImageSource: BitmapSource bitmap }
                    ? Math.Max(bitmap.PixelWidth / image.Width, bitmap.PixelHeight / image.Height) : dpi;
                if (!(rasterScale > 0 && double.IsFinite(rasterScale))) rasterScale = 1;
                var scale = Math.Min(rasterScale, 2048 / Math.Max(crop.Width, crop.Height));
                var width = Math.Max(1, (int)Math.Ceiling(crop.Width * scale)); var height = Math.Max(1, (int)Math.Ceiling(crop.Height * scale));
                var target = new RenderTargetBitmap(width, height, 96 * width / crop.Width, 96 * height / crop.Height, PixelFormats.Bgra32);
                var drawing = new SoftwareDrawingContext(target);
                var paint = CssImageRenderingProperties.ResolveBrush(owner, source);
                var fullImage = new Rect(-crop.X, -crop.Y, image.Width, image.Height);
                if (paint is ImageBrush { ImageSource: BitmapSource raster, Stretch: Stretch.Fill, TileMode: TileMode.None,
                    CssImageLayout: null, CssBackgroundLayout: null } imageBrush &&
                    imageBrush.Transform?.Value.IsIdentity != false && imageBrush.RelativeTransform?.Value.IsIdentity != false)
                {
                    // The image already supplies its coverage. Rasterizing an additional
                    // antialiased rectangle can fade a one-pixel image at fractional metrics.
                    drawing.PushOpacity(imageBrush.Opacity);
                    drawing.DrawImage(raster, fullImage, imageBrush.ScalingMode);
                    drawing.Pop();
                }
                else drawing.DrawRectangle(paint, null, fullImage);
                drawing.Close();
                result = target;
            }
            if (result.CanFreeze) result.Freeze();
            if (_patches.Count >= 16) _patches.Clear();
            _patches[crop] = result; return result;
        }
    }

    internal (Brush Brush, Size Pixels, Size? Natural, CssImageIntrinsicSize Intrinsic)? Resolve(FrameworkElement owner, Size area)
    {
        if (Brush is null) return null;
        if (Brush.CssImageLayout is { } generated)
        {
            if (!generated.IsValid) return null;
            var intrinsic = generated.IntrinsicSize(area);
            var dimensions = intrinsic.DefaultSize(area);
            var resolved = generated.Resolve(dimensions.Width, dimensions.Height);
            return resolved is null ? null : (resolved, dimensions, intrinsic.CompleteSize, intrinsic);
        }
        if (Brush is not ImageBrush { ImageSource: { } source }) return null;
        var raw = CssImageIntrinsicSize.FromBrush(Brush, area);
        var size = raw.HasAny ? raw.DefaultSize(area) : (Size?)null;
        if (source is BitmapImage bitmap)
        {
            bitmap.RequestMetadata();
            if (size is not { } imageSize) return null;
            var scale = Math.Min(1, 2048 / Math.Max(imageSize.Width, imageSize.Height));
            bitmap.RequestDecode(Math.Max(1, (int)Math.Ceiling(imageSize.Width * scale)), Math.Max(1, (int)Math.Ceiling(imageSize.Height * scale)), false);
            if (!bitmap.TryGetPixelSnapshot(out _) && bitmap.AnimatedSubstitute is null) return null;
        }
        if (source is not SvgImage && size is null) return null;
        if (!CssImageIntrinsicSize.IsAvailable(source)) return null;
        var natural = raw.AtDensity(Density);
        return (Brush, size ?? area, natural.CompleteSize, natural);
    }
}

internal static class CssBorderImageProperties
{
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs _)
    {
        if (owner is FrameworkElement element) element.SetCssBorderImageInk(owner.GetValue(SourceProperty) is not null);
        Jalium.UI.Controls.CssBorderImagePainter.InvalidateTemplate(owner);
        (owner as UIElement)?.InvalidateVisual();
    }
    internal static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("CssBorderImageSource", typeof(CssBorderImageSource), typeof(CssBorderImageProperties), new PropertyMetadata(null, Changed));
    internal static readonly DependencyProperty SliceProperty = DependencyProperty.RegisterAttached("CssBorderImageSlice", typeof(CssBorderImageMeasures), typeof(CssBorderImageProperties), new PropertyMetadata(CssBorderImageMeasures.Uniform(100, CssUnit.Percent), Changed));
    internal static readonly DependencyProperty WidthProperty = DependencyProperty.RegisterAttached("CssBorderImageWidth", typeof(CssBorderImageMeasures), typeof(CssBorderImageProperties), new PropertyMetadata(CssBorderImageMeasures.Uniform(1, CssUnit.None, true), Changed));
    internal static readonly DependencyProperty OutsetProperty = DependencyProperty.RegisterAttached("CssBorderImageOutset", typeof(CssBorderImageMeasures), typeof(CssBorderImageProperties), new PropertyMetadata(CssBorderImageMeasures.Uniform(0, CssUnit.None, true), Changed));
    internal static readonly DependencyProperty RepeatProperty = DependencyProperty.RegisterAttached("CssBorderImageRepeat", typeof(CssBorderImageTiling), typeof(CssBorderImageProperties), new PropertyMetadata(new CssBorderImageTiling(), Changed));
    internal static readonly DependencyProperty ResourcesProperty = DependencyProperty.RegisterAttached("CssBorderImageResources", typeof(CssBackgroundImageResource[]), typeof(CssBorderImageProperties), new PropertyMetadata(null));

    internal static Thickness Outset(DependencyObject owner, Thickness border)
    {
        if (owner is FrameworkElement element) owner = Jalium.UI.Controls.CssBorderImagePainter.StyleOwner(element);
        if (owner.GetValue(SourceProperty) is not CssBorderImageSource) return default;
        var value = (CssBorderImageMeasures)owner.GetValue(OutsetProperty)!;
        return new(value.At(3, 0, border.Left), value.At(0, 0, border.Top), value.At(1, 0, border.Right), value.At(2, 0, border.Bottom));
    }
}
