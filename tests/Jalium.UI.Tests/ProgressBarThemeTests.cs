using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ProgressBarThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void ProgressBar_ImplicitThemeStyle_ShouldApplyWithoutLocalHeightOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(progressBar);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(ProgressBar), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(progressBar.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.False(progressBar.HasLocalValue(Control.BackgroundProperty));
            Assert.False(progressBar.HasLocalValue(ProgressBar.ProgressBrushProperty));
            Assert.Equal(6, progressBar.Height);
            Assert.NotNull(progressBar.Background);
            Assert.NotNull(progressBar.ProgressBrush);
            Assert.True(progressBar.RenderSize.Height >= 6);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_ImplicitThemeStyle_ShouldRenderTrackAndIndicatorAsCapsules()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar
            {
                Width = 200,
                Height = 10,
                Value = 25
            };
            var host = new Grid { Width = 240, Height = 40 };
            host.Children.Add(progressBar);

            host.Measure(new Size(240, 40));
            host.Arrange(new Rect(0, 0, 240, 40));

            var track = Assert.IsType<Border>(progressBar.FindName("PART_Track"));
            var indicator = Assert.IsType<Border>(progressBar.FindName("PART_Indicator"));

            Assert.True(track.CornerRadius.TopLeft >= track.RenderSize.Height / 2);
            Assert.Equal(BorderShape.RoundedRectangle, track.Shape);
            Assert.True(indicator.CornerRadius.TopLeft >= indicator.RenderSize.Height / 2);
            Assert.Equal(BorderShape.RoundedRectangle, indicator.Shape);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_TemplateTrack_UsesCssBackgroundAndBorderBoxes()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar
            {
                Width = 80,
                Height = 60,
                Value = 0,
            };
            Css.SetStyle(progressBar,
                "border:8px solid transparent;padding:4px;background-color:blue;" +
                "background-image:linear-gradient(to right,red,lime);" +
                "background-size:20px 10px;background-position:right bottom;" +
                "background-repeat:no-repeat;background-origin:content-box;" +
                "background-clip:content-box");
            var host = new Grid { Width = 80, Height = 60 };
            host.Children.Add(progressBar);
            CssBorderRadiusTests.Layout(host, 80, 60);

            Assert.IsType<Border>(progressBar.FindName("PART_Track"));
            var pixels = CssBorderRadiusTests.Pixels(progressBar, 80, 60);
            static byte Channel(byte[] data, int x, int y, int channel)
                => data[(y * 80 + x) * 4 + channel];
            Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
            Assert.Equal((byte)255, Channel(pixels, 30, 30, 0));
            Assert.True(Channel(pixels, 50, 43, 2) > 100);
            Assert.True(Channel(pixels, 58, 41, 1) > 100);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_TemplateIndicator_UsesCssContentWidth()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar
            {
                Width = 80,
                Height = 60,
                Value = 50,
                ProgressBrush = Brushes.Red,
            };
            Css.SetStyle(progressBar,
                "border:8px solid transparent;padding:4px;background-color:blue;" +
                "background-clip:content-box");
            var host = new Grid { Width = 80, Height = 60 };
            host.Children.Add(progressBar);
            CssBorderRadiusTests.Layout(host, 80, 60);

            var pixels = CssBorderRadiusTests.Pixels(progressBar, 80, 60);
            static byte Channel(byte[] data, int x, int y, int channel)
                => data[(y * 80 + x) * 4 + channel];
            Assert.Equal((byte)0, Channel(pixels, 8, 30, 3));
            Assert.Equal((byte)255, Channel(pixels, 20, 30, 2));
            Assert.True(Channel(pixels, 45, 30, 2) < 200);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_TemplateTrack_PaintsCssBorderSides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar { Width = 80, Height = 60, Value = 0 };
            Css.SetStyle(progressBar, "border:8px solid;border-color:red green blue yellow");
            var host = new Grid { Width = 80, Height = 60 };
            host.Children.Add(progressBar);
            CssBorderRadiusTests.Layout(host, 80, 60);

            var pixels = CssBorderRadiusTests.Pixels(progressBar, 80, 60);
            static byte Channel(byte[] data, int x, int y, int channel)
                => data[(y * 80 + x) * 4 + channel];
            Assert.Equal((byte)255, Channel(pixels, 40, 4, 2));
            Assert.Equal((byte)128, Channel(pixels, 76, 30, 1));
            Assert.Equal((byte)255, Channel(pixels, 40, 56, 0));
            Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_TemplateIndicator_ReflowsAfterCssEdgesChange()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar { Width = 80, Height = 60, Value = 50 };
            var host = new Grid { Width = 80, Height = 60 };
            host.Children.Add(progressBar);
            Css.SetStyle(progressBar, "border:8px solid transparent;padding:4px");
            CssBorderRadiusTests.Layout(host, 80, 60);
            var indicator = Assert.IsType<Border>(progressBar.FindName("PART_Indicator"));
            Assert.Equal(28, indicator.Width);

            Css.SetStyle(progressBar, string.Empty);
            CssBorderRadiusTests.Layout(host, 80, 60);
            Assert.Equal(40, indicator.Width);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_TemplateVerticalAndIndeterminateIndicators_UseCssContentBox()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar
            {
                Width = 80, Height = 60, Orientation = Orientation.Vertical,
                Value = 50, ProgressBrush = Brushes.Red,
            };
            Css.SetStyle(progressBar,
                "border:8px solid transparent;padding:4px;background-color:blue;background-clip:content-box");
            var host = new Grid { Width = 80, Height = 60 };
            host.Children.Add(progressBar);
            CssBorderRadiusTests.Layout(host, 80, 60);

            var indicator = Assert.IsType<Border>(progressBar.FindName("PART_Indicator"));
            Assert.Equal(18, indicator.Height);
            var pixels = CssBorderRadiusTests.Pixels(progressBar, 80, 60);
            Assert.Equal((byte)255, pixels[(40 * 80 + 40) * 4 + 2]);
            Assert.True(pixels[(20 * 80 + 40) * 4 + 2] < 200);

            progressBar.IsIndeterminate = true;
            progressBar.UpdateIndeterminateAnimation(0.5);
            Assert.InRange(indicator.Height, 10.7, 10.9);
            Assert.InRange(indicator.RenderOffset.Y, 12.5, 12.7);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var sliderTrack = Assert.IsAssignableFrom<Brush>(app.Resources["SliderTrack"]);
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var disabledAccentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrushDisabled"]);
            var progressBar = new ProgressBar();

            Assert.Same(sliderTrack, InvokePrivateBrushResolver(progressBar, "ResolveTrackBrush"));
            Assert.Same(accentBrush, InvokePrivateBrushResolver(progressBar, "ResolveProgressBrush"));

            progressBar.IsEnabled = false;
            Assert.Same(disabledAccentBrush, InvokePrivateBrushResolver(progressBar, "ResolveProgressBrush"));

            var cornerRadius = InvokePrivateCornerRadiusResolver(progressBar, "ResolveCornerRadius");
            Assert.Equal(4, cornerRadius.TopLeft);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(ProgressBar progressBar, string methodName)
    {
        var method = typeof(ProgressBar).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(progressBar, null));
    }

    private static CornerRadius InvokePrivateCornerRadiusResolver(ProgressBar progressBar, string methodName)
    {
        var method = typeof(ProgressBar).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<CornerRadius>(method!.Invoke(progressBar, null));
    }
}
