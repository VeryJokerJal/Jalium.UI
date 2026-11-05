using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMediaObjectTests
{
    [Theory]
    [InlineData("fill", 0, 0, 100, 100)]
    [InlineData("contain", 0, 25, 100, 50)]
    [InlineData("cover", -50, 0, 200, 100)]
    [InlineData("none", 30, 40, 40, 20)]
    [InlineData("scale-down", 30, 40, 40, 20)]
    public void VideoFitChangesItsFrameAndDecoderTarget(string fit,
        double x, double y, double width, double height)
    {
        using var media = CreateMedia();
        Css.SetStyle(media, $"width:100px;height:100px;object-fit:{fit}");
        CssBorderRadiusTests.Layout(media, 100, 100);
        Assert.Equal(new Rect(x, y, width, height), DrawnRect(media));
        Assert.Equal(((int)width, (int)height), DecoderTarget(media, new Size(100, 100)));
    }

    [Theory]
    [InlineData("25% 75%", -25, 0)]
    [InlineData("right 10px bottom 5px", -110, -5)]
    public void VideoPositionUsesNegativeFreeSpaceAfterCover(string position, double x, double y)
    {
        using var media = CreateMedia();
        Css.SetStyle(media,
            $"width:100px;height:100px;object-fit:cover;object-position:{position}");
        CssBorderRadiusTests.Layout(media, 100, 100);
        Assert.Equal(new Rect(x, y, 200, 100), DrawnRect(media));
    }

    [Fact]
    public void VideoContentUsesCssPaddingAndClipsEvenWithVisibleOverflow()
    {
        using var media = CreateMedia();
        Css.SetStyle(media,
            "width:100px;height:100px;padding:10px;overflow:visible;" +
            "object-fit:cover;object-position:right bottom");
        CssBorderRadiusTests.Layout(media, 100, 100);
        Assert.Equal(new Rect(-70, 10, 160, 80), DrawnRect(media));
        Assert.Contains(Commands(media), static command =>
            command.Kind == DrawCommandKind.PushClip &&
            command.A is RectangleGeometry geometry &&
            geometry.Bounds == new Rect(10, 10, 80, 80));
        Assert.Equal((160, 80), DecoderTarget(media, new Size(100, 100)));
    }

    [Fact]
    public void VideoFitKeepsIntrinsicDesiredSizeAndNativeStretchPriority()
    {
        using var media = CreateMedia();
        Css.SetStyle(media, "padding:10px;object-fit:cover");
        media.Measure(new Size(100, 100));
        Assert.Equal(new Size(60, 40), media.DesiredSize);

        media.Stretch = Stretch.Uniform;
        Css.SetStyle(media, "width:100px;height:100px;object-fit:cover");
        CssBorderRadiusTests.Layout(media, 100, 100);
        Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(media));
    }

    [Fact]
    public void VideoPositionWorksWithoutAnAuthoredFitAndResetsWhenRemoved()
    {
        using var media = CreateMedia();
        Css.SetStyle(media, "width:100px;height:100px;object-position:right bottom");
        CssBorderRadiusTests.Layout(media, 100, 100);
        Assert.Equal(new Rect(0, 50, 100, 50), DrawnRect(media));

        Css.SetStyle(media, "width:100px;height:100px");
        CssBorderRadiusTests.Layout(media, 100, 100);
        Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(media));
    }

    internal static MediaElement CreateMedia()
    {
        var bitmap = new WriteableBitmap(40, 20, 96, 96, PixelFormat.Bgra32, null);
        var red = new byte[40 * 20 * 4];
        for (var pixel = 0; pixel < red.Length; pixel += 4)
        {
            red[pixel + 2] = 255;
            red[pixel + 3] = 255;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, 40, 20), red, 40 * 4, 0);
        var media = new MediaElement { LoadedBehavior = MediaState.Manual };
        SetField(media, "_hasVideo", true);
        SetField(media, "_videoWidth", 40);
        SetField(media, "_videoHeight", 20);
        SetField(media, "_frameBitmap", bitmap);
        return media;
    }

    private static (int Width, int Height) DecoderTarget(MediaElement media, Size slot)
        => ((int Width, int Height))typeof(MediaElement)
            .GetMethod("CalculateVideoRenderSize", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(media, [slot])!;

    private static void SetField(MediaElement media, string field, object value)
        => typeof(MediaElement).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(media, value);

    private static Rect DrawnRect(MediaElement media)
    {
        var command = Assert.Single(Commands(media).Where(static command => command.Kind == DrawCommandKind.DrawImage));
        return new Rect(command.V0, command.V1, command.V2, command.V3);
    }

    private static IReadOnlyList<DrawCommand> Commands(MediaElement media)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        media.Render(recorder);
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
