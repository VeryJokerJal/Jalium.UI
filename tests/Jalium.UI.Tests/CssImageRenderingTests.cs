using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Shapes;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssImageRenderingTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("smooth")]
    [InlineData("high-quality")]
    [InlineData("crisp-edges")]
    [InlineData("pixelated")]
    [InlineData("optimizeSpeed")]
    [InlineData("optimizeQuality")]
    public void AcceptsImageRenderingKeywords(string value)
        => Assert.True(Css.Supports("image-rendering", value));

    [Theory]
    [InlineData("nearest")]
    [InlineData("pixelated smooth")]
    public void RejectsInvalidImageRenderingKeywords(string value)
        => Assert.False(Css.Supports("image-rendering", value));

    [Fact]
    public void PixelatedUsesAnIntegerNearestStageBeforeSmoothResampling()
    {
        using var source = TwoPixels();
        var image = new Image { Source = source };

        byte[] Paint(string mode)
        {
            Css.SetStyle(image, $"width:5px;height:5px;object-fit:fill;image-rendering:{mode}");
            CssBorderRadiusTests.Layout(image, 5, 5);
            return CssBorderRadiusTests.Pixels(image, 5, 5);
        }

        var pixelated = Paint("pixelated");
        Assert.Equal((byte)255, Channel(pixelated, 1, 2));
        Assert.InRange(Channel(pixelated, 2, 2), (byte)120, (byte)135);
        Assert.Equal((byte)255, Channel(pixelated, 3, 0));

        var crisp = Paint("crisp-edges");
        Assert.Equal((byte)255, Channel(crisp, 1, 2));
        Assert.Equal((byte)0, Channel(crisp, 2, 2));

        var smooth = Paint("smooth");
        Assert.InRange(Channel(smooth, 1, 2), (byte)220, (byte)240);
    }

    [Fact]
    public void ImageRenderingInheritsAndCssWideKeywordsTrackTheParent()
    {
        using var source = TwoPixels();
        var image = new Image { Source = source };
        var parent = new Border { Child = image };
        Css.SetStyle(image, "width:5px;height:5px;object-fit:fill;image-rendering:inherit");
        Css.SetStyle(parent, "image-rendering:crisp-edges");
        CssBorderRadiusTests.Layout(parent, 5, 5);
        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(parent, 5, 5), 2, 2));

        Css.SetStyle(parent, "image-rendering:smooth");
        CssBorderRadiusTests.Layout(parent, 5, 5);
        Assert.InRange(Channel(CssBorderRadiusTests.Pixels(parent, 5, 5), 1, 2),
            (byte)220, (byte)240);

        Css.SetStyle(image, "width:5px;height:5px;object-fit:fill;image-rendering:initial");
        Css.SetStyle(parent, "image-rendering:crisp-edges");
        CssBorderRadiusTests.Layout(parent, 5, 5);
        Assert.InRange(Channel(CssBorderRadiusTests.Pixels(parent, 5, 5), 1, 2),
            (byte)220, (byte)240);
    }

    [Fact]
    public void CssBackgroundImageUsesItsOwnersSamplingHint()
    {
        using var source = TwoPixels();
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:5px 5px;" +
            "background-repeat:no-repeat;image-rendering:pixelated");
        CssBorderRadiusTests.Layout(border, 5, 5);
        Assert.IsType<ImageBrush>(border.Background).ImageSource = source;
        var pixels = CssBorderRadiusTests.Pixels(border, 5, 5);
        Assert.Equal((byte)255, Channel(pixels, 1, 2));
        Assert.InRange(Channel(pixels, 2, 2), (byte)120, (byte)135);
    }

    [Fact]
    public void LocalImageBrushUsesInheritedSamplingUnlessItHasAnExplicitMode()
    {
        using var source = TwoPixels();
        var brush = new ImageBrush(source);
        var child = new Border { Background = brush };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "image-rendering:pixelated");
        Css.SetStyle(child, "width:5px;height:5px");
        CssBorderRadiusTests.Layout(parent, 5, 5);

        var pixels = CssBorderRadiusTests.Pixels(parent, 5, 5);
        Assert.Equal((byte)255, Channel(pixels, 1, 2));
        Assert.InRange(Channel(pixels, 2, 2), (byte)120, (byte)135);
        Assert.Same(brush, child.Background);

        brush.ScalingMode = BitmapScalingMode.NearestNeighbor;
        pixels = CssBorderRadiusTests.Pixels(parent, 5, 5);
        Assert.Equal((byte)0, Channel(pixels, 2, 2));
    }

    [Fact]
    public void ShapeImageFillUsesInheritedSamplingWithoutChangingTheAuthoredBrush()
    {
        using var source = TwoPixels();
        var brush = new ImageBrush(source);
        var shape = new Rectangle { Fill = brush, StrokeThickness = 0 };
        var parent = new Border { Child = shape };
        Css.SetStyle(parent, "width:5px;height:5px;image-rendering:pixelated");
        CssBorderRadiusTests.Layout(parent, 5, 5);

        var pixels = CssBorderRadiusTests.Pixels(parent, 5, 5);
        Assert.Equal((byte)255, Channel(pixels, 1, 2));
        Assert.InRange(Channel(pixels, 2, 2), (byte)120, (byte)135);
        Assert.Same(brush, shape.Fill);
        Assert.Equal(BitmapScalingMode.Unspecified, brush.ScalingMode);

        brush.ScalingMode = BitmapScalingMode.NearestNeighbor;
        pixels = CssBorderRadiusTests.Pixels(parent, 5, 5);
        Assert.Equal((byte)0, Channel(pixels, 2, 2));
    }

    [Fact]
    public void ShapeImageStrokeUsesInheritedSamplingAndNativePriority()
    {
        using var source = TwoPixels();
        var brush = new ImageBrush(source);
        var line = new Line
        {
            X1 = 0, X2 = 5, Y1 = 2.5, Y2 = 2.5,
            Stroke = brush, StrokeThickness = 5,
        };
        var parent = new Border { Child = line };
        Css.SetStyle(parent, "width:5px;height:5px;image-rendering:pixelated");
        CssBorderRadiusTests.Layout(parent, 5, 5);

        var pixels = CssBorderRadiusTests.Pixels(parent, 5, 5);
        Assert.Equal((byte)255, Channel(pixels, 1, 2));
        Assert.InRange(Channel(pixels, 2, 2), (byte)120, (byte)135);
        Assert.Same(brush, line.Stroke);
        Assert.Equal(BitmapScalingMode.Unspecified, brush.ScalingMode);

        brush.ScalingMode = BitmapScalingMode.NearestNeighbor;
        pixels = CssBorderRadiusTests.Pixels(parent, 5, 5);
        Assert.True(Channel(pixels, 2, 2) <= 10 || Channel(pixels, 2, 2) >= 245);
    }

    [Fact]
    public void DashedShapeImageStrokeUsesInheritedSamplingAcrossTheFullLine()
    {
        using var source = TwoPixels();
        var line = new Line
        {
            X1 = 8, X2 = 88, Y1 = 8, Y2 = 8,
            Stroke = new ImageBrush(source),
            StrokeThickness = 8,
            StrokeDashArray = new DoubleCollection { 1, 2 },
            StrokeDashCap = PenLineCap.Flat,
        };
        var parent = new Border { Child = line };
        Css.SetStyle(parent, "width:96px;height:16px;image-rendering:crisp-edges");
        CssBorderRadiusTests.Layout(parent, 96, 16);

        var pixels = CssBorderRadiusTests.Pixels(parent, 96, 16);
        static byte ChannelAt(byte[] data, int x, int channel)
            => data[((8 * 96) + x) * 4 + channel];

        Assert.True(ChannelAt(pixels, 12, 2) > 220);
        Assert.Equal((byte)0, ChannelAt(pixels, 24, 3));
        Assert.True(ChannelAt(pixels, 84, 0) > 220);
    }

    [Fact]
    public void PolylineImageStrokeUsesOneSamplingAreaAcrossItsSegments()
    {
        using var source = TwoPixels();
        var line = new Polyline
        {
            Points = new PointCollection
            {
                new Point(0, 2.5), new Point(2.5, 2.5), new Point(5, 2.5),
            },
            Stroke = new ImageBrush(source),
            StrokeThickness = 5,
        };
        Css.SetStyle(line, "width:5px;height:5px;image-rendering:pixelated");
        CssBorderRadiusTests.Layout(line, 5, 5);

        var pixels = CssBorderRadiusTests.Pixels(line, 5, 5);
        Assert.Equal((byte)255, Channel(pixels, 1, 2));
        Assert.Equal((byte)0, Channel(pixels, 3, 2));
    }

    [Fact]
    public void NativeScalingModeKeepsPriorityOverCssOnImage()
    {
        using var source = TwoPixels();
        var image = new Image { Source = source };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Css.SetStyle(image, "width:5px;height:5px;object-fit:fill;image-rendering:smooth");
        CssBorderRadiusTests.Layout(image, 5, 5);
        var pixels = CssBorderRadiusTests.Pixels(image, 5, 5);
        Assert.Equal((byte)0, Channel(pixels, 2, 2));
    }

    [Fact]
    public void VideoFrameRecordsTheRequestedSamplingMode()
    {
        using var media = CssMediaObjectTests.CreateMedia();
        Css.SetStyle(media, "width:100px;height:100px;image-rendering:pixelated");
        CssBorderRadiusTests.Layout(media, 100, 100);
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        media.Render(recorder);
        var image = Assert.Single(Flatten(recorder.Commit()).Where(static c => c.Kind == DrawCommandKind.DrawImage));
        Assert.Equal((double)(int)BitmapScalingMode.Pixelated, image.V4);

        static IEnumerable<DrawCommand> Flatten(RecordedDrawing drawing)
        {
            foreach (var command in drawing.Commands)
            {
                if (command.Kind == DrawCommandKind.DrawRecordedDrawing && command.A is RecordedDrawing nested)
                {
                    foreach (var child in Flatten(nested)) yield return child;
                }
                else yield return command;
            }
        }
    }

    private static BitmapImage TwoPixels()
        => BitmapImage.FromPixels([0, 0, 255, 255, 255, 0, 0, 255], 2, 1);

    private static byte Channel(byte[] pixels, int x, int channel)
        => pixels[((2 * 5) + x) * 4 + channel];
}
