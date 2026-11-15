using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Markup;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssContentHostTests
{
    [Theory]
    [InlineData("border", "block")]
    [InlineData("content", "block")]
    [InlineData("presenter", "block")]
    [InlineData("template", "block")]
    [InlineData("custom", "block")]
    [InlineData("border", "flex")]
    [InlineData("content", "flex")]
    [InlineData("presenter", "flex")]
    [InlineData("template", "flex")]
    [InlineData("custom", "flex")]
    [InlineData("border", "grid")]
    [InlineData("content", "grid")]
    [InlineData("presenter", "grid")]
    [InlineData("template", "grid")]
    [InlineData("custom", "grid")]
    public void ContainersShareTheExistingBlockFlexAndGridFormatters(string kind, string display)
    {
        var body = new Border { Width = 20, Height = 12 }; Css.SetClass(body, "body");
        if (kind == "template") Css.SetStyle(body, "display: inline-block;");
        var host = Host(kind, body);
        Add(host, $$"""
            .host { display: {{display}}; align-items: start; grid-template-columns: 10px 20px 30px; }
            .host::before { content: ''; display: inline-block; width: 10px; height: 12px; }
            .host > .body { display: inline-block; width: 20px; }
            .host::after { content: ''; display: inline-block; width: 30px; height: 12px; }
            """);
        Layout(host);
        Assert.Equal(0, Before(host).VisualBounds.X, 5);
        Assert.Equal(10, body.VisualBounds.X, 5); Assert.Equal(30, After(host).VisualBounds.X, 5);
        Assert.Same(host, VisualTreeHelper.GetParent(body));
        Assert.Same(body, Content(host));
        Assert.Equal(3, VisualTreeHelper.GetChildrenCount(host));
        Assert.Equal(kind == "template" ? 0 : 1, CssMatcher.Children(CssNode.Get(host)).Count());
        Assert.Equal(Visibility.Visible, Before(host).Visibility);
    }

    [Theory]
    [InlineData("border")]
    [InlineData("content")]
    [InlineData("presenter")]
    [InlineData("template")]
    [InlineData("custom")]
    public void GeneratedContentWithoutDisplayUsesAFlowBoxAndRemovalRestoresNativeLayout(string kind)
    {
        var body = new Border { Width = 20, Height = 20 }; var host = Host(kind, body);
        Layout(host); var nativeBounds = body.VisualBounds;
        var sheet = Add(host, ".host::before { content: ''; display: block; height: 10px; } .host::after { content: ''; display: block; height: 8px; }");
        Layout(host);
        Assert.Equal(38, host.DesiredSize.Height, 5); Assert.Equal(10, body.VisualBounds.Y, 5);
        var before = Before(host); Css.GetStyleSheets(host).Remove(sheet); Layout(host);
        Assert.Equal(20, host.DesiredSize.Height, 5); Assert.Equal(nativeBounds, body.VisualBounds);
        Assert.Equal(Visibility.Collapsed, before.Visibility); Assert.Same(body, Content(host));
    }

    [Fact]
    public void BorderEdgesRemainOutsideAllThreeFormattingChildren()
    {
        var body = new Border { Width = 20, Height = 12 };
        var host = (Border)Host("border", body); host.Padding = new Thickness(5); host.BorderThickness = new Thickness(2);
        Add(host, ".host { display: flex; align-items: start; } .host::before, .host::after { content: ''; width: 10px; height: 12px; }");
        Layout(host);
        Assert.Equal(7, Before(host).VisualBounds.X, 5); Assert.Equal(17, body.VisualBounds.X, 5);
        Assert.Equal(7, body.VisualBounds.Y, 5); Assert.Equal(26, host.DesiredSize.Height, 5);
        Assert.Equal(54, host.DesiredSize.Width, 5);
    }

    [Theory]
    [InlineData("border")]
    [InlineData("content")]
    [InlineData("presenter")]
    [InlineData("template")]
    [InlineData("custom")]
    public void UnifiedVisualEnumerationHasStableOrderAndIndexBoundaries(string kind)
    {
        var body = new Border(); var host = Host(kind, body);
        Add(host, ".host::before { content: '前'; } .host::after { content: '后'; }"); Layout(host);
        Assert.Equal(3, VisualTreeHelper.GetChildrenCount(host));
        Assert.Same(Before(host), VisualTreeHelper.GetChild(host, 0));
        Assert.Same(body, VisualTreeHelper.GetChild(host, 1));
        Assert.Same(After(host), VisualTreeHelper.GetChild(host, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualTreeHelper.GetChild(host, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualTreeHelper.GetChild(host, 3));
    }

    [Fact]
    public void ReplacingNativeContentDoesNotLoseOrDuplicatePseudoVisuals()
    {
        var original = new Border { Height = 20 }; var replacement = new Border { Height = 30 };
        var host = (ContentControl)Host("content", original);
        Add(host, ".host::before { content: '前'; } .host::after { content: '后'; }");
        Layout(host); var before = Before(host); var after = After(host);
        foreach (var content in new UIElement?[] { replacement, null, original, replacement })
        {
            host.Content = content; Layout(host);
            Assert.Same(before, Before(host)); Assert.Same(after, After(host));
            Assert.Equal(content is null ? 2 : 3, VisualTreeHelper.GetChildrenCount(host));
            Assert.Equal(content is null ? 0 : 1, CssMatcher.Children(CssNode.Get(host)).Count());
            Assert.Same(content, Content(host));
        }
        Assert.Null(VisualTreeHelper.GetParent(original));
    }

    [Fact]
    public void TemplateReplacementAndDeferredCreationPreserveTheGeneratedSlots()
    {
        var first = new Border { Height = 20 }; var second = new Border { Height = 30 };
        var host = (Control)Host("template", first);
        Add(host, ".host { display: block; } .host::before { content: ''; display: block; height: 10px; }");
        Layout(host);
        var before = Before(host); Assert.Same(first, NativeRoot(host)); Assert.Equal(10, first.VisualBounds.Y, 5);
        host.Template = Template(second); Layout(host);
        Assert.Same(second, NativeRoot(host)); Assert.Same(before, Before(host));
        Assert.Equal(2, VisualTreeHelper.GetChildrenCount(host)); Assert.Equal(10, second.VisualBounds.Y, 5);
        Assert.Null(VisualTreeHelper.GetParent(first));
    }

    [Fact]
    public void InheritanceAttributesAndCountersUseTheOriginWithoutChangingDocumentStructure()
    {
        var body = new Border(); Css.SetClass(body, "body"); var host = Host("border", body); host.Tag = "甲";
        Add(host, """
            .host { color: red; font-size: 20px; --label: '标题'; counter-reset: n 7; }
            .host::before { content: var(--label) attr(Tag) counter(n); }
            .host::after { content: counter(n); }
            .host > .body:first-child:last-child { width: 35px; }
            .host:has(> TextBlock) { height: 900px; }
            """);
        Layout(host); Assert.Equal("标题甲7", Before(host).Text);
        Assert.Equal(20, Before(host).FontSize); Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Before(host).Foreground).Color);
        Assert.Equal(35, body.Width); Assert.True(double.IsNaN(host.Height));
        host.Tag = "乙"; Css.SetStyle(host, "--label: '更新'; counter-reset: n 9;"); Layout(host);
        Assert.Equal("更新乙9", Before(host).Text); Assert.Equal("9", After(host).Text);
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Theory]
    [InlineData("border")]
    [InlineData("content")]
    [InlineData("presenter")]
    [InlineData("template")]
    public void PseudoHitTargetsTheOriginAndRespectsItsClip(string kind)
    {
        var host = Host(kind, new Border { Width = 20, Height = 12 });
        Add(host, ".host { display: block; } .host::before { content: ''; display: block; width: 20px; height: 12px; position: relative; left: -10px; background-color: red; }");
        Layout(host);
        var point = new Point(-5, 5);
        Assert.Same(host, host.HitTest(point)?.VisualHit); Assert.Same(host, host.InputHitTest(point));
        host.ClipToBounds = true; Layout(host);
        Assert.Null(host.HitTest(point)); Assert.Null(host.InputHitTest(point));
        Assert.False(Before(host).Focusable);
    }

    [Fact]
    public void ListItemCountersAndOutsideMarkersWorkOnBorderOrigins()
    {
        var root = new StackPanel(); var rows = Enumerable.Range(0, 3).Select(_ => (Border)Host("border", new TextBlock { Text = "正文" })).ToArray();
        foreach (var row in rows) root.Children.Add(row);
        Add(root, ".host { display: list-item; list-style: decimal; }"); Layout(root);
        Assert.Equal(new[] { "1. ", "2. ", "3. " }, rows.Select(row => CssGeneratedContent.Get(row)!.Marker!.Text));
        Assert.All(rows, row => Assert.True(CssGeneratedContent.Get(row)!.Marker!.VisualBounds.X < 0));
    }

    [Fact]
    public void FlexOrderAndGapUpdateWithoutReorderingTheNativeDocument()
    {
        var a = new Border { Width = 20, Height = 12 }; Css.SetClass(a, "a");
        var b = new Border { Width = 20, Height = 12 }; Css.SetClass(b, "b");
        var host = new CustomContainer(a, b); Css.SetClass(host, "host");
        Add(host, """
            .host { display: flex; gap: 5px; align-items: start; }
            .host::before { content: ''; width: 10px; height: 12px; order: -1; }
            .host::after { content: ''; width: 10px; height: 12px; order: 3; }
            .a { order: 2; } .b { order: 1; }
            """);
        Layout(host); Assert.Equal(15, b.VisualBounds.X, 5); Assert.Equal(40, a.VisualBounds.X, 5);
        Assert.Equal(new[] { a, b }, host.Children);
        Css.SetStyle(a, "order: 0;"); Layout(host);
        Assert.Equal(15, a.VisualBounds.X, 5); Assert.Equal(40, b.VisualBounds.X, 5);
        Assert.Equal(new[] { a, b }, CssMatcher.Children(CssNode.Get(host)).Select(n => n.Target));
    }

    [Fact]
    public void NonPanelSubgridsBorrowTracksFromTheirParent()
    {
        var a = new Border { Height = 12 }; var b = new Border { Height = 12 };
        var subgrid = new CustomContainer(a, b); Css.SetClass(subgrid, "sub");
        var root = new StackPanel(); root.Children.Add(subgrid); Css.SetClass(root, "root");
        Add(root, ".root { display: grid; grid-template-columns: 40px 60px; column-gap: 10px; } .sub { display: grid; grid-column: 1 / 3; grid-template-columns: subgrid; }");
        Layout(root); Assert.Equal(110, subgrid.ActualWidth, 5);
        Assert.Equal(40, a.ActualWidth, 5); Assert.Equal(50, b.VisualBounds.X, 5); Assert.Equal(60, b.ActualWidth, 5);
    }

    [Fact]
    public void AutoGridHeightIsIntrinsicAndNativeWrappingSuppliesItsMinContentWidth()
    {
        var text = new TextBlock { Text = "自动高度的网格应按换行后的正文测量，而不是填满整个父级窗口。", TextWrapping = TextWrapping.Wrap };
        var host = Host("presenter", text);
        Add(host, ".host { display: grid; grid-template-columns: 1fr; width: 100px; }");
        Layout(host);
        Assert.InRange(host.DesiredSize.Height, 20, 180);
        Assert.InRange(text.ActualWidth, 1, 100);
        Assert.True(text.ActualHeight > 20);
        var box = new CssLayoutBox(text, 0); box.MeasureIntrinsicWidth();
        Assert.True(box.MinContent.Width < box.MaxContent.Width / 2);
    }

    [Fact]
    public void AbsoluteChildrenDoNotInflateTheHostAndFixedPseudosStillUseTheViewport()
    {
        var body = new Border(); Css.SetClass(body, "absolute"); var host = Host("border", body);
        Add(host, """
            .host { display: block; width: 100px; height: 80px; }
            .absolute { position: absolute; left: 10px; top: 6px; width: 20px; height: 12px; }
            .host::before { content: ''; position: fixed; left: 40px; top: 20px; width: 15px; height: 12px; }
            """);
        Layout(host);
        Assert.Equal(new Rect(10, 6, 20, 12), body.VisualBounds);
        Assert.Equal(new Rect(40, 20, 15, 12), Before(host).VisualBounds);
        Assert.Equal(100, host.DesiredSize.Width); Assert.Equal(80, host.DesiredSize.Height);
    }

    [Fact]
    public void PaintAndHitOrderAgreeForOverlappingPseudoAndNativeBoxes()
    {
        var body = new Border { Width = 20, Height = 20, Background = Brushes.Blue }; var host = Host("border", body);
        Add(host, """
            .host { display: block; }
            .host::before, .host::after { content: ''; position: absolute; left: 0; top: 0; width: 20px; height: 20px; }
            .host::before { background-color: red; z-index: 2; }
            .host::after { background-color: green; z-index: 1; }
            """);
        Layout(host);
        Assert.Equal(Colors.Red, Pixel(host)); Assert.Same(host, host.InputHitTest(new Point(5, 5)));
        Css.SetStyle(Before(host), "z-index: -1;"); Css.SetStyle(After(host), "z-index: -2;"); Layout(host);
        Assert.Equal(Colors.Blue, Pixel(host)); Assert.Same(body, host.InputHitTest(new Point(5, 5)));
    }

    [Fact]
    public void GeneratedSlotsPreserveKeyboardFocusThroughContentAndStyleUpdates()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var a = new Button { Content = "第一项" }; var b = new Button { Content = "第二项" };
        var host = new CustomContainer(a, b); Css.SetClass(host, "host");
        Add(host, ".host { display: flex; } .host::before { content: attr(Tag); } .host::after { content: '尾'; }");
        var window = new DisplayedTestWindow { Width = 240, Height = 240, Content = host, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            host.Tag = "更新"; Layout(window); Assert.Equal("更新", Before(host).Text); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static Color Pixel(FrameworkElement host)
    {
        var bitmap = new RenderTargetBitmap(240, 240, 96, 96, PixelFormat.Bgra32); bitmap.Render(host);
        var data = new byte[4]; bitmap.CopyPixels(new Int32Rect(5, 5, 1, 1), data, 4, 0);
        return Color.FromArgb(data[3], data[2], data[1], data[0]);
    }

    private static FrameworkElement Host(string kind, UIElement child)
    {
        FrameworkElement host = kind switch
        {
            "border" => new Border { Child = child }, "content" => new ContentControl { Content = child },
            "presenter" => new ContentPresenter { Content = child }, "template" => new Control { Template = Template((FrameworkElement)child) },
            _ => new CustomContainer(child),
        };
        Css.SetClass(host, "host"); return host;
    }
    private static ControlTemplate Template(FrameworkElement body)
    { var template = new ControlTemplate(typeof(Control)); template.SetVisualTree(() => body); return template; }
    private static UIElement? Content(FrameworkElement host) => host switch
    { Border b => b.Child, ContentControl c => c.Content as UIElement, ContentPresenter p => p.Content as UIElement, Control c => NativeRoot(c), CustomContainer c => c.Child, _ => null };
    private static CssGeneratedText Before(FrameworkElement host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.Before);
    private static CssGeneratedText After(FrameworkElement host) => Assert.IsType<CssGeneratedText>(CssGeneratedContent.Get(host)?.After);
    private static FrameworkElement? NativeRoot(Control host) => host.NativeVisualChildrenCount == 0 ? null : host.NativeGetVisualChild(0) as FrameworkElement;
    private static CssStyleSheet Add(FrameworkElement host, string css)
    { var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(host).Add(sheet); return sheet; }
    private static void Layout(FrameworkElement host)
    {
        for (var i = 0; i < 5; i++)
        { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); host.Measure(new Size(240, 240)); host.Arrange(new Rect(0, 0, 240, 240)); host.UpdateLayout(); }
    }
    private sealed class CustomContainer : FrameworkElement
    {
        private readonly List<UIElement> _native = [];
        internal IReadOnlyList<UIElement> Children => _native;
        internal UIElement Child => _native[0];
        internal CustomContainer(params UIElement[] children)
        { foreach (var child in children) { _native.Add(child); AddVisualChild(child); AddLogicalChild(child); } }
        protected override bool SupportsCssContentLayout => true;
        protected override int VisualChildrenCount => _native.Count;
        protected override Visual? GetVisualChild(int index) => _native[index];
        protected override Size MeasureOverride(Size available)
        { foreach (var child in _native) child.Measure(available); return new(_native.Max(c => c.DesiredSize.Width), _native.Sum(c => c.DesiredSize.Height)); }
        protected override Size ArrangeOverride(Size finalSize)
        { var y = 0d; foreach (var child in _native) { child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height)); y += child.DesiredSize.Height; } return finalSize; }
    }
}
