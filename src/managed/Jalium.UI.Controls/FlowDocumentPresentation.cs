using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

/// <summary>A read-only layout of original document nodes, shared by scrolling and paged views.</summary>
internal sealed partial class FlowDocumentPresentation : FrameworkElement, IDisposable
{
    internal readonly record struct Line(int Start, int End, double Top, double Height, bool BreakBefore, double Left = 0, double Width = double.NaN,
        bool IsSpace = false);
    internal readonly record struct FragmentBox(TextElement Source, int FirstLine, int EndLine,
        double MarginTop, double BorderTop, double Bottom, int[]? LineBands = null)
    { internal DependencyObject? AnonymousOwner { get; init; } }
    private sealed record ParagraphView(Paragraph Source, TextBlock Text, int Offset)
    {
        internal Rect Bounds;
        internal Rect? TableClip;
        internal bool TableSuppressed;
        internal bool ClampSuppressed;
        internal bool ContentsFlow;
        internal Rect[]? ColumnClips;
        internal Point VisualOffset;
        internal bool IsPageFixed;
    }
    private sealed record BlockPaint(
        DependencyObject Source,
        Rect Bounds,
        bool Frame = true,
        Rect? Clip = null,
        Rect? PaintBounds = null,
        Point VisualOffset = default,
        bool IsPageFixed = false);
    private readonly List<ParagraphView> _paragraphs = [];
    private readonly List<BlockPaint> _blocks = [];
    private readonly List<Line> _lines = [];
    private readonly List<FragmentBox> _fragmentBoxes = [];
    private readonly List<IDisposable> _consumers = [];
    private readonly bool _observe;
    private readonly bool _exposeAutomation;
    private Subscription? _subscription;
    private FlowDocument? _document;
    private TextSelection? _selection;
    private bool _highlightSelection = true;
    private bool _dirty = true;
    private bool _selecting;
    private int _anchor;
    private double _layoutWidth = double.NaN;
    private double _extentHeight;
    private double _viewportWidth = 816;
    private double _observedDpi = 1;
    private long _fontEpoch = -1;
    private Size? _pageSize;
    private double _pageTop;
    private double _pageBottom = double.PositiveInfinity;
    private int _pageIndex;
    private int _pageCount = 1;
    private bool _pageBlank;
    private CssComputedPageStyle? _pageStyle;
    private double _pageRepeatedHeaderHeight;
    private double _pageRepeatedFooterHeight;
    private double _pageRepeatTableTop = double.NaN;

    internal CssComputedPageStyle? PageStyle => _pageStyle;
    internal Thickness EffectivePagePadding => _pageStyle is { HasMatchedRules: true } style
        ? style.ContentInsets
        : _document?.PagePadding ?? default;

    internal FlowDocumentPresentation(bool observe = true) : this(observe, exposeAutomation: true) { }

    internal FlowDocumentPresentation(bool observe, bool exposeAutomation)
    {
        _observe = observe;
        _exposeAutomation = exposeAutomation;
        Focusable = observe;
        Cursor = Cursors.IBeam;
    }

    private Brush? _defaultForeground;
    private Color? _defaultForegroundColor;
    private double _defaultForegroundOpacity;

    // Editors resolve theme fallback outside the document. Borrow that ink for
    // private projections without writing a local value into the source nodes.
    internal bool SetDefaultForeground(Brush foreground)
    {
        var color = (foreground as SolidColorBrush)?.Color;
        if (ReferenceEquals(_defaultForeground, foreground) &&
            _defaultForegroundColor == color && _defaultForegroundOpacity == foreground.Opacity) return false;
        _defaultForeground = foreground;
        _defaultForegroundColor = color;
        _defaultForegroundOpacity = foreground.Opacity;
        DocumentChanged();
        return true;
    }

    internal FlowDocument? Document => _document;
    internal bool IsTextSelectionEnabled { get; set; } = true;
    internal Brush? SelectionBrush { get; set; }
    internal Brush? SelectionTextBrush { get; set; }
    internal TextSelection? Selection => _selection;
    internal IReadOnlyList<TextBlock> Paragraphs => _paragraphs.Select(p => p.Text).ToArray();
    internal IReadOnlyList<Line> Lines => _lines;
    internal IReadOnlyList<FragmentBox> FragmentBoxes => _fragmentBoxes;
    internal double ExtentHeight => _extentHeight;
    internal UIElement? WritingToolsOwner { get; set; }
    internal DependencyObject? TextDecorationOwner { get; set; }
    internal bool PassThroughSelfHit { get; set; }

