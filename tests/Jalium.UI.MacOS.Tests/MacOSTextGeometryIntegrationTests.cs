using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

// Opt in with -p:JaliumMacNativeGeometryTests=true and make the native payload
// available through DYLD_LIBRARY_PATH. These checks use CoreText through Metal,
// but do not create a native window or need an unlocked desktop session.
[Collection("macOS Window globals")]
public sealed class MacOSTextGeometryIntegrationTests : MacOSGeometryTestBase
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public MacOSTextGeometryIntegrationTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("Menlo", 12, "MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM")]
    [InlineData("Menlo", 15, "中文 🚀 👨‍👩‍👧 é tail   ")]
    [InlineData("Helvetica", 13, "AV fi office 中文 👩‍💻")]
    [InlineData("__UnavailableFont__, Menlo, PingFang SC", 18, "hello 中文 🇨🇳")]
    public void CoreText_ImmutableLinePreservesFractionalAdvancesAndAllGraphemeCarets(string family, double size, string text)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var line = TextLineLayout.TryCreate(text, family, size);
        Assert.NotNull(line); Assert.True(line.IsCurrent);
        var measured = new FormattedText(text, family, size);
        Assert.True(TextMeasurement.MeasureText(measured));
        Assert.InRange(Math.Abs(line.Width - measured.WidthIncludingTrailingWhitespace), 0, .001);
        foreach (int offset in System.Globalization.StringInfo.ParseCombiningCharacters(text).Append(text.Length))
        {
            Assert.True(TextMeasurement.HitTestTextPositionWrapped(text, family, size, 400, 0,
                float.PositiveInfinity, (uint)offset, false, out var caret));
            Assert.InRange(Math.Abs(line.GetCaretX(offset) - caret.CaretX), 0, .001);
            Assert.Equal(offset, line.HitTest(line.GetCaretX(offset)));
        }
    }

    [Fact]
    public void CoreText_ImmutableLineLifetimeAndInvalidInputsAreExplicit()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal, forceReplace: true);
        using var empty = TextLineLayout.TryCreate("", "Menlo", 15);
        Assert.NotNull(empty); Assert.Equal(0, empty.Width); Assert.Equal(0, empty.GetCaretX(0));
        Assert.Equal(0, empty.HitTest(100));
        Assert.Null(TextLineLayout.TryCreate("a\nb", "Menlo", 15));
        Assert.Null(TextLineLayout.TryCreate("text", "Menlo", double.NaN));
        Assert.Null(TextLineLayout.TryCreate("text", "Menlo", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.GetCaretX(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.HitTest(double.NaN));
        using var line = TextLineLayout.TryCreate("a👨‍👩‍👧éz", "Menlo", 15);
        Assert.NotNull(line); Assert.Equal(line.GetCaretX(1), line.GetCaretX(3));
        context.Dispose(); Assert.False(line.IsCurrent);
        line.Dispose(); Assert.False(line.IsCurrent);
        Assert.Throws<ObjectDisposedException>(() => line.GetCaretX(1));
    }

    [Theory]
    [InlineData("a😀b", 2, 1, 1, 2)]
    [InlineData("ae\u0301b", 2, 1, 1, 2)]
    [InlineData("a👨‍👩‍👧b", 3, 1, 1, 8)]
    [InlineData("aאבגb", 1, 1, 1, 1)]
    public void CoreText_OneLayoutRangePreservesUnicodeAndBidiEdges(string text, int start, int length, int actualStart, int actualLength)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, 400,
            (uint)start, (uint)length, out var range));
        Assert.Equal((uint)actualStart, range.TextPosition); Assert.Equal((uint)actualLength, range.Length);
        Assert.True(range.Width > 2);
        Assert.True(TextMeasurement.HitTestTextPositionWrapped(text, "Helvetica", 18, 400, 0, 400,
            (uint)actualStart, false, out var leading));
        Assert.True(TextMeasurement.HitTestTextPositionWrapped(text, "Helvetica", 18, 400, 0, 400,
            (uint)actualStart, true, out var trailing));
        Assert.Equal(Math.Min(leading.CaretX, trailing.CaretX), range.X, 2);
        Assert.Equal(Math.Abs(leading.CaretX - trailing.CaretX), range.Width, 2);
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, 400,
            (uint)actualStart, 0, out var insertion));
        Assert.Equal(leading.CaretX, insertion.X, 2); Assert.Equal(0, insertion.Width);
    }

    [Fact]
    public void CoreText_RangeReportsFirstRowCrLfAndRejectsInvalidInputs()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        const string text = "ab\r\ncd";
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, 400, 0, 6, out var first));
        Assert.Equal(4u, first.Length);
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, 400, 4, 2, out var second));
        Assert.True(second.Y > first.Y);
        Assert.False(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, float.NaN, 0, 1, out _));
        Assert.False(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, 400, 5, uint.MaxValue, out _));
        Assert.True(TextMeasurement.HitTestTextRangeWrapped(text, "Helvetica", 18, 400, 0, 400, 6, 0, out var end));
        Assert.Equal(6u, end.TextPosition); Assert.Equal(0u, end.Length);
    }

    [Fact]
    public void CoreText_WrappedCompositionDecoratesEveryShapedRow()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var box = new TextBox { Text = "abc", FontFamily = new FontFamily("Helvetica"), FontSize = 18,
            TextWrapping = TextWrapping.Wrap, Padding = new Thickness(4), BorderThickness = new Thickness(2) };
        box.Measure(new Size(105, 240)); box.Arrange(new Rect(0, 0, 105, 240));
        IImeSupport support = box;
        support.TrySetImeSelection(1, 0); support.OnImeCompositionStart();
        const string marked = "拼音输入中文测试拼音输入中文测试";
        support.OnImeCompositionUpdate(marked, marked.Length);
        using var drawing = new CompositionDrawingContext();
        typeof(TextBox).GetMethod("DrawImeComposition", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.Invoke(box, [drawing, new Rect(6, 6, 93, 228), 22d]);
        Assert.True(drawing.Backgrounds.Count > 2);
        int offset = 0;
        foreach (Rect row in drawing.Backgrounds)
        {
            Assert.True(support.TryGetImeTextRangeGeometry(offset, marked.Length - offset, true, out var geometry));
            Assert.Equal(geometry.Rectangle, row);
            Assert.Contains(drawing.Lines, line => Math.Abs(line.from.Y - (row.Bottom - 2)) < .01 &&
                Math.Abs(line.to.Y - (row.Bottom - 2)) < .01 && Math.Abs(line.from.X - row.Left) < .01);
            offset += geometry.Length;
        }
        Assert.Equal(marked.Length, offset);
        Assert.True(support.GetImeCaretRectangle().Y >= drawing.Backgrounds[^1].Y);
        support.OnImeCompositionEnd(null);
    }

    private sealed class CompositionDrawingContext : DrawingContextAdapter
    {
        public List<Rect> Backgrounds = [];
        public List<(Point from, Point to)> Lines = [];
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Backgrounds.Add(rectangle);
        public override void DrawLine(Pen pen, Point point0, Point point1) => Lines.Add((point0, point1));
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

    [Fact]
    public void CoreText_LongDocumentRangeCompletesWithoutRepeatedParagraphLayout()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var box = new TextBox { Text = new string('a', 8192), FontFamily = new FontFamily("Helvetica"), FontSize = 18 };
        box.Measure(new Size(400, 100)); box.Arrange(new Rect(0, 0, 400, 100));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(((IImeSupport)box).TryGetImeTextRangeGeometry(0, box.Text.Length, false, out var range));
        watch.Stop();
        _output.WriteLine($"8192-character range: {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(box.Text.Length, range.Length);
        Assert.True(range.Rectangle.Width > 50000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Range query took {watch.Elapsed}; it must not reshape the paragraph per grapheme.");
    }

    [Theory]
    [InlineData("a😀b", 1)]
    [InlineData("ae\u0301b", 1)]
    [InlineData("a👨‍👩‍👧b", 1)]
    [InlineData("אבג", 0)]
    public void CoreText_LeadingAndTrailingEdgesEncloseAWholeGrapheme(string text, int index)
    {
        Assert.True(OperatingSystem.IsMacOS());
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.Equal(RenderBackend.Metal, context.Backend);
        var box = new TextBox { Text = text, FontFamily = new FontFamily("Helvetica"), FontSize = 18 };
        box.Measure(new Size(400, 120)); box.Arrange(new Rect(0, 0, 400, 120));
        IImeSupport support = box;
        Assert.True(support.TryGetImeTextRangeGeometry(index, 1, false, out var range));
        Assert.Equal(GraphemeClusters.NextBoundary(text, index) - index, range.Length);
        Assert.True(range.Rectangle.Width > 2);
        Rect leading = box.GetRectFromCharacterIndex(index);
        Rect trailing = box.GetRectFromCharacterIndex(index, true);
        Assert.True(Math.Abs(trailing.X - leading.X) > 2);
        Assert.Equal(leading.Y, trailing.Y);
    }

    [Fact]
    public void CoreText_WrappedRangeAndCompositionUseTheVisibleRow()
    {
        Assert.True(OperatingSystem.IsMacOS());
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.Equal(RenderBackend.Metal, context.Backend);
        var box = new TextBox
        {
            Text = "abcdefgh ijklmnop qrstuvwx yzabcdef ghijklmn",
            FontFamily = new FontFamily("Helvetica"), FontSize = 18,
            TextWrapping = TextWrapping.Wrap, Padding = new Thickness(4), BorderThickness = new Thickness(2)
        };
        box.Measure(new Size(105, 250)); box.Arrange(new Rect(0, 0, 105, 250));
        IImeSupport support = box;
        Assert.True(support.TryGetImeTextRangeGeometry(0, box.Text.Length, false, out var first));
        Assert.InRange(first.Length, 1, box.Text.Length - 1);
        Assert.True(support.TryGetImeTextRangeGeometry(first.Length, 1, false, out var second));
        Assert.True(second.Rectangle.Y > first.Rectangle.Y);
        int anchorIndex = first.Length + 1;
        Assert.True(support.TrySetImeSelection(anchorIndex, 0));
        Rect anchor = box.GetRectFromCharacterIndex(anchorIndex);
        support.OnImeCompositionStart(); support.OnImeCompositionUpdate("拼音", 1);
        Assert.True(support.TryGetImeTextRangeGeometry(0, 1, true, out var mark));
        Assert.Equal(anchor.X, mark.Rectangle.X, 2);
        Assert.Equal(anchor.Y, mark.Rectangle.Y, 2);
        Rect caret = support.GetImeCaretRectangle();
        Assert.True(caret.Y >= anchor.Y);
        Assert.True(support.TryGetImeCharacterIndex(new Point(mark.Rectangle.X + mark.Rectangle.Width / 4,
            mark.Rectangle.Y + mark.Rectangle.Height / 2), true, out int hit));
        Assert.InRange(hit, 0, 1);
        support.OnImeCompositionEnd(null);
    }

    [Fact]
    public void CoreText_VerticalAlignmentAndScrollOffsetsMatchTheCandidateRectangle()
    {
        Assert.True(OperatingSystem.IsMacOS());
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.Equal(RenderBackend.Metal, context.Backend);
        var box = new TextBox { Text = "abc", FontSize = 18, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8), BorderThickness = new Thickness(2) };
        box.Measure(new Size(240, 140)); box.Arrange(new Rect(0, 0, 240, 140));
        box.CaretIndex = 1;
        Rect actual = box.GetRectFromCharacterIndex(1);
        Assert.True(actual.Y > 30);
        Rect candidate = ((IImeSupport)box).GetImeCaretRectangle();
        Assert.Equal(actual.X, candidate.X); Assert.Equal(actual.Y, candidate.Y);
        Assert.Equal(actual.Height, candidate.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CoreText_FiveEditorsSupplyDocumentAndProvisionalGeometry(int kind)
    {
        Assert.True(OperatingSystem.IsMacOS());
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.Equal(RenderBackend.Metal, context.Backend);
        Control control;
        switch (kind)
        {
            case 0: control = new TextBox { Text = "ab😀cd" }; break;
            case 1:
                var rich = new RichTextBox(); rich.SetPlainText("ab😀cd"); control = rich; break;
            case 2:
                var editor = new EditControl(); editor.LoadText("ab😀cd"); control = editor; break;
            case 3: control = new AutoCompleteBox { Text = "ab😀cd" }; break;
            default: control = new NumberBox { Text = "123456" }; break;
        }
        control.FontFamily = new FontFamily("Helvetica"); control.FontSize = 18;
        control.Measure(new Size(400, 160)); control.Arrange(new Rect(0, 0, 400, 160));
        IImeSupport support = (IImeSupport)control;
        Assert.True(support.TryGetImeTextRangeGeometry(3, 1, false, out var document));
        Assert.True(document.Rectangle.Width > 2);
        Assert.Equal(kind == 4 ? 1 : 2, document.Length);
        Assert.True(support.TryGetImeCharacterIndex(new Point(document.Rectangle.X + document.Rectangle.Width / 4,
            document.Rectangle.Y + document.Rectangle.Height / 2), false, out int offset));
        Assert.InRange(offset, document.Start, document.Start + document.Length);
        Assert.True(support.TrySetImeSelection(2, 2));
        support.OnImeCompositionStart(); support.OnImeCompositionUpdate("pin", 2);
        Assert.True(support.TryGetImeTextRangeGeometry(1, 1, true, out var marked));
        Assert.True(marked.Rectangle.Width > 2);
        Assert.True(support.TryGetImeTextRangeGeometry(2, 0, true, out var insertion));
        Rect caret = support.GetImeCaretRectangle();
        Assert.Equal(insertion.Rectangle.X, caret.X, 3);
        Assert.Equal(insertion.Rectangle.Y, caret.Y, 3);
        Assert.True(support.TryGetImeCharacterIndex(new Point(marked.Rectangle.X + marked.Rectangle.Width / 4,
            marked.Rectangle.Y + marked.Rectangle.Height / 2), true, out int markedOffset));
        Assert.InRange(markedOffset, 1, 2);
        support.OnImeCompositionEnd(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreText_RtlQueriesUseTheRenderedGlyphPositions(bool richText)
    {
        Assert.True(OperatingSystem.IsMacOS());
        using var context = new RenderContext(RenderBackend.Metal);
        Assert.Equal(RenderBackend.Metal, context.Backend);
        Control control;
        if (richText)
        {
            var rich = new RichTextBox(); rich.SetPlainText("אבג"); control = rich;
        }
        else control = new TextBox { Text = "אבג", FontFamily = new FontFamily("Helvetica"), FontSize = 18 };
        control.Measure(new Size(400, 160)); control.Arrange(new Rect(0, 0, 400, 160));
        IImeSupport support = (IImeSupport)control;
        Assert.True(support.TryGetImeTextRangeGeometry(0, 1, false, out var first));
        Assert.True(support.TryGetImeTextRangeGeometry(2, 1, false, out var last));
        Assert.True(first.Rectangle.X > last.Rectangle.X);
        Assert.True(support.TryGetImeCharacterIndex(new Point(first.Rectangle.X + first.Rectangle.Width / 2,
            first.Rectangle.Y + first.Rectangle.Height / 2), false, out int offset));
        Assert.InRange(offset, 0, 1);
    }
}
