using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTextContentTests
{
    [Theory]
    [InlineData("block")]
    [InlineData("flow-root")]
    [InlineData("flex")]
    [InlineData("grid")]
    [InlineData("")]
    public void GeneratedBoxesAndOriginalTextShareSourceOrderedLayout(string display)
    {
        var text = Text();
        Add(text, $$"""
            .text { {{(display.Length == 0 ? "" : $"display: {display};")}} width: 160px;
                flex-direction: column; align-items: stretch; align-content: start; grid-template-columns: 1fr; }
            .text::before { content: '前置'; display: block; height: 20px; }
            .text::after { content: '尾注'; display: block; height: 12px; }
            """);
        Layout(text);
        var body = Body(text); var slots = CssGeneratedContent.Get(text)!;
        Assert.Same(text, VisualTreeHelper.GetParent(body));
        Assert.Equal(20, body.VisualBounds.Y, 4);
        Assert.Equal(body.VisualBounds.Bottom, slots.After!.VisualBounds.Y, 4);
        Assert.Equal(body.DesiredSize.Height + 32, text.DesiredSize.Height, 4);
        Assert.Equal(new[] { "前置", "原生正文", "尾注" }, Draw(text));
        Assert.Equal("原生正文", text.Text);
        Assert.Empty(CssMatcher.Children(CssNode.Get(text)));
    }

    [Theory]
    [InlineData("block")]
    [InlineData("flex")]
    [InlineData("grid")]
    public void ExplicitDisplayWithoutPseudosRetainsTheOriginalText(string display)
    {
        var text = Text(); Add(text, $".text {{ display: {display}; width: 150px; }}"); Layout(text);
        Assert.Single(Draw(text)); Assert.Equal("原生正文", Draw(text)[0]);
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(text));
        Assert.True(Body(text).ActualHeight > 0);
    }

    [Fact]
    public void PaddingAndBorderBelongToTheOuterBoxOnly()
    {
        var text = Text(); text.Padding = new Thickness(5);
        Add(text, ".text { display: grid; align-content: start; width: 180px; border: 2px solid green; } .text::before { content: ''; height: 10px; }");
        Layout(text); var body = Body(text);
        Assert.Equal(7, body.VisualBounds.X); Assert.Equal(17, body.VisualBounds.Y);
        Assert.Equal(166, body.ActualWidth); Assert.Equal(body.DesiredSize.Height + 24, text.DesiredSize.Height, 4);
        var segment = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        Assert.Equal(7, segment.StartX, 4); Assert.True(segment.Baseline > 17);
    }

    [Fact]
    public void InlineObjectsAndSelectionStayOnTheOriginalSource()
    {
        var text = Text(); text.Text = "";
        var a = new Run("甲") { FontSize = 24 }; var b = new Run("乙") { Foreground = Brushes.Blue };
        text.Inlines.Add(a); text.Inlines.Add(b);
        var originalParent = a.Parent;
        Add(text, ".text { display: flex; flex-direction: column; } .text::before { content: '标签'; }");
        Layout(text); text.Select(1, 1);
        Assert.Same(a, text.Inlines[0]); Assert.Same(b, text.Inlines[1]);
        Assert.Same(originalParent, a.Parent); Assert.Equal("乙", text.SelectedText);
        Assert.Equal("甲乙", text.Text); Assert.Equal(2, text.Inlines.Count);
        Assert.Contains("甲", Draw(text)); Assert.Contains("乙", Draw(text));
        b.Text = "丙"; Layout(text); Assert.Equal("甲丙", text.Text); Assert.Equal("丙", text.SelectedText);
        Assert.Contains("丙", Draw(text));
    }

    [Fact]
    public void TextChangesAndStyleRemovalRefreshTheBodyAndRestoreNativeLayout()
    {
        var text = Text(); Layout(text); var nativeSize = text.DesiredSize;
        var sheet = Add(text, ".text::before { content: '标签'; display: block; height: 20px; }");
        Layout(text); var body = Body(text); text.Text = "新的正文"; Layout(text);
        Assert.Contains("新的正文", Draw(text)); Assert.DoesNotContain("原生正文", Draw(text));
        Css.GetStyleSheets(text).Remove(sheet); text.Text = "原生正文"; Layout(text);
        Assert.Equal(nativeSize, text.DesiredSize); Assert.Equal(Visibility.Collapsed, body.Visibility);
        Assert.Equal(new[] { "原生正文" }, Draw(text));
        Css.GetStyleSheets(text).Add(sheet); Layout(text); Assert.Same(body, Body(text));
        Assert.Equal(Visibility.Visible, body.Visibility); Assert.Equal(2, VisualTreeHelper.GetChildrenCount(text));
    }

    [Theory]
    [InlineData("flex", "")]
    [InlineData("flex", " \t\r\n\f")]
    [InlineData("grid", "")]
    [InlineData("grid", " \t\r\n\f")]
    public void EmptyAndDocumentWhitespaceDoNotProduceAnonymousFlexOrGridItems(string display, string source)
    {
        var text = Text();
        Add(text, $$"""
            .text { display: {{display}}; flex-direction: column; gap: 6px; white-space: pre; }
            .text::before, .text::after { content: ''; height: 10px; }
            """);
        Layout(text); var body = Body(text);
        text.Text = source; Layout(text);
        Assert.Equal(Visibility.Collapsed, body.Visibility); Assert.Equal(26, text.DesiredSize.Height, 4);
        text.Text = "\u00a0"; Layout(text); Assert.Equal(Visibility.Visible, body.Visibility);
        Assert.True(text.DesiredSize.Height > 26);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("flex")]
    [InlineData("grid")]
    public void WrappingAndIntrinsicWidthsUseTheBodyConstraint(string display)
    {
        var text = Text(); text.Text = "窄窗口中的正文需要正常换行，并使用正文排版盒的宽度。";
        Add(text, $".text {{ display: {display}; width: 100px; white-space: normal; }} .text::before {{ content: ''; display: block; height: 16px; }}");
        if (display == "flex") Css.SetStyle(text, "flex-direction: column; align-items: stretch;");
        Layout(text); var body = Body(text); var narrow = body.ActualHeight;
        Assert.Equal(100, body.ActualWidth, 4); Assert.True(narrow > 40);
        var intrinsic = new CssLayoutBox(body, 0); intrinsic.MeasureIntrinsicWidth();
        Assert.True(intrinsic.MinContent.Width < intrinsic.MaxContent.Width / 2);
        Css.SetStyle(text, (display == "flex" ? "flex-direction: column; align-items: stretch; " : "") + "width: 220px;");
        Layout(text); Assert.Equal(220, body.ActualWidth, 4); Assert.True(body.ActualHeight < narrow);
    }

    [Theory]
    [InlineData("flex")]
    [InlineData("grid")]
    public void PointMappingAndSourceSegmentsIncludeTheAllocatedBodyOffset(string display)
    {
        var text = Text(); text.Text = "ABC\nDEF"; text.TextWrapping = TextWrapping.Wrap;
        Add(text, $".text {{ display: {display}; width: 180px; padding: 7px; white-space: pre-wrap; flex-direction: column; }} .text::before {{ content: ''; display: block; height: 30px; }}");
        Layout(text); var body = Body(text);
        var segments = text.GetSourceRangeSegments(0, text.Text.Length);
        Assert.Equal(2, segments.Count); Assert.Equal(body.VisualBounds.X, segments[0].StartX, 4);
        var point = new Point(segments[1].StartX + 0.1, segments[1].Baseline - 2);
        var position = Assert.IsType<TextPointer>(text.GetPositionFromPoint(point, true));
        Assert.Equal(4, text.ContentStart.GetOffsetToPosition(position));
        Assert.Null(text.GetPositionFromPoint(new Point(8, 8), false));
        Assert.Same(text, text.InputHitTest(point)); Assert.False(body.Focusable); Assert.Null(body.GetAutomationPeer());
        Assert.Equal(text.Text, text.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void AnonymousBodyCannotMatchSelectorsOrGenerateMorePseudos()
    {
        var text = Text();
        Add(text, ".text { display: flex; } .text > * { width: 999px; } *::before { content: '前'; } *::after { content: '后'; }");
        Layout(text); var body = Body(text);
        Assert.Null(body.CssRuntimeState); Assert.True(double.IsNaN(body.Width));
        Assert.Empty(CssMatcher.Children(CssNode.Get(text)));
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(body)); Assert.Equal(3, VisualTreeHelper.GetChildrenCount(text));
        Assert.Equal(new[] { "前", "原生正文", "后" }, Draw(text));
    }

    [Fact]
    public void GeneratedTextRemainsANativeLeaf()
    {
        var text = Text(); Add(text, ".text { display: grid; } .text::before { content: '前'; display: flex; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(before)); Assert.Equal("前", Draw(before).Single());
    }

    [Theory]
    [InlineData("flex")]
    [InlineData("grid")]
    public void AnonymousItemStaysAtOrderZeroWhilePseudoLayoutAndPaintUseAuthoredOrder(string display)
    {
        var text = Text();
        Add(text, $".text {{ display: {display}; align-items: start; grid-template-columns: auto auto auto; }} .text::before {{ content: '前'; order: 2; }} .text::after {{ content: '后'; order: -1; }}");
        Layout(text); var slots = CssGeneratedContent.Get(text)!; var body = Body(text);
        Assert.True(slots.After!.VisualBounds.X < body.VisualBounds.X);
        Assert.True(body.VisualBounds.X < slots.Before!.VisualBounds.X);
        Assert.Equal(new[] { "后", "原生正文", "前" }, Draw(text));
        Assert.Same(slots.Before, VisualTreeHelper.GetChild(text, 0));
        Assert.Equal("原生正文", text.Text);
    }

    [Fact]
    public void RemovingImplicitCssLayoutRestoresNativeWhitespaceEvenWithLocalNoWrap()
    {
        var text = Text(); text.Text = "A   B"; text.TextWrapping = TextWrapping.NoWrap;
        Layout(text); Assert.Equal(new[] { "A   B" }, Draw(text));
        var sheet = Add(text, ".text::before { content: ''; display: block; height: 10px; }");
        Layout(text); Assert.Equal(new[] { "A B" }, Draw(text));
        Css.GetStyleSheets(text).Remove(sheet); Layout(text);
        Assert.Equal(new[] { "A   B" }, Draw(text));
    }

    [Fact]
    public void DefaultCssWhitespaceWrapsWhileAnExplicitNativeNoWrapStillWins()
    {
        var text = Text(); text.Text = "中文正文应默认换行并保留原有显式原生属性的优先级。";
        Add(text, ".text { display: block; width: 70px; }"); Layout(text);
        Assert.True(Body(text).ActualHeight > 30);
        text.TextWrapping = TextWrapping.NoWrap; Layout(text);
        Assert.True(Body(text).DesiredSize.Height < 30);
    }

    [Theory]
    [InlineData("inside")]
    [InlineData("outside")]
    public void TextBlockListItemsKeepTheBodyAndUseItsFirstBaseline(string position)
    {
        var text = Text(); Add(text, $".text {{ display: list-item; list-style: decimal {position}; width: 180px; }}"); Layout(text);
        var marker = CssGeneratedContent.Get(text)!.Marker!; Assert.Equal("1. ", marker.Text);
        Assert.Contains("原生正文", Draw(text)); Assert.Same(text, VisualTreeHelper.GetParent(marker));
        if (position == "outside")
        {
            Assert.True(marker.VisualBounds.X < 0);
            Assert.Equal(text.GetFirstBaselineOffset(text.ActualHeight),
                marker.VisualBounds.Y + CssFlowProperties.LineMetrics(marker).Baseline, 4);
        }
        else Assert.True(marker.VisualBounds.Right <= Body(text).VisualBounds.X + 0.001);
    }

    [Fact]
    public void GeneratedAndAnonymousBoxesDoNotInterruptKeyboardFocus()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var text = Text(); var first = new Button { Content = "前" }; var next = new Button { Content = "后" };
        var root = new StackPanel(); root.Children.Add(first); root.Children.Add(text); root.Children.Add(next);
        Add(text, ".text { display: flex; } .text::before { content: attr(Tag); } .text::after { content: '尾'; }");
        var window = new DisplayedTestWindow { Width = 240, Height = 300, Content = root, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(first.Focus()); Assert.True(KeyboardNavigation.MoveFocus(first));
            Assert.Same(next, Keyboard.FocusedElement);
            text.Tag = "更新"; text.Text = "新正文"; Layout(window); Assert.Same(next, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(next, reverse: true)); Assert.Same(first, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void AnonymousPaintingKeepsTheNativeTextReentrancyGuard()
    {
        var text = Text(); Add(text, ".text { display: grid; }"); Layout(text);
        var dc = new TextCapture(); var callbacks = 0;
        dc.OnText = () => { if (++callbacks == 1) text.Render(dc); };
        text.Render(dc);
        Assert.Equal(new[] { "原生正文" }, dc.Text); Assert.Equal(1, callbacks);
    }

    private static TextBlock Text() { var text = new TextBlock { Text = "原生正文", FontSize = 16, Foreground = Brushes.Black, IsTextSelectionEnabled = true }; Css.SetClass(text, "text"); return text; }
    private static FrameworkElement Body(TextBlock text) => Assert.IsAssignableFrom<FrameworkElement>(text.NativeGetVisualChild(0));
    private static CssStyleSheet Add(TextBlock text, string css) { var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(text).Add(sheet); return sheet; }
    private static void Layout(FrameworkElement root)
    {
        for (var i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new Size(240, 300)); root.Arrange(new Rect(0, 0, 240, 300)); root.UpdateLayout(); }
    }
    private static string[] Draw(FrameworkElement text) { var dc = new TextCapture(); text.Render(dc); return dc.Text.ToArray(); }
    private sealed class TextCapture : DrawingContextAdapter
    {
        internal readonly List<string> Text = [];
        internal Action? OnText;
        public override void DrawText(FormattedText text, Point origin) { Text.Add(text.Text); OnText?.Invoke(); }
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
