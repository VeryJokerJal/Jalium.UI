using Jalium.UI.Interop;

namespace Jalium.UI.Media.Imaging;

internal sealed partial class SoftwareDrawingContext
{
    private void DrawSvgViewportStroke(Pen pen, Geometry geometry)
    {
        if (_opacity <= 0 || pen.Brush is null || pen.Thickness <= 0) return;
        var drawing = new GeometryDrawing(null, pen, geometry);
        var matrix = new TranslateTransform(Offset.X, Offset.Y).Value * _matrix;
        var bounds = new MatrixTransform(matrix).TransformBounds(drawing.Bounds);
        if (bounds.IsEmpty) return;
        bounds.Inflate(1, 1);
        bounds = Rect.Intersect(bounds, _clipBounds);
        var left = (int)Math.Clamp(Math.Floor(bounds.Left), 0, _target.PixelWidth);
        var top = (int)Math.Clamp(Math.Floor(bounds.Top), 0, _target.PixelHeight);
        var right = (int)Math.Clamp(Math.Ceiling(bounds.Right), left, _target.PixelWidth);
        var bottom = (int)Math.Clamp(Math.Ceiling(bounds.Bottom), top, _target.PixelHeight);
        var width = right - left; var height = bottom - top;
        if (width <= 0 || height <= 0) return;
        var group = new DrawingGroup { Transform = new MatrixTransform(matrix) };
        group.Children.Add(drawing);
        // Reuse the vector rasterizer's contour union and analytic coverage.
        // Taking the maximum of individual short-segment SDFs makes a curve's
        // edge depend on its tessellation, even when the chord error is tiny.
        var pixels = SoftwareVectorRasterizer.Rasterize(group, width, height, new Rect(left, top, width, height));
        if (pixels is null) return;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            var alpha = pixels[at + 3] / 255d * _opacity * ClipCoverage(left + x, top + y);
            if (alpha > 0) BlendPixel(left + x, top + y, pixels[at + 2] / 255d, pixels[at + 1] / 255d, pixels[at] / 255d, alpha);
        }
    }
}
