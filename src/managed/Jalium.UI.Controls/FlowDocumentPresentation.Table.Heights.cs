using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private static bool ExplicitCellHeight(DependencyObject source, bool table)
    {
        if (source.HasLocalOrAnimatedValue(FrameworkElement.HeightProperty))
            return double.IsFinite((double)source.GetValue(FrameworkElement.HeightProperty)!);
        if (CssNode.Get(source).CssLayout?.Height is { IsSet: true, IsAuto: false } height)
            return table || !height.IsPercent;
        return double.IsFinite((double)source.GetValue(FrameworkElement.HeightProperty)!);
    }

    private bool NeedsCellHeightLayout(Table table, TableCell cell)
    {
        if (!ExplicitCellHeight(cell, false) && !ExplicitCellHeight(table, true)) return false;
        if (HasOwnDocumentColumns(cell)) return true;
        static bool UsesCellBasis(DependencyObject block, DependencyProperty property, CssLayoutLength declared)
        {
            if (block.HasLocalOrAnimatedValue(property)) return false;
            var value = DocumentSizeValue(block, property, declared);
            return value.IsPercent || value.Kind == CssLayoutLength.KindStretch || value.SizingBasis.Kind == CssLayoutLength.KindStretch;
        }
        static bool AnyDescendantUsesCellBasis(IEnumerable<DependencyObject> nodes)
        {
            foreach (var block in ElidedDocumentChildren(nodes))
            {
                var state = CssNode.Get(block).CssLayout;
                if (UsesCellBasis(block, FrameworkElement.HeightProperty, state?.Height ?? default) ||
                    UsesCellBasis(block, FrameworkElement.MinHeightProperty, state?.MinHeight ?? default) ||
                    UsesCellBasis(block, FrameworkElement.MaxHeightProperty, state?.MaxHeight ?? default)) return true;
            }
            return false;
        }
        return AnyDescendantUsesCellBasis(CellNodes(cell));
    }

    // Only cell content is laid out again. Track minima and row baselines keep
    // their first-pass values, so percentage overflow cannot feed back into rows.
    private sealed class TableCellLayoutPass(FlowDocumentPresentation owner)
    {
        private readonly int _lines = owner._lines.Count, _blocks = owner._blocks.Count,
            _fragments = owner._fragmentBoxes.Count, _heightSpaces = owner._heightSpaces.Count,
            _cells = owner._tableCells.Count, _geometry = owner._tableGeometryKeys.Count,
            _scopes = owner._columnScopes.Count, _texts = owner._columnTexts.Count,
            _rules = owner._documentColumnRules.Count, _free = owner._columnFreeSpans.Count,
            _breaks = owner._nestedPageBreaks.Count, _contents = owner._documentContentsRuns.Count;
        private readonly DocumentColumnScope? _active = owner._activeColumnScope;
        private readonly bool _heights = owner._hasDocumentHeights, _columns = owner._columnsApplied,
            _constraintsChanged = owner._columnConstraintsChanged;
        private readonly Dictionary<DependencyObject, ColumnConstraint> _constraints = new(owner._nestedColumnConstraints);
        private readonly HashSet<int> _clipped = new(owner._clippedTableLines);
        private readonly (DocumentColumnScope Scope, int Count)[] _spanners = owner._columnScopes.Select(scope => (scope, scope.Spanners.Count)).ToArray();
        private readonly CollapsedTableFrame[] _frames = owner._collapsedTableFrames.ToArray();

        internal void Restore(int firstParagraph, int endParagraph)
        {
            for (var index = owner._columnTexts.Count - 1; index >= _texts; index--) owner._columnTexts[index].Release();
            Trim(owner._columnTexts, _texts); owner._columnsApplied = _columns;
            owner.ReleaseDocumentContentsRuns(_contents);
            for (var index = firstParagraph; index < endParagraph; index++)
            {
                var view = owner._paragraphs[index]; view.TableClip = null; view.TableSuppressed = false; view.ColumnClips = null;
                view.Text.CssColumnBox = null; view.Text.InvalidateCssAllocation();
            }
            Trim(owner._lines, _lines); Trim(owner._blocks, _blocks); Trim(owner._fragmentBoxes, _fragments);
            Trim(owner._heightSpaces, _heightSpaces); owner._hasDocumentHeights = _heights;
            Trim(owner._tableCells, _cells);
            foreach (var key in owner._tableGeometryKeys.Skip(_geometry)) owner._tableGeometry.Remove(key);
            Trim(owner._tableGeometryKeys, _geometry);
            Trim(owner._columnScopes, _scopes); owner._activeColumnScope = _active;
            foreach (var (scope, count) in _spanners) Trim(scope.Spanners, count);
            Trim(owner._documentColumnRules, _rules); Trim(owner._columnFreeSpans, _free); Trim(owner._nestedPageBreaks, _breaks);
            owner._nestedColumnConstraints.Clear(); foreach (var pair in _constraints) owner._nestedColumnConstraints.Add(pair.Key, pair.Value);
            owner._columnConstraintsChanged = _constraintsChanged;
            owner._clippedTableLines.Clear(); owner._clippedTableLines.UnionWith(_clipped);
            owner._collapsedTableFrames.Clear(); owner._collapsedTableFrames.AddRange(_frames);
        }
        private static void Trim<T>(List<T> list, int count) => list.RemoveRange(count, list.Count - count);
    }
}
