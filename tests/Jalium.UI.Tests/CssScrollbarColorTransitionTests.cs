using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssScrollbarColorTransitionTests
{
    [Fact]
    public void PairInterpolationUsesLegacySrgbOrModernOklabForBothColors()
    {
        var legacyFrom = Parse("red blue");
        var legacyTo = Parse("blue yellow");
        var legacy = AtHalf(legacyFrom, legacyTo);
        Assert.InRange(legacy.Thumb.R, 127, 128);
        Assert.Equal((byte)0, legacy.Thumb.G);
        Assert.InRange(legacy.Thumb.B, 127, 128);
        Assert.InRange(legacy.Track.R, 127, 128);
        Assert.InRange(legacy.Track.G, 127, 128);
        Assert.InRange(legacy.Track.B, 127, 128);

        var modernFrom = Parse("color(srgb 1 0 0) color(srgb 0 1 0)");
        var modernTo = Parse("color(srgb 0 0 1) color(srgb 1 0 0)");
        var modern = AtHalf(modernFrom, modernTo);
        Assert.InRange(modern.Thumb.G, 70, 100);
        Assert.InRange(modern.Track.R, 190, 220);
        Assert.InRange(modern.Track.G, 160, 180);
        Assert.InRange(modern.Track.B, 0, 30);

        Assert.Null(AnimationFactory.CreateTransitionAnimation(
            ScrollViewer.CssScrollBarColorsProperty, null, legacyTo,
            TimeSpan.FromSeconds(1), TransitionTimingFunction.Linear));
    }

    [Fact]
    public void TransparentColorInterpolationPremultipliesBothPairs()
    {
        var from = Parse("rgb(255 0 0 / 0) rgb(0 255 0 / 0)");
        var to = Parse("blue red");
        var halfway = AtHalf(from, to);
        Assert.InRange(halfway.Thumb.A, 127, 128);
        Assert.InRange(halfway.Thumb.R, 0, 1);
        Assert.InRange(halfway.Thumb.B, 254, 255);
        Assert.InRange(halfway.Track.A, 127, 128);
        Assert.InRange(halfway.Track.R, 254, 255);
        Assert.InRange(halfway.Track.G, 0, 1);
    }

    [Fact]
    public void CssStepsTimingControlsBothColorChannels()
    {
        var viewer = new ScrollViewer();
        Css.SetStyle(viewer, "transition:scrollbar-color 1s steps(2,end)");
        var data = Assert.IsType<CssTransitionData>(viewer.GetValue(CssTransitions.DataProperty));
        var transition = Assert.IsType<CssTransition>(
            data.Find(viewer, ScrollViewer.CssScrollBarColorsProperty));
        var from = Parse("red blue");
        var to = Parse("blue yellow");
        var animation = Assert.IsType<CssScrollBarColorsAnimation>(
            CssTransitions.Create(viewer, ScrollViewer.CssScrollBarColorsProperty,
                from, to, transition));
        Assert.Same(transition.Timing, animation.EasingFunction);

        var clock = animation.CreateClock();
        const long start = 1_000_000_000;
        clock.BeginAt(start);
        clock.Tick(start + Stopwatch.Frequency * 3 / 10);
        Assert.Same(from, clock.GetCurrentValue(from, to));
        clock.Tick(start + Stopwatch.Frequency * 3 / 4);
        var halfway = Assert.IsType<CssScrollBarColors>(clock.GetCurrentValue(from, to));
        Assert.InRange(halfway.Thumb.R, 127, 128);
        Assert.InRange(halfway.Track.G, 127, 128);
    }

    [Fact]
    public void CssTransitionUpdatesTheViewerBarsAndRestoresBaseValue()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 300, Height = 240 },
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            IsOverlayScrollBarEnabled = false,
            IsScrollBarAutoHideEnabled = false,
        };
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(viewer,
                "scrollbar-color:red blue; transition:scrollbar-color 1s linear");
            CssBorderRadiusTests.Layout(host, 200, 140);
            var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
            Assert.Equal(Colors.Red.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
            Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));

            Css.SetStyle(viewer,
                "scrollbar-color:blue yellow; transition:scrollbar-color 1s linear");
            Assert.True(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarColorsProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);

            var colors = Assert.IsType<CssScrollBarColors>(
                viewer.GetValue(ScrollViewer.CssScrollBarColorsProperty));
            Assert.InRange(colors.Thumb.R, 125, 130);
            Assert.InRange(colors.Thumb.B, 125, 130);
            Assert.InRange(colors.Track.R, 125, 130);
            Assert.InRange(colors.Track.G, 125, 130);
            Assert.Equal(colors.Thumb.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
            Assert.Equal(colors.Track.ToArgb(), ColorOf(bar.Background));

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start +
                Stopwatch.Frequency * 2);
            Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Track.Thumb!.Background));
            Assert.Equal(Colors.Yellow.ToArgb(), ColorOf(bar.Background));
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarColorsProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    private static CssScrollBarColors Parse(string pair)
    {
        var viewer = new ScrollViewer();
        Css.SetStyle(viewer, $"scrollbar-color:{pair}");
        CssEvaluationScheduler.FlushIfPending(viewer.Dispatcher);
        return Assert.IsType<CssScrollBarColors>(viewer.CssScrollBarColors);
    }

    private static CssScrollBarColors AtHalf(CssScrollBarColors from,
        CssScrollBarColors to)
    {
        var animation = Assert.IsType<CssScrollBarColorsAnimation>(
            AnimationFactory.CreateTransitionAnimation(
                ScrollViewer.CssScrollBarColorsProperty, from, to,
                TimeSpan.FromSeconds(1), TransitionTimingFunction.Linear));
        var clock = animation.CreateClock();
        const long start = 1_000_000_000;
        clock.BeginAt(start);
        clock.Tick(start + Stopwatch.Frequency / 2);
        return Assert.IsType<CssScrollBarColors>(clock.GetCurrentValue(from, to));
    }

    private static uint ColorOf(Brush? brush)
        => Assert.IsType<SolidColorBrush>(brush).Color.ToArgb();
}
