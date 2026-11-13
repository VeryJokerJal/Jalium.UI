using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    // Source nodes retain their original CSS/document parents. Only private view
    // boxes and existing text proxies participate in the visual formatting tree.
    private sealed class DocumentCssBox(DependencyObject source) : Border, ICssStyledBox
    {
        internal readonly List<UIElement> Items = [];
        public DependencyObject StyleSource => source;
        internal CssDisplaySpecification Display;
        private Border? _borderPainter;
        protected override bool SupportsCssInlineContentLayout => true;
        protected override int VisualChildrenCount => Items.Count;
        protected override Visual? GetVisualChild(int index) => Items[index];
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
        protected override void OnPostRender(DrawingContext context)
        {
            if (CssBorderPaintProperties.Get(this) is null) { base.OnPostRender(context); return; }
            if (!CssInlineBoxPainter.Border(this, context, ref _borderPainter))
                CssBorderAdornment.Draw(this, context, ref _borderPainter);
        }
        internal void Append(UIElement child) { Items.Add(child); AddVisualChild(child); }
        internal void Remove(UIElement child) { Items.Remove(child); RemoveVisualChild(child); }
        internal void Release()
        {
            CssGeneratedContent.UnbindProjectedHost(source, this);
            if (CssMultiColumnLayout.Try(this, out var columns)) columns!.Clear();
            CssLineClampLayout.For(this).Clear();
            TextBlock.CssTextFlowLayout.ReleaseInlineTree(this, this);
            foreach (var child in Items.ToArray())
            {
                if (child is DocumentCssBox box) box.Release();
                if (child is DocumentListItemHost listItem) listItem.Release();
                Remove(child);
            }
        }
    }
    // The native ListItem owns its marker column, independently of its List's
    // CSS padding. This also preserves the percentage containing block used by
    // the direct document layout when the surrounding flow becomes grid/flex.
    private sealed class DocumentListItemHost(FlowDocumentPresentation owner, DocumentCssBox content, double indent, FlowDirection direction)
        : Border, ICssAnonymousBox
    {
        FrameworkElement ICssAnonymousBox.Owner => owner;
        double ICssAnonymousBox.BaselineOffset(double height, bool last)
            => CssDisplayLayout.TryGetBaseline(content, content.RenderSize.Height, last, out var baseline) ? baseline : height;
        internal void Attach()
        {
            SetValue(CssDisplayProperties.SpecificationProperty, new CssDisplaySpecification(CssDisplayMode.Block));
            SetValue(CssDisplayLayout.ModeProperty, CssDisplayMode.Block);
            Padding = direction == FlowDirection.RightToLeft ? new Thickness(0, 0, indent, 0) : new Thickness(indent, 0, 0, 0);
            Child = content;
        }
        internal void Release() { TextBlock.CssTextFlowLayout.ReleaseInlineTree(this, this); content.Release(); Child = null; }
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
    }
    // An inline/contents document is still inside a viewport formatting context.
    // Measuring that source box directly skips its promoted block descendants.
    private sealed class DocumentCssRootHost : FrameworkElement, ICssAnonymousBox
    {
        private readonly FlowDocumentPresentation _owner;
        private readonly DocumentCssBox _content;
        internal DocumentCssRootHost(FlowDocumentPresentation owner, DocumentCssBox content)
        {
            _owner = owner; _content = content;
            SetValue(CssDisplayProperties.SpecificationProperty, new CssDisplaySpecification(CssDisplayMode.Block));
            SetValue(CssDisplayLayout.ModeProperty, CssDisplayMode.Block);
            AddVisualChild(content);
        }
        FrameworkElement ICssAnonymousBox.Owner => _owner;
        double ICssAnonymousBox.BaselineOffset(double height, bool last) => height;
        protected override bool SupportsCssContentLayout => true;
        protected override bool SupportsCssInlineContentLayout => true;
        protected override int VisualChildrenCount => 1;
        protected override Visual? GetVisualChild(int index) => index == 0 ? _content : throw new ArgumentOutOfRangeException(nameof(index));
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
        internal void Release()
        {
            TextBlock.CssTextFlowLayout.ReleaseInlineTree(this, this);
            RemoveVisualChild(_content);
        }
    }
    private DocumentCssBox? _documentCssRoot;
    private DocumentCssRootHost? _documentCssRootHost;
    private FrameworkElement? DocumentCssSurface => (FrameworkElement?)_documentCssRootHost ?? _documentCssRoot;
    private readonly Dictionary<DependencyObject, DocumentCssBox> _documentCssBoxes = [];
    private readonly List<IDisposable> _documentCssConsumers = [];
    private Size _documentCssSlot;
    private Size _documentCssContainingBlock;

    private static IEnumerable<DependencyObject> DocumentBoxChildren(DependencyObject source) => source switch
    {
        FlowDocument document => document.Blocks,
        Section section => section.Blocks,
        DocList list => list.ListItems,
        ListItem item => item.Blocks,
        Table table => table.Columns.Cast<DependencyObject>().Concat(table.RowGroups),
        TableRowGroup group => group.Rows,
        TableRow row => row.Cells,
        TableCell cell => cell.Blocks,
        BlockUIContainer container when container.Child is { } child => [child],
        _ => [],
    };
    private static CssDisplayMode NativeDocumentDisplay(DependencyObject source) => source switch
    {
        Table => CssDisplayMode.Table,
        TableRowGroup => CssDisplayMode.TableRowGroup,
        TableRow => CssDisplayMode.TableRow,
        TableCell => CssDisplayMode.TableCell,
        TableColumn => CssDisplayMode.TableColumn,
        _ => CssDisplayMode.Block,
    };
    private static CssDisplaySpecification DocumentDisplay(DependencyObject source)
    {
        if (source.GetEffectiveValueLayer(CssDisplayProperties.SpecificationProperty) is null)
            return CssDisplayProperties.Compute(CssNode.Get(source), new(NativeDocumentDisplay(source), ListItem: source is ListItem));
        return CssDisplayProperties.Computed(CssNode.Get(source));
    }
    private static bool NeedsDocumentCssBoxes(DependencyObject source)
    {
        if (source is BlockUIContainer || CssGeneratedContent.HasProjectedTarget(source) || CssReplacedContent.IsActive(source)) return true;
        if (CssContainmentProperties.GetContain(source) != CssContainFlags.None ||
            CssContainmentProperties.GetContentVisibility(source) != CssContentVisibility.Visible) return true;
        // Native document layout already owns block dimensions, intrinsic
        // sizing, columns and ordinary text. Preserve that formatter unless
        // the source actually changes the document's box-tree structure.
        if (source is Block &&
            (CssFloatSide)source.GetValue(CssFloatProperties.FloatProperty)! != CssFloatSide.None) return true;
        if (source is Paragraph paragraph && paragraph.Inlines.Any(TextBlock.InlineNeedsBox)) return true;
        if (source is ListItem && CssListProperties.Inside(source)) return true;
        var display = DocumentDisplay(source);
        if (display.None) return false;
        if (source is FlowDocument && display.Mode == CssDisplayMode.Contents) return true;
        if (source is ListItem && source.GetEffectiveValueLayer(CssDisplayProperties.SpecificationProperty) is not null && display.Mode != CssDisplayMode.Contents && !display.ListItem) return true;
        if (display.Mode != CssDisplayMode.Contents &&
            (display.Inline || display.ListItem && source is not ListItem || display.Mode != NativeDocumentDisplay(source) &&
                !(source is TableRowGroup && display.Mode is CssDisplayMode.TableHeaderGroup or CssDisplayMode.TableFooterGroup) &&
                !(NativeDocumentDisplay(source) == CssDisplayMode.Block && display.Mode == CssDisplayMode.FlowRoot))) return true;
        return DocumentBoxChildren(source).Any(NeedsDocumentCssBoxes);
    }

    private void ClearDocumentCssBoxes()
    {
        DetachDocumentMarkers();
        if (_documentCssRoot is not { } root) return;
        if (_documentCssRootHost is { } host)
        {
            host.Release(); RemoveVisualChild(host); _documentCssRootHost = null;
        }
        else RemoveVisualChild(root);
        root.Release();
        _documentCssRoot = null; _documentCssBoxes.Clear();
        foreach (var consumer in _documentCssConsumers) consumer.Dispose();
        _documentCssConsumers.Clear();
        foreach (var view in _paragraphs)
        {
            CssGeneratedContent.UnbindProjectedHost(view.Source, view.Text);
            view.Text.ClearValue(CssDisplayProperties.SpecificationProperty);
            view.Text.ClearValue(CssDisplayLayout.ModeProperty);
            view.Text.CssInlineOuter = false;
            view.Text.IsDocumentBoxContent = false;
            if (view.Text.VisualParent is null) AddVisualChild(view.Text);
        }
    }

    private static readonly DependencyProperty[] DocumentBoxNativeProperties =
    [
        FrameworkElement.WidthProperty, FrameworkElement.HeightProperty,
        FrameworkElement.MinWidthProperty, FrameworkElement.MinHeightProperty,
        FrameworkElement.MaxWidthProperty, FrameworkElement.MaxHeightProperty,
        TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
        TextElement.FontStyleProperty, TextElement.FontStretchProperty, TextElement.ForegroundProperty,
        FrameworkElement.FlowDirectionProperty,
    ];
    private void MirrorDocumentBoxStyle(DependencyObject source, DocumentCssBox box)
    {
        var state = CssNode.Get(source).CssRuntimeState;
        var properties = new HashSet<DependencyProperty>(CssPropertyRegistry.GetInheritedStorageProperties());
        properties.UnionWith(DocumentBoxNativeProperties);
        if (state?.Applied is { } applied) properties.UnionWith(applied.Keys);
        foreach (var name in new[] { "Margin", "Padding", "BorderThickness", "BorderBrush", "Background", "CornerRadius" })
            if (CssDependencyPropertyLookup.Find(source.GetType(), name) is { } property) properties.Add(property);
        foreach (var property in properties)
        {
            if (property.ReadOnly || property == CssDisplayProperties.SpecificationProperty || property == CssDisplayLayout.ModeProperty || property == CssDisplayLayout.InlineProperty) continue;
            // Some nonvisual roots store native fallbacks on Control's DPs;
            // Border has distinct DPs with those same presentation names.
            var target = property == CssBorderUsedThicknessProperties.ValueProperty ? Border.BorderThicknessProperty
                : CssDependencyPropertyLookup.Find(box.GetType(), property.Name) ?? property;
            if (target.PropertyType != property.PropertyType) continue;
            if (source.HasLocalOrAnimatedValue(property)) box.SetValue(target, source.GetValue(property));
            else box.SetLayerValue(target, source.GetValue(property), state?.Applied?.TryGetValue(property, out var css) == true ? css.Layer : LayerValueSource.StyleSetter);
        }
        if (CssDependencyPropertyLookup.Find(source.GetType(), "BorderBrush") is null &&
            source.GetValue(CssBorderPaintProperties.ValueProperty) is CssBorderPaint paint &&
            state?.Applied?.TryGetValue(CssBorderPaintProperties.ValueProperty, out var borderPaint) == true)
            box.SetLayerValue(Border.BorderBrushProperty, paint.Top, borderPaint.Layer);
        box.CssLayout = CssNode.Get(source).CssLayout?.CloneForLayout();
        box.Display = DocumentDisplay(source);
        box.SetValue(CssDisplayProperties.SpecificationProperty, box.Display);
        box.SetValue(CssDisplayLayout.ModeProperty, box.Display.Mode);
        box.SetValue(CssDisplayLayout.InlineProperty, box.Display.Inline);
        if (box.Display.None) box.SetLayerValue(UIElement.VisibilityProperty, Visibility.Collapsed, LayerValueSource.CssBase);
        var lineHeight = CssComputedLineHeightValue.Inherited(CssNode.Get(source));
        if (!CssComputedLineHeightValue.HasCssSource(source) && lineHeight.Kind == CssLineHeightKind.Normal && double.IsFinite(_document!.LineHeight))
            lineHeight = new(CssLineHeightKind.Pixels, _document.LineHeight);
        box.SetValue(CssComputedLineHeightValue.Property, lineHeight);
        var alignment = source is FlowDocument ? FlowDocument.TextAlignmentProperty : source is TableCell ? TableCell.TextAlignmentProperty : Block.TextAlignmentProperty;
        if (source.HasLocalOrAnimatedValue(alignment)) box.SetValue(TextBlock.TextAlignmentProperty, source.GetValue(alignment));
        if (source is Table table) box.SetValue(CssTableProperties.SpacingProperty, CssTableProperties.Spacing(table));
        if (source is TableCell cell && box.Display.Mode == CssDisplayMode.TableCell) { Grid.SetColumnSpan(box, cell.ColumnSpan); Grid.SetRowSpan(box, cell.RowSpan); }
        if (source is TableColumn { Width.IsAbsolute: true } column) box.Width = column.Width.Value;
    }

    private void LayoutDocumentCssBoxes(double width)
    {
        ClearDocumentCssBoxes();
        ClearDocumentClamping(); ClearDocumentColumns(); ClearDocumentContentsRuns();
        _lines.Clear(); _positionLines.Clear(); _blocks.Clear(); _fragmentBoxes.Clear(); _heightSpaces.Clear();
        _tableCells.Clear(); _tableGeometry.Clear(); _tableGeometryKeys.Clear(); ClearCollapsedTables();
        _hasDocumentHeights = _hasNestedColumns = false;
        var views = _paragraphs.ToDictionary(view => view.Source);
        foreach (var view in _paragraphs)
        {
            RemoveVisualChild(view.Text);
            view.Text.DocumentDisplaySuppressed = false; view.Text.Visibility = Visibility.Visible;
            view.Text.CssInlineOuter = true; view.TableClip = null; view.ColumnClips = null;
            view.Text.IsDocumentBoxContent = true;
            view.TableSuppressed = view.ClampSuppressed = false; view.ContentsFlow = false;
        }
        FrameworkElement Build(DependencyObject source)
        {
            var box = new DocumentCssBox(source); _documentCssBoxes[source] = box;
            MirrorDocumentBoxStyle(source, box); box.SetRootDpi(DpiScale);
            AttachDocumentMarker(source, box, generated: true);
            if (_observe) _documentCssConsumers.Add(CssDocumentConsumers.Register(source, box));
            if (source is Paragraph paragraph)
            {
                var text = views[paragraph].Text;
                // The native reader's default Left fallback is not an authored
                // override of CSS start alignment in a projected RTL context.
                var nativeAlignment = _document!.HasLocalOrAnimatedValue(FlowDocument.TextAlignmentProperty);
                for (DependencyObject? current = paragraph; current is FrameworkContentElement content; current = content.Parent)
                {
                    var alignment = current is TableCell ? TableCell.TextAlignmentProperty : Block.TextAlignmentProperty;
                    nativeAlignment |= current.GetValueSourceInternal(alignment).BaseValueSource is not (BaseValueSource.Default or BaseValueSource.Inherited) &&
                        (current.HasLocalOrAnimatedValue(alignment) || CssNode.Get(current).CssRuntimeState?.Applied?.ContainsKey(alignment) != true);
                }
                if (!nativeAlignment) text.ClearValue(TextBlock.TextAlignmentProperty);
                if (text.HasLocalOrAnimatedValue(TextBlock.TextAlignmentProperty)) box.SetValue(TextBlock.TextAlignmentProperty, text.TextAlignment);
                if (box.Display.Mode is CssDisplayMode.Flex or CssDisplayMode.Grid)
                {
                    CssContentsLayout.Observe(CssDisplayMode.Contents);
                    text.SetValue(CssDisplayProperties.SpecificationProperty, new CssDisplaySpecification(CssDisplayMode.Contents));
                    text.SetValue(CssDisplayLayout.ModeProperty, CssDisplayMode.Contents);
                }
                if (CssReplacedContent.IsActive(paragraph))
                    CssGeneratedContent.BindProjectedHost(paragraph, box, () => { if (!_preparingMarkers) DocumentChanged(); });
                else
                    CssGeneratedContent.BindProjectedHost(paragraph, text, () => { if (!_preparingMarkers) DocumentChanged(); });
                box.Append(text);
            }
            else if (source is BlockUIContainer container)
            {
                CssGeneratedContent.BindProjectedHost(source, box, () => { if (!_preparingMarkers) DocumentChanged(); });
                if (container.Child is { } blockChild)
                {
                    if (blockChild.VisualParent is Visual oldParent && !ReferenceEquals(oldParent, box))
                        blockChild.DetachFromVisualParent();
                    box.Append(blockChild);
                }
            }
            else
            {
                CssGeneratedContent.BindProjectedHost(source, box, () => { if (!_preparingMarkers) DocumentChanged(); });
                foreach (var child in DocumentBoxChildren(source)) box.Append(Build(child));
            }
            if (source is ListItem { List: { } list } && box.Display.ListItem &&
                DocumentDisplay(list) is { Mode: CssDisplayMode.Block or CssDisplayMode.FlowRoot or CssDisplayMode.Contents, Inline: false } &&
                box.Display.Mode != CssDisplayMode.Contents && !box.Display.None)
            {
                var host = new DocumentListItemHost(this, box, NativeListIndent(list), CssListProperties.MarkerDirection(source));
                host.Attach(); return host;
            }
            return box;
        }
        _documentCssRoot = (DocumentCssBox)Build(_document!);
        if (_documentCssRoot.Display.Mode == CssDisplayMode.Contents || _documentCssRoot.Display is { Mode: CssDisplayMode.Native, Inline: true })
            _documentCssRootHost = new(this, _documentCssRoot);
        var surface = DocumentCssSurface!; AddVisualChild(surface);
        RemeasureDocumentCssBoxes(width);
    }
    private void RemeasureDocumentCssBoxes(double width)
    {
        if (DocumentCssSurface is not { } surface || _document is null) return;
        _lines.Clear(); _positionLines.Clear(); _blocks.Clear(); _fragmentBoxes.Clear(); _heightSpaces.Clear();
        _tableCells.Clear(); _tableGeometry.Clear(); _tableGeometryKeys.Clear();
        _hasDocumentHeights = false;
        var viewportHeight = _pageSize?.Height ?? _viewportHeight;
        _documentCssContainingBlock = new(width, double.IsFinite(viewportHeight)
            ? Math.Max(0, viewportHeight - _document.PagePadding.Top - _document.PagePadding.Bottom) : double.PositiveInfinity);
        new CssLayoutBox(surface, 0).MeasureBorderBox(new(width, double.PositiveInfinity), _documentCssContainingBlock);
        _documentCssSlot = new(width, surface.DesiredSize.Height);
        ArrangeDocumentCssBoxes();
        _extentWidth = Math.Max(width, surface.DesiredSize.Width);
        _extentHeight = surface.DesiredSize.Height;
        CollectDocumentCssGeometry();
        if (_extentHeight > 0) { _heightSpaces.Add(new(_document, 0, _extentHeight)); _hasDocumentHeights = true; }
        PrepareTablePagination();
    }
    private void ArrangeDocumentCssBoxes()
    {
        if (DocumentCssSurface is not { } root || _document is not { } document) return;
        var p = document.PagePadding;
        new CssLayoutBox(root, 0).ArrangeBorderBox(new(p.Left, p.Top - _pageTop, _documentCssSlot.Width, _documentCssSlot.Height), _documentCssContainingBlock);
        foreach (var view in _paragraphs)
        {
            var at = view.Text.TransformToAncestor(this);
            view.Bounds = new(at.X - p.Left, at.Y - p.Top + _pageTop, view.Text.RenderSize.Width, view.Text.RenderSize.Height);
            view.ContentsFlow = view.Text.DocumentSharedFlow is not null;
        }
    }
    private bool DocumentInlineSeparator(DependencyObject source) => _documentCssRoot is not null && source is Paragraph &&
        _documentCssBoxes.TryGetValue(source, out var box) && box.Display.Inline;

    private AutomationVisibility? DocumentCssVisibility(ParagraphView view, bool outerClips, out Matrix toBody)
    {
        toBody = Matrix.Identity;
        if (!GetRenderMatrix().TryInvert(out var inverse)) return null;
        toBody = Matrix.Multiply(view.Text.GetRenderMatrix(), inverse);
        return new(view.Text, outerClips ? null : this);
    }
    private IReadOnlyList<Rect> DocumentCssTextBounds(int start, int length, bool clip)
    {
        var result = new List<Rect>();
        foreach (var view in _paragraphs)
        {
            if (!view.Text.IsVisible) continue;
            var from = Math.Max(start, view.Offset); var to = Math.Min(start + length, view.Offset + view.Text.Text.Length);
            if (to < from || DocumentCssVisibility(view, false, out var transform) is not { } visibility) continue;
            foreach (var rect in view.Text.DocumentTextBounds(from - view.Offset, to - from))
                if (visibility.TryClip(rect, transform, out var bounds)) result.Add(bounds);
        }
        return clip ? AutomationVisibility.ClipRectangles(this, result) : result;
    }
    private bool TryOffsetFromDocumentCssPoint(Point point, out int offset)
    {
        offset = 0; var distance = double.PositiveInfinity; ParagraphView? nearest = null; var hit = default(Point);
        foreach (var view in _paragraphs)
        {
            if (!view.Text.IsVisible || DocumentCssVisibility(view, true, out var transform) is not { } visibility || !transform.TryInvert(out var inverse)) continue;
            var preferred = inverse.Transform(point);
            foreach (var rect in view.Text.DocumentTextBounds(0, view.Text.Text.Length).Concat(view.Text.DocumentContentsMarkerBounds is { } marker ? [marker] : Array.Empty<Rect>()))
            {
                if (!visibility.TryClip(rect, transform, out var bounds) || !visibility.TryGetPoint(rect, preferred, out var inside)) continue;
                var next = DistanceToBox(point, bounds); if (next >= distance) continue;
                distance = next; nearest = view; hit = inside;
            }
        }
        if (nearest is null) return false;
        offset = nearest.Offset + nearest.Text.DocumentCharacterFromPoint(hit); return true;
    }

    private void CollectDocumentCssGeometry()
    {
        var grouped = new Dictionary<(object Flow, double Top, double Height), int>();
        foreach (var view in _paragraphs)
        {
            if (!view.Text.IsVisible) continue;
            var visibility = DocumentCssVisibility(view, false, out var transform);
            if (visibility is null) continue;
            foreach (var line in view.Text.GetDocumentLines(view.Text.RenderSize.Width))
            {
                var visible = Rect.Empty;
                foreach (var rect in view.Text.DocumentTextLineBounds(line.Start, line.End - line.Start))
                    if (visibility.TryClip(rect, transform, out var projected)) visible.Union(projected);
                if (visible.IsEmpty) continue;
                var p = _document!.PagePadding;
                var top = visible.Top - p.Top + _pageTop;
                var value = new Line(view.Offset + line.Start, view.Offset + line.End, top, visible.Height, false, visible.Left - p.Left, visible.Width);
                // Different lines can share y after column projection. Merge
                // seams only when they came from the same original shared line.
                var key = (view.Text.DocumentSharedLine(line.Start, line.End) ?? view, Math.Round(top, 6), Math.Round(visible.Height, 6));
                if (grouped.TryGetValue(key, out var at))
                {
                    var previous = _lines[at]; var left = Math.Min(previous.Left, value.Left); var right = Math.Max(previous.Left + previous.Width, value.Left + value.Width);
                    _lines[at] = previous with { Start = Math.Min(previous.Start, value.Start), End = Math.Max(previous.End, value.End), Left = left, Width = right - left };
                }
                else { grouped[key] = _lines.Count; _lines.Add(value); }
            }
        }
        _lines.Sort(static (a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
        foreach (var (source, box) in _documentCssBoxes)
        {
            if (source is not TextElement text || box.Display.None || box.Display.Mode == CssDisplayMode.Contents) continue;
            var at = box.TransformToAncestor(this); var p = _document!.PagePadding;
            var bounds = new Rect(at.X - p.Left, at.Y - p.Top + _pageTop, box.ActualWidth, box.ActualHeight);
            if (CssTableLayout.IsPart(box.Display.Mode) || box.Display.Mode == CssDisplayMode.Table) SetTableGeometry(source, bounds);
            if (source is TableCell cell && CssTableLayout.CellTrack(box) is { } track)
                _tableCells.Add(new(cell, bounds, track.Row, track.Column, track.RowSpan, track.ColumnSpan));
            if (!box.IsVisible) continue;
            var start = _document!.GetDocumentOffset(text, 0); var end = start + TextElement.GetContentLength(text);
            var indices = _lines.Select((line, index) => (line, index)).Where(item => item.line.Start <= end && item.line.End >= start).Select(item => item.index).ToArray();
            if (indices.Length == 0 && !_tableGeometry.ContainsKey(source)) continue;
            _fragmentBoxes.Add(new(text, indices.Length == 0 ? 0 : indices.Min(), indices.Length == 0 ? 0 : indices.Max() + 1, at.Y - p.Top + _pageTop, at.Y - p.Top + _pageTop, at.Y - p.Top + _pageTop + box.ActualHeight));
        }
    }
}
