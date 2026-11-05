using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private DocumentBox _rootBox;
    private DocumentHeight _rootHeight;
    private double _viewportHeight = double.NaN;
    private double _pageAreaWidth = double.NaN;
    private double _extentWidth;
    private double RootLeft => _rootBox.Margin.Left + _rootBox.Padding.Left + _rootBox.Border.Left;
    private double RootTop => _rootBox.Margin.Top + _rootBox.Padding.Top + _rootBox.Border.Top;

    internal bool SetViewportHeight(double height)
    {
        height = double.IsFinite(height) && height >= 0 ? height : double.NaN;
        if (_viewportHeight.Equals(height)) return false;
        _viewportHeight = height; _layoutWidth = double.NaN; InvalidateMeasure(); return true;
    }

    internal void EnsureViewerLayout(double fallbackWidth)
    {
        if (_document is not { } document) return;
        var width = double.IsFinite(_pageAreaWidth) ? _pageAreaWidth : fallbackWidth;
        var p = EffectivePagePadding;
        EnsureDocumentLayout(Math.Max(1, width - p.Left - p.Right));
        // A query may refresh layout between normal arrange passes. Cell-local
        // shaping positions must be placed back in the document before search,
        // accessibility and rendering read the original text geometry.
        ArrangeDocumentParagraphs();
    }

    private void ArrangeDocumentParagraphs()
    {
        if (_document is not { } document) return;
        if (_documentCssRoot is not null) { ArrangeDocumentCssBoxes(); return; }
        var padding = EffectivePagePadding;
        foreach (var view in _paragraphs)
        {
            if (_pageBlank && !view.IsPageFixed)
            {
                view.Text.Arrange(new(-10000, -10000, 0, 0));
                continue;
            }
            if (view.IsPageFixed)
            {
                view.Text.Arrange(new(
                    view.Bounds.X + padding.Left + view.VisualOffset.X,
                    view.Bounds.Y + padding.Top + view.VisualOffset.Y,
                    view.Bounds.Width,
                    view.Bounds.Height));
                continue;
            }
            var dy = MapDocumentYToPageOffset(view.Source, out _);
            view.Text.Arrange(new(
                view.Bounds.X + padding.Left + view.VisualOffset.X,
                view.Bounds.Y + dy + view.VisualOffset.Y,
                view.Bounds.Width,
                view.Bounds.Height));
        }
        ArrangeDocumentMarkers();
    }

    private void PrepareRootBox(double width)
    {
        var document = _document!;
        var p = EffectivePagePadding;
        _rootBox = ResolveDocumentBox(document, width, TableBorder(document));
        var height = double.IsFinite(_paginationColumnHeight) ? _paginationColumnHeight
            : double.IsFinite(_viewportHeight) ? Math.Max(0, _viewportHeight - p.Top - p.Bottom) : double.NaN;
        _rootHeight = ResolveDocumentHeight(document, _rootBox, height);
    }

    private void FinishRootBox(double contentBottom, bool columnsPlanned)
    {
        var document = _document!;
        var bottom = contentBottom + _rootBox.Padding.Bottom + _rootBox.Border.Bottom;
        if (!columnsPlanned || _rootColumnScope is not { Synthetic: false } || !_rootHeight.Constrained)
            bottom = ApplyDocumentHeight(document, _rootHeight, _rootBox.Margin.Top, bottom);
        else _hasDocumentHeights = true;
        // PagePadding belongs to each page. CSS root insets belong to the root
        // box and are consumed once, including when the root spans pages.
        if (RootTop > _rootBox.Margin.Top + .00001)
        { _heightSpaces.Add(new(document, _rootBox.Margin.Top, RootTop)); _hasDocumentHeights = true; }
        var contentEnd = bottom - _rootBox.Padding.Bottom - _rootBox.Border.Bottom;
        if (bottom > contentEnd + .00001)
        { _heightSpaces.Add(new(document, contentEnd, bottom)); _hasDocumentHeights = true; }
        var frame = new Rect(_rootBox.Margin.Left, _rootBox.Margin.Top, _rootBox.Width, Math.Max(0, bottom - _rootBox.Margin.Top));
        _blocks.Insert(0, new(document, frame));
        _extentHeight = Math.Max(0, bottom + _rootBox.Margin.Bottom);
        _extentWidth = Math.Max(0, frame.Right + _rootBox.Margin.Right);
        foreach (var view in _paragraphs.Where(view => !view.Text.DocumentDisplaySuppressed && !view.TableSuppressed && CssTableVisibility.Visible(view.Source)))
        {
            var bounds = view.TableClip is { } clip ? Rect.Intersect(view.Bounds, clip) : view.Bounds;
            _extentHeight = Math.Max(_extentHeight, bounds.Bottom); _extentWidth = Math.Max(_extentWidth, bounds.Right);
        }
        foreach (var paint in _blocks.Where(paint => CssTableVisibility.Visible(paint.Source)))
        {
            var bounds = paint.Clip is { } clip ? Rect.Intersect(paint.Bounds, clip) : paint.Bounds;
            _extentHeight = Math.Max(_extentHeight, bounds.Bottom); _extentWidth = Math.Max(_extentWidth, bounds.Right);
        }
        if (columnsPlanned && double.IsFinite(_paginationColumnHeight))
        {
            var pageHeight = Math.Max(1, _paginationColumnHeight);
            while (_columnPages.Count == 0 || _columnPages[^1].Bottom < Math.Max(frame.Bottom, _extentHeight - _rootBox.Margin.Bottom) - .00001)
            {
                var top = _columnPages.Count == 0 ? 0 : _columnPages[^1].Bottom;
                _columnPages.Add(new(top, top + pageHeight));
            }
        }
    }
}
