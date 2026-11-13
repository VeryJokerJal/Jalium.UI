using Jalium.UI.Media;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed class CssInlineFrame(FrameworkElement element, CssInlineFrame? parent, Thickness insets, Thickness margin)
    {
        internal FrameworkElement Element = element;
        internal CssInlineFrame? Parent = parent;
        internal Thickness Insets = insets, Margin = margin;
        internal readonly List<CssInlineFramePiece> Fragments = [];
        internal Rect Allocation;
        internal int LastUnit;
        internal InitialLetterPlacement? Initial;
        internal bool Clone => CssBoxDecorationBreakProperties.Value(Element) == CssBoxDecorationBreak.Clone;
        internal bool RightToLeft => (Parent?.Element ?? Element.FrameworkParent) is { } parent &&
            CssWritingAxes.UsedDirection(parent) == FlowDirection.RightToLeft;
    }

    private sealed class CssInlineFramePiece(CssInlineFrame frame, int leftIndex, int rightIndex, bool first, bool last)
    {
        internal CssInlineFrame Frame = frame;
        internal int LeftIndex = leftIndex, RightIndex = rightIndex;
        internal bool First = first, Last = last;
        internal bool SkipDecoration;
        internal bool LeftEdge => !SkipDecoration && (Frame.Clone || (Frame.RightToLeft ? Last : First));
        internal bool RightEdge => !SkipDecoration && (Frame.Clone || (Frame.RightToLeft ? First : Last));
        internal double Left => LeftEdge ? Frame.Insets.Left : 0;
        internal double Right => RightEdge ? Frame.Insets.Right : 0;
        internal double MarginLeft => LeftEdge ? Frame.Margin.Left : 0;
        internal double MarginRight => RightEdge ? Frame.Margin.Right : 0;
        internal double Top, Baseline;
        internal Rect Rect;
        internal InitialFragmentBox? Initial;
    }

    internal sealed partial class CssTextFlowLayout
    {
        private readonly List<CssInlineFrame> _frames = [];

        // Native self-drawing controls keep their own layout. A content adapter
        // explicitly opts into fragments; inline-block/flex/grid stay atomic.
        private static bool IsInlineFrame(UIElement element)
        {
            if (!SharesWritingMode(element) || CssReplacedContent.IsActive(element)) return false;
            if (element is not FrameworkElement { CanFragmentCssInlineContent: true } host ||
                element is TextBlock text && !text.HasCssInlineBoxes && !CssGeneratedContent.HasBoxes(text) && text is not CssGeneratedText { HasImageContent: true }) return false;
            host.PrepareCssContent();
            return true;
        }

        internal static bool HasNestedFloats(UIElement element) => IsInlineFrame(element) &&
            new CssPanelChildren((FrameworkElement)element).Enumerate().Any(static child =>
                CssFloatProperties.ActiveSide(child) != CssFloatSide.None || HasNestedFloats(child));

        internal static void ReleaseInlineTree(UIElement element, FrameworkElement root)
        {
            TextSource(element)?.ReleaseCssInlineAllocation(root);
            if (element is not FrameworkElement host) return;
            SuspendRubyHost(host);
            host.ReleaseCssInlineFrame(root);
            if (host.IsCssContentHost)
                foreach (var child in new CssPanelChildren(host, includeInactive: true)) ReleaseInlineTree(child, root);
        }

        internal bool IsSourceActive(UIElement element)
        {
            var source = _sources.FirstOrDefault(source => ReferenceEquals(source.Element, element));
            return source is not null && (source.Frame is not { } frame || frame.Element.CssInlineBox is { IsActive: true } allocation && allocation.Owner == owner);
        }

        private void AppendElement(UIElement element, List<CssInlineUnit> pending, ref bool collapsedSpace, CssInlineFrame? parent = null)
        {
            PrepareFirstLineSource(element);
            if (element.Visibility == Visibility.Collapsed || element is FrameworkElement { CssLayout.IsOutOfFlow: true }) return;
            if (element is FrameworkElement { CssDisplayMode: CssDisplayMode.Ruby } ruby)
            {
                AppendRuby(ruby, new CssPanelChildren(ruby).Enumerate().ToArray(), pending, ref collapsedSpace, parent);
                return;
            }
            if (TryAppendInitialLetter(element, pending, ref collapsedSpace, parent)) return;
            if (_floatSpace is not null && CssFloatProperties.ActiveSide(element) != CssFloatSide.None)
            {
                _floatEvents.Add(new(pending.Count, element)); return;
            }
            if (IsInlineFrame(element))
            {
                var host = (FrameworkElement)element;
                var frame = new CssInlineFrame(host, parent, Axes.LineLogical(CssBoxMetrics.ContentInsets(host, _containing.Width)), Axes.LineLogical(CssBoxMetrics.Margin(host, _containing.Width)));
                _frames.Add(frame);
                var first = pending.Count;
                var split = HasNestedBlocks(host);
                if (split) AppendFrameMarker(frame, pending);
                AppendElements(new CssPanelChildren(host).Enumerate(), pending, ref collapsedSpace, frame);
                if (split) AppendFrameMarker(frame, pending);
                if (pending.Count == first || pending.Skip(first).All(static unit => unit.Collapse))
                {
                    // An empty decorated inline has a font strut, without a new
                    // visual, logical child, selectable character or word break.
                    AppendFrameMarker(frame, pending);
                }
                frame.LastUnit = pending.Count - 1;
            }
            else if (parent is not null && element is not FrameworkElement { CssInlineOuter: true } && element is not ICssAnonymousTextBox)
            {
                AppendBlock(element, parent, pending); collapsedSpace = false;
            }
            else if (IsFragmentableText(element))
            {
                AppendText(element, TextSource(element)!, element is ICssAnonymousTextBox && element is not TextBlock { IsDocumentParagraph: true }, pending, ref collapsedSpace);
                _sources[^1].Frame = parent;
            }
            else AppendAtomic(element, pending, ref collapsedSpace, parent);
        }

        private void AppendAtomic(UIElement element, List<CssInlineUnit> pending, ref bool collapsedSpace, CssInlineFrame? frame = null)
        {
            if (frame is not null)
            {
                element.InvalidateCssAllocation();
                MeasureInlineBox(element, new(_containing.Width, double.PositiveInfinity));
            }
            var text = element as TextBlock;
            var margin = element is FrameworkElement framework ? Axes.LineLogical(CssBoxMetrics.Margin(framework, _containing.Width)) : default;
            var desired = Axes.Logical(element.DesiredSize);
            var size = new Size(Math.Max(0, desired.Width), Math.Max(0, desired.Height));
            var baseline = TextBlock.GetBaselineOffset(element);
            var parallel = element is not FrameworkElement child ||
                CssWritingModeProperties.IsVertical(CssWritingModeProperties.GetWritingMode(child)) == Axes.Vertical;
            if (!double.IsFinite(baseline) && parallel && !CssDisplayLayout.TryGetBaseline(element, size.Height, last: true, out baseline))
                baseline = text?.GetLastBaselineOffset(size.Height) ?? double.NaN;
            if (!double.IsFinite(baseline)) baseline = WritingMode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
                CssWritingModeProperties.GetTextOrientation(owner) != CssTextOrientation.Sideways ? size.Height / 2 : size.Height;
            var source = new CssInlineSource(element, text, false, default, margin)
            { Atomic = true, AtomicWraps = WrapsAroundAtomic, AtomicSize = size, AtomicBaseline = baseline, LastUnit = pending.Count, Frame = frame };
            _sources.Add(source); pending.Add(new(source, 0, 0, false, false, false)); collapsedSpace = false;
        }

        private static bool BelongsTo(CssInlineSource source, CssInlineFrame ancestor)
        {
            for (var frame = source.Frame; frame is not null; frame = frame.Parent)
                if (ReferenceEquals(frame, ancestor)) return true;
            return false;
        }

        private void DecorateInlineFrames(List<CssInlineFragment> fragments, int consumed)
        {
            if (fragments.Count == 0) return;
            foreach (var fragment in fragments) { fragment.OuterLeft = fragment.OuterRight = 0; fragment.FramePieces = null; }
            if (fragments.Any(fragment => fragment.BidiDirection is not null))
            { DecorateInlineBidiFrames(fragments, consumed); return; }
            var pieces = new List<CssInlineFramePiece>();
            var rtl = UsedFlowDirection == FlowDirection.RightToLeft;
            foreach (var frame in _frames)
            {
                var first = fragments.FindIndex(fragment => BelongsTo(fragment.Source, frame));
                if (first < 0) continue;
                var last = fragments.FindLastIndex(fragment => BelongsTo(fragment.Source, frame));
                var skip = false;
                if (_initialLetter is { Inline: true, Frame: { } initialFrame } && BelongsToFrame(initialFrame, frame))
                {
                    // Ancestor boundaries start after the initial's margin
                    // edge, with inheritance and source order unchanged.
                    var ordinaryFirst = fragments.FindIndex(fragment => BelongsTo(fragment.Source, frame) && fragment.Source.Initial is null);
                    if (ordinaryFirst < 0) skip = true;
                    else
                    {
                        first = ordinaryFirst;
                        last = fragments.FindLastIndex(fragment => BelongsTo(fragment.Source, frame) && fragment.Source.Initial is null);
                    }
                }
                var piece = new CssInlineFramePiece(frame, rtl ? last : first, rtl ? first : last,
                    frame.Fragments.Count == 0, frame.LastUnit < consumed) { SkipDecoration = skip };
                pieces.Add(piece);
                fragments[piece.LeftIndex].OuterLeft += piece.Left + piece.MarginLeft;
                fragments[piece.RightIndex].OuterRight += piece.Right + piece.MarginRight;
            }
            fragments[0].FramePieces = pieces;
        }

        private static IReadOnlyList<CssInlineFramePiece> FramePieces(List<CssInlineFragment> fragments) =>
            fragments.Count > 0 ? fragments[0].FramePieces ?? [] : [];

        private void PlaceInlineFrames(List<CssInlineFragment> fragments, bool empty, double lineHeight)
        {
            var pieces = FramePieces(fragments);
            foreach (var piece in pieces)
            {
                if (piece.SkipDecoration) continue;
                var left = fragments[piece.LeftIndex].FlowLeft + piece.MarginLeft;
                var right = fragments[piece.RightIndex].FlowRight - piece.MarginRight;
                left += fragments[piece.LeftIndex].Shape?.BeforeOutside(piece.Frame.Element) ?? 0;
                right -= fragments[piece.RightIndex].Shape?.AfterOutside(piece.Frame.Element) ?? 0;
                for (var parent = piece.Frame.Parent; parent is not null; parent = parent.Parent)
                {
                    var outer = pieces.FirstOrDefault(item => ReferenceEquals(item.Frame, parent) &&
                        Math.Min(item.LeftIndex,item.RightIndex)<=Math.Min(piece.LeftIndex,piece.RightIndex) &&
                        Math.Max(item.LeftIndex,item.RightIndex)>=Math.Max(piece.LeftIndex,piece.RightIndex));
                    // An initial letter can sit outside its ancestor's
                    // ordinary decorated range on this very same line.
                    if (outer is null) continue;
                    if (outer.LeftIndex == piece.LeftIndex) left += outer.Left + outer.MarginLeft;
                    if (outer.RightIndex == piece.RightIndex) right -= outer.Right + outer.MarginRight;
                }
                if (piece.Frame.Initial is null)
                {
                    // Root ink fitting changes only the containing advance;
                    // descendant decoration retains its ordinary inline size.
                    if (UsedFlowDirection == FlowDirection.RightToLeft) left += fragments[piece.LeftIndex].InitialAdvance;
                    else right -= fragments[piece.RightIndex].InitialAdvance;
                }
                var metrics = InlineLineMetrics(FirstLineMetricsElement(piece.Frame.Element));
                piece.Rect = piece.Initial is { } initial
                    ? new(left + initial.GridOffset, _y + initial.Top, initial.Width, initial.Height)
                    : new(left + InitialFrameShift(piece.Frame, pieces), _y + piece.Top - piece.Frame.Insets.Top, Math.Max(0, right - left),
                    (empty ? 0 : metrics.Height) + piece.Frame.Insets.Top + piece.Frame.Insets.Bottom);
                piece.Rect = Axes.LinePlacement(piece.Rect, _y, lineHeight);
                piece.Baseline = Axes.ReversedLineCross ? _y + lineHeight - piece.Baseline : piece.Baseline + _y;
                piece.Frame.Fragments.Add(piece);
            }
        }

        private void InstallFrames()
        {
            UpdateFrameAllocations();
            foreach (var frame in _frames)
            {
                var union = frame.Allocation;
                frame.Element.CssInlineBox = new(frame.Element, owner,
                    () => IsActive && (frame.Parent is null || frame.Parent.Element.IsCssInlineFrame), Axes.Physical(union.Size),
                    frame.Fragments.Select(piece => PhysicalFragment(piece.Rect, union, piece.LeftEdge, piece.RightEdge)).ToArray(),
                    frame.Fragments.Count > 0 ? frame.Fragments[0].Baseline - union.Y : double.NaN,
                    frame.Fragments.Count > 0 ? frame.Fragments[^1].Baseline - union.Y : double.NaN);
                frame.Element.CssInlineLayoutOwner = owner;
                if (frame.Element.CssLayout is { } layout) layout.ContainingWidthCache = _containing.Width;
                frame.Element.InvalidateCssAllocation();
                frame.Element.InvalidateVisual();
                frame.Element.Measure(Axes.Physical(union.Size));
                _allocationChanged?.Invoke(frame.Element, union);
            }
        }

        private void UpdateFrameAllocations()
        {
            // Parent allocations also contain overflowing child boxes so their
            // origins remain stable for the unchanged native parent chain.
            for (var index = _frames.Count - 1; index >= 0; index--)
            {
                var frame = _frames[index]; var union = Rect.Empty;
                foreach (var piece in frame.Fragments) union.Union(piece.Rect);
                foreach (var source in _sources.Where(source => ReferenceEquals(source.Frame, frame))) union.Union(Allocation(source));
                foreach (var item in _nestedFloatRects.Where(item => ReferenceEquals(item.Key.VisualParent, frame.Element))) union.Union(item.Value);
                foreach (var child in _frames.Where(child => ReferenceEquals(child.Parent, frame))) union.Union(child.Allocation);
                frame.Allocation = union.IsEmpty ? new(0, 0, 0, 0) : union;
            }
        }

        private void ArrangeAllocated(Thickness insets, Action<FrameworkElement>? prepareBlock = null, Size? physicalSize = null)
        {
            CssFirstLinePainter.Offset(owner,insets);
            var physicalInsets = Axes.Physical(insets);
            // FrameworkElement publishes RenderSize after ArrangeOverride.
            // The first layout therefore needs its actual allocated size here.
            var physicalContent = CssBoxMetrics.InnerSize(physicalSize ?? owner.RenderSize, physicalInsets);
            void Arrange(UIElement element, Rect rect)
            {
                var parent = _frames.FirstOrDefault(frame => ReferenceEquals(frame.Element, CssContentsLayout.BoxParent(element)));
                var offset = parent is null ? new Point(-physicalInsets.Left, -physicalInsets.Top)
                    : Axes.Physical(parent.Allocation, physicalContent).Location;
                var physical = Axes.Physical(rect, physicalContent);
                var block = _sources.FirstOrDefault(source => source.Block && ReferenceEquals(source.Element, element));
                if (block is not null && element is FrameworkElement host) prepareBlock?.Invoke(host);
                var initial = _sources.Any(source => source.Initial is not null && ReferenceEquals(source.Element, element));
                new CssLayoutBox(element, 0).ArrangeBorderBox(new(physical.X - offset.X, physical.Y - offset.Y, physical.Width, physical.Height), Axes.Physical(_containing),
                    floats: block?.BlockEnvironment, vertical: initial ? CssBoxAlignment.Stretch : CssBoxAlignment.Start, containingWritingMode: WritingMode);
            }
            foreach (var frame in _frames) Arrange(frame.Element, frame.Allocation);
            foreach (var (element, rect) in _allocated) Arrange(element, rect);
            ArrangeRuby();
            foreach (var frame in _frames)
            {
                frame.Element.MeasureCssLayoutAbsoluteChildren(Axes.Physical(frame.Allocation.Size));
                frame.Element.ArrangeCssLayoutAbsoluteChildren(Axes.Physical(frame.Allocation.Size));
            }
        }

        private sealed class InlineMetricNode(FrameworkElement element, double height, double baseline,
            CssInlineFramePiece? frame = null, CssInlineFragment? fragment = null,
            (double Height, double Ascent, double Descent)? strutMetrics = null)
        {
            internal FrameworkElement Element = element;
            internal double StrutHeight = height, StrutBaseline = baseline;
            internal double Ascent = baseline, Descent = height - baseline, Height = height;
            internal CssInlineFramePiece? Frame = frame;
            internal CssInlineFragment? Fragment = fragment;
            internal readonly List<InlineMetricNode> Children = [];
            internal CssInlineAlignment Alignment;
            internal double Shift;
            // The metric tree is built and evaluated synchronously. Reuse the
            // strut already measured for its root/frames within this calculation.
            private readonly (double Height, double Ascent, double Descent)? _strutMetrics = strutMetrics;

            internal void Compute()
            {
                if (Children.Count == 0)
                {
                    // Preserve the original arithmetic without querying unused
                    // font metrics for a fragment leaf.
                    Height = Math.Max(0d, Ascent + Descent);
                    return;
                }
                var metrics = _strutMetrics;
                if (metrics is null)
                {
                    var measured = CssFlowProperties.LineMetrics(Element);
                    metrics = (measured.Height, measured.Ascent, measured.Descent);
                }
                var strut = metrics.Value; var alignedHeight = 0d;
                foreach (var child in Children)
                {
                    child.Compute();
                    if (child.Frame?.Frame.Initial is { Inline: true }) continue;
                    var item = child.Fragment?.Source.Element ?? child.Element;
                    var alignment = (CssInlineVerticalAlign)item.GetValue(CssFlowProperties.VerticalAlignProperty)!;
                    child.Alignment = alignment.Kind;
                    child.Shift = CssFlowProperties.InlineShift(Element, item, alignment, child.StrutBaseline,
                        child.StrutHeight, strut.Height, strut.Ascent, strut.Descent);
                    if (alignment.Kind is CssInlineAlignment.Top or CssInlineAlignment.Bottom or CssInlineAlignment.Center)
                        alignedHeight = Math.Max(alignedHeight, child.Height);
                    else
                    {
                        Ascent = Math.Max(Ascent, child.Ascent + child.Shift);
                        Descent = Math.Max(Descent, child.Descent - child.Shift);
                    }
                }
                Height = Math.Max(alignedHeight, Ascent + Descent);
            }

            internal void Place(double baseline, double lineHeight)
            {
                if (Fragment is { } fragment) fragment.MetricTop = baseline - StrutBaseline;
                if (Frame is { } frame) { frame.Top = baseline - StrutBaseline; frame.Baseline = baseline; }
                foreach (var child in Children)
                {
                    var childBaseline = child.Alignment switch
                    {
                        CssInlineAlignment.Top => child.Ascent,
                        CssInlineAlignment.Bottom => lineHeight - child.Height + child.Ascent,
                        CssInlineAlignment.Center => (lineHeight - child.Height) / 2 + child.Ascent,
                        _ => baseline - child.Shift,
                    };
                    if (child.Frame?.Frame.Initial is { Inline: true }) child.Place(child.Ascent, child.Height);
                    else child.Place(childBaseline, lineHeight);
                }
            }
        }

        private (double Height, double Ascent, List<(CssInlineAlignment Kind, double Shift)> Alignments, bool Empty) NestedFragmentMetrics(List<CssInlineFragment> fragments)
        {
            var rootStyle = FirstLineMetricsElement(owner);
            var metrics = InlineLineMetrics(rootStyle);
            var root = new InlineMetricNode(rootStyle, metrics.Height, metrics.Baseline,
                strutMetrics: (metrics.Height, metrics.Ascent, metrics.Descent));
            var nodes = new Dictionary<CssInlineFrame, InlineMetricNode>();
            var additional = new List<(CssInlineFramePiece Piece, InlineMetricNode Node)>();
            foreach (var piece in FramePieces(fragments))
            {
                if (nodes.TryGetValue(piece.Frame, out var existing))
                { additional.Add((piece,existing)); continue; }
                var style = FirstLineMetricsElement(piece.Frame.Element);
                var strut = InlineLineMetrics(style);
                var node = new InlineMetricNode(style, strut.Height, strut.Baseline, frame: piece,
                    strutMetrics: (strut.Height, strut.Ascent, strut.Descent));
                nodes.Add(piece.Frame, node);
                (piece.Frame.Parent is { } parent ? nodes[parent] : root).Children.Add(node);
            }
            foreach (var fragment in fragments)
            {
                var parent = fragment.Source.Frame is { } frame ? nodes[frame] : root;
                parent.Children.Add(new(fragment.Source.Initial is not { Inline: false } ? fragment.Source.Element as FrameworkElement ?? owner : rootStyle,
                    fragment.Line.Height, fragment.Line.Baseline, fragment: fragment));
            }
            root.Compute(); root.Place(root.Ascent, root.Height);
            foreach (var (piece,node) in additional)
            { piece.Top=node.Frame!.Top;piece.Baseline=node.Frame.Baseline; }
            PrepareInlineInitial(fragments, nodes);
            var empty = fragments.All(static fragment => !fragment.Source.Atomic && fragment.Line.Length == 0 &&
                !fragment.Line.HasLineBreakAfter && fragment.Source.Insets == default && fragment.Source.Margin == default) &&
                FramePieces(fragments).All(static piece => piece.Left == 0 && piece.Right == 0 &&
                    piece.MarginLeft == 0 && piece.MarginRight == 0 && piece.Frame.Insets.Top == 0 && piece.Frame.Insets.Bottom == 0);
            return (empty ? 0 : root.Height, root.Ascent, [], empty);
        }
    }
}
