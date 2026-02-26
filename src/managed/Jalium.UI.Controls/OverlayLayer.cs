using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// A special Canvas that lives at the top of Window's visual tree and hosts
/// popup overlay content. Elements in this layer render on top of all other
/// content and receive hit test priority.
/// </summary>
internal sealed class OverlayLayer : Canvas
{
    private readonly HashSet<PopupRoot> _lightDismissRoots = [];

    public OverlayLayer()
    {
        // Overlay content should not be clipped — shadows can bleed beyond bounds
        ClipToBounds = false;
        IsHitTestVisible = true;
    }

    /// <summary>
    /// Returns true if any light-dismiss popups are currently open.
    /// </summary>
    public bool HasLightDismissPopups => _lightDismissRoots.Count > 0;

    /// <summary>
    /// Adds a PopupRoot to the overlay layer.
    /// </summary>
    public void AddPopupRoot(PopupRoot root)
    {
        Children.Add(root);

        if (root.IsLightDismiss)
        {
            _lightDismissRoots.Add(root);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Removes a PopupRoot from the overlay layer.
    /// </summary>
    public void RemovePopupRoot(PopupRoot root)
    {
        _lightDismissRoots.Remove(root);
        Children.Remove(root);

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Handles light dismiss logic. Called by Window on mouse down.
    /// Returns true if the click was consumed by light dismiss (i.e., clicked outside all popups).
    /// </summary>
    public bool TryHandleLightDismiss(Point windowPosition)
    {
        if (_lightDismissRoots.Count == 0) return false;

        // Check if click is inside any popup root
        foreach (var root in _lightDismissRoots)
        {
            // Use Canvas.Left/Top + actual size for robust bounds calculation,
            // falling back to VisualBounds if Canvas properties are not set.
            var left = GetLeft(root);
            var top = GetTop(root);
            Rect rootBounds;

            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                var w = root.ActualWidth > 0 ? root.ActualWidth : root.DesiredSize.Width;
                var h = root.ActualHeight > 0 ? root.ActualHeight : root.DesiredSize.Height;
                rootBounds = new Rect(left, top, w, h);
            }
            else
            {
                rootBounds = root.VisualBounds;
            }

            if (rootBounds.Contains(windowPosition))
                return false; // Click is inside a popup — don't dismiss
        }

        // Click is outside all light-dismiss popups — close them
        var popupsToClose = _lightDismissRoots.Select(r => r.OwnerPopup).ToList();
        foreach (var popup in popupsToClose)
        {
            popup.IsOpen = false;
        }

        return true;
    }

    /// <summary>
    /// Hit test override: returns null when no children exist at the point,
    /// allowing clicks to pass through to underlying content.
    /// </summary>
    protected override HitTestResult? HitTestCore(Point point)
    {
        if (Children.Count == 0) return null;

        // Delegate to base Canvas hit testing (checks children in reverse order)
        var result = base.HitTestCore(point);

        // If base returns this OverlayLayer itself (no child hit), return null
        // so clicks pass through to underlying content
        if (result?.VisualHit == this) return null;

        return result;
    }

    /// <summary>
    /// OverlayLayer does not consume layout space.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        // Measure all children with infinite space (they position themselves absolutely)
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        return Size.Empty;
    }
}
