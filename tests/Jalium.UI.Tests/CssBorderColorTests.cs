using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssBorderColorTests
{
    [Theory]
    [InlineData("red", "red", "red", "red", "red")]
    [InlineData("red green", "red", "green", "red", "green")]
    [InlineData("red green blue", "red", "green", "blue", "green")]
    [InlineData("red green blue yellow", "red", "green", "blue", "yellow")]
    public void Shorthand_PaintsEachSide(string value, string top, string right, string bottom, string left)
    {
        var border = CssBorderRadiusTests.Box($"border:10px solid black; border-color:{value}", 80, 60);
        AssertSides(border, top, right, bottom, left);
    }

    [Fact]
    public void PhysicalAndLogicalSides_ShareCascadeInRtl()
    {
        var border = CssBorderRadiusTests.Box(
            "direction:rtl; border:10px solid black; border-inline-color:red blue; " +
            "border-left-color:lime!important; border-block-end-color:yellow", 80, 60);
        AssertSides(border, "black", "red", "yellow", "lime");
    }

    [Fact]
    public void RoundedBorder_UsesIndependentSideColors()
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid; border-color:red green blue yellow; border-radius:20px", 80, 60);
        AssertSides(border, "red", "green", "blue", "yellow");
        Assert.Equal((byte)0, Pixel(border, 0, 0).A);
        var reference = CssBorderRadiusTests.Box("border:10px solid red; border-radius:20px", 80, 60);
        Assert.Equal(Pixel(reference, 7, 7).A, Pixel(border, 7, 7).A);
    }

    [Theory]
    [InlineData("", 75, 5, 5, 55)]
    [InlineData("border-radius:20px", 70, 9, 9, 50)]
    public void TranslucentSidesWithSharedAlpha_CompositeCornerJoinsOnce(
        string radius, int topRightX, int topRightY, int bottomLeftX, int bottomLeftY)
    {
        const string alpha = "rgba(255,0,0,.5)";
        var reference = CssBorderRadiusTests.Box($"border:10px solid {alpha};{radius}", 80, 60);
        var colored = CssBorderRadiusTests.Box(
            $"border:10px solid {alpha};" +
            "border-color:rgba(255,0,0,.5) rgba(0,255,0,.5) " +
            $"rgba(0,0,255,.5) rgba(255,255,0,.5);{radius}", 80, 60);
        Assert.Equal(Pixel(reference, topRightX, topRightY).A,
            Pixel(colored, topRightX, topRightY).A);
        Assert.Equal(Pixel(reference, bottomLeftX, bottomLeftY).A,
            Pixel(colored, bottomLeftX, bottomLeftY).A);
        Assert.Equal((byte)128, Pixel(colored, 40, 5).A);
    }

    [Theory]
    [InlineData("", 74, 5)]
    [InlineData("border-radius:20px", 70, 9)]
    public void TranslucentSidesWithDifferentAlpha_InterpolateAtCornerJoin(
        string radius, int x, int y)
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid;" +
            "border-color:rgba(128,128,128,.5) rgba(128,128,128,.25) " +
            $"rgba(128,128,128,.75) rgb(128,128,128);{radius}", 80, 60);
        Assert.Equal((byte)128, Pixel(border, 40, 5).A);
        Assert.Equal((byte)64, Pixel(border, 75, 30).A);
        Assert.InRange(Pixel(border, x, y).A, (byte)92, (byte)100);
    }

    [Theory]
    [InlineData("", 74, 5)]
    [InlineData("border-radius:20px", 70, 9)]
    public void DifferentAlphaJoin_InterpolatesPremultipliedColors(
        string radius, int x, int y)
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid;" +
            "border-color:rgba(255,0,0,.5) rgba(0,0,255,.25) " +
            $"rgba(0,255,0,.75) transparent;{radius}", 80, 60);
        var joint = Pixel(border, x, y);
        Assert.InRange(joint.A, (byte)92, (byte)100);
        Assert.InRange(joint.R, (byte)164, (byte)176);
        Assert.InRange(joint.B, (byte)79, (byte)91);
        Assert.Equal((byte)0, joint.G);
    }

    [Fact]
    public void DifferentAlphaSides_CoverAllCornerDirections()
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid;" +
            "border-color:rgba(128,128,128,.5) rgba(128,128,128,.25) " +
            "rgba(128,128,128,.75) rgb(128,128,128)", 80, 60);
        Assert.InRange(Pixel(border, 5, 5).A, (byte)188, (byte)196);
        Assert.InRange(Pixel(border, 74, 5).A, (byte)92, (byte)100);
        Assert.InRange(Pixel(border, 74, 54).A, (byte)124, (byte)132);
        Assert.InRange(Pixel(border, 5, 54).A, (byte)219, (byte)227);
    }

    [Fact]
    public void DifferentAlphaSides_UseAsymmetricWidthJoin()
    {
        var border = CssBorderRadiusTests.Box(
            "border-width:15px 5px 3px 7px; border-style:solid; " +
            "border-color:rgba(128,128,128,.5) rgba(128,128,128,.25) " +
            "rgba(128,128,128,.75) rgb(128,128,128)", 80, 60);
        Assert.InRange(Pixel(border, 77, 7).A, (byte)92, (byte)100);
    }

    [Fact]
    public void CurrentColorAndInherit_ResolveAgainstTheChildColor()
    {
        var child = new Border { Width = 80, Height = 60 };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "color:red; border:10px solid; border-color:currentColor green blue yellow");
        Css.SetStyle(child, "color:purple; border:10px solid; border-top-color:inherit; border-right-color:inherit");
        CssBorderRadiusTests.Layout(parent, 80, 60);
        AssertSides(child, "purple", "green", "purple", "purple");
    }

    [Fact]
    public void WholeColorShorthand_InheritsFourDifferentParentSides()
    {
        var child = new Border { Width = 80, Height = 60 };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "border:10px solid; border-color:red green blue yellow");
        Css.SetStyle(child, "border:10px solid; border-color:inherit");
        CssBorderRadiusTests.Layout(parent, 80, 60);
        AssertSides(child, "red", "green", "blue", "yellow");
    }

    [Fact]
    public void InheritedLogicalColor_TracksNativeParentBrush()
    {
        var child = new Border { Width = 80, Height = 60 };
        var parent = new Border { Child = child, BorderBrush = Brushes.Red };
        Css.SetStyle(child, "border:10px solid black; border-inline-start-color:inherit");
        CssBorderRadiusTests.Layout(parent, 80, 60);
        AssertColor(Pixel(child, 5, 30), "red");
        parent.BorderBrush = Brushes.Blue;
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        AssertColor(Pixel(child, 5, 30), "blue");
    }

    [Fact]
    public void InheritedSideColor_TracksParentCssSideChanges()
    {
        var child = new Border { Width = 80, Height = 60 };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "border:10px solid; border-right-color:red");
        Css.SetStyle(child, "border:10px solid black; border-right-color:inherit");
        CssBorderRadiusTests.Layout(parent, 80, 60);
        AssertColor(Pixel(child, 75, 30), "red");
        Css.SetStyle(parent, "border:10px solid; border-right-color:blue");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        AssertColor(Pixel(child, 75, 30), "blue");
    }

    [Fact]
    public void UnspecifiedSides_TrackMutableCurrentColorBrush()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var border = new Border();
        border.SetValue(TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border, "border-width:10px; border-style:solid; border-top-color:blue");
        CssBorderRadiusTests.Layout(border, 80, 60);
        AssertColor(Pixel(border, 75, 30), "red");
        foreground.Color = Colors.Green;
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        AssertColor(Pixel(border, 75, 30), "green");
    }

    [Fact]
    public void TranslucentSideImage_TracksMutableCurrentColorBrush()
    {
        var foreground = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
        var border = new Border();
        border.SetValue(TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border,
            "border-width:10px; border-style:solid; " +
            "border-right-color:rgba(0,255,0,.5); " +
            "border-bottom-color:rgba(0,0,255,.5); " +
            "border-left-color:rgba(255,255,0,.5)");
        CssBorderRadiusTests.Layout(border, 80, 60);
        var red = Pixel(border, 40, 5);
        Assert.True(red.R > red.B);
        foreground.Color = Color.FromArgb(128, 0, 0, 255);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        var blue = Pixel(border, 40, 5);
        Assert.True(blue.B > blue.R);
        Assert.Equal(red.A, blue.A);
    }

    [Fact]
    public void DifferentAlphaSideImage_TracksMutableCurrentColorBrush()
    {
        var foreground = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
        var border = new Border();
        border.SetValue(TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border,
            "border-width:10px; border-style:solid; " +
            "border-right-color:rgba(0,255,0,.25)");
        CssBorderRadiusTests.Layout(border, 80, 60);
        var red = Pixel(border, 40, 5);
        Assert.True(red.R > red.B);
        foreground.Color = Color.FromArgb(128, 0, 0, 255);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        var blue = Pixel(border, 40, 5);
        Assert.True(blue.B > blue.R);
        Assert.Equal(red.A, blue.A);
    }

    [Fact]
    public void NativeBrushOverridesSideColors_AndCssReturnsAfterClear()
    {
        var border = CssBorderRadiusTests.Box("border:10px solid; border-color:red green blue yellow", 80, 60);
        border.BorderBrush = Brushes.Purple;
        AssertSides(border, "purple", "purple", "purple", "purple");
        border.ClearValue(Border.BorderBrushProperty);
        AssertSides(border, "red", "green", "blue", "yellow");
    }

    [Fact]
    public void InvalidShorthand_DoesNotOverwriteValidColors()
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid; border-color:red green blue yellow; border-color:red green blue yellow lime", 80, 60);
        AssertSides(border, "red", "green", "blue", "yellow");
    }

    [Fact]
    public void InitialColor_UsesCurrentColorForAllSides()
    {
        var border = CssBorderRadiusTests.Box(
            "color:purple; border:10px solid red; border-color:initial", 80, 60);
        AssertSides(border, "purple", "purple", "purple", "purple");
    }

    [Fact]
    public void NoneStyle_HidesEveryColor()
    {
        var border = CssBorderRadiusTests.Box(
            "border:10px solid red; border-color:red green blue yellow; border-style:none", 80, 60);
        Assert.Equal((byte)0, Pixel(border, 40, 5).A);
        Assert.Null(border.BorderBrush);
    }

    private static void AssertSides(Border border, string top, string right, string bottom, string left)
    {
        AssertColor(Pixel(border, 40, 5), top);
        AssertColor(Pixel(border, 75, 30), right);
        AssertColor(Pixel(border, 40, 55), bottom);
        AssertColor(Pixel(border, 5, 30), left);
    }

    private static (byte B, byte G, byte R, byte A) Pixel(Border border, int x, int y)
    {
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 60);
        var offset = (y * 80 + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static void AssertColor((byte B, byte G, byte R, byte A) pixel, string name)
    {
        var expected = name switch
        {
            "red" => (B: (byte)0, G: (byte)0, R: (byte)255, A: (byte)255),
            "green" => (B: (byte)0, G: (byte)128, R: (byte)0, A: (byte)255),
            "blue" => (B: (byte)255, G: (byte)0, R: (byte)0, A: (byte)255),
            "yellow" => (B: (byte)0, G: (byte)255, R: (byte)255, A: (byte)255),
            "lime" => (B: (byte)0, G: (byte)255, R: (byte)0, A: (byte)255),
            "purple" => (B: (byte)128, G: (byte)0, R: (byte)128, A: (byte)255),
            "black" => (B: (byte)0, G: (byte)0, R: (byte)0, A: (byte)255),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        Assert.Equal(expected, pixel);
    }
}
