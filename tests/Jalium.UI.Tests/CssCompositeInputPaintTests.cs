using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCompositeInputPaintTests
{
    public CssCompositeInputPaintTests() => CssInputFixtureEnvironment.Reset();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void DecorationsAndEmphasisPaintWithoutChangingTheEditableSource(bool number, bool hosted, bool under)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(number, hosted);
        var before = read();
        Css.SetStyle(input, "padding:0;border:0;color:#233c32;text-emphasis:dot #146342;text-decoration:underline #20537a;text-emphasis-position:" + (under ? "under right" : "over right") + ";");
        Layout(root);
        var capture = new Capture(); root.Render(capture);
        var marks = capture.Text.Where(row => row.Text == "•" && row.Size == 10).ToArray();
        Assert.Equal(number ? 5 : 3, marks.Length);
        Assert.All(marks, row => Assert.Equal(Color.FromRgb(20, 99, 66), row.Color));
        Assert.NotEmpty(capture.Lines);
        Assert.All(capture.Lines, row => Assert.Equal(Color.FromRgb(32, 83, 122), row.Color));
        var body = Assert.Single(capture.Text.Where(row => row.Text == before));
        Assert.All(marks, mark => Assert.True(under ? mark.Point.Y > body.Point.Y : mark.Point.Y < body.Point.Y));
        Assert.Equal(before, read());
        if (input is NumberBox value) Assert.Equal(12345, value.Value);
        Css.SetStyle(input, "padding:0;border:0;"); Layout(root);
        capture = new Capture(); root.Render(capture);
        Assert.DoesNotContain(capture.Text, row => row.Text == "•" && row.Size == 10);
        Assert.Empty(capture.Lines);
        Assert.Equal(before, read());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnnotationSpaceParticipatesInDesiredHeight(bool number, bool hosted)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene(number, hosted);
        input.ClearValue(FrameworkElement.HeightProperty);
        input.FontSize = 32;
        Css.SetStyle(input, "padding:0;border:0;"); Layout(root);
        var before = input.DesiredSize.Height;
        Css.SetStyle(input, "padding:0;border:0;text-emphasis:dot;"); Layout(root);
        Assert.True(input.DesiredSize.Height > before + 8, $"Annotation did not increase desired height: {before} -> {input.DesiredSize.Height}");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectionUsesCssInkAndIncludesAnnotationSpace(bool number, bool hosted)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(number, hosted);
        var before = read();
        Css.SetClass(input, "subject");
        Css.SetStyle(input, "padding:0;border:0;color:#233c32;text-emphasis:dot;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::selection{color:#662d70;background:#fff8de;text-decoration:underline #20537a;}"));
        input.IsInactiveSelectionHighlightEnabled = true;
        input.SelectionStartCore = 0; input.SelectionLengthCore = before.Length;
        Layout(root);
        var capture = new Capture(); root.Render(capture);
        Assert.Contains(capture.Text, row => row.Text == before && row.Color == Color.FromRgb(102, 45, 112));
        Assert.Contains(capture.Rectangles, row => row.Color == Color.FromRgb(255, 248, 222) && row.Bounds.Height > 24);
        Assert.Contains(capture.Lines, row => row.Color == Color.FromRgb(32, 83, 122));
        Assert.Equal(before, read());
        Assert.Equal(before.Length, input.SelectionLengthCore);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ImeGeometryAndHitTestingFollowThePaintedBody(bool number, bool hosted, bool under)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(number, hosted);
        Css.SetStyle(input, "padding:0;border:0;color:#233c32;text-emphasis:dot;text-emphasis-position:" + (under ? "under left;" : "over right;"));
        Layout(root); var before = read(); input.CaretIndexCore = 1;
        var capture = new Capture(); root.Render(capture);
        var body = Assert.Single(capture.Text.Where(row => row.Text == read()));
        var ime = (IImeSupport)input; var caret = ime.GetImeCaretRectangle();
        Assert.InRange(Math.Abs(caret.Y - body.Point.Y), 0, .02);
        Assert.True(ime.TryGetImeTextRangeGeometry(1, 1, false, out var range));
        Assert.InRange(Math.Abs(range.Rectangle.Y - caret.Y), 0, .02);
        Assert.InRange(Math.Abs(range.Rectangle.Height - caret.Height), 0, .02);
        Assert.True(ime.TryGetImeCharacterIndex(new Point(caret.X + .01, caret.Y + caret.Height / 2), false, out var index));
        Assert.Equal(1, index);
        ime.OnImeCompositionStart(); ime.OnImeCompositionUpdate("拼音", 1);
        var provisional = ime.GetImeCaretRectangle();
        Assert.InRange(Math.Abs(provisional.Y - caret.Y), 0, .02);
        Assert.Equal(before, read());
        ime.OnImeCompositionEnd(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollingKeepsTheCaretInsideTheActualTextViewport(bool number)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene(number, false);
        input.Width = 100;
        if (input is NumberBox value) { value.Value = 123456789012345; value.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline; value.Header = "数值"; }
        else ((AutoCompleteBox)input).Text = "Office affinity with another ordinary word";
        Css.SetStyle(input, "padding:4px;border:1px solid #146342;text-emphasis:dot;font-weight:700;font-style:italic;");
        Layout(root); input.CaretIndexCore = input is NumberBox n ? n.Text.Length : ((AutoCompleteBox)input).Text.Length;
        input.ScrollToCaretPosition(); root.Render(new Capture());
        var ime = (IImeSupport)input; var caret = ime.GetImeCaretRectangle();
        Assert.True(ime.TryGetImeCharacterIndex(new Point(caret.X - .1, caret.Y + caret.Height / 2), false, out _), $"Caret escaped the text viewport: {caret}");
        Assert.True(input.HorizontalOffset > 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisabledAndReadOnlyStylesPreserveTheEditableSource(bool number, bool hosted)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(number, hosted); var before = read();
        Css.SetClass(input, "subject"); Css.SetStyle(input, "text-emphasis:dot;text-decoration:underline;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject{color:#20537a;}.subject:disabled{color:#662d70;text-emphasis-position:under left;}"));
        Layout(root); input.CaretIndexCore = 1; input.IsEnabled = false; Layout(root);
        var capture = new Capture(); root.Render(capture);
        Assert.Contains(capture.Text, row => row.Text == before && row.Color == Color.FromRgb(102, 45, 112));
        Assert.Equal(1, input.CaretIndexCore); Assert.Equal(before, read());
        input.IsEnabled = true; input.IsReadOnly = true; Layout(root);
        var ime = (IImeSupport)input; Assert.False(ime.IsImeAllowed);
        Assert.False(ime.TryReplaceImeText(0, 1, "9")); Assert.Equal(before, read());
        input.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.A, ModifierKeys.Control, true, false, 0));
        Assert.Equal(before.Length, input.SelectionLengthCore); Assert.Equal(before, read());
        input.IsReadOnly = false; Css.GetStyleSheets(root).Clear(); Layout(root);
        Assert.True(ime.IsImeAllowed); Assert.Equal(before, read());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FixedHeightRejectsImeHitsBelowTheAllocatedControl(bool number, bool hosted)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(number, hosted); var before = read();
        // Exercise a 32-DIP allocation even when another fixture registered native minimum sizes.
        input.MinHeight = 0;
        input.Height = 32; input.FontSize = 32;
        Css.SetStyle(input, "text-emphasis:dot;"); Layout(root);
        Assert.Equal(32, input.RenderSize.Height);
        Assert.False(((IImeSupport)input).TryGetImeCharacterIndex(new Point(5, 33), false, out _));
        Assert.Equal(before, read());
    }

    [Fact]
    public void AutoCompletionKeepsItsSuggestionAndAcceptanceFlowAfterRestyling()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var input = new AutoCompleteBox { Width = 300, ItemsSource = new[] { "apple", "apricot", "banana" }, MinimumPrefixLength = 1, Text = "ap" };
        var root = new StackPanel(); root.Children.Add(input);
        Css.SetStyle(input, "text-emphasis:dot;text-decoration:underline;text-shadow:1px 2px 0 currentColor;");
        Layout(root); root.Render(new Capture());
        Assert.Equal("ap", input.Text); Assert.True(input.IsDropDownOpen); Assert.Equal(2, input.FilteredItems.Count);
        input.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Tab, ModifierKeys.None, true, false, 0));
        input.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "\t", 1));
        Assert.Equal("apple", input.Text); Assert.False(input.IsDropDownOpen);
    }

    [Fact]
    public void NumericFormattingRemainsTheSourceForThePainter()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var input = new NumberBox { Value = 12.5, Width = 300 };
        var root = new StackPanel(); root.Children.Add(input); var before = input.Text;
        Css.SetStyle(input, "text-emphasis:dot;text-decoration:underline;text-shadow:1px 2px 0 currentColor;");
        Layout(root); root.Render(new Capture());
        Assert.Equal(12.5, input.Value); Assert.Equal(before, input.Text);
    }

    private static (StackPanel Root, TextBoxBase Input, Func<string> Read) Scene(bool number, bool hosted)
    {
        TextBoxBase input; Func<string> read;
        if (number) { var value = new NumberBox { Value = 12345, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden }; input = value; read = () => value.Text; }
        else { var value = new AutoCompleteBox { Text = "A e\u0301 、😀" }; input = value; read = () => value.Text; }
        input.Width = 300; input.Height = 96; input.FontSize = 20; input.FontFamily = new FontFamily("Georgia");
        input.Background = null; input.BorderThickness = default; input.Padding = default;
        if (hosted)
        {
            var template = new ControlTemplate(input.GetType());
            template.SetVisualTree(() => new Border { Name = "PART_ContentHost" });
            input.Template = template;
        }
        var root = new StackPanel { Width = 300, Height = 200 }; root.Children.Add(input);
        Css.SetStyle(input, "padding:0;border:0;"); Layout(root);
        return (root, input, read);
    }

    private static void Layout(FrameworkElement root)
    {
        for (var pass = 0; pass < 6; pass++)
        { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(300, 200)); root.Arrange(new(0, 0, 300, 200)); root.UpdateLayout(); }
    }

    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<(string Text, double Size, Color Color, Point Point)> Text = [];
        internal readonly List<(Color Color, Point Start, Point End)> Lines = [];
        internal readonly List<(Color Color, Rect Bounds)> Rectangles = [];
        public override void DrawText(FormattedText text, Point point) { if (text.Foreground is SolidColorBrush ink) Text.Add((text.Text, text.FontSize, ink.Color, new Point(point.X + Offset.X, point.Y + Offset.Y))); }
        public override void DrawLine(Pen pen, Point a, Point b) { if (pen.Brush is SolidColorBrush ink) Lines.Add((ink.Color, a, b)); }
        public override void DrawRectangle(Brush? b, Pen? p, Rect r) { if (b is SolidColorBrush ink) Rectangles.Add((ink.Color, r)); }
        public override void DrawGeometry(Brush? b, Pen? p, Geometry g) { if (b is SolidColorBrush ink) Rectangles.Add((ink.Color, g.Bounds)); }
        public override void DrawRoundedRectangle(Brush? b, Pen? p, Rect r, double x, double y) { }
        public override void DrawEllipse(Brush? b, Pen? p, Point q, double x, double y) { }
        public override void DrawImage(ImageSource i, Rect r) { }
        public override void DrawBackdropEffect(Rect r, IBackdropEffect e, CornerRadius c) { }
        public override void PushClip(Geometry g) { }
        public override void PushTransform(Transform t) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
