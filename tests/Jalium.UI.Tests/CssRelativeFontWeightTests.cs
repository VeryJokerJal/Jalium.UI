using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssRelativeFontWeightTests
{
    [Theory]
    [InlineData(1, 400, 1)]
    [InlineData(99, 400, 99)]
    [InlineData(100, 400, 100)]
    [InlineData(349, 400, 100)]
    [InlineData(350, 700, 100)]
    [InlineData(549, 700, 100)]
    [InlineData(550, 900, 400)]
    [InlineData(749, 900, 400)]
    [InlineData(750, 900, 700)]
    [InlineData(899, 900, 700)]
    [InlineData(900, 900, 700)]
    [InlineData(999, 999, 700)]
    [InlineData(1000, 1000, 700)]
    public void RelativeWeightUsesTheInheritedWeightTable(int parentWeight, int bolder, int lighter)
    {
        var parent = new StackPanel();
        var boldChild = new TextBlock();
        var lightChild = new TextBlock();
        parent.Children.Add(boldChild);
        parent.Children.Add(lightChild);

        Css.SetStyle(parent, $"font-weight:{parentWeight}");
        Css.SetStyle(boldChild, "font-weight:bolder");
        Css.SetStyle(lightChild, "font-weight:lighter");

        Assert.Equal(bolder, boldChild.FontWeight.ToOpenTypeWeight());
        Assert.Equal(lighter, lightChild.FontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void RelativeWeightAndFontShorthandUpdateWhenTheParentChanges()
    {
        var parent = new StackPanel();
        var child = new TextBlock();
        parent.Children.Add(child);
        Css.SetStyle(parent, "font-weight:700");
        Css.SetStyle(child, "font:italic lighter 16px Arial");
        Assert.Equal(400, child.FontWeight.ToOpenTypeWeight());

        Css.SetStyle(parent, "font-weight:400");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(100, child.FontWeight.ToOpenTypeWeight());

        child.FontWeight = FontWeight.FromOpenTypeWeight(600);
        Assert.Equal(600, child.FontWeight.ToOpenTypeWeight());
        child.ClearValue(TextElement.FontWeightProperty);
        Assert.Equal(100, child.FontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void NestedRelativeWeightsUseTheParentsComputedWeight()
    {
        var grandparent = new StackPanel();
        var parent = new StackPanel();
        var child = new TextBlock();
        grandparent.Children.Add(parent);
        parent.Children.Add(child);
        Css.SetStyle(grandparent, "font-weight:400");
        Css.SetStyle(parent, "font-weight:bolder");
        Css.SetStyle(child, "font-weight:bolder");
        Assert.Equal(700, parent.GetValue(TextElement.FontWeightProperty) is FontWeight w ? w.ToOpenTypeWeight() : 0);
        Assert.Equal(900, child.FontWeight.ToOpenTypeWeight());

        Css.SetStyle(grandparent, "font-weight:100");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(400, parent.GetValue(TextElement.FontWeightProperty) is FontWeight updated ? updated.ToOpenTypeWeight() : 0);
        Assert.Equal(700, child.FontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void RelativeWeightAppliesToDocumentRuns()
    {
        var paragraph = new Paragraph();
        var run = new Run("text");
        paragraph.Inlines.Add(run);
        Css.SetStyle(paragraph, "font-weight:700");
        Css.SetStyle(run, "font-weight:lighter");
        Assert.Equal(400, run.FontWeight.ToOpenTypeWeight());
    }

    [Theory]
    [InlineData("font-weight:1000")]
    [InlineData("font:1000 16px Arial")]
    public void CssWeight1000RetainsItsComputedWeight(string declaration)
    {
        var block = new TextBlock();
        Css.SetStyle(block, declaration);
        Assert.Equal(1000, block.FontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void Weight1000SurvivesInheritanceAndNativeParentChanges()
    {
        var parent = new StackPanel();
        var child = new TextBlock();
        parent.Children.Add(child);

        Css.SetStyle(parent, "font-weight:1000");
        Css.SetStyle(child, "font-weight:bolder");
        Assert.Equal(1000, child.FontWeight.ToOpenTypeWeight());

        parent.SetValue(TextElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(900));
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(900, child.FontWeight.ToOpenTypeWeight());

        parent.ClearValue(TextElement.FontWeightProperty);
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(1000, child.FontWeight.ToOpenTypeWeight());
    }

    [Theory]
    [InlineData("font:bold bolder 16px Arial")]
    [InlineData("font:lighter 700 16px Arial")]
    [InlineData("font:italic oblique 16px Arial")]
    public void FontShorthandRejectsDuplicatePrefixComponents(string declaration)
    {
        var parts = declaration.Split(':', 2);
        Assert.False(Css.Supports(parts[0], parts[1]));
    }
}
