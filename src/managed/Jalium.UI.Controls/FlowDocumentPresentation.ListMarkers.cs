using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed class DocumentMarker(DependencyObject source, CssGeneratedText text, IDisposable registration,
        IDisposable consumer) : IDisposable
    {
        internal readonly DependencyObject Source = source;
        internal readonly CssGeneratedText Text = text;
        internal FrameworkElement? Host;
        internal IDisposable? Attachment;
        internal bool Plain;
        public void Dispose() { Text.ProjectionInvalidated = null; registration.Dispose(); consumer.Dispose(); }
    }
    private readonly Dictionary<DependencyObject, DocumentMarker> _documentMarkers = [];
    private readonly List<DocumentMarker> _markerOrder = [];
    private bool _preparingMarkers;

    private void SynchronizeDocumentMarkers()
    {
        if (_document is not { } document || _preparingMarkers) return;
        _preparingMarkers = true;
        try
        {
            var sources = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance); _markerOrder.Clear();
            void Walk(CssNode node)
            {
                if (node.Target is ListItem || CssListProperties.IsItem(node))
                {
                    sources.Add(node.Target);
                    if (!_documentMarkers.TryGetValue(node.Target, out var marker))
                    {
                        var text = new CssGeneratedMarker(node) { IsHitTestVisible = false, Focusable = false, IsDocumentBoxContent = true };
                        marker = new(node.Target, text, CssGeneratedContent.RegisterProjectedMarker(node, text),
                            CssDocumentConsumers.Register(node.Target, text));
                        _documentMarkers.Add(node.Target, marker);
                        text.ProjectionInvalidated = () => { if (!_preparingMarkers) DocumentChanged(); };
                    }
                    _markerOrder.Add(marker);
                    var source = node.Target; var target = marker.Text;
                    foreach (var property in new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty,
                        TextElement.FontWeightProperty, TextElement.FontStyleProperty, TextElement.FontStretchProperty,
                        TextElement.ForegroundProperty })
                    {
                        var destination = CssDependencyPropertyLookup.Find(typeof(TextBlock), property.Name) ?? property;
                        target.SetLayerValue(destination, source.GetValue(property), LayerValueSource.StyleSetter);
                    }
                    target.SetLayerValue(FrameworkElement.FlowDirectionProperty, CssListProperties.MarkerDirection(source), LayerValueSource.StyleSetter);
                    var height = (double)source.GetValue(Block.LineHeightProperty)!;
                    target.SetLayerValue(TextBlock.LineHeightProperty, double.IsFinite(height) ? height : document.LineHeight, LayerValueSource.StyleSetter);
                    // Inherited CSS metrics must come from the original item,
                    // rather than the private visual parent. Otherwise a text
                    // mutation after attachment falls back to natural leading.
                    var computedHeight = CssComputedLineHeightValue.Inherited(node);
                    if (!CssComputedLineHeightValue.HasCssSource(source) && computedHeight.Kind == CssLineHeightKind.Normal && double.IsFinite(document.LineHeight))
                        computedHeight = new(CssLineHeightKind.Pixels, document.LineHeight);
                    target.SetLayerValue(CssComputedLineHeightValue.Property, computedHeight, LayerValueSource.StyleSetter);
                    target.SetRootDpi(DpiScale);
                    target.Visibility = CssTableVisibility.Visible(source) ? Visibility.Visible : Visibility.Hidden;
                }
                foreach (var child in node.EnumerateChildren()) Walk(child);
            }
            Walk(CssNode.Get(document));
            foreach (var old in _documentMarkers.Values.Where(marker => !sources.Contains(marker.Source)).ToArray())
            { DetachDocumentMarker(old); old.Dispose(); _documentMarkers.Remove(old.Source); }
            CssEvaluationScheduler.FlushIfPending(document.Dispatcher);
            foreach (var marker in _markerOrder) marker.Text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        }
        finally { _preparingMarkers = false; }
    }

    private void DetachDocumentMarker(DocumentMarker marker)
    {
        if (marker.Attachment is { } attachment) { attachment.Dispose(); marker.Attachment = null; }
        else if (marker.Plain) RemoveVisualChild(marker.Text);
        marker.Host = null; marker.Plain = false;
    }
    private void DetachDocumentMarkers()
    { foreach (var marker in _documentMarkers.Values) DetachDocumentMarker(marker); }
    private void ClearDocumentMarkers()
    {
        DetachDocumentMarkers(); foreach (var marker in _documentMarkers.Values) marker.Dispose();
        _documentMarkers.Clear(); _markerOrder.Clear();
    }
    private void AttachDocumentMarker(DependencyObject source, FrameworkElement host, bool generated)
    {
        if (!_documentMarkers.TryGetValue(source, out var marker) || ReferenceEquals(marker.Host, host)) return;
        DetachDocumentMarker(marker); marker.Host = host;
        if (generated) marker.Attachment = CssGeneratedContent.AttachProjectedMarker(host, marker.Text);
        else { marker.Plain = true; AddVisualChild(marker.Text); }
    }
    private int PlainDocumentMarkerCount => _markerOrder.Count(marker => marker.Plain);
    private Visual PlainDocumentMarker(int index) => _markerOrder.Where(marker => marker.Plain).ElementAt(index).Text;

    private double NativeListIndent(DocList list)
    {
        var width = 20d;
        foreach (var item in list.ListItems)
            if (!CssDisplayProperties.SuppressesBoxTree(item) && !CssListProperties.Inside(item) &&
                _documentMarkers.TryGetValue(item, out var marker) && marker.Text.Visibility != Visibility.Collapsed)
                width = Math.Max(width, marker.Text.DesiredSize.Width + (double.IsFinite(list.MarkerOffset) ? list.MarkerOffset : 0));
        return width;
    }

    private void ArrangeDocumentMarkers()
    {
        if (_documentCssRoot is not null || _document is not { } document) return;
        foreach (var marker in _markerOrder)
        {
            AttachDocumentMarker(marker.Source, this, generated: false);
            var fragment = _fragmentBoxes.FirstOrDefault(value => ReferenceEquals(value.Source, marker.Source));
            if (fragment.EndLine <= fragment.FirstLine || fragment.FirstLine >= _lines.Count)
            { marker.Text.Arrange(new(0, 0, 0, 0)); continue; }
            var line = _lines[fragment.FirstLine];
            var paragraph = _paragraphs.FirstOrDefault(view => line.Start >= view.Offset && line.Start <= view.Offset + view.Text.Text.Length &&
                IsDescendant(view.Source, marker.Source));
            var baseline = paragraph is null ? CssFlowProperties.LineMetrics(marker.Text).Baseline :
                paragraph.Text.GetFirstBaselineOffset(paragraph.Text.RenderSize.Height) - paragraph.Text.GetDocumentLines(paragraph.Text.RenderSize.Width).FirstOrDefault().Top;
            // Image/text markers obtain their shared-flow baseline during
            // arrange. Place their internal content before reading that metric.
            marker.Text.Arrange(new(0, 0, marker.Text.DesiredSize.Width, marker.Text.DesiredSize.Height));
            var markerBaseline = CssDisplayLayout.TryGetBaseline(marker.Text, marker.Text.DesiredSize.Height, last: true, out var measuredBaseline)
                ? measuredBaseline : marker.Text.GetLastBaselineOffset(marker.Text.DesiredSize.Height);
            if (!double.IsFinite(markerBaseline)) markerBaseline = CssFlowProperties.LineMetrics(marker.Text).Baseline;
            var gap = marker.Source is ListItem { List: { MarkerOffset: var offset } } && double.IsFinite(offset) ? offset : 0;
            var direction = CssListProperties.MarkerDirection(marker.Source);
            var x = direction == FlowDirection.RightToLeft ? line.Left + line.Width + gap : line.Left - marker.Text.DesiredSize.Width - gap;
            marker.Text.Arrange(new(x + document.PagePadding.Left, line.Top + baseline - markerBaseline + document.PagePadding.Top - _pageTop,
                marker.Text.DesiredSize.Width, marker.Text.DesiredSize.Height));
        }
        static bool IsDescendant(TextElement current, DependencyObject parent)
        {
            for (DependencyObject? node = current; node is not null; node = (node as FrameworkContentElement)?.Parent)
                if (ReferenceEquals(node, parent)) return true;
            return false;
        }
    }
}
