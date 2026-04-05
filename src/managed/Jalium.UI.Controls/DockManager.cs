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

    /// <summary>
    /// Sets the given panel as the active (focused) panel, deactivating all others.
    /// </summary>
    internal static void SetActivePanel(DockTabPanel activePanel)
    {
        foreach (var panel in _panels)
        {
            panel.SetPanelFocusedInternal(panel == activePanel);
        }
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
    internal static DockTabPanel? HitTestPanel(int screenX, int screenY, Window? excludeWindow)
    {
        foreach (var panel in _panels)
        {
            // Skip panels in the excluded window (the floating window being dragged)
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
    internal static DockLayout? HitTestLayout(int screenX, int screenY, Window? excludeWindow)
    {
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
    /// Gets the screen rectangle of a FrameworkElement (in physical pixels),
    /// using a pre-resolved parent window to avoid redundant visual tree walks.
    /// </summary>
    private static Rect? GetElementScreenRect(FrameworkElement element, Window? window)
    {
        if (window == null || window.Handle == nint.Zero) return null;

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
    /// Gets the screen rectangle of a FrameworkElement (in physical pixels).
    /// Uses ClientToScreen to get the exact client area origin, which aligns
    /// with TransformToAncestor(null) coordinates.
    /// </summary>
    private static Rect? GetElementScreenRect(FrameworkElement element)
    {
        return GetElementScreenRect(element, FindParentWindow(element));
    }

    /// <summary>
    /// Converts screen pixel coordinates to an element's local DIP coordinates,
    /// using a pre-resolved parent window to avoid redundant visual tree walks.
    /// </summary>
    private static Point? ScreenToLocal(FrameworkElement element, Window? window, int screenX, int screenY)
    {
        var screenRect = GetElementScreenRect(element, window);
        if (screenRect == null) return null;

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

    // Cached exclude window to avoid repeated FindParentWindow during a drag session
    private static Window? _cachedExcludeWindow;
    private static DockTabPanel? _cachedExcludePanel;

    /// <summary>
    /// Updates dock indicator state during a floating window drag.
    /// Performs hit-testing against both center cross buttons and edge buttons.
    /// Manages topmost overlay windows so indicators render above the floating window.
    /// </summary>
    internal static void UpdateHighlight(int screenX, int screenY, DockTabPanel? exclude)
    {
        // Cache the exclude window lookup for the duration of the drag
        if (exclude != _cachedExcludePanel)
        {
            _cachedExcludePanel = exclude;
            _cachedExcludeWindow = exclude != null ? FindParentWindow(exclude) : null;
        }

        var targetPanel = HitTestPanel(screenX, screenY, _cachedExcludeWindow);
        var targetLayout = HitTestLayout(screenX, screenY, _cachedExcludeWindow);

        bool panelChanged = targetPanel != _currentHighlight;
        bool layoutChanged = targetLayout != _currentEdgeLayout;

        // Update panel highlight if target changed
        if (panelChanged)
        {
            _currentHighlight = targetPanel;
            _highlightParentWindow = null; // Clear cached window for new target
            _currentDockPosition = DockPosition.None;

            // Dispose old center indicator window; will be recreated if needed
            if (_centerIndicatorWindow != null)
            {
                _centerIndicatorWindow.Dispose();
                _centerIndicatorWindow = null;
            }
        }

        // Update edge layout highlight if target changed
        if (layoutChanged)
        {
            _currentEdgeLayout = targetLayout;
            _edgeLayoutParentWindow = null; // Clear cached window for new target

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
            var edgeWindow = FindParentWindow(_currentEdgeLayout);
            var localPoint = ScreenToLocal(_currentEdgeLayout, edgeWindow, screenX, screenY);
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
            var panelWindow = FindParentWindow(_currentHighlight);
            var localPoint = ScreenToLocal(_currentHighlight, panelWindow, screenX, screenY);
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

            // NOTE: Do NOT call InvalidateVisual() on _currentHighlight or _currentEdgeLayout
            // here. DockLayout is the root container — invalidating it triggers a full-window
            // re-render of the entire visual tree (all child controls), because its dirty region
            // covers >50% of the window area and gets promoted to a full render.
            // The DockIndicatorWindow already renders dock indicators and preview overlays
            // in a separate lightweight topmost window, so the accent border is unnecessary.

            // Update indicator windows with new hovered position
            _centerIndicatorWindow?.UpdateIndicator(
                IsEdgePosition(newPosition) ? DockPosition.None : newPosition);
            _edgeIndicatorWindow?.UpdateIndicator(
                IsEdgePosition(newPosition) ? newPosition : DockPosition.None);
        }
    }

    // Cached parent windows for current highlight targets to avoid repeated lookups
    private static Window? _highlightParentWindow;
    private static Window? _edgeLayoutParentWindow;

    /// <summary>
    /// Creates or repositions the center indicator window over the current target panel.
    /// </summary>
    private static void EnsureCenterIndicatorWindow()
    {
        if (_currentHighlight == null) return;

        _highlightParentWindow ??= FindParentWindow(_currentHighlight);
        var window = _highlightParentWindow;
        if (window == null) return;

        var screenRect = GetElementScreenRect(_currentHighlight, window);
        if (screenRect == null) return;

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

        _edgeLayoutParentWindow ??= FindParentWindow(_currentEdgeLayout);
        var window = _edgeLayoutParentWindow;
        if (window == null) return;

        var screenRect = GetElementScreenRect(_currentEdgeLayout, window);
        if (screenRect == null) return;

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

        _currentHighlight = null;
        _currentEdgeLayout = null;
        _currentDockPosition = DockPosition.None;
        _cachedExcludePanel = null;
        _cachedExcludeWindow = null;
        _highlightParentWindow = null;
        _edgeLayoutParentWindow = null;
        DockIndicator.InvalidateResourceCache();

        return (panel, layout, position);
    }

    internal static void ClearHighlight()
    {
        // Clean up indicator windows
        DisposeIndicatorWindows();

        _currentHighlight = null;
        _currentEdgeLayout = null;
        _currentDockPosition = DockPosition.None;
        _cachedExcludePanel = null;
        _cachedExcludeWindow = null;
        _highlightParentWindow = null;
        _edgeLayoutParentWindow = null;
        DockIndicator.InvalidateResourceCache();
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
