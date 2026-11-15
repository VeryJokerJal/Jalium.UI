namespace Jalium.UI.Media.Imaging;

internal sealed partial class SoftwareDrawingContext : IBlendDrawingContext
{
    bool IBlendDrawingContext.SupportsBlendModes => !_closed;
    void IBlendDrawingContext.BeginBlendCapture(Rect bounds) => ((IAlphaMaskDrawingContext)this).BeginAlphaMaskCapture(bounds);
    void IBlendDrawingContext.EndBlendCapture() => ((IAlphaMaskDrawingContext)this).EndAlphaMaskCapture();
    void IBlendDrawingContext.CompositeBlendCapture(CssBlendMode mode, Rect bounds)
    {
        if (_completedMask is not { } frame) return; _completedMask = null;
        var pixels = _target.GetPixelBuffer();
        for (var row = 0; row < frame.Bounds.Height; row++) for (var column = 0; column < frame.Bounds.Width; column++)
        {
            var x = column + frame.Bounds.X; var y = row + frame.Bounds.Y;
            if (x + 1 <= _clipBounds.Left || x >= _clipBounds.Right || y + 1 <= _clipBounds.Top || y >= _clipBounds.Bottom) continue;
            var offset = (row * frame.Bounds.Width + column) * 4;
            CssBlend.Composite(frame.Pixels.AsSpan(offset, 4), pixels.AsSpan(y * _target.Stride + x * 4, 4), mode, _opacity * ClipCoverage(x, y));
        }
    }
}
