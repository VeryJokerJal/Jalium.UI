using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSelectionTests
{
    private const string Source = "Selection stays tied to its original source.";
    private static readonly Color Blue = Color.FromRgb(0x20, 0x53, 0x7a);
    private static readonly Color Cream = Color.FromRgb(0xff, 0xf8, 0xde);
    private static readonly Color Ordinary = Color.FromRgb(0x23, 0x3c, 0x32);
    private static (StackPanel Root, TextBlock Text) Tree(string rules)
    {
        var root = new StackPanel(); Css.SetClass(root, "parent");
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        var text = new TextBlock { Text = Source, IsTextSelectionEnabled = true };
        Css.SetClass(text, "subject"); Css.SetStyle(text, "display:block;"); root.Children.Add(text);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(rules)); Layout(root); return (root, text);
    }
    private static void Layout(FrameworkElement root)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(312, 240)); root.Arrange(new(0, 0, 312, 240)); root.UpdateLayout();
        }
    }
    private static Capture Paint(TextBlock text) { var result = new Capture(); text.Render(result); return result; }

    [Theory]
    [InlineData("TextBlock::selection{color:red;}")]
    [InlineData("::selection{background-color:blue;}")]
    [InlineData(".parent > .subject::selection{color:var(--selection);}")]
    [InlineData("@supports selector(::selection){TextBlock{color:green;}}")]
    public void SelectionSelectorsAreParsed(string css) => Assert.Empty(CssStyleSheet.Parse(css).Diagnostics);

    [Fact]
    public void SelectionPaintUsesBothColorsWithoutChangingTheOriginalForeground()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, text) = Tree(".subject::selection{color:#fff8de;background-color:#20537a;}");
        text.SelectAll(); var paint = Paint(text);
        Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
        Assert.Equal(Ordinary, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
        Assert.Equal(Source, text.Text); Assert.Equal(Source, text.SelectedText); Assert.Equal(16, text.FontSize);
        text.Select(0, 0); Layout(root); paint = Paint(text);
        Assert.DoesNotContain(Blue, paint.Fills); Assert.DoesNotContain(Cream, paint.TextColors); Assert.Contains(Ordinary, paint.TextColors);
    }
    [Fact]
    public void BackgroundInheritsFromTheParentsHighlightWhileTheChildOverridesColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".parent::selection{color:green;background-color:#20537a;} .subject::selection{color:#fff8de;}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
    }
    [Fact]
    public void ColorOnlyRemovesThePairedDefaultBackground()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".subject::selection{color:#fff8de;}"); text.SelectAll();
        var paint = Paint(text); Assert.Empty(paint.Fills); Assert.Contains(Cream, paint.TextColors);
    }
    [Fact]
    public void HighlightVariablesComeFromTheOriginatingElement()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".parent{--ink:red;} .subject{--ink:#fff8de;} .parent::selection{--ink:green;background:#20537a;} .subject::selection{color:var(--ink);}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Cream, paint.TextColors); Assert.Contains(Blue, paint.Fills);
    }
    [Theory]
    [InlineData("inherit")] [InlineData("unset")]
    public void BackgroundWideValuesUseTheHighlightParent(string value)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".parent::selection{background:#20537a;color:#fff8de;} .subject::selection{background-color:" + value + ";}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
    }
    [Fact]
    public void InheritedCurrentColorUsesEachSelectedElementsOrdinaryColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".parent::selection{color:currentColor;background:#20537a;} .subject{color:#fff8de;}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Cream, paint.TextColors); Assert.Contains(Blue, paint.Fills);
    }
    [Fact]
    public void SelectionPropertiesCannotChangeNormalLayoutOrContent()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, text) = Tree(".subject::selection{color:#fff8de;background:#20537a;font-size:90px;width:1px;height:1px;display:none;content:'changed';}");
        var size = text.RenderSize; text.SelectAll(); Paint(text); Layout(root);
        Assert.Equal(Source, text.Text); Assert.Equal(Source, text.SelectedText); Assert.Equal(16, text.FontSize);
        Assert.Equal(size, text.RenderSize); Assert.Equal(312, text.RenderSize.Width);
    }
    [Fact]
    public void RemovingSelectionRulesRestoresNativeSelectionWithoutMovingTheRange()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, text) = Tree(".subject::selection{color:#fff8de;background:#20537a;}");
        text.Select(2, 8); Assert.Contains(Blue, Paint(text).Fills);
        Css.GetStyleSheets(root).Clear(); Layout(root); var paint = Paint(text);
        Assert.DoesNotContain(Blue, paint.Fills); Assert.DoesNotContain(Cream, paint.TextColors);
        Assert.Equal(Source.Substring(2, 8), text.SelectedText); Assert.Equal(Source, text.Text);
    }
    [Fact]
    public void UniversalSelectionOverridesInheritedBackgroundOnEveryOrigin()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".parent::selection{background:#20537a;} ::selection{background:red;color:#fff8de;}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Colors.Red, paint.Fills); Assert.DoesNotContain(Blue, paint.Fills);
    }
    [Theory]
    [InlineData("initial")]
    [InlineData("transparent")]
    public void TransparentAuthorBackgroundSuppressesTheNativeBackground(string value)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree(".parent::selection{background:#20537a;} .subject::selection{background-color:" + value + ";color:#fff8de;}");
        text.SelectAll(); Assert.Empty(Paint(text).Fills);
    }
    [Fact]
    public void LayerRollbackAndScopeKeepTheOriginatingSubject()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree("@layer base,top; @layer base {.subject::selection{background:#20537a;}} @layer top {.subject::selection{background:revert-layer;}} @scope (.parent) {@supports selector(::selection) {.subject::selection{color:#fff8de;}}}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
    }
    [Fact]
    public void NonInheritingRegisteredVariablesStillComeFromTheirOrigin()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_, text) = Tree("@property --ink {syntax:'<color>';inherits:false;initial-value:red;} .subject{--ink:#fff8de;} .parent::selection{--ink:green;background:#20537a;} .subject::selection{color:var(--ink);}");
        text.SelectAll(); var paint = Paint(text); Assert.Contains(Cream, paint.TextColors); Assert.Contains(Blue, paint.Fills);
    }
    [Fact]
    public void OrdinaryColorChangesRefreshSymbolicCurrentColor()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, text) = Tree(".subject::selection{color:currentColor;background-color:currentColor;}"); text.SelectAll();
        Assert.Contains(Ordinary, Paint(text).Fills);
        Css.SetStyle(text, "display:block;color:#fff8de;"); Layout(root); var paint = Paint(text);
        Assert.Contains(Cream, paint.Fills); Assert.Contains(Cream, paint.TextColors); Assert.DoesNotContain(Ordinary, paint.Fills);
    }
    [Fact]
    public void ReparentingRefreshesHighlightInheritanceWithoutChangingTheSelection()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (first, text) = Tree(".parent::selection{color:#fff8de;background:#20537a;}"); text.Select(2,8); Assert.Contains(Blue,Paint(text).Fills);
        first.Children.Remove(text); var next = new StackPanel(); next.Children.Add(text);
        Css.GetStyleSheets(next).Add(CssStyleSheet.Parse("StackPanel::selection{color:#fff8de;background:red;}")); Layout(next);
        var paint = Paint(text); Assert.Contains(Colors.Red,paint.Fills); Assert.DoesNotContain(Blue,paint.Fills);
        Assert.Equal(Source.Substring(2,8),text.SelectedText); Assert.Equal(Source,text.Text);
    }
    [Fact]
    public void SingleColonSelectionIsRejectedBySupports()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (_,text)=Tree("@supports selector(:selection) {.subject{color:red;}} @supports selector(::selection) {.subject::selection{color:#fff8de;}}");
        text.SelectAll(); var paint=Paint(text); Assert.Contains(Cream,paint.TextColors); Assert.Equal(Ordinary,Assert.IsType<SolidColorBrush>(text.Foreground).Color);
    }

    [Fact]
    public void RegisteredPropertiesDoNotRestartAnUnrelatedFirstLineAllocation()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var registration = new StackPanel();
        Css.GetStyleSheets(registration).Add(CssStyleSheet.Parse("@property --paint {syntax:'<color>';inherits:false;initial-value:red;}"));
        Layout(registration);
        var text = new TextBlock { Text = "Alpha\nGamma" }; var root = new StackPanel(); root.Children.Add(text);
        Css.SetStyle(text, "display:block;white-space:pre-line;font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("TextBlock::first-line{font-size:28px;color:red;}"));
        Layout(root); var paint = Paint(text);
        Assert.Contains(28d, paint.FontSizes); Assert.Contains(16d, paint.FontSizes);
        Assert.Equal("Alpha\nGamma", text.Text); Assert.True(root.IsArrangeValid);
        Layout(root); Assert.Contains(16d, Paint(text).FontSizes);
    }

    private static Capture PaintControl(UIElement control) { var capture = new Capture(); control.Render(capture); return capture; }

    [Theory, InlineData(false), InlineData(true)]
    public void TextEditorsUseAuthorColorsAndRetainTheirSource(bool password)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel();
        FrameworkElement editor = password
            ? new PasswordBox { Password = "Private office", IsInactiveSelectionHighlightEnabled = true }
            : new TextBox { Text = "Public office", IsInactiveSelectionHighlightEnabled = true };
        Css.SetClass(editor, "editor"); root.Children.Add(editor);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".editor::selection{color:#fff8de;background-color:#20537a;}"));
        Layout(root);
        if (editor is PasswordBox secret) secret.Select(0, 7); else ((TextBox)editor).Select(0, 7);
        var paint = PaintControl(editor); Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
        if (editor is PasswordBox masked)
        {
            Assert.Equal("Private office", masked.Password);
            Assert.DoesNotContain(paint.Text, value => value.Contains("Private", StringComparison.Ordinal));
        }
        else Assert.Equal("Public office", ((TextBox)editor).Text);
    }
    [Fact]
    public void TextBoxSelectionTextBrushIsPaintedWithoutCss()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var editor = new TextBox { Text = Source, SelectionTextBrush = new SolidColorBrush(Cream), IsInactiveSelectionHighlightEnabled = true };
        Layout(editor); editor.Select(1, 8); Assert.Contains(Cream, PaintControl(editor).TextColors);
        Assert.Equal(Source.Substring(1, 8), editor.SelectedText);
    }
    [Fact]
    public void AnInactiveEditorDoesNotExposeItsHighlightWhenDisabled()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var editor = new TextBox { Text = Source, IsInactiveSelectionHighlightEnabled = false };
        Css.GetStyleSheets(editor).Add(CssStyleSheet.Parse("TextBox::selection{color:#fff8de;background:#20537a;}"));
        Layout(editor); editor.SelectAll(); var paint = PaintControl(editor);
        Assert.DoesNotContain(Blue, paint.Fills); Assert.DoesNotContain(Cream, paint.TextColors);
    }
    [Theory, InlineData(false), InlineData(true)]
    public void RichTextBothLayoutPathsUseTheOriginalRunHighlight(bool shared)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var run = new Run(Source); var paragraph = new Paragraph(run); var document = new FlowDocument(paragraph);
        var editor = new RichTextBox { Document = document, IsReadOnly = true };
        if (shared) Css.SetStyle(paragraph, "display:flex;");
        Css.GetStyleSheets(editor).Add(CssStyleSheet.Parse("RichTextBox::selection{background:#20537a;} Run::selection{color:#fff8de;}"));
        Layout(editor); editor.Selection.Select(run.ContentStart, run.ContentEnd); Layout(editor);
        var paint = PaintControl(editor); Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
        Assert.Same(run, Assert.Single(paragraph.Inlines)); Assert.Same(paragraph, run.Parent);
        Assert.Equal(Source + "\n", document.GetText()); Assert.Equal(Source, editor.Selection.Text);
    }
    [Fact]
    public void SelectionIgnoresFontSizePaddingAndDisplay()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, text) = Tree(".subject::selection{font-size:99px;padding:80px;display:none;color:#fff8de;background:#20537a;}");
        var size = text.RenderSize; text.SelectAll(); var paint = Paint(text); Layout(root);
        Assert.Contains(Cream, paint.TextColors); Assert.Equal(size, text.RenderSize); Assert.Equal(16, text.FontSize);
        Assert.All(paint.FontSizes, value => Assert.Equal(16, value));
    }
    [Fact]
    public void RunColorsAndParentHighlightRemainIndependentOfOrdinaryRunInheritance()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, text) = Tree(".parent::selection{background:#20537a;} Run::selection{color:#fff8de;}");
        text.Text = ""; var first = new Run("office "); var second = new Run("ligature");
        text.Inlines.Add(first); text.Inlines.Add(second); Layout(root); var parents = (first.Parent, second.Parent); text.SelectAll();
        var paint = Paint(text); Assert.Contains(Blue, paint.Fills); Assert.Contains(Cream, paint.TextColors);
        Assert.Equal("office ligature", text.SelectedText); Assert.Same(parents.Item1, first.Parent); Assert.Same(parents.Item2, second.Parent);
        Assert.Equal(Ordinary, Assert.IsType<SolidColorBrush>(first.Foreground).Color);
    }

    private sealed class Capture : DrawingContextAdapter
    {
        internal readonly List<Color> Fills = [];
        internal readonly List<Color> TextColors = [];
        internal readonly List<string> Text = [];
        internal readonly List<double> FontSizes = [];
        private void Fill(Brush? brush) { if (brush is SolidColorBrush b && b.Color.A > 0) Fills.Add(b.Color); }
        public override void DrawText(FormattedText text, Point point) { Text.Add(text.Text); FontSizes.Add(text.FontSize); if (text.Foreground is SolidColorBrush b) TextColors.Add(b.Color); }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => Fill(brush);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Fill(brush);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => Fill(brush);
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) => Fill(brush);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
