using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>Allocates a dock panel's header strip, selected content and optional scroll bar.</summary>
public sealed class DockTabLayoutPanel : Panel
{
    private DockTabPanel? Owner
    {
        get
        {
            for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
                if (parent is DockTabPanel owner) return owner.OwnsDockLayout(this) ? owner : null;
            return null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
        => Owner?.MeasureDockArea(availableSize) ?? base.MeasureOverride(availableSize);

    protected override Size ArrangeOverride(Size finalSize)
        => Owner?.ArrangeDockArea(finalSize) ?? base.ArrangeOverride(finalSize);

    internal override Geometry? GetAdditionalChildLayoutClip(Visual child) => Owner?.GetDockHeaderClip(child);
}
