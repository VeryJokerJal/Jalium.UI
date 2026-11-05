using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentPaintRefreshTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("run")]
    [InlineData("span")]
    [InlineData("paragraph")]
    [InlineData("section")]
    [InlineData("root")]
    [InlineData("cell")]
    [InlineData("list")]
    public void InPlaceSourceBrushChangesInvalidateEveryConsumerAndLeaveOtherDocumentsClean(string kind)
    {
        var run = new Run("原文"); var span = new Span(run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
        var document = new FlowDocument(section); var cell = new TableCell(); var list = new Jalium.UI.Documents.List();
        DependencyObject source = kind switch { "run" => run, "span" => span, "paragraph" => paragraph, "section" => section,
            "root" => document, "cell" => cell, _ => list };
        var brush = new SolidColorBrush(Colors.Green); source.SetValue(TextElement.BackgroundProperty, brush);
        var one = new Border(); var two = new Border(); var unrelated = new Border();
        using var first = CssDocumentConsumers.Register(source, one); using var second = CssDocumentConsumers.Register(source, two);
        _ = source.GetValue(TextElement.BackgroundProperty); Clear(one, two, unrelated);
        brush.Color = Colors.Blue;
        Assert.True(one.IsRenderDirty); Assert.True(two.IsRenderDirty); Assert.False(unrelated.IsRenderDirty);
        Assert.Same(span, run.Parent); Assert.Same(paragraph, span.Parent); Assert.Equal("原文\n", document.GetText());
    }

    [Fact]
    public void AChildBrushReachesTheRegisteredAncestorWithoutAVisualParent()
    {
        var brush = new SolidColorBrush(Colors.Green); var run = new Run("原文") { Foreground = brush };
        var paragraph = new Paragraph(new Span(run)); var document = new FlowDocument(paragraph); var view = new Border();
        using var registration = CssDocumentConsumers.Register(document, view); _ = run.Foreground; Clear(view);
        brush.Opacity = .5;
        Assert.True(view.IsRenderDirty); Assert.Equal("原文\n", document.GetText());
    }

    [Fact]
    public void ReplacingSharedSourceBrushesRemovesOnlyTheReplacedPropertyUse()
    {
        var brush = new SolidColorBrush(Colors.Green); var replacement = new SolidColorBrush(Colors.Red);
        var run = new Run("原文") { Foreground = brush, Background = brush }; var view = new Border();
        using var registration = CssDocumentConsumers.Register(run, view); _ = run.Foreground; _ = run.Background;
        run.Foreground = replacement; _ = run.Foreground; Clear(view); brush.Color = Colors.Blue;
        Assert.True(view.IsRenderDirty);
        run.Background = replacement; _ = run.Background; Clear(view); brush.Color = Colors.Yellow;
        Assert.False(view.IsRenderDirty);
        replacement.Color = Colors.Black; Assert.True(view.IsRenderDirty);
    }

    [Fact]
    public void GradientStopAndBrushTransformChangesInvalidateTheSourceView()
    {
        var brush = new LinearGradientBrush(Colors.Green, Colors.Blue, new(0, 0), new(1, 1));
        var run = new Run("原文") { Background = brush }; var view = new Border();
        using var registration = CssDocumentConsumers.Register(run, view); _ = run.Background; Clear(view);
        brush.GradientStops[0].Color = Colors.Red; Assert.True(view.IsRenderDirty);
        brush.Transform = new TranslateTransform(0, 0); Clear(view); ((TranslateTransform)brush.Transform).X = 2;
        Assert.True(view.IsRenderDirty);
    }

    [Fact]
    public void ABrushInTagDoesNotCreateAPaintDependency()
    {
        var brush = new SolidColorBrush(Colors.Green); var source = new Run("原文") { Tag = brush }; var view = new Border();
        using var registration = CssDocumentConsumers.Register(source, view); _ = source.Tag; Clear(view);
        brush.Color = Colors.Blue; Assert.False(view.IsRenderDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentlyObservedPresentationsStillRegisterForPaintOnlyUpdates(bool observe)
    {
        var brush = new SolidColorBrush(Colors.Green); var paragraph = new Paragraph(new Run("原文"));
        var document = new FlowDocument(paragraph) { Background = brush, PagePadding = new(0) };
        using var body = new FlowDocumentPresentation(observe: observe); body.SetDocument(document);
        for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); body.Measure(new(240, 180)); body.Arrange(new(0, 0, 240, 180)); }
        _ = document.Background; Clear(body); var valid = body.IsMeasureValid;
        brush.Color = Colors.Blue;
        Assert.True(body.IsRenderDirty); Assert.Equal(valid, body.IsMeasureValid); Assert.Same(paragraph, Assert.Single(document.Blocks));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("run")]
    [InlineData("span")]
    [InlineData("gradient")]
    public void SharedDocumentViewsReceivePaintUpdatesWithoutChangingGeometryOrText(string kind)
    {
        Brush brush = kind == "gradient" ? new LinearGradientBrush(Colors.Green, Colors.Blue, new(0, 0), new(1, 1)) : new SolidColorBrush(Colors.Green);
        var run = new Run("共享原文"); var span = new Span(run); var paragraph = new Paragraph(span);
        var document = new FlowDocument(paragraph) { PagePadding = new(0) };
        if (kind == "root") document.Background = brush;
        else if (kind == "span") span.Background = brush;
        else run.Foreground = brush;
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation(observe: false);
        one.SetDocument(document); two.SetDocument(document);
        foreach (var body in new[] { one, two })
        { body.Measure(new(body == one ? 180 : 300, 160)); body.Arrange(new(0, 0, body == one ? 180 : 300, 160)); body.UpdateLayout(); }
        _ = document.Background; _ = span.Background; _ = run.Foreground;
        var sizes = new[] { one.ExtentHeight, two.ExtentHeight }; Clear(one, two);
        if (brush is SolidColorBrush solid) solid.Color = Colors.Blue;
        else ((LinearGradientBrush)brush).GradientStops[0].Color = Colors.Red;
        Assert.True(one.IsRenderDirty); Assert.True(two.IsRenderDirty);
        Assert.Equal(sizes, new[] { one.ExtentHeight, two.ExtentHeight }); Assert.Equal("共享原文\n", document.GetText());
        Assert.Same(span, run.Parent); Assert.Same(paragraph, span.Parent);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EmptyDocumentsStillRegisterTheirRootPaintAndDetachOnReplacement(bool observe, bool emptySection)
    {
        var brush = new SolidColorBrush(Colors.Green);
        var document = emptySection ? new FlowDocument(new Section()) : new FlowDocument();
        document.Background = brush;
        using var body = new FlowDocumentPresentation(observe: observe); body.SetDocument(document);
        body.Measure(new(240, 180)); body.Arrange(new(0, 0, 240, 180)); body.UpdateLayout();
        _ = document.Background; Clear(body); var measureValid = body.IsMeasureValid;
        brush.Color = Colors.Blue;
        Assert.True(body.IsRenderDirty); Assert.Equal(measureValid, body.IsMeasureValid); Assert.Empty(body.Paragraphs);
        body.SetDocument(new FlowDocument()); body.Measure(new(240, 180)); body.Arrange(new(0, 0, 240, 180)); body.UpdateLayout();
        Clear(body); brush.Color = Colors.Red; Assert.False(body.IsRenderDirty);
    }

    [Fact]
    public void DisposingAConsumerRemovesItsPaintDependency()
    {
        var brush = new SolidColorBrush(Colors.Green); var source = new Run("原文") { Foreground = brush };
        var view = new Border(); var registration = CssDocumentConsumers.Register(source, view); _ = source.Foreground;
        registration.Dispose(); Clear(view); brush.Color = Colors.Blue; Assert.False(view.IsRenderDirty);
    }

    [Fact]
    public void ASharedBrushDoesNotKeepItsNonvisualSourceOrConsumerAlive()
    {
        var brush = new SolidColorBrush(Colors.Green); var weak = MakeWeakSource(brush);
        for (var i = 0; i < 3 && weak.Any(reference => reference.IsAlive); i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.All(weak, reference => Assert.False(reference.IsAlive)); brush.Color = Colors.Blue; GC.KeepAlive(brush);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] MakeWeakSource(Brush brush)
    {
        var source = new Run("原文") { Foreground = brush }; var consumer = new Border();
        _ = CssDocumentConsumers.Register(source, consumer); _ = source.Foreground;
        return [new(source), new(consumer)];
    }

    private static void Clear(params Visual[] views) { foreach (var view in views) view.ClearRenderDirty(); }
}
