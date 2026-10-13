using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

// Real CoreText layout, no AppKit windows or foreground input. Opt in together
// with the geometry integration checks using JaliumMacNativeGeometryTests.
[Collection("macOS Window globals")]
public sealed class MacOSWindowVisualLineTests : MacOSGeometryTestBase
{
    private static readonly MethodInfo PlatformEventMethod = typeof(Window).GetMethod(
        "OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> TextBoxCases()
    {
        foreach (string text in new[]
        {
            "alpha beta gamma delta epsilon zeta eta theta iota kappa",
            "中文换行测试中文换行测试中文换行测试中文换行测试",
            "one 👩‍👩‍👧‍👦 two e\u0301 three 😀 four five six seven",
        })
        foreach (bool shift in new[] { false, true })
        foreach (bool right in new[] { false, true })
            yield return [text, shift, right];
    }

    [Theory]
    [MemberData(nameof(TextBoxCases))]
    public void CommandArrow_UsesCurrentShapedRowAndKeepsItsCaret(
        string text, bool shift, bool right)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = CreateTextBox(text, 150);
        var window = new DisplayedTestWindow { Content = box, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            var (start, end) = SecondRow(box);
            int caret = GraphemeClusters.NextBoundary(text, start);
            Assert.InRange(caret, start + 1, end - 1);
            box.Select(caret, 0);
            Assert.True(box.Focus());
            Rect before = ((IImeSupport)box).GetImeCaretRectangle();
            Dispatch(window, right ? 0x27 : 0x25, 8 | (shift ? 1 : 0));
            Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var selection));
            Assert.Equal(right ? end : start, selection.CursorIndex);
            Assert.Equal(shift ? caret : selection.CursorIndex, selection.AnchorIndex);
            Rect after = ((IImeSupport)box).GetImeCaretRectangle();
            Assert.Equal(before.Y, after.Y);
            Assert.Equal(0, box.HorizontalOffset);
            Assert.True(((IImeSupport)box).TryGetImeTextRangeGeometry(box.CaretIndex, 0, false, out var insertion));
            Assert.Equal(after, insertion.Rectangle);

