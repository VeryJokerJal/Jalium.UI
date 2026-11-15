using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssRichDocumentImageTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private const string Words = "正文跨行后继续使用原始的文档节点与选择范围，图片边框保留连续相位，并在滚动时跟随文字。";
    public CssRichDocumentImageTests() { Assert.True(_context.IsValid); Assert.Equal(RenderBackend.Metal, _context.Backend); }
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void RunAndSpanUseTheNativeParagraphFragments(bool span, bool clone)
    {
        var run = new Run(Words); Inline source = run;
        if (span) { var container = new Span(); container.Inlines.Add(run); source = container; }
        var box = Editor(new Paragraph(source)); var images = Attach(box, source, $"border-image:url('nine.png') 10 fill / 4px;box-decoration-break:{(clone ? "clone" : "slice")};");
        Ready(box, source); var pieces = box.GetDocumentDecorationFragments(source); Assert.True(pieces.Length > 1);
        for (var i = 0; i < pieces.Length; i++)
        {
            Assert.Equal(i == 0 || clone ? Colors.Red : Colors.Lime, Pixel(box, pieces[i].Rect, 1, 1));
            Assert.Equal(i == pieces.Length - 1 || clone ? Colors.Blue : Colors.Lime, Pixel(box, pieces[i].Rect, -2, 1));
        }
        Assert.Same(source, ((Paragraph)box.Document.Blocks[0]).Inlines[0]); Assert.Equal(Words, run.Text);
        Assert.True(((IImeSupport)box).TrySetImeSelection(2, 6)); Assert.Equal(Words.Substring(2, 6), box.Selection.Text);
    }

    [Theory]
    [InlineData("stretch")] [InlineData("repeat")] [InlineData("round")] [InlineData("space")]
    public void ClippedScrolledFragmentsKeepTheJoinedImagesPatternPhase(string repeat)
    {
        var run = new Run(Words + Words); var box = Editor(new Paragraph(run));
        Attach(box, run, $"border-image:url('pattern.png') 10 / 4px {repeat};"); Ready(box, run);
        box.VerticalOffsetCore = 37.25; box.HorizontalOffsetCore = 3.5; Layout(box, 200, 120);
        var pieces = box.GetDocumentDecorationFragments(run); Assert.True(pieces.Length > 2); Assert.True(pieces[0].Rect.Y < 0);
        // The style belongs to the nonvisual run rather than the editor.
        var style = new Border(); foreach (var property in ImageProperties) style.SetValue(property, run.GetValue(property));
        var reference = new RenderTargetBitmap(200, 120, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(reference);
        dc.DrawRectangle(Brushes.White, null, new(0, 0, 200, 120)); dc.PushClip(new RectangleGeometry(Content(box)));
        var advance = 0d; var width = pieces.Sum(p => p.Rect.Width);
        foreach (var piece in pieces)
        {
            dc.PushClip(new RectangleGeometry(piece.Rect)); CssBorderImagePainter.Draw(style, dc,
                new(piece.Rect.X - advance, piece.Rect.Y, width, piece.Rect.Height), default, false); dc.Pop(); advance += piece.Rect.Width;
        }
        dc.Pop(); dc.Close(); var expected = reference.GetPixelBuffer();
        foreach (var piece in pieces.Where(p => p.Rect.Y >= Content(box).Y && p.Rect.Y + 3 < Content(box).Bottom))
        for (var offset = 7; offset < piece.Rect.Width - 7; offset += 9)
        {
            var x = (int)(piece.Rect.X + offset); var y = (int)piece.Rect.Y + 1; if (!Content(box).Contains(new Point(x, y))) continue;
            var at = (y * 200 + x) * 4; Assert.Equal(Color.FromArgb(expected[at + 3], expected[at + 2], expected[at + 1], expected[at]), Sample(box, x, y));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedBackgroundsAndImageFillsFollowAncestorPaintOrder(bool innerImage)
    {
        var first = new Run("前段 "); var inner = new Run("内部正文"); var outer = new Span(); outer.Inlines.Add(first); outer.Inlines.Add(inner); outer.Inlines.Add(new Run(" 尾段"));
        var box = Editor(new Paragraph(outer)); Attach(box, outer, "background:yellow;border-image:image(red) 0 fill / 0;");
        Css.SetStyle(inner, innerImage ? "background:white;border-image:image(blue) 0 fill / 0;" : "background:blue;"); Layout(box);
        var parent = box.GetDocumentDecorationFragments(outer)[0].Rect; var child = box.GetDocumentDecorationFragments(inner)[0].Rect;
        Assert.Equal(Colors.Red, Pixel(box, parent, 2, 2)); Assert.Equal(Colors.Blue, Pixel(box, child, 2, 2));
        Assert.True(((IImeSupport)box).TrySetImeSelection(1, 5)); Assert.Equal(box.Document.GetText().Substring(1, 5), box.Selection.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParentDirectionChoosesCutEdgesIndependentlyOfTheRunDirection(bool rtl)
    {
        var run = new Run(Words); var paragraph = new Paragraph(run); Css.SetStyle(paragraph, "direction:" + (rtl ? "rtl" : "ltr") + ";");
        var box = Editor(paragraph); Attach(box, run, "direction:" + (rtl ? "ltr" : "rtl") + ";border-image:url('nine.png') 10 fill / 4px;"); Ready(box, run);
        var pieces = box.GetDocumentDecorationFragments(run); Assert.True(pieces.Length > 1);
        Assert.Equal(!rtl, pieces[0].LeftEdge); Assert.Equal(rtl, pieces[0].RightEdge);
        Assert.Equal(rtl ? Colors.Blue : Colors.Red, Pixel(box, pieces[0].Rect, rtl ? -2 : 1, 1));
    }

    [Fact]
    public void DisconnectedBidiPiecesLeaveInterveningSiblingRangesUnpainted()
    {
        // Opposite-direction text produces a source which can occupy several visual pieces.
        var span = new Span(); span.Inlines.Add(new Run("abc ")); span.Inlines.Add(new Run("אבג "));
        var other = new Run("דהו 123"); var paragraph = new Paragraph(); paragraph.Inlines.Add(span); paragraph.Inlines.Add(other);
        var box = Editor(paragraph); Attach(box, span, "border-image:image(red) 0 fill / 0;"); Css.SetStyle(other, "background:blue;"); Layout(box, 600);
        var ours = box.GetDocumentDecorationFragments(span); var theirs = box.GetDocumentDecorationFragments(other); Assert.True(ours.Length > 1); Assert.NotEmpty(theirs);
        foreach (var piece in theirs)
        {
            Assert.All(ours, own => Assert.False(own.Rect.IntersectsWith(new Rect(piece.Rect.X + .1, piece.Rect.Y, Math.Max(0, piece.Rect.Width - .2), piece.Rect.Height))));
            Assert.Equal(Colors.Blue, Pixel(box, piece.Rect, 2, 2));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedSectionsAndListsSharePaintSelectionHitAndImeCoordinates(bool list)
    {
        var run = new Run(Words); var paragraph = new Paragraph(run) { Margin = new(7, 0, 0, 11) };
        var section = new Section { Margin = new(13, 0, 0, 9) };
        if (list) { var items = new DocList { Margin = new(5, 0, 0, 6) }; var item = new ListItem(); item.Blocks.Add(paragraph); items.ListItems.Add(item); section.Blocks.Add(items); }
        else section.Blocks.Add(paragraph);
        var tail = new Paragraph(new Run("最后一段")); var box = Editor(section, tail); Attach(box, run, "border-image:image(red) 0 fill / 0;"); Layout(box, 250);
        var piece = box.GetDocumentDecorationFragments(run)[0].Rect;
        Assert.Equal(8 + 13 + 7 + (list ? 25 : 0), piece.X, 3);
        Assert.True(((IImeSupport)box).TrySetImeSelection(0, 0)); var caret = ((IImeSupport)box).GetImeCaretRectangle(); Assert.Equal(piece.X, caret.X, 3);
        var hit = box.GetPositionFromPoint(new Point(caret.X + .1, caret.Y + caret.Height / 2), false); Assert.NotNull(hit); Assert.Equal(0, hit.DocumentOffset);
        Assert.Equal(Colors.Red, Pixel(box, piece, 2, 2));
    }

    [Fact]
    public void PendingFailureAndSourceRemovalRetainTheOriginalBackgroundAndSelection()
    {
        var run = new Run(Words) { Background = Brushes.White }; var box = Editor(new Paragraph(run)); var images = new Images(true);
        Attach(box, run, "border-image:url('nine.png') 10 fill / 4px;", images); Layout(box); var piece = box.GetDocumentDecorationFragments(run)[0].Rect;
        Assert.Equal(Colors.White, Pixel(box, piece, 1, 1)); images.Open(); Ready(box, run); Assert.Equal(Colors.Red, Pixel(box, piece, 1, 1));
        Assert.True(((IImeSupport)box).TrySetImeSelection(2, 6)); Css.SetStyle(run, "border-image:url('bad.png') 10 fill / 4px;");
        foreach (var resource in Resources(run)) Wait(() => resource.Ready.IsCompleted); Layout(box);
        Assert.Equal(Colors.White, Pixel(box, piece, 1, 1)); Css.SetStyle(run, "border-image:none;"); Layout(box);
        Assert.Equal(Words.Substring(2, 6), box.Selection.Text); Assert.Same(run, ((Paragraph)box.Document.Blocks[0]).Inlines[0]);
    }

    [Fact]
    public void VisualHostDensityAndInheritedSourcesUpdateNonvisualConsumers()
    {
        var run = new Run(Words); var span = new Span(); span.Inlines.Add(run); var box = Editor(new Paragraph(span));
        Attach(box, span, "border-image:image-set('nine.png' 1x,'high.png' 2x) calc(100% / 3) fill / 4px;"); Css.SetStyle(run, "border-image:inherit;"); Ready(box, span, run);
        var first = Resources(run)[0]; var piece = box.GetDocumentDecorationFragments(run)[0].Rect; Assert.Equal(Colors.Red, Pixel(box, piece, 1, 1));
        VisualTreeHelper.SetRootDpi(box, new(2, 2)); Ready(box, span, run); Assert.Equal(Colors.Orange, Pixel(box, piece, 1, 1));
        Assert.NotSame(first, Resources(run)[0]); VisualTreeHelper.SetRootDpi(box, new(1, 1)); Ready(box, span, run); Assert.Same(first, Resources(run)[0]);
        Css.SetStyle(span, "border-image:image(blue) 0 fill / 0;"); Layout(box); Assert.Equal(Colors.Blue, Pixel(box, piece, 2, 2));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AlphaCenterPaintsOnceAndOutsetAtAnOffscreenLineRemainsVisible(bool outset)
    {
        var run = new Run(Words); var box = Editor(new Paragraph(run)); Attach(box, run, outset ? "border-image:image(red) 0 fill / 0 / 12px;" : "border-image:image(rgb(255 0 0 / .5)) 0 fill / 0;"); Layout(box, 200, 100);
        var piece = box.GetDocumentDecorationFragments(run)[0].Rect;
        if (!outset) Assert.Equal(Color.FromRgb(255, 127, 127), Pixel(box, piece, 2, 2));
        else
        {
            box.VerticalOffsetCore = piece.Height + 6; Layout(box, 200, 100);
            // The preceding line is fully clipped, but its outset still overlaps the viewport.
            Assert.Equal(Colors.Red, Sample(box, (int)Content(box).X + 2, (int)Content(box).Y + 2));
            Assert.Equal(Colors.White, Sample(box, 2, 2));
        }
    }

    [Theory]
    [InlineData("linear-gradient(red,red)")] [InlineData("cross-fade(red,red)")] [InlineData("url('vector.svg')")] [InlineData("image('bad.png',red)")]
    public void RichDocumentUsesTheSharedImagePipeline(string source)
    {
        var run = new Run(Words); var box = Editor(new Paragraph(run)); Attach(box, run, "border-image:" + source + " 0 fill / 0;"); Layout(box);
        foreach (var resource in Resources(run)) Wait(() => resource.Ready.IsCompleted); Layout(box);
        Assert.Equal(Colors.Red, Pixel(box, box.GetDocumentDecorationFragments(run)[0].Rect, 2, 2));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeEditorBorderIsSuppressedByAValidImageAndRestoredAfterRemoval(bool transparent)
    {
        var box = Editor(new Paragraph(new Run(Words))); box.BorderBrush = Brushes.Green; box.BorderThickness = new(4);
        Css.SetStyle(box, $"border-image:image({(transparent ? "transparent" : "red")}) 10 fill / 4px;"); Layout(box);
        Assert.NotNull(box.GetValue(CssBorderImageProperties.SourceProperty));
        Assert.True(CssBorderImagePainter.Active(box, new(box.RenderSize), box.BorderThickness));
        Assert.Equal(transparent ? Colors.White : Colors.Red, Sample(box, 1, 1));
        Css.SetStyle(box, "border-image:none;"); Layout(box); Assert.Equal(Colors.Green, Sample(box, 1, 1));
    }

    [Fact]
    public void ReflowAndFontUpdatesKeepSourcePointersAndSelection()
    {
        var run = new Run(Words); var box = Editor(new Paragraph(run)); Attach(box, run, "border-image:image(red) 0 fill / 0;"); Layout(box);
        var original = box.GetDocumentDecorationFragments(run); var start = box.Document.GetPositionAtOffset(2, LogicalDirection.Forward)!;
        box.Selection.Select(start, box.Document.GetPositionAtOffset(8, LogicalDirection.Forward)!); Layout(box, 350); Assert.True(box.GetDocumentDecorationFragments(run).Length < original.Length);
        Layout(box, 200); Assert.Equal(original, box.GetDocumentDecorationFragments(run)); Assert.Same(start, box.Selection.Start);
        run.FontSize = 28; Layout(box); Assert.True(box.GetDocumentDecorationFragments(run).Length > original.Length);
        Assert.Same(run, ((Paragraph)box.Document.Blocks[0]).Inlines[0]); Assert.Equal(Words.Substring(2, 6), box.Selection.Text);
    }

    [Fact]
    public void ExternalEditingUndoRedoRetainsOriginalInlineStyling()
    {
        var run = new Run(Words); var span = new Span(); span.Inlines.Add(run); var box = Editor(new Paragraph(span)); box.IsReadOnly = false;
        Attach(box, span, "border-image:image(red) 0 fill / 0;"); Layout(box);
        run.Text += "新增"; Layout(box); Assert.True(box.Undo()); Layout(box);
        Assert.Equal(Words, run.Text); Assert.Same(span, ((Paragraph)box.Document.Blocks[0]).Inlines[0]); Assert.Same(run, span.Inlines[0]);
        Assert.Equal(Colors.Red, Pixel(box, box.GetDocumentDecorationFragments(span)[0].Rect, 2, 2));
        Assert.True(box.Redo()); Layout(box); Assert.Equal(Words + "新增", run.Text);
        Assert.Equal(Colors.Red, Pixel(box, box.GetDocumentDecorationFragments(span)[0].Rect, 2, 2));
    }

    [Fact]
    public void LegacyRunLayoutUsesTheSameDecorationPainter()
    {
        using var software = RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        var run = new Run("正文"); var box = Editor(new Paragraph(run)); Attach(box, run, "background:blue;border-image:image(red) 0 fill / 0;"); Layout(box);
        var piece = Assert.Single(box.GetDocumentDecorationFragments(run)); Assert.Equal(Colors.Red, Pixel(box, piece.Rect, 2, 2));
    }

    [Fact]
    public void AnimatedPixelsDoNotRebuildTheNativeParagraphOrModifyDocumentContent()
    {
        var run = new Run(Words); var box = Editor(new Paragraph(run)); Attach(box, run, "border-image:url('animated.gif') 0 fill / 0;"); Ready(box, run);
        var cacheField = typeof(RichTextBox).GetField("_layoutCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bitmap = (BitmapImage)Resources(run)[0].Get(CssNode.Get(run))!;
        Wait(() => bitmap.AnimatedSubstitute is not null); var animation = bitmap.AnimatedSubstitute!; animation.Stop(); var changes = 0; run.TextContentChanged += (_, _) => changes++;
        var rect = box.GetDocumentDecorationFragments(run)[0].Rect;
        var original = cacheField.GetValue(box); Assert.NotNull(original);
        try
        {
            animation.CurrentFrameIndex = 0; Flush(); var first = Pixel(box, rect, 2, 2); box.ClearRenderDirty();
            animation.CurrentFrameIndex = 1; Flush(); Assert.True(box.IsRenderDirty); Assert.Equal(0, changes); Assert.Same(original, cacheField.GetValue(box));
            var second = Pixel(box, rect, 2, 2); Assert.NotEqual(first, second); Assert.Equal(Colors.White, second);
            Css.SetStyle(run, "border-image:none;"); Layout(box); box.ClearRenderDirty(); animation.CurrentFrameIndex = 0; Flush(); Assert.False(box.IsRenderDirty);
        }
        finally { animation.Stop(); }
    }

    [Fact]
    public void DecorationChangesPreserveTheEditorTabTargetAndAutomationTextProvider()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var run = new Run(Words); var box = Editor(new Paragraph(run));
        var before = new TextBlock { Text = "之前", Focusable = true }; var after = new TextBlock { Text = "之后", Focusable = true };
        var root = new StackPanel(); root.Children.Add(before); root.Children.Add(box); root.Children.Add(after); box.Height = 180;
        foreach (var element in new FrameworkElement[] { before, box, after }) KeyboardNavigation.SetIsTabStop(element, true);
        var window = new DisplayedTestWindow { Content = root, Width = 260, Height = 500, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Attach(box, run, "border-image:image(red) 0 fill / 0;");
            window.Measure(new(260, 500)); window.Arrange(new(0, 0, 260, 500)); window.UpdateLayout(); Flush();
            var peer = box.GetAutomationPeer()!; var provider = Assert.IsAssignableFrom<Jalium.UI.Automation.Provider.ITextProvider>(peer.GetPattern(Jalium.UI.Automation.Peers.PatternInterface.Text));
            Assert.Equal(box.Document.GetText(), provider.DocumentRange.GetText(-1));
            Assert.True(before.Focus()); Assert.True(KeyboardNavigation.MoveFocus(before)); Assert.Same(box, Keyboard.FocusedElement);
            Css.SetStyle(run, "border-image:none;"); Flush(); Assert.Same(box, Keyboard.FocusedElement); Assert.Same(peer, box.GetAutomationPeer());
            Assert.Equal(box.Document.GetText(), provider.DocumentRange.GetText(-1)); Assert.True(KeyboardNavigation.MoveFocus(box)); Assert.Same(after, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void OpaqueSelectionIsAboveInlineImagesAndBehindNativeGlyphs()
    {
        var run = new Run(Words); var box = Editor(new Paragraph(run)); box.SelectionBrush = Brushes.Blue;
        Attach(box, run, "border-image:image(red) 0 fill / 0;"); Layout(box); Assert.True(((IImeSupport)box).TrySetImeSelection(2, 4));
        var drawing = new OrderCapture(box.SelectionBrush); box.Render(drawing);
        var image = drawing.Events.IndexOf("image"); var selection = drawing.Events.IndexOf("selection"); var glyph = drawing.Events.IndexOf("glyph");
        Assert.True(image >= 0 && selection > image && glyph > selection, string.Join(',', drawing.Events));
    }

    private sealed class OrderCapture(Brush? selection) : DrawingContextAdapter
    {
        internal List<string> Events = [];
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect)
        { if (ReferenceEquals(brush, selection)) Events.Add("selection"); }
        public override void DrawImage(ImageSource image, Rect rect) => Events.Add("image");
        public override void DrawImage(ImageSource image, Rect rect, BitmapScalingMode mode) => Events.Add("image");
        public override void DrawText(FormattedText text, Point origin) => Events.Add("glyph");
        public override void DrawLine(Pen pen, Point start, Point end) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }

    private static readonly DependencyProperty[] ImageProperties = [CssBorderImageProperties.SourceProperty, CssBorderImageProperties.SliceProperty, CssBorderImageProperties.WidthProperty, CssBorderImageProperties.OutsetProperty, CssBorderImageProperties.RepeatProperty];
    private static RichTextBox Editor(params Block[] blocks)
    {
        var document = new FlowDocument { FontSize = 16, FontFamily = "PingFang SC", Foreground = Brushes.Transparent };
        foreach (var block in blocks) document.Blocks.Add(block);
        return new RichTextBox(document) { Background = Brushes.White, Foreground = Brushes.Transparent, BorderThickness = new(0), Padding = new(8), IsReadOnly = true };
    }
    private static Images Attach(RichTextBox box, DependencyObject source, string value, Images? images = null)
    {
        images ??= new(); Css.SetClass(source, "piece"); var sheet = CssStyleSheet.Parse(".piece{" + value + "}", "rich-document-image", new Uri("file:///rich-document/"), images);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(box).Add(sheet); Flush(); return images;
    }
    private static CssBackgroundImageResource[] Resources(DependencyObject owner) => (CssBackgroundImageResource[]?)owner.GetValue(CssBorderImageProperties.ResourcesProperty) ?? [];
    private static Rect Content(RichTextBox box) => new(box.BorderThickness.Left + box.Padding.Left, box.BorderThickness.Top + box.Padding.Top,
        box.RenderSize.Width - box.BorderThickness.Left - box.BorderThickness.Right - box.Padding.Left - box.Padding.Right, box.RenderSize.Height - box.BorderThickness.Top - box.BorderThickness.Bottom - box.Padding.Top - box.Padding.Bottom);
    private static void Ready(RichTextBox box, params DependencyObject[] sources)
    {
        for (var i = 0; i < 3; i++)
        {
            Layout(box); foreach (var source in sources) foreach (var resource in Resources(source))
            { Wait(() => resource.Ready.IsCompleted); if (resource.Get(CssNode.Get(source)) is BitmapImage bitmap) { var task = bitmap.PreloadAsync(300, 200); Wait(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); } }
        }
        Layout(box);
    }
    private static void Layout(RichTextBox box, double width = 200, double height = 600)
    { for (var i = 0; i < 4; i++) { Flush(); box.Measure(new(width, height)); box.Arrange(new(0, 0, width, height)); box.UpdateLayout(); } }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Wait(Func<bool> ready)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); } Assert.True(ready()); Flush(); }
    private static Color Pixel(RichTextBox box, Rect rect, double x, double y) => Sample(box, (int)(x >= 0 ? rect.X + x : rect.Right + x), (int)(rect.Y + y));
    private static Color Sample(RichTextBox box, int x, int y)
    {
        var w = (int)Math.Ceiling(box.ActualWidth); var h = (int)Math.Ceiling(box.ActualHeight); Assert.InRange(x, 0, w - 1); Assert.InRange(y, 0, h - 1);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap); box.Render(dc); dc.Close(); var p = bitmap.GetPixelBuffer(); var i = (y * w + x) * 4;
        return Color.FromArgb(p[i + 3], p[i + 2], p[i + 1], p[i]);
    }
    private sealed class Images(bool gated = false) : ICssResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open() => _gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (gated) await _gate.Task;
            if (uri.LocalPath.EndsWith("bad.png")) return new(uri, new MemoryStream([1, 2, 3]));
            if (uri.LocalPath.EndsWith("animated.gif")) return new(uri, new MemoryStream(Gif()));
            if (uri.LocalPath.EndsWith("vector.svg")) return new(uri, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='30' height='30'><rect width='30' height='30' fill='red'/></svg>")));
            var high = uri.LocalPath.EndsWith("high.png"); var pattern = uri.LocalPath.EndsWith("pattern.png");
            Color[] colors = [high ? Colors.Orange : Colors.Red, Colors.Lime, Colors.Blue, Colors.Cyan, Colors.Purple, Colors.Yellow, Colors.White, Colors.Black, Colors.Magenta];
            var data = new byte[3600]; for (var y = 0; y < 30; y++) for (var x = 0; x < 30; x++)
            {
                var c = colors[y / 10 * 3 + x / 10]; if (pattern && y < 10 && x is >= 10 and < 20) c = x % 5 < 2 ? Colors.Black : Colors.Lime;
                var i = (y * 30 + x) * 4; data[i] = c.B; data[i + 1] = c.G; data[i + 2] = c.R; data[i + 3] = c.A;
            }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(30, 30, 96, 96, PixelFormats.Bgra32, null, data, 120)));
            using var stream = new MemoryStream(); encoder.Save(stream); return new(uri, new MemoryStream(stream.ToArray()));
        }
        private static byte[] Gif()
        {
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)4); writer.Write((ushort)1); writer.Write(new byte[] { 0x80, 0, 0, 114, 180, 149, 0, 0, 0 });
            foreach (var samples in new[] { new[] { 0, 0, 1, 1 }, new[] { 1, 1, 0, 0 } })
            {
                writer.Write(new byte[] { 0x21, 0xf9, 4, 9, 1, 0, 1, 0, 0x2c, 0, 0, 0, 0, 4, 0, 1, 0, 0, 2, 4 });
                uint bits = 0; var shift = 0; foreach (var sample in samples) { bits |= (uint)4 << shift; shift += 3; bits |= (uint)sample << shift; shift += 3; }
                bits |= (uint)5 << shift; writer.Write(bits); writer.Write((byte)0);
            }
            writer.Write((byte)0x3b); return output.ToArray();
        }
    }
}
