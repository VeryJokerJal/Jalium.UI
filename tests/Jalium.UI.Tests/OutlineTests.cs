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
    public void Css_OutlineNone_ClearsRing()
    {
        var border = new Border();
        Css.SetStyle(border, "outline: 2px solid red");
        Css.SetStyle(border, "outline: none");
        Assert.Null(border.OutlineBrush);
        Assert.Equal(0.0, border.OutlineThickness);
        Assert.Equal(OutlineStyle.None, border.OutlineStyle);
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

    [Fact]
    public void Css_OutlineStyle_DoubleDegradesToSolid()
    {
        var border = new Border();
        Css.SetStyle(border, "outline-style: double");
        Assert.Equal(OutlineStyle.Solid, border.OutlineStyle);

        Css.SetStyle(border, "outline-style: dashed");
        Assert.Equal(OutlineStyle.Dashed, border.OutlineStyle);
    }

    [Fact]
    public void Css_OutlineOffset_AllowsNegative()
    {
        var border = new Border();
        Css.SetStyle(border, "outline-offset: -2px");
        Assert.Equal(-2.0, border.OutlineOffset);
    }

    [Fact]
    public void Css_OutlineColor_Invert_FallsBackToForeground()
    {
        var block = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xAA, 0xBB, 0xCC)) };
        Css.SetStyle(block, "outline-color: invert");
        var brush = Assert.IsType<SolidColorBrush>(block.OutlineBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0xAA, 0xBB, 0xCC), brush.Color);
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
