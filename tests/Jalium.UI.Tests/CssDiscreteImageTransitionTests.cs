using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssDiscreteImageTransitionTests
{
    [Fact]
    public void InheritedImageRenderingChangesSamplingAtTheEasedMidpoint()
    {
        using var source = BitmapImage.FromPixels(
            [0, 0, 255, 255, 255, 0, 0, 255], 2, 1);
        var image = new Image { Source = source, Width = 5, Height = 5 };
        Css.SetStyle(image, "object-fit:fill");
        var parent = new Border { Child = image };
        var host = new Grid { Width = 5, Height = 5 };
        host.Children.Add(parent);
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(parent,
                "image-rendering:crisp-edges; transition:image-rendering 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 5, 5);
            Assert.Equal((byte)0, RedAtCenter(CssBorderRadiusTests.Pixels(host, 5, 5)));

            Css.SetStyle(parent,
                "image-rendering:pixelated; transition:image-rendering 1s linear allow-discrete");
            Assert.True(parent.HasAutomaticTransition(CssImageRenderingProperties.ValueProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.Equal(CssImageRendering.CrispEdges,
                image.GetValue(CssImageRenderingProperties.ValueProperty));
            Assert.Equal((byte)0, RedAtCenter(CssBorderRadiusTests.Pixels(host, 5, 5)));

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Equal(CssImageRendering.Pixelated,
                image.GetValue(CssImageRenderingProperties.ValueProperty));
            Assert.InRange(RedAtCenter(CssBorderRadiusTests.Pixels(host, 5, 5)),
                (byte)120, (byte)135);
        }
        finally
        {
            parent.StopAutomaticTransition(CssImageRenderingProperties.ValueProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void ObjectFitChangesTheRenderedImageSizeAtTheEasedMidpoint()
    {
        var image = CreateImage();
        var host = new Grid { Width = 100, Height = 100 };
        host.Children.Add(image);
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(image,
                "width:100px;height:100px;object-fit:contain;" +
                "transition:object-fit 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 100, 100);
            Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(image));

            Css.SetStyle(image,
                "width:100px;height:100px;object-fit:cover;" +
                "transition:object-fit 1s linear allow-discrete");
            Assert.True(image.HasAutomaticTransition(CssImageObjectProperties.FitProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            CssBorderRadiusTests.Layout(host, 100, 100);
            Assert.Equal(new Rect(0, 25, 100, 50), DrawnRect(image));

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            CssBorderRadiusTests.Layout(host, 100, 100);
            Assert.Equal(new Rect(-50, 0, 200, 100), DrawnRect(image));
        }
        finally
        {
            image.StopAutomaticTransition(CssImageObjectProperties.FitProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void NormalBehaviorChangesObjectFitImmediately()
    {
        var image = CreateImage();
        var host = new Grid { Width = 100, Height = 100 };
        host.Children.Add(image);
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(image,
                "width:100px;height:100px;object-fit:contain;transition:object-fit 1s linear");
            CssBorderRadiusTests.Layout(host, 100, 100);
            Css.SetStyle(image,
                "width:100px;height:100px;object-fit:cover;transition:object-fit 1s linear");
            Assert.False(image.HasAutomaticTransition(CssImageObjectProperties.FitProperty));
            CssBorderRadiusTests.Layout(host, 100, 100);
            Assert.Equal(new Rect(-50, 0, 200, 100), DrawnRect(image));
        }
        finally
        {
            image.StopAutomaticTransition(CssImageObjectProperties.FitProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    private static Image CreateImage() => new()
    {
        Source = new WriteableBitmap(40, 20, 96, 96, PixelFormat.Bgra32, null),
    };

    private static byte RedAtCenter(byte[] pixels) => pixels[((2 * 5) + 2) * 4 + 2];

    private static Rect DrawnRect(Image image)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        image.Render(recorder);
        var command = Assert.Single(Flatten(recorder.Commit())
            .Where(static item => item.Kind == DrawCommandKind.DrawImage));
        return new Rect(command.V0, command.V1, command.V2, command.V3);
    }

    private static IEnumerable<DrawCommand> Flatten(RecordedDrawing drawing)
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
