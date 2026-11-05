using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Table formatting over existing native controls and private anonymous boxes.</summary>
internal sealed partial class CssTableLayout
{
    private readonly FrameworkElement owner;
    internal CssTableLayout(FrameworkElement owner) => this.owner = owner;
    private sealed class Allocation(CssTableLayout layout, FrameworkElement element, bool cell)
    {
        internal WeakReference<CssTableLayout> Layout = new(layout);
        internal WeakReference<Visual>? Parent = element.VisualParent is { } parent ? new(parent) : null;
        internal bool Cell = cell, Caption, Empty, Measuring, DefiniteHeight, CollapsedBorder, RemovedTrack, ClipTracks;
        internal Size ContentSize;
        internal Thickness Border;
        internal Rect Bounds;
        internal Rect[] Regions = [];
        internal double Offset, Remaining;
    }
    private static readonly ConditionalWeakTable<FrameworkElement, Allocation> s_allocations = new();
    private static readonly ConditionalWeakTable<FrameworkElement, List<CssTableAnonymousBox>> s_flowTables = new();
    private readonly HashSet<FrameworkElement> _managed = [];
    private Model? _model;
    private int _computing;
    private readonly Dictionary<FrameworkElement, Action<DependencyProperty, object?, object?>> _groupHeightObservers = [];
    internal double Baseline => _model?.Baseline ?? double.NaN;
    private FrameworkElement StyleOwner => owner is CssTableAnonymousBox box ? box.Owner : owner;
    private Point Origin => owner is CssTableAnonymousBox box ? box.AllocationOrigin : default;

    private static Allocation? Assigned(FrameworkElement element)
    {
        if (!s_allocations.TryGetValue(element, out var state)) return null;
        var sameParent = state.Parent is null ? element.VisualParent is null :
            state.Parent.TryGetTarget(out var parent) && ReferenceEquals(element.VisualParent, parent);
        if (sameParent && state.Layout.TryGetTarget(out _)) return state;
        s_allocations.Remove(element); return null;
    }
    internal static bool IsManaged(FrameworkElement element) => Assigned(element) is { Caption: false };
    internal static CssTableTracks.Cell? CellTrack(FrameworkElement element) =>
        Assigned(element) is { Cell: true } state && state.Layout.TryGetTarget(out var layout)
            ? layout._model?.Cells.FirstOrDefault(cell => ReferenceEquals(cell.Element, element))?.Track : null;
    internal static bool InCell(FrameworkElement element)
    {
        for (var parent = element.VisualParent as FrameworkElement; parent is not null; parent = parent.VisualParent as FrameworkElement)
            if (Assigned(parent) is { Cell: true }) return true;
        return false;
    }
    internal static bool IsStructural(FrameworkElement element) => Assigned(element) is { Cell: false, Caption: false };
    internal static bool SuppressFrame(Visual source) => source is FrameworkElement element &&
        (Assigned(element) is { Caption: false } state && (!state.Cell || state.Empty) || DecorationBox(element) is not null);
    internal static Thickness ContentInsets(FrameworkElement element, Thickness natural)
        => Assigned(element) is { Cell: true, Measuring: false } state
            ? new(natural.Left, natural.Top + state.Offset, natural.Right, natural.Bottom + state.Remaining) : natural;
    internal static bool TryCellDefiniteHeight(FrameworkElement element, out bool definite)
    {
        if (Assigned(element) is { Cell: true } state) { definite = state.DefiniteHeight; return true; }
        definite = false; return false;
    }
    internal static bool TryCellContainingHeight(FrameworkElement element, out double height)
    {
        if (Assigned(element) is { Cell: true, DefiniteHeight: true } state)
        {
            var insets = CssBoxMetrics.ContentInsets(element, element.CssLayout?.ContainingWidthCache ?? state.ContentSize.Width);
            height = Math.Max(0, state.ContentSize.Height - insets.Top - insets.Bottom);
            return true;
        }
        height = 0; return false;
    }

