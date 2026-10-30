using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssTextBlockBorderTests
{
    [Fact]
    public void EmptyText_MeasuresCssBorderAndPadding()
    {
        var text = new TextBlock();
        Css.SetStyle(text, "border:5px solid red; padding:3px");
        text.Measure(new Size(80, 60));
        Assert.Equal(new Thickness(5), CssBoxMetrics.BackgroundInsets(text, 80).Border);
        Assert.Equal(new Size(16, 16), text.DesiredSize);
    }

    [Fact]
    public void WidthWithoutStyleDoesNotReserveATextBorder()
    {
        var text = new TextBlock();
        Css.SetStyle(text, "border-width:10px; padding:3px");
        text.Measure(new Size(80, 60));
        Assert.Equal(new Thickness(0), CssBoxMetrics.BackgroundInsets(text, 80).Border);
        Assert.Equal(new Size(6, 6), text.DesiredSize);
    }

    [Fact]
    public void ContentBoxWidthIncludesCssBorderAndPadding()
    {
        var text = new TextBlock();
        Css.SetStyle(text,
            "width:20px; box-sizing:content-box; border:5px solid red; padding:3px");
        CssBorderRadiusTests.Layout(text, 80, 60);
        Assert.Equal(36, text.VisualBounds.Width);
    }

    [Theory]
    [InlineData("")]
    [InlineData("border-radius:20px")]
    public void TextBlock_PaintsFourCssBorderSides(string radius)
    {
        var text = new TextBlock();
        Css.SetStyle(text,
            "border:8px solid; border-color:red green blue yellow; " + radius);
        CssBorderRadiusTests.Layout(text, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(text, 80, 60);
        Assert.Equal((byte)255, pixels[(4 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)128, pixels[(30 * 80 + 76) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(56 * 80 + 40) * 4]);
        Assert.Equal((byte)255, pixels[(30 * 80 + 4) * 4 + 1]);
        Assert.Equal((byte)0, pixels[(30 * 80 + 40) * 4 + 3]);
        if (radius.Length > 0) Assert.Equal((byte)0, pixels[3]);
    }

    [Fact]
    public void TextBlock_BackgroundContentClipUsesCssBorderAndPadding()
    {
        var text = new TextBlock();
        Css.SetStyle(text,
            "border:8px solid blue; padding:4px; background-color:red; " +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(text, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(text, 80, 60);
        Assert.Equal((byte)255, pixels[(4 * 80 + 40) * 4]);
        Assert.Equal((byte)0, pixels[(10 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(20 * 80 + 40) * 4 + 2]);
    }

    [Fact]
    public void RemovingCssBorderRestoresTextInsets()
    {
        var text = new TextBlock();
        Css.SetStyle(text, "border:5px solid red; padding:3px");
        text.Measure(new Size(80, 60));
        Css.SetStyle(text, "padding:3px");
        text.Measure(new Size(80, 60));
        Assert.Equal(new Thickness(0), CssBoxMetrics.BackgroundInsets(text, 80).Border);
        Assert.Equal(new Size(6, 6), text.DesiredSize);
    }

    [Fact]
    public void TextOriginStartsAfterCssBorderAndPadding()
    {
        var text = new TextBlock { Text = "X" };
        Css.SetStyle(text, "border-left:7px solid red; padding:3px");
        CssBorderRadiusTests.Layout(text, 80, 60);
        var context = new TextOriginContext();
        text.Render(context);
        Assert.Equal(10, Assert.Single(context.TextOrigins).X);
    }

    [Fact]
    public void TopBorderMovesTheCenteredTextBaselineIntoItsContentBox()
    {
        var plain = new TextBlock { Text = "X" };
        var bordered = new TextBlock { Text = "X" };
        Css.SetStyle(bordered, "border-top:8px solid red");
        CssBorderRadiusTests.Layout(plain, 80, 60);
        CssBorderRadiusTests.Layout(bordered, 80, 60);
        Assert.Equal(4,
            bordered.GetFirstBaselineOffset(60) - plain.GetFirstBaselineOffset(60), 6);
    }

    [Fact]
    public void DoubleBorderKeepsItsMiddleGap()
    {
        var text = new TextBlock();
        Css.SetStyle(text, "border:9px double red");
        CssBorderRadiusTests.Layout(text, 80, 60);
        var pixels = CssBorderRadiusTests.Pixels(text, 80, 60);
        Assert.Equal((byte)255, pixels[(1 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)0, pixels[(4 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(7 * 80 + 40) * 4 + 2]);
    }

    private sealed class TextOriginContext : DrawingContextAdapter
    {
        internal List<Point> TextOrigins { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin) => TextOrigins.Add(origin);
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
