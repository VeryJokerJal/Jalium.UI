using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFirstLetterTests
{
    [Theory]
    [InlineData("TextBlock::first-letter", true)]
    [InlineData(".text:first-letter", true)]
    [InlineData(".text:hover::first-letter", true)]
    [InlineData("StackPanel > .text::first-letter", true)]
    [InlineData(".text::first-letter > Run", false)]
    [InlineData(".text::first-letter:hover", false)]
    [InlineData(":is(::first-letter)", true)]
    [InlineData(":has(::first-letter)", false)]
    public void FirstLetterSelectorsKeepThePseudoElementInTheSubject(string selector, bool valid)
        => Assert.Equal(valid, CssSelectorParser.ParseGroup(selector, out _) is not null);

    [Theory]
    [InlineData("Alpha beta", "A")]
    [InlineData("  Alpha beta", "A")]
    [InlineData("\n\tAlpha beta", "A")]
    [InlineData("“Alpha” beta", "“A")]
    [InlineData("「中」文排版", "「中」")]
    [InlineData("A! beta", "A!")]
    [InlineData("6,789 items", "6,")]
    [InlineData("e\u0301cole française", "e\u0301")]
    [InlineData("👩🏽‍💻 works here", "👩🏽‍💻")]
    [InlineData("🇨🇳 中文段落", "🇨🇳")]
    [InlineData("#Topic follows", "#T")]
    [InlineData("“\nAlpha beta", "“ A")]
    [InlineData("“\tAlpha beta", "“ A")]
    [InlineData("«\u202FA\u202F» more", "«\u202FA\u202F»")]
    [InlineData("\u00A0Alpha beta", "A")]
    [InlineData("\u3000中文排版", "中")]
    [InlineData("\u2009Alpha beta", "A")]
    [InlineData("A(beta)", "A")]
    [InlineData("A—beta", "A")]
    public void FirstLetterPaintsTheWholeTypographicUnitAndAssociatedPunctuation(string source, string expected)
    {
        var text = Text(source);
        Add(text, ".text { white-space: normal; } .text::first-letter { color: red; font-size: 40px; }");
        Layout(text);
        Assert.Equal(expected, string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        Assert.Equal(source, text.Text);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Fact]
    public void DutchIjIsSelectedAsOneLetterWithoutChangingTheRunOrItsParent()
    {
        var text = Text(""); text.Language = Jalium.UI.Markup.XmlLanguage.GetLanguage("nl-NL");
        var run = new Run("ijsselmeer verder"); text.Inlines.Add(run);
        var parent = ((FrameworkContentElement)run).Parent;
        Add(text, ".text::first-letter { color: red; text-transform: uppercase; }");
        Layout(text);
        Assert.Equal("IJ", string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        Assert.Same(run, Assert.Single(text.Inlines)); Assert.Same(parent, ((FrameworkContentElement)run).Parent);
        Assert.Equal("ijsselmeer verder", run.Text);
    }

    [Theory]
    [InlineData("\nAlpha")]
    [InlineData("\r\nAlpha")]
    [InlineData("\fAlpha")]
    public void PreservedEmptyFirstLineHasNoFirstLetter(string source)
    {
        var text = Text(source);
        Add(text, ".text { white-space: pre; } .text::first-letter { color: red; font-size: 40px; }");
        Layout(text); Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red);
    }

    [Fact]
    public void ExplicitLineBreakPreventsSelectingTheSecondLine()
    {
        var text = Text(""); text.Inlines.Add(new LineBreak()); text.Inlines.Add(new Run("Alpha"));
        Add(text, ".text::first-letter { color: red; }");
        Layout(text); Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red);
    }

    [Fact]
    public void FirstLetterInheritsFromItsImmediateInlineAndOverridesOnlyItsOwnProperties()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text(""); var run = new Run("Alpha beta");
        var span = new Italic(run) { FontSize = 23, Foreground = Brushes.Blue }; text.Inlines.Add(span);
        Css.SetStyle(run, "font-style:inherit;");
        Add(text, ".text::first-letter { color: red; } Run { color: green; }");
        Layout(text); var paint = Draw(text);
        Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.Contains(paint, p => p.Text == "A" && p.Color == Colors.Red && Math.Abs(p.Size - 23) < .001);
        Assert.Contains(paint, p => p.Text.Contains("lpha") && p.Color == Colors.Green && Math.Abs(p.Size - 23) < .001);
        Assert.Same(span, Assert.Single(text.Inlines)); Assert.Same(run, Assert.Single(span.Inlines));
        var expected = Text(""); var head = new Run("A"); var tail = new Run("lpha beta"); var referenceSpan = new Italic { FontSize = 23 };
        referenceSpan.Inlines.Add(head); referenceSpan.Inlines.Add(tail); expected.Inlines.Add(referenceSpan);
        Css.SetStyle(head, "display:inline;color:red;font-style:inherit;"); Css.SetStyle(tail, "color:green;font-style:inherit;"); Layout(expected);
        byte[] Pixels(TextBlock owner)
        {
            var bitmap = new RenderTargetBitmap(240, 300, 96, 96, PixelFormats.Bgra32); bitmap.Render(owner);
            var pixels = new byte[240 * 300 * 4]; bitmap.CopyPixels(pixels, 240 * 4, 0); return pixels;
        }
        var actualPixels = Pixels(text); var referencePixels = Pixels(expected);
        for (var index = 0; index < actualPixels.Length; index++) Assert.InRange(Math.Abs(actualPixels[index] - referencePixels[index]), 0, 3);
    }

    [Fact]
    public void ContentAndDisplayDoNotReplaceTheSourceOrSuppressTheLetter()
    {
        var text = Text("Alpha beta");
        Add(text, ".text::first-letter { color: red; content: 'wrong'; display: none; position: absolute; width: 900px; }");
        Layout(text); var paint = Draw(text);
        Assert.Equal("A", string.Concat(paint.Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        Assert.DoesNotContain(paint, p => p.Text.Contains("wrong"));
        Assert.Equal("Alpha beta", text.Text); Assert.True(text.DesiredSize.Width < 900);
    }

    [Fact]
    public void TextClassAndSheetChangesRebuildTheProjectionAndRestoreNativeText()
    {
        var text = Text("Alpha beta"); var sheet = Add(text, ".text::first-letter { color: red; font-size: 40px; }");
        Layout(text); Assert.Contains(Draw(text), p => p.Text == "A" && p.Color == Colors.Red);
        text.Text = "Beta gamma"; Layout(text); Assert.Contains(Draw(text), p => p.Text == "B" && p.Color == Colors.Red);
        Css.SetClass(text, "other"); Layout(text); Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red);
        Css.SetClass(text, "text"); Layout(text); Assert.Contains(Draw(text), p => p.Text == "B" && p.Color == Colors.Red);
        Css.GetStyleSheets(text).Remove(sheet); Layout(text); Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red);
        Assert.Equal("Beta gamma", text.Text);
    }

    [Theory]
    [InlineData("font-size:40px;color:red;")]
    [InlineData("font-size:2em;color:red;letter-spacing:2px;")]
    [InlineData("font-size:40px;color:red;padding:3px;border:2px solid blue;background:yellow;")]
    [InlineData("float:left;font-size:56px;line-height:.9;margin-right:8px;color:red;")]
    [InlineData("float:right;font-size:56px;line-height:1;margin-left:8px;padding:3px;border:2px solid blue;color:red;")]
    public void FirstLetterGeometryMatchesAnIndependentlyAuthoredInline(string style)
    {
        const string source = "Alpha beta gamma delta epsilon zeta eta theta. Another line follows the large letter.";
        var actual = Text(source); var expected = Text("");
        var letter = new Run("A"); expected.Inlines.Add(letter); expected.Inlines.Add(new Run(source[1..]));
        Add(actual, ".text { display:block;line-height:24px;white-space:normal; } .text::first-letter { " + style + " }");
        Add(expected, ".text { display:block;line-height:24px;white-space:normal; }");
        Css.SetStyle(letter, "display:inline;" + style);
        Layout(actual, 200); Layout(expected, 200);
        for (var i = 0; i < source.Length; i++)
            Assert.Equal(expected.DocumentTextBounds(i, 1), actual.DocumentTextBounds(i, 1));
        actual.Select(0, 5); Assert.Equal("Alpha", actual.SelectedText);
        var first = Assert.Single(actual.DocumentTextBounds(0, 1));
        Assert.InRange(actual.DocumentCharacterFromPoint(new(first.Left + .01, first.Top + first.Height / 2)), 0, 1);
    }

    [Fact]
    public void DecoratedFirstLetterReachesAStableLayoutWithoutReplacingItsProjection()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("Alpha beta 中文排版 continues through the following lines.");
        Add(text, ".text { display:block;line-height:24px; } .text::first-letter { font-size:40px;padding:3px;border:2px solid green;border-radius:6px;background:yellow;opacity:.7; }");
        Layout(text);
        Visual ProjectedLetterBox()
        {
            var boxes = new List<Visual>();
            void Visit(Visual visual)
            {
                if (visual is ICssStyledBox { StyleSource: Run run } && CssFirstLetter.Origin(run) is not null)
                    boxes.Add(visual);
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
                    Visit((Visual)VisualTreeHelper.GetChild(visual, index));
            }
            Visit(text);
            return Assert.Single(boxes);
        }
        var original = ProjectedLetterBox();
        for (var pass = 0; pass < 3; pass++)
        {
            text.UpdateLayout();
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
            Assert.Same(original, ProjectedLetterBox());
        }
    }

    [Fact]
    public void BlockAncestorAndParagraphPseudosNestWithInlineInheritance()
    {
        var root = new StackPanel(); Css.SetClass(root, "root");
        var first = Text("Alpha beta"); var second = Text("Gamma delta"); root.Children.Add(first); root.Children.Add(second);
        Add(root, ".root { display:block; } .root::first-letter { font-size:2em;color:red; } .text::first-letter { font-size:1.5em; }");
        Layout(root);
        Assert.Contains(Draw(first), p => p.Text == "A" && p.Color == Colors.Red && Math.Abs(p.Size - 48) < .001);
        Assert.Contains(Draw(second), p => p.Text == "G" && p.Color != Colors.Red && Math.Abs(p.Size - 24) < .001);
        root.Children.Remove(first); Layout(root);
        Assert.Contains(Draw(second), p => p.Text == "G" && p.Color == Colors.Red && Math.Abs(p.Size - 48) < .001);
    }

    [Fact]
    public void InFlowGeneratedBeforeOwnsTheFirstLetterAndDynamicContentReleasesIt()
    {
        var text = Text("Alpha beta"); text.Tag = "Note: ";
        Add(text, ".text::before { content:attr(Tag); } .text::first-letter { color:red;font-size:40px; }");
        Layout(text);
        Assert.Equal("N", string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        text.Tag = ""; Layout(text);
        Assert.Equal("A", string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        Assert.Equal("Alpha beta", text.Text);
    }

    [Theory]
    [InlineData("inline-block")]
    [InlineData("inline-flex")]
    [InlineData("inline-grid")]
    public void AnAtomicFirstChildStopsTheAncestorFirstLetter(string display)
    {
        var root = new StackPanel(); Css.SetClass(root, "root");
        var atom = Text("Alpha"); Css.SetClass(atom, "atom"); root.Children.Add(atom); root.Children.Add(Text("Beta"));
        Add(root, ".root { display:block; } .atom { display:" + display + "; } .root::first-letter { color:red;font-size:40px; }");
        Layout(root); Assert.DoesNotContain(Draw(root), p => p.Color == Colors.Red);
    }

    [Fact]
    public void InlineOriginDoesNotProduceItsOwnFirstLetter()
    {
        var text = Text("Alpha beta"); Add(text, ".text { display:inline; } .text::first-letter { color:red;font-size:40px; }");
        Layout(text); Assert.DoesNotContain(Draw(text), p => p.Color == Colors.Red);
    }

    [Fact]
    public void HiddenRunIsSkippedWithoutChangingSourceOffsets()
    {
        var text = Text(""); var hidden = new Run("Hidden "); var visible = new Run("Alpha beta");
        Css.SetStyle(hidden, "display:none;"); text.Inlines.Add(hidden); text.Inlines.Add(visible);
        Add(text, ".text::first-letter { color:red;font-size:40px; }"); Layout(text);
        Assert.Equal("A", string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        text.Select(7, 5); Assert.Equal("Alpha", text.SelectedText);
        Assert.Same(hidden, text.Inlines[0]); Assert.Same(visible, text.Inlines[1]);
    }

    [Fact]
    public void APrecedingSiblingChangingDisplayAndFloatInvalidatesTheOriginSelection()
    {
        var root = new StackPanel(); Css.SetClass(root, "root"); var blocker = new Image { Width = 20, Height = 20 };
        var body = Text("Alpha beta"); root.Children.Add(blocker); root.Children.Add(body);
        Add(root, ".root { display:block; } Image { display:inline-block; } .root::first-letter { color:red;font-size:40px; }");
        Layout(root); Assert.DoesNotContain(Draw(body), p => p.Color == Colors.Red);
        Css.SetStyle(blocker, "display:none;"); Layout(root); Assert.Contains(Draw(body), p => p.Text == "A" && p.Color == Colors.Red);
        Css.SetStyle(blocker, "display:inline-block;"); Layout(root); Assert.DoesNotContain(Draw(body), p => p.Color == Colors.Red);
        Css.SetStyle(blocker, "float:left;"); Layout(root); Assert.Contains(Draw(body), p => p.Text == "A" && p.Color == Colors.Red);
    }

    [Fact]
    public void HoverVariablesAndLayersUseTheOriginWhileTheRunKeepsInlineInheritance()
    {
        var body = Text(""); var span = new Span(new Run("Alpha beta")); body.Inlines.Add(span);
        Css.SetStyle(span, "--letter-size:2em;font-size:20px;");
        Add(body, "@layer base, accent; @layer base { .text::first-letter { color:blue; } } @layer accent { .text:hover::first-letter { color:red;font-size:var(--letter-size); } }");
        Layout(body);
        foreach (var hovered in new[] { true, false, true })
        {
            body.SetIsMouseOver(hovered); Layout(body);
            Assert.Contains(Draw(body), p => p.Text.Contains("A") && p.Color == (hovered ? Colors.Red : Colors.Blue) &&
                Math.Abs(p.Size - (hovered ? 40 : 20)) < .001);
        }
        Css.SetStyle(span, "--letter-size:3em;font-size:20px;"); Layout(body);
        Assert.Contains(Draw(body), p => p.Text == "A" && p.Color == Colors.Red && Math.Abs(p.Size - 60) < .001);
    }

    [Fact]
    public void DocumentProjectionBorrowsTheParagraphAndPreservesItsOriginalInlineTree()
    {
        var paragraph = new Paragraph(); var run = new Run("「中」文排版"); paragraph.Inlines.Add(run);
        var document = new FlowDocument(); document.Blocks.Add(paragraph); Css.SetClass(paragraph, "paragraph");
        var sheet = CssStyleSheet.Parse(".paragraph::first-letter { color:red;font-size:40px; }");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(document).Add(sheet);
        CssEvaluationScheduler.EvaluateSubtree(CssNode.Get(document));
        var text = new TextBlock(); text.PresentDocumentParagraph(paragraph, document); Layout(text);
        Assert.Equal("「中」", string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
        Assert.Same(run, Assert.Single(paragraph.Inlines)); Assert.Same(paragraph, run.Parent);
        text.Select(1, 1); Assert.Equal("中", text.SelectedText);
        run.Text = "Alpha beta"; text.PresentDocumentParagraph(paragraph, document); Layout(text);
        Assert.Equal("A", string.Concat(Draw(text).Where(p => p.Color == Colors.Red).Select(p => p.Text)));
    }

    [Fact]
    public void PrivateFirstLetterBoxesDoNotAddTabStopsOrAccessibleText()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var body = Text("Alpha beta"); var first = new Button { Content = "前" }; var next = new Button { Content = "后" };
        var root = new StackPanel(); root.Children.Add(first); root.Children.Add(body); root.Children.Add(next);
        Add(body, ".text::first-letter { font-size:40px;color:red; }");
        var window = new DisplayedTestWindow { Width = 240, Height = 300, Content = root, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(first.Focus()); Assert.True(KeyboardNavigation.MoveFocus(first)); Assert.Same(next, Keyboard.FocusedElement);
            Assert.Equal(body.Text, body.GetAutomationPeer()!.GetName()); Assert.Empty(body.GetAutomationPeer()!.GetChildren());
            body.Text = "Beta gamma"; Layout(window); Assert.Same(next, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(next, reverse: true)); Assert.Same(first, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, FontSize = 16, Foreground = Brushes.Black, IsTextSelectionEnabled = true };
        Css.SetClass(text, "text"); return text;
    }

    private static CssStyleSheet Add(FrameworkElement owner, string value)
    {
        var sheet = CssStyleSheet.Parse(value); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); return sheet;
    }

    private static void Layout(FrameworkElement root, double width = 240)
    {
        for (var i = 0; i < 5; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, 300)); root.Arrange(new Rect(0, 0, width, 300)); root.UpdateLayout();
        }
    }

    private static List<Paint> Draw(FrameworkElement root) { var context = new Capture(); root.Render(context); return context.Paints; }
    private sealed record Paint(string Text, double Size, int Style, Color Color);
    private sealed class Capture : DrawingContextAdapter
    {
        internal readonly List<Paint> Paints = [];
        public override void DrawText(FormattedText text, Point origin) => Paints.Add(new(text.Text, text.FontSize, text.FontStyle,
            text.Foreground is SolidColorBrush brush ? brush.Color : Colors.Transparent));
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }
}
