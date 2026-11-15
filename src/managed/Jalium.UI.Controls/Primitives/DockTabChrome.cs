using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>Paints the rounded dock frame or its selected-tab join above template content.</summary>
public sealed class DockTabChrome : FrameworkElement
{
    public static readonly DependencyProperty IsOverlayProperty = DependencyProperty.Register(
        nameof(IsOverlay), typeof(bool), typeof(DockTabChrome),
        new PropertyMetadata(false, (d, _) => ((DockTabChrome)d).InvalidateVisual()));

    public bool IsOverlay
    {
        get => (bool)GetValue(IsOverlayProperty)!;
        set => SetValue(IsOverlayProperty, value);
    }

    private Brush[] _brushes = [];

    internal void RefreshBrushes(IEnumerable<Brush> brushes)
    {
        var next = brushes.Where(brush => !brush.IsFrozen).Distinct(ReferenceEqualityComparer.Instance).Cast<Brush>().ToArray();
        foreach (var brush in _brushes)
            if (!next.Contains(brush)) brush.RemoveRenderOwner(this);
        foreach (var brush in next)
            if (!_brushes.Contains(brush)) brush.AddRenderOwner(this);
        _brushes = next;
    }

    internal void ReleaseBrushes()
    {
        foreach (var brush in _brushes) brush.RemoveRenderOwner(this);
        _brushes = [];
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
            if (parent is DockTabPanel owner)
            {
                if (owner.OwnsDockChrome(this))
                {
                    if (IsOverlay) owner.RenderDockOverlay(drawingContext);
                    else owner.RenderDockBackground(drawingContext);
                }
                return;
            }
    }
}