    internal bool SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0 || Math.Abs(width - _viewportWidth) < 0.01) return false;
        _viewportWidth = width;
        InvalidateMeasure();
        return true;
    }

    internal void SetDocument(FlowDocument? document)
    {
        if (ReferenceEquals(document, _document)) return;
        _subscription?.Dispose();
        ClearDocumentMarkers();
        ClearParagraphs();
        _document = document;
        _selection = document is null ? null : new(document.ContentStart, document.ContentStart);
        if (document is not null && _observe) _subscription = new(this, document);
        DocumentChanged();
    }

    internal void SetPage(
        Size size,
        double top,
        double bottom,
        int pageIndex = 0,
        int pageCount = 1,
        bool isBlank = false,
        CssComputedPageStyle? pageStyle = null,
        double repeatedHeaderHeight = 0,
        double repeatedFooterHeight = 0,
        double repeatTableTop = double.NaN)
    {
        _pageSize = size;
        _pageTop = top;
        _pageBottom = bottom;
        _pageIndex = pageIndex;
        _pageCount = Math.Max(1, pageCount);
        _pageBlank = isBlank;
        _pageStyle = pageStyle;
        _pageRepeatedHeaderHeight = repeatedHeaderHeight;
        _pageRepeatedFooterHeight = repeatedFooterHeight;
        _pageRepeatTableTop = repeatTableTop;
        var p = EffectivePagePadding;
        if (_document is not null) SetPaginationColumnHeight(Math.Max(1, size.Height - p.Top - p.Bottom));
        ClipToBounds = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private bool TryGetActivePageRepeatTable(out RepeatableTableInfo info)
    {
        if (_pageSize is not null && !_pageBlank && (_pageRepeatedHeaderHeight > 0 || _pageRepeatedFooterHeight > 0))
        {
            foreach (var candidate in _repeatableTables)
            {
                if (!double.IsNaN(_pageRepeatTableTop) && Math.Abs(candidate.TableBounds.Top - _pageRepeatTableTop) <= .5 ||
                    _pageTop < candidate.BodyBottom - .01 && _pageBottom > candidate.BodyTop + .01)
                {
                    info = candidate;
                    return true;
                }
            }
        }
        info = default;
        return false;
    }

    private double MapDocumentYToPageOffset(DependencyObject source, out Rect? bandClip)
    {
        bandClip = null;
        var p = EffectivePagePadding;
        if (TryGetActivePageRepeatTable(out var rt))
        {
            var pageWidth = _pageSize?.Width ?? RenderSize.Width;
            var bodyHeight = Math.Max(0, _pageBottom - _pageTop);
            if (_pageRepeatedHeaderHeight > 0 && rt.Header is not null && Within(source, rt.Header))
            {
                bandClip = new Rect(0, p.Top, pageWidth, _pageRepeatedHeaderHeight);
                return p.Top - rt.HeaderBounds.Top;
            }
            if (_pageRepeatedFooterHeight > 0 && rt.Footer is not null && Within(source, rt.Footer))
            {
                var footerTop = p.Top + _pageRepeatedHeaderHeight + bodyHeight;
                bandClip = new Rect(0, footerTop, pageWidth, _pageRepeatedFooterHeight);
                return footerTop - rt.FooterBounds.Top;
            }
            bandClip = new Rect(0, p.Top + _pageRepeatedHeaderHeight, pageWidth, bodyHeight);
        }
        return p.Top - _pageTop + _pageRepeatedHeaderHeight;
    }

    internal void SetSelection(TextSelection? selection, bool highlight = true)
    {
        _highlightSelection = highlight;
        if (selection is not null && ReferenceEquals(selection.Start.Document, _document)) _selection = selection;
        foreach (var paragraph in _paragraphs)
        {
            paragraph.Text.SelectionBrush = SelectionBrush;
            paragraph.Text.NativeSelectionTextBrush = SelectionTextBrush;
            var start = highlight && selection is not null ? Math.Clamp(selection.Start.DocumentOffset - paragraph.Offset, 0, paragraph.Text.Text.Length) : 0;
            var end = highlight && selection is not null ? Math.Clamp(selection.End.DocumentOffset - paragraph.Offset, start, paragraph.Text.Text.Length) : 0;
            paragraph.Text.Select(start, end - start);
        }
        InvalidateVisual();
    }

    internal void DocumentChanged()
    {
        if (_document is { } document && _documentMarkers.Count > 0) CssCounters.NoteEvaluated(CssNode.Get(document));
        _dirty = true;
        _layoutWidth = double.NaN;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ClearParagraphs()
    {
        ClearDocumentCssBoxes();
        ClearDocumentClamping();
        _documentExposure = new("", []);
        _intrinsicDocumentWidths.Clear();
        _documentNaturalHeightProbes.Clear();
        ClearDocumentColumns();
        ClearDocumentContentsRuns();
        ResetDocumentTableBoxes();
        _nestedColumnConstraints.Clear();
        _demotedDocumentSpanners.Clear();
        foreach (var consumer in _consumers) consumer.Dispose();
        _consumers.Clear();
        foreach (var paragraph in _paragraphs) RemoveVisualChild(paragraph.Text);
        _paragraphs.Clear();
        _heightSpaces.Clear(); _hasDocumentHeights = false;
    }

    private void RebuildParagraphs()
    {
        static IEnumerable<Paragraph> Sources(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph paragraph) yield return paragraph;
                else if (block is Section section) { foreach (var item in Sources(section.Blocks)) yield return item; }
                else if (block is DocList list)
                    foreach (var item in list.ListItems) foreach (var child in Sources(item.Blocks)) yield return child;
                else if (block is Table table)
                    foreach (var group in table.RowGroups) foreach (var row in group.Rows)
                        foreach (var cell in row.Cells) foreach (var child in Sources(cell.Blocks)) yield return child;
            }
        }
        if (_consumers.Count > 0 && _document is { } current && Sources(current.Blocks).SequenceEqual(_paragraphs.Select(view => view.Source)))
        {
            ClearDocumentColumns();
            _intrinsicDocumentWidths.Clear();
            _documentNaturalHeightProbes.Clear();
            _nestedColumnConstraints.Clear();
            _demotedDocumentSpanners.Clear();
            for (var index = 0; index < _paragraphs.Count; index++)
            {
                var view = _paragraphs[index];
                view.Text.PresentDocumentParagraph(view.Source, current, _defaultForeground);
                _paragraphs[index] = view with { Offset = current.GetDocumentOffset(view.Source, 0) };
            }
            _dirty = false;
            SetSelection(_selection, _highlightSelection);
            return;
        }
        ClearParagraphs();
        if (_document is not { } document) return;
        // Editor and paginator hosts can observe content themselves, but still
        // need paint invalidation for in-place brush/image resource changes.
        _consumers.Add(CssDocumentConsumers.Register(document, this));
        void Add(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph paragraph)
                {
                    var text = new ParagraphText(this, paragraph) { IsTextSelectionEnabled = false, Focusable = false };
                    text.PresentDocumentParagraph(paragraph, document, _defaultForeground);
                    _paragraphs.Add(new(paragraph, text, document.GetDocumentOffset(paragraph, 0)));
                    AddVisualChild(text);
                    if (_observe) _consumers.Add(CssDocumentConsumers.Register(paragraph, text));
                }
                else if (block is Section section) Add(section.Blocks);
                else if (block is DocList list)
                    foreach (var item in list.ListItems) Add(item.Blocks);
                else if (block is Table table)
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells) Add(cell.Blocks);
            }
        }
        Add(document.Blocks);
        _dirty = false;
        SetSelection(_selection, _highlightSelection);
    }

    internal void EnsureDocumentLayout(double width)
    {
        if (_document is not { } document) return;
        var dpi = VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(this) as Visual ?? this).DpiScaleY;
        if (_observe && _observedDpi != dpi)
        {
            _observedDpi = dpi;
            CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(document));
        }
        CssEvaluationScheduler.FlushIfPending(document.Dispatcher);
        if (!ReferenceEquals(document, _document)) return;
        if (_dirty) RebuildParagraphs();
        SynchronizeDocumentMarkers();
        RebuildDocumentExposure();
        width = double.IsFinite(width) ? Math.Max(1, width) : 816;
        if (_layoutWidth == width && _fontEpoch == TextMeasurement.FontCacheEpoch)
        {
            if (DocumentCssSurface is { IsMeasureValid: false })
            {
                RemeasureDocumentCssBoxes(width);
                RebuildDocumentExposure();
            }
            return;
        }
        _layoutWidth = width;
        _fontEpoch = TextMeasurement.FontCacheEpoch;
        ResetDocumentTableBoxes();
        if (NeedsDocumentCssBoxes(document)) LayoutDocumentCssBoxes(width);
        else if (CssDisplayProperties.SuppressesBoxTree(document)) { ClearDocumentCssBoxes(); LayoutWithoutDocumentBoxes(); }
        else { ClearDocumentCssBoxes(); LayoutDocumentWithClamping(width); }
        RebuildDocumentExposure();
    }

    private static Point ResolveDocumentRelativeOffset(DependencyObject source, double containingWidth, double containingHeight)
    {
        var layout = CssNode.Get(source).CssLayout;
        if (layout is not { Position: CssPositionMode.Relative }) return default;
        var hasLeft = layout.InsetLeft.IsSet && !layout.InsetLeft.IsAuto;
        var hasTop = layout.InsetTop.IsSet && !layout.InsetTop.IsAuto;
        var hasRight = layout.InsetRight.IsSet && !layout.InsetRight.IsAuto;
        var hasBottom = layout.InsetBottom.IsSet && !layout.InsetBottom.IsAuto;
        var basisW = double.IsFinite(containingWidth) ? containingWidth : 0;
        var basisH = double.IsFinite(containingHeight) ? containingHeight : 0;
        var dx = hasLeft ? layout.InsetLeft.Resolve(basisW, 0) : hasRight ? -layout.InsetRight.Resolve(basisW, 0) : 0;
        var dy = hasTop ? layout.InsetTop.Resolve(basisH, 0) : hasBottom ? -layout.InsetBottom.Resolve(basisH, 0) : 0;
        return new(dx, dy);
    }

    private void LayoutDocumentContent(double width)
    {
        var document = _document!;
        _nestedColumnConstraints.Clear();
        _documentNaturalHeightProbes.Clear();
        _demotedDocumentSpanners.Clear();
        _hasNestedColumns = HasNestedDocumentColumns(document.Blocks);
        var columnPassLimit = 8;
        for (var columnPass = 0; columnPass < columnPassLimit; columnPass++)
        {
            ClearDocumentClampGeometry();
            ResetDisplaySuppression();
            _columnConstraintsChanged = false;
            _documentSizingConstraintsChanged = false;
            ClearDocumentColumns();
            ClearDocumentContentsRuns();
            _intrinsicDocumentWidths.Clear();
            PrepareRootBox(width);
            _documentColumns = CssDocumentColumns.Resolve(document, _rootBox.ContentWidth);
            var syntheticColumns = _documentColumns is null;
            if (_documentColumns is null && _hasNestedColumns)
                _documentColumns = new(document, 1, _rootBox.ContentWidth, 0, _rootBox.ContentWidth, CssColumnFill.Auto, document.FlowDirection, 0, CssBorderLineStyle.None, null);
            _lines.Clear();
            _fragmentBoxes.Clear();
            _blocks.Clear();
            _heightSpaces.Clear(); _hasDocumentHeights = false;
            _tableCells.Clear();
            _repeatableTables.Clear();
            _tableGeometry.Clear();
            _tableGeometryKeys.Clear();
            ClearCollapsedTables();
            foreach (var view in _paragraphs) { view.TableClip = null; view.TableSuppressed = false; view.ClampSuppressed = false; view.VisualOffset = default; view.IsPageFixed = false; view.Text.Visibility = Visibility.Visible; }
            var index = 0;
            var y = RootTop;
            if (_documentColumns is { } rootColumns)
                _rootColumnScope = BeginColumnScope(document, rootColumns, RootLeft, RootTop, 0, syntheticColumns, syntheticColumns ? null : _rootHeight);
            void LayoutItem(ListItem item, DocList list, double left, double contentWidth, double containingHeight, Point inheritedOffset = default)
            {
                if (SkipDisplayNone(item, ref index, y) || SkipDocumentClampItem(item, ref index, y, contentWidth)) return;
                var itemFirst = _lines.Count;
                var itemMarginTop = y;
                var itemGroup = _fragmentBoxes.Count;
                _fragmentBoxes.Add(default);
                var indent = NativeListIndent(list);
                var itemBox = ResolveDocumentBox(item, Math.Max(0, contentWidth - indent), item.BorderThickness);
                var itemHeight = ResolveDocumentHeight(item, itemBox, containingHeight);
                var ownRel = ResolveDocumentRelativeOffset(item, contentWidth, containingHeight);
                var itemOffset = new Point(inheritedOffset.X + ownRel.X, inheritedOffset.Y + ownRel.Y);
                y += itemBox.Margin.Top; var itemTop = y;
                var itemPaint = _blocks.Count;
                var itemLeft = left + (CssListProperties.MarkerDirection(item) == FlowDirection.RightToLeft ? 0 : indent) + itemBox.Margin.Left;
                var itemContentLeft = itemLeft + itemBox.Padding.Left + itemBox.Border.Left;
                y += itemBox.Padding.Top + itemBox.Border.Top;
                var itemContentTop = y;
                var itemWidth = itemBox.ContentWidth;
                var itemColumns = HasOwnDocumentColumns(item) ? CssDocumentColumns.Resolve(item, itemWidth) : null;
                var itemScope = itemColumns is { } itemConfig ? BeginColumnScope(item, itemConfig, itemContentLeft, y, index, height: itemHeight) : null;
                Layout(item.Blocks, itemContentLeft, itemColumns?.Width ?? itemWidth, itemHeight.ContentBasis, itemOffset);
                if (_documentMarkers.TryGetValue(item, out var marker) && marker.Text.Visibility != Visibility.Collapsed)
                {
                    var markerHeight = marker.Text.DesiredSize.Height;
                    if (_lines.Count == itemFirst && markerHeight > 0)
                    {
                        int offset = document.GetDocumentOffset(item, 0);
                        _lines.Add(new(offset, offset, itemContentTop, markerHeight, false, itemContentLeft, itemWidth));
                    }
                    y = Math.Max(y, itemContentTop + markerHeight);
                }
                if (itemScope is not null) FinishColumnScope(itemScope, ref y, index);
                y += itemBox.Padding.Bottom + itemBox.Border.Bottom;
                if (itemScope is not null && itemHeight.Constrained) PreserveColumnHeightInsets(item, itemHeight, itemTop, y, itemBox);
                else y = ApplyDocumentHeight(item, itemHeight, itemTop, y);
                CaptureDocumentClampBox(item, itemBox, itemHeight, itemTop, y);
                _fragmentBoxes[itemGroup] = new(item, itemFirst, _lines.Count, itemMarginTop, itemTop, y);
                _blocks.Insert(itemPaint, new(item, new(itemLeft, itemTop, itemBox.Width, Math.Max(0, y - itemTop)), VisualOffset: itemOffset));
                y += itemBox.Margin.Bottom;
            }
            void Layout(IEnumerable<DependencyObject> blocks, double left, double available, double containingHeight = double.NaN, Point inheritedOffset = default)
            {
                var pending = new List<ParagraphView>();
                void Flush() => LayoutDocumentContentsRun(pending, left, available, ref y);
                foreach (var node in ContentsFlowNodes(blocks))
                {
                    if (SkipDisplayNone(node, ref index, y) || SkipDocumentClampItem(node, ref index, y, available)) continue;
                    if (node is Paragraph contentsParagraph && DocumentContents(node))
                    { pending.Add(_paragraphs[index++]); continue; }
                    Flush();
                    if (node is ListItem item)
                    { LayoutItem(item, (DocList)item.Parent!, left, available, containingHeight, inheritedOffset); continue; }
                    var block = (Block)node;
                    var blockCssLayout = CssNode.Get(block).CssLayout;
                    if (blockCssLayout is { Position: CssPositionMode.Fixed } && block is Paragraph fixedParagraph)
                    {
                        var view = _paragraphs[index++];
                        var fixedBorder = UsedTableBorder(block);
                        var fixedBox = ResolveDocumentBox(block, width, fixedBorder);
                        var fixedPad = fixedBox.Padding;
                        var fixedContentW = Math.Max(1, fixedBox.ContentWidth);
                        view.Text.Measure(new(fixedContentW, double.PositiveInfinity));
                        var fixedLines = view.Text.GetDocumentLines(fixedContentW);
                        var fixedH = fixedLines.Count > 0 ? fixedLines.Sum(l => l.Height)
                            : double.IsFinite(view.Text.LineHeight) ? view.Text.LineHeight : fixedParagraph.FontSize * 1.5;
                        var totalFixedH = fixedH + fixedPad.Top + fixedPad.Bottom + fixedBorder.Top + fixedBorder.Bottom;
                        var pageAreaH = double.IsFinite(_paginationColumnHeight) ? _paginationColumnHeight
                            : double.IsFinite(_viewportHeight) ? _viewportHeight : 1056;
                        var hasLeft = blockCssLayout.InsetLeft.IsSet && !blockCssLayout.InsetLeft.IsAuto;
                        var hasRight = blockCssLayout.InsetRight.IsSet && !blockCssLayout.InsetRight.IsAuto;
                        var hasTop = blockCssLayout.InsetTop.IsSet && !blockCssLayout.InsetTop.IsAuto;
                        var hasBottom = blockCssLayout.InsetBottom.IsSet && !blockCssLayout.InsetBottom.IsAuto;
                        var fx = hasLeft ? blockCssLayout.InsetLeft.Resolve(width, 0) + fixedBox.Margin.Left
                            : hasRight ? Math.Max(0, width - blockCssLayout.InsetRight.Resolve(width, 0) - fixedBox.Margin.Right - fixedBox.Width)
                            : left + fixedBox.Margin.Left;
                        var fy = hasTop ? blockCssLayout.InsetTop.Resolve(pageAreaH, 0) + fixedBox.Margin.Top
                            : hasBottom ? Math.Max(0, pageAreaH - blockCssLayout.InsetBottom.Resolve(pageAreaH, 0) - fixedBox.Margin.Bottom - totalFixedH)
                            : fixedBox.Margin.Top;
                        view.Bounds = new(fx + fixedPad.Left + fixedBorder.Left, fy + fixedPad.Top + fixedBorder.Top, fixedContentW, fixedH);
                        view.IsPageFixed = true;
                        view.Text.Arrange(view.Bounds);
                        _blocks.Add(new(block, new(fx, fy, fixedBox.Width, totalFixedH), IsPageFixed: true));
                        continue;
                    }
                    var spans = SpansDocumentColumns(block);
                    var blockLeft = spans ? _activeColumnScope!.Left : left;
                    var blockAvailable = spans ? _activeColumnScope!.Columns.Available : available;
                    var ownRelOffset = ResolveDocumentRelativeOffset(block, blockAvailable, containingHeight);
                    var blockVisualOffset = new Point(inheritedOffset.X + ownRelOffset.X, inheritedOffset.Y + ownRelOffset.Y);
                    var firstLine = _lines.Count;
                    var fragmentGroup = _fragmentBoxes.Count;
                    _fragmentBoxes.Add(default);
                    var marginTop = y;
                    var border = UsedTableBorder(block);
                    var box = ResolveDocumentBox(block, blockAvailable, border, block is Table collapsing && CssTableProperties.Collapsed(collapsing));
                    var heightConstraint = ResolveDocumentHeight(block, box, containingHeight);
                    var margin = box.Margin; var padding = box.Padding;
                    y += margin.Top;
                    var top = y;
                    var paintIndex = _blocks.Count;
                    var x = blockLeft + margin.Left;
                    var outerWidth = box.Width;
                    var innerWidth = box.ContentWidth;
                    y += padding.Top + border.Top;
                    var ownColumns = HasOwnDocumentColumns(block) ? CssDocumentColumns.Resolve(block, innerWidth) : null;
                    var columnScope = ownColumns is { } own ? BeginColumnScope(block, own, x + padding.Left + border.Left, y, index, height: heightConstraint) : null;
                    var contentWidth = ownColumns?.Width ?? innerWidth;
                    if (block is Paragraph paragraph)
                    {
                        var view = _paragraphs[index++];
                        view.VisualOffset = blockVisualOffset;
                        view.Text.Measure(new(contentWidth, double.PositiveInfinity));
                        var lines = view.Text.GetDocumentLines(contentWidth);
                        view.ClampSuppressed = DocumentClampTextSuppressed(view);
                        // TextBlock reserves optical measurement slack for standalone controls.
                        // A document fragmentainer advances by actual line boxes, never that slack
                        // or a vertical offset left over from the previous column allocation.
                        var height = view.ClampSuppressed ? 0 : lines.Count > 0 ? lines.Sum(line => line.Height)
                            : double.IsFinite(view.Text.LineHeight) ? view.Text.LineHeight : paragraph.FontSize * 1.5;
                        view.Bounds = new(x + padding.Left + border.Left, y, contentWidth, height);
                        if (!HasColumns)
                        {
                            // Line origins must use the current natural allocation on the
                            // first layout and after width/column changes alike. Otherwise
                            // search and pagination retain the previous optical text offset.
                            view.Text.Arrange(view.Bounds);
                            lines = view.Text.GetDocumentLines(contentWidth);
                        }
                        CaptureDocumentClampParagraph(view, lines, y, height);
                        if (lines.Count == 0 && !view.ClampSuppressed)
                            _lines.Add(new(view.Offset, view.Offset, y, height, block.BreakPageBefore, view.Bounds.X, contentWidth));
                        else
                        {
                            var lineTop = 0d;
                            for (var line = 0; line < lines.Count; line++)
                            {
                                _lines.Add(new(view.Offset + lines[line].Start, view.Offset + lines[line].End,
                                    y + (HasColumns ? lineTop : lines[line].Top), lines[line].Height, line == 0 && block.BreakPageBefore, view.Bounds.X, contentWidth));
                                lineTop += lines[line].Height;
                            }
                        }
                        y += height;
                    }
                    else if (block is Section section) Layout(section.Blocks, x + padding.Left + border.Left, contentWidth, heightConstraint.ContentBasis, blockVisualOffset);
                    else if (block is DocList list)
                        Layout(list.ListItems, x + padding.Left + border.Left, contentWidth, heightConstraint.ContentBasis, blockVisualOffset);
                    else if (block is Table table)
                    {
                        var insets = padding.Left + padding.Right + border.Left + border.Right;
                        var cssMargin = CssNode.Get(table).CssLayout;
                        var autoMargin = !table.HasLocalOrAnimatedValue(Block.MarginProperty) && cssMargin is { HasMargin: true } &&
                            (cssMargin.MarginLeft.IsAuto || cssMargin.MarginRight.IsAuto);
                        double ConstrainAutoWidth(double preferred) => ResolveDocumentBox(table, blockAvailable, border,
                            CssTableProperties.Collapsed(table), preferred + insets).ContentWidth;
                        double PlaceTable(double full, double visible)
                        {
                            var placed = ResizeDocumentBox(table, blockAvailable, box, (autoMargin ? visible : full) + insets);
                            x = blockLeft + placed.Margin.Left;
                            if (!autoMargin && table.FlowDirection == FlowDirection.RightToLeft) x += full - visible;
                            outerWidth = visible + insets;
                            return x + padding.Left + border.Left;
                        }
                        LayoutTable(table, innerWidth, padding, ref y, ref index, (children, childLeft, childWidth, childHeight) => Layout(children, childLeft, childWidth, childHeight, blockVisualOffset), ConstrainAutoWidth, PlaceTable, heightConstraint);
                    }
                    if (columnScope is not null) FinishColumnScope(columnScope, ref y, index);
                    y += padding.Bottom + border.Bottom;
                    if (columnScope is not null && heightConstraint.Constrained) PreserveColumnHeightInsets(block, heightConstraint, top, y, box);
                    else if (block is not Table) y = ApplyDocumentHeight(block, heightConstraint, top, y);
                    CaptureDocumentClampBox(block, box, heightConstraint, top, y);
                    _fragmentBoxes[fragmentGroup] = new(block, firstLine, _lines.Count, marginTop, top, y);
                    var paintBounds = new Rect(x, top, outerWidth, Math.Max(0, y - top));
                    _blocks.Insert(paintIndex, new(block, paintBounds, VisualOffset: blockVisualOffset));
                    if (block is Table) SetTableGeometry(block, paintBounds);
                    y += margin.Bottom;
                    if (spans) _activeColumnScope!.Spanners.Add(new(block, marginTop, y));
                }
                Flush();
            }
            Layout(document.Blocks, RootLeft, _documentColumns?.Width ?? _rootBox.ContentWidth, _rootHeight.ContentBasis);
            CaptureDocumentClampBox(document, _rootBox, _rootHeight, _rootBox.Margin.Top, y + _rootBox.Padding.Bottom + _rootBox.Border.Bottom);
            foreach (var view in _paragraphs) view.Text.Visibility = !view.Text.DocumentDisplaySuppressed && !view.TableSuppressed && !view.ClampSuppressed && CssTableVisibility.Visible(view.Source) ? Visibility.Visible : Visibility.Hidden;
            if (_documentColumns is null) FinishRootBox(y, columnsPlanned: false);
            else _extentHeight = Math.Max(0, y);
            PrepareTablePagination();
            if (_documentColumns is not null)
            {
                PlanDocumentColumns(y); ApplyDocumentColumns();
                foreach (var space in _rootColumnPlan!.ReservedSpace) _heightSpaces.Add(new(document, RootTop + space.Start, RootTop + space.End));
                FinishRootBox(RootTop + _rootColumnScope!.UsedHeight, columnsPlanned: true);
            }
            if (!_columnConstraintsChanged && !_documentSizingConstraintsChanged) break;
            var depth = 1;
            foreach (var scope in _columnScopes)
            {
                var currentDepth = 1; for (var parent = scope.Parent; parent is not null; parent = parent.Parent) currentDepth++;
                depth = Math.Max(depth, currentDepth);
            }
            columnPassLimit = Math.Clamp(depth * 2 + 4, 8, 64);
        }
    }

    protected override int VisualChildrenCount => _documentCssRoot is null ? _paragraphs.Count + PlainDocumentMarkerCount : 1;
    protected override Visual? GetVisualChild(int index) => _documentCssRoot is not null && index == 0 ? DocumentCssSurface : index >= 0 && index < _paragraphs.Count && _documentCssRoot is null
        ? _paragraphs[index].Text : _documentCssRoot is null && index >= _paragraphs.Count && index < VisualChildrenCount
            ? PlainDocumentMarker(index - _paragraphs.Count) : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_document is null) return default;
        var padding = EffectivePagePadding;
        var width = _pageSize?.Width ?? (double.IsFinite(availableSize.Width) ? availableSize.Width : _viewportWidth);
        _pageAreaWidth = width;
        if (_pageSize is null && double.IsFinite(availableSize.Height)) SetViewportHeight(availableSize.Height);
        EnsureDocumentLayout(Math.Max(1, width - padding.Left - padding.Right));
        return _pageSize ?? new Size(Math.Max(width, _extentWidth + padding.Left + padding.Right), Math.Max(0, _extentHeight + padding.Top + padding.Bottom));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_document is null) return finalSize;
        var padding = EffectivePagePadding;
        EnsureDocumentLayout(Math.Max(1, (_pageSize?.Width ?? (double.IsFinite(_pageAreaWidth) ? _pageAreaWidth : finalSize.Width)) - padding.Left - padding.Right));
        ArrangeDocumentParagraphs();
        return finalSize;
    }

    internal override Geometry? GetChildLayoutClip()
    {
        if (_pageSize is not { } size || _document is null) return null;
        var p = EffectivePagePadding;
        var contentHeight = _pageBlank
            ? 0
            : Math.Max(0, _pageBottom - _pageTop) + _pageRepeatedHeaderHeight + _pageRepeatedFooterHeight;
        return new RectangleGeometry(new Rect(p.Left, p.Top, Math.Max(0, size.Width - p.Left - p.Right),
            Math.Max(0, Math.Min(size.Height - p.Top - p.Bottom, contentHeight))));
    }

    private void PaintPageChrome(DrawingContext context)
    {
        if (_pageSize is not { } pageSize || _pageStyle is not { HasMatchedRules: true } style) return;
        if (style.Background is { } pageBg)
            context.DrawRectangle(pageBg, null, new Rect(pageSize));
        var m = style.Margin;
        var pageBoxRect = new Rect(
            m.Left,
            m.Top,
            Math.Max(0, pageSize.Width - m.Left - m.Right),
            Math.Max(0, pageSize.Height - m.Top - m.Bottom));
        if (style.BorderBrush is { } pageBorderBrush &&
            (style.BorderThickness.Left > 0 || style.BorderThickness.Top > 0 || style.BorderThickness.Right > 0 || style.BorderThickness.Bottom > 0) &&
            pageBoxRect.Width > 0 && pageBoxRect.Height > 0)
        {
            var bt = style.BorderThickness;
            var top = Math.Clamp(bt.Top, 0, pageBoxRect.Height);
            var bottom = Math.Clamp(bt.Bottom, 0, pageBoxRect.Height - top);
            var left = Math.Clamp(bt.Left, 0, pageBoxRect.Width);
            var right = Math.Clamp(bt.Right, 0, pageBoxRect.Width - left);
            var middle = pageBoxRect.Height - top - bottom;
            context.DrawRectangle(pageBorderBrush, null, new(pageBoxRect.X, pageBoxRect.Y, pageBoxRect.Width, top));
            context.DrawRectangle(pageBorderBrush, null, new(pageBoxRect.X, pageBoxRect.Bottom - bottom, pageBoxRect.Width, bottom));
            context.DrawRectangle(pageBorderBrush, null, new(pageBoxRect.X, pageBoxRect.Y + top, left, middle));
            context.DrawRectangle(pageBorderBrush, null, new(pageBoxRect.Right - right, pageBoxRect.Y + top, right, middle));
        }

        if (style.MarginBoxes.Count == 0) return;
        var contentLeft = m.Left;
        var contentRight = Math.Max(m.Left, pageSize.Width - m.Right);
        var contentWidth = contentRight - contentLeft;
        var contentTop = m.Top;
        var contentBottom = Math.Max(m.Top, pageSize.Height - m.Bottom);
        var contentHeight = contentBottom - contentTop;

        Rect ResolveMarginBoxBounds(CssPageMarginBoxKind kind)
        {
            return kind switch
            {
                CssPageMarginBoxKind.TopLeftCorner => new(0, 0, m.Left, m.Top),
                CssPageMarginBoxKind.TopRightCorner => new(contentRight, 0, m.Right, m.Top),
                CssPageMarginBoxKind.BottomLeftCorner => new(0, contentBottom, m.Left, m.Bottom),
                CssPageMarginBoxKind.BottomRightCorner => new(contentRight, contentBottom, m.Right, m.Bottom),
                CssPageMarginBoxKind.TopLeft or CssPageMarginBoxKind.TopCenter or CssPageMarginBoxKind.TopRight =>
                    ResolveHorizontalBandSlot(kind, contentLeft, 0, contentWidth, m.Top,
                        CssPageMarginBoxKind.TopLeft, CssPageMarginBoxKind.TopCenter, CssPageMarginBoxKind.TopRight),
                CssPageMarginBoxKind.BottomLeft or CssPageMarginBoxKind.BottomCenter or CssPageMarginBoxKind.BottomRight =>
                    ResolveHorizontalBandSlot(kind, contentLeft, contentBottom, contentWidth, m.Bottom,
                        CssPageMarginBoxKind.BottomLeft, CssPageMarginBoxKind.BottomCenter, CssPageMarginBoxKind.BottomRight),
                CssPageMarginBoxKind.LeftTop or CssPageMarginBoxKind.LeftMiddle or CssPageMarginBoxKind.LeftBottom =>
                    ResolveVerticalBandSlot(kind, 0, contentTop, m.Left, contentHeight,
                        CssPageMarginBoxKind.LeftTop, CssPageMarginBoxKind.LeftMiddle, CssPageMarginBoxKind.LeftBottom),
                CssPageMarginBoxKind.RightTop or CssPageMarginBoxKind.RightMiddle or CssPageMarginBoxKind.RightBottom =>
                    ResolveVerticalBandSlot(kind, contentRight, contentTop, m.Right, contentHeight,
                        CssPageMarginBoxKind.RightTop, CssPageMarginBoxKind.RightMiddle, CssPageMarginBoxKind.RightBottom),
                _ => default,
            };
        }

        Rect ResolveHorizontalBandSlot(
            CssPageMarginBoxKind kind,
            double x,
            double y,
            double width,
            double height,
            CssPageMarginBoxKind leftKind,
            CssPageMarginBoxKind centerKind,
            CssPageMarginBoxKind rightKind)
        {
            var hasL = style.MarginBoxes.ContainsKey(leftKind);
            var hasC = style.MarginBoxes.ContainsKey(centerKind);
            var hasR = style.MarginBoxes.ContainsKey(rightKind);
            var activeCount = (hasL ? 1 : 0) + (hasC ? 1 : 0) + (hasR ? 1 : 0);
            if (activeCount <= 1) return new(x, y, width, height);
            var third = width / 3.0;
            if (kind == leftKind) return new(x, y, hasC ? third : width * 0.5, height);
            if (kind == centerKind) return new(x + third, y, third, height);
            return new(x + (hasC ? third * 2.0 : width * 0.5), y, hasC ? third : width * 0.5, height);
        }

        Rect ResolveVerticalBandSlot(
            CssPageMarginBoxKind kind,
            double x,
            double y,
            double width,
            double height,
            CssPageMarginBoxKind topKind,
            CssPageMarginBoxKind middleKind,
            CssPageMarginBoxKind bottomKind)
        {
            var hasT = style.MarginBoxes.ContainsKey(topKind);
            var hasM = style.MarginBoxes.ContainsKey(middleKind);
            var hasB = style.MarginBoxes.ContainsKey(bottomKind);
            var activeCount = (hasT ? 1 : 0) + (hasM ? 1 : 0) + (hasB ? 1 : 0);
            if (activeCount <= 1) return new(x, y, width, height);
            var third = height / 3.0;
            if (kind == topKind) return new(x, y, width, hasM ? third : height * 0.5);
            if (kind == middleKind) return new(x, y + third, width, third);
            return new(x, y + (hasM ? third * 2.0 : height * 0.5), width, hasM ? third : height * 0.5);
        }

        foreach (var (kind, box) in style.MarginBoxes)
        {
            var slot = ResolveMarginBoxBounds(kind);
            if (slot.Width <= 0 || slot.Height <= 0) continue;
            if (box.Background is { } bg)
                context.DrawRectangle(bg, null, slot);
            if (box.BorderBrush is { } bb &&
                (box.BorderThickness.Left > 0 || box.BorderThickness.Top > 0 || box.BorderThickness.Right > 0 || box.BorderThickness.Bottom > 0))
            {
                var bt = box.BorderThickness;
                var top = Math.Clamp(bt.Top, 0, slot.Height);
                var bottom = Math.Clamp(bt.Bottom, 0, slot.Height - top);
                var left = Math.Clamp(bt.Left, 0, slot.Width);
                var right = Math.Clamp(bt.Right, 0, slot.Width - left);
                var middle = slot.Height - top - bottom;
                context.DrawRectangle(bb, null, new(slot.X, slot.Y, slot.Width, top));
                context.DrawRectangle(bb, null, new(slot.X, slot.Bottom - bottom, slot.Width, bottom));
                context.DrawRectangle(bb, null, new(slot.X, slot.Y + top, left, middle));
                context.DrawRectangle(bb, null, new(slot.Right - right, slot.Y + top, right, middle));
            }
            if (string.IsNullOrEmpty(box.Text)) continue;
            var family = (box.FontFamily ?? style.FontFamily ?? _document?.FontFamily)?.GetRenderingSource(this) ?? DefaultFontFamilyName;
            var fontSize = box.FontSize > 0 ? box.FontSize : style.FontSize > 0 ? style.FontSize : 12.0;
            var foreground = box.Foreground ?? style.Foreground ?? _document?.Foreground ?? _defaultForeground ?? Brushes.Black;
            var ft = new FormattedText(box.Text, family, fontSize)
            {
                Foreground = foreground,
                FontWeight = box.FontWeight.ToOpenTypeWeight(),
                FontStyle = box.FontStyle.ToOpenTypeStyle(),
            };
            TextMeasurement.MeasureText(ft);
            var innerLeft = slot.X + box.BorderThickness.Left + box.Padding.Left;
            var innerRight = Math.Max(innerLeft, slot.Right - box.BorderThickness.Right - box.Padding.Right);
            var innerTop = slot.Y + box.BorderThickness.Top + box.Padding.Top;
            var innerBottom = Math.Max(innerTop, slot.Bottom - box.BorderThickness.Bottom - box.Padding.Bottom);
            var align = box.TextAlignment ?? (kind switch
            {
                CssPageMarginBoxKind.TopCenter or CssPageMarginBoxKind.BottomCenter or
                CssPageMarginBoxKind.TopLeftCorner or CssPageMarginBoxKind.TopRightCorner or
                CssPageMarginBoxKind.BottomLeftCorner or CssPageMarginBoxKind.BottomRightCorner => TextAlignment.Center,
                CssPageMarginBoxKind.TopRight or CssPageMarginBoxKind.BottomRight or
                CssPageMarginBoxKind.RightTop or CssPageMarginBoxKind.RightMiddle or CssPageMarginBoxKind.RightBottom => TextAlignment.Right,
                _ => TextAlignment.Left,
            });
            var textX = align switch
            {
                TextAlignment.Center => innerLeft + Math.Max(0, (innerRight - innerLeft - ft.Width) * 0.5),
                TextAlignment.Right => Math.Max(innerLeft, innerRight - ft.Width),
                _ => innerLeft,
            };
            var textY = innerTop + Math.Max(0, (innerBottom - innerTop - ft.Height) * 0.5);
            context.PushClip(new RectangleGeometry(slot));
            try
            {
                context.DrawText(ft, new Point(textX, textY));
            }
            finally { context.Pop(); }
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        if (_document is null || CssDisplayProperties.SuppressesBoxTree(_document)) return;
        if (_documentCssRoot is null && _document.Background is { } background) context.DrawRectangle(background, null, new Rect(RenderSize));
        PaintPageChrome(context);
        if (_pageBlank && !_blocks.Any(b => b.IsPageFixed)) return;
        var clip = GetChildLayoutClip();
        if (clip is not null) context.PushClip(clip);
        try
        {
            var p = EffectivePagePadding;
            var hasRepeatTable = TryGetActivePageRepeatTable(out var activeRepeatTable);
            foreach (var block in _blocks)
            {
                if (_pageBlank && !block.IsPageFixed) continue;
                if (block.Source is not TableColumn && !CssTableVisibility.Visible(block.Source)) continue;
                Rect? bandClip = null;
                var dy = block.IsPageFixed ? p.Top : MapDocumentYToPageOffset(block.Source, out bandClip);
                var dx = p.Left + block.VisualOffset.X;
                var totalDy = dy + block.VisualOffset.Y;
                var rect = new Rect(block.Bounds.X + dx, block.Bounds.Y + totalDy, block.Bounds.Width, block.Bounds.Height);
                if (hasRepeatTable && !block.IsPageFixed &&
                    (ReferenceEquals(block.Source, activeRepeatTable.Table) || block.Source is TableColumn tc && Within(tc, activeRepeatTable.Table)))
                {
                    bandClip = null;
                    var tablePageTop = _pageRepeatedHeaderHeight > 0 ? p.Top : Math.Max(p.Top, activeRepeatTable.TableBounds.Top + totalDy);
                    var tablePageBottom = p.Top + _pageRepeatedHeaderHeight + Math.Max(0, _pageBottom - _pageTop) + _pageRepeatedFooterHeight;
                    if (_pageRepeatedFooterHeight <= 0)
                        tablePageBottom = Math.Min(tablePageBottom, activeRepeatTable.TableBounds.Bottom + totalDy);
                    rect = new Rect(rect.X, tablePageTop, rect.Width, Math.Max(0, tablePageBottom - tablePageTop));
                }
                else if (!HasColumns && _pageSize is { } ps && !block.IsPageFixed &&
                         CssBoxDecorationBreakProperties.Value(block.Source) == CssBoxDecorationBreak.Clone)
                {
                    var pageContentRect = new Rect(
                        p.Left,
                        p.Top,
                        Math.Max(0, ps.Width - p.Left - p.Right),
                        Math.Max(0, Math.Min(ps.Height - p.Top - p.Bottom, Math.Max(0, _pageBottom - _pageTop) + _pageRepeatedHeaderHeight + _pageRepeatedFooterHeight)));
                    var cloned = Rect.Intersect(rect, pageContentRect);
                    if (!cloned.IsEmpty && cloned.Width > 0 && cloned.Height > 0)
                        rect = cloned;
                }
                if (!rect.IntersectsWith(new Rect(RenderSize))) continue;
                Rect? effectiveClip = block.Clip is { } local ? new Rect(local.X + dx, local.Y + totalDy, local.Width, local.Height) : null;
                if (!block.IsPageFixed && bandClip is { } bc)
                    effectiveClip = effectiveClip is { } ec ? Rect.Intersect(ec, bc) : bc;
                if (effectiveClip is { IsEmpty: true } || effectiveClip is { Width: <= 0 } or { Height: <= 0 }) continue;
                var blockClip = effectiveClip is { } clipRect ? new RectangleGeometry(clipRect) : null;
                if (blockClip is not null) context.PushClip(blockClip);
                try
                {
                    if (!block.Frame)
                    {
                        var trackRect = block.PaintBounds is { } pb
                            ? Rect.Union(new(pb.X + dx, pb.Y + totalDy, pb.Width, pb.Height), rect) : rect;
                        var slotClip = trackRect != rect ? new RectangleGeometry(rect) : null;
                        if (slotClip is not null) context.PushClip(slotClip);
                        try
                        {
                            if (block.Source is not FlowDocument && TableBackground(block.Source) is { } trackBrush)
                                context.DrawRectangle(trackBrush, null, trackRect);
                            if (!DocumentCollapsedPart(block.Source))
                                CssBorderImagePainter.DecorationFragment(this, block.Source, context, new(trackRect, true, true), TableBorder(block.Source));
                        }
                        finally { if (slotClip is not null) context.Pop(); }
                        continue;
                    }
                    var radii = CssBorderRadiusProperties.Get(block.Source)?.Resolve(rect.Size) ?? default;
                    var hasRadii = radii != default;
                    if (block.Source is not FlowDocument && TableBackground(block.Source) is { } brush)
                    {
                        if (hasRadii) context.DrawGeometry(brush, null, new CssRoundedRectangleGeometry(rect, radii));
                        else context.DrawRectangle(brush, null, rect);
                    }
                    if (DocumentCollapsedPart(block.Source)) { PaintCollapsedCenter(block.Source, context, rect); continue; }
                    var thickness = TableBorder(block.Source);
                    CssBorderImagePainter.DecorationFragment(this, block.Source, context, new(rect, true, true), thickness);
                    var imageActive = block.Source.GetValue(CssBorderImageProperties.SourceProperty) is CssBorderImageSource image && image.Resolve(this, rect.Size) is not null;
                    if (!imageActive && TableBorderBrush(block.Source) is { } borderBrush)
                    {
                        if (hasRadii)
                        {
                            _collapsedBorderPainter ??= new Border();
                            _collapsedBorderPainter.SetRootDpi(DpiScale);
                            var styles = CssBorderStyleProperties.Get(block.Source);
                            _collapsedBorderPainter.DrawCssSideColorsFor(context, new(borderBrush, borderBrush, borderBrush, borderBrush), styles, rect, thickness, radii);
                        }
                        else
                        {
                            var t = thickness;
                            var top = Math.Clamp(t.Top, 0, rect.Height);
                            var bottom = Math.Clamp(t.Bottom, 0, rect.Height - top);
                            var left = Math.Clamp(t.Left, 0, rect.Width);
                            var right = Math.Clamp(t.Right, 0, rect.Width - left);
                            var middle = rect.Height - top - bottom;
                            context.DrawRectangle(borderBrush, null, new(rect.X, rect.Y, rect.Width, top));
                            context.DrawRectangle(borderBrush, null, new(rect.X, rect.Bottom - bottom, rect.Width, bottom));
                            context.DrawRectangle(borderBrush, null, new(rect.X, rect.Y + top, left, middle));
                            context.DrawRectangle(borderBrush, null, new(rect.Right - right, rect.Y + top, right, middle));
                        }
                    }
                }
                finally { if (blockClip is not null) context.Pop(); }
            }
            PaintDocumentColumnRules(context);
        }
        finally { if (clip is not null) context.Pop(); }
    }

    internal double TopForOffset(int offset)
        => _positionLines.FirstOrDefault(line => offset < line.End || offset == line.Start, _positionLines.LastOrDefault()).Top;

    private void SelectTo(int offset)
    {
        if (_document is null || _selection is null) return;
        var text = _document.GetText();
        offset = GraphemeClusters.SnapNearest(text, Math.Clamp(offset, 0, text.Length));
        _selection.SelectOffsets(_document, Math.Min(_anchor, offset), Math.Abs(offset - _anchor));
        SetSelection(_selection);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (e.Handled || !IsTextSelectionEnabled || _document is null || e.ChangedButton != MouseButton.Left) return;
        if (!TryOffsetFromPoint(e.GetPosition(this), out var offset)) return;
        Focus();
        _anchor = offset;
        if ((e.KeyboardModifiers & ModifierKeys.Shift) != 0 && _selection is not null) _anchor = _selection.Start.DocumentOffset;
        if (e.ClickCount >= 3) { _anchor = 0; SelectTo(_document.GetText().Length); }
        else if (e.ClickCount == 2)
        {
            var exposure = Exposure;
            var text = exposure.Text;
            var start = exposure.FromSource(_anchor);
            var end = start;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start = GraphemeClusters.PreviousBoundary(text, start);
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end = GraphemeClusters.NextBoundary(text, end);
            _anchor = exposure.ToSource(start);
            SelectTo(exposure.ToSource(end, forward: false));
        }
        else SelectTo(_anchor);
        _selecting = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_selecting) return;
        if (TryOffsetFromPoint(e.GetPosition(this), out var offset)) SelectTo(offset);
        e.Handled = true;
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (!_selecting || e.ChangedButton != MouseButton.Left) return;
        _selecting = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { _selecting = false; base.OnLostMouseCapture(e); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !IsTextSelectionEnabled || _document is null) return;
        if ((e.IsControlDown || e.IsCommandDown) && e.Key == Key.A)
        {
            _anchor = 0;
            SelectTo(_document.GetText().Length);
            e.Handled = true;
        }
        else if ((e.IsControlDown || e.IsCommandDown) && e.Key == Key.C && _selection is { IsEmpty: false })
        {
            var exposure = Exposure;
            var start = exposure.FromSource(_selection.Start.DocumentOffset);
            var end = exposure.FromSource(_selection.End.DocumentOffset);
            Clipboard.SetText(exposure.Text.Substring(start, end-start));
            e.Handled = true;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_document is not null) CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(_document));
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => _exposeAutomation ? new PresentationPeer(this) : null;
    private sealed class PresentationPeer : FrameworkElementAutomationPeer, IAutomationTextProviderSource, IAutomationTextViewSource
    {
        private readonly FlowDocumentPresentation owner;
        private readonly ITextProvider _text;
        internal PresentationPeer(FlowDocumentPresentation owner) : base(owner)
        { this.owner = owner; _text = new AutomationTextProvider(this, this); }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
        protected override string GetNameCore() => owner.Exposure.Text;
        protected override string GetClassNameCore() => nameof(FlowDocumentPresentation);
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Text ? _text : base.GetPatternCore(pattern);
        string IAutomationTextProviderSource.Text => owner.Exposure.Text;
        int IAutomationTextProviderSource.SelectionStart => owner.Exposure.FromSource(owner.Selection?.Start.DocumentOffset ?? 0);
        int IAutomationTextProviderSource.SelectionLength => owner.Selection is { } selection ? owner.Exposure.FromSource(selection.End.DocumentOffset) - owner.Exposure.FromSource(selection.Start.DocumentOffset) : 0;
        bool IAutomationTextProviderSource.IsReadOnly => true;
        SupportedTextSelection IAutomationTextProviderSource.SupportedTextSelection => owner.IsTextSelectionEnabled ? SupportedTextSelection.Single : SupportedTextSelection.None;
        void IAutomationTextProviderSource.Select(int start, int length)
        {
            if (!owner.IsTextSelectionEnabled || owner.Document is null) return;
            var exposure = owner.Exposure;
            owner._anchor = exposure.ToSource(start);
            owner.SelectTo(length==0?owner._anchor:exposure.ToSource(start+length,forward:false));
        }
        IReadOnlyList<Rect> IAutomationTextProviderSource.GetBoundingRectangles(int start, int length) => owner.ExposedTextBounds(start, length);
        void IAutomationTextProviderSource.ScrollIntoView(int start, int length)
        {
            var exposure = owner.Exposure;
            var sourceStart = exposure.ToSource(start);
            var sourceEnd = exposure.ToSource(start+length, forward:false);
            Jalium.UI.Controls.Primitives.DocumentViewerBase? pageHost = null;
            for (var parent = owner.VisualParent; parent is not null; parent = parent.VisualParent)
            {
                if (parent is FlowDocumentReader reader && owner.Document is { } source)
                {
                    NavigationCommands.GoToPage.Execute(FlowDocumentViewerSupport.GetPageNumberForOffset(source, sourceStart), reader);
                    return;
                }
                if (parent is Jalium.UI.Controls.Primitives.DocumentViewerBase viewer) pageHost ??= viewer;
            }
            if (pageHost is not null && owner.Document is { } document)
            {
                pageHost.GoToPage(FlowDocumentViewerSupport.GetPageNumberForOffset(document, sourceStart));
                return;
            }
            var rectangles = owner.TextBounds(sourceStart, Math.Max(0,sourceEnd-sourceStart), clip: false);
            owner.BringIntoView(rectangles.FirstOrDefault());
        }
        int IAutomationTextViewSource.CaretIndex => owner.Exposure.FromSource(owner.Selection?.End.DocumentOffset ?? 0);
        Rect IAutomationTextViewSource.TextViewport
        {
            get { owner.EnsureTextGeometry(); return owner.GetChildLayoutClip()?.Bounds ?? new Rect(owner.RenderSize); }
        }
        IReadOnlyList<AutomationTextLine> IAutomationTextViewSource.GetTextLines()
        {
            owner.EnsureTextGeometry();
            var p = owner.Document?.PagePadding ?? default;
            var lines = owner.HasColumns || owner._hasDocumentHeights ? owner._positionLines : owner.Lines;
            var exposure = owner.Exposure;
            return lines.Select(line => new AutomationTextLine(exposure.FromSource(line.Start), exposure.FromSource(line.End) - exposure.FromSource(line.Start),
                new Rect(p.Left + line.Left, line.Top + p.Top - owner._pageTop,
                    double.IsFinite(line.Width) ? line.Width : Math.Max(0, owner.RenderSize.Width - p.Left - p.Right), line.Height))).ToArray();
        }
        bool IAutomationTextViewSource.TryGetInsertionIndex(Point point, out int index)
        { var found=owner.TryOffsetFromPoint(point,out var source);index=owner.Exposure.FromSource(source);return found; }
        bool IAutomationTextViewSource.ReplaceSelection(string text) => false;
    }

    private sealed class ParagraphText(FlowDocumentPresentation owner, Paragraph paragraph) : TextBlock, ICssAnonymousBox
    {
        // Editor presentation depends on state outside this proxy (service
        // ranges, selection and original-node styles). Keep layout/shaping
        // caches, but read that state again at each paint boundary.
        protected override bool ParticipatesInRenderCache => owner.WritingToolsOwner is null && owner.TextDecorationOwner is null;
        internal override DependencyObject? DocumentDecorationSource => owner.TextDecorationOwner;
        internal override UIElement? DocumentWritingToolsOwner => owner.WritingToolsOwner;
        internal override IEnumerable<(int Start, int Length)> DocumentWritingToolsRanges(int start, int length)
        {
            if (owner._document is not { } document) return [];
            var offset = document.GetDocumentOffset(paragraph, 0);
            return owner.EditorSourceRanges(start, length).Select(range => (range.Start - offset, range.Length));
        }
        protected override AutomationPeer? OnCreateAutomationPeer() => owner._exposeAutomation ? base.OnCreateAutomationPeer() : null;
        protected override bool SupportsCssInlineContentLayout => IsDocumentBoxContent || HasCssInlineBoxes;
        FrameworkElement ICssAnonymousBox.Owner => owner;
        double ICssAnonymousBox.BaselineOffset(double height, bool last) => IsDocumentBoxContent
            ? last ? GetLastBaselineOffset(height) : GetFirstBaselineOffset(height) : height * 0.8;
        bool ICssAnonymousBox.Contains(Point point) => false;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly WeakReference<FlowDocumentPresentation> _owner;
        private FlowDocument? _document;
        internal Subscription(FlowDocumentPresentation owner, FlowDocument document)
        { _owner = new(owner); _document = document; document.ViewerPaginationChanged += Changed; }
        private void Changed(object? sender, EventArgs e)
        { if (_owner.TryGetTarget(out var owner)) owner.DocumentChanged(); else Dispose(); }
        public void Dispose()
        { if (_document is not { } document) return; _document = null; document.ViewerPaginationChanged -= Changed; }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        ClearDocumentMarkers();
        ClearParagraphs();
        _document = null;
        _lines.Clear();
        _fragmentBoxes.Clear();
        _blocks.Clear();
        _positionLines.Clear();
        _tableCells.Clear();
        _repeatableTables.Clear();
        _tableGeometry.Clear();
        _tableGeometryKeys.Clear();
        ClearCollapsedTables();
    }
}
