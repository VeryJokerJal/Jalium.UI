using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Central registry for dock panels, enabling cross-window drag-to-dock operations.
/// Tracks all <see cref="DockTabPanel"/> instances and root <see cref="DockLayout"/> containers.
/// Provides hit-testing for dock targets and manages dock indicator overlay windows.
/// </summary>
internal static partial class DockManager
{
    private static readonly List<DockTabPanel> _panels = new();
    private static readonly List<DockLayout> _layouts = new();

    #region Panel Registration

    internal static void Register(DockTabPanel panel)
    {
        if (!_panels.Contains(panel))
            _panels.Add(panel);
    }

    internal static void Unregister(DockTabPanel panel)
    {
        _panels.Remove(panel);
    }

    #endregion

    #region Root Layout Registration

    internal static void Register(DockLayout layout)
    {
        if (!_layouts.Contains(layout))
            _layouts.Add(layout);
    }

    internal static void Unregister(DockLayout layout)
    {
        _layouts.Remove(layout);
    }

    #endregion

    #region Hit-Testing

    /// <summary>
    /// Finds a <see cref="DockTabPanel"/> whose bounds contain the given screen point.
    /// Excludes panels in the same window as the excluded panel (the floating window being dragged).
    /// </summary>
    internal static DockTabPanel? HitTestPanel(int screenX, int screenY, DockTabPanel? exclude)
    {
        var excludeWindow = exclude != null ? FindParentWindow(exclude) : null;

        foreach (var panel in _panels)
        {
            if (panel == exclude) continue;

            // Skip panels in the same window as the excluded panel (the floating window being dragged)
            if (excludeWindow != null)
            {
                var panelWindow = FindParentWindow(panel);
                if (panelWindow == excludeWindow) continue;
            }

            var screenRect = GetElementScreenRect(panel);
            if (screenRect == null) continue;

            if (screenRect.Value.Contains(new Point(screenX, screenY)))
                return panel;
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="DockLayout"/> whose bounds contain the given screen point.
    /// Excludes layouts that belong to the floating window containing the excluded panel.
    /// </summary>
    internal static DockLayout? HitTestLayout(int screenX, int screenY, DockTabPanel? exclude)
    {
        var excludeWindow = exclude != null ? FindParentWindow(exclude) : null;

        foreach (var layout in _layouts)
        {
            // Skip layouts in the same window as the excluded panel (the floating window)
            var layoutWindow = FindParentWindow(layout);
            if (layoutWindow == excludeWindow) continue;

            var screenRect = GetElementScreenRect(layout);
            if (screenRect == null) continue;

            if (screenRect.Value.Contains(new Point(screenX, screenY)))
                return layout;
        }
        return null;
    }

    /// <summary>
    /// Gets the screen rectangle of a FrameworkElement (in physical pixels).
    /// Uses ClientToScreen to get the exact client area origin, which aligns
    /// with TransformToAncestor(null) coordinates.
    /// </summary>
    private static Rect? GetElementScreenRect(FrameworkElement element)
    {
        var window = FindParentWindow(element);
        if (window == null || window.Handle == nint.Zero) return null;

        // Get client area origin in screen coordinates.
        // This matches the coordinate system of TransformToAncestor(null).
        var clientOrigin = new POINT { x = 0, y = 0 };
        if (!ClientToScreen(window.Handle, ref clientOrigin))
            return null;

        var dpi = window.DpiScale;
        var localPos = element.TransformToAncestor(null);

        var screenX = clientOrigin.x + localPos.X * dpi;
        var screenY = clientOrigin.y + localPos.Y * dpi;
        var screenW = element.ActualWidth * dpi;
        var screenH = element.ActualHeight * dpi;

        return new Rect(screenX, screenY, screenW, screenH);
    }

    /// <summary>
    /// Converts screen pixel coordinates to an element's local DIP coordinates.
    /// </summary>
    private static Point? ScreenToLocal(FrameworkElement element, int screenX, int screenY)
    {
        var screenRect = GetElementScreenRect(element);
        if (screenRect == null) return null;

        var window = FindParentWindow(element);
        var dpi = window?.DpiScale ?? 1.0;

        return new Point(
            (screenX - screenRect.Value.X) / dpi,
            (screenY - screenRect.Value.Y) / dpi);
    }

    internal static Window? FindParentWindow(Visual visual)
    {
        Visual? current = visual;
        while (current != null)
        {
            if (current is Window window)
                return window;
            current = current.VisualParent;
        }
        return null;
    }

    #endregion

    #region Dock Highlight Management (Indicator Windows)

    private static DockTabPanel? _currentHighlight;
    private static DockLayout? _currentEdgeLayout;
    private static DockPosition _currentDockPosition = DockPosition.None;

    // Topmost overlay windows for rendering dock indicators above the floating window
    private static DockIndicatorWindow? _centerIndicatorWindow;
    private static DockIndicatorWindow? _edgeIndicatorWindow;

    /// <summary>
    /// Gets the current dock position being highlighted.
    /// </summary>
    internal static DockPosition CurrentDockPosition => _currentDockPosition;

    /// <summary>
    /// Updates dock indicator state during a floating window drag.
    /// Performs hit-testing against both center cross buttons and edge buttons.
    /// Manages topmost overlay windows so indicators render above the floating window.
    /// </summary>
    internal static void UpdateHighlight(int screenX, int screenY, DockTabPanel? exclude)
    {
        var targetPanel = HitTestPanel(screenX, screenY, exclude);
        var targetLayout = HitTestLayout(screenX, screenY, exclude);

        // Update panel highlight if target changed
        if (targetPanel != _currentHighlight)
        {
            if (_currentHighlight != null)
            {
                _currentHighlight.IsDockHighlighted = false;
                _currentHighlight.InvalidateVisual();
            }
            _currentHighlight = targetPanel;
            _currentDockPosition = DockPosition.None;

            // Dispose old center indicator window; will be recreated if needed
            if (_centerIndicatorWindow != null)
            {
                _centerIndicatorWindow.Dispose();
                _centerIndicatorWindow = null;
            }
        }

        // Update edge layout highlight if target changed
        if (targetLayout != _currentEdgeLayout)
        {
            if (_currentEdgeLayout != null)
            {
                _currentEdgeLayout.IsDockHighlighted = false;
                _currentEdgeLayout.InvalidateVisual();
            }
            _currentEdgeLayout = targetLayout;

            // Dispose old edge indicator window; will be recreated if needed
            if (_edgeIndicatorWindow != null)
            {
                _edgeIndicatorWindow.Dispose();
                _edgeIndicatorWindow = null;
            }
        }

        // Ensure indicator windows exist for current targets
        EnsureCenterIndicatorWindow();
        EnsureEdgeIndicatorWindow();

        DockPosition newPosition = DockPosition.None;

        // First: check edge buttons on the root layout (higher priority for edge areas)
        if (_currentEdgeLayout != null)
        {
            var localPoint = ScreenToLocal(_currentEdgeLayout, screenX, screenY);
            if (localPoint.HasValue)
            {
                newPosition = DockIndicator.HitTestEdge(
                    _currentEdgeLayout.ActualWidth,
                    _currentEdgeLayout.ActualHeight,
                    localPoint.Value);
            }
        }

        // Second: check center cross buttons on the target panel
        if (newPosition == DockPosition.None && _currentHighlight != null)
        {
            var localPoint = ScreenToLocal(_currentHighlight, screenX, screenY);
            if (localPoint.HasValue)
            {
                newPosition = DockIndicator.HitTestCenter(
                    _currentHighlight.ActualWidth,
                    _currentHighlight.ActualHeight,
                    localPoint.Value);
            }
        }

        // Update state if dock position changed
        if (newPosition != _currentDockPosition)
        {
            _currentDockPosition = newPosition;

            // Update highlight border on target panel
            if (_currentHighlight != null)
            {
                _currentHighlight.IsDockHighlighted = true;
                _currentHighlight.InvalidateVisual();
            }

            // Update highlight border on edge layout
            if (_currentEdgeLayout != null)
            {
                _currentEdgeLayout.IsDockHighlighted = true;
                _currentEdgeLayout.InvalidateVisual();
            }

            // Update indicator windows with new hovered position
            _centerIndicatorWindow?.UpdateIndicator(
                IsEdgePosition(newPosition) ? DockPosition.None : newPosition);
            _edgeIndicatorWindow?.UpdateIndicator(
                IsEdgePosition(newPosition) ? newPosition : DockPosition.None);
        }
    }

    /// <summary>
    /// Creates or repositions the center indicator window over the current target panel.
    /// </summary>
    private static void EnsureCenterIndicatorWindow()
    {
        if (_currentHighlight == null) return;

        var screenRect = GetElementScreenRect(_currentHighlight);
        if (screenRect == null) return;

        var window = FindParentWindow(_currentHighlight);
        if (window == null) return;
        var dpi = window.DpiScale;

        var sx = (int)screenRect.Value.X;
        var sy = (int)screenRect.Value.Y;
        var sw = (int)screenRect.Value.Width;
        var sh = (int)screenRect.Value.Height;

        if (_centerIndicatorWindow == null)
        {
            _centerIndicatorWindow = new DockIndicatorWindow(showCenterCross: true, showEdgeButtons: false);
            _centerIndicatorWindow.Show(window.Handle, sx, sy, sw, sh, dpi);
        }
        else
        {
            _centerIndicatorWindow.MoveTo(sx, sy, sw, sh);
        }
    }

    /// <summary>
    /// Creates or repositions the edge indicator window over the current target layout.
    /// </summary>
    private static void EnsureEdgeIndicatorWindow()
    {
        if (_currentEdgeLayout == null) return;

        var screenRect = GetElementScreenRect(_currentEdgeLayout);
        if (screenRect == null) return;

        var window = FindParentWindow(_currentEdgeLayout);
        if (window == null) return;
        var dpi = window.DpiScale;

        var sx = (int)screenRect.Value.X;
        var sy = (int)screenRect.Value.Y;
        var sw = (int)screenRect.Value.Width;
        var sh = (int)screenRect.Value.Height;

        if (_edgeIndicatorWindow == null)
        {
            _edgeIndicatorWindow = new DockIndicatorWindow(showCenterCross: false, showEdgeButtons: true);
            _edgeIndicatorWindow.Show(window.Handle, sx, sy, sw, sh, dpi);
        }
        else
        {
            _edgeIndicatorWindow.MoveTo(sx, sy, sw, sh);
        }
    }

    /// <summary>
    /// Completes the highlight operation and returns the target panel, root layout, and dock position.
    /// Disposes indicator windows.
    /// </summary>
    internal static (DockTabPanel? panel, DockLayout? layout, DockPosition position) FinishHighlight()
    {
        var panel = _currentHighlight;
        var layout = _currentEdgeLayout;
        var position = _currentDockPosition;

        // Clean up indicator windows
        DisposeIndicatorWindows();

        if (_currentHighlight != null)
        {
            _currentHighlight.IsDockHighlighted = false;
            _currentHighlight.InvalidateVisual();
            _currentHighlight = null;
        }

        if (_currentEdgeLayout != null)
        {
            _currentEdgeLayout.IsDockHighlighted = false;
            _currentEdgeLayout.InvalidateVisual();
            _currentEdgeLayout = null;
        }

        _currentDockPosition = DockPosition.None;

        return (panel, layout, position);
    }

    internal static void ClearHighlight()
    {
        // Clean up indicator windows
        DisposeIndicatorWindows();

        if (_currentHighlight != null)
        {
            _currentHighlight.IsDockHighlighted = false;
            _currentHighlight.InvalidateVisual();
            _currentHighlight = null;
        }

        if (_currentEdgeLayout != null)
        {
            _currentEdgeLayout.IsDockHighlighted = false;
            _currentEdgeLayout.InvalidateVisual();
            _currentEdgeLayout = null;
        }

        _currentDockPosition = DockPosition.None;
    }

    private static void DisposeIndicatorWindows()
    {
        if (_centerIndicatorWindow != null)
        {
            _centerIndicatorWindow.Dispose();
            _centerIndicatorWindow = null;
        }

        if (_edgeIndicatorWindow != null)
        {
            _edgeIndicatorWindow.Dispose();
            _edgeIndicatorWindow = null;
        }
    }

    private static bool IsEdgePosition(DockPosition position)
    {
        return position is DockPosition.EdgeLeft or DockPosition.EdgeRight
            or DockPosition.EdgeTop or DockPosition.EdgeBottom;
    }

    #endregion

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x, y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);


    #endregion
}
