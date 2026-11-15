using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssImageBrushTests
{
    private static Border Box(ImageBrush brush, int width, int height, string radius = "0")
    {
        var border = new Border { Background = brush };
        Css.SetStyle(border, "border-radius:" + radius);
        CssBorderRadiusTests.Layout(border, width, height);
        return border;
    }

    private static BitmapImage Pixels(params byte[] pixels) => BitmapImage.FromPixels(pixels, pixels.Length / 4, 1);
    private static (byte B, byte G, byte R, byte A) Pixel(byte[] pixels, int width, int x, int y)
    {
        var at = (y * width + x) * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
    }

    [Fact]
    public void SingleColourImage_ExactlyMatchesTheEllipticalSolidBrush()
    {
        using var source = Pixels(32, 128, 240, 255);
        var actual = Box(new ImageBrush(source), 120, 60, "50%");
        var reference = CssBorderRadiusTests.Box("background-color:rgb(240 128 32); border-radius:50%", 120, 60);
        Assert.Equal(CssBorderRadiusTests.Pixels(reference, 120, 60), CssBorderRadiusTests.Pixels(actual, 120, 60));
    }

    [Fact]
    public void BrushOpacityAndElementOpacity_ComposeWithoutLosingSourceAlpha()
    {
        using var source = Pixels(0, 255, 0, 128);
        var border = Box(new ImageBrush(source) { Opacity = .5 }, 20, 10, "50%"); border.Opacity = .5;
        var host = new Border { Child = border };
        CssBorderRadiusTests.Layout(host, 20, 10);
        var pixels = CssBorderRadiusTests.Pixels(host, 20, 10);
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)32), Pixel(pixels, 20, 10, 5));
        Assert.Equal((byte)0, Pixel(pixels, 20, 0, 0).A);
    }

    [Fact]
    public void BilinearSampling_DoesNotBleedHiddenRgbFromTransparentPixels()
    {
        using var source = Pixels(0, 0, 255, 0, 255, 0, 0, 255);
        var border = Box(new ImageBrush(source), 1, 1);
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)128), Pixel(CssBorderRadiusTests.Pixels(border, 1, 1), 1, 0, 0));
    }

    [Theory]
    [InlineData(TileMode.Tile)]
    [InlineData(TileMode.FlipX)]
    [InlineData(TileMode.FlipY)]
    [InlineData(TileMode.FlipXY)]
    public void TileModes_PreserveEachSourcePixel(TileMode mode)
    {
        byte[] colors = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
        using var source = BitmapImage.FromPixels(colors, 2, 2);
        var brush = new ImageBrush(source) { TileMode = mode, ViewportUnits = BrushMappingMode.Absolute, Viewport = new(0, 0, 2, 2) };
        var pixels = CssBorderRadiusTests.Pixels(Box(brush, 8, 8), 8, 8);
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
        {
            var sx = x % 2; var sy = y % 2;
            if (mode is TileMode.FlipX or TileMode.FlipXY && (x / 2) % 2 == 1) sx = 1 - sx;
            if (mode is TileMode.FlipY or TileMode.FlipXY && (y / 2) % 2 == 1) sy = 1 - sy;
            Assert.Equal(Pixel(colors, 2, sx, sy), Pixel(pixels, 8, x, y));
        }
    }

    [Fact]
    public void UniformAlignmentAndViewbox_KeepLetterboxesTransparent()
    {
        using var source = Pixels(0, 0, 255, 255, 0, 255, 0, 255);
        var brush = new ImageBrush(source) { Stretch = Stretch.Uniform, AlignmentY = AlignmentY.Bottom };
        var pixels = CssBorderRadiusTests.Pixels(Box(brush, 20, 20), 20, 20);
        Assert.Equal((byte)0, Pixel(pixels, 20, 10, 4).A);
        Assert.Equal((byte)255, Pixel(pixels, 20, 18, 15).G);
        brush.Viewbox = new Rect(.5, 0, .5, 1);
        var cropped = CssBorderRadiusTests.Pixels(Box(brush, 20, 20), 20, 20);
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), Pixel(cropped, 20, 15, 10));
    }

    [Fact]
    public void BrushTransform_MovesTheImageWithinAFixedContour()
    {
        using var source = Pixels(0, 0, 255, 255);
        var brush = new ImageBrush(source) { RelativeTransform = new TranslateTransform(.5, 0) };
        var border = Box(brush, 40, 20, "50%");
        var pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.Equal((byte)0, Pixel(pixels, 40, 10, 10).A);
        Assert.Equal((byte)255, Pixel(pixels, 40, 30, 10).R);
        Assert.Equal((byte)0, Pixel(pixels, 40, 39, 0).A);
        brush.RelativeTransform = Transform.Identity;
        brush.Transform = new TranslateTransform(10, 0);
        pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.Equal((byte)0, Pixel(pixels, 40, 5, 10).A);
        Assert.Equal((byte)255, Pixel(pixels, 40, 20, 10).R);
    }

    [Fact]
    public void MutableBitmap_UpdatesTheExistingBrushWithoutReplacingBindings()
    {
        var source = new WriteableBitmap(1, 1, 96, 96, PixelFormat.Bgra32, null);
        source.WritePixels(new Int32Rect(0, 0, 1, 1), [0, 0, 255, 255], 4, 0);
        var brush = new ImageBrush(source); var border = Box(brush, 20, 10, "50%");
        Assert.Equal((byte)255, Pixel(CssBorderRadiusTests.Pixels(border, 20, 10), 20, 10, 5).R);
        source.WritePixels(new Int32Rect(0, 0, 1, 1), [0, 255, 0, 255], 4, 0);
        Assert.Equal((byte)255, Pixel(CssBorderRadiusTests.Pixels(border, 20, 10), 20, 10, 5).G);
        Assert.Same(brush, border.Background); Assert.Same(source, brush.ImageSource);
    }

    [Fact]
    public void DrawingImageGeometry_UsesImagePixelsInsteadOfTheirAverage()
    {
        using var source = Pixels(0, 0, 255, 255, 0, 255, 0, 255);
        var geometry = Geometry.Parse("M0,0 L20,0 L0,10 Z");
        var imageBrush = new ImageBrush(source);
        var drawing = new GeometryDrawing(imageBrush, null, geometry);
        var pixels = Jalium.UI.Interop.SoftwareVectorRasterizer.Rasterize(drawing, 20, 10)!;
        Assert.Equal((byte)255, Pixel(pixels, 20, 1, 1).R);
        Assert.Equal((byte)0, Pixel(pixels, 20, 18, 8).A);
        Assert.True(Pixel(pixels, 20, 15, 1).G > Pixel(pixels, 20, 15, 1).R);
    }

    [Fact]
    public void VectorImageSource_IsSampledInsideTheCssContourAndTracksDrawingChanges()
    {
        var vectorBrush = new SolidColorBrush(Colors.Red);
        var vectorDrawing = new GeometryDrawing(vectorBrush, null,
            new RectangleGeometry(new Rect(0, 0, 4, 2)));
        var source = new DrawingImage(vectorDrawing);
        var border = Box(new ImageBrush(source), 40, 20, "50%");

        var pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.Equal((byte)0, Pixel(pixels, 40, 0, 0).A);
        Assert.Equal((byte)255, Pixel(pixels, 40, 20, 10).R);

        vectorBrush.Color = Color.FromRgb(0, 255, 0);
        pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.Equal((byte)255, Pixel(pixels, 40, 20, 10).G);
        Assert.Equal((byte)0, Pixel(pixels, 40, 20, 10).R);
        Assert.Same(source, ((ImageBrush)border.Background!).ImageSource);
    }

    [Fact]
    public void VectorImageSource_CanContainAMutableImageDrawing()
    {
        var nested = new WriteableBitmap(1, 1, 96, 96, PixelFormat.Bgra32, null);
        nested.WritePixels(new Int32Rect(0, 0, 1, 1), [0, 0, 255, 255], 4, 0);
        var source = new DrawingImage(new ImageDrawing(nested, new Rect(0, 0, 4, 2)));
        var border = Box(new ImageBrush(source), 40, 20, "50%");
        Assert.Equal((byte)255, Pixel(CssBorderRadiusTests.Pixels(border, 40, 20), 40, 20, 10).R);

        nested.WritePixels(new Int32Rect(0, 0, 1, 1), [0, 255, 0, 255], 4, 0);
        var updated = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.Equal((byte)255, Pixel(updated, 40, 20, 10).G);
        Assert.Equal((byte)0, Pixel(updated, 40, 20, 10).R);
    }

    [Fact]
    public void MissingImage_PaintsNothing_AndLocalCornerRadiusStillWins()
    {
        var border = Box(new ImageBrush(), 20, 10, "50%");
        Assert.All(CssBorderRadiusTests.Pixels(border, 20, 10), b => Assert.Equal((byte)0, b));
        using var source = Pixels(0, 255, 0, 255);
        border.Background = new ImageBrush(source); border.CornerRadius = new(0);
        Assert.Equal((byte)255, Pixel(CssBorderRadiusTests.Pixels(border, 20, 10), 20, 0, 0).G);
    }
}
