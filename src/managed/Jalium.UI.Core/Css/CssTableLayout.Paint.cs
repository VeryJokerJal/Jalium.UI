using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed partial class CssTableLayout
{
    private readonly Dictionary<FrameworkElement, Action<DependencyProperty, object?, object?>> _columnObservers = [];

    private void ObserveColumns(Model? model)
    {
        var current = model is null ? [] : model.Columns.SelectMany(column =>
            column.Group is null ? new[] { column.Source } : new[] { column.Source, column.Group }).ToHashSet();
        foreach (var old in _columnObservers.Keys.Where(element => !current.Contains(element)).ToArray())
        { old.PropertyChangedInternal -= _columnObservers[old]; _columnObservers.Remove(old); }
        foreach (var element in current.Where(element => !_columnObservers.ContainsKey(element)))
        {
            var weak = new WeakReference<CssTableLayout>(this);
            Action<DependencyProperty, object?, object?> changed = (_, _, _) =>
            {
                if (!weak.TryGetTarget(out var table)) return;
                table.InvalidateOwner();
            };
            _columnObservers.Add(element, changed); element.PropertyChangedInternal += changed;
        }
    }

    internal static bool PaintOwn(Visual visual, DrawingContext context)
    {
        if (visual is FrameworkElement root && DecorationBox(root) is not null) return true;
        if (visual is not FrameworkElement element || Assigned(element) is not { } state) return false;
        if (state.Caption) return false;
        if (state.Cell) return state.Empty;
        var regions = state.Regions.Select(rect => new Rect(rect.X - state.Bounds.X, rect.Y - state.Bounds.Y, rect.Width, rect.Height)).ToArray();
        PaintBackground(element, context, new Rect(state.Bounds.Size), regions); return true;
    }

    internal static void PaintColumns(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement element || element is CssTableAnonymousBox) return;
        if (element.CssDisplayMode == CssDisplayMode.Table && DecorationBox(element) is null) CssDisplayLayout.TableFor(element).PaintColumns(context, default);
        foreach (var table in EnumerateFlowTables(element))
            CssDisplayLayout.TableFor(table).PaintColumns(context, table.AllocationOrigin);
    }

    private void PaintColumns(DrawingContext context, Point offset)
    {
        if (_model is not { Rows.Count: > 0, Columns.Count: > 0 } model) return;
        var cells = model.Cells.Where(cell => !cell.Empty && !model.Visibility.Removed(cell.Track)).Select(cell => (Column: cell.Track.Column, cell.Bounds))
            .Concat(model.MissingRegions.Select(region => (region.Column, region.Bounds))).ToArray();
        if (cells.Length == 0) return;
        var top = cells.Min(cell => cell.Bounds.Top); var bottom = cells.Max(cell => cell.Bounds.Bottom);
        var origin = model.Insets.Left;
        var rtl = StyleOwner.FlowDirection == FlowDirection.RightToLeft;
        Rect Bounds(int first, int count)
        {
            var width = model.Horizontal.Span(first, count);
            return new(origin + (rtl ? model.Horizontal.Extent - model.Horizontal.Starts[first] - width : model.Horizontal.Starts[first]) + offset.X,
                top + offset.Y, width, bottom - top);
        }
        Rect[] Regions(int first, int count) => cells.Where(cell => cell.Column >= first && cell.Column < first + count)
            .Select(cell => new Rect(cell.Bounds.X + offset.X, cell.Bounds.Y + offset.Y, cell.Bounds.Width, cell.Bounds.Height)).ToArray();
        foreach (var group in model.Columns.Where(column => column.Group is not null).GroupBy(column => column.Group!))
        {
            var first = group.Min(column => column.First); var count = group.Sum(column => column.Count);
            PaintBackground(group.Key, context, Bounds(first, count), Regions(first, count));
        }
        foreach (var column in model.Columns)
            PaintBackground(column.Source, context, Bounds(column.First, column.Count), Regions(column.First, column.Count));
    }

    private static void PaintBackground(FrameworkElement element, DrawingContext context, Rect box, Rect[] regions)
    {
        if (regions.Length == 0) return;
        var property = CssDependencyPropertyLookup.Find(element.GetType(), "Background");
        var brush = property is not null ? element.GetValue(property) as Brush : null;
        var hasBorderImage = element.GetValue(CssBorderImageProperties.SourceProperty) is CssBorderImageSource;
        if (brush is null && !hasBorderImage) return;
        var coverage = regions[0]; foreach (var region in regions.Skip(1)) coverage = Rect.Union(coverage, region);
        var paintBox = box.Width > 0 && box.Height > 0 ? Rect.Union(box, coverage) : coverage;
        foreach (var region in regions)
        {
            if (region.Width <= 0 || region.Height <= 0) continue;
            context.PushClip(new RectangleGeometry(region));
            try
            {
                if (brush is not null && property is not null)
                {
                    void Shape(Brush current) => context.DrawRectangle(current, null, paintBox);
                    if (!CssBackgroundPainter.TryDraw(element, property, brush, context, paintBox, default, default, default, Shape)) Shape(brush);
                }
                else CssBorderImagePainter.Decoration(element, context, paintBox, default);
            }
            finally { context.Pop(); }
        }
    }
}
