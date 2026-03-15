using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// A special Canvas that lives at the top of Window's visual tree and hosts
/// popup overlay content. Elements in this layer render on top of all other
/// content and receive hit test priority.
/// </summary>
internal sealed class OverlayLayer : Canvas
{
    private readonly HashSet<PopupRoot> _popupRoots = [];
    private readonly HashSet<PopupRoot> _lightDismissRoots = [];
    private readonly HashSet<UIElement> _modalRoots = [];

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
    /// Returns true if any popup roots are currently hosted in the overlay.
    /// </summary>
    public bool HasPopupRoots => _popupRoots.Count > 0;

    /// <summary>
    /// Returns true when any modal overlay content is currently open.
    /// </summary>
    public bool HasModalRoots => _modalRoots.Count > 0;

    /// <summary>
    /// Adds a PopupRoot to the overlay layer.
    /// </summary>
    public void AddPopupRoot(PopupRoot root)
    {
        Children.Add(root);
        _popupRoots.Add(root);

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
        _popupRoots.Remove(root);
        _lightDismissRoots.Remove(root);
        Children.Remove(root);

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Adds a modal root that blocks interaction with content behind it.
    /// </summary>
    public void AddModalRoot(UIElement root)
    {
        if (_modalRoots.Add(root))
        {
            Children.Add(root);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Removes a previously registered modal root.
    /// </summary>
    public void RemoveModalRoot(UIElement root)
    {
        if (_modalRoots.Remove(root))
        {
            Children.Remove(root);
            InvalidateMeasure();
            InvalidateVisual();
        }
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
        return CloseLightDismissPopups() > 0;
    }

    internal int CloseLightDismissPopups()
    {
        if (_lightDismissRoots.Count == 0)
        {
            return 0;
        }

        var popupsToClose = _lightDismissRoots
            .Select(r => r.OwnerPopup)
            .Distinct()
            .ToList();
        foreach (var popup in popupsToClose)
        {
            popup.IsOpen = false;
        }

        return popupsToClose.Count;
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

        // If base returns this OverlayLayer itself (no child hit):
        // - when light-dismiss popups are open, block input passthrough so
        //   underlying controls cannot be interacted with behind the popup;
        // - otherwise keep passthrough behavior.
        if (result?.VisualHit == this)
        {
            if (HasLightDismissPopups || HasModalRoots)
            {
                return HitTestResult.GetReusable(this);
            }

            return null;
        }

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
