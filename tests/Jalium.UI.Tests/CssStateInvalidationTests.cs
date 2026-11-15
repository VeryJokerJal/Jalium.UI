using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssStateInvalidationTests
{
    [Fact]
    public void UnrelatedAttributeSelectorDoesNotRecascadeWindowWhenScrollbarMoves()
    {
        var root = new StackPanel();
        var scrollbar = new ScrollBar { Minimum = 0, Maximum = 100, Value = 10 };
        root.Children.Add(scrollbar);
        root.Children.Add(new Border { Tag = "decoration" });
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("Border[Tag='decoration'] { opacity: .75; }"));
        Flush();
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));

        scrollbar.Value = 20;
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
        scrollbar.Maximum = 200;
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Fact]
    public void NativeValueAttributesStillRefreshSiblingSelectors()
    {
        var root = new StackPanel();
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 25 };
        var marker = new Border();
        Css.SetClass(marker, "marker");
        root.Children.Add(slider);
        root.Children.Add(marker);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            Slider[Value='25'] + Border.marker { width: 41px; }
            Slider[Value='30'] + Border.marker { width: 63px; }
            """));
        Flush();
        Assert.Equal(41, marker.Width);
        slider.Value = 30;
        Flush();
        Assert.Equal(63, marker.Width);
    }

    [Fact]
    public void FunctionalCheckedStateAndInheritedEnabledStateStillRefresh()
    {
        var root = new StackPanel();
        Css.SetClass(root, "shell");
        var check = new CheckBox { IsChecked = false };
        var marker = new Border();
        Css.SetClass(marker, "marker");
        root.Children.Add(check);
        root.Children.Add(marker);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            Border.marker { opacity: 1; }
            .shell:has(CheckBox:checked) Border.marker { opacity: .5; }
            .shell:disabled Border.marker { opacity: .25; }
            """));
        Flush();
        Assert.Equal(1, marker.Opacity);
        check.IsChecked = true;
        Flush();
        Assert.Equal(.5, marker.Opacity);
        check.IsChecked = false;
        Flush();
        Assert.Equal(1, marker.Opacity);
        root.IsEnabled = false;
        Flush();
        Assert.Equal(.25, marker.Opacity);
        root.IsEnabled = true;
        Flush();
        Assert.Equal(1, marker.Opacity);
    }

    [Fact]
    public void RenamingElementStillRefreshesFunctionalIdSelectors()
    {
        var root = new StackPanel();
        var marker = new Border { Name = "other" };
        root.Children.Add(marker);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            Border { opacity: 1; }
            Border:is(#chosen) { opacity: .4; }
            """));
        Flush();
        Assert.Equal(1, marker.Opacity);
        marker.Name = "chosen";
        Flush();
        Assert.Equal(.4, marker.Opacity);
        marker.Name = "other";
        Flush();
        Assert.Equal(1, marker.Opacity);
    }

    [Fact]
    public void RangePseudoClassesStillObserveNumericLimits()
    {
        var root = new StackPanel();
        var number = new NumberBox { Minimum = 0, Maximum = 10, Value = 5 };
        var marker = new Border();
        root.Children.Add(number);
        root.Children.Add(marker);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            NumberBox:in-range + Border { opacity: .8; }
            NumberBox:out-of-range + Border { opacity: .3; }
            """));
        Flush();
        Assert.Equal(.8, marker.Opacity);
        number.Text = "20";
        Flush();
        Assert.Equal(.3, marker.Opacity);
    }

    private static void Flush()
    {
        for (int i = 0; i < 8 && CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher); i++)
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }
}
