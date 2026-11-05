using Jalium.UI.Automation;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private sealed class CssEditingHost(RichTextBox owner, FlowDocumentPresentation view) : FrameworkElement, ICssAnonymousBox
    {
        internal void Attach() => AddVisualChild(view);
        internal void Release() => RemoveVisualChild(view);
        FrameworkElement ICssAnonymousBox.Owner => owner;
        double ICssAnonymousBox.BaselineOffset(double height, bool last) => height;
        bool ICssAnonymousBox.Contains(Point point) => false;
        protected override int VisualChildrenCount => 1;
        protected override Visual? GetVisualChild(int index) => index == 0 ? view : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size availableSize) { view.Measure(availableSize); return view.DesiredSize; }
        protected override Size ArrangeOverride(Size finalSize) { view.Arrange(new(finalSize)); return finalSize; }
        protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;
    }

    private FlowDocumentPresentation? _cssDocumentView;
    private CssEditingHost? _cssDocumentHost;
    private bool _ensuringCssDocument;
    private TextSelection? _cssSelectionSource;
    private int _cssSelectionStart = -1, _cssSelectionLength = -1;
    private Brush? _cssSelectionBrush, _cssSelectionTextBrush;
    private double _cssEditorViewportHeight = double.NaN;
    private Thickness CssEditorInsets => new(Padding.Left + BorderThickness.Left, Padding.Top + BorderThickness.Top,
        Padding.Right + BorderThickness.Right, Padding.Bottom + BorderThickness.Bottom);
    private void PrepareCssEditingLayout()
    {
        if (_cssDocumentView is not null || UsesCssDocumentLayout()) EnsureLayout(GetContentBounds().Width);
    }

    private bool UsesCssDocumentLayout()
    {
        // These already use the native paragraph's shaping, bidi fragments and
        // image cache. Preserve that mature path when no new layout is needed;
        // other properties opt into the shared formatter by default.
        static bool NativePaint(CssNode node, DependencyProperty property) => property.Name is "FontFamily" or "FontSize" or "FontWeight" or "FontStyle" or "FontStretch" or
            "Foreground" or "Background" or "FlowDirection" or "TextDecorations" or "TextEffects" ||
            property.OwnerType == typeof(CssBorderImageProperties) || property.OwnerType == typeof(CssBoxDecorationBreakProperties) ||
            property.OwnerType == typeof(CssBackgroundImageResource) ||
            property.OwnerType == typeof(CssTextDecorationProperties) ||
            property == CssBackgroundPaintProperties.ValueProperty && CssBackgroundPaintProperties.Get(node.Target) is { ColorClip: CssBackgroundBox.Border } background && background.Layers.All(layer => layer.Image is null);
        static bool Styled(CssNode node) => node.Target is InlineUIContainer or BlockUIContainer ||
            node.CssRuntimeState?.Applied?.Keys.Any(property => !NativePaint(node, property)) == true ||
            node.CssLayout is not null || CssGeneratedContent.HasProjectedTarget(node.Target) || CssReplacedContent.IsActive(node.Target) ||
            node.EnumerateChildren().Any(Styled);
        if (Styled(CssNode.Get(_document))) return true;
        var inherited = CssPropertyRegistry.GetInheritedStorageProperties();
        return CssNode.Get(this).CssRuntimeState?.Applied?.Keys.Any(property => inherited.Contains(property) && !NativePaint(CssNode.Get(this), property)) == true;
    }

    protected override int VisualChildrenCount => base.VisualChildrenCount + (ReferenceEquals(_cssDocumentHost?.VisualParent, this) ? 1 : 0);
    protected override Visual? GetVisualChild(int index) => index < base.VisualChildrenCount
        ? base.GetVisualChild(index) : index == base.VisualChildrenCount && ReferenceEquals(_cssDocumentHost?.VisualParent, this)
            ? _cssDocumentHost : throw new ArgumentOutOfRangeException(nameof(index));

    internal override Geometry? GetAdditionalChildLayoutClip(Visual child) => ReferenceEquals(child, _cssDocumentHost)
        ? new RectangleGeometry(GetContentBounds()) : base.GetAdditionalChildLayoutClip(child);

    protected override Size MeasureOverride(Size availableSize)
    {
        _cssEditorViewportHeight = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height - CssEditorInsets.Top - CssEditorInsets.Bottom) : double.NaN;
        var native = base.MeasureOverride(availableSize);
        if (_cssDocumentView is null && !UsesCssDocumentLayout()) return native;
        var inset = CssEditorInsets;
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width - inset.Left - inset.Right) : 816;
        var layout = EnsureLayout(width);
        if (_cssDocumentView is null || layout is null) return native;
        return new(Math.Max(native.Width, Math.Min(availableSize.Width, layout.TotalWidth + inset.Left + inset.Right)),
            Math.Max(native.Height, Math.Min(availableSize.Height, Math.Max(GetDefaultLineHeight(), layout.TotalHeight) + inset.Top + inset.Bottom)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _cssEditorViewportHeight = Math.Max(0, finalSize.Height - CssEditorInsets.Top - CssEditorInsets.Bottom);
        var result = base.ArrangeOverride(finalSize);
        if (_cssDocumentView is null && !UsesCssDocumentLayout()) return result;
        EnsureLayout(Math.Max(1, finalSize.Width - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right));
        ArrangeCssDocument(finalSize);
        return result;
    }

    private void ArrangeCssDocument(Size size)
    {
        if (_cssDocumentView is not { } view) return;
        var inset = CssEditorInsets;
        _cssDocumentHost!.Arrange(new(inset.Left - _horizontalOffset, inset.Top - _verticalOffset,
            Math.Max(1, size.Width - inset.Left - inset.Right), Math.Max(view.DesiredSize.Height,
                double.IsFinite(_cssEditorViewportHeight) ? _cssEditorViewportHeight : GetDefaultLineHeight())));
    }

    private bool EnsureCssDocument(double width, out FlowDocumentLayoutInfo? layout)
    {
        layout = null;
        if (_ensuringCssDocument) return _cssDocumentView is not null;
        _ensuringCssDocument = true;
        try
        {
            if (!UsesCssDocumentLayout())
            {
                if (_cssDocumentView is { } previous)
                {
                    _cssDocumentView = null;
                    DetachDocumentHost();
                    _cssDocumentHost!.Release(); _cssDocumentHost = null;
                    _cssSelectionSource = null; _cssSelectionStart = _cssSelectionLength = -1; _cssSelectionBrush = null;
                    previous.Dispose();
                    _layoutCache?.Dispose(); _layoutCache = null; _layoutDirty = true;
                }
                return false;
            }
            if (_cssDocumentView is null)
            {
                _layoutCache?.Dispose(); _layoutCache = null;
                _cssDocumentView = new(observe: true, exposeAutomation: false) { Focusable = false, IsHitTestVisible = false, PassThroughSelfHit = true, IsTextSelectionEnabled = false, WritingToolsOwner = OperatingSystem.IsMacOS() ? this : null, TextDecorationOwner = this };
                _cssDocumentHost = new(this, _cssDocumentView) { IsHitTestVisible = false, Focusable = false,
                    Visibility = ShowsDocument ? Visibility.Visible : Visibility.Collapsed };
                _cssDocumentHost.Attach(); UpdateDocumentHostParent();
                _layoutDirty = true;
            }
            var hasEmbeddedElements = EnumerateDocumentElements(_document).Any();
            _cssDocumentView.IsHitTestVisible = hasEmbeddedElements;
            _cssDocumentHost!.IsHitTestVisible = hasEmbeddedElements;
            if (!IsDocumentEnabled && hasEmbeddedElements) ApplyDocumentEnabledState();
            var view = _cssDocumentView;
            view.SetDocument(_document);
            var foregroundChanged = view.SetDefaultForeground(ResolveDocumentForegroundBrush());
            var reflow = _layoutDirty || foregroundChanged || !ReferenceEquals(_layoutContext, RenderContext.Current);
            if (reflow)
            {
                view.DocumentChanged();
                // The private host can retain the same viewport constraint
                // while its document graph changes (including undo/redo).
                // Re-measure the child before painting, even without a window
                // layout manager propagating its invalidation upward.
                _cssDocumentHost!.InvalidateMeasure();
            }
            view.SetViewportWidth(width);
            view.SetViewportHeight(_cssEditorViewportHeight);
            _cssDocumentHost!.Measure(new(width, double.PositiveInfinity));
            ArrangeCssDocument(new(width + Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right, RenderSize.Height));
            var brush = ResolveSelectionBrush();
            var start = _selection?.Start.DocumentOffset ?? 0;
            var length = (_selection?.End.DocumentOffset ?? start) - start;
            if (reflow || !ReferenceEquals(_selection, _cssSelectionSource) || start != _cssSelectionStart || length != _cssSelectionLength || !ReferenceEquals(brush, _cssSelectionBrush) || !ReferenceEquals(SelectionTextBrush, _cssSelectionTextBrush))
            {
                view.SelectionBrush = brush;
                view.SelectionTextBrush = SelectionTextBrush;
                view.SetSelection(_selection, highlight: length != 0);
                _cssSelectionSource = _selection; _cssSelectionStart = start; _cssSelectionLength = length; _cssSelectionBrush = brush; _cssSelectionTextBrush = SelectionTextBrush;
            }
            _layoutCache ??= new();
            _layoutCache.TotalWidth = Math.Max(width, view.DesiredSize.Width);
            _layoutCache.TotalHeight = view.DesiredSize.Height;
            _layoutContext = RenderContext.Current; _layoutWidth = width; _layoutDirty = false;
            layout = _layoutCache;
            return true;
        }
        finally { _ensuringCssDocument = false; }
    }

    private Point CssDocumentOrigin => new(BorderThickness.Left + Padding.Left - _horizontalOffset,
        BorderThickness.Top + Padding.Top - _verticalOffset);
    private Rect CssToEditor(Rect rect) => new(rect.X + CssDocumentOrigin.X, rect.Y + CssDocumentOrigin.Y, rect.Width, rect.Height);
    private IReadOnlyList<FlowDocumentPresentation.Line> CssEditorLines => _cssDocumentView?.EditorLines ?? [];
    private TextPointer? CssPosition(Point point) => _cssDocumentView?.EditorPosition(new(point.X - CssDocumentOrigin.X, point.Y - CssDocumentOrigin.Y));

    private TextPointer? CssVisiblePosition(TextPointer? position, bool forward)
    {
        PrepareCssEditingLayout();
        if (position is null || _cssDocumentView is not { } view) return position;
        return view.EditorInsertionPosition(view.EditorSourceOffset(view.EditorExposedOffset(position.DocumentOffset), forward), forward) ?? position;
    }

    private bool DeleteCssVisibleText(bool backward, bool word)
    {
        PrepareCssEditingLayout();
        if (_cssDocumentView is not { } view || _caretPosition is null) return false;
        var text = view.EditorExposedText;
        var offset = view.EditorExposedOffset(_caretPosition.DocumentOffset);
        var other = word ? MacOSTextKeyBehavior.FindWordBoundary(text, offset, forward: !backward)
            : backward ? GraphemeClusters.PreviousBoundary(text, offset) : GraphemeClusters.NextBoundary(text, offset);
        var start = Math.Min(offset, other); var end = Math.Max(offset, other);
        var ranges = view.EditorSourceRanges(start, end - start);
        using var change = DeclareChangeBlock();
        foreach (var range in ranges.Reverse()) TryReplaceImeText(range.Start, range.Length, "");
        return true;
    }

    private TextPointer? CssLineBoundary(TextPointer? position, bool end)
    {
        if (position is null) return null;
        var lines = CssEditorLines;
        var offset = position.DocumentOffset;
        var matches = lines.Where(line => offset >= line.Start && offset <= line.End).ToArray();
        var line = position.LogicalDirection == LogicalDirection.Backward ? matches.FirstOrDefault() : matches.LastOrDefault();
        return line.Height <= 0 ? position : _document.GetPositionAtOffset(end ? line.End : line.Start,
            end ? LogicalDirection.Backward : LogicalDirection.Forward);
    }

    private TextPointer CssAdjacentLine(int direction)
    {
        var lines = CssEditorLines;
        var caret = _cssDocumentView?.EditorCaret(_caretPosition?.DocumentOffset ?? 0, _caretPosition?.LogicalDirection == LogicalDirection.Backward);
        if (caret is null || lines.Count == 0) return _caretPosition ?? _document.ContentStart;
        var current = lines.Select((line, index) => (line, index)).OrderBy(item => Math.Abs(item.line.Top - caret.Value.Top))
            .ThenBy(item => Math.Max(0, item.line.Start - (_caretPosition?.DocumentOffset ?? 0))).First().index;
        var target = current + direction;
        if (target < 0) return _document.ContentStart;
        if (target >= lines.Count) return _document.ContentEnd;
        var destination = lines[target];
        var source = _cssDocumentView!.OffsetFromPoint(new(caret.Value.X, destination.Top + destination.Height / 2));
        return _document.GetPositionAtOffset(Math.Clamp(source, destination.Start, destination.End),
            source >= destination.End ? LogicalDirection.Backward : LogicalDirection.Forward) ?? _document.ContentEnd;
    }

    private bool CssRangeGeometry(int start, int length, out ImeTextRangeGeometry geometry)
    {
        geometry = default;
        if (_cssDocumentView is not { } view) return false;
        if (length == 0)
        {
            var caret = view.EditorCaret(start, _caretPosition?.DocumentOffset == start && _caretPosition.LogicalDirection == LogicalDirection.Backward);
            if (caret is null) return false;
            geometry = new(CssToEditor(caret.Value), start, 0); return true;
        }
        var line = CssEditorLines.FirstOrDefault(line => start >= line.Start && start < line.End);
        if (line.Height <= 0) return false;
        var count = Math.Min(length, line.End - start);
        var bounds = view.EditorTextBounds(start, count).FirstOrDefault();
        if (bounds.Height <= 0) return false;
        geometry = new(CssToEditor(bounds), start, count); return true;
    }

    private double CssImeLineHeight(TextPointer anchor, double fallback) => _cssDocumentView?.EditorCaret(anchor.DocumentOffset,
        anchor.LogicalDirection == LogicalDirection.Backward)?.Height ?? fallback;

    protected override double GetVerticalScrollExtentHeight(double lineHeight)
    {
        PrepareCssEditingLayout();
        return _cssDocumentView is not null ? _layoutCache?.TotalHeight ?? 0 : base.GetVerticalScrollExtentHeight(lineHeight);
    }
    protected override double GetVerticalScrollViewportHeight()
        => _cssDocumentView is not null || UsesCssDocumentLayout() ? GetContentBounds().Height : base.GetVerticalScrollViewportHeight();
    protected override double GetHorizontalScrollExtentWidth()
    {
        PrepareCssEditingLayout();
        return _cssDocumentView is not null ? _layoutCache?.TotalWidth ?? 0 : base.GetHorizontalScrollExtentWidth();
    }
}
