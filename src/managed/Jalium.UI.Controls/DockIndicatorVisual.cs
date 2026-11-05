using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Lightweight visual element that renders dock indicator buttons on top of a panel or layout.
/// Added as the topmost visual child when dock highlighting is active.
/// </summary>
public sealed partial class DockIndicatorVisual : Control
{
    /// <summary>
    /// The dock position currently hovered by the cursor.
    /// </summary>
    public DockPosition HoveredPosition { get => (DockPosition)GetValue(HoveredPositionProperty)!; set => SetValue(HoveredPositionProperty, value); }

    /// <summary>
    /// Whether to render center cross buttons (for DockTabPanel).
    /// </summary>
    public bool ShowCenterCross { get => (bool)GetValue(ShowCenterCrossProperty)!; set => SetValue(ShowCenterCrossProperty, value); }

    /// <summary>
    /// Whether to render edge buttons (for DockLayout).
    /// </summary>
    public bool ShowEdgeButtons { get => (bool)GetValue(ShowEdgeButtonsProperty)!; set => SetValue(ShowEdgeButtonsProperty, value); }

    public DockIndicatorVisual()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetValue(TargetsPropertyKey, Array.AsReadOnly(_targets));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 300, double.IsFinite(availableSize.Height) ? availableSize.Height : 200);
        RefreshPresentation(size);
        base.MeasureOverride(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        RefreshPresentation(finalSize);
        base.ArrangeOverride(finalSize);
        return finalSize;
    }

}
