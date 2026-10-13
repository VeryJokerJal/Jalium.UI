using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class TextBlockNoWrapInheritanceTests
{
    [Theory]
    [InlineData("加流姆", false)]
    [InlineData("加流姆", true)]
    [InlineData("Jalium Studio", false)]
    [InlineData("Jalium Studio", true)]
    [InlineData("中文 👩‍💻 é", true)]
    public void DefaultNoWrap_RemainsSingleLineInsideChangingClip(string value, bool materializeInlines)
    {
        var text = new TextBlock { Text = value, FontSize = 32 };
        if (materializeInlines) _ = text.Inlines;
        var clip = new Border { Child = text };
        var root = new Border { Child = clip };

        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var naturalWidth = text.DesiredSize.Width;
        var naturalHeight = text.DesiredSize.Height;
        foreach (var width in new[] { 0, 1, 18, 32, naturalWidth, 32, 1, 0, naturalWidth })
        {
            clip.Width = width;
            Arrange(root);
            Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
            Assert.Equal(1, LineCount(text));
            Assert.Equal(naturalHeight, text.DesiredSize.Height, 3);
        }
    }

    [Fact]
    public void DefaultNoWrap_PreservesAuthoredHardBreaksInsideClip()
    {
        var text = new TextBlock { FontSize = 32, Text = "加流姆\nJalium Studio" };
        var root = new Border { Width = 32, Child = text };
        Arrange(root);
        Assert.Equal(2, LineCount(text));
    }

    [Theory]
    [InlineData("CssOverflowWrap")]
    [InlineData("CssWordBreak")]
    [InlineData("CssLineBreak")]
    [InlineData("CssLinePadding")]
    [InlineData("CssWordSpacing")]
    [InlineData("CssWordSpaceTransform")]
    [InlineData("CssLetterSpacing")]
    [InlineData("CssTextAutospace")]
    [InlineData("CssHangingPunctuation")]
    [InlineData("CssHyphens")]
    [InlineData("CssHyphenateCharacter")]
    [InlineData("CssHyphenateLimitLines")]
    [InlineData("CssHyphenateLimitLast")]
    [InlineData("CssHyphenateLimitZone")]
    [InlineData("CssHyphenateLimitChars")]
    public void AuthoredInheritedFlowValue_EnablesCssWrappingAndClearingRestoresNoWrap(string propertyName)
    {
        var text = new TextBlock { Text = "加流姆", FontSize = 32 };
        var clip = new Border { Width = 32, Child = text };
        var root = new Border { Child = clip };
        var property = FlowProperty(propertyName);
        Arrange(root);
        Assert.Equal(1, LineCount(text));
        // An explicit declaration of the initial CSS value is still authored.
        // The same value reached only through metadata defaults is not.
        root.SetValue(property, root.GetValue(property));
        Arrange(root);
        Assert.True(LineCount(text) > 1);

        text.TextWrapping = TextWrapping.NoWrap;
        Arrange(root);
        Assert.Equal(1, LineCount(text));

        text.ClearValue(TextBlock.TextWrappingProperty);
        root.ClearValue(property);
        Arrange(root);
        Assert.Equal(1, LineCount(text));
    }

    [Fact]
    public void AuthoredInlineFlowValue_EnablesCssWrappingAndClearingRestoresNoWrap()
    {
        var run = new Run("加流姆");
        var text = new TextBlock { FontSize = 32 };
        text.Inlines.Add(new Span(run));
        var root = new Border { Width = 32, Child = text };
        var property = FlowProperty("CssLetterSpacing");
        Arrange(root);
        Assert.Equal(1, LineCount(text));
        run.SetValue(property, 0d);
        Arrange(root);
        Assert.True(LineCount(text) > 1);

        run.ClearValue(property);
        Arrange(root);
        Assert.Equal(1, LineCount(text));
    }

    [Theory]
    [InlineData("white-space: normal", true)]
    [InlineData("white-space: nowrap", false)]
    [InlineData("word-break: normal", true)]
    [InlineData("letter-spacing: 0px", true)]
    public void CssDeclarations_KeepTheirInheritedWrappingSemantics(string style, bool wrapped)
    {
        var text = new TextBlock { Text = "加流姆", FontSize = 32 };
        var clip = new Border { Width = 32, Child = text };
        var root = new Border { Child = clip };
        Arrange(root);
        Assert.Equal(1, LineCount(text));
        Css.SetStyle(root, style);
        Arrange(root);
        Assert.Equal(wrapped, LineCount(text) > 1);

        Css.SetStyle(root, string.Empty);
        Arrange(root);
        Assert.Equal(1, LineCount(text));
    }

    private static DependencyProperty FlowProperty(string name) => typeof(CssFlowProperties)
        .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .Select(field => field.GetValue(null)).OfType<DependencyProperty>()
        .Single(property => property.Name == name);

    private static int LineCount(TextBlock text) => ((IList)typeof(TextBlock)
        .GetField("_layoutLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(text)!).Count;

    private static void Arrange(Border root)
    {
        root.Measure(new Size(480, 400));
        root.Arrange(new Rect(0, 0, 480, 400));
        root.UpdateLayout();
    }
}
