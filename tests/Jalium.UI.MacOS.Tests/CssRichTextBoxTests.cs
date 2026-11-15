using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssRichTextBoxTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() { Keyboard.ClearFocus(); _context.Dispose(); }

    public static IEnumerable<object[]> DisplayCases()
    {
        foreach (var target in new[] { "run", "span", "paragraph", "section", "document" })
            foreach (var display in new[] { "inline", "block", "inline-block", "flow-root", "flex", "inline-flex", "grid", "inline-grid", "table", "inline-table", "table-row", "table-cell", "contents", "none" })
                yield return [target, display];
    }

    [Theory]
    [MemberData(nameof(DisplayCases))]
    public void EditingTextGeometryMatchesTheOriginalDocumentViewer(string target, string display)
    {
        using var f = new Fixture(target);
        f.Style($"display:{display};grid-template-columns:32px 48px;gap:5px;");
        f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
    }

    public static IEnumerable<object[]> StyledCases()
    {
        foreach (var target in new[] { "run", "span", "paragraph", "section", "document" })
            foreach (var style in new[]
            {
                "display:block;margin:4px 7px;padding:8px;border:2px solid #146342;border-radius:5px;background:#eef5f0;",
                "display:inline;box-decoration-break:clone;padding:3px;border:1px solid #146342;",
                "display:grid;width:140px;grid-template-columns:50px 70px;gap:7px;padding:4px;",
                "display:flex;width:160px;gap:8px;justify-content:space-between;",
                "display:inline-block;width:50%;box-sizing:border-box;border:3px solid #146342;padding:5px;",
                "display:block;font-size:21px;line-height:1.5;letter-spacing:1px;",
                "display:block;position:relative;left:9px;top:6px;",
                "display:contents;padding:30px;border:9px solid red;background:red;",
            }) yield return [target, style];
    }

    [Theory]
    [MemberData(nameof(StyledCases))]
    public void EditingAndViewerBoxModelsUseTheSameGeometry(string target, string style)
    {
        using var f = new Fixture(target);
        f.Style(style); f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
    }

    [Theory]
    [InlineData("run")]
    [InlineData("span")]
    [InlineData("paragraph")]
    [InlineData("section")]
    public void StylesCanBeChangedAndRemovedWithoutReplacingTheSource(string target)
    {
        using var f = new Fixture(target);
        foreach (var display in new[] { "block", "inline-grid", "contents", "none", "inline", "flex", "" })
        {
            f.Style(display.Length == 0 ? "" : $"display:{display};grid-template-columns:36px 50px;gap:4px;padding:3px;");
            f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
            f.Runs[1].Text = f.Runs[1].Text == "BB " ? "XY " : "BB ";
            f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
        }
    }

    [Theory]
    [InlineData("block")]
    [InlineData("inline-grid")]
    [InlineData("contents")]
    public void ReplacingTextAndUndoRedoRetainDocumentStructureAndCss(string display)
    {
        using var f = new Fixture("span");
        f.Style($"display:{display};grid-template-columns:36px 48px;gap:4px;padding:3px;");
        f.Layout();
        var before = f.Document.GetText();
        var support = (IImeSupport)f.Editor;
        var offset = f.Document.GetDocumentOffset(f.Runs[1], 0);
        Assert.True(support.TryReplaceImeText(offset, 2, "补写🙂"));
        var after = f.Document.GetText();
        Assert.NotEqual(before, after);
        f.Layout(); f.SameTextAndGeometry();
        Assert.True(f.Editor.Undo());
        Assert.Equal(before, f.Document.GetText());
        f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
        Assert.True(f.Editor.Redo());
        Assert.Equal(after, f.Document.GetText());
        f.Layout(); f.SameTextAndGeometry();
    }

    [Fact]
    public void HiddenSourceOffsetsAreMappedForEditableAutomation()
    {
        using var f = new Fixture("run");
        f.Style("display:none;"); f.Layout();
        Assert.DoesNotContain("BB", f.Provider.DocumentRange.GetText(-1));
        var range = f.Provider.DocumentRange.FindText("CC", false, false)!;
        range.Select();
        Assert.Equal("CC", f.Editor.Selection.Text);
        Assert.Equal(f.Document.GetDocumentOffset(f.Runs[2], 0), f.Editor.Selection.Start.DocumentOffset);
        var view = (IAutomationTextViewSource)f.Editor.GetAutomationPeer()!;
        Assert.True(view.ReplaceSelection("替换"));
        Assert.Contains("替换", f.Document.GetText());
        Assert.Contains("BB", f.Document.GetText());
        f.Layout(); f.SameTextAndGeometry();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 9, 3)]
    public void PaddingBorderAndScrollAreAppliedOnceToTextHitsAndIme(double left, double top, double border)
    {
        using var f = new Fixture("span");
        f.Editor.Padding = new(left, top, 2, 4); f.Editor.BorderThickness = new(border);
        f.Style("display:block;padding:8px;border:2px solid green;margin:3px;");
        f.Layout(); f.SameTextAndGeometry();
        var raw = f.Document.GetDocumentOffset(f.Runs[1], 1);
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(raw, 0));
        var caret = f.Editor.GetImeCaretRectangle();
        var expected = Fixture.Bounds(f.ReferenceProvider.DocumentRange.FindText("BB", false, false)!).Single();
        Assert.Equal(expected.Top + top + border, caret.Top, 4);
        Assert.Equal(expected.Height, caret.Height, 4);
        Assert.True(caret.Left >= expected.Left + left + border);
        Assert.True(caret.Left <= expected.Right + left + border);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditableProviderHonorsReadOnlyAndDisabledState(bool readOnly)
    {
        using var f = new Fixture("span"); f.Style("display:block;padding:4px;"); f.Layout();
        f.Provider.DocumentRange.FindText("BB", false, false)!.Select();
        var before = f.Document.GetText();
        if (readOnly) f.Editor.IsReadOnly = true; else f.Editor.IsEnabled = false;
        Assert.False(((IAutomationTextViewSource)f.Editor.GetAutomationPeer()!).ReplaceSelection("禁止"));
        Assert.Equal(before, f.Document.GetText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisualLineNavigationUsesTheDisplayedBoxes(bool shift)
    {
        using var f = new Fixture("span"); f.Style("display:block;padding:5px;border:1px solid green;"); f.Layout();
        var support = (IImeSupport)f.Editor;
        Assert.True(support.TrySetImeSelection(1, 0));
        var initial = f.Editor.GetImeCaretRectangle();
        var lines = ((IAutomationTextViewSource)f.Reference.GetAutomationPeer()!).GetTextLines();
        var next = lines.First(line => line.Bounds.Top > initial.Top + .001);
        var expected = f.Reference.OffsetFromPoint(new(initial.X, next.Bounds.Top + next.Bounds.Height / 2));
        f.Key(Key.Down, shift ? ModifierKeys.Shift : ModifierKeys.None);
        Assert.Equal(expected, f.Editor.CaretPosition!.DocumentOffset);
        Assert.Equal(shift ? 1 : expected, f.Editor.Selection.Start.DocumentOffset);
        var row = f.Editor.GetImeCaretRectangle();
        f.Key(Key.End); var end = f.Editor.CaretPosition!.DocumentOffset;
        Assert.Equal(next.Start + next.Length, end);
        Assert.Equal(row.Y, f.Editor.GetImeCaretRectangle().Y, 4);
        f.Key(Key.End); Assert.Equal(end, f.Editor.CaretPosition!.DocumentOffset);
        Assert.Equal(row.Y, f.Editor.GetImeCaretRectangle().Y, 4);
        f.Key(Key.Home); Assert.Equal(next.Start, f.Editor.CaretPosition!.DocumentOffset);
    }

    [Fact]
    public void ClickingASoftRowEndKeepsItsBackwardCaretAffinity()
    {
        using var f = new Fixture("span");
        f.Runs[0].Text = "AA BB CC "; f.Runs[1].Text = "DD EE FF "; f.Runs[2].Text = "GG HH II "; f.Runs[3].Text = "JJ KK LL";
        f.Style("display:inline;"); f.Layout(110, 600);
        var lines = ((IAutomationTextViewSource)f.Reference.GetAutomationPeer()!).GetTextLines();
        var line = lines[0];
        var position = f.Editor.GetPositionFromPoint(new(109, line.Bounds.Top + line.Bounds.Height / 2), true)!;
        Assert.Equal(line.Start + line.Length, position.DocumentOffset);
        f.Editor.CaretPosition = position;
        Assert.Equal(line.Bounds.Top, f.Editor.GetImeCaretRectangle().Top, 4);
        Assert.Equal(LogicalDirection.Backward, position.LogicalDirection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrowsSkipHiddenSourcesWithoutEditingThem(bool right)
    {
        using var f = new Fixture("run"); f.Style("display:none;"); f.Layout();
        var seam = f.Document.GetDocumentOffset(f.Runs[1], 0);
        var next = f.Document.GetDocumentOffset(f.Runs[2], 0);
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(right ? seam - 1 : next, 0));
        f.Key(right ? Key.Right : Key.Left);
        Assert.Equal(right ? next : seam, f.Editor.CaretPosition!.DocumentOffset);
        Assert.Equal("BB ", f.Runs[1].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletingVisibleCharactersKeepsTheHiddenSource(bool backward)
    {
        using var f = new Fixture("run"); f.Style("display:none;"); f.Layout();
        var seam = f.Document.GetDocumentOffset(f.Runs[1], 0);
        var next = f.Document.GetDocumentOffset(f.Runs[2], 0);
        var original = f.Document.GetText();
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(backward ? next : seam, 0));
        f.Key(backward ? Key.Back : Key.Delete);
        Assert.Equal("BB ", f.Runs[1].Text);
        Assert.Equal(backward ? "AA" : "AA ", f.Runs[0].Text);
        Assert.Equal(backward ? "CC " : "C ", f.Runs[2].Text);
        f.Layout(); f.SameTextAndGeometry();
        Assert.True(f.Editor.Undo()); Assert.Equal(original, f.Document.GetText());
        f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
    }

    [Fact]
    public void AnEmptyCssDocumentCanBeEditedAndUndone()
    {
        var document = new FlowDocument { PagePadding = new(0), FontSize = 18 };
        Css.SetStyle(document, "display:block;line-height:28px;");
        var editor = new RichTextBox(document) { Padding = new(8), BorderThickness = new(2), Template = null };
        editor.Measure(new(180, 100)); editor.Arrange(new(0, 0, 180, 100));
        Assert.True(editor.GetImeCaretRectangle().Height > 0);
        Assert.True(((IImeSupport)editor).TryReplaceImeText(0, 0, "新文🙂"));
        Assert.Contains("新文🙂", document.GetText());
        Assert.True(editor.Undo()); Assert.Equal("", document.GetText());
        Assert.Same(document, editor.Document);
    }

    [Fact]
    public void TypingAtAHiddenSeamUsesTheVisibleRunAndItsFormatting()
    {
        using var f = new Fixture("run"); f.Style("display:none;");
        f.Runs[2].FontWeight = FontWeights.Bold; f.Layout();
        var start = f.Document.GetDocumentOffset(f.Runs[2], 0);
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(start, 0));
        Assert.True(((IImeSupport)f.Editor).TryReplaceImeText(start, 0, "新"));
        Assert.Equal("BB ", f.Runs[1].Text); Assert.Equal("新CC ", f.Runs[2].Text);
        Assert.Equal(FontWeights.Bold, f.Runs[2].FontWeight);
        f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
        Assert.True(f.Editor.Undo()); Assert.Equal("CC ", f.Runs[2].Text);
        f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
    }

    [Fact]
    public void WordDeletionCanCrossAHiddenSourceAsOneUndoUnit()
    {
        using var f = new Fixture("run"); f.Style("display:none;");
        f.Runs[0].Text = "AA"; f.Runs[1].Text = "BB"; f.Runs[2].Text = "CC"; f.Runs[3].Text = " DD"; f.Layout();
        f.Editor.IsUndoEnabled = false; f.Editor.IsUndoEnabled = true;
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(f.Document.GetDocumentOffset(f.Runs[2], 2), 0));
        f.Key(Key.Back, ModifierKeys.Control);
        Assert.Equal("BB", f.Runs[1].Text); Assert.Equal("BB DD\nZZ\n", f.Document.GetText());
        Assert.True(f.Editor.Undo()); Assert.Equal("AABBCC DD\nZZ\n", f.Document.GetText()); Assert.False(f.Editor.CanUndo);
        f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    public void NarrowColumnsAndClampedTextKeepViewerGeometry(double width)
    {
        using var f = new Fixture("span");
        f.Runs[0].Text = "AA BB CC "; f.Runs[1].Text = "DD EE FF "; f.Runs[2].Text = "GG HH II "; f.Runs[3].Text = "JJ KK LL";
        f.Style("display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;");
        foreach (var mode in new[] { "columns:2;column-gap:12px;height:48px;column-fill:auto;", "line-clamp:2 no-ellipsis;" })
        {
            Css.SetStyle(f.Document, "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;display:block;" + mode);
            f.Layout(width); f.SameTextAndGeometry(); f.OriginalNodes();
        }
    }

    [Fact]
    public void ADocumentWithAContentsParentBoxDoesNotPaintItsOwnInsets()
    {
        using var f = new Fixture("document");
        f.Style("display:contents;padding:30px;border:9px solid red;background:red;"); f.Layout();
        var first = Fixture.Bounds(f.Provider.DocumentRange.FindText("AA", false, false)!).Single();
        Assert.Equal(0, first.X, 4); Assert.Equal(0, first.Y, 4);
        f.SameTextAndGeometry();
    }

    [Theory]
    [InlineData("display:inline;")]
    [InlineData("display:contents;")]
    [InlineData("display:inline;box-decoration-break:clone;padding:3px;border:1px solid #146342;")]
    [InlineData("display:contents;padding:30px;border:9px solid red;background:red;")]
    public void ABoxlessOrInlineDocumentRootStillExposesItsVisibleBody(string style)
    {
        using var f = new Fixture("document"); f.Style(style); f.Layout(180, 240);
        var extent = (double)typeof(RichTextBox).GetMethod("GetVerticalScrollExtentHeight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(f.Editor, [24d])!;
        Assert.True(extent >= 48, $"Two 24px rows collapsed to {extent}px");
        foreach (var word in new[] { "AA", "BB", "CC", "DD", "ZZ" })
        {
            var range = f.Provider.DocumentRange.FindText(word, false, false);
            Assert.NotNull(range); Assert.NotEmpty(Fixture.Bounds(range));
            Assert.All(Fixture.Bounds(range), rect => Assert.Equal(24, rect.Height, 4));
        }
        if (style.StartsWith("display:contents;")) Assert.Equal(48, extent, 4);
        f.OriginalNodes();
    }

    [Fact]
    public void AHiddenDocumentDoesNotPaintItsCanvasBackground()
    {
        using var f = new Fixture("document"); f.Style("display:none;background:red;"); f.Layout(180, 200);
        var bitmap = new Jalium.UI.Media.Imaging.RenderTargetBitmap(180, 200, 96, 96, Jalium.UI.Media.PixelFormats.Bgra32);
        var drawing = new Jalium.UI.Media.Imaging.SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, 180, 200)); f.Editor.Render(drawing); drawing.Close();
        Assert.All(bitmap.GetPixelBuffer(), value => Assert.Equal((byte)255, value));
        Assert.Equal("", f.Provider.DocumentRange.GetText(-1));
        Assert.Contains("AA", f.Document.GetText());
    }

    [Theory]
    [InlineData(200, 0)]
    [InlineData(400, 0)]
    [InlineData(200, 10)]
    [InlineData(400, 6)]
    public void PercentageDocumentHeightUsesTheEditorContentViewport(double height, double pagePadding)
    {
        using var f = new Fixture("span"); f.Style("display:inline;");
        f.Editor.Padding = new(8); f.Editor.BorderThickness = new(2);
        f.Document.PagePadding = new(pagePadding);
        Css.SetStyle(f.Document, "display:block;height:50%;font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;");
        f.Layout(240, height);
        var extent = (double)typeof(RichTextBox).GetMethod("GetVerticalScrollExtentHeight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(f.Editor, [24d])!;
        Assert.Equal((height - 20 - 2 * pagePadding) / 2 + 2 * pagePadding, extent, 4);
        Assert.Equal(extent, f.Reference.ExtentHeight + 2 * pagePadding, 4);
    }

    [Fact]
    public void RemovingTheLastCssLayoutRestoresTheNativeSurface()
    {
        var run = new Run("AA"); var span = new Span(new Run("BB"));
        var paragraph = new Paragraph(run); paragraph.Inlines.Add(span);
        var document = new FlowDocument(paragraph) { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16 };
        var editor = new RichTextBox(document) { Padding = new(0), BorderThickness = new(0), Template = null };
        void Layout() { for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); editor.Measure(new(240, 200)); editor.Arrange(new(0, 0, 240, 200)); } }
        Layout(); var count = VisualTreeHelper.GetChildrenCount(editor); var caret = editor.GetImeCaretRectangle();
        Css.SetStyle(span, "display:block;padding:7px;"); Layout(); Assert.True(VisualTreeHelper.GetChildrenCount(editor) > count);
        Css.SetStyle(span, ""); Layout(); Assert.Equal(count, VisualTreeHelper.GetChildrenCount(editor)); Assert.Equal(caret, editor.GetImeCaretRectangle());
        Assert.Same(run, paragraph.Inlines[0]); Assert.Same(span, paragraph.Inlines[1]);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("inline-grid")]
    [InlineData("contents")]
    public void ParagraphInsertionAndUndoKeepCssOnTheSplitInline(string display)
    {
        using var f = new Fixture("span"); f.Style($"display:{display};padding:3px;"); f.Layout();
        var original = f.Document.GetText();
        var start = f.Document.GetDocumentOffset(f.Runs[1], 1);
        Assert.True(((IImeSupport)f.Editor).TrySetImeSelection(start, 0)); f.Key(Key.Enter);
        Assert.Equal(original.Insert(start, "\n"), f.Document.GetText());
        f.Layout(); f.SameTextAndGeometry();
        Assert.True(f.Editor.Undo()); Assert.Equal(original, f.Document.GetText()); f.Layout(); f.SameTextAndGeometry(); f.OriginalNodes();
        Assert.True(f.Editor.Redo()); Assert.Equal(original.Insert(start, "\n"), f.Document.GetText()); f.Layout(); f.SameTextAndGeometry();
    }

    [Fact]
    public void CompositionGeometryKeepsTheCssRowHeightAndOriginalText()
    {
        using var f = new Fixture("span"); f.Style("display:block;font-size:21px;line-height:36px;padding:5px;"); f.Layout();
        var support = (IImeSupport)f.Editor; var original = f.Document.GetText();
        Assert.True(support.TrySetImeSelection(f.Document.GetDocumentOffset(f.Runs[1], 1), 0));
        var caret = f.Editor.GetImeCaretRectangle();
        Assert.Equal(36, caret.Height, 4);
        support.OnImeCompositionStart(); support.OnImeCompositionUpdate("中🙂", 1);
        var composition = f.Editor.GetImeCaretRectangle();
        Assert.Equal(caret.Top, composition.Top, 4); Assert.Equal(caret.Height, composition.Height, 4);
        Assert.True(composition.X > caret.X); Assert.Equal(original, f.Document.GetText());
        support.OnImeCompositionEnd(null); Assert.Equal(caret.Height, f.Editor.GetImeCaretRectangle().Height, 4);
        Assert.Equal(original, f.Document.GetText());
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly Run[] Runs = [new("AA "), new("BB "), new("CC "), new("DD"), new("ZZ")];
        internal readonly Span Span;
        internal readonly Paragraph Paragraph;
        internal readonly Section Section;
        internal readonly FlowDocument Document;
        internal readonly RichTextBox Editor;
        internal readonly FlowDocumentPresentation Reference = new() { ClipToBounds = true };
        private readonly DependencyObject _target;
        private readonly Dictionary<TextElement, FrameworkContentElement?> _parents = [];
        internal ITextProvider Provider => (ITextProvider)Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        internal ITextProvider ReferenceProvider => (ITextProvider)Reference.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        internal static Rect[] Bounds(ITextRangeProvider range) => range.GetBoundingRectangles().Chunk(4).Select(value => new Rect(value[0], value[1], value[2], value[3])).ToArray();
        internal Fixture(string target)
        {
            Span = new(Runs[1]); Span.Inlines.Add(Runs[2]);
            Paragraph = new(Runs[0]); Paragraph.Inlines.Add(Span); Paragraph.Inlines.Add(Runs[3]);
            Section = new(Paragraph); Section.Blocks.Add(new Paragraph(Runs[4]));
            Document = new(Section) { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
            Css.SetStyle(Document, "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;white-space:normal;");
            Css.SetStyleSheet(Document, "Paragraph,Section{margin:0;padding:0;border:0;}");
            Editor = new(Document) { Padding = new(0), BorderThickness = new(0), Background = Brushes.White, Foreground = Brushes.Black, Template = null };
            Reference.SetDocument(Document);
            _target = target switch { "run" => Runs[1], "span" => Span, "paragraph" => Paragraph, "section" => Section, _ => Document };
            foreach (var node in Runs.Cast<TextElement>().Append(Span).Append(Paragraph).Append(Section)) _parents[node] = node.Parent;
            Editor.IsUndoEnabled = false; Editor.IsUndoEnabled = true;
        }
        internal void Style(string style)
        {
            var font = "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;white-space:normal;";
            Css.SetStyle(_target, ReferenceEquals(_target, Document) ? font + style : style);
        }
        internal void Layout(double width = 240, double height = 1800)
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                Editor.Measure(new(width, height)); Editor.Arrange(new(0, 0, width, height));
                var content = width - Editor.Padding.Left - Editor.Padding.Right - Editor.BorderThickness.Left - Editor.BorderThickness.Right;
                Reference.SetViewportHeight(height - Editor.Padding.Top - Editor.Padding.Bottom - Editor.BorderThickness.Top - Editor.BorderThickness.Bottom);
                Reference.Measure(new(content, double.PositiveInfinity));
                Reference.Arrange(new(0, 0, content, height - Editor.Padding.Top - Editor.Padding.Bottom - Editor.BorderThickness.Top - Editor.BorderThickness.Bottom));
            }
        }
        internal void SameTextAndGeometry()
        {
            Assert.Equal(ReferenceProvider.DocumentRange.GetText(-1), Provider.DocumentRange.GetText(-1));
            var dx = Editor.Padding.Left + Editor.BorderThickness.Left - Editor.HorizontalOffset;
            var dy = Editor.Padding.Top + Editor.BorderThickness.Top - Editor.VerticalOffset;
            foreach (var word in Runs.Select(run => run.Text.Trim()).Where(text => text.Length > 0))
            {
                var expectedRange = ReferenceProvider.DocumentRange.FindText(word, false, false);
                var actualRange = Provider.DocumentRange.FindText(word, false, false);
                if (expectedRange is null) { Assert.Null(actualRange); continue; }
                Assert.NotNull(actualRange);
                var expected = Bounds(expectedRange); var actual = Bounds(actualRange);
                Assert.Equal(expected.Length, actual.Length);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i].X + dx, actual[i].X, 4); Assert.Equal(expected[i].Y + dy, actual[i].Y, 4);
                    Assert.Equal(expected[i].Width, actual[i].Width, 4); Assert.Equal(expected[i].Height, actual[i].Height, 4);
                    var point = new Point(actual[i].X + Math.Min(2, actual[i].Width / 2), actual[i].Y + actual[i].Height / 2);
                    var raw = Editor.GetPositionFromPoint(point, true)!.DocumentOffset;
                    Assert.Equal(Reference.OffsetFromPoint(new(point.X - dx, point.Y - dy)), raw);
                }
            }
        }
        internal void OriginalNodes()
        {
            Assert.Same(Document, Editor.Document); Assert.Same(Editor, Document.Parent);
            foreach (var (node, parent) in _parents) Assert.Same(parent, node.Parent);
            Assert.Same(Runs[1], Span.Inlines[0]); Assert.Same(Runs[2], Span.Inlines[1]);
            Assert.Single(CssNode.Get(Editor).EnumerateChildren());
            Assert.Same(Document, CssNode.Get(Editor).EnumerateChildren().Single().Target);
            Assert.Empty(Editor.GetAutomationPeer()!.GetChildren());
        }
        internal void Key(Key key, ModifierKeys modifiers = ModifierKeys.None)
        {
            var args = new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 1);
            Editor.RaiseEvent(args); Assert.True(args.Handled);
        }
        public void Dispose() { Keyboard.ClearFocus(); Reference.Dispose(); }
    }
}
