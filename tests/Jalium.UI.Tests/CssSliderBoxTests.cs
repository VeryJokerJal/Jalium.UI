using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssSliderBoxTests
{
    private const string BackgroundStyle =
        "border:8px solid transparent;padding:4px;background-color:blue;" +
        "background-image:linear-gradient(to right,red,lime);" +
        "background-size:20px 10px;background-position:right bottom;" +
        "background-repeat:no-repeat;background-origin:content-box;" +
        "background-clip:content-box";

    private const string AsymmetricEdges =
        "border-width:8px 4px 8px 8px;border-style:solid;" +
        "border-color:transparent;padding:4px 4px 4px 12px";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSlider_PaintsCssBackgroundLayersAcrossItsBox(bool range)
    {
        var slider = range
            ? (Control)new RangeSlider { Template = null, RangeStart = 0, RangeEnd = 0 }
            : new Slider { Template = null, Value = 0 };
        Css.SetStyle(slider, BackgroundStyle);
        CssBorderRadiusTests.Layout(slider, 100, 60);

        AssertBackgroundPixels(CssBorderRadiusTests.Pixels(slider, 100, 60));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSlider_MapsPointerWithinCssContentBox(bool range)
    {
        var slider = range
            ? (Control)new RangeSlider { Template = null, Maximum = 100, RangeStart = 25, RangeEnd = 75 }
            : new Slider { Template = null, Maximum = 100, Value = 50 };
        Css.SetStyle(slider, AsymmetricEdges);
        CssBorderRadiusTests.Layout(slider, 100, 60);

        var thumbMethod = slider.GetType().GetMethod("GetThumbRect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (!range)
        {
            Assert.NotNull(thumbMethod);
            var thumb = Assert.IsType<Rect>(thumbMethod!.Invoke(slider, null));
            Assert.Equal(48, thumb.X);
        }

        var method = slider.GetType().GetMethod(range ? "ValueFromPosition" : "GetValueFromPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(0, (double)method!.Invoke(slider, [new Point(28, 30)])!, precision: 6);
        Assert.Equal(50, (double)method.Invoke(slider, [new Point(56, 30)])!, precision: 6);
        Assert.Equal(100, (double)method.Invoke(slider, [new Point(84, 30)])!, precision: 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeSlider_PaintsCssBackgroundLayersAndInsetsThumbs(bool range)
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = range
                ? (Control)new RangeSlider { Width = 100, Height = 60, RangeStart = 25, RangeEnd = 75 }
                : new Slider { Width = 100, Height = 60, Maximum = 100, Value = 50 };
            Css.SetStyle(slider, BackgroundStyle);
            var host = new Grid { Width = 100, Height = 60 };
            host.Children.Add(slider);
            CssBorderRadiusTests.Layout(host, 100, 60);

            Assert.IsType<Border>(slider.FindName("PART_Root"));
            AssertBackgroundPixels(CssBorderRadiusTests.Pixels(slider, 100, 60));
            if (range)
            {
                Assert.Equal(15, ((FrameworkElement)slider.FindName("PART_StartThumb")!).Margin.Left);
                Assert.Equal(45, ((FrameworkElement)slider.FindName("PART_EndThumb")!).Margin.Left);
            }
            else
            {
                Assert.Equal(30, ((FrameworkElement)slider.FindName("PART_Thumb")!).Margin.Left);
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectVerticalSlider_MapsPointerWithinCssContentBox(bool range)
    {
        var slider = range
            ? (Control)new RangeSlider
                { Template = null, Width = 60, Height = 100, Orientation = Orientation.Vertical }
            : new Slider
                { Template = null, Width = 60, Height = 100, Orientation = Orientation.Vertical, Maximum = 100 };
        Css.SetStyle(slider, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(slider, 60, 100);

        var method = slider.GetType().GetMethod(range ? "ValueFromPosition" : "GetValueFromPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(100, (double)method!.Invoke(slider, [new Point(30, 20)])!, precision: 6);
        Assert.Equal(50, (double)method.Invoke(slider, [new Point(30, 50)])!, precision: 6);
        Assert.Equal(0, (double)method.Invoke(slider, [new Point(30, 80)])!, precision: 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeVerticalSlider_UsesInsetTrackAndThumbPositions(bool range)
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = range
                ? (Control)new RangeSlider
                {
                    Width = 60, Height = 100, Orientation = Orientation.Vertical,
                    RangeStart = 25, RangeEnd = 75,
                }
                : new Slider
                {
                    Width = 60, Height = 100, Orientation = Orientation.Vertical,
                    Maximum = 100, Value = 50,
                };
            Css.SetStyle(slider, "border:8px solid transparent;padding:4px");
            var host = new Grid { Width = 60, Height = 100 };
            host.Children.Add(slider);
            CssBorderRadiusTests.Layout(host, 60, 100);

            var track = Assert.IsType<Jalium.UI.Shapes.Rectangle>(slider.FindName("PART_Track"));
            Assert.Equal(4, track.Width);
            Assert.True(track.RenderSize.Height >= 55);
            if (range)
            {
                Assert.Equal(45, ((FrameworkElement)slider.FindName("PART_StartThumb")!).Margin.Top);
                Assert.Equal(15, ((FrameworkElement)slider.FindName("PART_EndThumb")!).Margin.Top);
            }
            else
            {
                Assert.Equal(30, ((FrameworkElement)slider.FindName("PART_Thumb")!).Margin.Top);
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeSlider_ReflowsThumbWhenCssEdgesAreRemoved(bool range)
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = range
                ? (Control)new RangeSlider { Width = 100, Height = 60, RangeStart = 25, RangeEnd = 75 }
                : new Slider { Width = 100, Height = 60, Maximum = 100, Value = 50 };
            var host = new Grid { Width = 100, Height = 60 };
            host.Children.Add(slider);
            Css.SetStyle(slider, AsymmetricEdges);
            CssBorderRadiusTests.Layout(host, 100, 60);

            var thumb = (FrameworkElement)slider.FindName(range ? "PART_StartThumb" : "PART_Thumb")!;
            Assert.Equal(range ? 14 : 28, thumb.Margin.Left);

            Css.SetStyle(slider, string.Empty);
            CssBorderRadiusTests.Layout(host, 100, 60);
            Assert.Equal(range ? 21 : 42, thumb.Margin.Left);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Slider_PaintsCssBorderSideColors(bool range, bool themed)
    {
        if (themed) ResetApplicationState();
        var app = themed ? new Application() : null;

        try
        {
            var slider = range
                ? (Control)new RangeSlider { Width = 100, Height = 60 }
                : new Slider { Width = 100, Height = 60 };
            if (!themed) slider.Template = null;
            Css.SetStyle(slider, "border:8px solid;border-color:red green blue yellow");
            var host = new Grid { Width = 100, Height = 60 };
            if (themed) host.Children.Add(slider);
            CssBorderRadiusTests.Layout(themed ? host : slider, 100, 60);

            var pixels = CssBorderRadiusTests.Pixels(slider, 100, 60);
            Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
            Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
            Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
            Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        }
        finally
        {
            if (themed) ResetApplicationState();
            GC.KeepAlive(app);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Slider_NativeBackgroundAndBorderBrush_OverrideCssPaint(bool range, bool themed)
    {
        if (themed) ResetApplicationState();
        var app = themed ? new Application() : null;

        try
        {
            var slider = range
                ? (Control)new RangeSlider { Width = 100, Height = 60 }
                : new Slider { Width = 100, Height = 60 };
            if (!themed) slider.Template = null;
            slider.Background = Brushes.Blue;
            slider.BorderBrush = Brushes.Lime;
            Css.SetStyle(slider,
                "border:8px solid;border-color:red green blue yellow;" +
                "padding:4px;background-color:red;background-clip:content-box");
            var host = new Grid { Width = 100, Height = 60 };
            if (themed) host.Children.Add(slider);
            CssBorderRadiusTests.Layout(themed ? host : slider, 100, 60);

            var pixels = CssBorderRadiusTests.Pixels(slider, 100, 60);
            Assert.Equal((byte)255, Channel(pixels, 50, 4, 1));
            Assert.Equal((byte)0, Channel(pixels, 50, 4, 0));
            Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
            Assert.Equal((byte)0, Channel(pixels, 40, 20, 2));
        }
        finally
        {
            if (themed) ResetApplicationState();
            GC.KeepAlive(app);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeSlider_CssRadiusClipsWholeBackground(bool range)
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = range
                ? (Control)new RangeSlider { Width = 100, Height = 60 }
                : new Slider { Width = 100, Height = 60 };
            Css.SetStyle(slider, "border-radius:50%;background-color:blue");
            var host = new Grid { Width = 100, Height = 60 };
            host.Children.Add(slider);
            CssBorderRadiusTests.Layout(host, 100, 60);

            var pixels = CssBorderRadiusTests.Pixels(slider, 100, 60);
            Assert.Equal((byte)0, Channel(pixels, 5, 5, 3));
            Assert.Equal((byte)255, Channel(pixels, 50, 10, 0));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeSlider_SegmentedTrackUsesCssContentBox(bool range)
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = range
                ? (Control)new RangeSlider
                    { Width = 100, Height = 60, TrackMode = SliderTrackMode.Segmented }
                : new Slider
                    { Width = 100, Height = 60, TrackMode = SliderTrackMode.Segmented };
            Css.SetStyle(slider, "border:8px solid transparent;padding:4px");
            var host = new Grid { Width = 100, Height = 60 };
            host.Children.Add(slider);
            CssBorderRadiusTests.Layout(host, 100, 60);

            var segments = Assert.IsType<SegmentedTrackBar>(slider.FindName("PART_Segments"));
            Assert.Equal(76, segments.RenderSize.Width);
            Assert.Equal(36, segments.RenderSize.Height);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeSlider_CssEdgesKeepRoomForThumbAtDefaultHeight(bool range)
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = range
                ? (Control)new RangeSlider { Width = 100 }
                : new Slider { Width = 100 };
            Css.SetStyle(slider, "border:8px solid transparent;padding:4px");
            var host = new Grid { Width = 100, Height = 80 };
            host.Children.Add(slider);
            CssBorderRadiusTests.Layout(host, 100, 80);

            Assert.True(slider.RenderSize.Height >= 48);
            Assert.True(((FrameworkElement)slider.FindName(range ? "PART_StartThumb" : "PART_Thumb")!)
                .RenderSize.Height >= 16);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertBackgroundPixels(byte[] pixels)
    {
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    private static byte Channel(byte[] data, int x, int y, int channel)
        => data[(y * 100 + x) * 4 + channel];

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
