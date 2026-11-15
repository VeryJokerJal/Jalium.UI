using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

internal readonly record struct CssCrossFadePart(Brush? Brush, double Density, double Weight);

/// <summary>CSS Images 4 N-way dissolve in premultiplied sRGB, with live intrinsic dimensions.</summary>
internal sealed class CssCrossFadeLayout(CssCrossFadePart[] parts, CssBackgroundImageResource[] resources, double dpi) : CssImageLayout
{
    private readonly object _gate = new();
    private Size _lastSize;
    private long[]? _lastRevisions;
    private Brush? _lastBrush;

    internal override Size? NaturalSize => IntrinsicSize(CssImageIntrinsicSize.ReplacedDefault).CompleteSize;

    internal override CssImageIntrinsicSize IntrinsicSize(Size fallback)
        => CssImageIntrinsicSize.Mix(parts.Select(part =>
            (CssImageIntrinsicSize.FromBrush(part.Brush, fallback, part.Density), part.Weight)), fallback);

    internal static Size? Intrinsic(Brush? brush, double density)
    {
        return CssImageIntrinsicSize.FromBrush(brush, CssImageIntrinsicSize.ReplacedDefault, density).CompleteSize;
    }

    internal override Brush? Resolve(double width, double height)
    {
        if (!(width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))) return null;
        lock (_gate)
        {
            var revisions = resources.Select(resource => resource.Revision).ToArray();
            if (_lastBrush is not null && _lastSize == new Size(width, height) &&
                _lastRevisions is not null && _lastRevisions.AsSpan().SequenceEqual(revisions)) return _lastBrush;
            var resolved = parts.Select(part => part with { Brush = part.Brush?.CssImageLayout?.Resolve(width, height) ?? part.Brush }).ToArray();
            if (resolved.All(part => part.Brush is null or SolidColorBrush))
            {
                var solid = new float[4];
                foreach (var part in resolved)
                    if (part.Brush is SolidColorBrush color) AddSolid(solid, color.Color, color.Color.A / 255d * color.Opacity * part.Weight);
                return Save(Unpremultiply(solid), 1, 1, width, height, revisions);
            }
            var scale = double.IsFinite(dpi) && dpi > 0 ? dpi : 1;
            scale = Math.Min(scale, 2048 / Math.Max(width, height));
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
            var sum = new float[checked(pixelWidth * pixelHeight * 4)];
            foreach (var part in resolved)
            {
                if (part.Brush is null || part.Weight <= 0) continue;
                var brush = part.Brush;
                if (brush is SolidColorBrush solid)
                {
                    var alpha = solid.Color.A / 255d * solid.Opacity * part.Weight;
                    AddSolid(sum, solid.Color, alpha); continue;
                }
                var target = new RenderTargetBitmap(pixelWidth, pixelHeight,
                    96 * pixelWidth / width, 96 * pixelHeight / height, PixelFormats.Bgra32);
                var drawing = new SoftwareDrawingContext(target);
                drawing.DrawRectangle(brush, null, new Rect(new Size(width, height))); drawing.Close();
                var pixels = target.GetPixelBuffer();
                for (var index = 0; index < sum.Length; index += 4)
                {
                    var alpha = pixels[index + 3] / 255d * part.Weight;
                    sum[index] += (float)(pixels[index] / 255d * alpha);
                    sum[index + 1] += (float)(pixels[index + 1] / 255d * alpha);
                    sum[index + 2] += (float)(pixels[index + 2] / 255d * alpha);
                    sum[index + 3] += (float)alpha;
                }
            }
            return Save(Unpremultiply(sum), pixelWidth, pixelHeight, width, height, revisions);
        }
    }

    private Brush Save(byte[] pixels, int pixelWidth, int pixelHeight, double width, double height, long[] revisions)
    {
        var image = BitmapSource.Create(pixelWidth, pixelHeight, 96 * pixelWidth / width, 96 * pixelHeight / height,
            PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
        var result = new ImageBrush(image) { Stretch = Stretch.Fill };
        if (result.CanFreeze) result.Freeze();
        _lastRevisions = revisions; _lastSize = new(width, height); return _lastBrush = result;
    }

    private static byte[] Unpremultiply(float[] sum)
    {
        var output = new byte[sum.Length];
        for (var index = 0; index < sum.Length; index += 4)
        {
            var alpha = sum[index + 3];
            if (alpha <= 0) continue;
            output[index] = Channel(sum[index] / alpha); output[index + 1] = Channel(sum[index + 1] / alpha);
            output[index + 2] = Channel(sum[index + 2] / alpha); output[index + 3] = Channel(alpha);
        }
        return output;
    }

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    private static void AddSolid(float[] sum, Color color, double alpha)
    {
        var b = (float)(color.B / 255d * alpha); var g = (float)(color.G / 255d * alpha); var r = (float)(color.R / 255d * alpha);
        for (var index = 0; index < sum.Length; index += 4)
        { sum[index] += b; sum[index + 1] += g; sum[index + 2] += r; sum[index + 3] += (float)alpha; }
    }
}
