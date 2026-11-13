using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssBorderImageInlineTests
{
    private const string Words = "跨行图片边框沿用实际排版坐标并保留正文原始选择位置与链接语义";

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void JoiningUsesUsedWidthsAndKeepsEachFragmentsHeight(bool clone, bool rtl)
    {
        CssInlineBoxFragment[] boxes = [new(new(20, 0, 70, 20), !rtl, rtl), new(new(0, 30, 90, 28), rtl, !rtl)];
        var joined = CssInlineBoxFragment.Join(boxes, clone, rtl);
        Assert.Equal(new Rect(20, 0, 70, 20), joined[0].Rect);
        Assert.Equal(clone ? 70 : 160, joined[0].DecorationRect.Width);
        Assert.Equal(clone ? 90 : 160, joined[1].DecorationRect.Width);
        Assert.Equal(20, joined[0].DecorationRect.Height); Assert.Equal(28, joined[1].DecorationRect.Height);
        Assert.Equal(clone ? 0 : rtl ? 90 : 0, joined[0].DecorationOffset);
        Assert.Equal(clone ? 0 : rtl ? 0 : 70, joined[1].DecorationOffset);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void NativeTextFragmentsJoinTheBorderImageWithOrWithoutABackground(bool clone, bool background)
    {
        var text = InlineText(Words); var root = Flow(InlineText("前文"), text);
        var images = Attach(root, $".piece{{display:inline;border:4px solid transparent;{(background ? "background:white;" : "")}border-image:url('nine.png') 10 fill / 4px;box-decoration-break:{(clone ? "clone" : "slice")};}}", text);
        Ready(root, images, text); Layout(root);
        Assert.True(text.IsCssInlineBox);
        CheckCorners(root, text, text.CssDecorationFragments, clone);
        var original = text.Text; text.Select(2, 6);
        Assert.Equal(original.Substring(2, 6), text.SelectedText); Assert.Equal(original, text.Text);
        Assert.Same(text, root.Children[1]);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void NativeContentFramesJoinTheirUsedFragments(bool clone, bool background)
    {
        var frame = new Border { Child = InlineText(Words), Background = background ? Brushes.White : null };
        var root = Flow(InlineText("前文"), frame);
        var images = Attach(root, $".piece{{display:inline;padding:2px;border:4px solid transparent;border-image:url('nine.png') 10 fill / 4px;box-decoration-break:{(clone ? "clone" : "slice")};}}", frame);
        Ready(root, images, frame); Layout(root);
        Assert.True(frame.IsCssInlineFrame);
        CheckCorners(root, frame, frame.CssInlineBox!.Fragments, clone);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void DocumentRunAndSpanPaintTheirOwnImagesAndKeepOriginalObjects(bool span, bool clone)
    {
        var run = new Run(Words); Inline element = run;
        if (span) { var container = new Span(); container.Inlines.Add(run); element = container; }
        var text = Document(element);
        var images = Attach(text, $".piece{{border-image:url('nine.png') 10 fill / 4px;box-decoration-break:{(clone ? "clone" : "slice")};}}", element);
        Ready(text, images, element); Layout(text);
        CheckCorners(text, text, text.GetDocumentDecorationFragments(element), clone);
        text.Select(2, 6); Assert.Equal(Words.Substring(2, 6), text.SelectedText);
        Assert.Same(element, text.Inlines[0]); Assert.Equal(Words, run.Text);
    }

    [Theory]
    [InlineData("stretch")] [InlineData("repeat")] [InlineData("round")] [InlineData("space")]
    public void FragmentedEdgesUseTheSamePatternPhaseAsTheJoinedBox(string repeat)
    {
        var text = InlineText(Words); var root = Flow(InlineText("前"), text);
        var images = Attach(root, $".piece{{display:inline;border:4px solid transparent;border-image:url('pattern.png') 10 / 4px {repeat};}}", text);
        Ready(root, images, text); Layout(root);
        var fragments = text.CssDecorationFragments; Assert.True(fragments.Length > 1);
        var width = (int)Math.Ceiling(root.ActualWidth); var height = (int)Math.Ceiling(root.ActualHeight);
        var reference = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(reference); drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height));
        var position = text.TransformToAncestor(root); var advance = 0d; var total = fragments.Sum(piece => piece.Rect.Width);
        foreach (var fragment in fragments)
        {
            var rect = new Rect(position.X + fragment.Rect.X, position.Y + fragment.Rect.Y, fragment.Rect.Width, fragment.Rect.Height);
            drawing.PushClip(new RectangleGeometry(rect));
            CssBorderImagePainter.Draw(text, drawing, new(rect.X - advance, rect.Y, total, rect.Height), new(4), false);
            drawing.Pop(); advance += rect.Width;
        }
        drawing.Close(); var expected = reference.GetPixelBuffer();
        foreach (var fragment in fragments)
        for (var offset = 6; offset < fragment.Rect.Width - 6; offset += 7)
        {
            // Keep identical pixel origins: real native font advances need not be integers.
            var x = (int)Math.Floor(fragment.Rect.X + position.X + offset);
            var y = (int)Math.Floor(position.Y + fragment.Rect.Y) + 1;
            var at = (y * width + x) * 4;
            var color = Color.FromArgb(expected[at + 3], expected[at + 2], expected[at + 1], expected[at]);
            Near(color, Sample(root, x, y), 1);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParentProgressionChoosesCutEdgesEvenWhenTheElementHasTheOppositeDirection(bool frame)
    {
        FrameworkElement piece = frame ? new Border { Child = InlineText(Words) } : InlineText(Words);
        var root = Flow(InlineText("前"), piece); Css.SetStyle(root, "display:flow-root;direction:rtl;font-size:16px;white-space:normal;padding:16px;");
        var images = Attach(root, ".piece{display:inline;direction:ltr;border:4px solid transparent;border-image:url('nine.png') 10 fill / 4px;}", piece);
        Ready(root, images, piece); Layout(root);
        var fragments = frame ? piece.CssInlineBox!.Fragments : ((TextBlock)piece).CssDecorationFragments;
        Assert.True(fragments.Length > 1); Assert.False(fragments[0].LeftEdge); Assert.True(fragments[0].RightEdge);
        Assert.True(fragments[^1].LeftEdge); Assert.False(fragments[^1].RightEdge);
        var position = piece.TransformToAncestor(root);
        Assert.Equal(Colors.Blue, Sample(root, (int)(position.X + fragments[0].Rect.Right) - 2, (int)(position.Y + fragments[0].Rect.Y) + 1));
        Assert.Equal(Colors.Red, Sample(root, (int)(position.X + fragments[^1].Rect.X) + 1, (int)(position.Y + fragments[^1].Rect.Y) + 1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DocumentCutsUseParentDirectionAndImageAnnotationsUseTheInlinesDirection(bool rtl)
    {
        var run = new Run(Words); var text = Document(run); text.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var images = Attach(text, $".piece{{direction:{(rtl ? "ltr" : "rtl")};border-image:image(ltr 'nine.png') 10 fill / 4px;}}", run);
        Ready(text, images, run); Layout(text);
        var pieces = text.GetDocumentDecorationFragments(run); Assert.True(pieces.Length > 1);
        Assert.Equal(!rtl, pieces[0].LeftEdge); Assert.Equal(rtl, pieces[0].RightEdge);
        var first = pieces[0].Rect;
        Assert.Equal(Colors.Blue, Sample(text, rtl ? (int)first.Right - 2 : (int)first.X + 1, (int)first.Y + 1));
    }

    [Fact]
    public void NestedDocumentPaintIsBehindTextAndInFrontOfTheAncestorsImage()
    {
        var before = new Run("前段"); var inner = new Run("内层内容"); var outer = new Span();
        outer.Inlines.Add(before); outer.Inlines.Add(inner); outer.Inlines.Add(new Run("尾段"));
        var text = Document(outer); Css.SetClass(outer, "outer");
        Attach(text, ".outer{border-image:image(red) 0 fill / 0;} .piece{border-image:image(blue) 0 fill / 0;}", inner);
        Layout(text); var range = text.GetDocumentDecorationFragments(inner)[0].Rect;
        Assert.Equal(Colors.Blue, Sample(text, (int)range.X + 3, (int)range.Y + 3));
        var parent = text.GetDocumentDecorationFragments(outer)[0].Rect;
        Assert.Equal(Colors.Red, Sample(text, (int)parent.X + 2, (int)parent.Y + 3));
        text.Select(1, 5); Assert.Equal(text.Text.Substring(1, 5), text.SelectedText);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AlphaFillPaintsOnlyOnceOnEveryFragment(bool document)
    {
        var run = new Run(Words); var text = document ? Document(run) : InlineText(Words);
        FrameworkElement root = document ? text : Flow(text);
        Attach(root, $".piece{{{(document ? "" : "display:inline;")}border-image:image(rgb(255 0 0 / .5)) 0 fill / 0;}}", document ? run : text);
        Layout(root); var fragments = document ? text.GetDocumentDecorationFragments(run) : text.CssDecorationFragments;
        Assert.True(fragments.Length > 1); var position = text.TransformToAncestor(root);
        foreach (var fragment in fragments)
            Near(Color.FromRgb(255, 128, 128), Sample(root, (int)(position.X + fragment.Rect.X) + 3, (int)(position.Y + fragment.Rect.Y) + 3), 1);
    }

    [Fact]
    public void LoadingClearingAndReplacingADocumentSourceInvalidatesTheOwningText()
    {
        var run = new Run(Words) { Background = Brushes.White }; var text = Document(run); var images = new Images(true);
        Attach(text, ".piece{border-image:url('nine.png') 10 fill / 4px;}", run, images); Layout(text);
        var rect = text.GetDocumentDecorationFragments(run)[0].Rect;
        Color Pixel() => Sample(text, (int)rect.X + 1, (int)rect.Y + 1);
        Assert.Equal(Colors.White, Pixel()); text.ClearRenderDirty(); images.Open(); Ready(text, images, run); Layout(text);
        Assert.Equal(Colors.Red, Pixel());
        Css.SetStyle(run, "border-image:image(blue) 0 fill / 0;"); Layout(text); Assert.Equal(Colors.Blue, Pixel());
        Css.SetStyle(run, "border-image:none;"); Layout(text); Assert.Equal(Colors.White, Pixel());
        Assert.Equal(Words, text.Text);
    }

    [Fact]
    public void DocumentImageSetUsesTheVisualHostsDpiAndReusesResidentResources()
    {
        var run = new Run(Words); var text = Document(run);
        var images = Attach(text, ".piece{border-image-source:image-set('nine.png' 1x,'high.png' 2x);border-image-slice:10; border-image-width:4px;}", run);
        Ready(text, images, run); Layout(text); var rect = text.GetDocumentDecorationFragments(run)[0].Rect;
        Color Pixel() => Sample(text, (int)rect.X + 1, (int)rect.Y + 1);
        Assert.Equal(Colors.Red, Pixel());
        var first = Resources(run)[0]; VisualTreeHelper.SetRootDpi(text, new(2, 2)); Ready(text, images, run); Layout(text);
        Assert.Equal(Colors.Orange, Pixel()); Assert.NotSame(first, Resources(run)[0]);
        VisualTreeHelper.SetRootDpi(text, new(1, 1)); Ready(text, images, run); Layout(text);
        Assert.Equal(Colors.Red, Pixel()); Assert.Same(first, Resources(run)[0]);
    }

    [Fact]
    public void DocumentOutsetExpandsOnlyInkAndReflowRestoresTheSameFragmentsAndSelection()
    {
        var run = new Run(Words); var text = Document(run); Attach(text, ".piece{border-image:image(red) 10 fill / 4px / 8px;}", run);
        Layout(text, 180); var first = text.GetDocumentDecorationFragments(run); var desired = text.DesiredSize;
        Assert.Equal(8, text.GetExtraDirtyPadding()); Assert.Equal(180, text.RenderSize.Width);
        Assert.False(text.GetPositionFromPoint(new(-5, 2), false) is not null);
        text.Select(2, 6); Layout(text, 300); Assert.True(text.GetDocumentDecorationFragments(run).Length < first.Length);
        Layout(text, 180); Assert.Equal(first, text.GetDocumentDecorationFragments(run)); Assert.Equal(Words.Substring(2, 6), text.SelectedText);
        Css.SetStyle(run, "border-image:none;"); Layout(text, 180); Assert.Equal(0, text.GetExtraDirtyPadding()); Assert.Equal(desired, text.DesiredSize);
    }

    [Fact]
    public void TrimmingUsesOnlyTheVisibleDocumentFragmentsAndDoesNotPaintPastTheHost()
    {
        var run = new Run(Words); var text = Document(run); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis;
        Attach(text, ".piece{border-image:image(red) 10 fill / 4px;}", run); Layout(text, 100);
        var fragment = Assert.Single(text.GetDocumentDecorationFragments(run)); Assert.InRange(fragment.Rect.Right, 1, 100.01);
        Assert.Equal(Words, text.Text); Assert.Equal(Colors.Red, Sample(text, (int)fragment.Rect.X + 1, (int)fragment.Rect.Y + 1));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void OutsetsDoNotWrapTheCutEdgesOfSlicedFragments(bool document, bool clone)
    {
        var run = new Run(Words); var text = document ? Document(run) : InlineText(Words);
        FrameworkElement root = document ? new Border { Padding = new(16), Background = Brushes.White, Child = text } : Flow(text);
        Attach(root, $".piece{{{(document ? "" : "display:inline;")}border-image:image(red) 0 fill / 4px / 6px;box-decoration-break:{(clone ? "clone" : "slice")};}}", document ? run : text);
        Layout(root); var pieces = document ? text.GetDocumentDecorationFragments(run) : text.CssDecorationFragments;
        Assert.True(pieces.Length > 1); var point = text.TransformToAncestor(root); var second = pieces[1].Rect;
        var actual = Sample(root, (int)(point.X + second.X) - 2, (int)(point.Y + second.Y + second.Height / 2));
        Assert.Equal(clone ? Colors.Red : Colors.White, actual);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExtremelyDenseRepeatedEdgesRemainVisibleAfterTheFirstFragment(bool document)
    {
        var run = new Run(Words); var text = document ? Document(run) : InlineText(Words);
        FrameworkElement root = document ? text : Flow(text);
        Attach(root, $".piece{{{(document ? "" : "display:inline;border:4px solid transparent;")}border-image:linear-gradient(red,red) 49.999999999999% / 4px repeat;}}", document ? run : text);
        Layout(root); var pieces = document ? text.GetDocumentDecorationFragments(run) : text.CssDecorationFragments;
        Assert.True(pieces.Length > 1); var point = text.TransformToAncestor(root); var rect = pieces[^1].Rect;
        Assert.Equal(Colors.Red, Sample(root, (int)(point.X + rect.X) + 7, (int)(point.Y + rect.Y) + 1));
    }

    [Fact]
    public void DocumentExplicitInheritanceRematerializesImagesAndObservesParentChanges()
    {
        var run = new Run(Words); var span = new Span(); span.Inlines.Add(run); var text = Document(span);
        Css.SetClass(span, "parent"); var images = Attach(text, ".parent{border-image:url('nine.png') 10 fill / 4px;} .piece{border-image:inherit;}", run);
        Ready(text, images, span, run); Layout(text);
        Assert.NotSame(span.GetValue(CssBorderImageProperties.SourceProperty), run.GetValue(CssBorderImageProperties.SourceProperty));
        Assert.Same(Resources(span)[0], Resources(run)[0]);
        Css.SetStyle(span, "border-image:image(blue) 0 fill / 0;"); Layout(text);
        var rect = text.GetDocumentDecorationFragments(run)[0].Rect;
        Assert.Equal(Colors.Blue, Sample(text, (int)rect.X + 2, (int)rect.Y + 2));
        Css.SetStyle(span, "border-image:none;"); Layout(text); Assert.Null(run.GetValue(CssBorderImageProperties.SourceProperty));
    }

    [Fact]
    public void HardLineBreaksKeepSourceOffsetsAndTheLastVisibleFragmentKeepsItsPhaseWhenClipped()
    {
        var span = new Span(); span.Inlines.Add(new Run("第一段")); span.Inlines.Add(new LineBreak()); span.Inlines.Add(new Run(Words));
        var text = Document(span); var images = Attach(text, ".piece{border-image:url('nine.png') 10 fill / 4px;}", span);
        Ready(text, images, span); Layout(text); var pieces = text.GetDocumentDecorationFragments(span); Assert.True(pieces.Length > 2);
        var last = pieces[^1].Rect; var width = (int)Math.Ceiling(text.ActualWidth); var height = (int)Math.Ceiling(text.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        dc.PushClip(new RectangleGeometry(last)); text.Render(dc); dc.Pop(); dc.Close(); var bytes = bitmap.GetPixelBuffer();
        var at = (((int)last.Y + 1) * width + (int)last.X + 1) * 4;
        Assert.Equal(Colors.Lime, Color.FromArgb(bytes[at + 3], bytes[at + 2], bytes[at + 1], bytes[at]));
        text.Select(0, 5); Assert.Equal(text.Text[..5], text.SelectedText); Assert.Contains('\n', text.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ImagesRetainTheOriginalTabTargetAndAccessibilityName(bool document)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var run = new Run(Words); var child = document ? Document(run) : InlineText(Words);
        var before = InlineText("之前"); var after = InlineText("之后");
        foreach (var text in new[] { before, child, after }) { text.Focusable = true; KeyboardNavigation.SetIsTabStop(text, true); }
        var root = Flow(before, child, after); var window = new DisplayedTestWindow { Content = root, Width = 260, Height = 500, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Attach(root, ".piece{border-image:image(red) 0 fill / 0;}", document ? run : child); Layout(window, 260);
            var peer = child.GetAutomationPeer(); Assert.NotNull(peer); Assert.Equal(Words, peer.GetName());
            Assert.True(before.Focus()); Assert.True(KeyboardNavigation.MoveFocus(before)); Assert.Same(child, Keyboard.FocusedElement);
            child.Select(2, 6); Css.SetStyle(document ? run : child, "border-image:none;"); Layout(window, 260);
            Assert.Same(child, Keyboard.FocusedElement); Assert.Same(peer, child.GetAutomationPeer()); Assert.Equal(Words, peer.GetName());
            Assert.Equal(Words.Substring(2, 6), child.SelectedText); Assert.True(KeyboardNavigation.MoveFocus(child)); Assert.Same(after, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnonymousTextBodiesKeepHostCoordinatesAndForwardDocumentInk(bool generated)
    {
        var run = new Run(Words); var text = Document(run);
        Css.SetStyle(text, "display:block;padding:14px;white-space:normal;line-height:2;");
        Attach(text, ".piece{border-image:image(red) 0 fill / 0 / 8px;}" + (generated ? " TextBlock::before{content:'前置';display:inline;}" : ""), run);
        Layout(text); var piece = text.GetDocumentDecorationFragments(run)[0].Rect;
        Assert.True(piece.Left >= 14); Assert.Equal(Colors.Red, Sample(text, (int)piece.X + 1, (int)piece.Y + 1));
        var body = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(text)).Select(i => VisualTreeHelper.GetChild(text, i)).OfType<FrameworkElement>().Single(child => child is not TextBlock);
        Assert.Equal(8, body.GetExtraDirtyPadding()); Assert.Equal(8, text.GetExtraDirtyPadding());
        Css.SetStyle(run, "border-image:none;"); Layout(text);
        // Generated sibling sources retain a one-DIP raster fringe after the
        // image outset is removed; an isolated anonymous body needs no seam pad.
        Assert.Equal(generated ? 1 : 0, body.GetExtraDirtyPadding()); Assert.Equal(generated ? 1 : 0, text.GetExtraDirtyPadding());
    }

    [Theory]
    [InlineData("url('vector.svg')")]
    [InlineData("linear-gradient(red,red)")]
    [InlineData("image('bad.png',red)")]
    [InlineData("cross-fade(red,red)")]
    public void DocumentConsumersShareTheGeneratedVectorAndFallbackImagePipeline(string source)
    {
        var run = new Run(Words); var text = Document(run); Attach(text, ".piece{border-image:" + source + " 0 fill / 0;}", run);
        Layout(text); foreach (var resource in Resources(run)) PumpUntil(() => resource.Ready.IsCompleted); Layout(text);
        var rect = text.GetDocumentDecorationFragments(run)[0].Rect;
        Assert.Equal(Colors.Red, Sample(text, (int)rect.X + 2, (int)rect.Y + 2));
    }

    [Theory]
    [InlineData(23.3)] [InlineData(19.77)] [InlineData(17.03)]
    public void ConstantMixedImagesKeepTheirColorWithFractionalFragmentMetrics(double lineHeight)
    {
        var run = new Run(Words); var text = Document(run);
        Css.SetStyle(text, "white-space:normal;line-height:" + lineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px;");
        Attach(text, ".piece{border-image:cross-fade(red,red) 0 fill / 0;}", run); Layout(text);
        foreach (var fragment in text.GetDocumentDecorationFragments(run))
            Assert.Equal(Colors.Red, Sample(text, (int)fragment.Rect.X + 2, (int)fragment.Rect.Y + 2));
    }

    [Fact]
    public void DocumentAnimationRefreshesPixelsAndRetiredFramesCannotInvalidateTheHost()
    {
        var run = new Run(Words); var text = Document(run); var images = Attach(text, ".piece{border-image:url('animated.gif') 0 fill / 0;}", run);
        Ready(text, images, run); Layout(text); var bitmap = (BitmapImage)Resources(run)[0].Get(CssNode.Get(run))!;
        PumpUntil(() => bitmap.AnimatedSubstitute is not null); var animation = bitmap.AnimatedSubstitute!; animation.Stop();
        var contentChanges = 0; run.TextContentChanged += (_, _) => contentChanges++;
        var rect = text.GetDocumentDecorationFragments(run)[0].Rect;
        try
        {
            animation.CurrentFrameIndex = 0; Flush(); var first = Sample(text, (int)rect.X + 2, (int)rect.Y + 2); text.ClearRenderDirty();
            animation.CurrentFrameIndex = 1; Flush(); Assert.True(text.IsRenderDirty); var second = Sample(text, (int)rect.X + 2, (int)rect.Y + 2);
            Assert.Equal(0, contentChanges);
            Assert.NotEqual(first, second); Assert.Equal(Colors.White, second);
            Css.SetStyle(run, "border-image:none;"); Layout(text); text.ClearRenderDirty(); animation.CurrentFrameIndex = 0; Flush(); Assert.False(text.IsRenderDirty);
        }
        finally { animation.Stop(); }
    }

    private static void CheckCorners(FrameworkElement root, FrameworkElement owner, CssInlineBoxFragment[] pieces, bool clone)
    {
        Assert.True(pieces.Length > 1); var position = owner.TransformToAncestor(root);
        for (var i = 0; i < pieces.Length; i++)
        {
            var rect = pieces[i].Rect; Assert.True(rect.Width > 8);
            Assert.Equal(i == 0 || clone ? Colors.Red : Colors.Lime, Sample(root, (int)(position.X + rect.X) + 1, (int)(position.Y + rect.Y) + 1));
            Assert.Equal(i == pieces.Length - 1 || clone ? Colors.Blue : Colors.Lime, Sample(root, (int)(position.X + rect.Right) - 2, (int)(position.Y + rect.Y) + 1));
        }
    }

    private static TextBlock InlineText(string value)
    { var text = new TextBlock { Text = value, FontSize = 16, Foreground = Brushes.Transparent, IsTextSelectionEnabled = true }; Css.SetStyle(text, "display:inline;"); return text; }
    private static TextBlock Document(Inline inline)
    { var text = new TextBlock { FontSize = 16, Foreground = Brushes.Transparent, Background = Brushes.White, IsTextSelectionEnabled = true }; text.Inlines.Add(inline); Css.SetStyle(text, "white-space:normal;line-height:2;"); return text; }
    private static StackPanel Flow(params UIElement[] elements)
    { var root = new StackPanel { Background = Brushes.White }; foreach (var child in elements) root.Children.Add(child); Css.SetStyle(root, "display:flow-root;font-size:16px;white-space:normal;padding:16px;"); return root; }
    private static Images Attach(FrameworkElement root, string css, DependencyObject piece, Images? images = null)
    {
        images ??= new(); Css.SetClass(piece, "piece"); var sheet = CssStyleSheet.Parse(css, "border-inline", new Uri("file:///border-inline/"), images);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(root).Add(sheet); Flush(); return images;
    }
    private static CssBackgroundImageResource[] Resources(DependencyObject owner) => (CssBackgroundImageResource[]?)owner.GetValue(CssBorderImageProperties.ResourcesProperty) ?? [];
    private static void Ready(FrameworkElement root, Images _, params DependencyObject[] owners)
    {
        for (var round = 0; round < 3; round++)
        {
            Layout(root);
            foreach (var owner in owners) foreach (var resource in Resources(owner))
            {
                PumpUntil(() => resource.Ready.IsCompleted); Assert.True(resource.Ready.IsCompletedSuccessfully);
                if (resource.Get(CssNode.Get(owner)) is BitmapImage bitmap) { var task = bitmap.PreloadAsync(200, 200); PumpUntil(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); }
            }
        }
        Layout(root);
    }
    private static void Layout(FrameworkElement root, double width = 180)
    { for (var i = 0; i < 5; i++) { Flush(); root.Measure(new(width, double.PositiveInfinity)); root.Arrange(new(0, 0, width, Math.Max(1, root.DesiredSize.Height))); root.UpdateLayout(); } }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void PumpUntil(Func<bool> condition)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!condition() && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); } Assert.True(condition()); Flush(); }
    private static Color Sample(FrameworkElement owner, int x, int y)
    {
        var width = (int)Math.Ceiling(owner.ActualWidth); var height = (int)Math.Ceiling(owner.ActualHeight);
        Assert.InRange(x, 0, width - 1); Assert.InRange(y, 0, height - 1);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(target); owner.Render(dc); dc.Close();
        var bytes = target.GetPixelBuffer(); var at = (y * width + x) * 4; return Color.FromArgb(bytes[at + 3], bytes[at + 2], bytes[at + 1], bytes[at]);
    }
    private static void Near(Color expected, Color actual, int tolerance)
    { Assert.InRange(Math.Abs(expected.R - actual.R), 0, tolerance); Assert.InRange(Math.Abs(expected.G - actual.G), 0, tolerance); Assert.InRange(Math.Abs(expected.B - actual.B), 0, tolerance); Assert.InRange(Math.Abs(expected.A - actual.A), 0, tolerance); }

    private sealed class Images(bool gated = false) : ICssResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open() => _gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (gated) await _gate.Task;
            if (uri.LocalPath.EndsWith("vector.svg")) return new(uri, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='30' height='30'><rect width='30' height='30' fill='red'/></svg>")));
            if (uri.LocalPath.EndsWith("bad.png")) return new(uri, new MemoryStream([1, 2, 3]));
            if (uri.LocalPath.EndsWith("animated.gif")) return new(uri, new MemoryStream(Gif()));
            var high = uri.LocalPath.EndsWith("high.png"); var pattern = uri.LocalPath.EndsWith("pattern.png");
            Color[] colors = [high ? Colors.Orange : Colors.Red, Colors.Lime, Colors.Blue, Colors.Cyan, Colors.Purple, Colors.Yellow, Colors.White, Colors.Black, Colors.Magenta];
            var pixels = new byte[30 * 30 * 4];
            for (var y = 0; y < 30; y++) for (var x = 0; x < 30; x++)
            {
                var color = colors[y / 10 * 3 + x / 10];
                if (pattern && y < 10 && x is >= 10 and < 20) color = (x % 5) < 2 ? Colors.Black : Colors.Lime;
                var at = (y * 30 + x) * 4; pixels[at] = color.B; pixels[at + 1] = color.G; pixels[at + 2] = color.R; pixels[at + 3] = color.A;
            }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(30, 30, 96, 96, PixelFormats.Bgra32, null, pixels, 120)));
            using var stream = new MemoryStream(); encoder.Save(stream); return new(uri, new MemoryStream(stream.ToArray()));
        }
        private static byte[] Gif()
        {
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)4); writer.Write((ushort)1); writer.Write(new byte[] { 0x80, 0, 0, 114, 180, 149, 0, 0, 0 });
            foreach (var samples in new[] { new[] { 0, 0, 1, 1 }, new[] { 1, 1, 0, 0 } })
            {
                writer.Write(new byte[] { 0x21, 0xf9, 4, 9, 1, 0, 1, 0, 0x2c, 0, 0, 0, 0, 4, 0, 1, 0, 0, 2, 4 });
                uint packed = 0; var bits = 0; foreach (var value in samples) { packed |= (uint)4 << bits; bits += 3; packed |= (uint)value << bits; bits += 3; }
                packed |= (uint)5 << bits; writer.Write(packed); writer.Write((byte)0);
            }
            writer.Write((byte)0x3b); return output.ToArray();
        }
    }
}
