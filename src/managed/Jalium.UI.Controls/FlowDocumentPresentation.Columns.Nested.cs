using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly record struct ColumnConstraint(double Height, double FirstCapacity, double[]? Capacities = null);
    private sealed class DocumentColumnScope(DependencyObject source, CssDocumentColumns columns, double left, double top,
        DocumentColumnScope? parent, bool synthetic = false)
    {
        internal readonly DependencyObject Source = source;
        internal readonly CssDocumentColumns Columns = columns;
        internal readonly DocumentColumnScope? Parent = parent;
        internal readonly bool Synthetic = synthetic;
        internal double Left = left, Top = top, UsedHeight;
        internal DocumentHeight? Height;
        internal int FirstParagraph, EndParagraph, FirstLine, EndLine, FirstPaint, EndPaint, FirstFragment, EndFragment;
        internal readonly List<ColumnSpanner> Spanners = [];
    }
    private readonly record struct DocumentColumnRule(CssDocumentColumns Style, Rect Bounds, DependencyObject Source);
    private readonly record struct NestedPageBreak(DependencyObject Source, double At, CssFragmentBreak Value);
    private readonly List<NestedPageBreak> _nestedPageBreaks = [];
    private readonly List<DocumentColumnScope> _columnScopes = [];
    private readonly Dictionary<DependencyObject, ColumnConstraint> _nestedColumnConstraints = [];
    private DocumentColumnScope? _activeColumnScope, _rootColumnScope;
    private DocumentColumnPlan? _rootColumnPlan;
    private bool _hasNestedColumns, _columnConstraintsChanged;

    private static bool HasOwnDocumentColumns(DependencyObject source) => !DocumentContents(source) && source is not Table &&
        ((int)source.GetValue(CssMultiColumnProperties.CountProperty)! > 0 ||
         source.GetValue(CssMultiColumnProperties.WidthProperty) is CssLayoutLength { IsSet: true, IsAuto: false });

    private bool HasNestedDocumentColumns(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if(CssDisplayProperties.SuppressesBoxTree(block))continue;
            if (HasOwnDocumentColumns(block)) return true;
            if (block is Section section && HasNestedDocumentColumns(section.Blocks)) return true;
            if (block is Documents.List list && list.ListItems.Any(item => !CssDisplayProperties.SuppressesBoxTree(item) && (HasOwnDocumentColumns(item) || HasNestedDocumentColumns(item.Blocks)))) return true;
            if (block is Table table && table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells)
                .Any(cell => !CssDisplayProperties.SuppressesBoxTree(cell) && (HasOwnDocumentColumns(cell) || HasNestedDocumentColumns(cell.Blocks)))) return true;
        }
        return false;
    }

    private bool InColumnScope(DependencyObject source, DocumentColumnScope scope) => scope.Parent is null || Within(source, scope.Source);

    private DocumentColumnScope BeginColumnScope(DependencyObject source, CssDocumentColumns columns, double left, double top, int paragraph, bool synthetic = false,
        DocumentHeight? height = null)
    {
        var scope = new DocumentColumnScope(source, columns, left, top, _activeColumnScope, synthetic)
        {
            FirstParagraph = paragraph, FirstLine = _lines.Count, FirstPaint = _blocks.Count, FirstFragment = _fragmentBoxes.Count, Height = height,
        };
        _columnScopes.Add(scope); _activeColumnScope = scope; return scope;
    }

    private bool OwnsColumnBoundary(DocumentColumnScope scope, TextElement source)
    {
        if (ReferenceEquals(source, scope.Source)) return true;
        for (DependencyObject? parent = source.Parent; parent is not null; parent = (parent as FrameworkContentElement)?.Parent)
        {
            if (ReferenceEquals(parent, scope.Source)) return true;
            if (HasOwnDocumentColumns(parent)) return false;
        }
        return scope.Parent is null;
    }

    private DocumentColumnPlan BuildColumnPlan(DocumentColumnScope scope, double end)
    {
        var original = _lines.GetRange(scope.FirstLine, scope.EndLine - scope.FirstLine);
        var bands = new List<Line>(); var mapped = new int[original.Count];
        foreach (var item in original.Select((line, index) => (Line: line, Index: index)).Where(item => item.Line.Height > 0 && !item.Line.IsSpace).OrderBy(item => item.Line.Top))
        {
            var line = item.Line with { Top = item.Line.Top - scope.Top, Left = item.Line.Left - scope.Left };
            if (bands.Count > 0 && line.Top < bands[^1].Top + bands[^1].Height - .01)
            {
                var prior = bands[^1]; bands[^1] = prior with { Start = Math.Min(prior.Start, line.Start), End = Math.Max(prior.End, line.End),
                    Height = Math.Max(prior.Height, line.Top + line.Height - prior.Top), BreakBefore = prior.BreakBefore || line.BreakBefore };
            }
            else bands.Add(line);
            mapped[item.Index] = bands.Count - 1;
        }
        var boxes = new List<FragmentBox>();
        foreach (var box in _fragmentBoxes.Skip(scope.FirstFragment).Take(scope.EndFragment - scope.FirstFragment))
        {
            if (box.Source is null || !OwnsColumnBoundary(scope, box.Source)) continue;
            var indices = (box.LineBands ?? Enumerable.Range(box.FirstLine, box.EndLine - box.FirstLine).ToArray())
                .Select(index => index - scope.FirstLine).Where(index => index >= 0 && index < mapped.Length && original[index].Height > 0 && !original[index].IsSpace)
                .Select(index => mapped[index]).Distinct().Order().ToArray();
            boxes.Add(box with { FirstLine = indices.FirstOrDefault(), EndLine = indices.Length == 0 ? 0 : indices[^1] + 1,
                MarginTop = box.MarginTop - scope.Top, BorderTop = box.BorderTop - scope.Top, Bottom = box.Bottom - scope.Top,
                LineBands = box.Source is Paragraph && !HasOwnDocumentColumns(box.Source) ? indices : null });
        }
        if (scope.Source is Paragraph paragraph && bands.Count > 0)
            boxes.Add(new(paragraph, 0, bands.Count, 0, 0, end - scope.Top));
        var firstCapacity = scope.Parent is null && double.IsFinite(_paginationColumnHeight) && scope.Top > 0
            ? _paginationColumnHeight - scope.Top % _paginationColumnHeight : _paginationColumnHeight;
        var limit = _nestedColumnConstraints.GetValueOrDefault(scope.Source, new(_paginationColumnHeight, firstCapacity));
        var spanners = scope.Spanners.Select(span => span with { Start = span.Start - scope.Top, End = span.End - scope.Top }).ToArray();
        var forcedPages = _nestedPageBreaks.Where(item => InColumnScope(item.Source, scope)).Select(item => (item.At - scope.Top, item.Value)).ToArray();
        var reserved = _heightSpaces.Where(span => InColumnScope(span.Source, scope))
            .Select(span => (span.Start - scope.Top, span.End - scope.Top)).ToArray();
        var repeatable = _repeatableTables.Where(rt => InColumnScope(rt.Table, scope))
            .Select(rt => rt with
            {
                TableBounds = Shift(rt.TableBounds, 0, -scope.Top),
                HeaderBounds = rt.Header is not null ? Shift(rt.HeaderBounds, 0, -scope.Top) : default,
                FooterBounds = rt.Footer is not null ? Shift(rt.FooterBounds, 0, -scope.Top) : default,
                BodyTop = rt.BodyTop - scope.Top,
                BodyBottom = rt.BodyBottom - scope.Top,
            }).ToArray();
        return new(scope.Columns, bands, boxes, spanners, limit.Height, Math.Max(0, end - scope.Top), limit.FirstCapacity, forcedPages, limit.Capacities, reserved, scope.Height, repeatable);
    }

    private void FinishColumnScope(DocumentColumnScope scope, ref double y, int paragraph)
    {
        scope.EndParagraph = paragraph; scope.EndLine = _lines.Count; scope.EndPaint = _blocks.Count; scope.EndFragment = _fragmentBoxes.Count;
        var plan = BuildColumnPlan(scope, y);
        foreach (var demoted in plan.DemotedSpanners)
            if (_demotedDocumentSpanners.Add(demoted))
                _columnConstraintsChanged = true;
        scope.UsedHeight = plan.UsedHeight;
        if (scope.Height is { } height) CaptureDocumentNaturalHeight(scope.Source, height, plan.NaturalHeight + height.Insets);
        UpdateNestedColumnConstraints(scope, plan);
        ApplyColumnPlan(scope, plan);
        foreach (var space in plan.ReservedSpace) _heightSpaces.Add(new(scope.Source, scope.Top + space.Start, scope.Top + space.End));
        AddColumnFreeSpans(scope, plan);
        _nestedPageBreaks.AddRange(plan.ForcedPages.Select(item => new NestedPageBreak(scope.Source, scope.Top + item.At, item.Value)));
        y = scope.Top + scope.UsedHeight; _activeColumnScope = scope.Parent;
    }

    private void MoveNestedColumns(DependencyObject source, double dx, double dy, Rect? clip, bool removed)
    {
        foreach (var scope in _columnScopes.Where(scope => Within(scope.Source, source))) { scope.Left += dx; scope.Top += dy; }
        MoveColumnFreeSpans(source, dy, clip, removed);
        MoveDocumentHeightSpaces(source, dy, clip, removed);
        for (var index = _nestedPageBreaks.Count - 1; index >= 0; index--)
            if (Within(_nestedPageBreaks[index].Source, source))
            {
                if (removed) _nestedPageBreaks.RemoveAt(index);
                else _nestedPageBreaks[index] = _nestedPageBreaks[index] with { At = _nestedPageBreaks[index].At + dy };
            }
        for (var index = 0; index < _documentColumnRules.Count; index++)
        {
            var rule = _documentColumnRules[index]; if (!Within(rule.Source, source)) continue;
            var bounds = Shift(rule.Bounds, dx, dy);
            var ruleRemoved = removed;
            if (clip is { } box)
            {
                var top = Math.Max(bounds.Top, box.Top); var bottom = Math.Min(bounds.Bottom, box.Bottom);
                bounds = new(bounds.X, top, 0, Math.Max(0, bottom - top));
                if (bounds.X < box.Left || bounds.X > box.Right) ruleRemoved = true;
            }
            _documentColumnRules[index] = rule with { Bounds = ruleRemoved ? new(bounds.X, bounds.Y, 0, 0) : bounds };
        }
    }
}
