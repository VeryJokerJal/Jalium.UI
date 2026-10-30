using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTransformBoxTests
{
    [Theory]
    [InlineData("content-box", 45, 25, .125, .1875)]
    [InlineData("fill-box", 45, 25, .125, .1875)]
    [InlineData("border-box", 60, 40, 0, 0)]
    [InlineData("stroke-box", 60, 40, 0, 0)]
    [InlineData("view-box", 60, 40, 0, 0)]
    public void BoxSelectsOneReferenceForTranslationAndOrigin(
        string box, double translateX, double translateY, double originX, double originY)
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        // The reference-box declaration deliberately follows both consumers.
        Css.SetStyle(child, $"transform: translate(50%, 50%); transform-origin: left top; " +
            $"transform-box: {box}; padding: 10px; border: 5px solid black");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var transform = Assert.IsType<TranslateTransform>(child.RenderTransform);
        Assert.Equal(translateX, transform.X, 10);
        Assert.Equal(translateY, transform.Y, 10);
        Assert.Equal(originX, child.RenderTransformOrigin.X, 10);
        Assert.Equal(originY, child.RenderTransformOrigin.Y, 10);
    }

    [Fact]
    public void ContentBoxFollowsCssAndNativeInsetChanges()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        const string transform = "transform-box: content-box; transform: translateX(50%); transform-origin: left top; border: 5px solid black; ";
        Css.SetStyle(child, transform + "padding: 10px");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(45, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Assert.Equal(.125, child.RenderTransformOrigin.X, 10);

        Css.SetStyle(child, transform + "padding: 20px");
        Assert.Equal(35, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Assert.Equal(25.0 / 120, child.RenderTransformOrigin.X, 10);

        child.Padding = new Thickness(4);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(51, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Assert.Equal(9.0 / 120, child.RenderTransformOrigin.X, 10);

        child.BorderThickness = new Thickness(2);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(54, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Assert.Equal(6.0 / 120, child.RenderTransformOrigin.X, 10);
    }

    [Fact]
    public void RemovingContentBoxRestoresBorderBoxBasis()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        Css.SetStyle(child, "transform-box: content-box; padding: 10px; transform: translateX(50%); transform-origin: left top");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Assert.Equal(10.0 / 120, child.RenderTransformOrigin.X, 10);

        Css.SetStyle(child, "padding: 10px; transform: translateX(50%); transform-origin: left top");
        Assert.Equal(60, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Assert.Equal(0, child.RenderTransformOrigin.X, 10);
    }

    [Fact]
    public void InitialAndInheritedBoxResolveThroughTheCascade()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        Css.SetStyle(root, "transform-box: content-box");
        Css.SetStyle(child, "transform-box: inherit; padding: 10px; transform: translateX(50%)");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);

        Css.SetStyle(child, "transform-box: initial; padding: 10px; transform: translateX(50%)");
        Assert.Equal(60, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
    }

    [Fact]
    public void ContentBoxOffsetsFarEdgeAndCalculatedOrigin()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        const string chrome = "transform-box: content-box; padding: 10px 20px; border: 5px solid black; ";
        Css.SetStyle(child, chrome + "transform-origin: right bottom");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(95.0 / 120, child.RenderTransformOrigin.X, 10);
        Assert.Equal(65.0 / 80, child.RenderTransformOrigin.Y, 10);

        Css.SetStyle(child, chrome + "transform-origin: calc(50% + 10px) top");
        Assert.Equal(70.0 / 120, child.RenderTransformOrigin.X, 10);
        Assert.Equal(15.0 / 80, child.RenderTransformOrigin.Y, 10);
    }

    [Fact]
    public void UnsupportedBoxKeywordLeavesTheInitialBorderBoxBasis()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        Css.SetStyle(child, "transform-box: padding-box; padding: 10px; transform: translateX(50%)");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(60, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
    }

    [Fact]
    public void NativeStyleLayersDetermineTheUsedContentBox()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        child.SetLayerValue(Border.PaddingProperty, new Thickness(8),
            DependencyObject.LayerValueSource.StyleSetter);
        Css.SetStyle(child, "transform-box: content-box; padding: 10px; transform: translateX(50%)");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);

        child.SetLayerValue(Border.PaddingProperty, new Thickness(4),
            DependencyObject.LayerValueSource.TemplateTrigger);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(56, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);

        child.ClearLayerValue(Border.PaddingProperty, DependencyObject.LayerValueSource.TemplateTrigger);
        Css.SetStyle(child, "transform-box: content-box; transform: translateX(50%)");
        Assert.Equal(52, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
    }

    [Fact]
    public void PercentagePaddingAndKeyframesUseTheContentReferenceSize()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes move { from { transform: translateX(0%) } to { transform: translateX(100%) } }");
        Css.SetStyle(child, "transform-box: content-box; padding: 10%; transform: translateX(50%)");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var padding = child.Padding.Left;
        Assert.Equal(30, padding, 10);
        var contentWidth = child.ActualWidth - 2 * padding;
        Assert.Equal(contentWidth / 2, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);

        var rule = Assert.Single(CssStyleSheet.Parse(
            "@keyframes move { from { transform: translateX(0%) } to { transform: translateX(100%) } }").Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(child), rule)[UIElement.RenderTransformProperty];
        Assert.Equal(contentWidth / 2,
            Assert.IsType<TranslateTransform>(sampler.Sample(.5, new CssTimingFunction("linear"))).X, 10);
    }

    [Fact]
    public void DiscreteTransitionSwitchesTheReferenceBoxAtTheMidpoint()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        const string shared = "padding:10px; transform:translateX(50%); transform-origin:left top; " +
            "transition:transform-box 1s linear allow-discrete";
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child, "transform-box:border-box; " + shared);
            root.Measure(new Size(300, 200));
            root.Arrange(new Rect(0, 0, 300, 200));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

            Css.SetStyle(child, "transform-box:content-box; " + shared);
            Assert.True(child.HasAutomaticTransition(CssTransformReferenceBox.ValueProperty));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Assert.Equal(60, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
            Assert.Equal(0, child.RenderTransformOrigin.X, 10);

            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Assert.Equal(60, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
            Assert.Equal(10.0 / 120, child.RenderTransformOrigin.X, 10);
        }
        finally
        {
            child.StopAutomaticTransition(CssTransformReferenceBox.ValueProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void KeyframeBoxSamplesRefreshTheDependentTranslation()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        Css.SetStyle(child, "transform-box:border-box; padding:10px; transform:translateX(50%)");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var rule = Assert.Single(CssStyleSheet.Parse(
            "@keyframes box { from { transform-box:border-box } to { transform-box:content-box } }").Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(child), rule)[CssTransformReferenceBox.ValueProperty];
        var linear = new CssTimingFunction("linear");
        try
        {
            child.SetCssAnimatedValue(CssTransformReferenceBox.ValueProperty, sampler.Sample(.25, linear));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Assert.Equal(60, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);

            child.SetCssAnimatedValue(CssTransformReferenceBox.ValueProperty, sampler.Sample(.5, linear));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        }
        finally
        {
            child.ClearCssAnimatedValue(CssTransformReferenceBox.ValueProperty);
        }
    }

    [Fact]
    public void NormalTransitionBehaviorChangesTheBoxImmediately()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        const string shared = "padding:10px; transform:translateX(50%); transition:transform-box 1s linear";
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child, "transform-box:border-box; " + shared);
            root.Measure(new Size(300, 200));
            root.Arrange(new Rect(0, 0, 300, 200));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

            Css.SetStyle(child, "transform-box:content-box; " + shared);
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Assert.False(child.HasAutomaticTransition(CssTransformReferenceBox.ValueProperty));
            Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        }
        finally
        {
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void CompletedKeyframeBoxUpdatesTheLiveTransform()
    {
        var child = new Border { Width = 120, Height = 80 };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root,
            "@keyframes box { from { transform-box:border-box } to { transform-box:content-box } }");
        Css.SetStyle(child,
            "transform-box:border-box; padding:10px; transform:translateX(50%); " +
            "animation:box 0s linear forwards");
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Equal(CssTransformBox.ContentBox, child.GetValue(CssTransformReferenceBox.ValueProperty));
        Assert.Equal(50, Assert.IsType<TranslateTransform>(child.RenderTransform).X, 10);
        Css.SetStyle(child, "");
    }
}
