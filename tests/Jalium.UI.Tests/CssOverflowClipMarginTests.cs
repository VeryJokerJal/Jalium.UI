using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssOverflowClipMarginTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("8px")]
    [InlineData("content-box")]
    [InlineData("border-box 2em")]
    [InlineData("5px padding-box")]
    [InlineData("calc(2px + 3px) content-box")]
    public void AcceptsVisualBoxAndNonNegativeLengthInEitherOrder(string value)
        => Assert.True(Css.Supports("overflow-clip-margin", value));

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("-1px")]
    [InlineData("10%")]
    [InlineData("calc(10% + 2px)")]
    [InlineData("content-box padding-box")]
    [InlineData("1px 2px")]
    [InlineData("margin-box")]
    public void RejectsValuesOutsideTheGrammar(string value)
        => Assert.False(Css.Supports("overflow-clip-margin", value));

    [Fact]
    public void ClipMarginSelectsTheVisualBoxAndExpandsOnlyTheClipEdge()
    {
        var border = new Border { Width = 40, Height = 30, BorderThickness = new Thickness(4), Padding = new Thickness(3) };
        CssBorderRadiusTests.Layout(border, 40, 30);

        Css.SetStyle(border, "overflow:clip");
        Assert.Equal(new Rect(4, 4, 32, 22), Reference(border));

        Css.SetStyle(border, "overflow:clip;overflow-clip-margin:content-box");
        Assert.Equal(new Rect(7, 7, 26, 16), Reference(border));

        Css.SetStyle(border, "overflow:clip;overflow-clip-margin:2px content-box");
        Assert.Equal(new Rect(5, 5, 30, 20), Reference(border));

        Css.SetStyle(border, "overflow:clip;overflow-clip-margin:border-box 2px");
        Assert.Equal(new Rect(-2, -2, 44, 34), Reference(border));

        Css.SetStyle(border, "overflow:clip;overflow-clip-margin:padding-box 6px");
        Assert.Equal(new Rect(-2, -2, 44, 34), Reference(border));
    }

    [Fact]
    public void OverflowHiddenDoesNotUseClipMargin()
    {
        var panel = Box();
        Css.SetStyle(panel, "overflow:hidden;overflow-clip-margin:border-box 12px");
        var clip = Assert.IsType<RectangleGeometry>(panel.GetLayoutClip());
        Assert.Equal(new Rect(0, 0, 40, 30), clip.BoundsClipRect);
        Assert.Null(panel.HitTest(new Point(45, 15)));

        Css.SetStyle(panel, "overflow:clip;overflow-clip-margin:12px");
        clip = Assert.IsType<RectangleGeometry>(panel.GetLayoutClip());
        Assert.Equal(new Rect(-12, -12, 64, 54), clip.BoundsClipRect);
        Assert.NotNull(panel.HitTest(new Point(45, 15)));
        Assert.Null(panel.HitTest(new Point(55, 15)));

        Css.SetStyle(panel, "overflow-x:clip;overflow-y:hidden;overflow-clip-margin:12px");
        Assert.Equal(CssOverflowMode.Hidden, CssOverflowProperties.Get(panel)?.X);
        Assert.Equal(new Rect(0, 0, 40, 30), Assert.IsType<RectangleGeometry>(panel.GetLayoutClip()).BoundsClipRect);
    }

    [Fact]
    public void RoundedClipMarginAdjustsSmallCornersAndKeepsOpenAxisSquare()
    {
        var panel = Box();
        Css.SetStyle(panel, "border-radius:2px;overflow:clip;overflow-clip-margin:border-box 4px");
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(panel.GetLayoutClip());
        Assert.Equal(new Rect(-4, -4, 48, 38), clip.ClipReferenceRect);
        Assert.Equal(new Size(5.5, 5.5), clip.Radii.TopLeft);
        Assert.False(clip.FillContains(new Point(-3.5, -3.5)));

        Css.SetStyle(panel, "border-radius:2px;overflow:clip visible;overflow-clip-margin:4px");
        clip = Assert.IsType<CssRoundedRectangleGeometry>(panel.GetLayoutClip());
        Assert.Equal(default, clip.Radii.TopLeft);
        Assert.True(clip.FillContains(new Point(-3.5, -3.5)));
    }

    [Fact]
    public void CssWideValuesAndNativeClipOverridesRestoreTheCorrectEdge()
    {
        var parent = new Grid();
        var child = Box();
        parent.Children.Add(child);
        Css.SetStyle(parent, "overflow-clip-margin:border-box 7px");
        Css.SetStyle(child, "overflow:clip;overflow-clip-margin:inherit");
        CssBorderRadiusTests.Layout(parent, 40, 30);
        Assert.Equal(new Rect(-7, -7, 54, 44), Reference(child));

        Css.SetStyle(child, "overflow:clip;overflow-clip-margin:unset");
        Assert.Equal(new Rect(0, 0, 40, 30), Reference(child));

        Css.SetStyle(child, "overflow:clip;overflow-clip-margin:border-box 7px");
        child.ClipToBoundsEdges = ClipEdges.Top | ClipEdges.Bottom;
        Assert.Equal(new Rect(0, 0, 40, 30), Assert.IsType<RectangleGeometry>(child.GetLayoutClip()).BoundsClipRect);
        child.ClearValue(UIElement.ClipToBoundsEdgesProperty);
        Assert.Equal(new Rect(-7, -7, 54, 44), Reference(child));

        child.ClipToBounds = false;
        Assert.Null(child.GetLayoutClip());
        child.ClearValue(UIElement.ClipToBoundsProperty);
        Assert.Equal(new Rect(-7, -7, 54, 44), Reference(child));
        Css.SetStyle(child, "overflow:clip");
        Assert.Equal(new Rect(0, 0, 40, 30), Reference(child));
        Assert.Null(CssOverflowProperties.ClipMargin(child));
    }

    [Fact]
    public void RelativeLengthResolvesFromTheElementFontSize()
    {
        var panel = Box();
        Css.SetStyle(panel, "font-size:20px;overflow:clip;overflow-clip-margin:0.5em");
        Assert.Equal(new Rect(-10, -10, 60, 50), Reference(panel));
        Css.SetStyle(panel, "font-size:40px;overflow:clip;overflow-clip-margin:0.5em");
        Assert.Equal(new Rect(-20, -20, 80, 70), Reference(panel));
    }

    [Fact]
    public void NativeBorderRadiusAndExplicitClipKeepTheirPrecedence()
    {
        var border = new Border
        {
            Width = 40, Height = 30, BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(8),
        };
        Css.SetStyle(border, "overflow:clip;overflow-clip-margin:border-box 2px");
        CssBorderRadiusTests.Layout(border, 40, 30);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(border.GetLayoutClip());
        Assert.Equal(new Rect(-2, -2, 44, 34), clip.ClipReferenceRect);
        Assert.Equal(new Size(10, 10), clip.Radii.TopLeft);

        var explicitClip = new RectangleGeometry(new Rect(0, 0, 5, 5));
        border.Clip = explicitClip;
        Assert.Same(explicitClip, border.GetLayoutClip());
    }

    [Fact]
    public void ControlAndScrollViewerUseTheExpandedClip()
    {
        FrameworkElement[] elements = [new Button { Width = 40, Height = 30 },
            new ScrollViewer { Width = 40, Height = 30 }];
        foreach (var element in elements)
        {
            // ScrollViewer sets a native local clip in its constructor; clear it
            // so the CSS declaration is the effective overflow value.
            if (element is ScrollViewer) element.ClearValue(UIElement.ClipToBoundsProperty);
            Css.SetStyle(element, "overflow:clip;overflow-clip-margin:border-box 6px");
            CssBorderRadiusTests.Layout(element, 40, 30);
            Assert.Equal(new Rect(-6, -6, 52, 42), Reference(element));
        }
    }

    private static Rect Reference(FrameworkElement element) => element.GetLayoutClip() switch
    {
        CssRoundedRectangleGeometry rounded => rounded.ClipReferenceRect,
        RectangleGeometry rectangle => rectangle.BoundsClipRect,
        _ => throw new Xunit.Sdk.XunitException("Expected a bounds clip"),
    };

    private static Canvas Box()
    {
        var panel = new Canvas { Width = 40, Height = 30 };
        panel.Children.Add(new Border { Width = 80, Height = 80, Background = Brushes.Red });
        CssBorderRadiusTests.Layout(panel, 40, 30);
        return panel;
    }
}
