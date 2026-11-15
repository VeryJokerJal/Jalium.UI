using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>Immutable CPU pixels, retaining native alpha precision until thresholding.</summary>
internal sealed class CssShapeImagePixels(BitmapPixelOperations.PixelBuffer pixels, Size naturalSize,
    CssImageIntrinsicSize? intrinsic = null, double? rasterDensity = null)
{
    internal const int MaximumPixels = 16_777_216;
    internal int Width => pixels.Width;
    internal int Height => pixels.Height;
    internal CssImageIntrinsicSize RawIntrinsic => intrinsic ?? CssImageIntrinsicSize.FromSize(naturalSize);
    internal CssImageIntrinsicSize Intrinsic => RawIntrinsic.AtDensity(rasterDensity ?? 1);
    internal double? RasterDensity => rasterDensity;
    internal Size NaturalSize => Intrinsic.DefaultSize(naturalSize);
    internal CssShapeImagePixels WithIntrinsic(CssImageIntrinsicSize value, Size? fallback = null)
        => new(pixels, fallback ?? naturalSize, value);
    internal CssShapeImagePixels WithResolution(double resolution) => rasterDensity.HasValue
        ? new(pixels, naturalSize, RawIntrinsic, resolution) : this;
    internal double AlphaAt(double x, double y) => Width > 0 && Height > 0
        ? BitmapPixelOperations.ReadAlpha(pixels, Math.Clamp((int)(x * Width), 0, Width - 1),
            Math.Clamp((int)(y * Height), 0, Height - 1)) : 0;
    private readonly Dictionary<double, int[][]> _runs = [];
    internal double Alpha(int x, int y, int width, int height) => Width > 0 && Height > 0
        ? BitmapPixelOperations.ReadAlpha(pixels, Math.Clamp((int)((x + .5) * Width / width), 0, Width - 1),
            Math.Clamp((int)((y + .5) * Height / height), 0, Height - 1)) : 0;

    internal static CssShapeImagePixels FromBitmap(BitmapSource source)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 ||
            (long)source.PixelWidth * source.PixelHeight > MaximumPixels)
            throw new InvalidDataException("CSS shape image exceeds the pixel budget");
        return new(BitmapPixelOperations.Read(source), new(source.Width, source.Height),
            CssImageIntrinsicSize.FromSource(source), source.DpiX > 0 && double.IsFinite(source.DpiX) ? source.DpiX / 96 : 1);
    }

    internal int[][] Runs(double threshold)
    {
        lock (_runs)
        {
            if (_runs.TryGetValue(threshold, out var cached)) return cached;
            var rows = new int[Height][];
            for (var y = 0; y < Height; y++)
            {
                var spans = new List<int>(); var start = -1;
                for (var x = 0; x <= Width; x++)
                {
                    var opaque = x < Width && BitmapPixelOperations.ReadAlpha(pixels, x, y) > threshold;
                    if (opaque && start < 0) start = x;
                    else if (!opaque && start >= 0) { spans.Add(start); spans.Add(x); start = -1; }
                }
                rows[y] = spans.ToArray();
            }
            if (_runs.Count >= 4) _runs.Clear();
            return _runs[threshold] = rows;
        }
    }
}
