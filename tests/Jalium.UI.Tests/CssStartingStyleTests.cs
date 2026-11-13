using System.Diagnostics;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssStartingStyleTests
{
    [Fact]
    public void SupportsQueryRecognizesStartingStyleAtRule()
        => Assert.True(Css.Supports("at-rule(@starting-style)"));

    [Theory]
    [InlineData(".card{opacity:1;transition:opacity 1s linear} @starting-style{.card{opacity:0}}")]
    [InlineData(".card{opacity:1;transition:opacity 1s linear;@starting-style{opacity:0}}")]
    [InlineData("@layer theme{.card{opacity:1;transition:opacity 1s linear;@starting-style{opacity:0}}}")]
    [InlineData(".card{opacity:1;transition:opacity 1s linear} @media (width >= 0px){@starting-style{.card{opacity:0}}}")]
    public void InitialStyleStartsOpacityTransitionFromStartingRule(string stylesheet)
    {
        var (host, target) = CreateTarget();
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host, stylesheet);
            Layout(host);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(0, target.Opacity, 4);

            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.InRange(target.Opacity, .45, .55);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void StartingStyleUsesOrdinaryCascadeSpecificity()
    {
        var (host, target) = CreateTarget();
        target.Name = "target";
        Css.SetStyleSheet(host,
            ".card{opacity:.8;transition:opacity 1s linear} #target{opacity:1}" +
            "@starting-style{.card{opacity:0}}");
        Layout(host);
        Assert.False(target.HasAutomaticTransition(UIElement.OpacityProperty));
        Assert.Equal(1, target.Opacity);
    }

    [Fact]
    public void EarlierStartingRuleDoesNotOutrankLaterOrdinaryRule()
    {
        var (host, target) = CreateTarget();
        Css.SetStyleSheet(host,
            "@starting-style{.card{opacity:0}}" +
            ".card{opacity:1;transition:opacity 1s linear}");
        Layout(host);
        Assert.False(target.HasAutomaticTransition(UIElement.OpacityProperty));
        Assert.Equal(1, target.Opacity);
    }

    [Fact]
    public void StartingWidthChangesUsedLayoutWhileTransitionRuns()
    {
        var target = new Border { Height = 20 };
        Css.SetClass(target, "card");
        var host = new Grid { Width = 40, Height = 40 };
        host.Children.Add(target);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{width:30px;transition:width 1s linear}" +
                "@starting-style{.card{width:10px}}");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(FrameworkElement.WidthProperty));
            Assert.Equal(10, target.ActualWidth, 1);

            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Layout(host);
            Assert.InRange(target.ActualWidth, 19, 21);
        }
        finally
        {
            target.StopAutomaticTransition(FrameworkElement.WidthProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void StartingStyleCustomPropertySuppliesInitialValueOnly()
    {
        var (host, target) = CreateTarget();
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{opacity:1;transition:opacity 1s linear}" +
                "@starting-style{.card{--start:.2;opacity:var(--start)}}");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(.2, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void StartingStyleInheritsParentAfterChangeCustomProperty()
    {
        var (host, target) = CreateTarget();
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                "Grid{--entry-opacity:.3}.card{opacity:1;transition:opacity 1s linear}" +
                "@starting-style{.card{opacity:var(--entry-opacity)}}");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(.3, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void NativeLocalOpacityRetainsPriorityOverStartingStyle()
    {
        var (host, target) = CreateTarget();
        target.Opacity = .6;
        Css.SetStyleSheet(host,
            ".card{opacity:1;transition:opacity 1s linear}" +
            "@starting-style{.card{opacity:0}}");
        Layout(host);
        Assert.False(target.HasAutomaticTransition(UIElement.OpacityProperty));
        Assert.Equal(.6, target.Opacity);
    }

    [Fact]
    public void AlreadyArrangedElementUsesItsPresentedOpacity()
    {
        var (host, target) = CreateTarget();
        Layout(host);
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{opacity:.8;transition:opacity 1s linear}" +
                "@starting-style{.card{opacity:0}}");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(1, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void StartingStyleIsNotReusedForOrdinaryStateChanges()
    {
        var (host, target) = CreateTarget();
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{opacity:.8;transition:opacity 1s linear}.card.on{opacity:1}" +
                "@starting-style{.card{opacity:.1}}");
            Layout(host);
            Assert.Equal(.1, target.Opacity, 4);
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);

            Css.SetClass(target, "card on");
            Layout(host);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(.8, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void DisplayNoneToRenderedUsesStartingStyle()
    {
        var (host, target) = CreateTarget();
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyleSheet(host,
                ".card{display:none;opacity:1;transition:opacity 1s linear}" +
                ".card.shown{display:block}" +
                "@starting-style{.card{opacity:0}}");
            Layout(host);
            Assert.Equal(Visibility.Collapsed, target.Visibility);
            Assert.False(target.HasAutomaticTransition(UIElement.OpacityProperty));

            Css.SetClass(target, "card shown");
            Layout(host);
            Assert.Equal(Visibility.Visible, target.Visibility);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(0, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public async Task ImportedStartingRuleRetainsItsOriginAndStartsTransition()
    {
        var main = new Uri("https://example.test/main.css");
        var resolver = new MemoryResolver(new Dictionary<string, string>
        {
            [main.ToString()] = "@import 'start.css';.card{opacity:1;transition:opacity 1s linear}",
            ["https://example.test/start.css"] = "@starting-style{.card.card{opacity:0}}",
        });
        var sheet = await CssStyleSheet.LoadAsync(main, resolver);
        Assert.Contains(sheet.Rules, static rule => rule.StartingStyle);
        var (host, target) = CreateTarget();
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.GetStyleSheets(host).Add(sheet);
            Layout(host);
            Assert.True(target.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(0, target.Opacity, 4);
        }
        finally
        {
            target.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    private sealed class MemoryResolver(Dictionary<string, string> files) : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CssResource(uri,
                new MemoryStream(Encoding.UTF8.GetBytes(files[uri.ToString()]))));
    }

    private static (Grid Host, Border Target) CreateTarget()
    {
        var target = new Border { Width = 20, Height = 20 };
        Css.SetClass(target, "card");
        var host = new Grid { Width = 40, Height = 40 };
        host.Children.Add(target);
        return (host, target);
    }

    private static void Layout(Grid host)
    {
        CssEvaluationScheduler.FlushIfPending(host.Dispatcher);
        host.Measure(new(40, 40));
        host.Arrange(new(0, 0, 40, 40));
        host.UpdateLayout();
    }
}
