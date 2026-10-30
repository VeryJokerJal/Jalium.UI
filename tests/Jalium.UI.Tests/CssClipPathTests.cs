using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssClipPathTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("content-box")]
    [InlineData("fill-box")]
    [InlineData("stroke-box")]
    [InlineData("view-box")]
    [InlineData("inset(10px 20% 5px) border-box")]
    [InlineData("inset(0)")]
    [InlineData("inset(10px round 5px / 10%)")]
    [InlineData("xywh(10px 20% 30px 40%)")]
    [InlineData("rect(auto 80px 70px 20px round 5px)")]
    [InlineData("circle(30% at 50% 50%)")]
    [InlineData("circle(10px at top right)")]
    [InlineData("circle(0)")]
    [InlineData("circle(10px at right 10px bottom 20%)")]
    [InlineData("ellipse(10px 15px at bottom 20% right 10px)")]
    [InlineData("ellipse(20px 30% at 40px 50%) padding-box")]
    [InlineData("polygon(0 0, 100% 0, 50% 100%)")]
    [InlineData("polygon(1% 2%)")]
    [InlineData("polygon(calc(100% - 20px) calc(30% + 10px))")]
    [InlineData("polygon(evenodd, 0 0, 100% 0, 50% 100%) content-box")]
    [InlineData("polygon(round 10px, 0 0, 100% 0, 100% 100%, 0 100%)")]
    [InlineData("polygon(nonzero round 1em, 0 0, 100% 0, 100% 100%)")]
    [InlineData("path('M0 0 H40 L20 40')")]
    [InlineData("path(evenodd, \"M0 0 H40 V40 H0 Z M10 10 H30 V30 H10 Z\")")]
    [InlineData("path(nonzero, 'M0 0 L20 20 L0 20 Z') content-box")]
    [InlineData("padding-box path('M0 0 A20 20 0 0 1 20 20 Z')")]
    [InlineData("shape(from 0 0, line to 100% 0, line to 50% 100%)")]
    [InlineData("shape(evenodd from left top, hline to right, vline to bottom, hline to left, close)")]
    [InlineData("shape(from 20px 40px, move by 10px 5px, hline by 100%, vline to y-end)")]
    [InlineData("shape(from bottom right, curve to left with center right / bottom center)")]
    [InlineData("shape(from 20px 40px, curve by 20px 20px with 10px 30px from start / 12px 32px from end)")]
    [InlineData("shape(from top left, smooth to top right, smooth by 10px 10px with 5px 5px from origin)")]
    [InlineData("shape(from 20px 40px, arc by 20px 20px of -10% -20% large cw rotate 0.5turn)")]
    [InlineData("content-box shape(from x-start y-start, hline to x-end, vline to y-end, hline to x-start)")]
    [InlineData("shape(from inline-start block-start, line to inline-end block-end)")]
    [InlineData("url('shapes.svg#clip')")]
    [InlineData("url(shapes.svg#clip)")]
    [InlineData("url(#missing)")]
    public void SupportsImplementedShapes(string value) => Assert.True(Css.Supports("clip-path", value));

    [Theory]
    [InlineData("polygon(100px)")]
    [InlineData("polygon(1%)")]
    [InlineData("polygon(evenodd, 1px, 2px, 3em 4em)")]
    [InlineData("polygon(0 0,)")]
    [InlineData("polygon(round 10%, 0 0, 100% 0)")]
    [InlineData("polygon(round -1px, 0 0, 100% 0)")]
    [InlineData("inset(10px round -5px)")]
    [InlineData("inset(10)")]
    [InlineData("circle(10)")]
    [InlineData("xywh(0 0 10 10)")]
    [InlineData("rect(0 10 10 0)")]
    [InlineData("circle(-1px)")]
    [InlineData("ellipse(10px)")]
    [InlineData("circle(10px 20px)")]
    [InlineData("circle(10px) content-box padding-box")]
    [InlineData("circle(10px at right 10px bottom)")]
    [InlineData("xywh(0 0 -1px 20px)")]
    [InlineData("xywh(10px 20px 30px)")]
    [InlineData("rect(10px 20px 30px)")]
    [InlineData("path()")]
    [InlineData("path('')")]
    [InlineData("path('M0 0')")]
    [InlineData("path('L0 0 L20 20')")]
    [InlineData("path('F0 M0 0 L20 20')")]
    [InlineData("path('M0 0 F0 L20 20')")]
    [InlineData("path(evenodd 'M0 0 L20 20')")]
    [InlineData("path(badfillrule, 'M0 0 L20 20')")]
    [InlineData("path(M0 0 L20 20)")]
    [InlineData("path('M0 0 L20 20' extra)")]
    [InlineData("path('M0 0 L20 20') border-box padding-box")]
    [InlineData("shape()")]
    [InlineData("shape(from 0 0)")]
    [InlineData("shape(from 0 0,)")]
    [InlineData("shape(from 0 0 line to 10px 10px)")]
    [InlineData("shape(from 0 0, curve to 10px 10px)")]
    [InlineData("shape(from 0 0, curve by 10px 10px with left top)")]
    [InlineData("shape(from 0 0, hline to top)")]
    [InlineData("shape(from 0 0, vline to right)")]
    [InlineData("shape(from 0 0, hline to inline-start)")]
    [InlineData("shape(from 0 0, vline to block-end)")]
    [InlineData("shape(from 0 0, arc to 10px 10px)")]
    [InlineData("shape(from 0 0, arc to 10px 10px of 5px cw ccw)")]
    [InlineData("shape(from 0 0, arc to 10px 10px of 5px rotate 12deg rotate 13deg)")]
    [InlineData("shape(from start, line to 10px 10px)")]
    [InlineData("url()")]
    [InlineData("url('shapes.svg#clip' extra)")]
    [InlineData("url(shapes.svg#clip) padding-box")]
    public void RejectsShapesWithoutExactSupport(string value) => Assert.False(Css.Supports("clip-path", value));

    [Fact]
    public void InsetClipsPaintingAndDescendantInput_AndUpdatesAfterResize()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:inset(10% 20% 5px 10px)");
        CssBorderRadiusTests.Layout(border, 100, 80);

        Assert.Null(border.HitTest(new(5, 40)));
        Assert.Null(border.HitTest(new(85, 40)));
        Assert.Same(border, border.HitTest(new(50, 40))?.VisualHit);
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)0, Alpha(pixels, 100, 5, 40));
        Assert.Equal((byte)0, Alpha(pixels, 100, 85, 40));
        Assert.Equal((byte)255, Alpha(pixels, 100, 50, 40));

        border.Width = 200;
        CssBorderRadiusTests.Layout(border, 200, 80);
        Assert.Same(border, border.HitTest(new(85, 40))?.VisualHit);
        Assert.Null(border.HitTest(new(170, 40)));
    }

    [Fact]
    public void CircleAndEllipseClipAtTheirActualContour()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:circle(30px at 50% 50%)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Null(border.HitTest(new(25, 15))); // inside AABB, outside circle
        Assert.Same(border, border.HitTest(new(50, 15))?.VisualHit);
        Assert.Null(border.InputHitTest(new(25, 15)));
        Assert.Same(border, border.InputHitTest(new(50, 15)));
        var circle = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)0, Alpha(circle, 100, 25, 15));
        Assert.Equal((byte)255, Alpha(circle, 100, 50, 40));

        Css.SetStyle(border, "clip-path:ellipse(30px 20px at 50% 50%)");
        Assert.Null(border.HitTest(new(25, 25)));
        Assert.Same(border, border.HitTest(new(50, 25))?.VisualHit);
        var ellipse = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)0, Alpha(ellipse, 100, 25, 25));
        Assert.Equal((byte)255, Alpha(ellipse, 100, 50, 40));
    }

    [Fact]
    public void ClosestSideUsesDistanceToExtendedEdgesWhenCenterIsOutsideReferenceBox()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:circle(closest-side at 120px -15px)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        var circle = Assert.IsType<CssRoundedRectangleGeometry>(
            CssClipPathProperties.GetGeometry(border));
        Assert.Equal(new Rect(105, -30, 30, 30), circle.Bounds);

        Css.SetStyle(border, "clip-path:ellipse(closest-side at 120px -15px)");
        var ellipse = Assert.IsType<CssRoundedRectangleGeometry>(
            CssClipPathProperties.GetGeometry(border));
        Assert.Equal(new Rect(100, -30, 40, 30), ellipse.Bounds);
    }

    [Fact]
    public void SvgGeometryBoxKeywordsUseCssLayoutBoxFallbacks()
    {
        var border = new Border
        {
            Width = 100, Height = 80,
            BorderThickness = new Thickness(10), Padding = new Thickness(5),
        };
        CssBorderRadiusTests.Layout(border, 100, 80);
        Rect Bounds(string value)
        {
            Css.SetStyle(border, $"clip-path:{value}");
            return Assert.IsType<CssRoundedRectangleGeometry>(
                CssClipPathProperties.GetGeometry(border)).Bounds;
        }

        Assert.Equal(Bounds("content-box"), Bounds("fill-box"));
        Assert.Equal(Bounds("border-box"), Bounds("stroke-box"));
        Assert.Equal(Bounds("border-box"), Bounds("view-box"));
    }

    [Fact]
    public void InsetRoundKeepsItsCornerContour()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:inset(10px round 20px / 15px)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Null(border.HitTest(new(12, 12)));
        Assert.Same(border, border.HitTest(new(50, 12))?.VisualHit);
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)0, Alpha(pixels, 100, 12, 12));
        Assert.Equal((byte)255, Alpha(pixels, 100, 50, 12));
    }

    [Fact]
    public void SideKeywordPositionAndCrossingInsetsUseCssGeometry()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:circle(10px at top right)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Same(border, border.HitTest(new(95, 5))?.VisualHit);
        Assert.Null(border.HitTest(new(5, 5)));

        Css.SetStyle(border, "clip-path:inset(0 80px 0 80px)");
        var geometry = Assert.IsType<CssRoundedRectangleGeometry>(CssClipPathProperties.GetGeometry(border));
        Assert.Equal(50, geometry.Rect.Left);
        Assert.Equal(0, geometry.Rect.Width);
    }

    [Fact]
    public void FourValuePositionOffsetsResolveFromNamedEdges()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:circle(10px at right calc(10px + 5%) bottom 20%)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Same(border, border.HitTest(new(85, 64))?.VisualHit);
        Assert.Null(border.HitTest(new(97, 64)));

        Css.SetStyle(border, "clip-path:ellipse(10px 15px at bottom 20% right 15px)");
        Assert.Same(border, border.HitTest(new(85, 64))?.VisualHit);
        Assert.Null(border.HitTest(new(85, 40)));
    }

    [Fact]
    public void CirclePercentageRadiusUsesNormalizedDiagonal()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:circle(50%)");
        CssBorderRadiusTests.Layout(border, 100, 80);

        // Half the normalized diagonal is about 45.28, rather than 40 or 50.
        Assert.Same(border, border.HitTest(new(95, 40))?.VisualHit);
        Assert.Null(border.HitTest(new(96, 40)));
    }

    [Fact]
    public void XywhAndRectClipToTheirUsedCoordinates()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:xywh(10px 10px 30px 20px)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Same(border, border.HitTest(new(15, 15))?.VisualHit);
        Assert.Null(border.HitTest(new(45, 15)));

        Css.SetStyle(border, "clip-path:rect(10px 50px 30px 20px round 8px)");
        Assert.Null(border.HitTest(new(21, 11)));
        Assert.Same(border, border.HitTest(new(35, 20))?.VisualHit);
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)0, Alpha(pixels, 100, 21, 11));
        Assert.Equal((byte)255, Alpha(pixels, 100, 35, 20));

        Css.SetStyle(border, "clip-path:rect(auto auto auto 20px)");
        Assert.Null(border.HitTest(new(10, 40)));
        Assert.Same(border, border.HitTest(new(90, 40))?.VisualHit);
    }

    [Fact]
    public void PolygonClipsPaintingAndInputAndTracksReferenceBoxResize()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:polygon(0 0, 100% 0, 50% 100%)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Same(border, border.InputHitTest(new(50, 40)));
        Assert.Null(border.InputHitTest(new(5, 70)));
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)255, Alpha(pixels, 100, 50, 40));
        Assert.Equal((byte)0, Alpha(pixels, 100, 5, 70));

        border.Width = 200;
        CssBorderRadiusTests.Layout(border, 200, 80);
        Assert.Same(border, border.InputHitTest(new(150, 20)));
        Assert.Null(border.InputHitTest(new(85, 70)));

        border.Width = 100;
        border.BorderThickness = new Thickness(10);
        border.Padding = new Thickness(10);
        Css.SetStyle(border, "clip-path:polygon(0 0, 100% 0, 50% 100%) content-box");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Null(border.HitTest(new(10, 30)));
        Assert.Same(border, border.HitTest(new(50, 35))?.VisualHit);
    }

    [Fact]
    public void PolygonFillRuleControlsSelfIntersectingInterior()
    {
        var border = new Border { Width = 100, Height = 100, Background = Brushes.Red };
        const string points = "50% 0, 79% 90%, 3% 35%, 97% 35%, 21% 90%";
        Css.SetStyle(border, $"clip-path:polygon(nonzero, {points})");
        CssBorderRadiusTests.Layout(border, 100, 100);
        Assert.Same(border, border.InputHitTest(new(50, 50)));

        Css.SetStyle(border, $"clip-path:polygon(evenodd, {points})");
        Assert.Null(border.InputHitTest(new(50, 50)));
        Assert.Same(border, border.InputHitTest(new(50, 15)));
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 100);
        Assert.Equal((byte)0, Alpha(pixels, 100, 50, 50));
        Assert.Equal((byte)255, Alpha(pixels, 100, 50, 15));
    }

    [Fact]
    public void PolygonClipsOverflowingDescendantInManagedSoftware()
    {
        var panel = new Canvas { Width = 40, Height = 40, Background = Brushes.Red };
        panel.Children.Add(new Border { Width = 80, Height = 80, Background = Brushes.Red });
        Css.SetStyle(panel, "clip-path:polygon(0 0, 100% 0, 50% 100%)");
        CssBorderRadiusTests.Layout(panel, 40, 40);

        var pixels = CssBorderRadiusTests.Pixels(panel, 80, 80);
        Assert.Equal((byte)255, Alpha(pixels, 80, 20, 10));
        Assert.Equal((byte)0, Alpha(pixels, 80, 5, 35));
        Assert.Equal((byte)0, Alpha(pixels, 80, 60, 20));
        Assert.Null(panel.InputHitTest(new(5, 35)));
    }

    [Fact]
    public void RoundedPolygonUsesCircularCornersAndClampsToShortEdges()
    {
        var border = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        Css.SetStyle(border,
            "clip-path:polygon(round 50px, 0 0, 100% 0, 100% 100%, 0 100%)");
        CssBorderRadiusTests.Layout(border, 40, 40);
        Assert.Null(border.InputHitTest(new(1, 1)));
        Assert.Same(border, border.InputHitTest(new(20, 5)));
        var pixels = CssBorderRadiusTests.Pixels(border, 40, 40);
        Assert.Equal((byte)0, Alpha(pixels, 40, 1, 1));
        Assert.Equal((byte)255, Alpha(pixels, 40, 20, 5));
    }

    [Fact]
    public void PathUsesFixedPixelsImplicitClosureAndReferenceBoxOrigin()
    {
        var border = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:path('M0 0 H40 L20 40')");
        CssBorderRadiusTests.Layout(border, 40, 40);
        Assert.Same(border, border.InputHitTest(new(20, 15)));
        Assert.Null(border.InputHitTest(new(5, 35)));
        var pixels = CssBorderRadiusTests.Pixels(border, 40, 40);
        Assert.Equal((byte)255, Alpha(pixels, 40, 20, 15));
        Assert.Equal((byte)0, Alpha(pixels, 40, 5, 35));

        border.Width = 80;
        CssBorderRadiusTests.Layout(border, 80, 40);
        Assert.Same(border, border.InputHitTest(new(20, 15)));
        Assert.Null(border.InputHitTest(new(60, 15)));

        border.BorderThickness = new Thickness(5);
        border.Padding = new Thickness(5);
        Css.SetStyle(border, "clip-path:path('M0 0 H20 V20 H0') content-box");
        CssBorderRadiusTests.Layout(border, 80, 40);
        Assert.Null(border.InputHitTest(new(5, 15)));
        Assert.Same(border, border.InputHitTest(new(15, 15)));
        Assert.Null(border.InputHitTest(new(35, 15)));
        var shifted = CssBorderRadiusTests.Pixels(border, 80, 40);
        Assert.Equal((byte)0, Alpha(shifted, 80, 5, 15));
        Assert.Equal((byte)255, Alpha(shifted, 80, 15, 15));
    }

    [Fact]
    public void PathFillRuleControlsNestedSubpaths()
    {
        var border = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        const string path = "M0 0 H40 V40 H0 Z M10 10 H30 V30 H10 Z";
        Css.SetStyle(border, $"clip-path:path(nonzero, '{path}')");
        CssBorderRadiusTests.Layout(border, 40, 40);
        Assert.Same(border, border.InputHitTest(new(20, 20)));

        Css.SetStyle(border, $"clip-path:path(evenodd, '{path}')");
        Assert.Null(border.InputHitTest(new(20, 20)));
        Assert.Same(border, border.InputHitTest(new(5, 20)));
        var pixels = CssBorderRadiusTests.Pixels(border, 40, 40);
        Assert.Equal((byte)0, Alpha(pixels, 40, 20, 20));
        Assert.Equal((byte)255, Alpha(pixels, 40, 5, 20));
    }

    [Fact]
    public void ShapePercentagesTrackReferenceBoxAndClipPaintAndInput()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "clip-path:shape(from 0 0, hline to 100%, line to 50% 100%)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Same(border, border.InputHitTest(new(50, 30)));
        Assert.Null(border.InputHitTest(new(5, 70)));
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 80);
        Assert.Equal((byte)255, Alpha(pixels, 100, 50, 30));
        Assert.Equal((byte)0, Alpha(pixels, 100, 5, 70));

        border.Width = 200;
        CssBorderRadiusTests.Layout(border, 200, 80);
        Assert.Same(border, border.InputHitTest(new(150, 20)));
        Assert.Null(border.InputHitTest(new(85, 70)));

        border.Width = 100;
        border.BorderThickness = new Thickness(10);
        border.Padding = new Thickness(10);
        Css.SetStyle(border,
            "clip-path:shape(from 0 0, hline to 100%, vline to 100%, hline to 0) content-box");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Null(border.InputHitTest(new(10, 30)));
        Assert.Same(border, border.InputHitTest(new(50, 35)));
    }

    [Fact]
    public void ShapeFillRuleAndRelativeCommandsCreateAnInteriorHole()
    {
        var border = new Border { Width = 100, Height = 100, Background = Brushes.Red };
        const string commands = "from 0 0, hline to 100%, vline to 100%, hline to 0, close, " +
            "move to 25% 25%, line by 50% 0, line by 0 50%, line by -50% 0";
        Css.SetStyle(border, $"clip-path:shape(nonzero {commands})");
        CssBorderRadiusTests.Layout(border, 100, 100);
        Assert.Same(border, border.InputHitTest(new(50, 50)));

        Css.SetStyle(border, $"clip-path:shape(evenodd {commands})");
        Assert.Null(border.InputHitTest(new(50, 50)));
        Assert.Same(border, border.InputHitTest(new(10, 50)));
        var pixels = CssBorderRadiusTests.Pixels(border, 100, 100);
        Assert.Equal((byte)0, Alpha(pixels, 100, 50, 50));
        Assert.Equal((byte)255, Alpha(pixels, 100, 10, 50));
    }

    [Fact]
    public void ShapeMathAndCustomPropertiesResolveAtUsedSize()
    {
        var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
        Css.SetStyle(border, "--edge:5px;clip-path:shape(from var(--edge) 0, " +
            "hline to calc(100% - var(--edge)), vline to 100%, hline to var(--edge))");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Null(border.InputHitTest(new(2, 40)));
        Assert.Same(border, border.InputHitTest(new(50, 40)));
        Assert.Null(border.InputHitTest(new(98, 40)));

        border.Width = 200;
        CssBorderRadiusTests.Layout(border, 200, 80);
        Assert.Same(border, border.InputHitTest(new(190, 40)));
        Assert.Null(border.InputHitTest(new(198, 40)));
    }

    [Fact]
    public void ShapeCurvesReflectMatchingPreviousControlType()
    {
        var border = new Border { Width = 100, Height = 80 };
        Css.SetStyle(border, "clip-path:shape(from 10px 20px, " +
            "curve by 20px 0 with 5px -10px from start / 5px 10px from end, " +
            "smooth by 20px 0 with 5px 10px from origin, " +
            "smooth by 10px 0, curve by 10px 0 with 5px -10px, smooth by 10px 0)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        var geometry = Assert.IsType<PathGeometry>(CssClipPathProperties.GetGeometry(border));
        var segments = geometry.Figures[0].Segments;
        var first = Assert.IsType<BezierSegment>(segments[0]);
        Assert.Equal(new Point(15, 10), first.Point1);
        Assert.Equal(new Point(35, 30), first.Point2);
        var cubicSmooth = Assert.IsType<BezierSegment>(segments[1]);
        Assert.Equal(new Point(25, 10), cubicSmooth.Point1);
        Assert.Equal(new Point(5, 10), cubicSmooth.Point2);
        var quadraticAfterCubic = Assert.IsType<QuadraticBezierSegment>(segments[2]);
        Assert.Equal(new Point(50, 20), quadraticAfterCubic.Point1);
        var quadratic = Assert.IsType<QuadraticBezierSegment>(segments[3]);
        Assert.Equal(new Point(65, 10), quadratic.Point1);
        var quadraticSmooth = Assert.IsType<QuadraticBezierSegment>(segments[4]);
        Assert.Equal(new Point(75, 30), quadraticSmooth.Point1);
    }

    [Fact]
    public void ShapeArcRadiiAndLogicalEdgesResolveAgainstReferenceBox()
    {
        var border = new Border { Width = 100, Height = 80, FlowDirection = FlowDirection.RightToLeft };
        Css.SetStyle(border, "clip-path:shape(from x-start y-start, " +
            "arc to x-end y-end of -50% -25% cw large rotate 0.25turn)");
        CssBorderRadiusTests.Layout(border, 100, 80);
        var geometry = Assert.IsType<PathGeometry>(CssClipPathProperties.GetGeometry(border));
        Assert.Equal(new Point(100, 0), geometry.Figures[0].StartPoint);
        var arc = Assert.IsType<ArcSegment>(geometry.Figures[0].Segments[0]);
        Assert.Equal(new Point(0, 80), arc.Point);
        Assert.Equal(new Size(50, 20), arc.Size);
        Assert.Equal(90, arc.RotationAngle);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }

    [Fact]
    public void SvgClipPathDefinitionUnitesChildrenAndBakesTransforms()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='clip' transform='translate(10 0)'>" +
            "<rect x='0' y='0' width='10' height='20'/>" +
            "<circle cx='30' cy='10' r='10'/>" +
            "</clipPath></defs></svg>";
        var definition = Assert.IsType<SvgParser.ClipPathDefinition>(SvgParser.ParseClipPathDefinition(
            System.Text.Encoding.UTF8.GetBytes(svg), "clip"));
        var geometry = Assert.IsType<PathGeometry>(definition.Resolve(new Size(100, 80)));
        Assert.True(geometry.FillContains(new Point(15, 10)));
        Assert.True(geometry.FillContains(new Point(40, 10)));
        Assert.False(geometry.FillContains(new Point(5, 10)));
        Assert.False(geometry.FillContains(new Point(65, 10)));
    }

    [Fact]
    public void SvgClipPathUnionPreservesOverlapAndIndividualHoles()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='overlap'><path d='M0 0 H30 V30 H0 Z'/>" +
            "<path d='M40 0 V30 H10 V0 Z'/></clipPath>" +
            "<clipPath id='hole'><path clip-rule='evenodd' " +
            "d='M0 0 H40 V40 H0 Z M10 10 H30 V30 H10 Z'/>" +
            "<rect x='15' y='15' width='10' height='10'/></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        var overlap = Assert.IsType<SvgParser.ClipPathDefinition>(
            SvgParser.ParseClipPathDefinition(bytes, "overlap"));
        var overlapGeometry = Assert.IsType<PathGeometry>(overlap.Resolve(new Size(40, 40)));
        Assert.True(overlapGeometry.FillContains(new Point(20, 15)));
        Assert.True(overlapGeometry.FillContains(new Point(35, 15)));

        var hole = Assert.IsType<SvgParser.ClipPathDefinition>(
            SvgParser.ParseClipPathDefinition(bytes, "hole"));
        var holeGeometry = Assert.IsType<PathGeometry>(hole.Resolve(new Size(40, 40)));
        Assert.True(holeGeometry.FillContains(new Point(5, 20)));
        Assert.False(holeGeometry.FillContains(new Point(12, 20)));
        Assert.True(holeGeometry.FillContains(new Point(20, 20)));
    }

    [Fact]
    public void SvgClipPathSupportsGroupsUseAndCompressedSvg()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<rect id='tile' width='10' height='10'/>" +
            "<clipPath id='clip'><g transform='translate(10 0)'>" +
            "<use href='#tile' x='5' y='5'/></g></clipPath></defs></svg>";
        using var output = new MemoryStream();
        using (var compressed = new System.IO.Compression.GZipStream(output,
            System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
            compressed.Write(bytes);
        }
        var definition = Assert.IsType<SvgParser.ClipPathDefinition>(
            SvgParser.ParseClipPathDefinition(output.ToArray(), "clip"));
        var geometry = Assert.IsType<PathGeometry>(definition.Resolve(new Size(40, 40)));
        Assert.True(geometry.FillContains(new Point(20, 10)));
        Assert.False(geometry.FillContains(new Point(5, 10)));
        Assert.False(geometry.FillContains(new Point(30, 10)));
    }

    [Fact]
    public void SvgClipTextUsesGlyphContoursAndActualTextAnchorWidth()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='start'><text x='100' y='100' font-size='100' " +
            "font-family='Segoe UI' fill='none'>O</text></clipPath>" +
            "<clipPath id='middle'><text x='100' y='100' font-size='100' " +
            "font-family='Segoe UI' text-anchor='middle'>O</text></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        var start = Assert.IsType<PathGeometry>(Assert.IsType<SvgParser.ClipPathDefinition>(
            SvgParser.ParseClipPathDefinition(bytes, "start")).Resolve(new Size(200, 150)));
        var middle = Assert.IsType<PathGeometry>(Assert.IsType<SvgParser.ClipPathDefinition>(
            SvgParser.ParseClipPathDefinition(bytes, "middle")).Resolve(new Size(200, 150)));
        var bounds = start.Bounds;
        Assert.True(bounds.Width > 40);
        var centerY = bounds.Y + bounds.Height / 2;
        Assert.False(start.FillContains(new Point(bounds.X + bounds.Width / 2, centerY)));
        Assert.Contains(Enumerable.Range(1, 99), step =>
            start.FillContains(new Point(bounds.X + bounds.Width * step / 100, centerY)));
        Assert.InRange(start.Bounds.X - middle.Bounds.X, 30, 45);
        Assert.Equal(start.Bounds.Width, middle.Bounds.Width, 3);
    }

    [Fact]
    public void SvgClipTextPositionsNestedTspansAndInheritsTheirStyles()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='first'><text x='15' y='70' font-size='48'>O</text></clipPath>" +
            "<clipPath id='second'><text x='115' y='125' font-size='24'>O</text></clipPath>" +
            "<clipPath id='combined'><text x='15' y='70' font-size='48'>O" +
            "<tspan x='115' y='125'><tspan font-size='0.5em' fill='none'>O</tspan></tspan>" +
            "</text></clipPath>" +
            "<clipPath id='hidden'><text x='15' y='70' font-size='48' visibility='hidden'>" +
            "<tspan x='115' y='125' font-size='24' visibility='visible'>O</tspan>" +
            "</text></clipPath></defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(220, 160)));
        var first = Resolve("first");
        var second = Resolve("second");
        var combined = Resolve("combined");
        var hidden = Resolve("hidden");
        Assert.Equal(Rect.Union(first.Bounds, second.Bounds).X, combined.Bounds.X, 2);
        Assert.Equal(Rect.Union(first.Bounds, second.Bounds).Right, combined.Bounds.Right, 2);
        Assert.Equal(Rect.Union(first.Bounds, second.Bounds).Bottom, combined.Bounds.Bottom, 2);
        Assert.Equal(second.Bounds.X, hidden.Bounds.X, 2);
        Assert.Equal(second.Bounds.Y, hidden.Bounds.Y, 2);
        Assert.Equal(second.GetArea(), hidden.GetArea(), 1);
        for (var y = 0; y < 160; y += 4)
            for (var x = 0; x < 220; x += 4)
                Assert.Equal(first.FillContains(new Point(x, y)) ||
                    second.FillContains(new Point(x, y)),
                    combined.FillContains(new Point(x, y)));
    }

    [Fact]
    public void SvgClipTextAppliesRelativeTspanOffsetsAndAnchorsWholeChunk()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 48, 400, 0,
            out _, out var firstWidth, out _));
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 24, 400, 0,
            out _, out var secondWidth, out _));
        var secondX = (20 + firstWidth + 30).ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string header = "<svg xmlns='http://www.w3.org/2000/svg'><defs>";
        var svg = header +
            "<clipPath id='relative'><text x='20' y='75' font-size='48'>O" +
            "<tspan dx='30' dy='20' font-size='24'>O</tspan></text></clipPath>" +
            $"<clipPath id='absolute'><text x='20' y='75' font-size='48'>O" +
            $"<tspan x='{secondX}' y='95' font-size='24'>O</tspan></text></clipPath>" +
            "<clipPath id='start'><text x='140' y='75' font-size='48'>O" +
            "<tspan font-size='24'>O</tspan></text></clipPath>" +
            "<clipPath id='middle'><text x='140' y='75' font-size='48' text-anchor='middle'>O" +
            "<tspan font-size='24'>O</tspan></text></clipPath></defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(240, 140)));
        var relative = Resolve("relative");
        var absolute = Resolve("absolute");
        Assert.Equal(absolute.Bounds.X, relative.Bounds.X, 2);
        Assert.Equal(absolute.Bounds.Right, relative.Bounds.Right, 2);
        Assert.Equal(absolute.Bounds.Bottom, relative.Bounds.Bottom, 2);
        var start = Resolve("start");
        var middle = Resolve("middle");
        Assert.Equal((firstWidth + secondWidth) / 2,
            start.Bounds.X - middle.Bounds.X, 1);
        Assert.Equal(start.Bounds.Width, middle.Bounds.Width, 2);
    }

    [Fact]
    public void SvgClipTextCoordinateListsContinueAcrossNestedTspans()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 36, 400, 0,
            out _, out var width, out _));
        var x2 = (20 + width + 15).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x3 = (20 + 2 * width + 40).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='listed'><text x='20' y='70' dx='0 15 25' " +
            "dy='0 12 -6' font-size='36'>" +
            "<tspan>O</tspan><tspan>O</tspan>O</text></clipPath>" +
            "<clipPath id='separate'>" +
            "<text x='20' y='70' font-size='36'>O</text>" +
            $"<text x='{x2}' y='82' font-size='36'>O</text>" +
            $"<text x='{x3}' y='76' font-size='36'>O</text>" +
            "</clipPath></defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(240, 120)));
        var listed = Resolve("listed");
        var separate = Resolve("separate");
        Assert.Equal(separate.Bounds.X, listed.Bounds.X, 2);
        Assert.Equal(separate.Bounds.Y, listed.Bounds.Y, 2);
        Assert.Equal(separate.Bounds.Right, listed.Bounds.Right, 2);
        Assert.Equal(separate.Bounds.Bottom, listed.Bounds.Bottom, 2);
        Assert.Equal(separate.GetArea(), listed.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextRotationMatchesGroupTransformAroundGlyphBaseline()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='glyph'><text x='50' y='70' font-size='40' " +
            "rotate='90'>O</text></clipPath>" +
            "<clipPath id='group'><g transform='rotate(90 50 70)'>" +
            "<text x='50' y='70' font-size='40'>O</text></g></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(120, 120)));
        var glyph = Resolve("glyph");
        var group = Resolve("group");
        Assert.Equal(group.Bounds.X, glyph.Bounds.X, 2);
        Assert.Equal(group.Bounds.Y, glyph.Bounds.Y, 2);
        Assert.Equal(group.Bounds.Width, glyph.Bounds.Width, 2);
        Assert.Equal(group.Bounds.Height, glyph.Bounds.Height, 2);
        Assert.Equal(group.GetArea(), glyph.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextAppliesNestedTspanTransformsInTreeOrder()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='spans'><text x='50' y='70' font-size='40'>" +
            "<tspan transform='translate(15,7)'>" +
            "<tspan transform='rotate(90 50 70)'>O</tspan></tspan>" +
            "</text></clipPath>" +
            "<clipPath id='groups'><g transform='translate(15,7)'>" +
            "<g transform='rotate(90 50 70)'>" +
            "<text x='50' y='70' font-size='40'>O</text>" +
            "</g></g></clipPath></defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(140, 140)));
        var spans = Resolve("spans");
        var groups = Resolve("groups");
        Assert.Equal(groups.Bounds.X, spans.Bounds.X, 2);
        Assert.Equal(groups.Bounds.Y, spans.Bounds.Y, 2);
        Assert.Equal(groups.Bounds.Width, spans.Bounds.Width, 2);
        Assert.Equal(groups.Bounds.Height, spans.Bounds.Height, 2);
        Assert.Equal(groups.GetArea(), spans.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextLengthSpacingMovesGlyphsAndAnchorsAdjustedText()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 40, 400, 0,
            out _, out var width, out _));
        var target = (2 * width + 30).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var secondX = (20 + width + 30).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var shortTarget = (2 * width - 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var shortSecondX = (20 + width - 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            $"<clipPath id='spacing'><text x='20' y='80' font-size='40' textLength='{target}'>OO</text></clipPath>" +
            $"<clipPath id='middle'><text x='20' y='80' font-size='40' text-anchor='middle' textLength='{target}'>OO</text></clipPath>" +
            "<clipPath id='separate'><text x='20' y='80' font-size='40'>O</text>" +
            $"<text x='{secondX}' y='80' font-size='40'>O</text></clipPath>" +
            $"<clipPath id='compressed'><text x='20' y='80' font-size='40' textLength='{shortTarget}'>OO</text></clipPath>" +
            "<clipPath id='compressedReference'><text x='20' y='80' font-size='40'>O</text>" +
            $"<text x='{shortSecondX}' y='80' font-size='40'>O</text></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(220, 140)));
        var spacing = Resolve("spacing");
        var separate = Resolve("separate");
        var middle = Resolve("middle");
        Assert.Equal(separate.Bounds.X, spacing.Bounds.X, 2);
        Assert.Equal(separate.Bounds.Right, spacing.Bounds.Right, 2);
        Assert.Equal(separate.GetArea(), spacing.GetArea(), 1);
        Assert.Equal((2 * width + 30) / 2, spacing.Bounds.X - middle.Bounds.X, 1);
        var compressed = Resolve("compressed");
        var compressedReference = Resolve("compressedReference");
        Assert.Equal(compressedReference.Bounds.X, compressed.Bounds.X, 2);
        Assert.Equal(compressedReference.Bounds.Right, compressed.Bounds.Right, 2);
        Assert.Equal(compressedReference.GetArea(), compressed.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextLengthSpacingAndGlyphsScalesInkAlongInlineAxis()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 40, 400, 0,
            out _, out var width, out _));
        var target = (4 * width).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var compressedTarget = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var secondX = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            $"<clipPath id='scaled'><text x='20' y='80' font-size='40' textLength='{target}' lengthAdjust='spacingAndGlyphs'>OO</text></clipPath>" +
            $"<clipPath id='compressed'><text x='20' y='80' font-size='40' textLength='{compressedTarget}' lengthAdjust='spacingAndGlyphs'>OO</text></clipPath>" +
            "<clipPath id='reference'><g transform='translate(20) scale(2 1)'>" +
            "<text x='0' y='80' font-size='40'>O</text>" +
            $"<text x='{secondX}' y='80' font-size='40'>O</text>" +
            "</g></clipPath>" +
            "<clipPath id='compressedReference'><g transform='translate(20) scale(0.5 1)'>" +
            "<text x='0' y='80' font-size='40'>O</text>" +
            $"<text x='{secondX}' y='80' font-size='40'>O</text>" +
            "</g></clipPath></defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(220, 140)));
        var scaled = Resolve("scaled");
        var reference = Resolve("reference");
        Assert.Equal(reference.Bounds.X, scaled.Bounds.X, 2);
        Assert.Equal(reference.Bounds.Right, scaled.Bounds.Right, 2);
        Assert.Equal(reference.Bounds.Height, scaled.Bounds.Height, 2);
        Assert.Equal(reference.GetArea(), scaled.GetArea(), 1);
        var compressed = Resolve("compressed");
        var compressedReference = Resolve("compressedReference");
        Assert.Equal(compressedReference.Bounds.X, compressed.Bounds.X, 2);
        Assert.Equal(compressedReference.Bounds.Right, compressed.Bounds.Right, 2);
        Assert.Equal(compressedReference.GetArea(), compressed.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextLengthPercentageUsesHorizontalViewportSize()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='percent'><text x='20' y='80' font-size='40' textLength='50%'>OO</text></clipPath>" +
            "<clipPath id='number'><text x='20' y='80' font-size='40' textLength='110'>OO</text></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(220, 140)));
        var percent = Resolve("percent");
        var number = Resolve("number");
        Assert.Equal(number.Bounds.X, percent.Bounds.X, 2);
        Assert.Equal(number.Bounds.Right, percent.Bounds.Right, 2);
        Assert.Equal(number.GetArea(), percent.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextPathMapsStartOffsetAndAnchorToReferencedLine()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 40, 400, 0,
            out _, out var width, out _));
        var next = (20 + width).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var middleFirst = (110 - width).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var middleSecond = 110.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<path id='line' d='M20 80 H200'/>" +
            "<clipPath id='path'><text font-size='40'><textPath href='#line'>OO</textPath></text></clipPath>" +
            "<clipPath id='offset'><text font-size='40'><textPath href='#line' startOffset='50%' text-anchor='middle'>OO</textPath></text></clipPath>" +
            "<clipPath id='reference'><text x='20' y='80' font-size='40'>O</text>" +
            $"<text x='{next}' y='80' font-size='40'>O</text></clipPath>" +
            $"<clipPath id='middle'><text x='{middleFirst}' y='80' font-size='40'>O</text>" +
            $"<text x='{middleSecond}' y='80' font-size='40'>O</text></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(240, 140)));
        var path = Resolve("path");
        var reference = Resolve("reference");
        var offset = Resolve("offset");
        var middle = Resolve("middle");
        Assert.Equal(reference.Bounds.X, path.Bounds.X, 2);
        Assert.Equal(reference.Bounds.Right, path.Bounds.Right, 2);
        Assert.Equal(reference.GetArea(), path.GetArea(), 1);
        Assert.Equal(middle.Bounds.X, offset.Bounds.X, 2);
        Assert.Equal(middle.Bounds.Right, offset.Bounds.Right, 2);
        Assert.Equal(middle.GetArea(), offset.GetArea(), 1);
    }

    [Fact]
    public void SvgClipTextPathCalibratesNumericStartOffsetUsingReferencedPathLength()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<path id='guide' d='M20 80 H220' pathLength='400'/>" +
            "<clipPath id='number'><text font-size='40'>" +
            "<textPath href='#guide' startOffset='100'>O</textPath></text></clipPath>" +
            "<clipPath id='percent'><text font-size='40'>" +
            "<textPath href='#guide' startOffset='50%'>O</textPath></text></clipPath>" +
            "<clipPath id='at70'><text x='70' y='80' font-size='40'>O</text></clipPath>" +
            "<clipPath id='at120'><text x='120' y='80' font-size='40'>O</text></clipPath>" +
            "</defs></svg>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        PathGeometry Resolve(string id) => Assert.IsType<PathGeometry>(
            Assert.IsType<SvgParser.ClipPathDefinition>(
                SvgParser.ParseClipPathDefinition(bytes, id)).Resolve(new Size(240, 140)));
        var number = Resolve("number");
        var percent = Resolve("percent");
        var at70 = Resolve("at70");
        var at120 = Resolve("at120");
        Assert.Equal(at70.Bounds.X, number.Bounds.X, 2);
        Assert.Equal(at70.GetArea(), number.GetArea(), 1);
        Assert.Equal(at120.Bounds.X, percent.Bounds.X, 2);
        Assert.Equal(at120.GetArea(), percent.GetArea(), 1);
    }

    [Fact]
    public async Task UrlClipPathLoadsSvgDefinitionAndTracksObjectBoundingBox()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='triangle' clipPathUnits='objectBoundingBox'>" +
            "<polygon points='0,0 1,0 0.5,1'/>" +
            "</clipPath></defs></svg>";
        var file = Path.Combine(Path.GetTempPath(), "jalium-clip-" + Guid.NewGuid() + ".svg");
        try
        {
            await File.WriteAllTextAsync(file, svg);
            var border = new Border { Width = 100, Height = 80, Background = Brushes.Red };
            Css.SetStyle(border, $"clip-path:url('{new Uri(file).AbsoluteUri}#triangle')");
            var value = Assert.IsType<CssClipPathValue>(border.GetValue(CssClipPathProperties.ValueProperty));
            await value.UrlResource!.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            CssBorderRadiusTests.Layout(border, 100, 80);
            Assert.Same(border, border.InputHitTest(new(50, 20)));
            Assert.Null(border.InputHitTest(new(5, 70)));
            var pixels = CssBorderRadiusTests.Pixels(border, 100, 80);
            Assert.Equal((byte)255, Alpha(pixels, 100, 50, 20));
            Assert.Equal((byte)0, Alpha(pixels, 100, 5, 70));

            border.Width = 200;
            CssBorderRadiusTests.Layout(border, 200, 80);
            Assert.Same(border, border.InputHitTest(new(150, 20)));
            Assert.Null(border.InputHitTest(new(85, 70)));

            Css.SetStyle(border, "clip-path:none");
            Assert.Same(border, border.InputHitTest(new(85, 70)));
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrlClipPathLoadsSvgFromDataUrl(bool base64)
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'>" +
            "<clipPath id='left'><rect width='20' height='40'/></clipPath></svg>";
        var data = base64
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))
            : Uri.EscapeDataString(svg);
        var uri = "data:image/svg+xml" + (base64 ? ";base64" : "") + "," + data + "#left";
        var border = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        Css.SetStyle(border, $"clip-path:url('{uri}')");
        var value = Assert.IsType<CssClipPathValue>(border.GetValue(CssClipPathProperties.ValueProperty));
        await value.UrlResource!.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        CssBorderRadiusTests.Layout(border, 40, 40);
        Assert.Same(border, border.InputHitTest(new(10, 20)));
        Assert.Null(border.InputHitTest(new(30, 20)));
    }

    [Fact]
    public async Task UrlClipPathUsesImportedStylesheetBaseAndResolver()
    {
        var resources = new Dictionary<string, string>
        {
            ["https://clip.test/styles/main.css"] = "@import 'sub/clip.css';",
            ["https://clip.test/styles/sub/clip.css"] =
                ".target { clip-path: url('../masks/clip.svg#left'); }",
            ["https://clip.test/styles/masks/clip.svg"] =
                "<svg xmlns='http://www.w3.org/2000/svg'><clipPath id='left'>" +
                "<rect width='20' height='40'/></clipPath></svg>",
        };
        var resolver = new ClipResources(resources);
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://clip.test/styles/main.css"), resolver);
        var border = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        Css.SetClass(border, "target");
        Css.GetStyleSheets(border).Add(sheet);
        CssBorderRadiusTests.Layout(border, 40, 40);
        var value = Assert.IsType<CssClipPathValue>(border.GetValue(CssClipPathProperties.ValueProperty));
        await value.UrlResource!.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(new Uri("https://clip.test/styles/masks/clip.svg"), resolver.Requests);
        Assert.Same(border, border.InputHitTest(new(10, 20)));
        Assert.Null(border.InputHitTest(new(30, 20)));
    }

    private sealed class ClipResources(Dictionary<string, string> files) : ICssResourceResolver
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<Uri> Requests = new();

        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(uri);
            if (!files.TryGetValue(uri.AbsoluteUri, out var source))
                throw new FileNotFoundException(uri.AbsoluteUri);
            return ValueTask.FromResult(new CssResource(uri,
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source))));
        }
    }

    [Fact]
    public async Task UrlClipPathDistinguishesMissingAndEmptyDefinitions()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
            "<clipPath id='empty'/>" +
            "<path id='not-a-clip' d='M0 0 H40 V40 H0 Z'/>" +
            "</defs></svg>";
        var file = Path.Combine(Path.GetTempPath(), "jalium-clip-" + Guid.NewGuid() + ".svg");
        try
        {
            await File.WriteAllTextAsync(file, svg);
            var border = new Border { Width = 40, Height = 40, Background = Brushes.Red };
            CssBorderRadiusTests.Layout(border, 40, 40);
            var source = new Uri(file).AbsoluteUri;
            Css.SetStyle(border, $"clip-path:url('{source}#not-a-clip')");
            var value = Assert.IsType<CssClipPathValue>(border.GetValue(CssClipPathProperties.ValueProperty));
            await value.UrlResource!.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(CssClipPathProperties.GetGeometry(border));
            Assert.Same(border, border.InputHitTest(new(20, 20)));

            Css.SetStyle(border, $"clip-path:url('{source}#empty')");
            value = Assert.IsType<CssClipPathValue>(border.GetValue(CssClipPathProperties.ValueProperty));
            await value.UrlResource!.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsType<PathGeometry>(CssClipPathProperties.GetGeometry(border));
            Assert.Null(border.InputHitTest(new(20, 20)));
            var pixels = CssBorderRadiusTests.Pixels(border, 40, 40);
            Assert.Equal((byte)0, Alpha(pixels, 40, 20, 20));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ReferenceBoxAndNativeClipIntersect()
    {
        var border = new Border
        {
            Width = 100, Height = 80, Background = Brushes.Red,
            BorderThickness = new Thickness(10), Padding = new Thickness(10),
            Clip = new RectangleGeometry(new Rect(30, 0, 70, 80)),
        };
        Css.SetStyle(border, "clip-path:content-box");
        CssBorderRadiusTests.Layout(border, 100, 80);
        Assert.Null(border.HitTest(new(25, 40))); // native Clip wins here
        Assert.Same(border, border.HitTest(new(50, 40))?.VisualHit);
        Assert.Null(border.HitTest(new(85, 40))); // content-box ends at x=80

        Css.SetStyle(border, "clip-path:none");
        Assert.Same(border, border.HitTest(new(85, 40))?.VisualHit);
    }

    [Fact]
    public void SoftwareArbitraryGeometryClipUsesItsFilledShape()
    {
        var bitmap = new RenderTargetBitmap(30, 30, 96, 96, PixelFormat.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap);
        drawing.PushClip(new EllipseGeometry(new Rect(5, 5, 20, 20)));
        drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 30, 30));
        drawing.Pop();
        drawing.Close();
        var pixels = bitmap.GetPixelBuffer();
        Assert.Equal((byte)0, Alpha(pixels, 30, 6, 6));
        Assert.Equal((byte)255, Alpha(pixels, 30, 15, 15));
    }

    private static byte Alpha(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4 + 3];
}