            // Repeated boundary gestures must stay on the same visible row,
            // even when the end offset is also the following row's start.
            Dispatch(window, right ? 0x27 : 0x25, 8 | (shift ? 1 : 0));
            Assert.Equal(right ? end : start, box.CaretIndex);
            Assert.Equal(before.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, right ? 0x25 : 0x27, 8);
            Assert.Equal(right ? start : end, box.CaretIndex);
            Assert.Equal(before.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void SoftRowEnd_AffinitySurvivesModifiersButExplicitSelectionResetsIt()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        const string text = "alpha beta gamma delta epsilon zeta eta theta iota kappa";
        var box = CreateTextBox(text, 150);
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            var (start, end) = SecondRow(box);
            box.Select(start + 1, 0); Assert.True(box.Focus());
            double rowY = ((IImeSupport)box).GetImeCaretRectangle().Y;
            Dispatch(window, 0x27, 8);
            Assert.Equal(end, box.CaretIndex);
            Assert.Equal(rowY, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, 0x5b, 8); // Physical Command press does not move the caret.
            Assert.Equal(rowY, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, 0x25, 8);
            Assert.Equal(start, box.CaretIndex);
            Dispatch(window, 0x27, 8);
            box.CaretIndex = end; // Explicit index assignment chooses the leading caret.
            Assert.True(((IImeSupport)box).GetImeCaretRectangle().Y > rowY);
            box.Select(end, 0);
            Assert.True(((IImeSupport)box).GetImeCaretRectangle().Y > rowY);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RichText_CommandArrowUsesRenderedRunRowsAndPointerDirection(bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("first row ") { FontSize = 20 });
        paragraph.Inlines.Add(new Run("second row ") { FontSize = 20 });
        paragraph.Inlines.Add(new Run("third row ") { FontSize = 20 });
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        document.Blocks.Add(paragraph);
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(150, 300)); box.Arrange(new Rect(0, 0, 150, 300));
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            const int start = 10, end = 21, initial = 12;
            Assert.True(((IImeSupport)box).TrySetImeSelection(initial, 0));
            Assert.True(box.Focus());
            Rect row = ((IImeSupport)box).GetImeCaretRectangle();
            Assert.True(((IImeSupport)box).TryGetImeTextRangeGeometry(0, 1, false, out var first));
            Assert.True(row.Y > first.Rectangle.Y, "fixture must actually render multiple rows");
            Dispatch(window, 0x27, 8 | (shift ? 1 : 0));
            Assert.Equal(end, box.CaretPosition!.DocumentOffset);
            Assert.Equal(row.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, 0x25, 8 | (shift ? 1 : 0));
            Assert.Equal(start, box.CaretPosition!.DocumentOffset);
            Assert.Equal(row.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var selection));
            Assert.Equal(shift ? initial : start, selection.AnchorIndex);
            box.CaretPosition = document.GetPositionAtOffset(end, LogicalDirection.Forward);
            Assert.True(((IImeSupport)box).GetImeCaretRectangle().Y > row.Y);
            Dispatch(window, 0x26, 0);
            Assert.Equal(start, box.CaretPosition!.DocumentOffset);
            Assert.Equal(row.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, 0x28, 0);
            Assert.Equal(end, box.CaretPosition!.DocumentOffset);
            var hit = box.GetPositionFromPoint(new Point(140, row.Y + row.Height / 2), true);
            Assert.NotNull(hit); Assert.Equal(end, hit.DocumentOffset);
            box.CaretPosition = hit;
            Assert.Equal(row.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData("אבגדה וזחטי כלמנס", false)]
    [InlineData("אבגדה וזחטי כלמנס", true)]
    [InlineData("abc אבגדה 123 xyz", false)]
    [InlineData("abc אבגדה 123 xyz", true)]
    public void CommandArrow_UsesPhysicalEdgesInBidiText(string text, bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = CreateTextBox(text, 600);
        box.TextWrapping = TextWrapping.NoWrap;
        box.Measure(new Size(600, 300)); box.Arrange(new Rect(0, 0, 600, 300));
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            var edges = new List<double>();
            for (int i = 0; i < text.Length; i = GraphemeClusters.NextBoundary(text, i))
            {
                edges.Add(box.GetRectFromCharacterIndex(i).X);
                edges.Add(box.GetRectFromCharacterIndex(i, true).X);
            }
            box.Select(2, 0); Assert.True(box.Focus());
            Dispatch(window, 0x25, 8 | (shift ? 1 : 0));
            Assert.Equal(edges.Min(), ((IImeSupport)box).GetImeCaretRectangle().X);
            Dispatch(window, 0x27, 8 | (shift ? 1 : 0));
            Assert.Equal(edges.Max(), ((IImeSupport)box).GetImeCaretRectangle().X);
            if (shift)
            {
                Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var selection));
                Assert.Equal(2, selection.AnchorIndex);
            }
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void ClickingSoftRowTrailingEdge_KeepsTheClickedRow()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = CreateTextBox("alpha beta gamma delta epsilon zeta eta theta iota kappa", 150);
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            var (start, end) = SecondRow(box);
            Rect edge = box.GetRectFromCharacterIndex(GraphemeClusters.PreviousBoundary(box.Text, end), true);
            Point point = new(edge.X - .05, edge.Y + edge.Height / 2);
            box.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseDownEvent, point, MouseButton.Left,
                MouseButtonState.Pressed, 1, MouseButtonState.Pressed, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
                ModifierKeys.None, Environment.TickCount));
            Assert.Equal(end, box.CaretIndex);
            Assert.Equal(edge.Y, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, 0x25, 8);
            Assert.Equal(start, box.CaretIndex);
        }
        finally { Mouse.Capture(null); Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void NarrowRtlField_ScrollsToThePhysicalCaretRatherThanItsLogicalIndex()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = CreateTextBox("אבגדה וזחטי כלמנס עפקצ רשת אבגדה וזחטי כלמנס עפקצ רשת", 150);
        box.TextWrapping = TextWrapping.NoWrap;
        box.Measure(new Size(150, 300)); box.Arrange(new Rect(0, 0, 150, 300));
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            box.Select(2, 0); Assert.True(box.Focus());
            Dispatch(window, 0x25, 8);
            Assert.Equal(0, box.HorizontalOffset);
            Assert.InRange(((IImeSupport)box).GetImeCaretRectangle().X, 6, 144);
            Dispatch(window, 0x27, 8);
            Assert.True(box.HorizontalOffset > 0);
            Assert.InRange(((IImeSupport)box).GetImeCaretRectangle().X, 6, 144);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void SoftRowEnd_CancelledCompositionRestoresItsCaretAndCommittedTextResetsIt()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = CreateTextBox("alpha beta gamma delta epsilon zeta eta theta iota kappa", 150);
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            var (start, end) = SecondRow(box);
            box.Select(start + 1, 0); Assert.True(box.Focus());
            Dispatch(window, 0x27, 8);
            IImeSupport support = box;
            Rect original = support.GetImeCaretRectangle();
            support.OnImeCompositionStart(); support.OnImeCompositionUpdate("中文", 2);
            support.OnImeCompositionEnd(null);
            Assert.Equal(original, support.GetImeCaretRectangle());
            support.OnImeCompositionStart(); support.OnImeCompositionUpdate("中文", 2);
            support.OnImeCompositionEnd("中文");
            // The platform delivers committed text separately from pre-edit end.
            box.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "中文", Environment.TickCount));
            Assert.Equal(end + 2, box.CaretIndex);
            Assert.Contains("中文", box.Text);
            Assert.Equal(box.GetRectFromCharacterIndex(box.CaretIndex).Y, support.GetImeCaretRectangle().Y);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void RichText_ResizeReflowsBeforeResolvingTheVisualBoundary()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var paragraph = new Paragraph();
        foreach (string text in new[] { "first row ", "second row ", "third row " })
            paragraph.Inlines.Add(new Run(text) { FontSize = 20 });
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        document.Blocks.Add(paragraph);
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(150, 300)); box.Arrange(new Rect(0, 0, 150, 300));
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Assert.True(((IImeSupport)box).TrySetImeSelection(12, 0)); Assert.True(box.Focus());
            double narrowY = ((IImeSupport)box).GetImeCaretRectangle().Y;
            box.Measure(new Size(600, 300)); box.Arrange(new Rect(0, 0, 600, 300));
            Assert.True(((IImeSupport)box).GetImeCaretRectangle().Y < narrowY);
            Dispatch(window, 0x27, 8);
            Assert.Equal(31, box.CaretPosition!.DocumentOffset);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DrawnCaretAndImeRectangle_AgreeAtTheSoftRowEnd(bool readOnly)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var box = CreateTextBox("alpha beta gamma delta epsilon zeta eta theta iota kappa", 150);
        box.IsReadOnly = readOnly;
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            var (start, _) = SecondRow(box);
            box.Select(start + 1, 0); Assert.True(box.Focus()); Dispatch(window, 0x27, 8);
            Rect caret = ((IImeSupport)box).GetImeCaretRectangle();
            using var drawing = new CaretDrawingContext();
            typeof(TextBox).GetMethod("DrawCaret", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(box, [drawing, new Rect(6, 6, 138, 288), 22d]);
            var line = Assert.Single(drawing.Lines);
            Assert.Equal(caret.X, line.from.X); Assert.Equal(caret.Y, line.from.Y);
            Assert.Equal(caret.Y + caret.Height, Math.Round(line.to.Y));
            if (readOnly) Assert.False(((IImeSupport)box).IsImeAllowed);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private sealed class CaretDrawingContext : DrawingContextAdapter
    {
        public List<(Point from, Point to)> Lines = [];
        public override void DrawLine(Pen pen, Point point0, Point point1) => Lines.Add((point0, point1));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect bounds, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private static TextBox CreateTextBox(string text, double width)
    {
        var box = new TextBox { Text = text, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Helvetica"), FontSize = 18,
            Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(width, 300)); box.Arrange(new Rect(0, 0, width, 300));
        return box;
    }

    private static (int start, int end) SecondRow(TextBox box)
    {
        IImeSupport support = box;
        Assert.True(support.TryGetImeTextRangeGeometry(0, box.Text.Length, false, out var first));
        Assert.True(support.TryGetImeTextRangeGeometry(first.Length, box.Text.Length - first.Length, false, out var second));
        Assert.True(second.Rectangle.Y > first.Rectangle.Y);
        Assert.InRange(second.Start + second.Length, first.Length + 2, box.Text.Length - 1);
        return (second.Start, second.Start + second.Length);
    }

    private static void Dispatch(Window window, int key, int modifiers)
    {
        PlatformEventMethod.Invoke(window, [new PlatformEvent
        {
            Type = PlatformEventType.KeyDown, KeyCode = key, Modifiers = modifiers
        }]);
    }
}

[Collection("macOS Window globals")]
public sealed class MacOSVisualLineMetricsTests : MacOSGeometryTestBase
{
    [Fact]
    public void VisualLineContract_ExcludesHardSeparatorsAndSupportsEmptyTerminalRows()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.Equal(40, System.Runtime.InteropServices.Marshal.SizeOf<TextLineMetrics>());
        const string text = "ab\r\ncd\n";
        Assert.True(Query(text, 0, false, out var first));
        Assert.Equal(0u, first.TextPosition); Assert.Equal(2u, first.Length);
        Assert.True(Query(text, 4, true, out var second));
        Assert.Equal(4u, second.TextPosition); Assert.Equal(2u, second.Length);
        Assert.True(second.Y > first.Y);
        Assert.True(Query(text, 7, true, out var empty));
        Assert.Equal(7u, empty.TextPosition); Assert.Equal(0u, empty.Length);
        Assert.True(empty.Y > second.Y); Assert.True(empty.Height > 0);
        Assert.True(Query("", 0, false, out var initial)); Assert.True(initial.Height > 0);
        Assert.False(Query("a", 2, false, out _));
        Assert.False(TextMeasurement.TryGetVisualLineMetrics("a", "Helvetica", 18, 400, 0, float.NaN, 0, false, out _));
    }

    [Theory]
    [InlineData("alpha beta gamma delta epsilon zeta eta theta")]
    [InlineData("中文测试中文测试中文测试中文测试中文测试")]
    [InlineData("👩‍👩‍👧‍👦 e\u0301 👩‍👩‍👧‍👦 e\u0301 👩‍👩‍👧‍👦 e\u0301 👩‍👩‍👧‍👦 e\u0301 👩‍👩‍👧‍👦 e\u0301 👩‍👩‍👧‍👦")]
    public void SharedWrapIndex_SelectsEitherRowWithoutSplittingGraphemes(string text)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.True(Query(text, 0, false, out var first));
        uint next = first.TextPosition + first.Length;
        Assert.InRange(next, 1u, (uint)text.Length - 1);
        Assert.True(Query(text, next, true, out var previous));
        Assert.Equal(first.TextPosition, previous.TextPosition); Assert.Equal(first.Y, previous.Y);
        Assert.True(Query(text, next, false, out var following));
        Assert.Equal(next, following.TextPosition); Assert.True(following.Y > first.Y);
        var boundaries = GraphemeClusters.GetBoundaries(text);
        Assert.Contains((int)next, boundaries);
        Assert.Contains((int)first.LeftCaretPosition, boundaries);
        Assert.Contains((int)first.RightCaretPosition, boundaries);
    }

    [Fact]
    public void LongParagraphQuery_ShapesOnceAndReturnsOneRow()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        string text = string.Concat(Enumerable.Repeat("中文😀 e\u0301 ", 1024));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(Query(text, (uint)(text.Length / 2), false, out var row));
        Assert.InRange(row.Length, 1u, 30u);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    private static bool Query(string text, uint position, bool backward, out TextLineMetrics result) =>
        TextMeasurement.TryGetVisualLineMetrics(text, "Helvetica", 18, 400, 0, 138, position, backward, out result);
}
