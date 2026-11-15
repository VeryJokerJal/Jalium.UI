using Jalium.UI.Automation.Peers;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    // Formatting state only: do not write a local/CSS property over the
    // author's text-align or invalidate the paragraph while arranging it.
    private bool _cssRubyJustify;
    private double _cssRubyEdgeInset;
    private void SetRubyEdgeInset(double value)
    {
        if (_cssRubyEdgeInset == value) return;
        _cssRubyEdgeInset = value; ClearNativeBidiLines();
    }
    private void SetRubyJustification(bool value)
    {
        if (!value) SetRubyEdgeInset(0);
        if (_cssRubyJustify == value) return;
        _cssRubyJustify = value;
        ClearNativeBidiLines();
    }

    private static bool RubyAutoOpportunity(System.Text.Rune before, System.Text.Rune after, DependencyObject context, bool annotationContext = false)
    {
        static bool Bopomofo(System.Text.Rune rune) => rune.Value is >= 0x3100 and <= 0x312F or >= 0x31A0 and <= 0x31BF or
            0x02C7 or 0x02CA or 0x02CB or 0x02D9;
        if (!Bopomofo(before) || !Bopomofo(after) ||
            context.GetValueSourceInternal(CssFlowProperties.TextJustifyProperty).BaseValueSource != BaseValueSource.Default) return true;
        if (annotationContext) return false;
        for (var node = CssNode.Get(context); node is not null; node = node.FrameworkParent)
            if (CssRubyWritingMode.IsAnnotationContext(node.Target)) return false;
        return true;
    }

    internal sealed partial class CssTextFlowLayout
    {
        private readonly List<RubyModel> _ruby = [];
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, CssTextFlowLayout> s_rubyOwners = new();

        private sealed partial class RubyGroup(FrameworkElement style, UIElement[] items, FrameworkElement? annotationContainer = null,
            FrameworkElement? baseContainer = null)
        {
            internal readonly FrameworkElement Style = style;
            internal readonly UIElement[] Items = items;
            internal readonly FrameworkElement? AnnotationContainer = annotationContainer;
            internal readonly FrameworkElement? BaseContainer = baseContainer;
            internal bool InterCharacter;
            private CssWritingAxes _axes;
            internal Size HostSize => InterCharacter ? new(Height, Width) : new(Width, Height);
            internal bool AutomaticPhysicalHeight => Items.Length != 1 || Items[0] is not FrameworkElement item ||
                double.IsNaN(item.Height) && (item.CssLayout?.Height is not { IsSet: true } height || height.IsAuto);
            internal readonly List<(UIElement Element, Size Size, double Baseline)> Pieces = [];
            internal double Width, Height, Baseline;
            internal double LineHeight, LineBaseline;

            internal void Measure(CssTextFlowLayout flow)
            {
                _axes = InterCharacter ? new(CssWritingMode.VerticalRl) : flow.Axes;
                Width = Height = Baseline = LineHeight = LineBaseline = 0; Pieces.Clear();
                double descent = 0, lineDescent = double.NegativeInfinity;
                LineBaseline = double.NegativeInfinity;
                foreach (var item in Items)
                {
                    if (item is TextBlock textItem)
                    {
                        textItem.SetRubyJustification(false);
                        // An anonymous vertical annotation retains its child's
                        // DOM writing mode and its original native paragraph.
                        textItem._cssRubyAnonymousContent = InterCharacter && !CssRubyRoles.IsPart(textItem.CssDisplayMode);
                    }
                    item.InvalidateCssAllocation();
                    flow.MeasureInlineBox(item, new(double.PositiveInfinity, double.PositiveInfinity));
                    var size = _axes.Logical(item.DesiredSize);
                    if (!CssDisplayLayout.TryGetBaseline(item, size.Height, false, out double baseline))
                        baseline = item is TextBlock text ? text.GetFirstBaselineOffset(size.Height) : size.Height;
                    if (!double.IsFinite(baseline) || item is FrameworkElement element &&
                        CssWritingModeProperties.GetWritingMode(element) != _axes.Mode) baseline = size.Height;
                    Pieces.Add((item, size, baseline));
                    Width += size.Width; Baseline = Math.Max(Baseline, baseline);
                    descent = Math.Max(descent, size.Height - baseline);
                    var raw = CssFlowProperties.LineMetrics(item as FrameworkElement ?? Style, rubyFontArea: false);
                    double lineBaseline = flow.RubyMetricBaseline(raw.Height, raw.Baseline);
                    double lineHeight = raw.Height;
                    if (!CssRubyBoxMetrics.IsFontArea(item)) { lineBaseline = baseline; lineHeight = size.Height; }
                    LineBaseline = Math.Max(LineBaseline, lineBaseline);
                    lineDescent = Math.Max(lineDescent, lineHeight - lineBaseline);
                }
                Height = Baseline + descent;
                LineHeight = LineBaseline + lineDescent;
                if (Pieces.Count == 0)
                {
                    var strut = CssFlowProperties.LineMetrics(Style, rubyFontArea: false);
                    Height = strut.Ascent + strut.Descent; Baseline = flow.RubyMetricBaseline(Height, strut.Ascent);
                    LineHeight = strut.Height; LineBaseline = flow.RubyMetricBaseline(strut.Height, strut.Baseline);
                }
            }

            internal IEnumerable<(UIElement Element, Rect Rect)> Place(Rect area)
            {
                if (IsMerged)
                {
                    foreach (var placement in PlaceMerged(area)) yield return placement;
                    yield break;
                }
                if (InterCharacter)
                {
                    // A vertical annotation is an independent inline-block in
                    // the horizontal base level. Keep its full allocated box;
                    // the existing text formatter distributes its real glyphs.
                    var logical = new Rect(0, 0, area.Height, area.Width);
                    if (Pieces.Count == 1 && CssRubyRoles.Role(Pieces[0].Element) == CssDisplayMode.RubyText)
                    {
                        var piece = Pieces[0];
                        if (piece.Element is TextBlock { UsesNativeRubyContent: true } text)
                        {
                            var sideAlign = CssWritingModeProperties.GetRubyAlign(Style);
                            text.SetRubyJustification(sideAlign is CssRubyAlign.SpaceBetween or CssRubyAlign.SpaceAround);
                            if (sideAlign == CssRubyAlign.SpaceAround &&
                                text._layoutLines.Count == 1 && text.TryGetJustifiedLine(text._layoutLines[0], logical.Width, out var plan))
                                text.SetRubyEdgeInset(Math.Max(0, logical.Width - text._layoutLines[0].Width) / (2 * (plan.Gaps.Length + 1)));
                        }
                        yield return (piece.Element, area);
                        yield break;
                    }
                    var groupAlign = CssWritingModeProperties.GetRubyAlign(Style);
                    double at = groupAlign == CssRubyAlign.Start ? 0 : Math.Max(0, logical.Width - Width) / 2;
                    foreach (var piece in Pieces)
                    {
                        var rect = _axes.Physical(new(at, Baseline - piece.Baseline, piece.Size.Width, piece.Size.Height), area.Size);
                        rect.Offset(area.X, area.Y); yield return (piece.Element, rect); at += piece.Size.Width;
                    }
                    yield break;
                }
                var align = CssWritingModeProperties.GetRubyAlign(Style);
                double free = Math.Max(0, area.Width - Width), step = 0;
                double x = area.X;
                if (align == CssRubyAlign.Center || Pieces.Count == 1 && align != CssRubyAlign.Start) x += free / 2;
                else if (align == CssRubyAlign.SpaceBetween && Pieces.Count > 1) step = free / (Pieces.Count - 1);
                else if (align == CssRubyAlign.SpaceAround && Pieces.Count > 0) { step = free / Pieces.Count; x += step / 2; }
                bool rtl = CssWritingAxes.UsedDirection(Style) == FlowDirection.RightToLeft;
                foreach (var piece in Pieces)
                {
                    double pieceWidth = piece.Size.Width, pieceX = x;
                    if (Pieces.Count == 1 && free > .001 &&
                        align is CssRubyAlign.SpaceBetween or CssRubyAlign.SpaceAround &&
                        piece.Element is TextBlock { HasCssInlineBoxes: false } text && text._layoutLines.Count == 1)
                    {
                        text.SetRubyJustification(true);
                        if (text.TryGetJustifiedLine(text._layoutLines[0], text.GetContentWidth(area.Width), out var justified))
                        {
                            double edge = align == CssRubyAlign.SpaceAround ? free / (2 * (justified.Gaps.Length + 1)) : 0;
                            pieceWidth = area.Width - 2 * edge; pieceX = area.X + edge;
                        }
                        else text.SetRubyJustification(false);
                    }
                    double at = rtl ? area.Right - (pieceX - area.X) - pieceWidth : pieceX;
                    yield return (piece.Element, new(at, area.Y + Baseline - piece.Baseline, pieceWidth, piece.Size.Height));
                    x += piece.Size.Width + step;
                }
            }
        }

        private sealed record RubyAnnotation(RubyGroup Group, int First, int Last, int Level, bool Hidden = false)
        {
            internal CssRubyMerge Merge => Group.InterCharacter ? CssRubyMerge.Separate :
                CssWritingModeProperties.GetRubyMerge(Group.AnnotationContainer ?? Group.Style);
        }

        // This box contributes one associated base/annotation unit to the real
        // paragraph. It has no authored, accessibility or selectable text node.
        // Adjacent units remain distinct line-breaking opportunities.
        private sealed class RubyCellBox(FrameworkElement host, CssWritingAxes axes) : FrameworkElement, ICssAnonymousBox
        {
            internal readonly List<(RubyGroup Group, Rect Area)> Tracks = [];
            internal Size LogicalSize;
            internal double Baseline;
            internal CssInlineSource? Source;
            internal CssInlineFrame? Frame;
            internal Rect BaseLevelArea;
            internal RubyMergeSegment? MergeSegment;
            internal int FirstBase, LastBase;
            internal readonly Dictionary<int, Rect> Columns = [];
            public FrameworkElement Owner => host;
            public double BaselineOffset(double height, bool last) => Baseline;
            protected override Size MeasureOverride(Size availableSize) => axes.Physical(LogicalSize);
            protected override Size ArrangeOverride(Size finalSize) => finalSize;
            protected override AutomationPeer? OnCreateAutomationPeer() => null;
        }

        private sealed class RubyModel(FrameworkElement host, CssWritingAxes axes, CssInlineFrame? frame)
        {
            internal readonly FrameworkElement Host = host;
            internal readonly CssWritingAxes Axes = axes;
            internal readonly CssInlineFrame? Frame = frame;
            internal readonly List<RubyCellBox> Cells = [];
            internal readonly HashSet<FrameworkElement> Containers = [];
            internal readonly HashSet<UIElement> Items = [];
            internal readonly List<RubySegment> SideSegments = [];
            internal readonly List<RubyMergeSegment> MergeSegments = [];
            internal readonly Dictionary<UIElement, (bool Hidden, bool Ignore, DependencyObject? Root)> VisibilityStates = [];
            internal readonly Dictionary<UIElement, bool> MetricStates = [];
        }

        private sealed class RubySegment
        {
            internal readonly List<RubyCellBox> Cells = [];
            internal readonly HashSet<FrameworkElement> Containers = [];
        }

        private readonly Dictionary<UIElement, FrameworkElement> _retiredRubyVisibility = [];
        private readonly Dictionary<UIElement, FrameworkElement> _retiredRubyMetrics = [];

        private void FinishRubyVisibility()
        {
            foreach (var (item, host) in _retiredRubyVisibility) CssRubyVisibility.Clear(item, host);
            _retiredRubyVisibility.Clear();
            foreach (var (item, host) in _retiredRubyMetrics) CssRubyBoxMetrics.Clear(item, host);
            _retiredRubyMetrics.Clear();
        }

        private void ClearRuby(bool preserveVisibility = false)
        {
            _hasContextualRuby = false;
            if (!preserveVisibility) FinishRubyVisibility();
            foreach (var model in _ruby)
            {
                foreach (var item in model.VisibilityStates.Keys)
                    if (preserveVisibility) _retiredRubyVisibility[item] = model.Host;
                    else CssRubyVisibility.Clear(item, model.Host);
                foreach (var item in model.MetricStates.Keys)
                    if (preserveVisibility) _retiredRubyMetrics[item] = model.Host;
                    else CssRubyBoxMetrics.Clear(item, model.Host);
                foreach (var cell in model.Cells)
                    if (ReferenceEquals(cell.VisualParent, model.Host)) model.Host.DetachCssGeneratedChild(cell);
                foreach (var text in model.Items.OfType<TextBlock>()) { text.SetRubyJustification(false); text._cssRubyAnonymousContent = false; }
                if (s_rubyOwners.TryGetValue(model.Host, out var current) && ReferenceEquals(current, this)) s_rubyOwners.Remove(model.Host);
            }
            _ruby.Clear();
        }

        private void ActivateRubyHost(FrameworkElement host)
        {
            if (s_rubyOwners.TryGetValue(host, out var previous))
            {
                if (ReferenceEquals(previous, this)) return;
                SuspendRubyHost(host);
            }
            s_rubyOwners.Add(host, this);
            foreach (var model in _ruby.Where(model => ReferenceEquals(model.Host, host)))
            {
                foreach (var (item, state) in model.VisibilityStates)
                    CssRubyVisibility.Set(item, host, state.Hidden, state.Ignore, state.Root);
                foreach (var (item, fontArea) in model.MetricStates) CssRubyBoxMetrics.Set(item, host, fontArea);
            }
        }

        internal static bool IsAllocatedRubyContainer(FrameworkElement element)
        {
            if (element.CssDisplayMode is not (CssDisplayMode.RubyBaseContainer or CssDisplayMode.RubyTextContainer)) return false;
            for (var current = element.VisualParent; current is not null; current = current.VisualParent)
                if (current is FrameworkElement host && s_rubyOwners.TryGetValue(host, out var layout) &&
                    layout._ruby.Any(model => ReferenceEquals(model.Host, host) && model.Containers.Contains(element))) return true;
            return false;
        }

        internal static void SuspendRubyHost(FrameworkElement host)
        {
            if (!s_rubyOwners.TryGetValue(host, out var previous)) return;
            foreach (var model in previous._ruby.Where(model => ReferenceEquals(model.Host, host)))
            {
                foreach (var item in model.VisibilityStates.Keys) CssRubyVisibility.Clear(item, host);
                foreach (var item in model.MetricStates.Keys) CssRubyBoxMetrics.Clear(item, host);
                foreach (var cell in model.Cells)
                    if (ReferenceEquals(cell.VisualParent, host)) host.DetachCssGeneratedChild(cell);
                foreach (var text in model.Items.OfType<TextBlock>()) { text.SetRubyJustification(false); text._cssRubyAnonymousContent = false; }
            }
            s_rubyOwners.Remove(host);
        }

        internal void ReleaseRubyFormatting()
        {
            ClearRuby();
            foreach (var frame in _frames) frame.Element.ReleaseCssInlineFrame(owner);
            _allocated.Clear(); _sources.Clear(); _frames.Clear();
        }

        private void AppendElements(IEnumerable<UIElement> input, List<CssInlineUnit> pending,
            ref bool collapsedSpace, CssInlineFrame? parent = null)
        {
            var items = input.ToArray();
            for (int i = 0; i < items.Length;)
            {
                if (!CssRubyRoles.IsPart(CssRubyRoles.Role(items[i])))
                { AppendElement(items[i++], pending, ref collapsedSpace, parent); continue; }
                int first = i++;
                while (i < items.Length && CssRubyRoles.IsPart(CssRubyRoles.Role(items[i]))) i++;
                // Missing ruby container: pair the consecutive internal boxes
                // in this formatting context, without changing their parents.
                AppendRuby(parent?.Element ?? owner, items[first..i], pending, ref collapsedSpace, parent, root: true);
            }
        }

        private void AppendRuby(FrameworkElement host, UIElement[] children, List<CssInlineUnit> pending,
            ref bool collapsedSpace, CssInlineFrame? parent, bool root = false)
        {
            host.PrepareCssContent();
            ActivateRubyHost(host);
            CssInlineFrame? frame = root ? parent : new(host, parent,
                Axes.LineLogical(CssBoxMetrics.ContentInsets(host, _containing.Width)),
                Axes.LineLogical(CssBoxMetrics.Margin(host, _containing.Width)));
            if (!root) _frames.Add(frame!);
            var model = new RubyModel(host, Axes, frame); _ruby.Add(model);
            bool rubySpace = false;
            var bases = new List<RubyGroup>(); var levels = new List<List<RubyGroup>>();
            bool annotations = false;
            foreach (var child in children.Where(child => child.Visibility != Visibility.Collapsed && child is not FrameworkElement { CssLayout.IsOutOfFlow: true }))
            {
                var role = CssRubyRoles.Role(child);
                if (role is CssDisplayMode.RubyText or CssDisplayMode.RubyTextContainer)
                {
                    annotations = true;
                    if (role == CssDisplayMode.RubyTextContainer && child is FrameworkElement container)
                    {
                        model.Containers.Add(container);
                        levels.Add(Groups(container, CssDisplayMode.RubyText));
                    }
                    else
                    {
                        if (levels.Count == 0) levels.Add([]);
                        levels[0].Add(new(child as FrameworkElement ?? host, [child], host));
                    }
                }
                else
                {
                    if (annotations) { EmitSegment(); bases.Clear(); levels.Clear(); annotations = false; }
                    if (role == CssDisplayMode.RubyBaseContainer && child is FrameworkElement container)
                    { model.Containers.Add(container); bases.AddRange(Groups(container, CssDisplayMode.RubyBase)); }
                    else bases.Add(new(child as FrameworkElement ?? host, [child]));
                }
            }
            EmitSegment();
            if (!root) frame!.LastUnit = pending.Count - 1;
            collapsedSpace = false;

            List<RubyGroup> Groups(FrameworkElement container, CssDisplayMode expected)
            {
                var result = new List<RubyGroup>(); var anonymous = new List<UIElement>();
                void Flush() { if (anonymous.Count == 0) return; result.Add(new(container, anonymous.ToArray(), expected == CssDisplayMode.RubyText ? container : null,
                    expected == CssDisplayMode.RubyBase ? container : null)); anonymous.Clear(); }
                foreach (var item in new CssPanelChildren(container))
                {
                    if (item.Visibility == Visibility.Collapsed || item is FrameworkElement { CssLayout.IsOutOfFlow: true }) continue;
                    if (CssRubyRoles.Role(item) == expected) { Flush(); result.Add(new(item as FrameworkElement ?? container, [item], expected == CssDisplayMode.RubyText ? container : null,
                        expected == CssDisplayMode.RubyBase ? container : null)); }
                    else anonymous.Add(item);
                }
                Flush(); return result;
            }

            void EmitSegment()
            {
                int count = Math.Max(bases.Count, levels.Select(level => level.Count).DefaultIfEmpty().Max());
                if (count == 0) return;
                while (bases.Count < count) bases.Add(new(host, []));
                foreach (var group in bases.Concat(levels.SelectMany(level => level)))
                {
                    group.InterCharacter = !Axes.Vertical && group.AnnotationContainer is { } annotation &&
                        CssWritingModeProperties.GetRubyPosition(annotation) == CssRubyPosition.InterCharacter;
                    foreach (var item in group.Items)
                    {
                        bool fontArea = !group.InterCharacter && CssRubyBoxMetrics.Eligible(item);
                        model.MetricStates[item] = fontArea; _retiredRubyMetrics.Remove(item);
                        CssRubyBoxMetrics.Set(item, host, fontArea);
                    }
                }
                var allNotes = new List<RubyAnnotation>();
                for (int level = 0; level < levels.Count; level++)
                {
                    var row = levels[level];
                    for (int i = 0; i < row.Count; i++)
                    {
                        var group = row[i];
                        int last = i == row.Count - 1 ? count - 1 : i;
                        bool merge = !group.InterCharacter && CssWritingModeProperties.GetRubyMerge(group.AnnotationContainer ?? group.Style) == CssRubyMerge.Merge;
                        bool hidden = !merge && (CssDisplayProperties.ComputedVisibility(CssNode.Get(group.Style)) == CssVisibilityMode.Collapse ||
                            RubyTextContent(group.Items) == RubyTextContent(bases.Skip(i).Take(last-i+1).SelectMany(b => b.Items)));
                        foreach (var item in group.Items)
                        {
                            var root = CssRubyRoles.Role(item) == CssDisplayMode.RubyText ? item : group.AnnotationContainer ?? group.Style;
                            model.VisibilityStates[item] = (hidden, merge, root);
                            _retiredRubyVisibility.Remove(item);
                            CssRubyVisibility.Set(item, host, hidden, merge, root);
                        }
                        allNotes.Add(new(group, i, last, level, hidden));
                    }
                }
                foreach (var group in bases.Concat(allNotes.Where(note => !note.Hidden).Select(note => note.Group))) group.Measure(this);
                foreach (var item in bases.Concat(levels.SelectMany(level => level)).SelectMany(group => group.Items)) model.Items.Add(item);
                // Every annotation level shares a line-local baseline, including
                // separate annotations with different fonts. The same measured
                // segment model also retains merge/auto and side-note pairing.
                EmitRubyCells(model, bases.ToArray(), allNotes.ToArray(), pending, frame, ref rubySpace);
            }
        }

        private (double Height, double Baseline) RubyLineBounds(FrameworkElement host, IEnumerable<RubyGroup> groups,
            double baseHeight, double before, double after, double sideAscent, double sideDescent)
        {
            var strut = CssFlowProperties.LineMetrics(host, rubyFontArea: false);
            double above = RubyMetricBaseline(strut.Height, strut.Baseline), below = strut.Height - above;
            foreach (var group in groups)
            {
                above = Math.Max(above, group.LineBaseline);
                below = Math.Max(below, group.LineHeight - group.LineBaseline);
            }
            above = Math.Max(above, sideAscent); below = Math.Max(below, sideDescent);
            double height = Math.Max(0, above + below);
            double extra = before + after > 0 ? Math.Max(0, before + baseHeight + after - height) : 0;
            double leadingBefore = before + after > 0 ? extra * before / (before + after) : 0;
            return (height + extra, above + leadingBefore);
        }

        private double RubyMetricBaseline(double height, double baseline) =>
            WritingMode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
            CssWritingModeProperties.GetTextOrientation(owner) != CssTextOrientation.Sideways ? height / 2 : baseline;

        private void ArrangeRuby()
        {
            foreach (var model in _ruby)
            {
                ActivateRubyHost(model.Host);
                foreach (var cell in model.Cells)
                    if (!ReferenceEquals(cell.VisualParent, model.Host)) model.Host.AttachCssGeneratedChild(cell);
                var placements = new Dictionary<UIElement, Rect>();
                foreach (var cell in model.Cells)
                {
                    if (cell.Source is not { Fragments.Count: > 0 }) continue;
                    if (!model.Host.GetRenderMatrix().TryInvert(out var inverse)) continue;
                    var placement = Matrix.Multiply(cell.GetRenderMatrix(), inverse);
                    var origin = placement.Transform(new Point(0, 0));
                    foreach (var track in cell.Tracks)
                    foreach (var (element, rect) in track.Group.Place(track.Area))
                    {
                        if (element is TextBlock text)
                            text._cssRubyAnonymousContent = track.Group.InterCharacter && !CssRubyRoles.IsPart(text.CssDisplayMode);
                        var physical = model.Axes.Physical(model.Axes.LinePlacement(rect, 0, cell.LogicalSize.Height), cell.RenderSize);
                        placements[element] = new(physical.X + origin.X, physical.Y + origin.Y, physical.Width, physical.Height);
                    }
                }
                ArrangeMergedRuby(model, placements);
                var containers = model.Containers.OrderByDescending(Depth).ToArray();
                foreach (var container in containers)
                {
                    var union = Rect.Empty;
                    foreach (var item in placements.Where(item => ReferenceEquals(item.Key.VisualParent, container))) union.Union(item.Value);
                    placements[container] = union.IsEmpty ? new(0, 0, 0, 0) : union;
                }
                foreach (var segment in model.SideSegments)
                {
                    var union = Rect.Empty;
                    foreach (var cell in segment.Cells)
                    {
                        if (cell.Source is not { Fragments.Count: > 0 } || !model.Host.GetRenderMatrix().TryInvert(out var inverse)) continue;
                        var origin = Matrix.Multiply(cell.GetRenderMatrix(), inverse).Transform(new Point(0, 0));
                        var area = cell.BaseLevelArea; area.Offset(origin.X, origin.Y); union.Union(area);
                    }
                    foreach (var container in segment.Containers) placements[container] = union.IsEmpty ? new(0,0,0,0) : union;
                }
                foreach (var element in containers.Reverse().Cast<UIElement>().Concat(model.Items))
                {
                    var rect = placements.GetValueOrDefault(element, new(0, 0, 0, 0));
                    if (element.VisualParent is UIElement parent && placements.TryGetValue(parent, out var parentRect))
                        rect = new(rect.X - parentRect.X, rect.Y - parentRect.Y, rect.Width, rect.Height);
                    new CssLayoutBox(element, 0).ArrangeBorderBox(rect, model.Host.RenderSize, vertical: CssBoxAlignment.Normal,
                        containingWritingMode: WritingMode);
                    // Intrinsic probing can retain a bidi paragraph before
                    // the distributed width is allocated. Refresh its native
                    // paint/range cache after the final physical slot exists.
                    if (element is TextBlock { _cssRubyJustify: true } text) text.ClearNativeBidiLines();
                }
            }
            static int Depth(FrameworkElement element) { int result = 0; for (var current = element.VisualParent; current is not null; current = current.VisualParent) result++; return result; }
        }
    }
}