    internal static Size MeasureCell(FrameworkElement element, Size available)
    {
        var assigned = Assigned(element); var old = assigned?.Measuring ?? false;
        if (assigned is not null) assigned.Measuring = true;
        try
        {
            if (CssMultiColumnProperties.Active(element)) return CssMultiColumnLayout.For(element).Measure(available);
            return CssLineClampProperties.Active(element) ? CssLineClampLayout.For(element).Measure(available) : CssDisplayLayout.FlowFor(element).Measure(available);
        }
        finally { if (assigned is not null) assigned.Measuring = old; }
    }
    internal static bool TryMeasureAssigned(FrameworkElement element, Size available, out Size size)
    {
        if (Assigned(element) is not { Caption: false } state) { size = default; return false; }
        if (state.Cell)
        {
            if (!element.IsCssContentHost) { size = default; return false; }
            element.PrepareCssContent(); size = MeasureCell(element, (state.ClipTracks || state.DefiniteHeight) && !state.Measuring ? state.ContentSize : available); return true;
        }
        element.MeasureCssLayoutAbsoluteChildren(state.Bounds.Size); size = state.Bounds.Size; return true;
    }
    internal static bool TryArrangeAssigned(FrameworkElement element, Size available, out Size size)
    {
        if (Assigned(element) is not { Caption: false } state) { size = default; return false; }
        if (state.Cell)
        {
            if (!element.IsCssContentHost) { size = default; return false; }
            var slotSize = state.ClipTracks || state.DefiniteHeight ? state.ContentSize : available;
            if (CssMultiColumnProperties.Active(element)) CssMultiColumnLayout.For(element).Arrange(slotSize);
            else if (CssLineClampProperties.Active(element)) CssLineClampLayout.For(element).Arrange(slotSize);
            else CssDisplayLayout.FlowFor(element).Arrange(slotSize);
            size = available; return true;
        }
        if (state.Layout.TryGetTarget(out var layout)) layout.ArrangeChildren(element);
        element.ArrangeCssLayoutAbsoluteChildren(available); size = available; return true;
    }

