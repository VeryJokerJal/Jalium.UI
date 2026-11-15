using Jalium.UI.Media;

namespace Jalium.UI.Interop;

internal static partial class SoftwareVectorRasterizer
{
    private static void RenderIsolatedGroup(DrawingGroup group, in SoftwareRenderContext parent)
    {
        var local = group.LocalBounds;
        if (local.IsEmpty || local.Width <= 0 || local.Height <= 0 || !parent.Matrix.HasInverse) return;
        var corners = new[] { new Point(local.Left, local.Top), new Point(local.Right, local.Top),
            new Point(local.Left, local.Bottom), new Point(local.Right, local.Bottom) }.Select(parent.Matrix.Transform).ToArray();
        if (corners.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return;
        var left = (int)Math.Clamp(Math.Floor(corners.Min(point => point.X)), 0, parent.Width);
        var top = (int)Math.Clamp(Math.Floor(corners.Min(point => point.Y)), 0, parent.Height);
        var right = (int)Math.Clamp(Math.Ceiling(corners.Max(point => point.X)), left, parent.Width);
        var bottom = (int)Math.Clamp(Math.Ceiling(corners.Max(point => point.Y)), top, parent.Height);
        var extent = new Rect(0, 0, right - left, bottom - top);
        var size = DrawingGroup.RasterSize(extent, 1);
        if (size.Width == 0) return;
        var width = size.Width; var height = size.Height;
        var sx = width / extent.Width; var sy = height / extent.Height;
        var matrix = parent.Matrix;
        matrix = Matrix.Multiply(matrix, new Matrix(sx, 0, 0, sy, -left * sx, -top * sy));
        var source = new byte[checked(width * height * 4)];
        var context = new SoftwareRenderContext(source, width, height, width * 4, matrix, parent.TouchedSources);
        foreach (var child in group.Children) if (child is not null) RenderDrawing(child, context);
        // Clip and opacity belong to the completed group. Applying them to each
        // primitive would make overlapping content exceed an antialiased clip or
        // accumulate opacity more than once.
        var clip = group.ClipGeometry is { } geometry ? ApplyClip(context, geometry).ClipMask : null;
        byte[]? maskPixels = null;
        using var scope = group.SvgMask?.Enter();
        if (group.SvgMask is { } binding && scope is not null)
        {
            var drawing = binding.Definition!.Resolve(binding.ObjectBounds, binding.Viewport);
            if (drawing is not null)
            {
                maskPixels = new byte[source.Length];
                RenderDrawing(drawing, new SoftwareRenderContext(maskPixels, width, height, width * 4, matrix, parent.TouchedSources));
            }
        }
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var px = Math.Clamp((x - left + .5) * sx - .5, 0, width - 1d);
            var py = Math.Clamp((y - top + .5) * sy - .5, 0, height - 1d);
            var color = GroupSample(source, width, height, px, py);
            var alpha = color.A * Math.Clamp(group.Opacity, 0, 1) * parent.Opacity;
            if (clip is not null) alpha *= MaskSample(clip, width, height, px, py);
            if (group.SvgMask is { } mask)
            {
                if (maskPixels is null) continue;
                var value = GroupSample(maskPixels, width, height, px, py);
                alpha *= mask.Alpha(value.R, value.G, value.B, value.A);
            }
            if (parent.ClipMask is { } inherited) alpha *= inherited[y * parent.Width + x] / 255d;
            var a = (byte)Math.Clamp(Math.Round(alpha), 0, 255);
            if (a > 0)
            {
                var destination = (y * parent.Stride + x * 4);
                var blended = CssBlend.Composite(Color.FromArgb(a, color.R, color.G, color.B),
                    Color.FromArgb(parent.Pixels[destination+3], parent.Pixels[destination+2],
                        parent.Pixels[destination+1], parent.Pixels[destination]), group.BlendMode);
                parent.Pixels[destination] = blended.B;
                parent.Pixels[destination+1] = blended.G;
                parent.Pixels[destination+2] = blended.R;
                parent.Pixels[destination+3] = blended.A;
            }
        }
    }

    private static double MaskSample(byte[] pixels, int width, int height, double x, double y)
    {
        var x0 = (int)x; var y0 = (int)y; var x1 = Math.Min(width - 1, x0 + 1); var y1 = Math.Min(height - 1, y0 + 1);
        var fx = x - x0; var fy = y - y0;
        return ((pixels[y0 * width + x0] * (1 - fx) + pixels[y0 * width + x1] * fx) * (1 - fy) +
            (pixels[y1 * width + x0] * (1 - fx) + pixels[y1 * width + x1] * fx) * fy) / 255d;
    }
    private static (byte B, byte G, byte R, byte A) GroupSample(byte[] pixels, int width, int height, double x, double y)
    {
        var x0 = (int)x; var y0 = (int)y; var fx = x - x0; var fy = y - y0;
        double a = 0, b = 0, g = 0, r = 0;
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 2; column++)
        {
            var at = (Math.Min(height - 1, y0 + row) * width + Math.Min(width - 1, x0 + column)) * 4;
            var weight = (column == 0 ? 1 - fx : fx) * (row == 0 ? 1 - fy : fy) * pixels[at + 3] / 255d;
            a += weight; b += pixels[at] * weight; g += pixels[at + 1] * weight; r += pixels[at + 2] * weight;
        }
        return a <= 0 ? default : ((byte)Math.Clamp(Math.Round(b / a), 0, 255), (byte)Math.Clamp(Math.Round(g / a), 0, 255),
            (byte)Math.Clamp(Math.Round(r / a), 0, 255), (byte)Math.Clamp(Math.Round(a * 255), 0, 255));
    }
}
