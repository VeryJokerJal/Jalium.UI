using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssBorderStyleTests
{
    [Theory]
    [InlineData("border-width:12px")]
    [InlineData("border-color:red")]
    [InlineData("border-width:12px; border-color:red")]
    public void WidthOrColorWithoutStyle_HasNoLayoutOrPaintedBorder(string css)
    {
        var child = new Border { Width = 20, Height = 10 };
        var border = new Border { Child = child };
        Css.SetStyle(border, css);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        border.Measure(new Size(80, 60));
        Assert.Equal(new Size(20, 10), border.DesiredSize);
        border.Arrange(new Rect(0, 0, 80, 60));
        Assert.Equal(new Thickness(0), border.BorderThickness);
        AssertSide(border, 40, 4, false);
    }

    [Fact]
    public void NativeThicknessStillOverridesAColorOnlyCssBorder()
    {
        var border = new Border { BorderThickness = new Thickness(6) };
        Css.SetStyle(border, "border-color:red");
        CssBorderRadiusTests.Layout(border, 80, 60);
        Assert.Equal(new Thickness(6), border.BorderThickness);
        Assert.Equal((byte)255, Pixel(border, 40, 3).R);
        Assert.Equal((byte)255, Pixel(border, 40, 3).A);

        border.ClearValue(Border.BorderThicknessProperty);
        Assert.Equal(new Thickness(0), border.BorderThickness);
        AssertSide(border, 40, 3, false);
    }

    [Fact]
    public void PartiallySpecifiedWidth_UsesMediumOnOtherSolidSides()
    {
        var border = CssBorderRadiusTests.Box(
            "border-left-width:8px; border-style:solid; border-color:red", 80, 60);
        Assert.Equal(new Thickness(8, 3, 3, 3), border.BorderThickness);
        AssertSide(border, 4, 30, true);
        AssertSide(border, 40, 1, true);
    }

    [Fact]
    public void PartiallySpecifiedStyle_LeavesOtherWidthsAtZero()
    {
        var border = CssBorderRadiusTests.Box(
            "border-width:8px; border-left-style:solid; border-color:red", 80, 60);
        Assert.Equal(new Thickness(8, 0, 0, 0), border.BorderThickness);
        AssertSide(border, 4, 30, true);
        AssertSide(border, 40, 4, false);
    }

    [Fact]
    public void InitialStyle_RemovesTheWidthOfAnEarlierBorder()
    {
        var border = CssBorderRadiusTests.Box(
            "border:8px solid red; border-style:initial", 80, 60);
        Assert.Equal(new Thickness(0), border.BorderThickness);
        AssertSide(border, 4, 30, false);
    }

    [Theory]
    [InlineData("solid none", true, false, true, false)]
    [InlineData("solid none hidden", true, false, false, false)]
    [InlineData("solid none solid hidden", true, false, true, false)]
    public void Shorthand_UsesTopRightBottomLeftOrder(
        string styles, bool top, bool right, bool bottom, bool left)
    {
        var border = CssBorderRadiusTests.Box(
            $"border-width:8px; border-color:red; border-style:{styles}", 80, 60);
        AssertSide(border, 40, 4, top);
        AssertSide(border, 76, 30, right);
        AssertSide(border, 40, 56, bottom);
        AssertSide(border, 4, 30, left);
        Assert.Equal(new Thickness(left ? 8 : 0, top ? 8 : 0, right ? 8 : 0, bottom ? 8 : 0),
            border.BorderThickness);
    }

    [Fact]
    public void LogicalStyles_CascadeWithPhysicalStylesInRtl()
    {
        var border = CssBorderRadiusTests.Box(
            "direction:rtl; border:8px solid red; border-inline-style:none solid; " +
            "border-right-style:solid!important; border-block-end-style:hidden", 80, 60);
        AssertSide(border, 40, 4, true);
        AssertSide(border, 76, 30, true);
        AssertSide(border, 40, 56, false);
        AssertSide(border, 4, 30, true);
    }

    [Fact]
    public void LogicalStyle_RevertLayerFallsBackToPreviousLayer()
    {
        var border = new Border();
        Css.SetClass(border, "box");
        Css.SetStyleSheet(border,
            "@layer base {.box {border:8px solid red; border-left-style:none}} " +
            "@layer app {.box {border-left-style:solid; border-inline-start-style:revert-layer}}");
        CssBorderRadiusTests.Layout(border, 80, 60);
        AssertSide(border, 4, 30, false);
        AssertSide(border, 76, 30, true);
    }

    [Fact]
    public void RoundedBorder_OmitsOnlyTheHiddenSide()
    {
        var border = CssBorderRadiusTests.Box(
            "border:8px solid red; border-radius:20px; border-top-style:none", 80, 60);
        AssertSide(border, 40, 4, false);
        AssertSide(border, 76, 30, true);
        AssertSide(border, 40, 56, true);
        AssertSide(border, 4, 30, true);
    }

    [Fact]
    public void BareStyle_UsesMediumWidthAndCurrentColor()
    {
        var border = CssBorderRadiusTests.Box("color:red; border-style:solid", 80, 60);
        Assert.Equal(new Thickness(3), border.BorderThickness);
        AssertSide(border, 40, 1, true);
    }

    [Fact]
    public void InheritedLogicalStyle_TracksParentCssChanges()
    {
        var child = new Border { Width = 80, Height = 60 };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "border:8px solid red; border-inline-start-style:none");
        Css.SetStyle(child, "border:8px solid red; border-inline-start-style:inherit");
        CssBorderRadiusTests.Layout(parent, 80, 60);
        AssertSide(child, 4, 30, false);
        Css.SetStyle(parent, "border:8px solid red; border-inline-start-style:solid");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        AssertSide(child, 4, 30, true);
    }

    [Fact]
    public void NativeThicknessOverridesCssStyleMask()
    {
        var border = CssBorderRadiusTests.Box("border:8px solid red; border-left-style:none", 80, 60);
        AssertSide(border, 4, 30, false);
        border.BorderThickness = new Thickness(8);
        AssertSide(border, 4, 30, true);
        border.ClearValue(Border.BorderThicknessProperty);
        AssertSide(border, 4, 30, false);
    }

    [Fact]
    public void InvalidShorthand_DoesNotReplaceEarlierStyles()
    {
        var border = CssBorderRadiusTests.Box(
            "border:8px solid red; border-style:solid none; border-style:solid none solid none solid", 80, 60);
        AssertSide(border, 40, 4, true);
        AssertSide(border, 76, 30, false);
    }

    [Theory]
    [InlineData("dashed")]
    [InlineData("dotted")]
    public void PatternedStyles_HaveVisibleMarksAndGaps(string style)
    {
        var border = CssBorderRadiusTests.Box($"border:8px {style} red", 80, 60);
        Assert.Equal(style == "dashed" ? CssBorderLineStyle.Dashed : CssBorderLineStyle.Dotted,
            Assert.IsType<CssBorderStyles>(CssBorderStyleProperties.Get(border)).Top);
        Assert.NotNull(CssBorderPaintProperties.Get(border));
        Assert.Null(border.Background);
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        Assert.Equal((byte)0, pixels[(30 * 80 + 40) * 4 + 3]);
        var visible = Enumerable.Range(12, 56).Count(x => pixels[(4 * 80 + x) * 4 + 3] > 200);
        Assert.InRange(visible, 8, 50);
    }

    [Fact]
    public void DashedStyle_EmitsSegmentsInsteadOfAFilledRing()
    {
        var border = new InspectableBorder();
        Css.SetStyle(border, "border:8px dashed red");
        CssBorderRadiusTests.Layout(border, 80, 60);
        var context = new RecordingContext();
        border.DrawPost(context);
        Assert.Equal(4, context.Draws.Count);
        Assert.All(context.Draws, call =>
        {
            Assert.NotNull(call.Brush);
            Assert.True(Assert.IsType<PathGeometry>(call.Geometry).Figures.Count > 1);
        });
    }

    [Fact]
    public void DoubleStyle_PaintsTwoBandsWithAVisibleGap()
    {
        var border = CssBorderRadiusTests.Box("border:9px double red", 80, 60);
        AssertSide(border, 40, 1, true);
        AssertSide(border, 40, 4, false);
        AssertSide(border, 40, 7, true);
    }

    [Theory]
    [InlineData("double", 78, 1)]
    [InlineData("double", 72, 7)]
    [InlineData("solid double solid double", 78, 1)]
    [InlineData("solid double solid double", 72, 7)]
    public void TranslucentDouble_CompositeVisibleCornerBandsOnce(
        string styles, int x, int y)
    {
        var reference = CssBorderRadiusTests.Box(
            "border:9px solid rgba(128,128,128,.5)", 80, 60);
        var border = CssBorderRadiusTests.Box(
            $"border:9px solid rgba(128,128,128,.5); border-style:{styles}", 80, 60);
        Assert.Equal(Pixel(reference, x, y).A, Pixel(border, x, y).A);
        if (styles == "double") Assert.Equal((byte)0, Pixel(border, 40, 4).A);
    }

    [Fact]
    public void DifferentAlphaDouble_InterpolatesSideColorsAtBothBands()
    {
        var border = CssBorderRadiusTests.Box(
            "border:9px double; " +
            "border-color:rgba(255,0,0,.5) rgba(0,0,255,.25) " +
            "rgba(0,255,0,.75) rgb(128,128,128)", 80, 60);
        foreach (var (x, y) in new[] { (78, 1), (72, 7) })
        {
            var joint = Pixel(border, x, y);
            Assert.InRange(joint.A, (byte)92, (byte)100);
            Assert.InRange(joint.R, (byte)164, (byte)176);
            Assert.InRange(joint.B, (byte)79, (byte)91);
        }
        Assert.Equal((byte)0, Pixel(border, 40, 4).A);
    }

    [Fact]
    public void RoundedTranslucentDouble_PreservesBothBandsAndTheGap()
    {
        var reference = CssBorderRadiusTests.Box(
            "border:9px solid rgba(128,128,128,.5); border-radius:20px", 80, 60);
        var border = CssBorderRadiusTests.Box(
            "border:9px double rgba(128,128,128,.5); border-radius:20px", 80, 60);
        Assert.Equal(Pixel(reference, 73, 6).A, Pixel(border, 73, 6).A);
        Assert.Equal(Pixel(reference, 69, 11).A, Pixel(border, 69, 11).A);
        Assert.Equal((byte)0, Pixel(border, 71, 8).A);
    }

    [Fact]
    public void TranslucentDoubleAndRelief_RespectVisibleBandsAndGapAtJoin()
    {
        var border = CssBorderRadiusTests.Box(
            "border:9px solid rgba(128,128,128,.5); " +
            "border-style:double groove solid ridge", 80, 60);
        Assert.InRange(Pixel(border, 78, 1).A, (byte)120, (byte)136);
        Assert.InRange(Pixel(border, 72, 7).A, (byte)120, (byte)136);
        Assert.InRange(Pixel(border, 75, 4).A, (byte)58, (byte)70);
    }

    [Fact]
    public void TranslucentDoubleAndDotted_KeepPatternAndMiddleGap()
    {
        var border = CssBorderRadiusTests.Box(
            "border:9px solid rgba(128,128,128,.5); " +
            "border-style:dotted solid solid double", 80, 60);
        Assert.InRange(Pixel(border, 2, 2).A, (byte)120, (byte)136);
        Assert.InRange(Pixel(border, 4, 4).A, (byte)58, (byte)70);
        Assert.Equal((byte)0, Pixel(border, 4, 30).A);
    }

    [Fact]
    public void MixedTranslucentStyleImage_FollowsMutableCurrentColor()
    {
        var foreground = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
        var border = new Border();
        border.SetValue(TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border,
            "border-width:9px; border-style:double groove solid dotted; " +
            "border-color:currentColor");
        CssBorderRadiusTests.Layout(border, 80, 60);
        var red = Pixel(border, 40, 1);
        Assert.True(red.R > red.B);
        foreground.Color = Color.FromArgb(128, 0, 0, 255);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        var blue = Pixel(border, 40, 1);
        Assert.True(blue.B > blue.R);
        Assert.Equal(red.A, blue.A);
    }

    [Fact]
    public void RoundedDashedStyle_FollowsTheCornerAndKeepsGaps()
    {
        var border = CssBorderRadiusTests.Box(
            "border:8px dashed red; border-radius:20px", 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        Assert.Equal((byte)0, pixels[3]); // Outside the rounded corner.
        var visible = Enumerable.Range(24, 32).Count(x => pixels[(4 * 80 + x) * 4 + 3] > 200);
        Assert.InRange(visible, 4, 28);
    }

    [Fact]
    public void DifferentLineStyles_StayOnTheirOwnSides()
    {
        var border = CssBorderRadiusTests.Box(
            "border-width:9px; border-color:red; border-style:solid dashed double dotted", 80, 60);
        AssertSide(border, 40, 4, true);
        AssertSide(border, 40, 55, false); // Bottom double gap.
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        var rightMarks = Enumerable.Range(12, 36).Count(y => pixels[(y * 80 + 75) * 4 + 3] > 200);
        var leftMarks = Enumerable.Range(12, 36).Count(y => pixels[(y * 80 + 4) * 4 + 3] > 200);
        Assert.InRange(rightMarks, 4, 32);
        Assert.InRange(leftMarks, 4, 32);
    }

    [Theory]
    [InlineData("inset", false)]
    [InlineData("outset", true)]
    public void InsetAndOutset_UseOpposingSideShades(string style, bool topIsLighter)
    {
        var border = CssBorderRadiusTests.Box($"border:10px {style} #808080", 80, 60);
        var top = Pixel(border, 40, 5);
        var right = Pixel(border, 75, 30);
        var bottom = Pixel(border, 40, 55);
        var left = Pixel(border, 5, 30);
        Assert.Equal((byte)255, top.A);
        Assert.Equal(top.R, left.R);
        Assert.Equal(right.R, bottom.R);
        Assert.Equal(topIsLighter, top.R > bottom.R);
    }

    [Theory]
    [InlineData("groove", false)]
    [InlineData("ridge", true)]
    public void GrooveAndRidge_ReverseTheInnerAndOuterHalfShades(string style, bool outsideIsLighter)
    {
        var border = CssBorderRadiusTests.Box($"border:10px {style} #808080", 80, 60);
        var topOuter = Pixel(border, 40, 2);
        var topInner = Pixel(border, 40, 7);
        var bottomOuter = Pixel(border, 40, 57);
        var bottomInner = Pixel(border, 40, 52);
        Assert.Equal((byte)255, topOuter.A);
        Assert.Equal((byte)255, topInner.A);
        Assert.Equal(outsideIsLighter, topOuter.R > topInner.R);
        Assert.Equal(outsideIsLighter, bottomOuter.R < bottomInner.R);
    }

    [Fact]
    public void ReliefStyles_PreserveAlphaAndWorkWithRoundedCorners()
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px groove rgba(128, 128, 128, 0.5); border-radius:20px", 80, 60);
        Assert.Equal((byte)0, Pixel(border, 0, 0).A);
        Assert.InRange(Pixel(border, 40, 2).A, (byte)120, (byte)136);
        Assert.InRange(Pixel(border, 40, 7).A, (byte)120, (byte)136);
        Assert.True(Pixel(border, 40, 2).R < Pixel(border, 40, 7).R);
    }

    [Theory]
    [InlineData("inset", "", 74, 5)]
    [InlineData("outset", "", 74, 5)]
    [InlineData("groove", "", 74, 5)]
    [InlineData("ridge", "", 74, 5)]
    [InlineData("inset", "border-radius:20px", 70, 9)]
    [InlineData("outset", "border-radius:20px", 70, 9)]
    [InlineData("groove", "border-radius:20px", 70, 9)]
    [InlineData("ridge", "border-radius:20px", 70, 9)]
    public void TranslucentRelief_CompositeCornerOnce(string style, string radius, int x, int y)
    {
        var reference = CssBorderRadiusTests.Box(
            $"border:10px solid rgba(128,128,128,.5);{radius}", 80, 60);
        var relief = CssBorderRadiusTests.Box(
            $"border:10px {style} rgba(128,128,128,.5);{radius}", 80, 60);
        Assert.Equal(Pixel(reference, x, y).A, Pixel(relief, x, y).A);
    }

    [Theory]
    [InlineData("dashed")]
    [InlineData("dotted")]
    public void TranslucentPattern_CornerMarkCompositesOnce(string style)
    {
        var reference = CssBorderRadiusTests.Box("border:10px solid rgba(128,128,128,.5)", 80, 60);
        var pattern = CssBorderRadiusTests.Box($"border:10px {style} rgba(128,128,128,.5)", 80, 60);
        Assert.Equal(Pixel(reference, 5, 5).A, Pixel(pattern, 5, 5).A);
        var top = CssBorderRadiusTests.Pixels(pattern, 80, 60);
        var visible = Enumerable.Range(12, 56).Count(x => top[(5 * 80 + x) * 4 + 3] > 100);
        Assert.InRange(visible, 8, 50);
    }

    [Theory]
    [InlineData("inset", "")]
    [InlineData("outset", "")]
    [InlineData("groove", "")]
    [InlineData("ridge", "")]
    [InlineData("inset", "border-radius:20px")]
    [InlineData("outset", "border-radius:20px")]
    [InlineData("groove", "border-radius:20px")]
    [InlineData("ridge", "border-radius:20px")]
    public void DifferentAlphaRelief_InterpolatesAtCorner(string style, string radius)
    {
        var border = CssBorderRadiusTests.Box(
            $"border:10px {style};" +
            "border-color:rgba(128,128,128,.5) rgba(128,128,128,.25) " +
            $"rgba(128,128,128,.75) rgb(128,128,128);{radius}", 80, 60);
        var (x, y) = radius.Length == 0 ? (74, 5) : (70, 9);
        Assert.InRange(Pixel(border, x, y).A, (byte)92, (byte)100);
    }

    [Theory]
    [InlineData("dashed")]
    [InlineData("dotted")]
    public void DifferentAlphaPattern_InterpolatesAtCornerMark(string style)
    {
        var border = CssBorderRadiusTests.Box(
            $"border:10px {style};" +
            "border-color:rgba(255,0,0,.5) rgba(0,255,0,.5) " +
            "rgba(0,0,255,.5) rgba(0,0,255,.25)", 80, 60);
        var joint = Pixel(border, 5, 5);
        Assert.InRange(joint.A, (byte)92, (byte)100);
        Assert.InRange(joint.R, (byte)164, (byte)176);
        Assert.InRange(joint.B, (byte)79, (byte)91);
    }

    [Fact]
    public void DifferentTranslucentPatternStyles_CompositeSharedCornerMarkOnce()
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid rgba(128,128,128,.5); " +
            "border-style:dotted dashed solid dashed", 80, 60);
        Assert.InRange(Pixel(border, 5, 5).A, (byte)120, (byte)136);
        Assert.Equal((byte)128, Pixel(border, 40, 55).A);
    }

    [Theory]
    [InlineData("")]
    [InlineData("border-radius:20px")]
    public void UnequalWidthTranslucentPatterns_KeepTheirMarksAndGaps(string radius)
    {
        var border = CssBorderRadiusTests.Box(
            "border-width:10px 8px 12px 6px; border-style:dotted dashed solid dotted; " +
            $"border-color:rgba(128,128,128,.5);{radius}", 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        var rightMarks = Enumerable.Range(12, 36).Count(y => pixels[(y * 80 + 75) * 4 + 3] > 100);
        Assert.InRange(rightMarks, 4, 32);
        Assert.Equal((byte)128, Pixel(border, 40, 55).A);
    }

    [Fact]
    public void ReliefShades_RemainVisibleForBlackAndWhiteBaseColors()
    {
        var black = CssBorderRadiusTests.Box("border:10px inset black", 80, 60);
        Assert.Equal((byte)0, Pixel(black, 40, 5).R);
        Assert.True(Pixel(black, 40, 55).R > 0);

        var white = CssBorderRadiusTests.Box("border:10px inset white", 80, 60);
        Assert.True(Pixel(white, 40, 5).R < 255);
        Assert.Equal((byte)255, Pixel(white, 40, 55).R);
    }

    [Fact]
    public void ReliefShades_FollowMutableCurrentColor()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var border = new Border();
        border.SetValue(TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border, "border:10px inset currentColor");
        CssBorderRadiusTests.Layout(border, 80, 60);
        Assert.True(Pixel(border, 40, 5).R > Pixel(border, 40, 5).B);
        foreground.Color = Colors.Blue;
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.True(Pixel(border, 40, 5).B > Pixel(border, 40, 5).R);
    }

    [Fact]
    public void TranslucentReliefImage_FollowsMutableCurrentColor()
    {
        var foreground = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
        var border = new Border();
        border.SetValue(TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border, "border:10px groove currentColor");
        CssBorderRadiusTests.Layout(border, 80, 60);
        var red = Pixel(border, 40, 2);
        Assert.True(red.R > red.B);
        foreground.Color = Color.FromArgb(128, 0, 0, 255);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        var blue = Pixel(border, 40, 2);
        Assert.True(blue.B > blue.R);
        Assert.Equal(red.A, blue.A);
    }

    [Fact]
    public void LogicalBorderShorthands_SetBothComponentsOnTheirAxes()
    {
        var border = CssBorderRadiusTests.Box(
            "border-inline:8px solid red; border-block:5px solid blue", 80, 60);
        Assert.Equal(new Thickness(8, 5, 8, 5), border.BorderThickness);
        Assert.Equal((byte)255, Pixel(border, 4, 30).R);
        Assert.Equal((byte)255, Pixel(border, 76, 30).R);
        Assert.Equal((byte)255, Pixel(border, 40, 2).B);
        Assert.Equal((byte)255, Pixel(border, 40, 57).B);
    }

    [Fact]
    public void LogicalSideShorthand_MapsAfterRtlAndCompetesWithPhysicalSide()
    {
        var border = CssBorderRadiusTests.Box(
            "direction:rtl; border-left:8px solid red; border-inline-start:8px solid blue", 80, 60);
        Assert.Equal((byte)255, Pixel(border, 4, 30).R);
        Assert.Equal((byte)255, Pixel(border, 76, 30).B);
        AssertSide(border, 40, 4, false);
    }

    [Fact]
    public void LogicalBorderShorthand_InheritTransfersAllThreeComponentsAcrossDirections()
    {
        var child = new Border();
        var parent = new Border { Child = child };
        Css.SetStyle(parent,
            "direction:ltr; border-inline-start:8px solid red; border-inline-end:5px dashed blue");
        Css.SetStyle(child, "direction:rtl; border-inline:inherit");
        CssBorderRadiusTests.Layout(parent, 100, 80);
        Assert.Equal(new Thickness(5, 0, 8, 0), child.BorderThickness);
        var styles = Assert.IsType<CssBorderStyles>(CssBorderStyleProperties.Get(child));
        Assert.Equal(CssBorderLineStyle.Dashed, styles.Left);
        Assert.Equal(CssBorderLineStyle.Solid, styles.Right);
        var colors = Assert.IsType<CssBorderPaint>(CssBorderPaintProperties.Get(child));
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(colors.Left).Color);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(colors.Right).Color);
    }

    [Fact]
    public void SideShorthand_ResetsOmittedStyleToNone()
    {
        var border = CssBorderRadiusTests.Box(
            "border:8px solid red; border-left:8px blue", 80, 60);
        AssertSide(border, 4, 30, false);
        AssertSide(border, 76, 30, true);
    }

    [Fact]
    public void InvalidSideShorthand_DoesNotReplaceEarlierValues()
    {
        var border = CssBorderRadiusTests.Box(
            "border-left:8px solid red; border-left:8px solid solid", 80, 60);
        AssertSide(border, 4, 30, true);
    }

    [Fact]
    public void StyleAndSideShorthandSupports_RejectExtraComponents()
    {
        Assert.True(Css.Supports("border-inline-style", "dotted dashed"));
        Assert.True(Css.Supports("border-block", "8px double red"));
        Assert.False(Css.Supports("border-inline-style", "solid dashed dotted"));
        Assert.False(Css.Supports("border-left", "8px solid solid"));
    }

    private static (byte B, byte G, byte R, byte A) Pixel(Border border, int x, int y)
    {
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        var offset = (y * 80 + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static void AssertSide(Border border, int x, int y, bool visible)
    {
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        var offset = (y * 80 + x) * 4;
        Assert.Equal(visible ? (byte)255 : (byte)0, pixels[offset + 3]);
    }

    private sealed class InspectableBorder : Border
    {
        internal void DrawPost(DrawingContext context) => base.OnPostRender(context);
    }

    private sealed class RecordingContext : DrawingContextAdapter
    {
        internal readonly List<(Brush? Brush, Pen? Pen, Geometry Geometry)> Draws = [];
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Draws.Add((brush, pen, geometry));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
