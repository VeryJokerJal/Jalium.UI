using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssInitialLetterBlockFlowTests
{
    private const string Body = "display:block;font-family:Georgia;font-size:16px;line-height:24px;";
    private const string Following = "Following paragraphs continue around the initial letter until its bottom. " +
        "Ordinary text then returns to the full available width and keeps its original reading order.";

    private sealed record Tree(StackPanel Root, FrameworkElement First, TextBlock Initial, StackPanel Next, TextBlock Text);

    private static Tree Create(bool pseudo, bool definite, string nextStyle = "", bool rtl = false)
    {
        var initial = new TextBlock { Text = "A", IsTextSelectionEnabled = true };
        FrameworkElement first;
        if (pseudo)
        {
            initial.Text = "A brief"; Css.SetClass(initial, "first");
            Css.GetStyleSheets(initial).Add(CssStyleSheet.Parse(".first{" + Body + "color:darkred;" +
                (definite ? "height:24px;" : "") + "}.first::first-letter{initial-letter:3;initial-letter-wrap:none;color:green;}" +
                ".first.off::first-letter{initial-letter:normal;}"));
            first = initial;
        }
        else
        {
            var panel = new StackPanel(); first = panel;
            var tail = new TextBlock { Text = "brief" }; Css.SetStyle(tail, "display:inline;color:darkred;");
            panel.Children.Add(initial); panel.Children.Add(tail);
            Css.SetStyle(initial, "display:inline;initial-letter:3;initial-letter-wrap:none;color:green;");
            Css.SetStyle(first, Body + (definite ? "height:24px;" : ""));
        }
        var next = new StackPanel(); Css.SetStyle(next, Body + nextStyle);
        var text = new TextBlock { Text = Following, IsTextSelectionEnabled = true };
        Css.SetStyle(text, "display:inline;color:black;"); next.Children.Add(text);
        var root = new StackPanel(); Css.SetStyle(root, Body); root.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        root.Children.Add(first); root.Children.Add(next);
        return new(root, first, initial, next, text);
    }

    private static void Layout(FrameworkElement root, int width = 312)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(width, 800)); root.Arrange(new(0, 0, width, 800)); root.UpdateLayout();
        }
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void AShortParagraphExportsTheInitialExclusionToFollowingText(bool pseudo, bool definite)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(pseudo, definite); Layout(tree.Root);
        Assert.Equal(24, tree.First.RenderSize.Height, 3);
        Assert.Equal(24, tree.Next.VisualBounds.Y, 3);
        var paints = Capture(tree.Root).Where(p => p.Color == Colors.Black).ToArray();
        Assert.True(paints[0].X > 20, $"Following text ignored initial: x={paints[0].X}");
        Assert.Contains(paints, paint => paint.X < .001 && paint.Baseline > paints[0].Baseline + 24);
        tree.Text.SelectAll(); Assert.Equal(Following, tree.Text.SelectedText);
        Assert.Equal(pseudo ? "A brief" : "A", tree.Initial.Text); Assert.Equal(16, tree.Initial.FontSize);
    }

    [Theory]
    [InlineData(false, "clear:left;", true)] [InlineData(false, "clear:right;", false)]
    [InlineData(false, "clear:both;", true)] [InlineData(false, "clear:inline-start;", true)]
    [InlineData(true, "clear:right;", true)] [InlineData(true, "clear:left;", false)]
    [InlineData(true, "clear:inline-start;", true)] [InlineData(true, "clear:inline-end;", false)]
    [InlineData(false, "display:flow-root;", true)] [InlineData(true, "display:flow-root;", true)]
    public void FollowingBlockClearUsesTheInitialsStartSideAndIndependentFlowClearsIt(bool rtl, string style, bool clears)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(false, true, style, rtl); Layout(tree.Root);
        Assert.True(clears ? tree.Next.VisualBounds.Y > 48 : Math.Abs(tree.Next.VisualBounds.Y - 24) < .001,
            $"rtl={rtl}, {style}, next y={tree.Next.VisualBounds.Y}");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AFollowingInitialClearsThePreviousInkInsideItsOwnBlock(bool pseudo)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(pseudo, false);
        var second = new TextBlock { Text = "B" };
        Css.SetStyle(second, "display:inline;initial-letter:3;initial-letter-wrap:none;color:blue;");
        tree.Next.Children.Insert(0, second);
        Css.SetStyle(tree.Next, Body + "border:1px solid gray;");
        Layout(tree.Root);
        Assert.Equal(24, tree.Next.VisualBounds.Y, 3);
        var paints = Capture(tree.Root);
        var prior = Assert.Single(paints.Where(paint => paint.Color == Colors.Green && paint.Size > 60));
        var next = Assert.Single(paints.Where(paint => paint.Color == Colors.Blue && paint.Size > 60));
        var cap = TextMeasurement.GetFontUnitMetrics("Georgia", next.Size).CapHeight;
        Assert.True(next.Baseline - cap >= prior.Baseline - .001, "The following initial overlaps the previous ink");
        Assert.True(paints.First(paint => paint.Color == Colors.Black).Baseline > prior.Baseline);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedBlocksAndEmptySiblingsPreserveTheExclusionCoordinates(bool pseudo)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(pseudo, false);
        tree.Root.Children.Remove(tree.First);
        var wrapper = new StackPanel(); wrapper.Children.Add(tree.First);
        Css.SetStyle(wrapper, "display:block;margin-left:12px;padding-left:8px;padding-top:5px;border-left:2px solid gray;");
        var empty = new StackPanel(); Css.SetStyle(empty, "display:block;height:0;margin-bottom:3px;");
        tree.Root.Children.Insert(0, wrapper); tree.Root.Children.Insert(1, empty);
        Layout(tree.Root);
        Assert.Equal(29, wrapper.RenderSize.Height, 3);
        Assert.Equal(32, tree.Next.VisualBounds.Y, 3);
        var paints = Capture(tree.Root).Where(paint => paint.Color == Colors.Black).ToArray();
        Assert.True(paints[0].X > 40, $"Nested initial was lost: x={paints[0].X}");
        Assert.Contains(paints, paint => paint.X < .001 && paint.Baseline > paints[0].Baseline + 24);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void RepeatedStyleChangesAndResizeMatchFreshTrees(bool pseudo, bool definite)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(pseudo, definite); Layout(tree.Root);
        foreach (var width in new[] { 312, 500, 312 })
            foreach (var state in new[] { "normal", "hidden", "initial" })
            {
                Apply(tree, state); Layout(tree.Root, width);
                var fresh = Create(pseudo, definite); Apply(fresh, state); Layout(fresh.Root, width);
                var actual = Capture(tree.Root); var expected = Capture(fresh.Root);
                Assert.Equal(expected.Count, actual.Count);
                for (var index = 0; index < expected.Count; index++)
                {
                    Assert.Equal(expected[index].Text, actual[index].Text); Assert.Equal(expected[index].Color, actual[index].Color);
                    Assert.Equal(expected[index].Size, actual[index].Size, 3);
                    Assert.Equal(expected[index].X, actual[index].X, 3); Assert.Equal(expected[index].Baseline, actual[index].Baseline, 3);
                }
                Assert.Equal(fresh.Next.VisualBounds.Y, tree.Next.VisualBounds.Y, 3);
                Assert.Equal(pseudo ? "A brief" : "A", tree.Initial.Text); Assert.Equal(16, tree.Initial.FontSize);
            }
        void Apply(Tree target, string state)
        {
            if (pseudo)
            {
                Css.SetClass(target.Initial, state == "normal" ? "first off" : "first");
                Css.SetStyle(target.First, state == "hidden" ? "display:none;" : "");
            }
            else
            {
                Css.SetStyle(target.Initial, "display:inline;initial-letter:" + (state == "normal" ? "normal" : "3") + ";initial-letter-wrap:none;color:green;");
                Css.SetStyle(target.First, Body + (definite ? "height:24px;" : "") + (state == "hidden" ? "display:none;" : ""));
            }
        }
    }

    [Fact]
    public void FloatClearanceDoesNotTreatAnInitialAsAnOrdinaryFloat()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(false, false);
        var floating = new Border(); Css.SetStyle(floating, "float:left;clear:left;width:14px;height:12px;background:blue;");
        ((StackPanel)tree.First).Children.Add(floating); Layout(tree.Root);
        Assert.True(floating.VisualBounds.Y < 24, $"clear on a float incorrectly cleared an initial: y={floating.VisualBounds.Y}");
    }

    [Fact]
    public void TheFormattingContextStillContainsTheExportedInitialInk()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var tree = Create(false, false); tree.Root.Children.Remove(tree.Next); Layout(tree.Root);
        Assert.Equal(24, tree.First.RenderSize.Height, 3);
        Assert.True(tree.Root.DesiredSize.Height > 60);
    }

    [Fact]
    public void DiscoveringAChildInitialPreservesItsFirstLineProjection()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); Css.SetStyle(root, Body + "color:black;");
        var text = new TextBlock { Text = Following, IsTextSelectionEnabled = true };
        Css.SetClass(text, "first-line-initial"); root.Children.Add(text);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".first-line-initial{display:block;}" +
            ".first-line-initial::first-line{font-family:Arial;font-size:22px;line-height:32px;}" +
            ".first-line-initial::first-letter{initial-letter:3;color:green;}"));
        Layout(root);
        var paints = Capture(root); var initial = paints.First(paint => paint.Color == Colors.Green);
        var ordinary = paints.First(paint => paint.Color != Colors.Green);
        var cap = TextMeasurement.GetFontUnitMetrics("Arial", 22).CapHeight;
        Assert.Equal((48 + cap) / cap * 22, initial.Size, 3);
        Assert.Equal(48, initial.Baseline - ordinary.Baseline, 3);
        Assert.Equal(22, ordinary.Size);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APropertyRegistrationInAnotherTreeDoesNotRestartTheInitialFirstLine(bool removeRegistration)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var registration = new StackPanel();
        Css.SetClass(registration, "qa-registration");
        var sheets = Css.GetStyleSheets(registration);
        var sheet = CssStyleSheet.Parse("@property --qa-initial-first-line {syntax:\"<length>\";inherits:true;initial-value:0px;}" +
            ".qa-registration {--qa-initial-first-line:1px;}");
        Assert.Empty(sheet.Diagnostics);
        sheets.Add(sheet);
        Layout(registration);
        Assert.True(CssRegisteredProperties.For(registration).ContainsKey("--qa-initial-first-line"));
        if (removeRegistration)
        {
            sheets.Clear();
            Layout(registration);
            Assert.False(CssRegisteredProperties.For(registration).ContainsKey("--qa-initial-first-line"));
        }
        // Keep the original six-pass geometry and font assertions intact.
        DiscoveringAChildInitialPreservesItsFirstLineProjection();
        GC.KeepAlive(registration);
    }

    private sealed record Paint(string Text, double Size, double X, double Baseline, Color Color);
    private static List<Paint> Capture(FrameworkElement root)
    { var capture = new TextCapture(); root.Render(capture); return capture.Paints; }

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
