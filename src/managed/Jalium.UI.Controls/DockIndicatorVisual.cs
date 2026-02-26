using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Lightweight visual element that renders dock indicator buttons on top of a panel or layout.
/// Added as the topmost visual child when dock highlighting is active.
/// </summary>
internal sealed class DockIndicatorVisual : FrameworkElement
{
    /// <summary>
    /// The dock position currently hovered by the cursor.
    /// </summary>
    internal DockPosition HoveredPosition { get; set; }

    /// <summary>
    /// Whether to render center cross buttons (for DockTabPanel).
    /// </summary>
    internal bool ShowCenterCross { get; set; }

    /// <summary>
    /// Whether to render edge buttons (for DockLayout).
    /// </summary>
    internal bool ShowEdgeButtons { get; set; }

    internal DockIndicatorVisual()
    {
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
            return;

        if (ShowCenterCross)
        {
            DockIndicator.RenderPreview(dc, ActualWidth, ActualHeight, HoveredPosition);
            DockIndicator.RenderCenterCross(dc, ActualWidth, ActualHeight, HoveredPosition);
        }

        if (ShowEdgeButtons)
        {
            // Only render preview for edge positions
            if (HoveredPosition is DockPosition.EdgeLeft or DockPosition.EdgeRight
                or DockPosition.EdgeTop or DockPosition.EdgeBottom)
            {
                DockIndicator.RenderPreview(dc, ActualWidth, ActualHeight, HoveredPosition);
            }
            DockIndicator.RenderEdgeButtons(dc, ActualWidth, ActualHeight, HoveredPosition);
        }
    }
}
