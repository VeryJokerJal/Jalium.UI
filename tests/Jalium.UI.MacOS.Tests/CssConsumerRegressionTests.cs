using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssConsumerRegressionTests
{
    [Theory]
    [InlineData(RenderBackend.Software)]
    public void HiddenContentPreservesItsBoxAndAuthoredDescendantValues(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var text = new TextBlock { Text = "CONTENT", FontSize = 28, Foreground = Brushes.Red,
            Opacity = 1, IsHitTestVisible = true };
        var host = new Border { Width = 180, Height = 90, Background = Brushes.Blue, Child = text };
        Css.SetStyle(host, "content-visibility:hidden;"); Layout(host);
        var pixels = Pixels(context, host);
        Assert.All(Enumerable.Range(0, 180 * 90), i =>
        {
            Assert.Equal(255, pixels[4 * i]);
            Assert.Equal(0, pixels[4 * i + 2]);
            Assert.Equal(255, pixels[4 * i + 3]);
        });
        Assert.Equal(1d, text.Opacity);
        Assert.Equal(true, text.ReadLocalValue(UIElement.IsHitTestVisibleProperty));
        Assert.False(text.IsHitTestVisible);
        Assert.Empty(host.GetAutomationPeer()!.GetChildren());
        Css.SetStyle(host, "content-visibility:visible;"); Layout(host);
        Assert.True(text.IsHitTestVisible);
        var visible = Pixels(context, host);
        Assert.True(Enumerable.Range(0, visible.Length / 4).Any(i => visible[i * 4 + 2] > 0));
        text.ClearValue(UIElement.OpacityProperty);
        text.SetLayerValue(UIElement.OpacityProperty, .6, DependencyObject.LayerValueSource.ParentTemplate);
        Css.SetStyle(host, "content-visibility:hidden;"); Layout(host);
        Assert.Equal(.6, text.Opacity);
        Css.SetStyle(host, "content-visibility:visible;"); Layout(host);
        Assert.Equal(.6, text.Opacity);
    }

    [Theory]
    [InlineData(RenderBackend.Software, false)]
    [InlineData(RenderBackend.Software, true)]
    public void SkippedOwnTextAndRecordedFramesDoNotLeakLaterVisibility(RenderBackend backend, bool auto)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var text = new TextBlock { Text = "HIDDEN", FontSize = 28, Foreground = Brushes.Black, Width = 180, Height = 90 };
        Css.SetStyle(text, auto ? "content-visibility:auto;" : "content-visibility:hidden;");
        if (auto) CssContainmentProperties.SetOffscreenForAutoVisibility(text, true);
        Layout(text);
        var hidden = Pixels(context, text);
        Assert.All(hidden, value => Assert.Equal(0, value));
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(text, "content-visibility:visible;"); Layout(text);
        Assert.Contains(Pixels(context, text), value => value != 0);
        Assert.Equal(hidden, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        Css.SetStyle(text, "content-visibility:hidden;"); Layout(text);
        Assert.Empty(text.GetAutomationPeer()!.GetName());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MotionPathComposesWithAndRestoresTheOriginalTransform(bool native)
    {
        var actor = new Border { Width = 20, Height = 10 };
        var root = new Canvas { Width = 300, Height = 90 }; root.Children.Add(actor);
        var baseTransform = new TranslateTransform(40, 0);
        if (native) actor.RenderTransform = baseTransform;
        var ordinary = native ? "" : "transform:translateX(40px);";
        const string path = "offset-path:path('M 0 0 L 200 0');offset-distance:50%;offset-rotate:0deg;offset-anchor:0px 0px;transform-origin:0px 0px;";
        for (var i = 0; i < 3; i++)
        {
            Css.SetStyle(actor, ordinary + path + "contain:layout;"); Layout(root, 300);
            Assert.Equal(140, actor.RenderTransform!.Value.OffsetX, 4);
            Assert.Equal(140, actor.TransformToAncestor(root).X, 4);
            Css.SetStyle(actor, ordinary + "offset-path:none;transform-origin:0px 0px;"); Layout(root, 300);
            Assert.Equal(40, actor.RenderTransform!.Value.OffsetX, 4);
        }
        if (native)
        {
            Assert.Same(baseTransform, actor.ReadLocalValue(UIElement.RenderTransformProperty));
            Css.SetStyle(actor, path); Layout(root, 300);
            baseTransform.X = 65;
            Assert.Equal(165, actor.TransformToAncestor(root).X, 4);
            Css.SetStyle(actor, "offset-path:none;"); Layout(root, 300);
            Assert.Same(baseTransform, actor.RenderTransform);
            Assert.Equal(65, actor.TransformToAncestor(root).X, 4);
        }
    }

    [Theory]
    [InlineData(RenderBackend.Software)]
    public void BorderAreaTextPaintsOneUnionWithTheBackgroundAlpha(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var box = new Border { Width = 180, Height = 90, Child = new TextBlock
            { Text = "INK", FontFamily = new FontFamily("Helvetica"), FontSize = 28, Foreground = Brushes.Transparent } };
        Css.SetStyle(box, "border:12px solid transparent;padding:8px;background-color:rgba(255,0,0,0.5);background-clip:border-area text;");
        Layout(box); var p = Pixels(context, box);
        Assert.InRange(p[(45 * 180 + 4) * 4 + 3], (byte)126, (byte)129);
        Assert.True(Enumerable.Range(0, 180 * 90).Any(i => i % 180 > 20 && i % 180 < 160 &&
            i / 180 > 20 && i / 180 < 70 && p[i * 4 + 3] > 0));
        Assert.All(Enumerable.Range(0, 180 * 90), i => Assert.InRange(p[i * 4 + 3], (byte)0, (byte)129));
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); box.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(box, "background:blue;"); Layout(box);
        Assert.Equal(p, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
    }

    [Fact]
    public void MotionPathPreservesTheOrdinaryTransformsDepthAndRestoresIt()
    {
        var actor = new Border { Width = 20, Height = 10 };
        var root = new Canvas { Width = 300, Height = 90 }; root.Children.Add(actor);
        const string ordinary = "transform:translate3d(10px,20px,30px);transform-origin:0px 0px;";
        const string path = "offset-path:path('M 0 0 L 200 0');offset-distance:50%;offset-anchor:0px 0px;offset-rotate:0deg;";
        for (var pass = 0; pass < 3; pass++)
        {
            Css.SetStyle(actor, ordinary + path); Layout(root, 300);
            Assert.True(CssTransform3D.Has3D(actor.RenderTransform));
            Assert.True(CssTransform3D.Needs3DRendering(actor));
            var matrix = CssTransform3D.GetTransformMatrix3D(actor.RenderTransform);
            Assert.Equal(110, matrix.OffsetX, 4);
            Assert.Equal(20, matrix.OffsetY, 4);
            Assert.Equal(30, matrix.OffsetZ, 4);
            Css.SetStyle(actor, ordinary + "offset-path:none;"); Layout(root, 300);
            Assert.True(CssTransform3D.Has3D(actor.RenderTransform));
            matrix = CssTransform3D.GetTransformMatrix3D(actor.RenderTransform);
            Assert.Equal(10, matrix.OffsetX, 4);
            Assert.Equal(20, matrix.OffsetY, 4);
            Assert.Equal(30, matrix.OffsetZ, 4);
        }
    }

    [Fact]
    public void MotionRotationKeepsTheOrdinaryTransformOriginAndMutableSource()
    {
        var scale = new ScaleTransform(2, 2);
        var actor = new Border { Width = 20, Height = 10, RenderTransform = scale,
            RenderTransformOrigin = new Point(.5, .5) };
        var root = new Canvas { Width = 300, Height = 90 }; root.Children.Add(actor);
        const string path = "offset-path:path('M 0 0 L 200 0');offset-distance:50%;offset-rotate:90deg;offset-anchor:0px 0px;";
        Css.SetStyle(actor, path); Layout(root, 300);
        var point = actor.TransformToAncestor(root);
        Assert.Equal(105, point.X, 4); Assert.Equal(-10, point.Y, 4);
        scale.ScaleX = 3; scale.ScaleY = 3;
        point = actor.TransformToAncestor(root);
        Assert.Equal(110, point.X, 4); Assert.Equal(-20, point.Y, 4);
        var replacement = new TranslateTransform(-15, 0);
        actor.RenderTransform = replacement;
        scale.ScaleX = 8; // The replaced source must no longer affect this element.
        point = actor.TransformToAncestor(root);
        Assert.Equal(100, point.X, 4); Assert.Equal(-15, point.Y, 4);
        Css.SetStyle(actor, "offset-path:none;"); Layout(root, 300);
        Assert.Same(replacement, actor.RenderTransform);
        point = actor.TransformToAncestor(root);
        Assert.Equal(-15, point.X, 4); Assert.Equal(0, point.Y, 4);
    }

    [Fact]
    public void EmbeddedInlineBackgroundCoversThePublicSourceRangeInActualPixels()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = new Run("A"); var last = new Run("B");
        var span = new Span(first) { Background = Brushes.Yellow };
        span.Inlines.Add(new InlineUIContainer(new Border { Width = 12, Height = 12, Background = Brushes.Green }));
        span.Inlines.Add(last);
        var document = new FlowDocument(new Paragraph(span)) { PagePadding = default, FontSize = 18 };
        var editor = new RichTextBox(document) { Width = 180, Height = 90, Padding = default, BorderThickness = default };
        Layout(editor);
        var firstRect = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var lastRect = last.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        var pixels = Pixels(context, editor);
        var yellow = Enumerable.Range(0, 180 * 90).Where(i => pixels[i * 4] == 0 &&
            pixels[i * 4 + 1] == 255 && pixels[i * 4 + 2] == 255 && pixels[i * 4 + 3] == 255).ToArray();
        Assert.NotEmpty(yellow);
        Assert.InRange(yellow.Min(i => i % 180), firstRect.Left - 1, firstRect.Left + 1);
        Assert.InRange(yellow.Max(i => i % 180) + 1, lastRect.Right - 1, lastRect.Right + 1);
        Assert.Contains(Enumerable.Range(0, 180 * 90), i => pixels[i * 4] == 0 && pixels[i * 4 + 1] == 128 &&
            pixels[i * 4 + 2] == 0 && pixels[i * 4 + 3] == 255);
    }

    [Fact]
    public void EmbeddedInlineOccupiesOneSourceUnitAndItsAllocatedHitRange()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { FontSize = 18 };
        text.Inlines.Add(new Run("A"));
        text.Inlines.Add(new InlineUIContainer(new Border { Width = 12, Height = 12, Background = Brushes.Green }));
        text.Inlines.Add(new Run("B"));
        Css.SetStyle(text, "display:block;"); Layout(text);
        Assert.Equal("A\uFFFCB", text.Text);
        var bounds = Assert.Single(text.DocumentTextBounds(1, 1));
        Assert.Equal(12, bounds.Width, 4); Assert.Equal(12, bounds.Height, 4);
        Assert.Equal(1, text.DocumentCharacterFromPoint(new(bounds.Left + 1, bounds.Top + 1)));
        Assert.Equal(2, text.DocumentCharacterFromPoint(new(bounds.Right - 1, bounds.Top + 1)));
        Assert.Equal(12, Assert.Single(text.GetSourceRangeSegments(1, 1)).EndX -
            Assert.Single(text.GetSourceRangeSegments(1, 1)).StartX, 4);
        text.SelectAll(); Assert.Equal("A\uFFFCB", text.SelectedText);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("paragraph")]
    [InlineData("document")]
    public void HiddenDocumentSourcesSuppressPaintAndExposedTextWithoutChangingOriginalText(string target)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var run = new Run("HIDDEN"); var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph) { PagePadding = default, FontSize = 24, Foreground = Brushes.Black };
        DependencyObject source = target == "run" ? run : target == "paragraph" ? paragraph : document;
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); Layout(body);
        Assert.Contains(Pixels(context, body), value => value != 0);
        Assert.Contains("HIDDEN", body.GetAutomationPeer()!.GetName());
        for (var pass = 0; pass < 2; pass++)
        {
            Css.SetStyle(source, "content-visibility:hidden;"); Layout(body);
            Assert.All(Pixels(context, body), value => Assert.Equal(0, value));
            Assert.DoesNotContain("HIDDEN", body.GetAutomationPeer()!.GetName());
            Assert.DoesNotContain("HIDDEN", run.GetAutomationPeer()!.GetName());
            Assert.Equal("HIDDEN\n", document.GetText());
            Css.SetStyle(source, "content-visibility:visible;"); Layout(body);
            Assert.Contains(Pixels(context, body), value => value != 0);
            Assert.Contains("HIDDEN", body.GetAutomationPeer()!.GetName());
        }
    }

    [Fact]
    public void FocusPreservesTheSkippedOwnerBoxAndRevealsAutoBeforeCallingTheProvider()
    {
        var previous = FocusService.Provider;
        var provider = new FocusProbe(); FocusService.Provider = provider;
        try
        {
            var child = new Border { Width = 40, Height = 20, Focusable = true };
            var host = new Border { Width = 180, Height = 90, Focusable = true, Child = child };
            Css.SetStyle(host, "content-visibility:hidden;"); Layout(host);
            Assert.True(host.Focus()); // The owner's box still participates in input.
            Assert.False(child.Focus());
            Assert.Same(host, provider.FocusedElement);
            FocusService.ClearFocus();
            Css.SetStyle(host, "content-visibility:auto;");
            CssContainmentProperties.SetOffscreenForAutoVisibility(host, true); Layout(host);
            Assert.True(CssContainmentProperties.IsContentSkipped(host));
            provider.BeforeFocus = () => Assert.False(CssContainmentProperties.IsContentSkipped(host));
            Assert.True(child.Focus());
            Assert.True(child.IsHitTestVisible);
        }
        finally { FocusService.ClearFocus(); FocusService.Provider = previous; }
    }

    private sealed class FocusProbe : IFocusProvider
    {
        public IInputElement? FocusedElement { get; private set; }
        internal Action? BeforeFocus;
        public IInputElement? Focus(IInputElement? element) { BeforeFocus?.Invoke(); return FocusedElement = element; }
        public void ClearFocus() => FocusedElement = null;
        public bool MoveFocus(UIElement element, FocusNavigationDirection direction) => false;
    }

    [Fact]
    public void IdentityMotionPathStillChangesAndRestoresTheFixedContainingBlock()
    {
        var child = new Border { Width = 10, Height = 10 };
        Css.SetStyle(child, "position:fixed;left:10px;top:10px;");
        var owner = new Border { Width = 100, Height = 50, Child = child };
        var root = new Canvas { Width = 300, Height = 90 };
        Canvas.SetLeft(owner, 50); root.Children.Add(owner); Layout(root, 300);
        Assert.Equal(10, child.TransformToAncestor(root).X, 4);
        Css.SetStyle(owner, "offset-path:path('M 0 0 L 200 0');offset-distance:0px;offset-anchor:0px 0px;offset-rotate:0deg;");
        Layout(root, 300);
        Assert.Equal(60, child.TransformToAncestor(root).X, 4);
        Css.SetStyle(owner, "offset-path:none;"); Layout(root, 300);
        Assert.Null(owner.RenderTransform);
        Assert.Equal(10, child.TransformToAncestor(root).X, 4);
    }

    private static void Layout(FrameworkElement root, int width = 180)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, 90)); root.Arrange(new Rect(0, 0, width, 90)); root.UpdateLayout();
        }
    }
    private static byte[] Pixels(RenderContext context, Visual root) => Pixels(context, root.Render);
    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(180, 90);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var p = new byte[180 * 90 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(p, 180 * 4, out _, out _)); return p;
    }
}
