using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssBackgroundTextClipTests
{
    [Theory]
    [InlineData("background-clip", "text", true)]
    [InlineData("-webkit-background-clip", "text", true)]
    [InlineData("background-clip", "TEXT,content-box,text", true)]
    [InlineData("background-clip", "text,", false)]
    [InlineData("background-clip", "text text", false)]
    [InlineData("background-clip", "padding-box text", false)]
    [InlineData("background-origin", "text", false)]
    [InlineData("background-origin", "padding-box,text", false)]
    [InlineData("background", "linear-gradient(red,blue) text", true)]
    [InlineData("background", "red text", true)]
    [InlineData("background", "red padding-box text", true)]
    [InlineData("background", "red text content-box", true)]
    [InlineData("background", "image(red) text,red content-box", true)]
    [InlineData("background", "red text text", false)]
    [InlineData("background", "red border-box padding-box text", false)]
    [InlineData("background", "red text padding-box content-box", false)]
    public void GrammarDistinguishesPositioningAreaFromTextClip(string property, string value, bool accepted)
        => Assert.Equal(accepted, Css.Supports(property, value));

    [Theory]
    [InlineData("background:red text", (int)CssBackgroundBox.Padding)]
    [InlineData("background:red content-box text", (int)CssBackgroundBox.Content)]
    [InlineData("background:red text border-box", (int)CssBackgroundBox.Border)]
    public void ShorthandKeepsTextOutOfOrigin(string style, int expected)
    {
        var owner = new Border(); Attach(owner, style);
        var state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(owner));
        Assert.Equal(CssBackgroundBox.Text, state.ColorClip);
        Assert.All(state.Origins, origin => Assert.Equal((CssBackgroundBox)expected, origin));
    }

    [Fact]
    public void DescendantPaintAndCompositionChangesInvalidateTheBackgroundOwner()
    {
        var child = new TextBlock { Text = "CSS" }; var owner = new Border { Child = child };
        Attach(owner, "background:red;background-clip:text");
        owner.ClearRenderDirty(); child.ClearRenderDirty();
        child.SetRenderDirty(); Assert.True(owner.IsRenderDirty);
        owner.ClearRenderDirty(); child.ClearRenderDirty();
        child.MarkSubtreeDirtyForComposition(); Assert.True(owner.IsRenderDirty);
    }

    [Fact]
    public void OrdinaryBoxBackgroundDoesNotInvalidateForChildComposition()
    {
        var child = new TextBlock { Text = "CSS" }; var owner = new Border { Child = child };
        Attach(owner, "background:red;background-clip:padding-box");
        owner.ClearRenderDirty(); child.ClearRenderDirty();
        child.MarkSubtreeDirtyForComposition(); Assert.False(owner.IsRenderDirty);
    }

    [Fact]
    public void DetachedTextLayoutInvalidatesItsOwnAndAncestorTextClipDrawing()
    {
        var child = new TextBox { Text = "CSS" }; var owner = new Border { Child = child };
        Attach(owner, "background:red;background-clip:text");
        Assert.Null(owner.GetWindowHostOrNull());
        owner.ClearRenderDirty(); child.ClearRenderDirty();
        child.Text += " Q";
        Assert.True(child.IsRenderDirty);
        Assert.True(owner.IsRenderDirty);
        Assert.Equal("CSS Q", child.GetLineText(0));
    }

    [Fact]
    public void CompletionValuesDoNotOfferTextForBackgroundOrigin()
    {
        Assert.False(CssAuthoring.IsValueSupported("background-origin", "text"));
        Assert.True(CssAuthoring.IsValueSupported("background-clip", "text"));
    }

    private static void Attach(FrameworkElement owner, string style)
    {
        Css.SetClass(owner, "owner");
        var sheet = CssStyleSheet.Parse(".owner{width:240px;height:90px;" + style + "}");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet);
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.GetForCurrentThread().ProcessQueue();
            owner.Measure(new Size(240,90)); owner.Arrange(new Rect(0,0,240,90)); owner.UpdateLayout();
        }
    }
}
