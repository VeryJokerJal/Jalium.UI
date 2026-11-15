using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInputSpacingTests
{
    public CssInputSpacingTests() => CssInputFixtureEnvironment.Reset();

    [Theory]
    [InlineData("auto", false, 40)]
    [InlineData("auto", true, 40)]
    [InlineData("number", false, 20)]
    [InlineData("number", true, 20)]
    [InlineData("text", false, 40)]
    [InlineData("text", true, 40)]
    [InlineData("password", false, 20)]
    [InlineData("password", true, 20)]
    [InlineData("revealed", false, 40)]
    [InlineData("revealed", true, 40)]
    [InlineData("rich", false, 40)]
    [InlineData("rich-shared", false, 40)]
    public void SpacingChangesTheCaretAdvanceAndRemovingItRestoresTheSource(string kind, bool hosted, double expected)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read, end) = Scene(kind, hosted);
        var source = read(); Layout(root); var before = end().X;
        Css.SetStyle(input, "letter-spacing:10px;word-spacing:20px;"); Layout(root);
        Assert.InRange(end().X - before, expected - 1, expected + 1);
        Assert.Equal(source, read());
        Css.SetStyle(input, "letter-spacing:normal;word-spacing:normal;"); Layout(root);
        Assert.InRange(Math.Abs(end().X - before), 0, .02); Assert.Equal(source, read());
    }

    [Theory]
    [InlineData("auto", 40)]
    [InlineData("number", 20)]
    [InlineData("text", 40)]
    [InlineData("password", 20)]
    [InlineData("revealed", 40)]
    public void NonSolidForegroundKeepsSpacingAndCaretGeometry(string kind, double expected)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read, end) = Scene(kind, false);
        input.Foreground = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
        Layout(root); var source = read(); var before = end().X;
        Css.SetStyle(input, "letter-spacing:10px;word-spacing:20px;"); Layout(root);
        Assert.InRange(end().X - before, expected - 1, expected + 1);
        Assert.Equal(source, read());
        input.Foreground = new SolidColorBrush(Colors.Green); Layout(root);
        Assert.InRange(end().X - before, expected - 1, expected + 1);
    }

    [Theory]
    [InlineData(1, false, "B")]
    [InlineData(2, false, "A")]
    [InlineData(1, true, "B")]
    [InlineData(2, true, "A")]
    public void FallbackPaintHonorsSourceRowsAndSelectionForeground(int visibility, bool gradient, string hidden)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var owner = new TextBox { Text = "A A\nB B", FontFamily = new FontFamily("Georgia"), FontSize = 24,
            FontStretch = FontStretches.Condensed };
        Css.SetStyle(owner, "letter-spacing:4px;word-spacing:6px;"); Layout(owner);
        Brush ink = gradient ? new LinearGradientBrush(Colors.Red, Colors.Blue, 0) : Brushes.Black;
        using var layout = new TextBlock.EditorLayout(owner, owner.Text, 300, TextWrapping.Wrap, TextAlignment.Left, ink);
        var selected = new SolidColorBrush(Colors.Green);
        var text = new FormattedText(owner.Text, "Georgia", 24) { Foreground = selected,
            GlyphRanges = [0, 4], GlyphVisibility = visibility };
        var capture = new TextCapture(); layout.Draw(capture, text, default);
        Assert.NotEmpty(capture.Texts);
        Assert.DoesNotContain(hidden, string.Concat(capture.Texts.Select(VisibleText)));
        Assert.All(capture.Texts, item => Assert.Same(selected, item.Foreground));
        Assert.Equal("A A\nB B", owner.Text);
        using var target = context.CreateOffscreenRenderTarget(300, 100);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var drawing = new RenderTargetDrawingContext(target, context))
        { layout.Draw(drawing, text, default); drawing.Close(); }
        target.EndDraw();
        var pixels = new byte[300 * 100 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 300 * 4, out _, out _));
        var firstRow = layout.Selection(0, 3).Single();
        var secondRow = layout.Selection(4, 3).Single();
        bool HasInk(TextRangeMetrics row) => Enumerable.Range((int)Math.Ceiling(row.Y),
            Math.Max(0, (int)Math.Floor(row.Y + row.Height) - (int)Math.Ceiling(row.Y)))
            .Any(y => Enumerable.Range(0, 300).Any(x => pixels[(y * 300 + x) * 4 + 3] > 0));
        Assert.Equal(visibility == 1, HasInk(firstRow));
        Assert.Equal(visibility == 2, HasInk(secondRow));
    }

    private static string VisibleText(FormattedText text)
    {
        // Native paragraph commands retain the source string and carry a glyph
        // mask. Inspect the selected paint rather than mistaking retained text
        // for visible ink. Gradient fallback commands may already be sliced.
        if (text.GlyphVisibility is not (1 or 2)) return text.Text;
        var ranges = text.GlyphRanges ?? [];
        return string.Concat(text.Text.Where((_, index) =>
        {
            var included = false;
            for (var i = 0; i + 1 < ranges.Length; i += 2)
                included |= index >= ranges[i] && index < (long)ranges[i] + ranges[i + 1];
            return text.GlyphVisibility == 1 ? included : !included;
        }));
    }

    private sealed class TextCapture : DrawingContextAdapter
    {
        internal readonly List<FormattedText> Texts = [];
        public override void DrawText(FormattedText text, Point origin) => Texts.Add(text);
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawLine(Pen pen, Point start, Point end) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushClip(Geometry clip) { }
        public override void PushTransform(Transform transform) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackingParticipatesInWrappingAndSourceGeometry(bool hosted)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, read, _) = Scene("text", hosted); var input = (TextBox)control;
        input.Text = "A A A A A"; input.Width = 120; input.Height = 180; input.TextWrapping = TextWrapping.Wrap;
        Layout(root); var before = input.GetRectFromCharacterIndex(input.Text.Length);
        Css.SetStyle(input, "letter-spacing:10px;word-spacing:20px;"); Layout(root);
        var after = input.GetRectFromCharacterIndex(input.Text.Length);
        Assert.True(after.Y > before.Y, $"Tracking did not reflow input rows: {before} -> {after}");
        Assert.Equal("A A A A A", read());
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("text")]
    [InlineData("password")]
    [InlineData("revealed")]
    public void NegativeTrackingUsesNonnegativeGraphemeAdvances(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read, end) = Scene(kind, false); Layout(root); var source = read(); var before = end().X;
        Css.SetStyle(input, "letter-spacing:-3px;word-spacing:-2px;"); Layout(root);
        var expected = kind is "number" or "password" ? -6 : -8;
        Assert.InRange(end().X - before, expected - 1, expected + 1); Assert.Equal(source, read());
    }

    [Theory]
    [InlineData("auto", false)]
    [InlineData("auto", true)]
    [InlineData("number", false)]
    [InlineData("number", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("password", false)]
    [InlineData("password", true)]
    [InlineData("revealed", false)]
    [InlineData("revealed", true)]
    public void RelativeLengthsKeepTheirDeclaringFontContext(string kind, bool inherited)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read, end) = Scene(kind, false); Layout(root); var source = read(); var before = end().X;
        Css.SetStyle(inherited ? root : input, (inherited ? "font-size:40px;" : "") + "letter-spacing:.25em;word-spacing:calc(.25em + 2px);"); Layout(root);
        var expected = kind is "number" or "password" ? (inherited ? 20 : 12) : (inherited ? 32 : 20);
        Assert.InRange(end().X - before, expected - 1, expected + 1); Assert.Equal(source, read());
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("text")]
    [InlineData("password")]
    [InlineData("revealed")]
    public void ProvisionalTextCaretUsesTheSameTrackingAsItsPaint(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read, _) = Scene(kind, false); Layout(root); var source = read();
        double Advance()
        {
            var ime = (IImeSupport)input; ime.OnImeCompositionStart(); ime.OnImeCompositionUpdate("A A", 0);
            var start = ime.GetImeCaretRectangle(); ime.OnImeCompositionUpdate("A A", 3); var end = ime.GetImeCaretRectangle();
            ime.OnImeCompositionEnd(null); return end.X - start.X;
        }
        var before = Advance(); Css.SetStyle(input, "letter-spacing:10px;word-spacing:20px;"); Layout(root);
        Assert.InRange(Advance() - before, 39, 41); Assert.Equal(source, read());
        if (input is PasswordBox password) Assert.False(((IImeSupport)password).TryGetImeSurroundingText(out _));
    }

    [Fact]
    public void WordSpacingDoesNotDiscloseSpacesInTheProtectedDisplay()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _, end) = Scene("password", false); var password = (PasswordBox)input; Layout(root); var before = end().X;
        Css.SetStyle(input, "word-spacing:30px;"); Layout(root); Assert.InRange(Math.Abs(end().X - before), 0, .02);
        password.Password = "AAA"; Layout(root); Assert.InRange(Math.Abs(end().X - before), 0, .02);
        Assert.False(((IImeSupport)password).TryGetImeSurroundingText(out _));
    }

    [Theory]
    [InlineData("auto", false)]
    [InlineData("auto", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    public void TrackedPointerHitsStayAtWholeSourceGraphemes(string kind, bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "A\u0308 e\u0301 👩‍👩‍👧‍👦 אבג";
        var (root, input, read, _) = Scene(kind, false);
        if (input is AutoCompleteBox auto) auto.Text = source; else ((TextBox)input).Text = source;
        input.FontSize = 18; input.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Css.SetStyle(input, "letter-spacing:3px;word-spacing:6px;text-emphasis:dot;"); Layout(root);
        var editor = (TextBoxBase)input; editor.CaretIndexCore = 0;
        var ime = (IImeSupport)input; var caret = ime.GetImeCaretRectangle();
        var boundaries = GraphemeClusters.GetBoundaries(source);
        for (var x = 1; x < 299; x += 2)
        {
            Assert.True(ime.TryGetImeCharacterIndex(new Point(x, caret.Y + caret.Height / 2), false, out var index));
            Assert.Contains(index, boundaries);
        }
        Assert.Equal(source, read());
    }

    [Fact]
    public void WrappedWordMovementUsesTrackedRowsWithoutChangingTheDocument()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, _, _) = Scene("text", false); var input = (TextBox)control;
        const string source = "alpha beta gamma"; input.Text = source; input.Width = 130;
        input.TextWrapping = TextWrapping.Wrap; Css.SetStyle(input, "letter-spacing:4px;word-spacing:6px;"); Layout(root);
        input.CaretIndexCore = 0;
        for (var i = 0; i < 8; i++) input.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Right, ModifierKeys.Control, true, false, 0));
        Assert.Equal(source.Length, input.CaretIndexCore);
        for (var i = 0; i < 8; i++) input.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Left, ModifierKeys.Control, true, false, 0));
        Assert.Equal(0, input.CaretIndexCore); Assert.Equal(source, input.Text);
    }

    private static (StackPanel Root, Control Input, Func<string> Read, Func<Rect> End) Scene(string kind, bool hosted)
    {
        Control input; Func<string> read; Func<Rect> end;
        if (kind is "password" or "revealed")
        {
            var value = new PasswordBox { Password = "A A", PasswordChar = '•', IsPasswordRevealed = kind == "revealed" };
            value.Resources.MergedDictionaries.Add(MenuFlyoutTemplateTestHelper.Themes.Value);
            input = value; read = () => value.Password; end = () => { value.SelectAll(); return value.GetImeCaretRectangle(); };
        }
        else if (kind == "number")
        {
            var value = new NumberBox { Value = 121, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
            input = value; read = () => value.Text; end = () => { value.SelectAll(); return value.GetImeCaretRectangle(); };
        }
        else if (kind == "auto")
        {
            var value = new AutoCompleteBox { Text = "A A" };
            input = value; read = () => value.Text; end = () => { value.SelectAll(); return value.GetImeCaretRectangle(); };
        }
        else if (kind.StartsWith("rich", StringComparison.Ordinal))
        {
            var paragraph = new Paragraph(new Run("A A"));
            if (kind == "rich-shared") Css.SetStyle(paragraph, "display:flex;");
            var value = new RichTextBox { Document = new FlowDocument(paragraph) { FontFamily = new FontFamily("Georgia"), FontSize = 24 } };
            input = value; read = () => value.Document.GetText().TrimEnd('\n'); end = () => { value.SelectAll(); return ((IImeSupport)value).GetImeCaretRectangle(); };
        }
        else
        {
            var value = new TextBox { Text = "A A" };
            input = value; read = () => value.Text; end = () => value.GetRectFromCharacterIndex(value.Text.Length);
        }
        input.Width = 300; input.Height = 96; input.FontSize = 24; input.FontFamily = new FontFamily("Georgia");
        input.Padding = default; input.BorderThickness = default; input.Background = null;
        if (hosted)
        {
            var template = new ControlTemplate(input.GetType());
            if (input is PasswordBox password)
                template.SetVisualTree(() => new PasswordBoxView { Name = "PART_TextView", Owner = password });
            else
                template.SetVisualTree(() => new Border { Name = "PART_ContentHost" });
            input.Template = template;
        }
        var root = new StackPanel { Width = 300, Height = 220 }; root.Children.Add(input); return (root, input, read, end);
    }

    private static void Layout(FrameworkElement root)
    {
        for (var i = 0; i < 6; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(300, 220)); root.Arrange(new(0, 0, 300, 220)); root.UpdateLayout(); }
    }
}
