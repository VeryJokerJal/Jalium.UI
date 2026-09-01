using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Jalium.UI.Input;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssInlineStyleTests
{
    [Fact]
    public void Margin_TwoValues_CssOrderMapsToThickness()
    {
        var border = new Border();
        Css.SetStyle(border, "margin: 4px 8px");
        // CSS (vertical, horizontal) → Thickness(left, top, right, bottom).
        Assert.Equal(new Thickness(8, 4, 8, 4), border.Margin);
    }

    [Theory]
    [InlineData("margin: 10px", 10, 10, 10, 10)]
    [InlineData("margin: 1px 2px 3px", 2, 1, 2, 3)]
    [InlineData("margin: 1px 2px 3px 4px", 4, 1, 2, 3)]
    public void Margin_ValueCounts_FollowCssOrdering(string style, double l, double t, double r, double b)
    {
        var border = new Border();
        Css.SetStyle(border, style);
        Assert.Equal(new Thickness(l, t, r, b), border.Margin);
    }

    [Fact]
    public void MarginTop_AloneFillsRemainingEdgesWithZero()
    {
        var border = new Border();
        Css.SetStyle(border, "margin-top: 8px");
        Assert.Equal(new Thickness(0, 8, 0, 0), border.Margin);
    }

    [Fact]
    public void Margin_ShorthandThenLonghand_LonghandWins()
    {
        var border = new Border();
        Css.SetStyle(border, "margin: 4px; margin-top: 9px");
        Assert.Equal(new Thickness(4, 9, 4, 4), border.Margin);
    }

    [Fact]
    public void Margin_AutoDegradesToZero()
    {
        var border = new Border();
        Css.SetStyle(border, "margin: 0 auto");
        Assert.Equal(new Thickness(0, 0, 0, 0), border.Margin);
    }

    [Fact]
    public void Margin_PointUnitsConvert()
    {
        var border = new Border();
        Css.SetStyle(border, "margin: 72pt");
        Assert.Equal(new Thickness(96, 96, 96, 96), border.Margin);
    }

    [Fact]
    public void Padding_AppliesOnBorder_AndClampsNegative()
    {
        var border = new Border();
        Css.SetStyle(border, "padding: -4px 8px");
        Assert.Equal(new Thickness(8, 0, 8, 0), border.Padding);
    }

    [Theory]
    [InlineData("opacity: .5", 0.5)]
    [InlineData("opacity: 50%", 0.5)]
    [InlineData("opacity: 1.5", 1.0)]
    [InlineData("opacity: -1", 0.0)]
    public void Opacity_NumberAndPercent(string style, double expected)
    {
        var border = new Border();
        Css.SetStyle(border, style);
        Assert.Equal(expected, border.Opacity);
    }

    [Fact]
    public void WidthHeight_LengthsAndKeywords()
    {
        var border = new Border();
        Css.SetStyle(border, "width: 120px; height: auto; min-width: 10px; max-height: none");
        Assert.Equal(120.0, border.Width);
        Assert.True(double.IsNaN(border.Height));
        Assert.Equal(10.0, border.MinWidth);
        Assert.Equal(double.PositiveInfinity, border.MaxHeight);
    }

    [Fact]
    public void Em_ResolvesAgainstElementFontSize()
    {
        var block = new TextBlock { FontSize = 20 };
        Css.SetStyle(block, "width: 2em");
        Assert.Equal(40.0, block.Width);
    }

    [Fact]
    public void Display_NoneAndBlock_ToggleVisibility()
    {
        var border = new Border();
        Css.SetStyle(border, "display: none");
        Assert.Equal(Visibility.Collapsed, border.Visibility);

        Css.SetStyle(border, "display: block");
        Assert.Equal(Visibility.Visible, border.Visibility);
    }

    [Fact]
    public void Visibility_ThreeKeywords()
    {
        var border = new Border();
        Css.SetStyle(border, "visibility: hidden");
        Assert.Equal(Visibility.Hidden, border.Visibility);

        Css.SetStyle(border, "visibility: collapse");
        Assert.Equal(Visibility.Collapsed, border.Visibility);

        Css.SetStyle(border, "visibility: visible");
        Assert.Equal(Visibility.Visible, border.Visibility);
    }

    [Fact]
    public void Cursor_PointerMapsToHand()
    {
        var border = new Border();
        Css.SetStyle(border, "cursor: pointer");
        Assert.Same(Cursors.Hand, border.Cursor);

        Css.SetStyle(border, "cursor: not-allowed");
        Assert.Same(Cursors.No, border.Cursor);
    }

    [Fact]
    public void Overflow_HiddenClips()
    {
        var border = new Border();
        Css.SetStyle(border, "overflow: hidden");
        Assert.True(border.ClipToBounds);

        Css.SetStyle(border, "overflow: visible");
        Assert.False(border.ClipToBounds);
    }

    [Fact]
    public void PointerEvents_NoneDisablesHitTesting()
    {
        var border = new Border();
        Css.SetStyle(border, "pointer-events: none");
        Assert.False(border.IsHitTestVisible);
    }

    [Fact]
    public void Fallback_KebabCaseMapsToDependencyProperty()
    {
        var border = new Border();
        Css.SetStyle(border, "horizontal-alignment: Center; focusable: true");
        Assert.Equal(HorizontalAlignment.Center, border.HorizontalAlignment);
        Assert.True(border.Focusable);
    }

    [Fact]
    public void UnsupportedProperty_DoesNotFallThroughToKebabLookup()
    {
        var border = new Border();
        Css.SetStyle(border, "letter-spacing: 2px; opacity: .5");
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void UnknownProperty_IsSkippedWithoutAffectingOthers()
    {
        var border = new Border();
        Css.SetStyle(border, "frob-nicate: 12; width: 30px");
        Assert.Equal(30.0, border.Width);
    }

    [Fact]
    public void InvalidValue_DropsOnlyThatDeclaration()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: chartreuse-dreams; width: 30px");
        Assert.Equal(1.0, border.Opacity);
        Assert.Equal(30.0, border.Width);
    }

    [Fact]
    public void ClearingInlineStyle_RestoresDefaults()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5; margin: 4px; width: 30px");
        Assert.Equal(0.5, border.Opacity);

        Css.SetStyle(border, string.Empty);
        Assert.Equal(1.0, border.Opacity);
        Assert.Equal(new Thickness(0), border.Margin);
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void UpdatingInlineStyle_RemovesStaleProperties()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .5; width: 30px");
        Css.SetStyle(border, "opacity: .7");
        Assert.Equal(0.7, border.Opacity);
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void Important_WithinInline_ProtectsAgainstLaterNormal()
    {
        var border = new Border();
        Css.SetStyle(border, "opacity: .3 !important; opacity: .9");
        Assert.Equal(0.3, border.Opacity);
    }

    [Fact]
    public void ClassAttachedProperty_ParsesSortedUniqueList()
    {
        var border = new Border();
        Css.SetClass(border, "  card   primary card ");
        Assert.Equal(new[] { "card", "primary" }, border.CssRuntimeState!.Classes);
    }

    [Fact]
    public void SameInlineText_SharesCompiledDeclarations()
    {
        var a = new Border();
        var b = new Border();
        Css.SetStyle(a, "margin: 4px; opacity: .5");
        Css.SetStyle(b, "margin: 4px; opacity: .5");
        Assert.Same(a.CssRuntimeState!.InlineDeclarations, b.CssRuntimeState!.InlineDeclarations);
    }
}
