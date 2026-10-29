using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTreeCountingTests
{
    [Fact]
    public void TreeCountingFunctionsHaveNumberTypesAndNoArguments()
    {
        Assert.True(Css.Supports("width", "calc(sibling-index() * 10px)"));
        Assert.True(Css.Supports("height", "calc(5px * sibling-count())"));
        Assert.True(Css.Supports("z-index", "sibling-index()"));
        Assert.False(Css.Supports("width", "calc(sibling-index(1) * 10px)"));
        Assert.False(Css.Supports("width", "calc(10 * sibling-index())"));
    }

    [Fact]
    public void TreeCountingFunctionsUpdateWhenSiblingsChange()
    {
        var root = new StackPanel();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        const string style = "width:calc(sibling-index() * 10px);height:calc(sibling-count() * 5px)";
        Css.SetStyle(first, style);
        Css.SetStyle(second, style);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(10, first.Width);
        Assert.Equal(20, second.Width);
        Assert.Equal(10, first.Height);
        Assert.Equal(10, second.Height);

        root.Children.Remove(first);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(10, second.Width);
        Assert.Equal(5, second.Height);

        root.Children.Add(new Border());
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(10, second.Width);
        Assert.Equal(10, second.Height);
    }

    [Fact]
    public void TreeCountingFunctionsTreatTheRootAsItsOnlySibling()
    {
        var root = new Border();
        Css.SetStyle(root, "width:calc(sibling-index() * 10px);height:calc(sibling-count() * 5px)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(10, root.Width);
        Assert.Equal(5, root.Height);
    }

    [Fact]
    public void RegisteredNumberPropertyUpdatesAfterSiblingRemoval()
    {
        var root = new StackPanel();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        Css.SetStyleSheet(root, "@property --slot { syntax:'<number>'; inherits:false; initial-value:0 }");
        Css.SetStyle(second, "--slot:sibling-index();width:calc(var(--slot) * 10px)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(20, second.Width);

        root.Children.Remove(first);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(10, second.Width);
    }

    [Fact]
    public void ContainerQueriesUseTheContainersSiblingPositionAndCount()
    {
        var root = new StackPanel();
        var preceding = new Border();
        var target = new Border();
        var container = new StackPanel { Width = 100 };
        container.Children.Add(target);
        root.Children.Add(preceding);
        root.Children.Add(container);
        Css.SetClass(target, "target");
        Css.SetStyle(container, "container-type:inline-size");
        Css.SetStyleSheet(root, """
            .target { width:10px;opacity:.9 }
            @container (width = calc(100px * sibling-index())) { .target {opacity:.2} }
            @container (width = calc(50px * sibling-count())) { .target {width:20px} }
            """);
        root.Measure(new(300, 100));
        root.Arrange(new(0, 0, 300, 100));
        root.UpdateLayout();
        Assert.Equal(.9, target.Opacity);
        Assert.Equal(20, target.Width);

        root.Children.Remove(preceding);
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);
        Assert.Equal(10, target.Width);
    }

    [Fact]
    public void GradientColorChannelsResolveForEachSiblingAndAfterTreeChanges()
    {
        const string gradient = "linear-gradient(white, rgb(0 0 calc(sibling-index() * 100)))";
        Assert.True(Css.Supports("background-image", gradient));
        var root = new StackPanel();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        Css.SetStyle(first, "background-image:" + gradient);
        Css.SetStyle(second, "background-image:" + gradient);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var firstBrush = Assert.IsType<LinearGradientBrush>(first.Background);
        var secondBrush = Assert.IsType<LinearGradientBrush>(second.Background);
        Assert.Equal(0xFF000064u, Assert.IsType<LinearGradientBrush>(
            firstBrush.CssGradientLayout!.Resolve(100, 100)).GradientStops[^1].Color.ToArgb());
        Assert.Equal(0xFF0000C8u, Assert.IsType<LinearGradientBrush>(
            secondBrush.CssGradientLayout!.Resolve(100, 100)).GradientStops[^1].Color.ToArgb());

        root.Children.Remove(first);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        secondBrush = Assert.IsType<LinearGradientBrush>(second.Background);
        Assert.Equal(0xFF000064u, Assert.IsType<LinearGradientBrush>(
            secondBrush.CssGradientLayout!.Resolve(100, 100)).GradientStops[^1].Color.ToArgb());
    }

    [Fact]
    public void GradientStopPositionsResolveForEachSibling()
    {
        const string gradient = "linear-gradient(red 0px, blue calc(sibling-index() * 10px))";
        Assert.True(Css.Supports("background-image", gradient));
        var root = new StackPanel();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        Css.SetStyle(first, "background-image:" + gradient);
        Css.SetStyle(second, "background-image:" + gradient);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var firstBrush = Assert.IsType<LinearGradientBrush>(first.Background);
        var secondBrush = Assert.IsType<LinearGradientBrush>(second.Background);
        Assert.InRange(Assert.IsType<LinearGradientBrush>(
            firstBrush.CssGradientLayout!.Resolve(100, 100)).GradientStops[^1].Offset, .099999, .100001);
        Assert.InRange(Assert.IsType<LinearGradientBrush>(
            secondBrush.CssGradientLayout!.Resolve(100, 100)).GradientStops[^1].Offset, .199999, .200001);

        root.Children.Remove(first);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        secondBrush = Assert.IsType<LinearGradientBrush>(second.Background);
        Assert.InRange(Assert.IsType<LinearGradientBrush>(
            secondBrush.CssGradientLayout!.Resolve(100, 100)).GradientStops[^1].Offset, .099999, .100001);
    }

    [Fact]
    public void KeyframeLengthsTrackSiblingOrderChanges()
    {
        var root = new StackPanel();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        Css.SetStyleSheet(root, "@keyframes slot { from { width:calc(100px * sibling-index()) } to { width:0px } }");
        Css.SetStyle(second, "animation:slot 1000s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(second.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp);
        Assert.Equal(200, second.Width);

        root.Children.Remove(first);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Same(runner, second.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp);
        Assert.Equal(100, second.Width);
        Css.SetStyle(second, "");
    }
}
