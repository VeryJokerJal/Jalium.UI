using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Runtime.InteropServices;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichParagraphTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData("alpha beta gamma delta epsilon zeta eta theta iota kappa")]
    [InlineData("中文换行测试中文换行测试中文换行测试中文换行测试")]
    [InlineData("one 👩‍👩‍👧‍👦 two e\u0301 three 😀 four five six seven")]
    [InlineData("אבגדה וזחטי כלמנס עפרצק שתאבג דהוזח טיכלמ נסעפצ")]
    public void LongRun_WrapsAndRetainsDocumentOffsets(string text)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var paragraph = new Paragraph(new Run(text) { FontSize = 20 });
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        document.Blocks.Add(paragraph);
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(150, 500)); box.Arrange(new Rect(0, 0, 150, 500));
        var lines = Lines(box);
        Assert.True(lines.Count > 1, "a long single Run must produce multiple visual rows");
        int end = 0;
        foreach (object line in lines)
        {
            int start = Property<int>(line, "StartOffset");
            Assert.Equal(end, start);
            end = Property<int>(line, "EndOffset");
            Assert.InRange(Property<double>(line, "Width"), 0, 139);
            Assert.Equal(end, GraphemeClusters.SnapNearest(text, end));
        }
        Assert.Equal(text.Length, end);
        object second = lines[1]!;
        int secondStart = Property<int>(second, "StartOffset");
        Assert.True(((IImeSupport)box).TrySetImeSelection(secondStart, 0));
        Rect caret = ((IImeSupport)box).GetImeCaretRectangle();
        Assert.True(caret.Y > 6);
        var pointer = box.GetPositionFromPoint(new Point(caret.X, caret.Y + caret.Height / 2), true);
        Assert.NotNull(pointer); Assert.Equal(secondStart, pointer.DocumentOffset);
    }

    [Theory]
    [InlineData("ab 👩‍👩‍👧‍👦 cd e\u0301 ef 😀 gh ij kl mn op qr")]
    [InlineData("中文测试中文测试中文测试中文测试中文测试")]
    [InlineData("abc אבגדה 123 xyz العربية fi fl end another row")]
    public void PreparedParagraph_CaretsHitTestsAndRangeBoundsUseWholeGraphemes(string text)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var paragraph = CreateNative(text);
        foreach (var row in paragraph.Lines)
        {
            int start = (int)row.Metrics.Line.TextPosition;
            int end = start + (int)row.Metrics.Line.Length;
            for (int i = start; i < end; i = GraphemeClusters.NextBoundary(text, i))
            {
                int next = GraphemeClusters.NextBoundary(text, i);
                var leading = paragraph.Caret(row.Index, i, false);
                var trailing = paragraph.Caret(row.Index, next, true);
                Assert.True(float.IsFinite(leading.X)); Assert.True(float.IsFinite(trailing.X));
                var selection = paragraph.Selection(row.Index, i, next - i);
                Assert.NotEmpty(selection);
                Assert.Equal(Math.Min(leading.X, trailing.X), selection.Min(r => r.X), 2);
                Assert.Equal(Math.Max(leading.X, trailing.X), selection.Max(r => r.X + r.Width), 2);
            }
            for (double x = -5; x < 145; x += 1.5)
            {
                var hit = paragraph.HitTest(row.Index, x);
                Assert.Contains((int)hit.TextPosition, GraphemeClusters.GetBoundaries(text));
                var caret = paragraph.Caret(row.Index, (int)hit.TextPosition, hit.BackwardAffinity != 0);
                Assert.Equal(hit.X, caret.X);
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LongRun_CommandAndUpDownRetainRowAffinity(bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        document.Blocks.Add(new Paragraph(new Run("alpha beta gamma delta epsilon zeta eta theta iota kappa") { FontSize = 20 }));
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(150, 500)); box.Arrange(new Rect(0, 0, 150, 500));
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            var lines = Lines(box);
            int start = Property<int>(lines[1]!, "StartOffset"), end = Property<int>(lines[1]!, "EndOffset");
            int initial = start + 1;
            Assert.True(((IImeSupport)box).TrySetImeSelection(initial, 0)); Assert.True(box.Focus());
            double rowY = ((IImeSupport)box).GetImeCaretRectangle().Y;
            Dispatch(window, 0x27, 8 | (shift ? 1 : 0));
            Assert.Equal(end, box.CaretPosition!.DocumentOffset);
            Assert.Equal(rowY, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Dispatch(window, 0x25, 8 | (shift ? 1 : 0));
            Assert.Equal(start, box.CaretPosition.DocumentOffset);
            Assert.True(((IImeSupport)box).TryGetImeSurroundingText(out var selection));
            Assert.Equal(shift ? initial : start, selection.AnchorIndex);
            Dispatch(window, 0x28, 0);
            Assert.True(((IImeSupport)box).GetImeCaretRectangle().Y > rowY);
            Dispatch(window, 0x26, 0);
            Assert.Equal(rowY, ((IImeSupport)box).GetImeCaretRectangle().Y);
            Assert.Equal(start, box.CaretPosition.DocumentOffset);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData("abc אבגדה 123 xyz العربية end", false)]
    [InlineData("abc אבגדה 123 xyz العربية end", true)]
    [InlineData("אבגדה וזחטי כלמנס", false)]
    [InlineData("אבגדה וזחטי כלמנס", true)]
    public void RichCommandArrow_UsesPhysicalParagraphEdges(string text, bool shift)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var doc = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        doc.Blocks.Add(new Paragraph(new Run(text) { FontSize = 20 }));
        var box = new RichTextBox(doc) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(600, 200)); box.Arrange(new Rect(0, 0, 600, 200));
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            object row = Lines(box)[0]!;
            var native = Property<NativeTextParagraph.Line>(row, "NativeLine");
            Assert.True(((IImeSupport)box).TrySetImeSelection(2, 0)); Assert.True(box.Focus());
            Dispatch(window, 0x25, 8 | (shift ? 1 : 0));
            Assert.Equal(native.Metrics.Line.X + 6, ((IImeSupport)box).GetImeCaretRectangle().X, 2);
            Dispatch(window, 0x27, 8 | (shift ? 1 : 0));
            Assert.Equal(native.Metrics.Line.X + native.Metrics.Line.Width + 6,
                ((IImeSupport)box).GetImeCaretRectangle().X, 2);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void StyledParagraph_DrawCommandsRetainLayoutAndReactToBrushAndWidthChanges()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var red = new SolidColorBrush(Colors.Red);
        var paragraph = new Paragraph();
        var first = new Run("first styled paragraph with several words ") { FontSize = 16, Foreground = red };
        var second = new Run("larger blue words 中文 👩‍👩‍👧‍👦 end") { FontSize = 32, Foreground = new SolidColorBrush(Colors.Blue), FontWeight = FontWeights.Bold };
        paragraph.Inlines.Add(first); paragraph.Inlines.Add(second);
        var document = new FlowDocument { FontSize = 16, FontFamily = "Helvetica" }; document.Blocks.Add(paragraph);
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(220, 800)); box.Arrange(new Rect(0, 0, 220, 800));
        var rows = Lines(box);
        Assert.True(rows.Count >= 4);
        var oldLine = Property<NativeTextParagraph.Line>(rows[0]!, "NativeLine");
        using var drawing = new ParagraphDrawingContext();
        typeof(RichTextBox).GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, [drawing, new Rect(6, 6, 208, 788)]);
        Assert.Equal(rows.Count, drawing.Text.Count);
        Assert.Equal(first.Text + second.Text, string.Concat(drawing.Text.Select(command => command.text.Text)));
        for (int i = 0; i < rows.Count; i++)
        {
            var command = drawing.Text[i];
            var line = Assert.IsType<NativeTextParagraph.Line>(command.text.PlatformTextLine);
            Assert.Equal((uint)i, line.Index); Assert.Same(oldLine.Paragraph, line.Paragraph);
            Assert.Equal(first.Text + second.Text, line.Paragraph.Text);
            if (i > 0) Assert.Equal(drawing.Text[i-1].origin.Y +
                Property<double>(rows[i-1]!, "Height"), command.origin.Y);
        }
        red.Color = Colors.Green;
        var refreshed = Property<NativeTextParagraph.Line>(Lines(box)[0]!, "NativeLine");
        Assert.NotSame(oldLine.Paragraph, refreshed.Paragraph); Assert.True(oldLine.Paragraph.IsDisposed);
        red.Opacity = 0.25;
        var faded = Property<NativeTextParagraph.Line>(Lines(box)[0]!, "NativeLine");
        Assert.True(refreshed.Paragraph.IsDisposed); Assert.NotSame(refreshed.Paragraph, faded.Paragraph);
        box.Measure(new Size(140, 800)); box.Arrange(new Rect(0, 0, 140, 800));
        Assert.True(Lines(box).Count > rows.Count);
        Assert.True(faded.Paragraph.IsDisposed);
    }

    [Fact]
    public void HardBreaksAndTerminalEmptyRow_KeepOffsetsAndInsertionGeometry()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var paragraph = new Paragraph(new Run("ab\r\ncd\n") { FontSize = 20 });
        paragraph.Inlines.Add(new LineBreak());
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" }; document.Blocks.Add(paragraph);
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(200, 400)); box.Arrange(new Rect(0, 0, 200, 400));
        var rows = Lines(box);
        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { 0, 4, 7, 8 }, rows.Cast<object>().Select(row => Property<int>(row, "StartOffset")));
        Assert.Equal(new[] { 2, 6, 7, 8 }, rows.Cast<object>().Select(row => Property<int>(row, "EndOffset")));
        Assert.True(((IImeSupport)box).TrySetImeSelection(8, 0));
        var insertion = ((IImeSupport)box).GetImeCaretRectangle();
        Assert.Equal(96, insertion.Y);
        Assert.Equal(1, insertion.Width);
    }

    [Fact]
    public void PreparedParagraph_OutlivesInputFontsAndContextAndRejectsUseAfterDispose()
    {
        var context = new RenderContext(RenderBackend.Metal);
        var paragraph = CreateNative("font lifetime test words words words");
        var retained = new FormattedText(paragraph.Text, "Helvetica", 20)
        {
            PlatformTextLine = paragraph.Lines[0],
        }.CreateRenderSnapshot(Brushes.Black);
        context.Dispose();
        Assert.True(paragraph.Caret(0, 0, false).X >= 0);
        paragraph.Dispose(); paragraph.Dispose();
        Assert.Throws<ObjectDisposedException>(() => paragraph.Caret(0, 0, false));
        Assert.False(paragraph.DrawLine(0, 0, 0, 0));
        GC.KeepAlive(retained);
    }

    [Fact]
    public void ParagraphAbi_HasStablePackedFieldOffsets()
    {
        Assert.Equal(32, Marshal.SizeOf<NativeMethods.ParagraphSpan>());
        Assert.Equal(8, Marshal.OffsetOf<NativeMethods.ParagraphSpan>("TextPosition").ToInt32());
        Assert.Equal(44, Marshal.SizeOf<NativeMethods.ParagraphLineMetrics>());
        Assert.Equal(40, Marshal.OffsetOf<NativeMethods.ParagraphLineMetrics>("Baseline").ToInt32());
        Assert.Equal(20, Marshal.SizeOf<NativeMethods.ParagraphFragment>());
        Assert.Equal(12, Marshal.SizeOf<NativeMethods.ParagraphCaret>());
    }

    [Theory]
    [InlineData(2, 6)] [InlineData(4, 2)]
    public void MixedDirectionSelection_PaintsOnlySelectedGlyphIntervals(int start, int length)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        document.Blocks.Add(new Paragraph(new Run("abc אבגדה 123 xyz") { FontSize = 20 }));
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(400, 200)); box.Arrange(new Rect(0, 0, 400, 200));
        var row = Property<NativeTextParagraph.Line>(Lines(box)[0]!, "NativeLine");
        var expected = row.Paragraph.Selection(0, start, length);
        Assert.True(((IImeSupport)box).TrySetImeSelection(start, length));
        using var drawing = new ParagraphDrawingContext();
        typeof(RichTextBox).GetMethod("RenderSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, [drawing, new Rect(6, 6, 388, 188)]);
        Assert.Equal(expected.Length, drawing.Rectangles.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(new Rect(expected[i].X + 6, 6, expected[i].Width, row.Metrics.Line.Height), drawing.Rectangles[i]);
        Assert.True(((IImeSupport)box).TryGetImeTextRangeGeometry(start, length, false, out var geometry));
        Assert.Equal(expected.Min(r => r.X) + 6, geometry.Rectangle.X);
        Assert.Equal(expected.Max(r => r.X + r.Width) - expected.Min(r => r.X), geometry.Rectangle.Width);
        Assert.Equal(length, geometry.Length);
    }

    [Fact]
    public void MixedFontCaret_UsesTallRowHeightForDrawingAndIme()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var document = new FlowDocument { FontSize = 16, FontFamily = "Helvetica" };
        document.Blocks.Add(new Paragraph(new Run("large text 中文 words") { FontSize = 44 }));
        var box = new RichTextBox(document) { Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(200, 400)); box.Arrange(new Rect(0, 0, 200, 400));
        var row = Property<NativeTextParagraph.Line>(Lines(box)[0]!, "NativeLine");
        Assert.True(row.Metrics.Line.Height > document.FontSize * 1.5);
        Assert.True(((IImeSupport)box).TrySetImeSelection(1, 0));
        var geometry = ((IImeSupport)box).GetImeCaretRectangle();
        Assert.Equal(row.Metrics.Line.Height, geometry.Height);
        using var drawing = new ParagraphDrawingContext();
        typeof(RichTextBox).GetMethod("RenderCaret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, [drawing, new Rect(6, 6, 188, 388)]);
        var caret = Assert.Single(drawing.Rectangles);
        Assert.Equal(geometry.X, caret.X); Assert.Equal(geometry.Y, caret.Y); Assert.Equal(geometry.Height, caret.Height);
    }

    [Fact]
    public void LongParagraph_PreparesAllRowsWithoutRepeatedPrefixShaping()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var paragraph = CreateNative(string.Concat(Enumerable.Repeat("中文😀 e\u0301 words ", 1024)));
        Assert.True(paragraph.Lines.Length > 500);
        Assert.Equal(paragraph.Text.Length, (int)(paragraph.Lines[^1].Metrics.Line.TextPosition +
            paragraph.Lines[^1].Metrics.Line.Length));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    private static NativeTextParagraph CreateNative(string text) =>
        NativeTextParagraph.TryCreate([new NativeTextParagraph.Span(text, "Helvetica", 20, 400, 0, Colors.Black)],
            "Helvetica", 20, 138, 30, TextAlignment.Left, FlowDirection.LeftToRight)!;

    private static void Dispatch(Window window, int key, int modifiers)
    {
        var packet = new Jalium.UI.Controls.Platform.PlatformEvent
        { Type = Jalium.UI.Controls.Platform.PlatformEventType.KeyDown, KeyCode = key, Modifiers = modifiers };
        typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [packet]);
    }

    private sealed class ParagraphDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText text, Point origin)> Text { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add((text, origin));
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => Rectangles.Add(rect);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double rx, double ry) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double rx, double ry) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect bounds, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }

    internal static IList Lines(RichTextBox box)
    {
        typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, [box.RenderSize.Width - 12]);
        object layout = typeof(RichTextBox).GetField("_layoutCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;
        object block = ((IList)layout.GetType().GetProperty("Blocks")!.GetValue(layout)!)[0]!;
        return (IList)block.GetType().GetProperty("Lines")!.GetValue(block)!;
    }

    internal static T Property<T>(object item, string name) => (T)item.GetType().GetProperty(name)!.GetValue(item)!;
}
