using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssThemeCurrentColorTests
{
    private const string Effects = "text-emphasis:dot;caret-color:currentColor;text-decoration:underline currentColor;text-shadow:1px 2px 0 currentColor;";

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void EffectsFollowUsedInkAcrossThemeReplacementAndBrushMutation(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(kind);
        var before = read();
        foreach (var color in new[] { Color.FromRgb(32, 83, 122), Color.FromArgb(128, 20, 99, 66) })
        {
            root.Resources["TextPrimary"] = new SolidColorBrush(color);
            Layout(root);
            AssertInk(root, input, kind, kind == "password" ? Colors.White : color);
        }
        var mutable = Assert.IsType<SolidColorBrush>(root.Resources["TextPrimary"]);
        mutable.Color = Color.FromRgb(102, 45, 112);
        Layout(root);
        AssertInk(root, input, kind, kind == "password" ? Colors.White : mutable.Color);
        Assert.Equal(before, read());
        Assert.False(input.HasLocalValue(Control.ForegroundProperty));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void CssAndNativeOverridesKeepTheirPriorityAcrossReparenting(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, read) = Scene(kind);
        var before = read();
        var green = Color.FromRgb(20, 99, 66);
        var orange = Color.FromRgb(137, 65, 18);
        Css.SetStyle(input, Effects + "color:#146342!important;");
        Layout(root);
        AssertInk(root, input, kind, green);
        input.Foreground = new SolidColorBrush(orange);
        Layout(root);
        AssertInk(root, input, kind, orange);
        input.ClearValue(Control.ForegroundProperty);
        Layout(root);
        AssertInk(root, input, kind, green);
        Css.SetStyle(input, Effects);
        root.Children.Remove(input);
        var destination = new StackPanel { Width = 312, Height = 120 };
        var purple = Color.FromRgb(102, 45, 112);
        destination.Resources["TextPrimary"] = new SolidColorBrush(purple);
        destination.Children.Add(input);
        Layout(destination);
        AssertInk(destination, input, kind, kind == "password" ? Colors.White : purple);
        root.Resources["TextPrimary"] = new SolidColorBrush(Colors.Red);
        Layout(destination);
        AssertInk(destination, input, kind, kind == "password" ? Colors.White : purple);
        Assert.Equal(before, read());
    }

    [Theory]
    [InlineData("document")]
    [InlineData("paragraph")]
    [InlineData("run")]
    public void ExplicitInheritedColorUsesTheNativeDocumentHostsInk(string scope)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, read) = Scene("rich-shared");
        var input = Assert.IsType<RichTextBox>(control);
        var paragraph = Assert.Single(input.Document.Blocks.OfType<Paragraph>());
        var run = Assert.Single(paragraph.Inlines.OfType<Run>());
        DependencyObject source = scope == "document" ? input.Document : scope == "paragraph" ? paragraph : run;
        Css.SetStyle(source, "color:inherit;" + (scope == "paragraph" ? "display:flex;" : ""));
        var before = read();
        foreach (var color in new[] { Color.FromRgb(32, 83, 122), Color.FromRgb(102, 45, 112) })
        {
            root.Resources["TextPrimary"] = new SolidColorBrush(color);
            Layout(root);
            Assert.Equal(color, Assert.IsType<SolidColorBrush>(source.GetValue(TextElement.ForegroundProperty)).Color);
            Assert.Equal(color, Body(root).Color);
            Assert.False(source.HasLocalValue(TextElement.ForegroundProperty));
        }
        Assert.Equal(before, read());
        Assert.Same(paragraph, run.Parent);
        Assert.Same(input, input.Document.Parent);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void HighlightCurrentColorKeepsTheUnderlyingNativeInk(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene(kind);
        Css.SetClass(input, "subject");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::selection{color:currentColor;background:currentColor;text-decoration:underline currentColor;text-shadow:1px 1px 0 currentColor;}"));
        foreach (var color in new[] { Color.FromRgb(32, 83, 122), Color.FromRgb(20, 99, 66) })
        {
            root.Resources["TextPrimary"] = new SolidColorBrush(color);
            Layout(root);
            var body = Body(root);
            var expected = kind == "password" ? Colors.White : color;
            Assert.Equal(expected, body.Color);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(CssSelection.SourceForeground(CssNode.Get(input))).Color);
            var selection = Assert.IsType<CssSelection.Style>(CssSelection.For(input));
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(CssSelection.Background(selection, body)).Color);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(CssSelection.DecorationColor(selection, body)).Color);
        }
    }

    private static void AssertInk(StackPanel root, Control input, string kind, Color expected)
    {
        Assert.Equal(expected, Body(root).Color);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(CssCaretColorProperties.Get(input)).Color);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(CssTextDecorationProperties.Color(input)).Color);
        var shadow = Assert.IsType<DropShadowEffect>(CssTextShadowProperties.Value(input));
        Assert.Equal(Color.FromRgb(expected.R, expected.G, expected.B), shadow.Color);
        Assert.Equal(expected.A / 255.0, shadow.Opacity, 8);
        if (kind is "auto" or "number") return;
        var capture = new Capture();
        root.Render(capture);
        var marks = capture.Text.Where(row => row.Text == "•" && row.Size == 8).ToArray();
        Assert.True(marks.Length > 0, "No expected marks. Text calls: " + string.Join("; ", capture.Text.Select(row => row.Text + " size=" + row.Size + " ink=" + row.Brush.Color)));
        Assert.All(marks, mark => Assert.Equal(expected, mark.Brush.Color));
        Assert.NotEmpty(capture.Lines);
        Assert.All(capture.Lines, brush => Assert.Equal(expected, brush.Color));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("password")]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void OmittedDecorationColorFollowsTheHostsInk(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input, _) = Scene(kind);
        Css.SetStyle(input, "text-decoration-line:underline;");
        foreach (var color in new[] { Color.FromRgb(32, 83, 122), Color.FromRgb(102, 45, 112) })
        {
            root.Resources["TextPrimary"] = new SolidColorBrush(color);
            Layout(root);
            var capture = new Capture();
            root.Render(capture);
            var expected = kind == "password" ? Colors.White : color;
            Assert.Equal(expected, Body(root).Color);
            Assert.NotEmpty(capture.Lines);
            Assert.All(capture.Lines, line => Assert.Equal(expected, line.Color));
        }
    }

    [Theory]
    [InlineData("rich")]
    [InlineData("rich-shared")]
    public void HostDecorationKeepsItsColorWhenARunOverridesTheBodyInk(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, control, _) = Scene(kind);
        var input = Assert.IsType<RichTextBox>(control);
        var paragraph = Assert.Single(input.Document.Blocks.OfType<Paragraph>());
        var run = Assert.Single(paragraph.Inlines.OfType<Run>());
        var green = Color.FromRgb(20, 99, 66);
        run.Foreground = new SolidColorBrush(green);
        Css.SetStyle(input, "text-decoration-line:underline;");
        Layout(root);
        var capture = new Capture();
        root.Render(capture);
        // A native paragraph carries per-span colors independently of the
        // FormattedText fallback brush observed by this adapter. The native
        // bitmap matrix checks that path's actual run pixels.
        if (kind == "rich-shared") Assert.Equal(green, Body(root).Color);
        Assert.Equal(green, Assert.IsType<SolidColorBrush>(run.Foreground).Color);
        Assert.NotEmpty(capture.Lines);
        Assert.All(capture.Lines, line => Assert.Equal(Color.FromRgb(32, 83, 122), line.Color));
        Assert.Same(paragraph, run.Parent);
    }

    private static SolidColorBrush Body(StackPanel root)
    {
        var capture = new Capture();
        root.Render(capture);
        var colors = capture.Text.Where(row => row.Text is "ABCD" or "12345" or "••••")
            .Select(row => row.Brush).Concat(capture.GlyphInk).ToArray();
        Assert.True(colors.Length > 0, "No body ink. Text commands: " + string.Join(", ", capture.Text.Select(row => $"'{row.Text}':{row.Brush.Color}")));
        Assert.All(colors, brush => Assert.Equal(colors[0].Color, brush.Color));
        return colors[0];
    }

    private static (StackPanel Root, Control Input, Func<string> Read) Scene(string kind)
    {
        var root = new StackPanel { Width = 312, Height = 120 };
        root.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(32, 83, 122));
        Control input;
        Func<string> read;
        if (kind == "text") { var value = new TextBox { Text = "ABCD" }; input = value; read = () => value.Text; }
        else if (kind == "auto") { var value = new AutoCompleteBox { Text = "ABCD" }; input = value; read = () => value.Text; }
        else if (kind == "number") { var value = new NumberBox { Value = 12345 }; input = value; read = () => value.Text; }
        else if (kind == "password") { var value = new PasswordBox { Password = "ABCD" }; input = value; read = () => value.Password; }
        else
        {
            var paragraph = new Paragraph(new Run("ABCD"));
            if (kind == "rich-shared") Css.SetStyle(paragraph, "display:flex;");
            var document = new FlowDocument(paragraph) { FontSize = 16, FontFamily = new FontFamily("Georgia") };
            var value = new RichTextBox { Document = document };
            input = value;
            read = () => document.GetText();
        }
        input.Width = 312; input.Height = 80; input.FontSize = 16; input.FontFamily = new FontFamily("Georgia");
        input.Background = null; input.BorderThickness = default; input.Padding = default;
        Css.SetStyle(input, Effects);
        root.Children.Add(input);
        Layout(root);
        return (root, input, read);
    }

    private static void Layout(FrameworkElement root)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(312, 120)); root.Arrange(new(0, 0, 312, 120)); root.UpdateLayout();
        }
    }

    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<(string Text, double Size, SolidColorBrush Brush)> Text = [];
        internal readonly List<SolidColorBrush> Lines = [];
        internal readonly List<SolidColorBrush> GlyphInk = [];
        public override void DrawText(FormattedText text, Point point) { if (text.Foreground is SolidColorBrush brush) Text.Add((text.Text, text.FontSize, brush)); }
        public override void DrawGlyphRun(Brush? foreground, GlyphRun run) { if (foreground is SolidColorBrush brush) GlyphInk.Add(brush); }
        public override void DrawLine(Pen pen, Point a, Point b) { if (pen.Brush is SolidColorBrush brush) Lines.Add(brush); }
        public override void DrawRectangle(Brush? b, Pen? p, Rect r) { }
        public override void DrawGeometry(Brush? b, Pen? p, Geometry g) { }
        public override void DrawRoundedRectangle(Brush? b, Pen? p, Rect r, double x, double y) { }
        public override void DrawEllipse(Brush? b, Pen? p, Point q, double x, double y) { }
        public override void DrawImage(ImageSource i, Rect r) { }
        public override void DrawBackdropEffect(Rect r, IBackdropEffect e, CornerRadius c) { }
        public override void PushClip(Geometry g) { }
        public override void PushTransform(Transform t) { }
        public override void PushOpacity(double d) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
