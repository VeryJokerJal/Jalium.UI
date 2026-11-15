namespace Jalium.UI.Media.Imaging;

internal sealed partial class SoftwareDrawingContext
{
    private sealed record MaskCapture(Int32Rect Bounds, byte[] Before, ClipEntry[] Clips, Rect ClipBounds, double Opacity);
    private readonly Stack<MaskCapture> _maskCaptures = new();
    private (Int32Rect Bounds, byte[] Pixels)? _completedMask;

    bool IAlphaMaskDrawingContext.SupportsAlphaMasks => !_closed;
    double IAlphaMaskDrawingContext.AlphaMaskRasterScale => DeviceScale;

    void IAlphaMaskDrawingContext.BeginAlphaMaskCapture(Rect bounds)
    {
        var device = TransformBounds(bounds);
        var x = (int)Math.Clamp(Math.Floor(device.Left), 0, _target.PixelWidth);
        var y = (int)Math.Clamp(Math.Floor(device.Top), 0, _target.PixelHeight);
        var right = (int)Math.Clamp(Math.Ceiling(device.Right), x, _target.PixelWidth);
        var bottom = (int)Math.Clamp(Math.Ceiling(device.Bottom), y, _target.PixelHeight);
        var region = new Int32Rect(x, y, right - x, bottom - y);
        var before = CopyMaskRegion(region);
        _maskCaptures.Push(new(region, before, _clips.ToArray(), _clipBounds, _opacity));
        _completedMask = null;
        _clips.Clear(); _clipBounds = new Rect(x, y, region.Width, region.Height); _opacity = 1;
        var pixels = _target.GetPixelBuffer();
        for (var row = y; row < bottom; row++) Array.Clear(pixels, row * _target.Stride + x * 4, region.Width * 4);
    }

    void IAlphaMaskDrawingContext.EndAlphaMaskCapture()
    {
        if (!_maskCaptures.TryPop(out var frame)) return;
        var content = CopyMaskRegion(frame.Bounds);
        var pixels = _target.GetPixelBuffer();
        for (var row = 0; row < frame.Bounds.Height; row++)
            Buffer.BlockCopy(frame.Before, row * frame.Bounds.Width * 4, pixels,
                (row + frame.Bounds.Y) * _target.Stride + frame.Bounds.X * 4, frame.Bounds.Width * 4);
        _clips.Clear(); _clips.AddRange(frame.Clips); _clipBounds = frame.ClipBounds; _opacity = frame.Opacity;
        _completedMask = (frame.Bounds, content);
    }

    private byte[] CopyMaskRegion(Int32Rect bounds)
    {
        var result = new byte[checked(bounds.Width * bounds.Height * 4)];
        var pixels = _target.GetPixelBuffer();
        for (var row = 0; row < bounds.Height; row++)
            Buffer.BlockCopy(pixels, (row + bounds.Y) * _target.Stride + bounds.X * 4,
                result, row * bounds.Width * 4, bounds.Width * 4);
        return result;
    }

    void IAlphaMaskDrawingContext.ApplyAlphaMask(byte[] coverage, int width, int height, Rect maskBounds, Rect captureBounds)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        if (width <= 0 || height <= 0 || (long)width * height > coverage.Length)
            throw new ArgumentException("Invalid alpha mask dimensions.", nameof(coverage));
        if (_completedMask is not { } frame || !_matrix.HasInverse) return;
        var inverse = _matrix; inverse.Invert();
        for (var row = 0; row < frame.Bounds.Height; row++)
        for (var column = 0; column < frame.Bounds.Width; column++)
        {
            var x = column + frame.Bounds.X; var y = row + frame.Bounds.Y;
            // Keep pixels that intersect a fractional clip edge. ClipCoverage
            // supplies the partial coverage after this coarse bounds rejection.
            if (x + 1 <= _clipBounds.Left || x >= _clipBounds.Right ||
                y + 1 <= _clipBounds.Top || y >= _clipBounds.Bottom) continue;
            var point = inverse.Transform(new Point(x + .5, y + .5));
            var alpha = Styling.CssMaskSampling.Sample(coverage, width, height, maskBounds, point);
            var offset = (row * frame.Bounds.Width + column) * 4;
            BlendPixel(x, y, frame.Pixels[offset + 2] / 255d, frame.Pixels[offset + 1] / 255d,
                frame.Pixels[offset] / 255d, frame.Pixels[offset + 3] / 255d * alpha * _opacity * ClipCoverage(x, y));
        }
        _completedMask = null;
    }
}
