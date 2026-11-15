using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed class DocumentColumnOwner(FlowDocumentPresentation owner) : ICssColumnLayout
    {
        private readonly WeakReference<FlowDocumentPresentation> _owner = new(owner);
        public bool IsApplied => _owner.TryGetTarget(out var current) && current._columnsApplied;
        public bool Contains(FrameworkElement source) => _owner.TryGetTarget(out var current) && ReferenceEquals(source.VisualParent, current);
    }
    private DocumentColumnOwner? _columnOwner;
    private bool _columnsApplied;
    private readonly List<TextBlock.CssColumnTextSnapshot> _columnTexts = [];

    private void ClearDocumentColumns()
    {
        _columnsApplied = false;
        _documentColumns = null;
        foreach (var text in _columnTexts.AsEnumerable().Reverse()) text.Release();
        _columnTexts.Clear();
        foreach (var view in _paragraphs)
        {
            if (view.Text.CssColumnBox?.Layout == _columnOwner) view.Text.CssColumnBox = null;
            view.ColumnClips = null; view.Text.InvalidateCssAllocation();
        }
        _columnPages.Clear(); _documentColumnRules.Clear();
        _columnScopes.Clear(); _nestedPageBreaks.Clear(); _columnFreeSpans.Clear(); _activeColumnScope = _rootColumnScope = null; _rootColumnPlan = null;
    }

    private static Rect[] ClipPieces(Rect bounds, IEnumerable<Rect> clips)
        => clips.Select(clip => Rect.Intersect(bounds, clip)).Where(rect => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0).ToArray();
    private static Geometry ColumnClip(IEnumerable<Rect> pieces)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rect in pieces) group.Children.Add(new RectangleGeometry(rect));
        group.Freeze(); return group;
    }

    private void ApplyDocumentColumns()
    {
        if (_rootColumnScope is { } scope && _rootColumnPlan is { } plan) ApplyColumnPlan(scope, plan);
    }

    private void ApplyColumnPlan(DocumentColumnScope scope, DocumentColumnPlan plan)
    {
        if (plan.Slices.Count == 0) return;
        var columns = scope.Columns;
        var slices = plan.Slices.Select(slice => slice with { Start = slice.Start + scope.Top, End = slice.End + scope.Top,
            Bounds = Shift(slice.Bounds, scope.Left, scope.Top), SourceLeft = scope.Left }).ToArray();
        var flowSlices = slices.Where(slice => !slice.IsRepeat).ToArray();
        if (flowSlices.Length == 0) flowSlices = slices;
        DocumentColumnSlice Slice(double top) => flowSlices.FirstOrDefault(slice => top >= slice.Start - .000001 && top < slice.End - .000001,
            top < flowSlices[0].Start ? flowSlices[0] : flowSlices[^1]);
        Point Project(Point point) { var slice = Slice(point.Y); return new(point.X + slice.Dx, point.Y + slice.Dy); }
        Rect[] Pieces(Rect bounds) => flowSlices.Where(slice => slice.End > bounds.Top && slice.Start < bounds.Bottom)
            .Select(slice => Shift(Rect.Intersect(bounds, new(bounds.X, slice.Start, bounds.Width, slice.End - slice.Start)), slice.Dx, slice.Dy))
            .Where(bounds => !bounds.IsEmpty && bounds.Height > .00001 && bounds.Width > .00001).ToArray();
        Rect Union(Rect bounds)
        {
            var result = Rect.Empty; foreach (var piece in Pieces(bounds)) result.Union(piece);
            return result.IsEmpty ? new(Project(bounds.Location), bounds.Size) : result;
        }
        _columnOwner ??= new(this);
        FreezeCollapsedColumnGeometry();
        var views = _paragraphs.Skip(scope.FirstParagraph).Take(scope.EndParagraph - scope.FirstParagraph).ToArray();
        var natural = views.ToDictionary(view => view, view => view.Bounds);
        var snapshots = new List<TextBlock.CssColumnTextSnapshot>();
        foreach (var view in views)
        {
            view.Text.Arrange(view.Bounds);
            var snapshot = new TextBlock.CssColumnTextSnapshot(view.Text, this, _columnOwner);
            snapshots.Add(snapshot); _columnTexts.Add(snapshot);
        }
        _columnsApplied = true;
        for (var index = 0; index < views.Length; index++)
        {
            var view = views[index]; var original = natural[view];
            var pieces = new List<CssColumnPiece>(); var clips = new List<Rect>(); var union = Rect.Empty;
            foreach (var slice in slices)
            {
                if (slice.IsRepeat && (slice.RepeatGroup is null || !Within(view.Source, slice.RepeatGroup))) continue;
                if (slice.Spanner is { } span && !Within(view.Source, span)) continue;
                var top = Math.Max(original.Top, slice.Start); var bottom = Math.Min(original.Bottom, slice.End);
                // A balancing epsilon at an exact line edge is not another
                // visible fragment in the next ancestor column.
                if (bottom - top <= .00001) continue;
                var region = new Rect(original.X, top, original.Width, bottom - top);
                var destination = Shift(region, slice.Dx, slice.Dy);
                pieces.Add(new(new(0, top - original.Top, original.Width, bottom - top), destination)); union.Union(destination);
                // Adjacent text sources share a line. Their advance envelopes
                // are not ink clips: preserve the same one-DIP antialias fringe
                // used by the shared formatter, while retaining page/cell clips.
                var inkRegion = view.ContentsFlow ? new Rect(region.X - 1, region.Y, region.Width + 2, region.Height) : region;
                var sourceClips = view.ColumnClips is { } prior ? ClipPieces(inkRegion, prior) : new[] { inkRegion };
                if (view.TableClip is { } cell) sourceClips = ClipPieces(cell, sourceClips);
                clips.AddRange(sourceClips.Select(clip => Shift(clip, slice.Dx, slice.Dy)));
            }
            if (union.IsEmpty) union = new(Project(original.Location), original.Size);
            view.Bounds = union; view.ColumnClips = clips.ToArray(); view.TableClip = null;
            var local = pieces.Select(piece => new CssColumnPiece(piece.Source, Shift(piece.Destination, -union.X, -union.Y))).ToArray();
            view.Text.CssColumnBox = new(view.Text, _columnOwner, original.Size, union.Size, local,
                clips.Select(clip => Shift(clip, -union.X, -union.Y)).ToArray());
            view.Text.InvalidateCssAllocation(); view.Text.Measure(union.Size);
            snapshots[index].Apply(union, Project);
        }
        var paints = _blocks.Skip(scope.FirstPaint).Take(scope.EndPaint - scope.FirstPaint).ToArray();
        _blocks.RemoveRange(scope.FirstPaint, scope.EndPaint - scope.FirstPaint);
        foreach (var paint in paints)
            foreach (var slice in slices)
            {
                if (slice.IsRepeat && (slice.RepeatGroup is null || !Within(paint.Source, slice.RepeatGroup))) continue;
                if (slice.Spanner is { } span && !Within(paint.Source, span)) continue;
                var region = Rect.Intersect(paint.Bounds, new Rect(paint.Bounds.X, slice.Start, paint.Bounds.Width, slice.End - slice.Start));
                if (paint.Clip is { } clip) region.Intersect(clip);
                if (region.IsEmpty || region.Width <= .00001 || region.Height <= .00001) continue;
                var clone = CssBoxDecorationBreakProperties.Value(paint.Source) == CssBoxDecorationBreak.Clone;
                _blocks.Add(paint with { Bounds = clone ? Shift(region, slice.Dx, slice.Dy) : Shift(paint.Bounds, slice.Dx, slice.Dy),
                    Clip = Shift(region, slice.Dx, slice.Dy),
                    PaintBounds = paint.PaintBounds is { } pb ? Shift(pb, slice.Dx, slice.Dy) : null });
            }
        var frames = _collapsedTableFrames.Where(frame => InColumnScope(frame.Plan.Table, scope)).ToArray();
        _collapsedTableFrames.RemoveAll(frame => InColumnScope(frame.Plan.Table, scope));
        foreach (var frame in frames)
            foreach (var slice in flowSlices)
            {
                if (!_tableGeometry.TryGetValue(frame.Plan.Table, out var bounds)) continue;
                if (slice.Spanner is { } span && !Within(frame.Plan.Table, span)) continue;
                var clip = Rect.Intersect(bounds, new Rect(bounds.X, slice.Start, bounds.Width, slice.End - slice.Start));
                if (frame.Clip is { } old) clip.Intersect(old);
                if (clip.IsEmpty || clip.Height <= .00001) continue;
                _collapsedTableFrames.Add(frame with { Origin = new(frame.Origin.X + slice.Dx, frame.Origin.Y + slice.Dy),
                    Clip = Shift(clip, slice.Dx, slice.Dy), Projection = new(frame.Projection.X + slice.Dx, frame.Projection.Y + slice.Dy) });
            }
        var cells = _tableCells.Where(cell => InColumnScope(cell.Source, scope)).ToArray();
        _tableCells.RemoveAll(cell => InColumnScope(cell.Source, scope));
        foreach (var cell in cells) foreach (var rect in Pieces(cell.Bounds)) _tableCells.Add(cell with { Bounds = rect });
        foreach (var key in _tableGeometryKeys.Where(key => InColumnScope(key, scope))) _tableGeometry[key] = Union(_tableGeometry[key]);
        if (scope.Parent is not null)
            for (var index = scope.FirstFragment; index < scope.EndFragment; index++)
            {
                var box = _fragmentBoxes[index]; if (box.Source is null) continue;
                var margin = Union(new(scope.Left, box.MarginTop, columns.Available, Math.Max(.000001, box.Bottom - box.MarginTop)));
                var border = Union(new(scope.Left, box.BorderTop, columns.Available, Math.Max(.000001, box.Bottom - box.BorderTop)));
                _fragmentBoxes[index] = box with { MarginTop = margin.Top, BorderTop = border.Top, Bottom = border.Bottom };
            }
        Line ProjectLine(Line line)
        {
            var slice = Slice(line.Top); return line with { Top = line.Top + slice.Dy, Left = line.Left + slice.Dx,
                Width = double.IsFinite(line.Width) ? line.Width : slice.Bounds.Width };
        }
        for (var index = scope.FirstLine; index < scope.EndLine; index++) _lines[index] = ProjectLine(_lines[index]);
        if (scope.Parent is null)
            for (var index = 0; index < _positionLines.Count; index++) _positionLines[index] = ProjectLine(_positionLines[index]);
        var rules = _documentColumnRules.Where(rule => InColumnScope(rule.Source, scope)).ToArray();
        _documentColumnRules.RemoveAll(rule => InColumnScope(rule.Source, scope));
        foreach (var rule in rules)
            foreach (var slice in flowSlices)
            {
                var top = Math.Max(rule.Bounds.Top, slice.Start); var bottom = Math.Min(rule.Bounds.Bottom, slice.End);
                if (bottom - top > .00001) _documentColumnRules.Add(rule with { Bounds = new(rule.Bounds.X + slice.Dx, top + slice.Dy, 0, bottom - top) });
            }
        _documentColumnRules.AddRange(plan.Rules.Select(rule => new DocumentColumnRule(columns, Shift(rule, scope.Left, scope.Top), scope.Source)));
        ProjectColumnFreeSpans(scope, flowSlices);
        ProjectDocumentHeightSpaces(scope, flowSlices);
        for (var index = 0; index < _nestedPageBreaks.Count; index++)
            if (InColumnScope(_nestedPageBreaks[index].Source, scope))
                _nestedPageBreaks[index] = _nestedPageBreaks[index] with { At = Project(new(scope.Left, _nestedPageBreaks[index].At)).Y };
        foreach (var child in _columnScopes.Where(child => !ReferenceEquals(child, scope) && InColumnScope(child.Source, scope)))
        { var point = Project(new(child.Left, child.Top)); child.Left = point.X; child.Top = point.Y; }
        UIElement.InvalidateHitTestCache(); InvalidateVisual();
    }

    private void PaintDocumentColumnRules(DrawingContext context)
    {
        if (_document is null) return;
        var padding = EffectivePagePadding;
        foreach (var rule in _documentColumnRules)
            if (rule.Style.RuleBrush is { } brush && rule.Bounds.Height > 0)
                CssColumnBoxPainter.PaintRules(context, this, [Shift(rule.Bounds, padding.Left, padding.Top - _pageTop + _pageRepeatedHeaderHeight)],
                    rule.Style.RuleWidth, rule.Style.RuleStyle, brush);
    }
}
