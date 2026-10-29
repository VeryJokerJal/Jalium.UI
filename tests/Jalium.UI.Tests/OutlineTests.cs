using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// CSS outline: four FrameworkElement DPs, self-drawn ring outside the bounds,
/// zero layout impact, dirty-padding mirroring, and the CSS property entries.
/// </summary>
public sealed class OutlineTests
{
    static OutlineTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    private sealed class RecordingOutlineContext : DrawingContextAdapter
    {
        public readonly List<(Brush? Brush, Pen? Pen, Rect Rect, CornerRadius Radius)> RoundedRects = new();
        public readonly List<(Brush? Brush, Pen? Pen, Geometry Geometry)> Geometries = new();

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)
            => RoundedRects.Add((brush, pen, rectangle, cornerRadius));

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
            => Geometries.Add((brush, pen, geometry));

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
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

    private static Border MakeArranged(double width = 100, double height = 40)
    {
        var border = new Border { Width = width, Height = height };
        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        return border;
    }

    [Fact]
    public void Defaults_NoOutline()
    {
        var border = new Border();
        Assert.Null(border.OutlineBrush);
        Assert.Equal(0.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.Solid, border.OutlineStyle);
        Assert.Equal(0.0, border.OutlineOffset);
        Assert.Equal(0.0, border.GetExtraDirtyPadding());
    }

