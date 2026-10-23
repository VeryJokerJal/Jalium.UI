#if JALIUM_MAC_NATIVE_GEOMETRY_TESTS
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTextBoxAlignmentTests : MacOSGeometryTestBase
{
    private const string Sample = "Miii中文🙂e\u0301 אבג xyz tail";
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Theory]
    [InlineData(0, false, false)] [InlineData(1, false, false)] [InlineData(2, false, false)]
    [InlineData(0, true, false)] [InlineData(1, true, false)] [InlineData(2, true, false)]
    [InlineData(0, false, true)] [InlineData(1, false, true)] [InlineData(2, false, true)]
    [InlineData(0, true, true)] [InlineData(1, true, true)] [InlineData(2, true, true)]
    public void CaretsSelectionHitAndImeUseThePaintedAlignment(int alignment, bool rtl, bool wrap)
    {
        var box = Box(wrap ? Sample + " second row 中文🙂 end" : Sample, wrap ? 128 : 420);
        box.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        box.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        box.TextAlignment = (TextAlignment)alignment;
        Arrange(box, wrap ? 128 : 420);
        var viewport = Viewport(box);
        using var expected = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, "Times", 20, 700, 1);
        expected.SetTextAlignment((TextAlignment)alignment); expected.SetNoWrap(!wrap);
        double layoutWidth = wrap ? viewport.Width : Math.Max(viewport.Width, expected.MeasureText(box.Text, 100000, 100000).WidthIncludingTrailingWhitespace);
        foreach (int offset in StringInfo.ParseCombiningCharacters(box.Text).Append(box.Text.Length))
        {
            foreach (bool trailing in new[] { false, true })
            {
                Assert.True(expected.HitTestTextPosition(box.Text, (float)layoutWidth, 100000, (uint)offset, trailing, out var edge));
                Rect actual = box.GetRectFromCharacterIndex(offset, trailing);
                Assert.InRange(Math.Abs(actual.X - Math.Round(viewport.X + edge.CaretX)), 0, .01);
                Assert.InRange(Math.Abs(actual.Y - Math.Round(viewport.Y + edge.CaretY)), 0, .01);
            }
        }
        box.Select(4, 4);
        using var drawing = new SelectionDrawing();
        DrawSelection(box, drawing, viewport);
        Assert.NotEmpty(drawing.Rectangles);
        Assert.True(expected.HitTestTextRange(box.Text, (float)layoutWidth, 100000, 4, 4, out var range));
        var selected = drawing.Rectangles.First(rect => Math.Abs(rect.Y - Math.Round(viewport.Y + range.Y)) <= 1);
        Assert.InRange(Math.Abs(selected.X - Math.Round(viewport.X + range.X)), 0, 1);
        Assert.InRange(Math.Abs(selected.Right - Math.Round(viewport.X + range.X + range.Width)), 0, 1);
        IImeSupport ime = box;
        Assert.True(ime.TryGetImeTextRangeGeometry(4, 4, false, out var candidate));
        Assert.InRange(Math.Abs(candidate.Rectangle.X - selected.X), 0, 1);
        var point = new Point(viewport.X + range.X + range.Width / 4, viewport.Y + range.Y + range.Height / 2);
        Assert.True(expected.HitTestPoint(box.Text, (float)layoutWidth, 100000, (float)(point.X - viewport.X), (float)(point.Y - viewport.Y), out var hit));
        int expectedIndex = hit.IsTrailingHit != 0 ? GraphemeClusters.NextBoundary(box.Text, (int)hit.TextPosition) : (int)hit.TextPosition;
        Assert.Equal(expectedIndex, box.GetCharacterIndexFromPoint(point, true));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DirectionRelativeStartAlignmentChangesEveryGeometryPath(bool wrap)
    {
        var box = Box("Miii中文🙂e\u0301", 310); box.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Css.SetStyle(box, "text-align: start;");
        Arrange(box, 310); Rect left = box.GetRectFromCharacterIndex(4);
        box.FlowDirection = FlowDirection.RightToLeft; Arrange(box, 310);
        Rect right = box.GetRectFromCharacterIndex(4);
        Assert.True(right.X > left.X + 100);
        IImeSupport ime = box;
        Assert.True(ime.TryGetImeTextRangeGeometry(4, 1, false, out var range));
        Assert.InRange(Math.Abs(range.Rectangle.X - right.X), 0, 1);
        Assert.Equal(4, box.GetCharacterIndexFromPoint(new Point(right.X + 1, right.Y + right.Height / 2), true));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void EmptyLineCaretAndImeHonorAlignment(int alignment)
    {
        var box = Box("", 310); box.TextAlignment = (TextAlignment)alignment; Arrange(box, 310);
        var viewport = Viewport(box); var caret = box.GetRectFromCharacterIndex(0);
        double expectedX = viewport.X + (alignment == 1 ? viewport.Width : alignment == 2 ? viewport.Width / 2 : 0);
        Assert.Equal(Math.Round(expectedX), caret.X);
        var candidate = ((IImeSupport)box).GetImeCaretRectangle();
        Assert.Equal(caret.X, candidate.X); Assert.Equal(caret.Y, candidate.Y); Assert.Equal(caret.Height, candidate.Height);
        Assert.Equal(0, box.GetCharacterIndexFromPoint(new Point(expectedX, caret.Y + 2), true));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void OverflowKeepsCaretVisibleAndHitTestingUsesHorizontalScroll(int alignment)
    {
        var box = Box(string.Concat(Enumerable.Repeat("Miii中文🙂e\u0301 ", 16)), 160);
        box.TextAlignment = (TextAlignment)alignment; Arrange(box, 160);
        box.CaretIndex = box.Text.Length;
        var viewport = Viewport(box); var caret = box.GetRectFromCharacterIndex(box.Text.Length);
        Assert.InRange(caret.X, viewport.X, viewport.Right);
        Assert.Equal(box.Text.Length, box.GetCharacterIndexFromPoint(new Point(caret.X, caret.Y + caret.Height / 2), true));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void BidiSelectionKeepsSeparateVisualFragments(int alignment)
    {
        var box = Box("abc אבג xyz", 310); box.TextAlignment = (TextAlignment)alignment; Arrange(box, 310);
        box.Select(1, 4); using var drawing = new SelectionDrawing();
        DrawSelection(box, drawing, Viewport(box));
        Assert.Equal(2, drawing.Rectangles.Count);
        Assert.True(drawing.Rectangles[0].Right < drawing.Rectangles[1].Left);
        var start = box.GetRectFromCharacterIndex(1);
        Assert.InRange(Math.Abs(drawing.Rectangles[0].Left - start.X), 0, 1);
    }

    private static void DrawSelection(TextBox box, DrawingContext drawing, Rect viewport)
    {
        var method = typeof(TextBox).GetMethod("DrawSelection", Private)!;
        object?[] arguments = method.GetParameters().Length == 4
            ? [drawing, viewport, Math.Round(LineHeight(box)), null]
            : [drawing, viewport, Math.Round(LineHeight(box))];
        method.Invoke(box, arguments);
    }

    private static TextBox Box(string text, double width) => new() { Text = text, FontFamily = new FontFamily("Times"),
        FontSize = 20, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic,
        Width = width, Height = 400, Padding = new Thickness(4), BorderThickness = new Thickness(2) };
    private static void Arrange(TextBox box, double width) { box.Measure(new Size(width, 400)); box.Arrange(new Rect(0, 0, width, 400)); }
    private static Rect Viewport(TextBox box) => (Rect)typeof(TextBox).GetMethod("GetTextViewportRect", Private)!.Invoke(box, null)!;
    private static double LineHeight(TextBox box) => (double)typeof(TextBox).GetMethod("GetLineHeight", Private)!.Invoke(box, null)!;
    private sealed class SelectionDrawing : DrawingContextAdapter
    {
        internal List<Rect> Rectangles { get; } = [];
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Rectangles.Add(rectangle);
        public override void DrawLine(Pen pen, Point from, Point to) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double rx, double ry) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double rx, double ry) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect bounds, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
#endif
