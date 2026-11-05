using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>Allocates the content viewport and optional scroll bars of a ScrollViewer template.</summary>
public sealed class ScrollViewerLayoutPanel : Panel
{
    private ScrollViewer? Owner
    {
        get
        {
            for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
                if (parent is ScrollViewer owner) return owner;
            return null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
        => Owner?.MeasureScrollArea(availableSize) ?? base.MeasureOverride(availableSize);

    protected override Size ArrangeOverride(Size finalSize)
        => Owner?.ArrangeScrollArea(finalSize) ?? base.ArrangeOverride(finalSize);

    internal override Geometry? GetChildLayoutClip()
        => Owner?.GetScrollAreaChildClip();

    internal override Geometry? GetAdditionalChildLayoutClip(Visual child)
        => Owner?.GetScrollAreaContentClip(child);
}
