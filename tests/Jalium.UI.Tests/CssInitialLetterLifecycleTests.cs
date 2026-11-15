using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssInitialLetterLifecycleTests
{
    private const string RootStyle = "display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;";
    private const string HeadStyle = "display:inline;initial-letter:3;color:green;";
    private const string InitialText = "Alpha beta eta";
    private const string TailText = "continues with ordinary text across the remaining lines.";

    public static TheoryData<string, string> EligibilityChanges => new()
    {
        { "text", "display:block;" }, { "border", "display:block;" },
        { "text", "position:absolute;left:0;top:0;width:240px;" }, { "border", "position:absolute;left:0;top:0;width:240px;" },
        { "text", "position:relative;left:4px;" }, { "border", "position:relative;left:4px;" },
        { "text", "float:left;width:240px;" }, { "border", "float:left;width:240px;" },
        { "text", "float:right;width:240px;" }, { "border", "float:right;width:240px;" },
        { "text", "display:contents;" }, { "border", "display:contents;" },
        { "text", "display:none;" }, { "border", "display:none;" },
        { "text", "visibility:hidden;" }, { "border", "visibility:hidden;" },
        { "text", "display:inline-block;" }, { "border", "display:inline-block;" },
        { "text", "display:inline-flex;" }, { "border", "display:inline-flex;" },
        { "text", "initial-letter:normal;" }, { "border", "initial-letter:normal;" },
        { "text", "position:static;" }, { "border", "position:static;" },
    };

    private sealed record Tree(StackPanel Root, FrameworkElement Head, TextBlock Text, TextBlock Tail);

    private static Tree Create(string host, string state = "")
    {
        var text = new TextBlock { Text = InitialText, IsTextSelectionEnabled = true };
        FrameworkElement head = host == "border" ? new Border { Child = text } : text;
        if (!ReferenceEquals(head, text)) Css.SetStyle(text, "display:inline;");
        var tail = new TextBlock { Text = TailText }; Css.SetStyle(tail, "display:inline;");
        var root = new StackPanel(); root.Children.Add(head); root.Children.Add(tail);
        Css.SetStyle(root, RootStyle); Css.SetStyle(head, HeadStyle + state);
        return new(root, head, text, tail);
    }

    private static void Layout(FrameworkElement root, int width = 260)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(width, 900)); root.Arrange(new(0, 0, width, 900)); root.UpdateLayout();
        }
    }

    [Theory]
    [MemberData(nameof(EligibilityChanges))]
    public void EligibilityChangesMatchAFreshTreeAndCanBeRepeated(string host, string state)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Create(host); Layout(actual.Root);
        Assert.Contains(Capture(actual.Root), paint => paint.Color == Colors.Green && paint.Size > 60);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            Css.SetStyle(actual.Head, HeadStyle + state); Layout(actual.Root);
            var expected = Create(host, state); Layout(expected.Root);
            EqualPaints(expected.Root, actual.Root, host + ":" + state);
            Assert.Equal(CssInitialLetterProperties.Metrics(expected.Head), CssInitialLetterProperties.Metrics(actual.Head));
            Assert.Equal(expected.Head.IsCssInlineFrame, actual.Head.IsCssInlineFrame);
            if (CssDisplayProperties.SuppressesBoxTree(actual.Head))
            {
                // A removed CSS box has no allocation or hit target. Native
                // RenderSize can retain its last arrange until it is shown.
                Assert.Null(actual.Head.CssInlineBox);
                Assert.Null(actual.Head.InputHitTest(new(10, 10)));
            }
            else
            {
                Assert.Equal(expected.Head.RenderSize.Width, actual.Head.RenderSize.Width, 3);
                Assert.Equal(expected.Head.RenderSize.Height, actual.Head.RenderSize.Height, 3);
            }
            OriginalContent(actual);

            Css.SetStyle(actual.Head, HeadStyle); Layout(actual.Root);
            var restored = Create(host); Layout(restored.Root);
            EqualPaints(restored.Root, actual.Root, "restored " + host + ":" + state);
            Assert.Contains(Capture(actual.Root), paint => paint.Color == Colors.Green && paint.Size > 60);
            OriginalContent(actual);
        }
    }

    [Theory]
    [InlineData("text", false)] [InlineData("border", false)]
    [InlineData("text", true)] [InlineData("border", true)]
    public void MovingTheInitialToASecondSiblingRetiresItsUsedState(string host, bool otherFont)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Create(host); Layout(actual.Root);
        var other = new StackPanel(); Css.SetStyle(other, RootStyle + (otherFont ? "font-family:Arial;font-size:20px;line-height:30px;" : ""));
        var prefix = new TextBlock { Text = "Before ", Foreground = new SolidColorBrush(Colors.Black) };
        Css.SetStyle(prefix, "display:inline;"); other.Children.Add(prefix);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            actual.Root.Children.Remove(actual.Head); other.Children.Add(actual.Head); Layout(actual.Root); Layout(other);
            var capture = Capture(other);
            Assert.All(capture.Where(paint => paint.Color == Colors.Green), paint => Assert.Equal(otherFont ? 20 : 16, paint.Size));
            Assert.Null(CssInitialLetterProperties.Metrics(actual.Head)); OriginalContent(actual, otherFont ? 20 : 16);
            other.Children.Remove(actual.Head); actual.Root.Children.Insert(0, actual.Head); Layout(actual.Root);
            Assert.Contains(Capture(actual.Root), paint => paint.Color == Colors.Green && paint.Size > 60);
            OriginalContent(actual);
        }
    }

    [Theory]
    [InlineData("text")] [InlineData("border")]
    public void LiveFontsAndContentRemainEquivalentToFreshStyles(string host)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Create(host); Layout(actual.Root);
        foreach (var size in new[] { 24, 12, 24, 16 })
        {
            var state = "font-size:" + size + "px;";
            Css.SetStyle(actual.Head, HeadStyle + state); Layout(actual.Root);
            var expected = Create(host, state); Layout(expected.Root);
            EqualPaints(expected.Root, actual.Root, host + ":" + state);
            Assert.Equal(size, actual.Text.FontSize);
        }
        foreach (var content in new[] { "j", "A small phrase", InitialText })
        {
            actual.Text.Text = content; Layout(actual.Root);
            var expected = Create(host); expected.Text.Text = content; Layout(expected.Root);
            EqualPaints(expected.Root, actual.Root, host + ":" + content);
            actual.Text.SelectAll(); Assert.Equal(content, actual.Text.SelectedText); actual.Text.Select(0, 0);
        }
        Css.SetStyle(actual.Head, HeadStyle); Layout(actual.Root);
        foreach (var size in new[] { 20, 12, 16 })
        {
            var style = RootStyle + "font-size:" + size + "px;line-height:" + size * 1.5 + "px;";
            Css.SetStyle(actual.Root, style); Layout(actual.Root);
            var expected = Create(host); Css.SetStyle(expected.Root, style); Layout(expected.Root);
            EqualPaints(expected.Root, actual.Root, "inherited font " + size);
            Assert.Equal(size, actual.Text.FontSize);
        }
        OriginalContent(actual);
    }

    [Theory]
    [InlineData("display:inline-block;")]
    [InlineData("position:absolute;left:180px;top:0;")]
    [InlineData("float:left;")]
    [InlineData("display:none;")]
    public void DescendantsLeavingTheInitialsInlineContentRetireTheirPrivateFonts(string state)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Create("border"); Layout(actual.Root);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            Css.SetStyle(actual.Text, "display:inline;" + state); Layout(actual.Root);
            var expected = Create("border"); Css.SetStyle(expected.Text, "display:inline;" + state); Layout(expected.Root);
            EqualPaints(expected.Root, actual.Root, "descendant " + state);
            Css.SetStyle(actual.Text, "display:inline;"); Layout(actual.Root);
            var restored = Create("border"); Layout(restored.Root);
            EqualPaints(restored.Root, actual.Root, "restored descendant " + state);
            OriginalContent(actual);
        }
    }

    [Fact]
    public void AGeneratedInitialKeepsItsTextAndRestoresAfterItsStyleIsDisabled()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { Text = TailText, IsTextSelectionEnabled = true };
        Css.SetClass(text, "article");
        Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(".article{" + RootStyle + "}" +
            ".article::before{content:'Alpha beta eta';display:inline;initial-letter:3;color:green;}" +
            ".article.off::before{initial-letter:normal;}"));
        foreach (var enabled in new[] { true, false, true, false })
        {
            Css.SetClass(text, enabled ? "article" : "article off"); Layout(text);
            var paints = Capture(text); var initial = paints.Where(paint => paint.Color == Colors.Green).ToArray();
            Assert.Equal(InitialText.Replace(" ", ""), string.Concat(initial.Select(paint => paint.Text)).Replace(" ", ""));
            Assert.All(initial, paint => Assert.Equal(enabled, paint.Size > 60));
            Assert.Equal(TailText, text.Text); text.SelectAll(); Assert.Equal(TailText, text.SelectedText); text.Select(0, 0);
            Assert.True(text.IsMeasureValid); Assert.True(text.IsArrangeValid);
        }
    }

    private static void OriginalContent(Tree tree, double fontSize = 16)
    {
        Assert.Equal(InitialText, tree.Text.Text); Assert.Equal(fontSize, tree.Text.FontSize);
        tree.Text.SelectAll(); Assert.Equal(InitialText, tree.Text.SelectedText);
        Assert.Equal(InitialText, tree.Text.GetAutomationPeer()!.GetName()); tree.Text.Select(0, 0);
        Assert.Equal(TailText, tree.Tail.Text);
        Assert.True(tree.Root.IsMeasureValid); Assert.True(tree.Root.IsArrangeValid);
    }

    private sealed record Paint(string Text, double Size, double X, double Baseline, Color Color);
    private static List<Paint> Capture(FrameworkElement root)
    { var capture = new TextCapture(); root.Render(capture); return capture.Paints; }

    private static void EqualPaints(FrameworkElement expected, FrameworkElement actual, string state)
    {
        var a = Capture(expected); var b = Capture(actual);
        Assert.True(a.Count == b.Count, state + $": paint count {b.Count} != fresh {a.Count}");
        for (var index = 0; index < a.Count; index++)
        {
            Assert.Equal(a[index].Text, b[index].Text); Assert.Equal(a[index].Color, b[index].Color);
            Assert.True(Math.Abs(a[index].Size - b[index].Size) < .001, state + $": paint {index} font {b[index].Size} != fresh {a[index].Size}");
            Assert.Equal(a[index].X, b[index].X, 3); Assert.Equal(a[index].Baseline, b[index].Baseline, 3);
        }
    }

    private sealed class TextCapture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<Paint> Paints = [];
        public override void DrawText(FormattedText text, Point point)
        {
            if (!text.IsMeasured && text.PlatformTextLine is null) TextMeasurement.MeasureText(text);
            Paints.Add(new(text.Text, text.FontSize, point.X + Offset.X, point.Y + Offset.Y + text.Baseline,
                text.Foreground is SolidColorBrush brush ? brush.Color : Colors.Transparent));
        }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }
}
