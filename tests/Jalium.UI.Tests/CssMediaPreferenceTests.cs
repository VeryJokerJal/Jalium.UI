using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMediaPreferenceTests
{
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ObserveDetachedRoot()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root,
            "@media (prefers-reduced-motion: reduce) { .card { opacity: .4 } }");
        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        return new WeakReference(root);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "reduce", false)]
    [InlineData(false, "no-preference", true)]
    [InlineData(true, null, true)]
    [InlineData(true, "reduce", true)]
    [InlineData(true, "no-preference", false)]
    public void ReducedMotion_BooleanAndKeywordSemantics(bool reduced, string? value, bool expected)
        => Assert.Equal(expected ? CssQueryResult.True : CssQueryResult.False,
            CssMediaQuery.EvaluateReducedMotion(reduced, value));

    [Fact]
    public void ReducedMotion_UsesSystemAnimationSettingAndRefreshesOnNotification()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .6 }
            @media (prefers-reduced-motion: reduce) { .card { opacity: .4 } }
            @media (prefers-reduced-motion: no-preference) { .card { opacity: .8 } }
            """);

        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var reduced = !SystemParameters.ClientAreaAnimation || !SystemParameters.UIEffects;
        Assert.Equal(reduced ? .4 : .8, child.Opacity);

        SystemParameters.NotifyStaticPropertyChanged(null);
        Assert.True(CssEvaluationScheduler.HasPending(root.Dispatcher));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(reduced ? .4 : .8, child.Opacity);

        Assert.Equal(reduced, new CssCondition("media", "(prefers-reduced-motion)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-reduced-motion: invalid)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-reduced-motion: 42)").Evaluate(root));
        Assert.False(new CssCondition("media", "not (prefers-reduced-motion > reduce)").Evaluate(root));
    }

    [Fact]
    public void ReducedMotion_WeakObserverDoesNotRetainDetachedTree()
    {
        var root = ObserveDetachedRoot();
        for (var i = 0; i < 3 && root.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(root.IsAlive);
        SystemParameters.NotifyStaticPropertyChanged(null);
    }
}
