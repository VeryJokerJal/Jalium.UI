using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>CSS ↔ FlexPanel wiring: keywords, the flex shorthand, gap, display, interception.</summary>
public sealed class CssFlexMappingTests
{
    static CssFlexMappingTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    [Theory]
    [InlineData("flex-direction: row-reverse", nameof(FlexPanel.Direction), (int)FlexDirection.RowReverse)]
    [InlineData("flex-direction: column", nameof(FlexPanel.Direction), (int)FlexDirection.Column)]
    [InlineData("flex-wrap: wrap", nameof(FlexPanel.Wrap), (int)FlexWrap.Wrap)]
    [InlineData("flex-wrap: wrap-reverse", nameof(FlexPanel.Wrap), (int)FlexWrap.WrapReverse)]
    [InlineData("justify-content: space-between", nameof(FlexPanel.JustifyContent), (int)FlexJustify.SpaceBetween)]
    [InlineData("justify-content: start", nameof(FlexPanel.JustifyContent), (int)FlexJustify.FlexStart)]
    [InlineData("align-items: center", nameof(FlexPanel.AlignItems), (int)FlexAlign.Center)]
    [InlineData("align-items: baseline", nameof(FlexPanel.AlignItems), (int)FlexAlign.FlexStart)]
    [InlineData("align-content: space-evenly", nameof(FlexPanel.AlignContent), (int)FlexContentAlign.SpaceEvenly)]
    public void ContainerKeywords_MapToFlexPanelProperties(string style, string property, int expected)
    {
        var panel = new FlexPanel();
        Css.SetStyle(panel, style);
        var value = property switch
        {
            nameof(FlexPanel.Direction) => (int)panel.Direction,
            nameof(FlexPanel.Wrap) => (int)panel.Wrap,
            nameof(FlexPanel.JustifyContent) => (int)panel.JustifyContent,
            nameof(FlexPanel.AlignItems) => (int)panel.AlignItems,
            _ => (int)panel.AlignContent,
        };
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("flex: none", 0.0, 0.0, double.NaN)]
    [InlineData("flex: auto", 1.0, 1.0, double.NaN)]
    [InlineData("flex: initial", 0.0, 1.0, double.NaN)]
    [InlineData("flex: 1", 1.0, 1.0, 0.0)]
    [InlineData("flex: 2 3", 2.0, 3.0, 0.0)]
    [InlineData("flex: 2 150px", 2.0, 1.0, 150.0)]
    [InlineData("flex: 2 3 96px", 2.0, 3.0, 96.0)]
    [InlineData("flex: 1 1 auto", 1.0, 1.0, double.NaN)]
    public void FlexShorthand_ExpandsPerCssTable(string style, double grow, double shrink, double basis)
    {
        var child = new Border();
        Css.SetStyle(child, style);
        Assert.Equal(grow, FlexPanel.GetGrow(child));
        Assert.Equal(shrink, FlexPanel.GetShrink(child));
        if (double.IsNaN(basis))
        {
            Assert.True(double.IsNaN(FlexPanel.GetBasis(child)));
        }
        else
        {
            Assert.Equal(basis, FlexPanel.GetBasis(child));
        }
    }

    [Fact]
    public void ItemProperties_MapToAttachedDps()
    {
        var child = new Border();
        Css.SetStyle(child, "flex-grow: 2; flex-shrink: 0.5; flex-basis: 80px; align-self: center; order: -1");
        Assert.Equal(2.0, FlexPanel.GetGrow(child));
        Assert.Equal(0.5, FlexPanel.GetShrink(child));
        Assert.Equal(80.0, FlexPanel.GetBasis(child));
        Assert.Equal(FlexAlign.Center, FlexPanel.GetAlignSelf(child));
        Assert.Equal(-1, FlexPanel.GetOrder(child));
    }

    [Fact]
    public void Gap_DispatchesToFlexPanelSpacing()
    {
        var panel = new FlexPanel();
        Css.SetStyle(panel, "gap: 4px 8px");
        Assert.Equal(4.0, panel.RowSpacing);
        Assert.Equal(8.0, panel.ColumnSpacing);
    }

    [Fact]
    public void DisplayFlex_IsNoOpOnFlexPanel()
    {
        var panel = new FlexPanel();
        Css.SetStyle(panel, "display: flex");
        Assert.Equal(Visibility.Visible, panel.Visibility);

        Css.SetStyle(panel, "display: none");
        Assert.Equal(Visibility.Collapsed, panel.Visibility);
    }

    [Fact]
    public void FlexDirection_OnStackPanel_InterceptsToOrientation()
    {
        var panel = new StackPanel();
        Assert.Equal(Orientation.Vertical, panel.Orientation);

        Css.SetStyle(panel, "flex-direction: row");
        Assert.Equal(Orientation.Horizontal, panel.Orientation);

        // Rule removal clears the CSS layer and restores the default orientation.
        Css.SetStyle(panel, string.Empty);
        Assert.Equal(Orientation.Vertical, panel.Orientation);
    }

    [Fact]
    public void EndToEnd_StyleSheetFlexOne()
    {
        var fill = new Border { Height = 20 };
        Css.SetClass(fill, "fill");
        var fixedChild = new Border { Width = 50, Height = 20 };
        var panel = new FlexPanel();
        panel.Children.Add(fill);
        panel.Children.Add(fixedChild);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(".fill { flex: 1 }"));
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);

        panel.Measure(new Size(300, 100));
        panel.Arrange(new Rect(0, 0, 300, 100));

        Assert.Equal(250.0, fill.VisualBounds.Width);
        Assert.Equal(50.0, fixedChild.VisualBounds.Width);
    }
}
