using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static partial class CssBorderImagePainter
{
    // Sample very dense patterns at the bounded output resolution instead of recording millions of quads.
    private static void Dense(FrameworkElement owner, DrawingContext context, ImageSource patch,
        Rect region, Axis xAxis, Axis yAxis, DependencyObject? styleOwner = null)
    {
        if (patch.Width == 1 && patch.Height == 1 && xAxis.Step == xAxis.Tile && yAxis.Step == yAxis.Tile)
        { context.DrawImage(patch, region); return; }
        var image = new Size(patch.Width, patch.Height);
        var crop = new Rect(image);
        var dpi = VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(owner) as Visual ?? owner).DpiScaleY;
        if (!(dpi > 0 && double.IsFinite(dpi))) dpi = 1;
        var inputScale = Math.Min(1, 2048 / Math.Max(image.Width, image.Height));
        var inputWidth = Math.Max(1, (int)Math.Ceiling(image.Width * inputScale));
        var inputHeight = Math.Max(1, (int)Math.Ceiling(image.Height * inputScale));
        var input = new RenderTargetBitmap(inputWidth, inputHeight, 96 * inputWidth / image.Width, 96 * inputHeight / image.Height, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(input); drawing.DrawImage(patch, new Rect(image)); drawing.Close();
        var pixels = input.GetPixelBuffer();
        var outputScale = Math.Min(dpi, 2048 / Math.Max(region.Width, region.Height));
        var width = Math.Max(1, (int)Math.Ceiling(region.Width * outputScale));
        var height = Math.Max(1, (int)Math.Ceiling(region.Height * outputScale));
        var output = new byte[checked(width * height * 4)];
        var sampling = CssImageRenderingProperties.Resolve(styleOwner ?? owner, BitmapScalingMode.Unspecified);
        var nearest = sampling is BitmapScalingMode.NearestNeighbor or BitmapScalingMode.Pixelated;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = Coordinate(region.X + (x + .5) * region.Width / width, xAxis);
            var v = Coordinate(region.Y + (y + .5) * region.Height / height, yAxis);
            if (u is null || v is null) continue;
            var px = (crop.X + u.Value * crop.Width) * inputWidth / image.Width - .5;
            var py = (crop.Y + v.Value * crop.Height) * inputHeight / image.Height - .5;
            var at = (y * width + x) * 4;
            if (nearest)
            {
                var ix = Math.Clamp((int)Math.Floor(px + .5), 0, inputWidth - 1); var iy = Math.Clamp((int)Math.Floor(py + .5), 0, inputHeight - 1);
                pixels.AsSpan((iy * inputWidth + ix) * 4, 4).CopyTo(output.AsSpan(at)); continue;
            }
            var x0 = (int)Math.Floor(px); var y0 = (int)Math.Floor(py); var fx = px - x0; var fy = py - y0;
            double alpha = 0, blue = 0, green = 0, red = 0;
            for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
            {
                var ix = Math.Clamp(x0 + column, 0, inputWidth - 1); var iy = Math.Clamp(y0 + row, 0, inputHeight - 1);
                var sample = (iy * inputWidth + ix) * 4;
                var weight = (column == 0 ? 1 - fx : fx) * (row == 0 ? 1 - fy : fy) * pixels[sample + 3] / 255d;
                alpha += weight; blue += pixels[sample] * weight; green += pixels[sample + 1] * weight; red += pixels[sample + 2] * weight;
            }
            if (alpha <= 0) continue;
            output[at] = Channel(blue / alpha); output[at + 1] = Channel(green / alpha); output[at + 2] = Channel(red / alpha); output[at + 3] = Channel(alpha * 255);
        }
        var bitmap = BitmapSource.Create(width, height, 96 * width / region.Width, 96 * height / region.Height, PixelFormats.Bgra32, null, output, width * 4);
        context.DrawImage(bitmap, region, sampling);
    }

    internal static double? Coordinate(double position, Axis axis)
    {
        if (!(axis.Step > 0 && axis.Tile > 0)) return null;
        var relative = (position - axis.Start) % axis.Step;
        if (relative < 0) return null;
        return relative < axis.Tile ? relative / axis.Tile : null;
    }
    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