    internal void Clear()
    {
        foreach (var element in _managed)
            if (Assigned(element)?.Layout.TryGetTarget(out var layout) == true && ReferenceEquals(layout, this))
            { s_allocations.Remove(element); element.InvalidateMeasure(); element.InvalidateVisual(); element.UpdateIsVisibleFromTree(forceDescendants: true); }
        _managed.Clear(); _model = null;
        foreach (var box in _anonymous.ToArray()) box.Detach();
        _anonymous.Clear();
        ObserveColumns(null);
        ObserveCaptions(null);
        ObserveBorders(null);
        ObserveGroupHeights(null);
    }
    internal static void ReleaseFlowTables(FrameworkElement parent)
    {
        if (!s_flowTables.TryGetValue(parent, out var wrappers)) return;
        foreach (var box in wrappers.ToArray()) box.Detach();
        wrappers.Clear();
    }
    internal static IEnumerable<CssTableAnonymousBox> EnumerateFlowTables(FrameworkElement root)
    {
        void VisitTable(CssTableLayout table, Queue<CssTableAnonymousBox> queue)
        {
            foreach (var cell in table._anonymous)
                if (s_flowTables.TryGetValue(cell, out var nested))
                    foreach (var box in nested) queue.Enqueue(box);
        }
        var pending = new Queue<CssTableAnonymousBox>();
        if (s_flowTables.TryGetValue(root, out var direct))
            foreach (var box in direct) pending.Enqueue(box);
        if (CssDisplayLayout.TryTableFor(root, out var rootTable) && rootTable is not null)
            VisitTable(rootTable, pending);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            yield return current;
            if (CssDisplayLayout.TryTableFor(current, out var currentTable) && currentTable is not null)
                VisitTable(currentTable, pending);
        }
    }
    internal static void DisplayChanged(FrameworkElement element)
    {
        CssDisplayLayout.TableFor(element).Clear();
        if (Assigned(element)?.Layout.TryGetTarget(out var layout) == true) { layout.Clear(); layout.InvalidateOwner(); }
        ReleaseFlowTables(element);
        for (var ancestor = element.VisualParent as FrameworkElement; ancestor is not null; ancestor = ancestor.VisualParent as FrameworkElement)
        {
            if (Assigned(ancestor)?.Layout.TryGetTarget(out var ancestorLayout) == true) { ancestorLayout.Clear(); ancestorLayout.InvalidateOwner(); }
            if (ancestor.CssDisplayMode == CssDisplayMode.Table && CssDisplayLayout.TryTableFor(ancestor, out var tableLayout) && tableLayout is not null)
            { tableLayout.Clear(); tableLayout.InvalidateOwner(); }
            if (s_flowTables.TryGetValue(ancestor, out var wrappers) && wrappers.Count > 0)
            {
                foreach (var box in wrappers.ToArray()) box.Detach();
                wrappers.Clear();
                ancestor.InvalidateMeasure();
                ancestor.InvalidateVisual();
            }
        }
        if (CssDisplayProperties.HasVisibilityDeclarations || IsInternal(element.CssDisplayMode)) CssDisplayProperties.RefreshVisibility(element);
    }

    private Allocation Assign(FrameworkElement element, bool cell, Rect bounds, Rect[]? regions = null)
    {
        var state = Assigned(element);
        if (state is null || !state.Layout.TryGetTarget(out var previous) || !ReferenceEquals(previous, this) || state.Cell != cell)
        {
            s_allocations.Remove(element); state = new(this, element, cell); s_allocations.Add(element, state); element.InvalidateCssAllocation();
        }
        if (state.Bounds != bounds || regions is not null && !state.Regions.SequenceEqual(regions)) element.InvalidateVisual();
        state.Bounds = bounds; if (regions is not null) state.Regions = regions; _managed.Add(element); return state;
    }

    internal Size Measure(Size available)
        => Measure(available, owner.CssParentBox?.ContainingBlock ?? owner.CssNativeBox?.ContainingBlock ?? available);

    internal Size Measure(Size available, Size containingBlock)
    {
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? available.Width);
        var inner = CssBoxMetrics.InnerSize(available, insets);
        _model = Compute(inner, insets, containingBlock, arrange: false);
        owner.MeasureCssLayoutAbsoluteChildren(available);
        return new(_model.Desired.Width + _model.RemovedSize.Width + insets.Left + insets.Right,
            _model.Desired.Height + _model.RemovedSize.Height + insets.Top + insets.Bottom);
    }
    internal Size Arrange(Size finalSize)
        => Arrange(finalSize, owner.CssParentBox?.ContainingBlock ?? owner.CssNativeBox?.ContainingBlock ?? finalSize);

    internal Size Arrange(Size finalSize, Size containingBlock)
    {
        if (_model is { Visibility.Any: true } measured)
            finalSize = new(Math.Max(finalSize.Width, measured.Desired.Width + measured.RemovedSize.Width + measured.Insets.Left + measured.Insets.Right),
                Math.Max(finalSize.Height, measured.Desired.Height + measured.RemovedSize.Height + measured.Insets.Top + measured.Insets.Bottom));
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? finalSize.Width);
        _model = Compute(CssBoxMetrics.InnerSize(finalSize, insets), insets, containingBlock, arrange: true);
        if (_model.Captions.Count > 0 || _model.Borders is not null)
        {
            var grid = _model.GridBounds;
            var overflow = _model.Borders?.Overflow ?? default;
            var effectPadding = owner.Effect is { HasEffect: true } eff ? eff.EffectPadding : default;
            var outlineReach = owner.OutlineBrush is not null && owner.OutlineStyle != OutlineStyle.None &&
                (owner.OutlineStyle == OutlineStyle.Auto || owner.OutlineThickness > 0)
                ? Math.Max(0, owner.OutlineOffset + (owner.OutlineStyle == OutlineStyle.Auto ? 3 : owner.OutlineThickness)) : 0;
            var padLeft = Math.Max(overflow.Left, effectPadding.Left + outlineReach);
            var padTop = Math.Max(overflow.Top, effectPadding.Top + outlineReach);
            var padRight = Math.Max(overflow.Right, effectPadding.Right + outlineReach);
            var padBottom = Math.Max(overflow.Bottom, effectPadding.Bottom + outlineReach);
            var extra = Math.Max(Math.Max(padLeft - grid.Left, padTop - grid.Top), Math.Max(grid.Right + padRight - finalSize.Width, grid.Bottom + padBottom - finalSize.Height));
            owner.CssTableInkPadding = Math.Max(owner.CssTableInkPadding, (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, extra))));
        }
        var used = new Size(_model.Desired.Width + insets.Left + insets.Right,
            _model.Desired.Height + insets.Top + insets.Bottom);
        ArrangeChildren(owner); owner.ArrangeCssLayoutAbsoluteChildren(used); return used;
    }

    private Model Compute(Size available, Thickness insets, Size containingBlock, bool arrange)
    {
        _computing++;
        try { return ComputeCore(available, insets, containingBlock, arrange); }
        finally { _computing--; }
    }

    private Model ComputeCore(Size available, Thickness insets, Size containingBlock, bool arrange)
    {
        var oldManaged = _managed.ToArray(); _managed.Clear();
        var model = TrackModel(out var tracks, out var count);
        model.Visibility = TrackVisibility(model, count);
        model.Insets = insets;
        _activeBorders = model.Borders;
        ObserveColumns(model);
        ObserveCaptions(model);
        ObserveBorders(model);
        ObserveGroupHeights(model);
        var captionMinimum = CaptionMinimum(model, available.Width + insets.Left + insets.Right);
        var spacing = model.Borders is null ? (Size)StyleOwner.GetValue(CssTableProperties.SpacingProperty)! : default;
        var rowless = model.Rows.Count == 0;
        double Assignable(double basis) => double.IsFinite(basis) ? Math.Max(0, basis - spacing.Width * (count + 1)) : basis;
        var byElement = model.Cells.ToDictionary(cell => cell.Element);
        var parentWidth = containingBlock.Width;
        foreach (var track in tracks)
        {
            var cell = byElement[(FrameworkElement)track.Source]; cell.Track = track;
            var state = Assign(cell.Element, true, default); state.Measuring = true;
            SetCollapsedBorder(cell.Element, state, model.Borders?.CellInsets(track));
            cell.Box.MeasureIntrinsicWidth();
            var bounds = cell.Element.CssWidthBounds(parentWidth);
            track.Min = Math.Max(bounds.Min, cell.Box.MinContent.Width); track.Max = Math.Max(track.Min, cell.Box.MaxContent.Width);
            var cellInsets = CssBoxMetrics.ContentInsets(cell.Element, double.NaN);
            track.SpecifiedWidth = cell.Anonymous ? default : CssTableTracks.WidthOf(cell.Element, cellInsets.Left + cellInsets.Right);
            track.Preferred = cell.Anonymous || track.SpecifiedWidth.Percentage ? double.NaN : cell.Element.CssPreferredWidth(Assignable(available.Width));
        }
        CssTableTracks.Column[] Definitions(double basis)
        {
            basis = Assignable(basis);
            var result = new CssTableTracks.Column[count]; Array.Fill(result, new(double.NaN));
            foreach (var column in model.Columns)
            {
                var width = column.Source.CssPreferredWidth(basis);
                var groupWidth = column.Group?.CssPreferredWidth(basis) ?? double.NaN;
                var bounds = column.Source.CssWidthBounds(basis);
                if (column.Group is { } groupBoundsOwner)
                {
                    var groupBounds = groupBoundsOwner.CssWidthBounds(basis);
                    bounds = (Math.Max(bounds.Min, groupBounds.Min), Math.Max(Math.Max(bounds.Min, groupBounds.Min), Math.Min(bounds.Max, groupBounds.Max)));
                }
                var request = CssTableTracks.WidthOf(column.Source);
                if (!request.Specified && column.Group is { } group)
                {
                    var groupRequest = CssTableTracks.WidthOf(group);
                    if (groupRequest.Specified) request = groupRequest;
                }
                if (!double.IsFinite(width) && double.IsFinite(groupWidth)) width = groupWidth;
                for (var i = column.First; i < column.First + column.Count; i++)
                    result[i] = new(double.IsFinite(width) ? Math.Max(0, width) : double.NaN, Request: request, Min: bounds.Min, Max: bounds.Max);
            }
            return result;
        }
        var preferred = owner is CssTableAnonymousBox ? double.NaN : owner.CssPreferredWidth(parentWidth);
        var fixedMode = double.IsFinite(preferred) && owner is not CssTableAnonymousBox && (CssTableLayoutMode)owner.GetValue(CssTableProperties.LayoutProperty)! == CssTableLayoutMode.Fixed;
        var width = available.Width;
        if (!double.IsFinite(preferred))
        {
            var definitions = rowless ? [] : Definitions(available.Width);
            var (min, max) = CssTableTracks.IntrinsicColumns(rowless ? 0 : count, spacing.Width, definitions, tracks, double.IsFinite(width));
            width = double.IsFinite(width) ? Math.Max(min, Math.Min(max, width)) : max;
            if (owner is not CssTableAnonymousBox)
            {
                var cached = owner.CssLayout?.ContainingWidthCache ?? double.NaN;
                var basis = owner.CssParentBox?.ContainingBlock.Width ?? (!double.IsNaN(cached) ? cached : available.Width);
                var bounds = owner.CssWidthBounds(basis); var chrome = insets.Left + insets.Right;
                var lower = Math.Max(0, bounds.Min - chrome); var upper = Math.Max(lower, bounds.Max - chrome);
                width = Math.Max(min, Math.Clamp(width, lower, upper));
            }
        }
        width = Math.Max(width, Math.Max(0, captionMinimum - insets.Left - insets.Right));
        foreach (var cell in model.Cells)
        {
            var cellInsets = CssBoxMetrics.ContentInsets(cell.Element, width);
            cell.Track.SpecifiedWidth = cell.Anonymous ? default : CssTableTracks.WidthOf(cell.Element, cellInsets.Left + cellInsets.Right);
            cell.Track.Preferred = cell.Anonymous || cell.Track.SpecifiedWidth.Percentage ? double.NaN : cell.Element.CssPreferredWidth(Assignable(width));
        }
        // Without rows there are no grid slots to size. Keep declared columns
        // observable for later insertions, but do not let their widths or gaps
        // enlarge an empty table's specified box or intrinsic contribution.
        model.FullHorizontal = rowless ? new(new double[count], new double[count], 0)
            : CssTableTracks.Columns(count, width, spacing.Width, fixedMode, Definitions(width), tracks);
        model.Horizontal = rowless ? model.FullHorizontal : CssTableVisibility.Project(model.FullHorizontal, model.Visibility.Columns, spacing.Width);
        var outerWidth = Math.Max(model.Horizontal.Extent, (rowless || count == 0) && double.IsFinite(width) ? width : 0) + insets.Left + insets.Right;
        var topCaptions = MeasureCaptions(model, outerWidth);
        foreach (var cell in model.Cells)
        {
            var track = cell.Track; var state = Assign(cell.Element, true, default);
            if (state.DefiniteHeight) { state.DefiniteHeight = false; cell.Element.InvalidateCssAllocation(); }
            state.Measuring = true;
            cell.Box.MeasureBorderBox(new(model.FullHorizontal.Span(track.Column, track.ColumnSpan), double.PositiveInfinity), new(model.FullHorizontal.Extent, double.PositiveInfinity));
            cell.ContentHeight = cell.Element.DesiredSize.Height;
            track.Height = Math.Max(cell.ContentHeight, cell.Element.CssPreferredHeight(double.PositiveInfinity) is var h && double.IsFinite(h) ? h : 0);
            track.Height = Math.Max(track.Height, cell.Element.CssHeightBounds(double.PositiveInfinity).Min);
            var hasBaseline = CssDisplayLayout.TryGetBaseline(cell.Element, cell.Element.DesiredSize.Height, false, out var b);
            var baseline = hasBaseline ? b : cell.Element.DesiredSize.Height;
            track.Baseline = double.IsFinite(baseline) ? baseline : track.Height;
            cell.Empty = model.Borders is null && !cell.Anonymous && IsEmpty(cell.Element) && !(bool)cell.Element.GetValue(CssTableProperties.EmptyCellsProperty)!;
            state.Empty = cell.Empty; if (cell.Empty) { track.Height = 0; track.Baseline = 0; cell.ContentHeight = 0; }
            track.HasBaseline = !cell.Empty && hasBaseline && Alignment(cell) == CssInlineAlignment.Baseline;
            var cellInsets = CssBoxMetrics.ContentInsets(cell.Element, model.FullHorizontal.Extent);
            track.SpecifiedHeight = cell.Empty || cell.Anonymous ? default : CssTableTracks.HeightOf(cell.Element, cellInsets.Top + cellInsets.Bottom);
        }
        var baselines = new double[model.Rows.Count];
        foreach (var cell in model.Cells) if (!cell.Empty && Alignment(cell) == CssInlineAlignment.Baseline) baselines[cell.Track.Row] = Math.Max(baselines[cell.Track.Row], cell.Track.Baseline);
        foreach (var cell in model.Cells) if (!cell.Empty && Alignment(cell) == CssInlineAlignment.Baseline) cell.Track.OffsetY = Math.Max(0, baselines[cell.Track.Row] - cell.Track.Baseline);
        var coverage = new int[model.Rows.Count + 1];
        foreach (var cell in model.Cells.Where(cell => !cell.Empty)) { coverage[cell.Track.Row]++; coverage[cell.Track.Row + cell.Track.RowSpan]--; }
        var collapsed = new bool[model.Rows.Count]; var covering = 0;
        var missingRows = model.Missing.Select(slot => slot.Row).ToHashSet();
        bool ShowMissing(int row) => model.Borders is not null ||
            (bool)(model.Rows[row].Source ?? model.Rows[row].Group ?? StyleOwner).GetValue(CssTableProperties.EmptyCellsProperty)!;
        for (var row = 0; row < collapsed.Length; row++)
        {
            covering += coverage[row];
            collapsed[row] = covering == 0 && (model.Rows[row].Cells.Count > 0 || missingRows.Contains(row)) &&
                !(missingRows.Contains(row) && ShowMissing(row));
        }
        var minimums = model.Rows.Select(row => row.Source?.CssPreferredHeight(double.PositiveInfinity) ?? double.NaN).ToArray();
        var preferredHeight = owner is CssTableAnonymousBox ? double.NaN : owner.CssPreferredHeight(containingBlock.Height);
        var minHeight = preferredHeight;
        if (owner is not CssTableAnonymousBox)
        {
            var bounds = owner.CssHeightBounds(containingBlock.Height);
            if (double.IsFinite(minHeight)) minHeight = Math.Clamp(minHeight, bounds.Min, bounds.Max);
            if (bounds.Min > 0) minHeight = Math.Max(double.IsFinite(minHeight) ? minHeight : 0, bounds.Min);
        }
        if (double.IsFinite(minHeight)) minHeight = Math.Max(0, minHeight - insets.Top - insets.Bottom);
        if (arrange && (double.IsFinite(preferredHeight) || owner.CssTableHeightUsesAllocation(containingBlock.Height)))
            minHeight = Math.Max(double.IsFinite(minHeight) ? minHeight : 0, available.Height - model.CaptionHeight);
        var groups = new List<CssTableTracks.RowGroup>();
        for (var first = 0; first < model.Rows.Count;)
        {
            var group = model.Rows[first].Group; var end = first + 1;
            while (end < model.Rows.Count && ReferenceEquals(model.Rows[end].Group, group)) end++;
            var body = group is null || !ReferenceEquals(group, model.Header) && !ReferenceEquals(group, model.Footer);
            groups.Add(new(first, end - first, group is null ? default : CssTableTracks.HeightOf(group), body)); first = end;
        }
        model.FullVertical = CssTableTracks.Rows(model.Rows.Count, spacing.Height, minimums, tracks, collapsed, minHeight,
            model.Rows.Select(row => row.Source is { } source ? CssTableTracks.HeightOf(source) : default).ToArray(), groups);
        model.Vertical = CssTableVisibility.Project(model.FullVertical, model.Visibility.Rows, spacing.Height);
        model.RemovedSize = new(Math.Max(0, model.FullHorizontal.Extent - model.Horizontal.Extent), Math.Max(0, model.FullVertical.Extent - model.Vertical.Extent));
        var rtl = StyleOwner.FlowDirection == FlowDirection.RightToLeft;
        var trackWidth = rowless || count == 0 ? Math.Max(0, outerWidth - insets.Left - insets.Right) : model.Horizontal.Extent;
        var tableDefiniteHeight = ExplicitCellHeight(owner, true) || double.IsFinite(minHeight) && owner.CssTableHeightUsesAllocation(containingBlock.Height);
        foreach (var cell in model.Cells)
        {
            var track = cell.Track; var w = model.Horizontal.Span(track.Column, track.ColumnSpan); var h = model.Vertical.Span(track.Row, track.RowSpan);
            cell.Bounds = new(insets.Left + (rtl ? model.Horizontal.Extent - model.Horizontal.Starts[track.Column] - w : model.Horizontal.Starts[track.Column]),
                topCaptions + insets.Top + model.Vertical.Starts[track.Row], w, h); model.Slots[cell.Element] = cell.Bounds;
            var state = Assign(cell.Element, true, cell.Bounds);
            var contentSize = new Size(model.FullHorizontal.Span(track.Column, track.ColumnSpan), model.FullVertical.Span(track.Row, track.RowSpan));
            var definiteHeight = !cell.Empty && (ExplicitCellHeight(cell.Element, false) || tableDefiniteHeight);
            state.DefiniteHeight = definiteHeight;
            state.ContentSize = contentSize;
            if (definiteHeight)
            {
                state.Measuring = true;
                cell.Element.InvalidateCssAllocation();
                cell.Box.MeasureBorderBox(contentSize, new(model.FullHorizontal.Extent, contentSize.Height));
                cell.ContentHeight = cell.Element.DesiredSize.Height;
            }
            state.Measuring = false;
            AssignVisibility(cell.Element, state, model.Visibility.Removed(track), model.Visibility.Intersects(track), contentSize);
            var spare = Math.Max(0, contentSize.Height - cell.ContentHeight);
            state.Offset = Alignment(cell) switch { CssInlineAlignment.Bottom => spare, CssInlineAlignment.Middle or CssInlineAlignment.Center => spare / 2, _ => track.OffsetY };
            state.Remaining = Math.Max(0, spare - state.Offset); cell.Element.InvalidateArrange();
        }
        foreach (var slot in model.Missing)
        {
            var source = model.Rows[slot.Row].Source ?? model.Rows[slot.Row].Group ?? StyleOwner;
            if (!ShowMissing(slot.Row) || model.Visibility.Rows[slot.Row] || !CssTableVisibility.Visible(source)) continue;
            for (var column = slot.Column; column < slot.Column + slot.ColumnSpan; column++)
            {
                if (model.Visibility.Columns[column]) continue;
                var x = insets.Left + (rtl ? model.Horizontal.Extent - model.Horizontal.Starts[column] - model.Horizontal.Sizes[column] : model.Horizontal.Starts[column]);
                var rect = new Rect(x, topCaptions + insets.Top + model.Vertical.Starts[slot.Row], model.Horizontal.Sizes[column], model.Vertical.Sizes[slot.Row]);
                model.MissingRegions.Add((slot.Row, column, rect));
            }
        }
        var missingByRow = model.MissingRegions.ToLookup(region => region.Row);
        for (var row = 0; row < model.Rows.Count; row++)
        {
            var entry = model.Rows[row]; entry.Bounds = new(insets.Left + spacing.Width, topCaptions + insets.Top + model.Vertical.Starts[row], Math.Max(0, trackWidth - 2 * spacing.Width), model.Vertical.Sizes[row]);
            if (entry.Source is not { } element || ReferenceEquals(element, owner)) continue;
            model.Slots[element] = entry.Bounds; var regions = entry.Cells.Where(cell => !cell.Empty && !model.Visibility.Removed(cell.Track)).Select(cell => cell.Bounds)
                .Concat(missingByRow[row].Select(region => region.Bounds)).ToArray(); model.Regions[element] = regions;
            Assign(element, false, entry.Bounds, regions);
        }
        var gridHeight = Math.Max(model.Vertical.Extent, double.IsFinite(minHeight) ? Math.Max(0, minHeight - model.RemovedSize.Height) : 0) + insets.Top + insets.Bottom;
        foreach (var (group, first) in model.GroupStarts)
        {
            if (ReferenceEquals(group, owner)) continue;
            var rows = model.Rows.Where(row => ReferenceEquals(row.Group, group)).ToArray();
            var emptyTop = rowless ? spacing.Height : first < model.Rows.Count ? model.Vertical.Starts[first] : ReferenceEquals(group, model.Header) ? spacing.Height
                : Math.Max(0, gridHeight - insets.Top - insets.Bottom - spacing.Height);
            var rect = rows.Length == 0 ? new Rect(insets.Left + spacing.Width, topCaptions + insets.Top + emptyTop, Math.Max(0, trackWidth - 2 * spacing.Width), 0)
                : new Rect(rows[0].Bounds.Left, rows[0].Bounds.Top, rows[0].Bounds.Width, rows[^1].Bounds.Bottom - rows[0].Bounds.Top);
            model.Slots[group] = rect; var regions = rows.SelectMany(row => row.Cells).Where(cell => !cell.Empty && !model.Visibility.Removed(cell.Track)).Select(cell => cell.Bounds)
                .Concat(model.MissingRegions.Where(region => ReferenceEquals(model.Rows[region.Row].Group, group)).Select(region => region.Bounds)).ToArray(); model.Regions[group] = regions; Assign(group, false, rect, regions);
        }
        model.GridBounds = new(0, topCaptions, outerWidth, gridHeight);
        PlaceCaptions(model);
        model.Desired = new(Math.Max(0, outerWidth - insets.Left - insets.Right), Math.Max(0, gridHeight + model.CaptionHeight - insets.Top - insets.Bottom));
        var firstVisible = Enumerable.Range(0, model.Rows.Count).FirstOrDefault(row => !model.Visibility.Rows[row], -1);
        if (firstVisible >= 0) model.Baseline = topCaptions + insets.Top + model.Vertical.Starts[firstVisible] +
            (model.Rows[firstVisible].Cells.Any(cell => !cell.Empty && Alignment(cell) == CssInlineAlignment.Baseline) ? baselines[firstVisible] : model.Vertical.Sizes[firstVisible]);
        else if (model.Captions.Count > 0) model.Baseline = topCaptions + gridHeight;
        foreach (var stale in oldManaged.Where(element => !_managed.Contains(element))) { s_allocations.Remove(stale); stale.InvalidateMeasure(); stale.InvalidateVisual(); stale.UpdateIsVisibleFromTree(forceDescendants: true); }
        foreach (var element in _managed.Where(element => Assigned(element) is { Cell: false, Caption: false }))
        { element.Measure(Assigned(element)!.Bounds.Size); element.InvalidateArrange(); }
        return model;
    }

    private static bool ExplicitCellHeight(FrameworkElement source, bool table)
    {
        if (source is CssTableAnonymousBox) return false;
        if (source.HasLocalOrAnimatedValue(FrameworkElement.HeightProperty))
            return double.IsFinite((double)source.GetValue(FrameworkElement.HeightProperty)!);
        if (source.CssLayout?.Height is { IsSet: true, IsAuto: false } height)
            return table || !height.IsPercent;
        return double.IsFinite((double)source.GetValue(FrameworkElement.HeightProperty)!);
    }

    private static CssInlineAlignment Alignment(CellBox cell)
    {
        if (cell.Anonymous) return CssInlineAlignment.Baseline;
        var value = ((CssInlineVerticalAlign)cell.Element.GetValue(CssFlowProperties.VerticalAlignProperty)!).Kind;
        return value is CssInlineAlignment.Top or CssInlineAlignment.Middle or CssInlineAlignment.Center or CssInlineAlignment.Bottom ? value : CssInlineAlignment.Baseline;
    }

    private void ObserveGroupHeights(Model? model)
    {
        var groups = model?.Rows.Select(row => row.Group).OfType<FrameworkElement>().Where(group => !ReferenceEquals(group, owner)).ToHashSet() ?? [];
        foreach (var old in _groupHeightObservers.Keys.Where(group => !groups.Contains(group)).ToArray())
        { old.PropertyChangedInternal -= _groupHeightObservers[old]; _groupHeightObservers.Remove(old); }
        foreach (var group in groups.Where(group => !_groupHeightObservers.ContainsKey(group)))
        {
            var weak = new WeakReference<CssTableLayout>(this);
            Action<DependencyProperty, object?, object?> changed = (property, _, _) =>
            {
                if (property == FrameworkElement.HeightProperty && weak.TryGetTarget(out var table) && table._computing == 0) table.InvalidateOwner();
            };
            _groupHeightObservers.Add(group, changed); group.PropertyChangedInternal += changed;
        }
    }

    private void ArrangeChildren(FrameworkElement parent)
    {
        if (_model is not { } model) return;
        var parentRect = ReferenceEquals(parent, owner) ? default : model.Slots.GetValueOrDefault(parent);
        foreach (var (child, bounds) in model.Slots)
        {
            var expectedParent = owner is CssTableAnonymousBox box && ReferenceEquals(parent, owner) ? box.Owner : parent;
            if (!ReferenceEquals(CssContentsLayout.BoxParent(child), expectedParent)) continue;
            var offset = ReferenceEquals(parent, owner) ? Origin : default;
            var rect = new Rect(bounds.X - parentRect.X + offset.X, bounds.Y - parentRect.Y + offset.Y, bounds.Width, bounds.Height);
            var containing = Assigned(child) is { Caption: true } ? model.Desired :
                new Size(model.Desired.Width + model.RemovedSize.Width, model.Desired.Height + model.RemovedSize.Height);
            new CssLayoutBox(child, 0).ArrangeBorderBox(rect, containing, CssBoxAlignment.Stretch);
        }
    }

    internal static bool HasMisparentedParts(FrameworkElement parent)
    {
        if (!parent.IsCssContentHost || parent.CssDisplayMode is CssDisplayMode.Table or CssDisplayMode.TableRow || IsGroup(parent.CssDisplayMode) || parent is CssTableAnonymousBox { CssDisplayMode: CssDisplayMode.Table }) return false;
        var children = parent is CssTableAnonymousBox anonymous ? anonymous.Items : new CssPanelChildren(parent).Enumerate();
        return children.OfType<FrameworkElement>().Any(child => IsPart(Role(child)));
    }
    internal static IEnumerable<CssLayoutBox> FlowChildren(FrameworkElement parent)
    {
        if (parent is CssTableAnonymousBox { CssDisplayMode: CssDisplayMode.Table } anonymous)
            return anonymous.Items.Select((child, i) => new CssLayoutBox(child, i));
        var raw = parent is CssTableAnonymousBox cellBox ? cellBox.Items : new CssPanelChildren(parent).Enumerate();
        var input = raw.Where(child => child is not CssTableAnonymousBox && child.Visibility != Visibility.Collapsed && child is not FrameworkElement { CssLayout.IsOutOfFlow: true }).ToArray();
        var attachOwner = parent is CssTableAnonymousBox anonymousOwner ? anonymousOwner.Owner : parent;
        var wrappers = s_flowTables.GetOrCreateValue(parent); var used = new HashSet<CssTableAnonymousBox>(); var result = new List<CssLayoutBox>();
        bool IsTablePart(UIElement item) => item is FrameworkElement part && IsPart(Role(part));
        for (var i = 0; i < input.Length;)
        {
            var first = i;
            if (IsTablePart(input[i]))
            {
                var items = new List<UIElement>();
                while (i < input.Length)
                {
                    if (IsTablePart(input[i])) { items.Add(input[i]); i++; continue; }
                    var wsStart = i;
                    while (i < input.Length && IsWhitespace(input[i])) i++;
                    if (i > wsStart && i < input.Length && IsTablePart(input[i])) continue;
                    i = wsStart;
                    break;
                }
                var groupItems = items.ToArray();
                var wrapper = wrappers.FirstOrDefault(box => ReferenceEquals(box.Owner, attachOwner) && box.Items.SequenceEqual(groupItems));
                if (wrapper is null) { wrapper = new(attachOwner, groupItems, table: true); wrappers.Add(wrapper); }
                wrapper.CssInlineOuter = parent is not CssTableAnonymousBox && parent.CssInlineOuter; used.Add(wrapper); result.Add(new(wrapper, first));
            }
            else { result.Add(new(input[i], i)); i++; }
        }
        foreach (var unused in wrappers.Where(wrapper => !used.Contains(wrapper)).ToArray()) { unused.Detach(); wrappers.Remove(unused); }
        return result;
    }
}