    [Fact]
    public void RenderGate_AllComponentsRequired()
    {
        var border = MakeArranged();
        var red = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0, 0));

        void AssertDrawCount(int expected)
        {
            var dc = new RecordingOutlineContext();
            border.Render(dc);
            Assert.Equal(expected, dc.RoundedRects.Count(c => c.Pen is not null) + dc.Geometries.Count(c => c.Pen is not null));
        }

        AssertDrawCount(0);

        border.OutlineBrush = red;
        AssertDrawCount(0); // thickness still 0

        border.OutlineThickness = 2;
        AssertDrawCount(1);

        border.OutlineStyle = OutlineStyle.None;
        AssertDrawCount(0);

        border.OutlineStyle = OutlineStyle.Solid;
        border.OutlineBrush = null;
        AssertDrawCount(0);
    }

    [Fact]
    public void Outline_DoesNotAffectLayout()
    {
        var border = MakeArranged(100, 40);
        var desiredBefore = border.DesiredSize;

        border.OutlineBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0xFF));
        border.OutlineThickness = 6;
        border.OutlineOffset = 4;

        Assert.True(border.IsMeasureValid);
        Assert.Equal(desiredBefore, border.DesiredSize);
    }

    [Fact]
    public void DirtyPadding_TracksRingAndIsMonotonic()
    {
        var border = MakeArranged();
        border.OutlineBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0));
        border.OutlineThickness = 2;
        border.OutlineOffset = 3;
        Assert.Equal(5.0, border.GetExtraDirtyPadding());

        // Shrinking keeps the old extent registered (residue protection).
        border.OutlineThickness = 1;
        Assert.Equal(5.0, border.GetExtraDirtyPadding());

        // Negative offset clamps to zero contribution but never below prior extent.
        border.OutlineOffset = -10;
        Assert.Equal(5.0, border.GetExtraDirtyPadding());
    }

    [Fact]
    public void Solid_DrawsExpandedRoundedRect()
    {
        var border = MakeArranged(100, 40);
        border.OutlineBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0, 0));
        border.OutlineThickness = 2;
        border.OutlineOffset = 1;

        var dc = new RecordingOutlineContext();
        border.Render(dc);

        var call = Assert.Single(dc.RoundedRects, c => c.Pen is not null);
        // expand = offset + thickness/2 = 2.
        Assert.Equal(-2, call.Rect.X, 6);
        Assert.Equal(-2, call.Rect.Y, 6);
        Assert.Equal(104, call.Rect.Width, 6);
        Assert.Equal(44, call.Rect.Height, 6);
        Assert.Equal(2.0, call.Pen!.Thickness);
        Assert.Null(call.Brush);
    }

    [Fact]
    public void CornerRadius_ExpandsWithRing_ZeroCornersStaySquare()
    {
        var border = MakeArranged(100, 40);
        border.CornerRadius = new CornerRadius(6, 0, 6, 0);
        border.OutlineBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0));
        border.OutlineThickness = 2;
        border.OutlineOffset = 1;

        var dc = new RecordingOutlineContext();
        border.Render(dc);

        var call = Assert.Single(dc.RoundedRects, c => c.Pen is not null);
        Assert.Equal(8.0, call.Radius.TopLeft, 6);
        Assert.Equal(0.0, call.Radius.TopRight);
        Assert.Equal(8.0, call.Radius.BottomRight, 6);
        Assert.Equal(0.0, call.Radius.BottomLeft);
    }

    [Fact]
    public void CssEllipticalRadius_ExpandsBothAxesAndKeepsIndependentCorners()
    {
        var border = MakeArranged(100, 40);
        Css.SetStyle(border,
            "border-radius:50px 20px / 10px 5px; outline:2px solid red; outline-offset:1px");

        var dc = new RecordingOutlineContext();
        border.RenderOutlineCore(dc);

        var call = Assert.Single(dc.Geometries, c => c.Pen is not null);
        var geometry = Assert.IsType<CssRoundedRectangleGeometry>(call.Geometry);
        Assert.Equal(new Rect(-2, -2, 104, 44), geometry.Rect);
        Assert.Equal(new Size(52, 12), geometry.Radii.TopLeft);
        Assert.Equal(new Size(22, 7), geometry.Radii.TopRight);
        Assert.Equal(new Size(52, 12), geometry.Radii.BottomRight);
        Assert.Equal(new Size(22, 7), geometry.Radii.BottomLeft);
        Assert.Empty(dc.RoundedRects);
    }

    [Fact]
    public void CssPercentageOutline_ReResolvesOnSizeAndNativeRadiusTakesOver()
    {
        var border = MakeArranged(100, 40);
        Css.SetStyle(border, "border-radius:50%; outline:2px solid red; outline-offset:1px");

        var firstContext = new RecordingOutlineContext();
        border.RenderOutlineCore(firstContext);
        var first = Assert.IsType<CssRoundedRectangleGeometry>(
            Assert.Single(firstContext.Geometries, c => c.Pen is not null).Geometry);
        Assert.Equal(new Size(52, 22), first.Radii.TopLeft);

        border.Width = 200;
        CssBorderRadiusTests.Layout(border, 200, 40);
        var resizedContext = new RecordingOutlineContext();
        border.RenderOutlineCore(resizedContext);
        var resized = Assert.IsType<CssRoundedRectangleGeometry>(
            Assert.Single(resizedContext.Geometries, c => c.Pen is not null).Geometry);
        Assert.Equal(new Size(102, 22), resized.Radii.TopLeft);
        Assert.NotSame(first, resized);

        border.CornerRadius = new CornerRadius(6, 0, 6, 0);
        var nativeContext = new RecordingOutlineContext();
        border.RenderOutlineCore(nativeContext);
        var native = Assert.Single(nativeContext.RoundedRects, c => c.Pen is not null);
        Assert.Equal(new CornerRadius(8, 0, 8, 0), native.Radius);
        Assert.DoesNotContain(nativeContext.Geometries, c => c.Pen is not null);
    }

    [Fact]
    public void DashedAndDotted_UseGeometryPathWithDashes()
    {
        var border = MakeArranged();
        border.OutlineBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0));
        border.OutlineThickness = 2;
        border.OutlineStyle = OutlineStyle.Dashed;

        var dc = new RecordingOutlineContext();
        border.Render(dc);
        var call = Assert.Single(dc.Geometries, c => c.Pen is not null);
        Assert.NotEmpty(call.Pen!.DashStyle.Dashes);
        Assert.DoesNotContain(dc.RoundedRects, c => c.Pen is not null);

        border.OutlineStyle = OutlineStyle.Dotted;
        var dc2 = new RecordingOutlineContext();
        border.Render(dc2);
        var dotted = Assert.Single(dc2.Geometries, c => c.Pen is not null);
        Assert.Equal(new[] { 1.0, 2.0 }, dotted.Pen!.DashStyle.Dashes);
    }

    [Fact]
    public void Css_OutlineShorthand_SetsAllThree()
    {
        var border = new Border();
        Css.SetStyle(border, "outline: 2px solid red");
        var brush = Assert.IsType<SolidColorBrush>(border.OutlineBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0, 0), brush.Color);
        Assert.Equal(2.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.Solid, border.OutlineStyle);
    }

    [Fact]
    public void Css_OutlineNone_ResetsComponentsAndSuppressesRing()
    {
        var border = MakeArranged();
        Css.SetStyle(border, "outline: 2px solid red");
        Css.SetStyle(border, "outline: none");
        Assert.NotNull(border.OutlineBrush);
        Assert.Equal(3.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.None, border.OutlineStyle);
        var dc = new RecordingOutlineContext();
        border.RenderOutlineCore(dc);
        Assert.Empty(dc.Geometries);
        Assert.Empty(dc.RoundedRects);
    }

    [Theory]
    [InlineData("2px 3px solid red")]
    [InlineData("2px solid dashed red")]
    [InlineData("2px solid red blue")]
    [InlineData("none solid")]
    public void Css_OutlineShorthand_RejectsDuplicateComponents(string value)
    {
        Assert.False(Css.Supports("outline", value));
        var border = new Border();
        Css.SetStyle(border, $"outline:2px solid red; outline:{value}");
        Assert.Equal(2.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.Solid, border.OutlineStyle);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(border.OutlineBrush).Color);
    }

    [Fact]
    public void Css_OmittedStyleResetsToNone_PerCssInitialValue()
    {
        // `outline: 2px red` has style=none per the CSS initial value — no visible ring.
        var border = new Border();
        Css.SetStyle(border, "outline: 2px red");
        Assert.Equal(2.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.None, border.OutlineStyle);
        Assert.NotNull(border.OutlineBrush);
    }

    [Fact]
    public void Css_SoloStyle_DefaultsWidthAndCurrentColor()
    {
        var block = new TextBlock();
        var foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x11, 0x22, 0x33));
        block.Foreground = foreground;
        Css.SetStyle(block, "outline: solid");
        Assert.Equal(3.0, block.OutlineThickness);
        Assert.Equal(OutlineStyle.Solid, block.OutlineStyle);
        var brush = Assert.IsType<SolidColorBrush>(block.OutlineBrush);
        Assert.Equal(foreground.Color, brush.Color);
    }

    [Theory]
    [InlineData("outline-width: thick", 5.0)]
    [InlineData("outline-width: thin", 1.0)]
    [InlineData("outline-width: 4px", 4.0)]
    public void Css_OutlineWidth_Keywords(string style, double expected)
    {
        var border = new Border();
        Css.SetStyle(border, style);
        Assert.Equal(expected, border.OutlineThickness);
    }

    [Fact]
    public void Css_OutlineWidth_Em()
    {
        var block = new TextBlock { FontSize = 20 };
        Css.SetStyle(block, "outline-width: 0.5em");
        Assert.Equal(10.0, block.OutlineThickness);
    }

    [Theory]
    [InlineData(".5em", 10.0)]
    [InlineData("calc(.25em + 2px)", 7.0)]
    public void Css_OutlineShorthand_ResolvesRelativeWidthAndFontChanges(string width, double expected)
    {
        var block = new TextBlock { FontSize = 20 };
        Css.SetStyle(block, $"outline:{width} solid red");
        Assert.Equal(expected, block.OutlineThickness, 2);
        Assert.Equal(OutlineStyle.Solid, block.OutlineStyle);

        block.FontSize = 40;
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal(expected * 2 - (width.StartsWith("calc", StringComparison.Ordinal) ? 2 : 0),
            block.OutlineThickness, 2);
    }

    [Theory]
    [InlineData("-2px solid red")]
    [InlineData("10% solid red")]
    public void Css_OutlineShorthand_RejectsInvalidWidths(string value)
        => Assert.False(Css.Supports("outline", value));

    [Fact]
    public void Css_DoubleOutline_UsesTwoBandsAroundTheEllipticalContour()
    {
        var border = MakeArranged();
        Css.SetStyle(border, "border-radius:30px / 10px; outline:9px double red");
        Assert.Equal(OutlineStyle.Double, border.OutlineStyle);

        var dc = new RecordingOutlineContext();
        border.RenderOutlineCore(dc);
        Assert.Equal(2, dc.Geometries.Count);
        var outer = Assert.IsType<CssRoundedRectangleGeometry>(dc.Geometries[0].Geometry);
        var inner = Assert.IsType<CssRoundedRectangleGeometry>(dc.Geometries[1].Geometry);
        Assert.Equal(3, dc.Geometries[0].Pen!.Thickness);
        Assert.Equal(3, dc.Geometries[1].Pen!.Thickness);
        Assert.Equal(new Rect(-7.5, -7.5, 115, 55), outer.Rect);
        Assert.Equal(new Rect(-1.5, -1.5, 103, 43), inner.Rect);
        Assert.Equal(new Size(37.5, 17.5), outer.Radii.TopLeft);
        Assert.Equal(new Size(31.5, 11.5), inner.Radii.TopLeft);

        var secondFrame = new RecordingOutlineContext();
        border.RenderOutlineCore(secondFrame);
        Assert.Same(outer, secondFrame.Geometries[0].Geometry);
        Assert.Same(inner, secondFrame.Geometries[1].Geometry);
    }

    [Fact]
    public void Css_DoubleOutline_PaintsTwoBandsWithAVisibleGap()
    {
        var canvas = new Canvas { Width = 100, Height = 80 };
        var border = new Border { Width = 40, Height = 20 };
        Css.SetStyle(border, "outline:9px double red");
        Canvas.SetLeft(border, 30);
        Canvas.SetTop(border, 30);
        canvas.Children.Add(border);
        CssBorderRadiusTests.Layout(canvas, 100, 80);

        var pixels = CssBorderRadiusTests.Pixels(canvas, 100, 80);
        static byte Alpha(byte[] data, int y) => data[(y * 100 + 50) * 4 + 3];
        Assert.Equal((byte)255, Alpha(pixels, 22));
        Assert.Equal((byte)0, Alpha(pixels, 25));
        Assert.Equal((byte)255, Alpha(pixels, 28));
    }

    [Theory]
    [InlineData("inset", false)]
    [InlineData("outset", true)]
    public void Css_InsetAndOutsetOutlines_ShadeOppositeSides(string style, bool topIsLighter)
    {
        var pixels = ReliefOutlinePixels($"outline:10px {style} #808080");
        var top = ReliefPixel(pixels, 70, 25);
        var right = ReliefPixel(pixels, 115, 50);
        var bottom = ReliefPixel(pixels, 70, 75);
        var left = ReliefPixel(pixels, 25, 50);
        Assert.Equal((byte)255, top.A);
        Assert.Equal(top.R, left.R);
        Assert.Equal(right.R, bottom.R);
        Assert.Equal(topIsLighter, top.R > bottom.R);
    }

    [Theory]
    [InlineData("groove", false)]
    [InlineData("ridge", true)]
    public void Css_GrooveAndRidgeOutlines_ReverseShadesAcrossTheWidth(string style, bool outerIsLighter)
    {
        var pixels = ReliefOutlinePixels($"outline:10px {style} #808080");
        var topOuter = ReliefPixel(pixels, 70, 22);
        var topInner = ReliefPixel(pixels, 70, 27);
        var bottomOuter = ReliefPixel(pixels, 70, 78);
        var bottomInner = ReliefPixel(pixels, 70, 73);
        Assert.Equal((byte)255, topOuter.A);
        Assert.Equal((byte)255, topInner.A);
        Assert.Equal(outerIsLighter, topOuter.R > topInner.R);
        Assert.Equal(outerIsLighter, bottomOuter.R < bottomInner.R);
    }

    [Fact]
    public void Css_ReliefOutline_UsesEllipticalCornersAndPreservesAlpha()
    {
        var pixels = ReliefOutlinePixels(
            "border-radius:20px / 10px; outline:10px groove rgba(128,128,128,.5)");
        Assert.Equal((byte)0, ReliefPixel(pixels, 22, 22).A);
        Assert.InRange(ReliefPixel(pixels, 70, 22).A, (byte)120, (byte)136);
        Assert.InRange(ReliefPixel(pixels, 70, 27).A, (byte)120, (byte)136);
        Assert.True(ReliefPixel(pixels, 70, 22).R < ReliefPixel(pixels, 70, 27).R);
    }

    [Fact]
    public void Css_OpaqueReliefOutline_ClosesTheJointsBetweenSides()
    {
        var solid = ReliefOutlinePixels("outline:10px solid #808080");
        var inset = ReliefOutlinePixels("outline:10px inset #808080");
        Assert.Equal((byte)255, ReliefPixel(solid, 25, 25).A);
        Assert.Equal(ReliefPixel(solid, 25, 25).A, ReliefPixel(inset, 25, 25).A);
        Assert.Equal(ReliefPixel(solid, 115, 75).A, ReliefPixel(inset, 115, 75).A);
    }

    [Theory]
    [InlineData("inset")]
    [InlineData("groove")]
    public void Css_TranslucentReliefOutline_CompositesSideJointsOnce(string style)
    {
        var solid = ReliefOutlinePixels("outline:10px solid rgba(128,128,128,.5)");
        var relief = ReliefOutlinePixels($"outline:10px {style} rgba(128,128,128,.5)");
        Assert.Equal(ReliefPixel(solid, 25, 25).A, ReliefPixel(relief, 25, 25).A);
        Assert.Equal(ReliefPixel(solid, 115, 75).A, ReliefPixel(relief, 115, 75).A);
        var roundedSolid = ReliefOutlinePixels(
            "border-radius:20px / 10px;outline:10px solid rgba(128,128,128,.5)");
        var roundedRelief = ReliefOutlinePixels(
            $"border-radius:20px / 10px;outline:10px {style} rgba(128,128,128,.5)");
        Assert.Equal(ReliefPixel(roundedSolid, 32, 29).A, ReliefPixel(roundedRelief, 32, 29).A);
        Assert.Equal(ReliefPixel(roundedSolid, 107, 70).A, ReliefPixel(roundedRelief, 107, 70).A);
        if (style == "groove")
        {
            var oddSolid = ReliefOutlinePixels("outline:9px solid rgba(128,128,128,.5)");
            var oddGroove = ReliefOutlinePixels("outline:9px groove rgba(128,128,128,.5)");
            Assert.Equal(ReliefPixel(oddSolid, 70, 25).A, ReliefPixel(oddGroove, 70, 25).A);
        }
    }

    [Fact]
    public void Css_TranslucentReliefOutline_TracksMutableCurrentColor()
    {
        var canvas = new Canvas { Width = 140, Height = 100 };
        var foreground = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
        var border = new Border { Width = 80, Height = 40 };
        border.SetValue(Jalium.UI.Documents.TextElement.ForegroundProperty, foreground);
        Css.SetStyle(border, "outline:10px inset currentColor");
        Canvas.SetLeft(border, 30);
        Canvas.SetTop(border, 30);
        canvas.Children.Add(border);
        CssBorderRadiusTests.Layout(canvas, 140, 100);

        var red = CssBorderRadiusTests.Pixels(canvas, 140, 100);
        var pixel = (25 * 140 + 70) * 4;
        Assert.True(red[pixel + 2] > red[pixel]);
        foreground.Color = Color.FromArgb(128, 0, 0, 255);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        var blue = CssBorderRadiusTests.Pixels(canvas, 140, 100);
        Assert.True(blue[pixel] > blue[pixel + 2]);
        Assert.Equal(red[pixel + 3], blue[pixel + 3]);
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("double")]
    [InlineData("inset")]
    [InlineData("groove")]
    public void Css_LargeNegativeOutlineOffset_ClampsTheOutsideToTwiceTheWidth(string style)
    {
        var border = MakeArranged(100, 40);
        Css.SetStyle(border, $"outline:10px {style} red;outline-offset:-100px");
        var dc = new RecordingOutlineContext();
        border.RenderOutlineCore(dc);

        var painted = dc.RoundedRects.Select(call =>
                new Rect(call.Rect.Left - call.Pen!.Thickness / 2,
                    call.Rect.Top - call.Pen.Thickness / 2,
                    call.Rect.Width + call.Pen.Thickness,
                    call.Rect.Height + call.Pen.Thickness))
            .Concat(dc.Geometries.Select(call => call.Geometry.Bounds))
            .ToArray();
        Assert.NotEmpty(painted);
        Assert.Equal(10, painted.Min(rect => rect.Left), 6);
        Assert.Equal(10, painted.Min(rect => rect.Top), 6);
        Assert.Equal(90, painted.Max(rect => rect.Right), 6);
        Assert.Equal(30, painted.Max(rect => rect.Bottom), 6);
    }

    private static byte[] ReliefOutlinePixels(string css)
    {
        var canvas = new Canvas { Width = 140, Height = 100 };
        var border = new Border { Width = 80, Height = 40 };
        Css.SetStyle(border, css);
        Canvas.SetLeft(border, 30);
        Canvas.SetTop(border, 30);
        canvas.Children.Add(border);
        CssBorderRadiusTests.Layout(canvas, 140, 100);
        return CssBorderRadiusTests.Pixels(canvas, 140, 100);
    }

    private static (byte R, byte A) ReliefPixel(byte[] pixels, int x, int y)
    {
        var offset = (y * 140 + x) * 4;
        return (pixels[offset + 2], pixels[offset + 3]);
    }

    [Fact]
    public void Css_OutlineOffset_AllowsNegative()
    {
        var border = new Border();
        Css.SetStyle(border, "outline-offset: -2px");
        Assert.Equal(-2.0, border.OutlineOffset);
    }

    [Fact]
    public void Css_OutlineColor_RejectsRemovedInvertKeyword()
    {
        var block = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xAA, 0xBB, 0xCC)) };
        Assert.False(Css.Supports("outline-color", "invert"));
        Assert.False(Css.Supports("outline", "solid invert"));
        Css.SetStyle(block, "outline-color: red; outline-color: invert");
        var brush = Assert.IsType<SolidColorBrush>(block.OutlineBrush);
        Assert.Equal(Colors.Red, brush.Color);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("auto auto")]
    [InlineData("2px auto")]
    [InlineData("auto solid")]
    [InlineData("auto red")]
    [InlineData("solid auto")]
    public void Css_OutlineShorthand_ParsesAutoSlots(string value)
        => Assert.True(Css.Supports("outline", value));

    [Theory]
    [InlineData("auto auto auto")]
    [InlineData("auto auto red")]
    [InlineData("auto red blue")]
    public void Css_OutlineShorthand_RejectsDuplicateAutoSlots(string value)
        => Assert.False(Css.Supports("outline", value));

    [Fact]
    public void Css_OutlineAuto_UsesAccentAndIgnoresAuthoredWidth()
    {
        var border = MakeArranged();
        var accent = new SolidColorBrush(Colors.Green);
        border.Resources["AccentBrush"] = accent;
        Css.SetStyle(border, "outline: 0 auto");

        Assert.Equal(OutlineStyle.Auto, border.OutlineStyle);
        Assert.True(CssOutlineAutoColorProperties.IsAuto(border));
        var drawing = new RecordingOutlineContext();
        border.RenderOutlineCore(drawing);
        var stroke = Assert.Single(drawing.RoundedRects).Pen!;
        Assert.Same(accent, stroke.Brush);
        Assert.Equal(3, stroke.Thickness);

        Css.SetStyle(border, "outline: 1px auto red");
        Assert.False(CssOutlineAutoColorProperties.IsAuto(border));
        drawing = new RecordingOutlineContext();
        border.RenderOutlineCore(drawing);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Assert.Single(drawing.RoundedRects).Pen!.Brush).Color);
        Assert.Equal(3, Assert.Single(drawing.RoundedRects).Pen!.Thickness);
    }

    [Fact]
    public void Css_OutlineLonghands_UseCssInitialsForOmittedParts()
    {
        var border = MakeArranged();
        border.SetValue(Jalium.UI.Documents.TextElement.ForegroundProperty, Brushes.Blue);
        border.Resources["AccentBrush"] = Brushes.Green;

        Css.SetStyle(border, "outline-style:auto");
        Assert.Equal(3, border.OutlineThickness);
        Assert.True(CssOutlineAutoColorProperties.IsAuto(border));
        var drawing = new RecordingOutlineContext();
        border.RenderOutlineCore(drawing);
        Assert.Same(Brushes.Green, Assert.Single(drawing.RoundedRects).Pen!.Brush);

        Css.SetStyle(border, "outline-style:solid");
        drawing = new RecordingOutlineContext();
        border.RenderOutlineCore(drawing);
        Assert.Equal(Colors.Blue,
            Assert.IsType<SolidColorBrush>(Assert.Single(drawing.RoundedRects).Pen!.Brush).Color);

        Css.SetStyle(border, "outline-color:red");
        Assert.Equal(OutlineStyle.None, border.OutlineStyle);
        drawing = new RecordingOutlineContext();
        border.RenderOutlineCore(drawing);
        Assert.Empty(drawing.RoundedRects);
    }

    [Fact]
    public void Css_OutlineColorAuto_UsesForegroundForSolidAndPreservesNativeBrush()
    {
        var block = new TextBlock { Text = "Hi", Foreground = Brushes.Blue };
        CssBorderRadiusTests.Layout(block, 100, 40);
        Css.SetStyle(block, "outline: 2px solid auto");
        Assert.True(CssOutlineAutoColorProperties.IsAuto(block));
        var drawing = new RecordingOutlineContext();
        block.RenderOutlineCore(drawing);
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(Assert.Single(drawing.RoundedRects).Pen!.Brush).Color);

        block.Foreground = Brushes.Green;
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        drawing = new RecordingOutlineContext();
        block.RenderOutlineCore(drawing);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(Assert.Single(drawing.RoundedRects).Pen!.Brush).Color);

        block.OutlineBrush = Brushes.Red;
        Css.SetStyle(block, "outline: auto");
        drawing = new RecordingOutlineContext();
        block.RenderOutlineCore(drawing);
        Assert.Same(Brushes.Red, Assert.Single(drawing.RoundedRects).Pen!.Brush);
    }

    [Fact]
    public void Css_OutlineColorAuto_WideKeywordsKeepTheComputedAutoValue()
    {
        var parent = new StackPanel();
        var child = MakeArranged();
        parent.Children.Add(child);
        Css.SetStyle(parent, "outline-color:auto;outline-style:auto");
        Css.SetStyle(child, "outline-color:inherit");
        Assert.True(CssOutlineAutoColorProperties.IsAuto(child));

        Css.SetStyle(child, "outline-color:red");
        Assert.False(CssOutlineAutoColorProperties.IsAuto(child));
        Css.SetStyle(child, "outline-color:initial");
        Assert.True(CssOutlineAutoColorProperties.IsAuto(child));
        Css.SetStyle(child, "outline-color:unset");
        Assert.True(CssOutlineAutoColorProperties.IsAuto(child));
    }

    [Fact]
    public void Css_OutlineColorInheritUsesTheParentsComputedAutoOrChildCurrentColor()
    {
        var parent = new StackPanel();
        parent.SetValue(Jalium.UI.Documents.TextElement.ForegroundProperty, Brushes.Blue);
        var child = new Border { Width = 100, Height = 40 };
        child.SetValue(Jalium.UI.Documents.TextElement.ForegroundProperty, Brushes.Red);
        child.Resources["AccentBrush"] = Brushes.Green;
        parent.Children.Add(child);
        CssBorderRadiusTests.Layout(parent, 100, 40);
        Css.SetStyle(parent, "outline-color:auto;outline-style:solid");
        Css.SetStyle(child, "outline-color:inherit;outline-style:auto;outline-width:2px");

        Assert.False(CssOutlineAutoColorProperties.IsAuto(child));
        var drawing = new RecordingOutlineContext();
        child.RenderOutlineCore(drawing);
        Assert.Equal(Colors.Red,
            Assert.IsType<SolidColorBrush>(Assert.Single(drawing.RoundedRects).Pen!.Brush).Color);

        Css.SetStyle(parent, "outline-color:auto;outline-style:auto");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.True(CssOutlineAutoColorProperties.IsAuto(child));
        drawing = new RecordingOutlineContext();
        child.RenderOutlineCore(drawing);
        Assert.Same(Brushes.Green, Assert.Single(drawing.RoundedRects).Pen!.Brush);
    }

    [Fact]
    public void Css_FocusVisible_OutlineLandsInStateLayer()
    {
        var border = new Border();
        var panel = new StackPanel();
        panel.Children.Add(border);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(
            "Border:focus-visible { outline: 2px solid red }"));
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.Null(border.OutlineBrush);

        border.UpdateIsKeyboardFocused(true);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.NotNull(border.OutlineBrush);
        Assert.Equal(2.0, border.OutlineThickness);
        Assert.Equal(BaseValueSource.StyleTrigger,
            DependencyPropertyHelper.GetValueSource(border, FrameworkElement.OutlineThicknessProperty).BaseValueSource);

        border.UpdateIsKeyboardFocused(false);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Null(border.OutlineBrush);
        Assert.Equal(0.0, border.OutlineThickness);
    }

    [Fact]
    public void Xaml_OutlineAttributesParse()
    {
        const string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    OutlineThickness="2" OutlineStyle="Dashed" OutlineOffset="1" />
            """;
        var border = Assert.IsType<Border>(Jalium.UI.Markup.XamlReader.Parse(xaml));
        Assert.Equal(2.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.Dashed, border.OutlineStyle);
        Assert.Equal(1.0, border.OutlineOffset);
    }
}
