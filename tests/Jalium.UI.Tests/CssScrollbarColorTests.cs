using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssScrollbarColorTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("red blue")]
    [InlineData("#123456 rgb(1 2 3 / 50%)")]
    [InlineData("currentColor color-mix(in srgb, currentColor 50%, white)")]
    public void AcceptsAutoOrTwoColors(string value)
        => Assert.True(Css.Supports("scrollbar-color", value));

    [Theory]
    [InlineData("red")]
    [InlineData("auto red")]
    [InlineData("red blue green")]
    [InlineData("none")]
    public void RejectsOtherValues(string value)
        => Assert.False(Css.Supports("scrollbar-color", value));

    [Fact]
    public void ThemedViewerColorsBothBarsAndRestoresThemeOnRemoval()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var viewer = CreateViewer();
            Css.SetStyle(viewer, "scrollbar-color: red blue");
            CssBorderRadiusTests.Layout(viewer, 200, 140);

            foreach (var bar in Bars(viewer))
            {
                Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));
                Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
                var border = Assert.IsType<Border>(bar.Track.Thumb.FindName("ThumbBorder"));
                Assert.Equal(Colors.Red.ToArgb(), ColorOf(border.Background));
            }

            Css.SetStyle(viewer, string.Empty);
            CssBorderRadiusTests.Layout(viewer, 200, 140);
            foreach (var bar in Bars(viewer))
            {
                Assert.NotEqual(Colors.Blue.ToArgb(), ColorOf(bar.Background));
                Assert.NotEqual(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
                Assert.NotEqual(Colors.Red.ToArgb(), ColorOf(
                    Assert.IsType<Border>(bar.Track.Thumb.FindName("ThumbBorder")).Background));
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void InheritsComputedCurrentColorAndAutoOverridesIt()
    {
        var parent = new Grid();
        var viewer = CreateViewer();
        parent.Children.Add(viewer);
        Css.SetStyle(parent, "color: red; scrollbar-color: currentColor blue");
        Css.SetStyle(viewer, "color: green");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        AssertColors(viewer, Colors.Red, Colors.Blue);
        var inheritedTrack = Bars(viewer)[0].Background;

        Css.SetStyle(viewer, "color: green; scrollbar-color: auto");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        Assert.Null(viewer.CssScrollBarColors);
        Assert.NotSame(inheritedTrack, Bars(viewer)[0].Background);

        Css.SetStyle(viewer, "color: green; scrollbar-color: inherit");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        AssertColors(viewer, Colors.Red, Colors.Blue);

        Css.SetStyle(viewer, "color: green; scrollbar-color: unset");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        AssertColors(viewer, Colors.Red, Colors.Blue);

        Css.SetStyle(viewer, "color: green; scrollbar-color: initial");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        Assert.Null(viewer.CssScrollBarColors);

        Css.SetStyle(viewer, "color: green; scrollbar-color: unset");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        AssertColors(viewer, Colors.Red, Colors.Blue);

        Css.SetStyle(parent, "color: blue; scrollbar-color: currentColor red");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        AssertColors(viewer, Colors.Blue, Colors.Red);
    }

    [Fact]
    public void DefaultListBoxTemplateInheritsScrollbarColors()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var owner = new ListBox();
            for (var index = 0; index < 30; index++)
                owner.Items.Add($"Row {index}");
            var host = new Grid();
            host.Children.Add(owner);
            Css.SetStyle(owner, "scrollbar-color: red blue");
            CssBorderRadiusTests.Layout(host, 200, 140);

            var viewer = Assert.IsType<ScrollViewer>(FindDescendant<ScrollViewer>(owner));
            Assert.Same(owner, viewer.TemplatedParent);
            AssertColors(viewer, Colors.Red, Colors.Blue);

            Css.SetStyle(owner, "scrollbar-color: auto");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Null(viewer.CssScrollBarColors);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CurrentColorTracksMutableForegroundBrush()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var viewer = CreateViewer();
        viewer.Foreground = foreground;
        Css.SetStyle(viewer, "scrollbar-color: currentColor blue");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        AssertColors(viewer, Colors.Red, Colors.Blue);

        foreground.Color = Colors.Green;
        CssEvaluationScheduler.FlushIfPending(viewer.Dispatcher);
        AssertColors(viewer, Colors.Green, Colors.Blue);
    }

    [Fact]
    public void ThemedViewerPaintsThumbAndTrackWithAuthoredColors()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var viewer = CreateViewer();
            Css.SetStyle(viewer, "scrollbar-color: red blue");
            CssBorderRadiusTests.Layout(viewer, 200, 140);

            var vertical = Bars(viewer)[0];
            var thumb = vertical.Track.Thumb!;
            var thumbOrigin = thumb.TransformToAncestor(viewer);
            var thumbCenter = new Point(thumbOrigin.X + thumb.RenderSize.Width / 2,
                thumbOrigin.Y + thumb.RenderSize.Height / 2);
            var trackOrigin = vertical.Track.TransformToAncestor(viewer);
            var trackEnd = new Point(trackOrigin.X + vertical.Track.RenderSize.Width / 2,
                trackOrigin.Y + vertical.Track.RenderSize.Height - 3);
            var pixels = CssBorderRadiusTests.Pixels(viewer, 200, 140);

            Assert.Equal(Colors.Red.ToArgb(), PixelAt(pixels, thumbCenter));
            Assert.Equal(Colors.Blue.ToArgb(), PixelAt(pixels, trackEnd));

            Css.SetStyle(viewer, "scrollbar-color: rgb(255 0 0 / 50%) blue");
            CssBorderRadiusTests.Layout(viewer, 200, 140);
            Assert.Equal(0x80FF0000u, ColorOf(thumb.Background));
            Assert.Equal(0x80FF0000u, ColorOf(
                Assert.IsType<Border>(thumb.FindName("ThumbBorder")).Background));
            var newThumbOrigin = thumb.TransformToAncestor(viewer);
            var newThumbCenter = new Point(newThumbOrigin.X + thumb.RenderSize.Width / 2,
                newThumbOrigin.Y + thumb.RenderSize.Height / 2);
            var translucent = PixelAt(CssBorderRadiusTests.Pixels(viewer, 200, 140), newThumbCenter);
            Assert.InRange(translucent, 0xFF80007Fu, 0xFF800080u);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ThemedThumbKeepsAuthoredColorDuringHoverAndDisabledStates()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var viewer = CreateViewer();
            Css.SetStyle(viewer, "scrollbar-color: red blue");
            CssBorderRadiusTests.Layout(viewer, 200, 140);
            var bar = Bars(viewer)[0];
            var thumb = bar.Track.Thumb!;
            var border = Assert.IsType<Border>(thumb.FindName("ThumbBorder"));

            thumb.SetIsMouseOver(true);
            Assert.Equal(Colors.Red.ToArgb(), ColorOf(border.Background));
            thumb.SetIsMouseOver(false);
            bar.IsEnabled = false;
            Assert.Equal(Colors.Red.ToArgb(), ColorOf(border.Background));

            Css.SetStyle(viewer, "scrollbar-color: auto");
            CssBorderRadiusTests.Layout(viewer, 200, 140);
            Assert.NotEqual(Colors.Red.ToArgb(), ColorOf(
                Assert.IsType<Border>(thumb.FindName("ThumbBorder")).Background));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ExplicitNativeColorsKeepPrecedence()
    {
        var viewer = CreateViewer();
        Css.SetStyle(viewer, "scrollbar-color: red blue");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        var bar = Bars(viewer)[0];
        var thumb = bar.Track.Thumb!;
        bar.Background = Brushes.Green;
        thumb.Background = Brushes.Yellow;
        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Same(Brushes.Green, bar.Background);
        Assert.Same(Brushes.Yellow, thumb.Background);
        Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(
            Assert.IsType<Border>(thumb.FindName("ThumbBorder")).Background));

        var nativeTemplate = new ControlTemplate(typeof(Thumb));
        nativeTemplate.SetVisualTree(() => new Border { Name = "NativeThumb" });
        thumb.Template = nativeTemplate;
        Css.SetStyle(viewer, "scrollbar-color: green black");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Same(nativeTemplate, thumb.Template);
    }

    private static void AssertColors(ScrollViewer viewer, Color thumb, Color track)
    {
        foreach (var bar in Bars(viewer))
        {
            Assert.Equal(track.ToArgb(), ColorOf(bar.Background));
            Assert.Equal(thumb.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
        }
    }

    private static ScrollBar[] Bars(ScrollViewer viewer) =>
    [
        Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)),
        Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)),
    ];

    private static uint ColorOf(Brush? brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color.ToArgb();

    private static uint PixelAt(byte[] pixels, Point point)
    {
        var offset = ((int)point.Y * 200 + (int)point.X) * 4;
        return ((uint)pixels[offset + 3] << 24) |
               ((uint)pixels[offset + 2] << 16) |
               ((uint)pixels[offset + 1] << 8) |
               pixels[offset];
    }

    private static ScrollViewer CreateViewer() => new()
    {
        Content = new Border { Width = 300, Height = 240 },
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        IsScrollBarAutoHideEnabled = false,
        IsOverlayScrollBarEnabled = false,
    };

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        for (var index = 0; index < root.VisualChildrenCount; index++)
        {
            if (root.GetVisualChild(index) is not { } child)
                continue;
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
