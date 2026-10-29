using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssCaretShapeTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("bar")]
    [InlineData("block")]
    [InlineData("underscore")]
    [InlineData("inherit")]
    public void CaretShapeAcceptsItsKeywords(string value)
        => Assert.True(Css.Supports("caret-shape", value));

    [Theory]
    [InlineData("none")]
    [InlineData("bar block")]
    [InlineData("vertical")]
    public void CaretShapeRejectsOtherValues(string value)
        => Assert.False(Css.Supports("caret-shape", value));

    [Theory]
    [InlineData("auto")]
    [InlineData("block manual #123456")]
    [InlineData("currentColor underscore")]
    [InlineData("manual color-mix(in srgb, red 50%, blue)")]
    [InlineData("auto auto auto")]
    [InlineData("inherit")]
    public void CaretShorthandAcceptsOneValueForEachLonghand(string value)
        => Assert.True(Css.Supports("caret", value));

    [Theory]
    [InlineData("none")]
    [InlineData("bar block")]
    [InlineData("manual manual")]
    [InlineData("red blue")]
    [InlineData("auto auto auto auto")]
    [InlineData("inherit bar")]
    public void CaretShorthandRejectsInvalidCombinations(string value)
        => Assert.False(Css.Supports("caret", value));

    [Fact]
    public void ShorthandResetsOmittedPartsAndWideKeywordsInherit()
    {
        var parent = new StackPanel();
        var box = new TextBox();
        parent.Children.Add(box);
        Css.SetStyle(parent, "caret: red manual block");
        Assert.Equal(CssCaretShape.Block, CssCaretShapeProperties.Get(box));
        Assert.True(CssCaretAnimationProperties.IsManual(box));
        Assert.Equal(Colors.Red.ToArgb(), Assert.IsType<SolidColorBrush>(CssCaretColorProperties.Get(box)).Color.ToArgb());

        Css.SetStyle(box, "caret: blue");
        Assert.Equal(CssCaretShape.Auto, CssCaretShapeProperties.Get(box));
        Assert.False(CssCaretAnimationProperties.IsManual(box));
        Assert.Equal(Colors.Blue.ToArgb(), Assert.IsType<SolidColorBrush>(CssCaretColorProperties.Get(box)).Color.ToArgb());

        Css.SetStyle(box, "caret: inherit");
        Assert.Equal(CssCaretShape.Block, CssCaretShapeProperties.Get(box));
        Assert.True(CssCaretAnimationProperties.IsManual(box));
        Assert.Equal(Colors.Red.ToArgb(), Assert.IsType<SolidColorBrush>(CssCaretColorProperties.Get(box)).Color.ToArgb());

        Css.SetStyle(box, "caret: initial");
        Assert.Equal(CssCaretShape.Auto, CssCaretShapeProperties.Get(box));
        Assert.False(CssCaretAnimationProperties.IsManual(box));
        Assert.Null(CssCaretColorProperties.Get(box));

        Css.SetStyle(box, "caret: unset");
        Assert.Equal(CssCaretShape.Block, CssCaretShapeProperties.Get(box));
        Assert.True(CssCaretAnimationProperties.IsManual(box));
    }

    [Fact]
    public void NextAdvanceUsesOneGraphemeAndFallsBackAtLineEnd()
    {
        Assert.Equal(12, CssCaretPainter.NextAdvance("e\u0301B", 0, index => index * 6, 8));
        Assert.Equal(6, CssCaretPainter.NextAdvance("e\u0301B", 2, index => index * 6, 8));
        Assert.Equal(8, CssCaretPainter.NextAdvance("e\u0301B", 3, _ => throw new Exception(), 8));
        Assert.Equal(8, CssCaretPainter.NextAdvance("A", 0, _ => double.NaN, 8));
    }

    [Fact]
    public void PainterKeepsBlockGlyphVisibleAndDrawsHorizontalUnderscore()
    {
        var drawing = new CaretDrawingContext();
        var brush = Brushes.Red;
        var dirty = CssCaretPainter.Draw(drawing, brush, CssCaretShape.Block, 4, 10, 18, 9, 2);
        Assert.Equal(new Rect(4, 10, 9, 18), drawing.Rectangles[0].Bounds);
        Assert.Same(brush, drawing.Rectangles[0].Brush);
        Assert.Null(drawing.Rectangles[0].Pen);
        Assert.Null(drawing.Rectangles[1].Brush);
        Assert.NotNull(drawing.Rectangles[1].Pen);
        Assert.Equal([0.38], drawing.Opacities);
        Assert.Equal(new Rect(2, 8, 13, 22), dirty);

        drawing.Clear();
        CssCaretPainter.Draw(drawing, brush, CssCaretShape.Underscore, 4, 10, 18, 9, 2);
        var underline = Assert.Single(drawing.Lines);
        Assert.Equal(new Point(4, 27), underline.Start);
        Assert.Equal(new Point(13, 27), underline.End);

        drawing.Clear();
        CssCaretPainter.Draw(drawing, brush, CssCaretShape.Bar, 4, 10, 18, 9, 2);
        var bar = Assert.Single(drawing.Lines);
        Assert.Equal(new Point(4, 10), bar.Start);
        Assert.Equal(new Point(4, 28), bar.End);
    }

    [Fact]
    public void TextBoxAndHexEditorRenderConfiguredShapes()
    {
        var box = new TextBox { Text = "AB", CaretIndex = 0 };
        Css.SetStyle(box, "caret: red manual block");
        CssBorderRadiusTests.Layout(box, 200, 40);
        var drawing = new CaretDrawingContext();
        DrawTextCaret(box, drawing);
        Assert.Equal(2, drawing.Rectangles.Count);
        Assert.True(drawing.Rectangles[0].Bounds.Width > 2);
        Assert.Equal(Colors.Red.ToArgb(), Assert.IsType<SolidColorBrush>(drawing.Rectangles[0].Brush).Color.ToArgb());

        Css.SetStyle(box, "caret: blue manual underscore");
        drawing.Clear();
        DrawTextCaret(box, drawing);
        Assert.Single(drawing.Lines);
        Assert.Equal(drawing.Lines[0].Start.Y, drawing.Lines[0].End.Y);

        var hex = new HexEditor { Data = [0x41] };
        Css.SetStyle(hex, "caret-shape: block");
        CssBorderRadiusTests.Layout(hex, 320, 100);
        drawing.Clear();
        typeof(HexEditor).GetMethod("DrawCaret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(hex, [drawing]);
        Assert.Equal(2, drawing.Rectangles.Count);
        Assert.True(drawing.Rectangles[0].Bounds.Width > 2);
    }

    [Fact]
    public void MaskedPasswordBlockSpansTheNextWholeGrapheme()
    {
        var single = new PasswordBox { Password = "a", CaretIndex = 0 };
        var combined = new PasswordBox { Password = "e\u0301", CaretIndex = 0 };
        Css.SetStyle(single, "caret: block manual");
        Css.SetStyle(combined, "caret: block manual");
        CssBorderRadiusTests.Layout(single, 200, 40);
        CssBorderRadiusTests.Layout(combined, 200, 40);

        var drawing = new CaretDrawingContext();
        DrawPasswordCaret(single, drawing);
        var singleWidth = drawing.Rectangles[0].Bounds.Width;
        drawing.Clear();
        DrawPasswordCaret(combined, drawing);
        Assert.InRange(drawing.Rectangles[0].Bounds.Width / singleWidth, 1.8, 2.2);
    }

    private static void DrawTextCaret(TextBox box, CaretDrawingContext drawing)
        => typeof(TextBox).GetMethod("DrawCaret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, [drawing, new Rect(0, 0, 200, 30), 20.0]);

    private static void DrawPasswordCaret(PasswordBox box, CaretDrawingContext drawing)
        => typeof(PasswordBox).GetMethod("DrawCaret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, [drawing, new Rect(0, 0, 200, 30), 20.0]);

    private sealed class CaretDrawingContext : DrawingContextAdapter
    {
        internal readonly List<(Brush? Brush, Pen? Pen, Rect Bounds)> Rectangles = [];
        internal readonly List<(Point Start, Point End)> Lines = [];
        internal readonly List<double> Opacities = [];

        internal void Clear()
        {
            Rectangles.Clear();
            Lines.Clear();
            Opacities.Clear();
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add((brush, pen, rectangle));
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add((point0, point1));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) => Opacities.Add(opacity);
        public override void Pop() { }
        public override void Close() { }
    }
}
