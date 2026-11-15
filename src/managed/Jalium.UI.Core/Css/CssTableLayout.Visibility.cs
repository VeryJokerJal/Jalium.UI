using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed partial class CssTableLayout
{
    private static CssTableVisibility TrackVisibility(Model model, int columns)
    {
        var visibility = new CssTableVisibility(model.Rows.Count, columns);
        for (var row = 0; row < model.Rows.Count; row++)
            visibility.Rows[row] = CssTableVisibility.Collapsed(model.Rows[row].Source) || CssTableVisibility.Collapsed(model.Rows[row].Group);
        foreach (var column in model.Columns)
            if (CssTableVisibility.Collapsed(column.Source) || CssTableVisibility.Collapsed(column.Group))
                Array.Fill(visibility.Columns, true, column.First, column.Count);
        return visibility;
    }

    internal static Size ProjectMeasuredSize(FrameworkElement element, Size size) =>
        element.CssDisplayMode == CssDisplayMode.Table && CssDisplayLayout.TryTableFor(element, out var table) && table?._model is { } model
            ? new(Math.Max(0, size.Width - model.RemovedSize.Width), Math.Max(0, size.Height - model.RemovedSize.Height)) : size;

    internal static Point ContentOrigin(FrameworkElement element) => Assigned(element) is { ClipTracks: true, Cell: true } state &&
        element.FlowDirection == FlowDirection.RightToLeft ? new(state.Bounds.Width - state.ContentSize.Width, 0) : default;

    internal static bool TrackSuppressed(DependencyObject source)
    {
        for (var current = source as Visual; current is not null; current = current.VisualParent)
            if (current is FrameworkElement element && Assigned(element) is { RemovedTrack: true }) return true;
        return false;
    }

    internal static Geometry? TrackClip(Visual visual, Geometry? clip)
    {
        if (visual is not FrameworkElement element || Assigned(element) is not { ClipTracks: true, Cell: true } state) return clip;
        var bounds = new RectangleGeometry(new Rect(state.Bounds.Size));
        return clip is null ? bounds : Geometry.Combine(clip, bounds, GeometryCombineMode.Intersect, null);
    }

    private static void AssignVisibility(FrameworkElement element, Allocation state, bool removed, bool clip, Size contentSize)
    {
        var changed = state.RemovedTrack != removed || state.ClipTracks != clip || state.ContentSize != contentSize;
        state.RemovedTrack = removed; state.ClipTracks = clip; state.ContentSize = contentSize;
        if (!changed) return;
        element.InvalidateVisual(); UIElement.InvalidateHitTestCache();
        element.UpdateIsVisibleFromTree(forceDescendants: true);
    }

    internal static void VisibilityChanged(UIElement element)
    {
        var layouts = new HashSet<CssTableLayout>();
        void Visit(UIElement current)
        {
            if (current is FrameworkElement framework)
            {
                if (Assigned(framework)?.Layout.TryGetTarget(out var assigned) == true) layouts.Add(assigned);
                if (framework.CssDisplayMode == CssDisplayMode.Table && CssDisplayLayout.TryTableFor(framework, out var table)) layouts.Add(table!);
                if (s_flowTables.TryGetValue(framework, out var wrappers))
                    foreach (var wrapper in wrappers) layouts.Add(CssDisplayLayout.TableFor(wrapper));
            }
            foreach (var child in current.SnapshotAttachedVisualChildren().OfType<UIElement>()) Visit(child);
        }
        Visit(element);
        for (var current = element.VisualParent; current is not null; current = current.VisualParent)
            if (current is FrameworkElement framework && framework.CssDisplayMode == CssDisplayMode.Table && CssDisplayLayout.TryTableFor(framework, out var table)) layouts.Add(table!);
        foreach (var layout in layouts) if (layout._computing == 0) layout.InvalidateOwner();
    }
}
