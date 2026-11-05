using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using DocPaginator = Jalium.UI.Documents.DocumentPaginator;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFlowDocumentViewerTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private const string Words = "共享文档保留原始节点与选择范围，行内样式和图片边框跟随真实的排版行。";
    public CssFlowDocumentViewerTests() => FlowDocumentTemplateTestHelper.Register();
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void ScrollingAndPagedViewsKeepOriginalRunAndSpanPaint(bool page, bool span)
    {
        var run = new Run(Words + Words); Inline source = run;
        if (span) { var parent = new Span(); parent.Inlines.Add(run); source = parent; }
        var doc = Document(new Paragraph(source)); Style(doc, source, "background:blue;border-image:image(red) 0 fill / 0;");
        var viewer = page ? (FrameworkElement)new FlowDocumentPageViewer { Document = doc } : new FlowDocumentScrollViewer { Document = doc };
        Layout(viewer); var body = Assert.Single(Bodies(viewer));
        AssertPaint(body, source, Colors.Red);
        Assert.Same(source, ((Paragraph)doc.Blocks[0]).Inlines[0]); Assert.Same(doc, body.Document); Assert.Null(doc.Parent);
        Assert.Equal(Words + Words, run.Text);
        Css.SetStyle(source, "background:blue;border-image:none;"); Layout(viewer); AssertPaint(Assert.Single(Bodies(viewer)), source, Colors.Blue);
    }

    [Fact]
    public void OneDocumentCanBeViewedInAnEditorAndSeveralReadOnlyViewsWithoutReparenting()
    {
        var run = new Run(Words); var doc = Document(new Paragraph(run)); var editor = new RichTextBox(doc);
        var first = new FlowDocumentScrollViewer { Document = doc }; var second = new FlowDocumentPageViewer { Document = doc };
        Layout(first); Layout(second); Assert.Same(editor, doc.Parent); Assert.Same(run, ((Paragraph)doc.Blocks[0]).Inlines[0]);
        Assert.Same(doc, Assert.Single(Bodies(first)).Document); Assert.Same(doc, Assert.Single(Bodies(second)).Document);
        run.Text += Words; Layout(first); Layout(second); Assert.Equal(run.Text, Assert.Single(Bodies(first)).Paragraphs[0].Text);
        first.Document = null; second.Document = null; Assert.Same(editor, doc.Parent);
    }

    [Fact]
    public void PublicAndViewerPaginatorEntriesKeepTheSameIdentityAcrossStyleRefreshes()
    {
        var run = new Run(Words); var doc = Document(new Paragraph(run)); var paginator = doc.ViewerPaginator;
        var viewer = new FlowDocumentPageViewer { Document = doc }; Layout(viewer);
        Assert.Same(paginator, ((IDocumentPaginatorSource)doc).DocumentPaginator);
        var visual = viewer.PageViews[0].DocumentPage.Visual;
        Css.SetStyle(run, "border-image:image(red) 0 fill / 0;"); Layout(viewer);
        Assert.Same(paginator, viewer.PageViews[0].DocumentPaginator); Assert.Same(visual, viewer.PageViews[0].DocumentPage.Visual);
    }

    [Fact]
    public void PaginationUsesMeasuredGlyphWidthsInsteadOfCharacterCounts()
    {
        var narrow = Document(new Paragraph(new Run(new string('i', 160))));
        var wide = Document(new Paragraph(new Run(new string('W', 160))));
        Assert.True(wide.ViewerPaginator.PageCount > narrow.ViewerPaginator.PageCount);
    }

    [Theory]
    [InlineData("e\u0301👩‍👩‍👧‍👦中文 ")] [InlineData("אבג العربية mixed ")]
    public void PageStartsAreActualLineAndGraphemeBoundaries(string text)
    {
        var run = new Run(string.Concat(Enumerable.Repeat(text, 25))); var doc = Document(new Paragraph(run));
        using var layout = new FlowDocumentPresentation(observe: false); layout.SetDocument(doc); layout.EnsureDocumentLayout(184);
        var starts = layout.Lines.Select(line => line.Start).ToHashSet();
        var metrics = (IFlowDocumentPageMetrics)doc.ViewerPaginator; var previous = -1;
        Assert.True(doc.ViewerPaginator.PageCount > 1);
        for (var page = 1; page <= doc.ViewerPaginator.PageCount; page++)
        {
            var offset = metrics.GetPageStartOffset(page);
            Assert.True(offset > previous); Assert.Contains(offset, starts);
            Assert.Equal(offset, GraphemeClusters.SnapNearest(doc.GetText(), offset));
            Assert.Equal(page, metrics.GetPageNumberForOffset(offset)); previous = offset;
        }
        Assert.Equal(doc.ViewerPaginator.PageCount, metrics.GetPageNumberForOffset(doc.GetText().Length));
    }

    [Fact]
    public void FontAndLineHeightChangesRepaginateOriginalContent()
    {
        var run = new Run(string.Concat(Enumerable.Repeat(Words, 5))); var doc = Document(new Paragraph(run));
        var count = doc.ViewerPaginator.PageCount; Css.SetStyle(run, "font-size:30px;"); Flush();
        Assert.True(doc.ViewerPaginator.PageCount > count); Css.SetStyle(run, string.Empty); Flush(); Assert.Equal(count, doc.ViewerPaginator.PageCount);
        doc.LineHeight = 48; Assert.True(doc.ViewerPaginator.PageCount > count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitParagraphPageBreakAndEmptyParagraphKeepTheirOffsets(bool empty)
    {
        var first = new Paragraph(new Run(empty ? "" : "前段")); var second = new Paragraph(new Run("后一页")) { BreakPageBefore = true };
        var doc = Document(first, second); Assert.Equal(2, doc.ViewerPaginator.PageCount);
        Assert.Equal(doc.GetDocumentOffset(second, 0), ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageStartOffset(2));
        Assert.Equal(empty ? "\n后一页\n" : "前段\n后一页\n", doc.GetText());
    }

    [Theory]
    [InlineData(FlowDocumentReaderViewingMode.Page)] [InlineData(FlowDocumentReaderViewingMode.TwoPage)] [InlineData(FlowDocumentReaderViewingMode.Scroll)]
    public void ReaderModesAndSearchRetainStylesAndOriginalSelections(FlowDocumentReaderViewingMode mode)
    {
        var run = new Run(string.Concat(Enumerable.Repeat(Words, 3)) + "needle"); var doc = Document(new Paragraph(run));
        Style(doc, run, "background:blue;border-image:image(red) 0 fill / 0;");
        var reader = new FlowDocumentReader { Document = doc, ViewingMode = mode, SearchText = "needle", IsFindEnabled = true, IsInactiveSelectionHighlightEnabled = true };
        Layout(reader, 200, 140); reader.Find(); Layout(reader, 200, 140);
        Assert.Same(doc, reader.Selection!.Start.Document); Assert.Equal("needle", reader.Selection.Text);
        foreach (var body in Bodies(reader))
        {
            Assert.Same(doc, body.Document); Assert.Same(run, ((Paragraph)doc.Blocks[0]).Inlines[0]);
            Assert.Contains(body.Paragraphs, paragraph => paragraph.Text.Contains("needle"));
        }
        Assert.True(reader.PageNumber > 1);
        Assert.Contains(Bodies(reader).SelectMany(body => body.Paragraphs), paragraph => paragraph.SelectedText == "needle");
    }

    [Theory]
    [InlineData(80d)] [InlineData(150d)] [InlineData(200d)]
    public void ZoomScalesAllTypographyAndDecorationsWithoutChangingDocumentValues(double zoom)
    {
        var run = new Run(Words); var doc = Document(new Paragraph(run)); Style(doc, run, "font-size:24px;border-image:image(red) 0 fill / 0;");
        var viewer = new FlowDocumentScrollViewer { Document = doc, Zoom = zoom };
        Layout(viewer, 600, 500); var body = Assert.Single(Bodies(viewer));
        Assert.Equal(16, doc.FontSize); Assert.Equal(24, run.FontSize); Assert.Equal(16, body.Paragraphs[0].FontSize);
        var transform = Assert.IsType<ScaleTransform>(body.LayoutTransform); Assert.Equal(zoom / 100, transform.ScaleX);
        AssertPaint(body, run, Colors.Red);
    }

    [Fact]
    public void SearchAcrossParagraphsHighlightsTheirLocalRangesWithoutReplacingSourcePointers()
    {
        var first = new Run("第一段结尾"); var last = new Run("下一段开头"); var doc = Document(new Paragraph(first), new Paragraph(last));
        var viewer = new FlowDocumentScrollViewer { Document = doc, IsInactiveSelectionHighlightEnabled = true };
        Layout(viewer); viewer.SearchText = "结尾\n下一"; viewer.Find(); Layout(viewer);
        var body = Assert.Single(Bodies(viewer)); Assert.Equal("结尾\n下一", viewer.Selection!.Text);
        Assert.Equal("结尾", body.Paragraphs[0].SelectedText); Assert.Equal("下一", body.Paragraphs[1].SelectedText);
        Assert.Same(first, viewer.Selection.Start.Parent); Assert.Same(last, viewer.Selection.End.Parent);
    }

    [Fact]
    public void PageVisualIsAnActualChildAndIsReleasedWhenThePageChanges()
    {
        var run = new Run(Words + Words + Words); var doc = Document(new Paragraph(run));
        var page = new DocumentPageView { DocumentPaginator = doc.ViewerPaginator }; Layout(page);
        var old = Assert.IsType<FlowDocumentPresentation>(page.DocumentPage.Visual);
        Assert.Same(page, VisualTreeHelper.GetParent(old)); Assert.Contains(old, Bodies(page));
        page.PageNumber = 1; Layout(page); Assert.NotSame(old, page.DocumentPage.Visual); Assert.Null(old.Document); Assert.Null(VisualTreeHelper.GetParent(old));
        ((IDisposable)page).Dispose(); Assert.Empty(Bodies(page));
    }

    [Fact]
    public void SharedImageSetUsesTheSharpestLiveViewAndReturnsWhenItIsDetached()
    {
        var run = new Run(Words); var doc = Document(new Paragraph(run)); var images = new Images();
        Style(doc, run, "border-image:image-set('low.png' 1x,'high.png' 2x) 0 fill / 0;", images);
        using var low = new FlowDocumentPresentation(); low.SetDocument(doc); Layout(low); Ready(run);
        AssertPaint(low, run, Colors.Red); var first = Resources(run)[0];
        using var high = new FlowDocumentPresentation(); VisualTreeHelper.SetRootDpi(high, new(2, 2)); high.SetDocument(doc); Layout(high); Layout(low); Ready(run); Layout(high); Layout(low);
        Assert.NotSame(first, Resources(run)[0]); AssertPaint(low, run, Colors.Orange); AssertPaint(high, run, Colors.Orange);
        high.SetDocument(null); Layout(low); Ready(run); Layout(low); Assert.Same(first, Resources(run)[0]); AssertPaint(low, run, Colors.Red);
    }

    [Fact]
    public void AncestorRootDpiTransitionsRefreshDocumentScopedInheritedImageSets()
    {
        var run = new Run(Words); var span = new Span(); span.Inlines.Add(run); var doc = Document(new Paragraph(span));
        Style(doc, span, "border-image:image-set('low.png' 1x,'high.png' 2x) 0 fill / 0;", new Images()); Css.SetStyle(run, "border-image:inherit;");
        var viewer = new FlowDocumentScrollViewer { Document = doc, Height = 160, IsInactiveSelectionHighlightEnabled = true }; var root = new StackPanel(); root.Children.Add(viewer);
        Layout(root); Ready(span); Ready(run); Layout(root); var low = Resources(run)[0]; AssertPaint(Assert.Single(Bodies(viewer)), run, Colors.Red);
        VisualTreeHelper.SetRootDpi(root, new(2, 2)); Layout(root); Ready(span); Ready(run); Layout(root);
        Assert.NotSame(low, Resources(run)[0]); AssertPaint(Assert.Single(Bodies(viewer)), run, Colors.Orange);
        VisualTreeHelper.SetRootDpi(root, new(1, 1)); Layout(root); Ready(span); Ready(run); Layout(root); Assert.Same(low, Resources(run)[0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LastPageRemainsConnectedDuringAncestorDpiAndInheritedStylePublication(bool reader)
    {
        var run = new Run(Words + Words + Words); var span = new Span(); span.Inlines.Add(run); var doc = Document(new Paragraph(span));
        Style(doc, span, "border-image:image-set('low.png' 1x,'high.png' 2x) 0 fill / 0;", new Images()); Css.SetStyle(run, "border-image:inherit;");
        var root = new StackPanel(); FrameworkElement viewer = reader
            ? new FlowDocumentReader { Document = doc, Height = 160, IsInactiveSelectionHighlightEnabled = true }
            : new FlowDocumentPageViewer { Document = doc, Height = 160, IsInactiveSelectionHighlightEnabled = true };
        root.Children.Add(viewer); Layout(root, width: 1000); Ready(span); Ready(run); Layout(root, width: 1000);
        if (viewer is FlowDocumentReader reading) reading.LastPage(); else ((FlowDocumentPageViewer)viewer).LastPage(); Layout(root, width: 1000);
        VisualTreeHelper.SetRootDpi(root, new(2, 2)); Layout(root, width: 1000); Ready(span); Ready(run); Layout(root, width: 1000);
        Assert.NotEmpty(Bodies(viewer)); Assert.All(Bodies(viewer), body => Assert.Same(doc, body.Document));
        AssertPaint(Assert.Single(Bodies(viewer)), run, Colors.Orange);
    }

    [Fact]
    public void PixelNotificationsRepaintEveryViewWithoutRepaginationOrContentChanges()
    {
        var run = new Run(Words); var doc = Document(new Paragraph(run)); var images = new Images();
        Style(doc, run, "border-image:url('low.png') 0 fill / 0;", images);
        using var first = new FlowDocumentPresentation(); using var second = new FlowDocumentPresentation(); first.SetDocument(doc); second.SetDocument(doc);
        Layout(first); Layout(second); Ready(run); Layout(first); Layout(second);
        var paragraphA = first.Paragraphs[0]; var paragraphB = second.Paragraphs[0];
        var paginator = doc.ViewerPaginator; _ = paginator.PageCount; var field = paginator.GetType().GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!; var pages = field.GetValue(paginator);
        var content = 0; doc.ContentChanged += (_, _) => content++; first.ClearRenderDirty(); second.ClearRenderDirty(); paragraphA.ClearRenderDirty(); paragraphB.ClearRenderDirty();
        var notify = typeof(CssBackgroundImageResource).GetMethod("NotifyUsers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var resource = Resources(run)[0]; notify.Invoke(resource, [true, null]); Flush();
        Assert.True(first.IsRenderDirty); Assert.True(second.IsRenderDirty); Assert.True(paragraphA.IsRenderDirty); Assert.True(paragraphB.IsRenderDirty);
        Assert.True(first.IsMeasureValid); Assert.True(second.IsMeasureValid); Assert.Equal(0, content); _ = paginator.PageCount; Assert.Same(pages, field.GetValue(paginator));
        Css.SetStyle(run, "border-image:none;"); Layout(first); Layout(second); first.ClearRenderDirty(); second.ClearRenderDirty(); notify.Invoke(resource, [true, null]); Flush();
        Assert.False(first.IsRenderDirty); Assert.False(second.IsRenderDirty);
    }

    [Fact]
    public void SourceMutationUpdatesEveryViewAndPreservesBorrowedInlineCollections()
    {
        var paragraph = new Paragraph(new Run(Words)); var doc = Document(paragraph);
        using var first = new FlowDocumentPresentation(); using var second = new FlowDocumentPresentation(); first.SetDocument(doc); second.SetDocument(doc); Layout(first); Layout(second);
        var inlines = paragraph.Inlines; var added = new Run("新增内容"); paragraph.Inlines.Add(added); Layout(first); Layout(second);
        Assert.Same(inlines, first.Paragraphs[0].Inlines); Assert.Same(inlines, second.Paragraphs[0].Inlines);
        Assert.EndsWith("新增内容", first.Paragraphs[0].Text); Assert.Same(paragraph, added.Parent); Assert.Null(doc.Parent);
    }

    [Fact]
    public void SoftwareLayoutAlsoWrapsInsideOriginalRunsAndPreservesImagePaint()
    {
        using var software = RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        var run = new Run(Words + Words); var doc = Document(new Paragraph(run)); Style(doc, run, "border-image:image(red) 0 fill / 0;");
        using var view = new FlowDocumentPresentation(); view.SetDocument(doc); Layout(view); Assert.True(view.Lines.Count > 1); Assert.True(doc.ViewerPaginator.PageCount > 1); AssertPaint(view, run, Colors.Red);
    }

    [Fact]
    public void ViewHasOneKeyboardTargetAndAnAccessibleOriginalDocumentName()
    {
        var run = new Run(Words); var doc = Document(new Paragraph(run)); using var view = new FlowDocumentPresentation(); view.SetDocument(doc); Layout(view);
        Assert.True(view.Focusable); Assert.All(view.Paragraphs, paragraph => Assert.False(paragraph.Focusable));
        var peer = view.GetAutomationPeer()!; Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType()); Assert.Equal(doc.GetText(), peer.GetName());
        var key = new KeyEventArgs(UIElement.PreviewKeyDownEvent, Key.A, ModifierKeys.Control, true, false, 0); view.RaiseEvent(key); Assert.True(key.Handled); Assert.Equal(doc.GetText(), view.Selection!.Text);
        var provider = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.ITextProvider>(peer.GetPattern(Jalium.UI.Automation.Peers.PatternInterface.Text));
        Assert.Equal(doc.GetText(), provider.DocumentRange.GetText(-1)); Assert.Equal(doc.GetText(), provider.GetSelection()[0].GetText(-1));
        var range = provider.DocumentRange.FindText(Words.Substring(2, 4), false, false)!; range.Select();
        Assert.Equal(Words.Substring(2, 4), view.Selection.Text); Assert.NotEmpty(range.GetBoundingRectangles());
    }

    [Fact]
    public void PageNavigationAtASoftWrapBoundaryUsesTheFollowingLine()
    {
        var doc = Document(new Paragraph(new Run(Words + Words + Words)));
        using var body = new FlowDocumentPresentation(observe: false); body.SetDocument(doc); body.EnsureDocumentLayout(184);
        var next = body.Lines[1]; Assert.Equal(next.Start, body.Lines[0].End);
        Assert.Equal(next.Top, body.TopForOffset(next.Start));
    }

    [Fact]
    public void DocumentViewsRemainSingleTabTargetsAfterDecorationChanges()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var run = new Run(Words); var doc = Document(new Paragraph(run));
        var viewer = new FlowDocumentScrollViewer { Document = doc, Height = 140 };
        var before = new TextBlock { Text = "之前", Focusable = true }; var after = new TextBlock { Text = "之后", Focusable = true };
        var root = new StackPanel(); root.Children.Add(before); root.Children.Add(viewer); root.Children.Add(after);
        foreach (var target in new FrameworkElement[] { before, after }) KeyboardNavigation.SetIsTabStop(target, true);
        var window = new DisplayedTestWindow { Content = root, Width = 260, Height = 500, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window, 260, 500); var body = Assert.Single(Bodies(viewer));
            Assert.True(before.Focus()); Assert.True(KeyboardNavigation.MoveFocus(before)); Assert.Same(body, Keyboard.FocusedElement);
            Css.SetStyle(run, "border-image:image(red) 0 fill / 0;"); Layout(window, 260, 500); Assert.Same(body, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(body)); Assert.Same(after, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void NestedBlockOffsetsAgreeWithOriginalDocumentHitTesting()
    {
        var run = new Run(Words); var paragraph = new Paragraph(run) { Margin = new(6, 4, 0, 0), Padding = new(3) };
        var item = new ListItem(); item.Blocks.Add(paragraph); var list = new Jalium.UI.Documents.List(); list.ListItems.Add(item);
        var section = new Section { Margin = new(10, 5, 0, 0) }; section.Blocks.Add(list); var doc = Document(section);
        using var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body);
        var text = Assert.Single(body.Paragraphs); var point = text.TransformToAncestor(body); Assert.Equal(47, point.X); Assert.Equal(20, point.Y);
        var offset = body.OffsetFromPoint(new(point.X + 1, point.Y + 8)); Assert.Same(run, doc.GetPositionAtOffset(offset, LogicalDirection.Forward)!.Parent);
    }

    [Fact]
    public void TranslucentBlockBorderCornersArePaintedOnce()
    {
        var paragraph = new Paragraph(new Run(Words)) { BorderThickness = new(4), BorderBrush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 255)) };
        var doc = Document(paragraph); using var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body);
        Assert.Equal(Color.FromRgb(127, 127, 255), Sample(body, 9, 9));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BlockImageSuppressesTheOrdinaryBorderAndRemovalRestoresIt(bool transparent)
    {
        var paragraph = new Paragraph(new Run(Words)) { BorderThickness = new(4), BorderBrush = Brushes.Green };
        var doc = Document(paragraph); Css.SetStyle(paragraph, $"border-image:image({(transparent ? "transparent" : "red")}) 10 fill / 4px;");
        using var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body);
        Assert.Equal(transparent ? Colors.White : Colors.Red, Sample(body, 9, 9));
        Css.SetStyle(paragraph, "border-image:none;"); Layout(body); Assert.Equal(Colors.Green, Sample(body, 9, 9));
    }

    [Fact]
    public void TextProviderScrollIntoViewNavigatesToTheOriginalRangePage()
    {
        var doc = Document(new Paragraph(new Run(Words + Words + Words + "needle")));
        var viewer = new FlowDocumentPageViewer { Document = doc }; Layout(viewer);
        var body = Assert.Single(Bodies(viewer));
        var provider = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.ITextProvider>(body.GetAutomationPeer()!.GetPattern(Jalium.UI.Automation.Peers.PatternInterface.Text));
        var range = provider.DocumentRange.FindText("needle", false, false)!; range.ScrollIntoView(true); Layout(viewer);
        Assert.Equal(doc.ViewerPaginator.PageCount, viewer.PageNumber);
        Assert.Same(doc, Assert.Single(Bodies(viewer)).Document);
    }

    private static FlowDocument Document(params Block[] blocks)
    {
        var doc = new FlowDocument { FontSize = 16, FontFamily = "PingFang SC", Foreground = Brushes.Transparent, Background = Brushes.White, PageWidth = 200, PageHeight = 80, PagePadding = new(8) };
        foreach (var block in blocks) doc.Blocks.Add(block); return doc;
    }
    private static void Style(FlowDocument doc, DependencyObject source, string value, Images? images = null)
    {
        Css.SetClass(source, "piece"); var sheet = CssStyleSheet.Parse(".piece{" + value + "}", "flow-viewer-test", new Uri("file:///flow-viewer/"), images ?? new Images());
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(doc).Add(sheet); Flush();
    }
    private static void Layout(FrameworkElement view, double width = 200, double height = 300)
    {
        if (view is FlowDocumentScrollViewer { Style: null } scrolling)
            scrolling.Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlowDocumentScrollViewer)];
        for (var i = 0; i < 4; i++) { Flush(); view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout(); }
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static IEnumerable<FlowDocumentPresentation> Bodies(Visual root)
    {
        if (root is FlowDocumentPresentation body) yield return body;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (VisualTreeHelper.GetChild(root, i) is Visual child) foreach (var item in Bodies(child)) yield return item;
    }
    private static void AssertPaint(FlowDocumentPresentation view, Inline source, Color expected)
    {
        var matches = view.Paragraphs.SelectMany(text => text.GetDocumentDecorationFragments(source).Select(fragment =>
            (Point: text.TransformToAncestor(view), Fragment: fragment.Rect))).Where(pair => pair.Fragment.Y + pair.Point.Y >= 8 && pair.Fragment.Bottom + pair.Point.Y <= view.RenderSize.Height - 8).ToArray();
        Assert.NotEmpty(matches); var sample = matches[0]; Assert.Equal(expected, Sample(view, (int)(sample.Point.X + sample.Fragment.X + 2), (int)(sample.Point.Y + sample.Fragment.Y + 2)));
    }
    private static Color Sample(FrameworkElement view, int x, int y)
    {
        var width = (int)Math.Ceiling(view.RenderSize.Width); var height = (int)Math.Ceiling(view.RenderSize.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap); view.Render(dc); dc.Close();
        var pixels = bitmap.GetPixelBuffer(); var at = (y * width + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
    private static CssBackgroundImageResource[] Resources(DependencyObject source) => (CssBackgroundImageResource[]?)source.GetValue(CssBorderImageProperties.ResourcesProperty) ?? [];
    private static void Ready(DependencyObject source)
    {
        foreach (var resource in Resources(source))
        {
            Wait(() => resource.Ready.IsCompleted);
            if (resource.Get(CssNode.Get(source)) is BitmapImage bitmap) { var task = bitmap.PreloadAsync(300, 200); Wait(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); }
        }
        Flush();
    }
    private static void Wait(Func<bool> ready)
    { var until = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < until) { Flush(); Thread.Sleep(1); } Assert.True(ready()); }
    private sealed class Images : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var color = uri.LocalPath.EndsWith("high.png") ? Colors.Orange : Colors.Red;
            var data = new byte[16 * 16 * 4]; for (var at = 0; at < data.Length; at += 4) { data[at] = color.B; data[at + 1] = color.G; data[at + 2] = color.R; data[at + 3] = 255; }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, data, 64)));
            using var stream = new MemoryStream(); encoder.Save(stream); return ValueTask.FromResult(new CssResource(uri, new MemoryStream(stream.ToArray())));
        }
    }
}
