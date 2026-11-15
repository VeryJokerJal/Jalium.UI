using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssImageObjectTests
{
    [Theory]
    [InlineData("object-fit", "fill")]
    [InlineData("object-fit", "contain")]
    [InlineData("object-fit", "cover")]
    [InlineData("object-fit", "none")]
    [InlineData("object-fit", "scale-down")]
    [InlineData("object-position", "25% 75%")]
    [InlineData("object-position", "right 10px bottom 5px")]
    [InlineData("object-position", "bottom 5px left 10%")]
    [InlineData("object-position", "left calc(10% + 2px) top 5px")]
    public void AcceptsObjectSizingAndPositionGrammar(string name, string value)
        => Assert.True(Css.Supports(name, value));

    [Theory]
    [InlineData("object-fit", "stretch")]
    [InlineData("object-fit", "cover contain")]
    [InlineData("object-position", "left right")]
    [InlineData("object-position", "center 2px top")]
    public void RejectsInvalidObjectGrammar(string name, string value)
        => Assert.False(Css.Supports(name, value));

    [Theory]
    [InlineData("fill", 0, 0, 100, 100)]
    [InlineData("contain", 0, 25, 100, 50)]
    [InlineData("cover", -50, 0, 200, 100)]
    [InlineData("none", 30, 40, 40, 20)]
    [InlineData("scale-down", 30, 40, 40, 20)]
    public void ObjectFitChangesOnlyTheConcreteImageSize(string fit,
        double x, double y, double width, double height)
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, $"width:100px;height:100px;object-fit:{fit}");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Size(100, 100), image.RenderSize);
        Assert.Equal(new Rect(x, y, width, height), DrawnRect(image));
    }

    [Fact]
    public void ScaleDownOnlyShrinksWhenTheNaturalImageExceedsTheBox()
    {
        var image = CreateImage(200, 100);
        Css.SetStyle(image, "width:100px;height:100px;object-fit:scale-down");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(image));
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("contain")]
    [InlineData("cover")]
    [InlineData("none")]
    [InlineData("scale-down")]
    public void ObjectFitDoesNotChangeTheIntrinsicElementSize(string fit)
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, $"padding:10px;object-fit:{fit}");
        image.Measure(new Size(100, 100));
        Assert.Equal(new Size(60, 40), image.DesiredSize);
    }

    [Theory]
    [InlineData("25% 75%", 15, 60)]
    [InlineData("right 10px bottom 5px", 50, 75)]
    [InlineData("bottom 5px left 10%", 6, 75)]
    [InlineData("left calc(10% + 2px) top 5px", 8, 5)]
    public void ObjectPositionUsesTheSpaceLeftAfterSizing(string position, double x, double y)
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, $"width:100px;height:100px;object-fit:none;object-position:{position}");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(x, y, 40, 20), DrawnRect(image));
    }

    [Theory]
    [InlineData("25% 75%", -25, 0)]
    [InlineData("right 10px bottom 5px", -110, -5)]
    public void ObjectPositionHandlesNegativeSpaceAfterCover(string position, double x, double y)
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, $"width:100px;height:100px;object-fit:cover;object-position:{position}");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(x, y, 200, 100), DrawnRect(image));
    }

    [Fact]
    public void ObjectPositionAlsoMovesAnImageWithoutObjectFit()
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, "width:100px;height:100px;object-position:right bottom");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(0, 50, 100, 50), DrawnRect(image));

        Css.SetStyle(image, "width:100px;height:100px");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(image));
    }

    [Fact]
    public void PaddingAlsoParticipatesInNativeStretchMeasureWithoutObjectFit()
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, "padding:10px");
        image.Measure(new Size(100, 100));
        Assert.Equal(new Size(100, 60), image.DesiredSize);
    }

    [Fact]
    public void InitialFitFillsTheContentBoxAndRemovingItRestoresNativeStretch()
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image, "width:100px;height:100px;object-fit:initial");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(0, 0, 100, 100), DrawnRect(image));

        Css.SetStyle(image, "width:100px;height:100px");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(image));
    }

    [Fact]
    public void NativeLocalStretchKeepsPriorityOverCssObjectFit()
    {
        var image = CreateImage(40, 20);
        image.Stretch = Stretch.UniformToFill;
        Css.SetStyle(image, "width:100px;height:100px;object-fit:contain");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(-50, 0, 200, 100), DrawnRect(image));
    }

    [Fact]
    public void PaddingDefinesThePositioningAndClippingArea()
    {
        var image = CreateImage(40, 20);
        Css.SetStyle(image,
            "width:100px;height:100px;padding:10px;object-fit:cover;object-position:right bottom");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.Equal(new Rect(-70, 10, 160, 80), DrawnRect(image));
        Assert.Contains(Commands(image), static command =>
            command.Kind == DrawCommandKind.PushClip &&
            command.A is RectangleGeometry geometry &&
            geometry.Bounds == new Rect(10, 10, 80, 80));
    }

    [Fact]
    public void ObjectFitClipsItsContentEvenWhenTheElementAllowsOverflow()
    {
        var image = CreateImage(40, 20);
        image.ClipToBounds = false;
        Css.SetStyle(image, "width:100px;height:100px;overflow:visible;object-fit:cover");
        CssBorderRadiusTests.Layout(image, 100, 100);
        Assert.False(image.ClipToBounds);
        Assert.Contains(Commands(image), static command =>
            command.Kind == DrawCommandKind.PushClip &&
            command.A is RectangleGeometry geometry &&
            geometry.Bounds == new Rect(0, 0, 100, 100));
    }

    [Fact]
    public void ObjectFitAndPositionChangeRenderedPixelsAndClipToTheContentBox()
    {
        var bitmap = new WriteableBitmap(4, 2, 96, 96, PixelFormat.Bgra32, null);
        var red = new byte[4 * 2 * 4];
        for (var pixel = 0; pixel < red.Length; pixel += 4)
        {
            red[pixel + 2] = 255;
            red[pixel + 3] = 255;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, 4, 2), red, 16, 0);
        var image = new Image { Source = bitmap };

        byte Alpha(int x, int y)
        {
            CssBorderRadiusTests.Layout(image, 100, 100);
            var pixels = CssBorderRadiusTests.Pixels(image, 100, 100);
            return pixels[(y * 100 + x) * 4 + 3];
        }

        Css.SetStyle(image, "width:100px;height:100px;object-fit:contain");
        Assert.Equal((byte)0, Alpha(50, 10));
        Assert.Equal((byte)255, Alpha(50, 50));

        Css.SetStyle(image, "width:100px;height:100px;object-fit:cover");
        Assert.Equal((byte)255, Alpha(50, 10));

        Css.SetStyle(image, "width:100px;height:100px;object-fit:none;object-position:right bottom");
        Assert.Equal((byte)0, Alpha(50, 50));
        Assert.Equal((byte)255, Alpha(98, 98));

        Css.SetStyle(image,
            "width:100px;height:100px;padding:10px;object-fit:cover;object-position:right bottom");
        Assert.Equal((byte)0, Alpha(5, 50));
        Assert.Equal((byte)255, Alpha(15, 50));
    }

    [Fact]
    public void NoneRequestsTheNaturalDecodeEvenWhenItsVisibleContentBoxIsSmall()
    {
        var decoder = new RecordingImageDecoder { NaturalWidth = 1024, NaturalHeight = 512 };
        BitmapImage.SetDecoder(decoder);
        using var file = new TempImageFile();
        using var bitmap = file.CreateDeferredImage();

        // Start with a small resident bucket, as a deferred image often does.
        ImagePipelineTestHarness.AwaitDecodeOnThisThread(bitmap.RequestDecodeAsync(100, 100, cover: false));
        Assert.NotEqual((1024, 512), ImagePipelineTestHarness.PublishedBucket(bitmap));

        var image = new Image { Source = bitmap };
        Css.SetStyle(image, "width:100px;height:100px;object-fit:none");
        CssBorderRadiusTests.Layout(image, 100, 100);

        Assert.True(ImagePipelineTestHarness.SpinUntil(
            () => ImagePipelineTestHarness.PublishedBucket(bitmap) == (1024, 512),
            ImagePipelineTestHarness.DecodeTimeout));
        Assert.Equal(new Size(100, 100), image.RenderSize);
        Assert.True(decoder.DecodeCalls <= 2);
    }

    private static Image CreateImage(int width, int height)
        => new() { Source = new WriteableBitmap(width, height, 96, 96, PixelFormat.Bgra32, null) };

    private static Rect DrawnRect(Image image)
    {
        var command = Assert.Single(Commands(image).Where(static command => command.Kind == DrawCommandKind.DrawImage));
        return new Rect(command.V0, command.V1, command.V2, command.V3);
    }

    private static IReadOnlyList<DrawCommand> Commands(Image image)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        image.Render(recorder);
        return Flatten(recorder.Commit()).ToArray();

        static IEnumerable<DrawCommand> Flatten(RecordedDrawing drawing)
        {
            foreach (var command in drawing.Commands)
            {
                if (command.Kind == DrawCommandKind.DrawRecordedDrawing &&
                    command.A is RecordedDrawing nested)
                {
                    foreach (var child in Flatten(nested)) yield return child;
                }
                else yield return command;
            }
        }
    }
}
