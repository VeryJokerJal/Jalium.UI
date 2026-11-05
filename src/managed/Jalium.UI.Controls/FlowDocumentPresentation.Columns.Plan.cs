using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly record struct ColumnSpanner(Block Source, double Start, double End);
    private readonly record struct DocumentColumnSlice(double Start, double End, Rect Bounds, Block? Spanner = null,
        double SourceLeft = 0, double Capacity = double.PositiveInfinity, int Group = 0, bool Balanced = false,
        bool IsRepeat = false, DependencyObject? RepeatGroup = null)
    {
        internal double Dx => Bounds.X - SourceLeft;
        internal double Dy => Bounds.Y - Start;
    }
    private readonly record struct DocumentColumnSlot(double Top, double Capacity, double Used);
    internal readonly record struct ColumnPage(double Top, double Bottom, bool Blank = false);
    private readonly List<ColumnPage> _columnPages = [];
    private readonly List<DocumentColumnRule> _documentColumnRules = [];
    private readonly HashSet<Block> _demotedDocumentSpanners = [];
    private double _paginationColumnHeight = double.NaN;
    private CssDocumentColumns? _documentColumns;
    internal bool HasColumns => _documentColumns is not null;
    internal IReadOnlyList<ColumnPage> ColumnPages => _columnPages;

    internal void SetPaginationColumnHeight(double height)
    {
        height = double.IsFinite(height) ? Math.Max(1, height) : double.NaN;
        if (_paginationColumnHeight.Equals(height)) return;
        _paginationColumnHeight = height; _layoutWidth = double.NaN;
    }

    private bool Within(DependencyObject source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = DocumentBoxParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private bool SpansDocumentColumns(Block source)
    {
        if (_demotedDocumentSpanners.Contains(source)) return false;
        if (_activeColumnScope is not { Synthetic: false } scope || !(bool)source.GetValue(CssMultiColumnProperties.SpanProperty)!) return false;
        for (DependencyObject? parent = DocumentBoxParent(source); parent is not null && !ReferenceEquals(parent, scope.Source); parent = DocumentBoxParent(parent))
            if (!DocumentContents(parent) && (parent is TableCell || parent is TextElement text && (bool)text.GetValue(CssMultiColumnProperties.SpanProperty)! ||
                HasOwnDocumentColumns(parent))) return false;
        return true;
    }

    private void PlanDocumentColumns(double end)
    {
        _columnPages.Clear();
        if (_documentColumns is not { } columns) return;
        // Cell content can overflow the row after percentage-height relayout.
        // Include its visible lines before projecting the root's fragments;
        // expanding the scroll extent afterwards cannot restore clipped pieces.
        end = Math.Max(end, _lines.Where(line => line.Height > 0).Select(line => line.Top + line.Height).DefaultIfEmpty(0).Max());
        var scope = _rootColumnScope!;
        scope.EndParagraph = _paragraphs.Count; scope.EndLine = _lines.Count; scope.EndPaint = _blocks.Count; scope.EndFragment = _fragmentBoxes.Count;
        var plan = _hasNestedColumns || scope.Top != 0 || scope.Left != 0 || scope.Height is { Constrained: true } || _repeatableTables.Count > 0
            ? BuildColumnPlan(scope, end)
            : new DocumentColumnPlan(columns, _lines, _fragmentBoxes, scope.Spanners, _paginationColumnHeight, end,
                reservedSpace: _heightSpaces.Select(span => (span.Start, span.End)).ToArray());
        foreach (var demoted in plan.DemotedSpanners)
            if (_demotedDocumentSpanners.Add(demoted))
                _columnConstraintsChanged = true;
        _rootColumnPlan = plan; UpdateNestedColumnConstraints(scope, plan);
        CaptureDocumentNaturalHeight(scope.Source, _rootHeight, plan.NaturalHeight + _rootHeight.Insets);
        scope.UsedHeight = plan.UsedHeight;
        if (double.IsFinite(_paginationColumnHeight))
        {
            var height = Math.Max(1, _paginationColumnHeight);
            for (var top = 0d; top + height <= scope.Top + .00001; top += height) _columnPages.Add(new(top, top + height));
            for (var index = 0; index < plan.Pages.Count; index++)
            {
                var page = plan.Pages[index];
                _columnPages.Add(page with { Top = index == 0 ? Math.Floor(scope.Top / height) * height : scope.Top + page.Top, Bottom = scope.Top + page.Bottom });
            }
        }
        else _columnPages.AddRange(plan.Pages);
        _extentHeight = scope.Top + plan.ExtentHeight;
    }
}
