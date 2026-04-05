using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a dockable panel item with a header and content.
/// Used as a child of <see cref="DockTabPanel"/>.
/// Supports drag-to-float: dragging a tab header beyond a threshold tears it off into a floating window.
/// </summary>
public partial class DockItem : HeaderedContentControl
{
    private const string HeaderEllipsis = "...";
    private const double DefaultSelectedTabCornerRadius = 8.0;

    // Cached brushes and pens for OnRender (OneTheme-aligned deep blue-gray palette)
    private static readonly SolidColorBrush s_fallbackSelectedBackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly SolidColorBrush s_fallbackHoverBackgroundBrush = new(Color.FromRgb(0x22, 0x22, 0x34));
    private static readonly SolidColorBrush s_transparentBrush = new(Color.Transparent);
    private static readonly SolidColorBrush s_fallbackActiveTextBrush = new(Color.FromRgb(0xCC, 0xCC, 0xD6));
    private static readonly SolidColorBrush s_fallbackInactiveTextBrush = new(Color.FromRgb(0x6E, 0x6E, 0x86));
    private static readonly SolidColorBrush s_fallbackCloseButtonHoverBrush = new(Color.FromRgb(0x2C, 0x2C, 0x44));
    private static readonly SolidColorBrush s_fallbackCloseButtonPressedBrush = new(Color.FromRgb(0x38, 0x38, 0x54));
    private static readonly SolidColorBrush s_fallbackIndicatorBrush = new(Color.FromRgb(0x7A, 0xA2, 0xF7));
    private static readonly SolidColorBrush s_fallbackDragDimBrush = new(Color.FromArgb(80, 0, 0, 0));
    private static readonly SolidColorBrush s_fallbackWindowBackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x2E));

    internal DockTabPanel? OwnerPanel { get; set; }

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DockItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanCloseProperty =
        DependencyProperty.Register(nameof(CanClose), typeof(bool), typeof(DockItem),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanFloatProperty =
        DependencyProperty.Register(nameof(CanFloat), typeof(bool), typeof(DockItem),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedBackgroundProperty =
        DependencyProperty.Register(nameof(SelectedBackground), typeof(Brush), typeof(DockItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.Register(nameof(HoverBackground), typeof(Brush), typeof(DockItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty IndicatorBrushProperty =
        DependencyProperty.Register(nameof(IndicatorBrush), typeof(Brush), typeof(DockItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanClose
    {
        get => (bool)GetValue(CanCloseProperty)!;
        set => SetValue(CanCloseProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanFloat
    {
        get => (bool)(GetValue(CanFloatProperty) ?? true);
        set => SetValue(CanFloatProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public Brush? SelectedBackground
    {
        get => (Brush?)GetValue(SelectedBackgroundProperty);
        set => SetValue(SelectedBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? HoverBackground
    {
        get => (Brush?)GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? IndicatorBrush
    {
        get => (Brush?)GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    #endregion

    #region Close Button State

    private bool _isCloseButtonHovered;
    private bool _isCloseButtonPressed;
    private Rect _closeButtonRect;

    #endregion

    #region Drag State

    private bool _isMouseDown;
    private Point _mouseDownPos; // Position in OwnerPanel's coordinate space
    private const double DragThreshold = 8.0;

    // Reorder preview drag state
    private bool _isReorderDragging;
    private readonly TranslateTransform _reorderDragTransform = new();
    private bool _hasReorderVisualState;
    private object? _reorderOriginalRenderTransform;
    private double _reorderOriginalOpacity = 1.0;
    private int _reorderOriginalZIndex;

    // Floating window drag state (used when dragging a tab in a floating window)
    private bool _isDraggingFloatingWindow;
    private Window? _floatingDragWindow;
    private POINT _floatingDragStartCursor;
    private RECT _floatingDragStartWindowRect;
    private FloatingRestoreContext? _floatingRestoreContext;
    private Window? _floatingWindowOwner;
    private EventHandler<System.ComponentModel.CancelEventArgs>? _floatingWindowClosingHandler;
    private bool _suppressRestoreOnFloatingWindowClose;

    // Cached pens
    private Pen? _closeButtonPen;
    private Brush? _closeButtonPenBrush;
    private Pen? _dragIndicatorPen;
    private Brush? _dragIndicatorPenBrush;

    #endregion

    public DockItem()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
    }

    /// <summary>
    /// Do NOT add Content as visual child 閳?DockTabPanel manages content display.
    /// When content changes, notify OwnerPanel to release the old content and adopt the new one.
    /// </summary>
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        if (oldContent is UIElement oldElement)
            OwnerPanel?.ReleaseContentElement(oldElement);

        // If this item is currently selected, tell the panel to adopt the new content
        if (IsSelected)
            OwnerPanel?.AdoptContentForSelectedItem(this);

        InvalidateMeasure();
    }

    public override int VisualChildrenCount => 0;

    public override Visual? GetVisualChild(int index)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockItem item)
            item.InvalidateVisual();
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockItem item)
            item.InvalidateVisual();
    }

    private bool CanDragFloatingWindow()
    {
        return CanFloat
            && OwnerPanel != null
            && OwnerPanel.IsFloating
            && OwnerPanel.Items.Count <= 1;
    }

    private bool CanTearOffToFloatingWindow()
    {
        if (!CanFloat)
            return false;

        var panel = OwnerPanel;
        if (panel == null || panel.IsFloating)
            return true;

        var layout = FindParentDockLayout(panel);
        return layout?.CanFloat ?? true;
    }

    private static DockLayout? FindParentDockLayout(Visual visual)
    {
        Visual? current = visual;
        while (current != null)
        {
            if (current is DockLayout layout)
                return layout;
            current = current.VisualParent;
        }
        return null;
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // Check if clicking the close button
            if (CanClose && _closeButtonRect.Width > 0)
            {
                var pos = e.GetPosition(this);
                if (_closeButtonRect.Contains(pos))
                {
                    _isCloseButtonPressed = true;
                    _isCloseButtonHovered = true;
                    CaptureMouse();
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            // Select tab, activate panel focus, and begin potential drag
            OwnerPanel?.SelectTab(this);
            if (OwnerPanel != null)
                DockManager.SetActivePanel(OwnerPanel);
            _isMouseDown = true;
            // Track in OwnerPanel's coordinate space 閳?stable during tab reorder
            _mouseDownPos = e.GetPosition((UIElement?)OwnerPanel ?? this);
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_isCloseButtonPressed)
            {
                var pos = e.GetPosition(this);
                var shouldClose = _closeButtonRect.Contains(pos);

                _isCloseButtonPressed = false;
                _isCloseButtonHovered = shouldClose;
                if (IsMouseCaptured)
                    ReleaseMouseCapture();

                InvalidateVisual();

                if (shouldClose)
                    OwnerPanel?.CloseItem(this);

                e.Handled = true;
                return;
            }

            if (_isDraggingFloatingWindow)
            {
                FinishFloatingWindowDrag();
                e.Handled = true;
                return;
            }

            if (_isReorderDragging)
            {
                FinishReorderDrag();
                e.Handled = true;
                return;
            }

            _isMouseDown = false;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        // Floating window drag 閳?move the window and update dock highlights
        if (_isDraggingFloatingWindow && _floatingDragWindow != null)
        {
            GetCursorPos(out var cursor);

            var dx = cursor.X - _floatingDragStartCursor.X;
            var dy = cursor.Y - _floatingDragStartCursor.Y;

            SetWindowPos(_floatingDragWindow.Handle, nint.Zero,
                _floatingDragStartWindowRect.left + dx,
                _floatingDragStartWindowRect.top + dy,
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

            // Update dock target highlight
            DockManager.UpdateHighlight(cursor.X, cursor.Y, OwnerPanel);

            e.Handled = true;
            return;
        }

        // Reorder preview drag 閳?update insertion indicator position
        if (_isReorderDragging && OwnerPanel != null)
        {
            if (e.LeftButton == MouseButtonState.Released)
            {
                CancelReorderDrag();
                return;
            }

            var posInPanel = e.GetPosition((UIElement)OwnerPanel);
            UpdateReorderDragVisual(posInPanel);

            // Escape from the tab-strip band: if cursor moves too far away on the cross-axis,
            // switch from reorder preview to tear-off behavior.
            var tabStripRect = OwnerPanel.GetTabStripInteractionRect();
            var escapedStrip = OwnerPanel.IsVerticalTabStrip
                ? (posInPanel.X < tabStripRect.X - 20 || posInPanel.X > tabStripRect.Right + 20)
                : (posInPanel.Y < tabStripRect.Y - 20 || posInPanel.Y > tabStripRect.Bottom + 20);
            if (escapedStrip)
            {
                CancelReorderDrag();
                if (CanDragFloatingWindow())
                    StartFloatingWindowDrag();
                else if (CanTearOffToFloatingWindow())
                    TearOffToFloatingWindow();

                e.Handled = true;
                return;
            }

            // Update the insertion indicator position
            var insertIndex = OwnerPanel.CalculateReorderInsertIndex(posInPanel);
            OwnerPanel.ReorderInsertIndex = insertIndex;

            e.Handled = true;
            return;
        }

        // Close button hover tracking
        if (CanClose && _closeButtonRect.Width > 0)
        {
            var pos = e.GetPosition(this);
            var newHover = _closeButtonRect.Contains(pos);
            if (newHover != _isCloseButtonHovered)
            {
                _isCloseButtonHovered = newHover;
                InvalidateVisual();
            }

            // While pressing close, do not enter tab drag/select behavior.
            if (_isCloseButtonPressed)
            {
                e.Handled = true;
                return;
            }
        }

        // Initial drag detection (threshold check)
        if (_isMouseDown && OwnerPanel != null)
        {
            // Guard: if left button was released without us seeing MouseUp, cancel drag
            if (e.LeftButton == MouseButtonState.Released)
            {
                _isMouseDown = false;
                return;
            }

            var referenceElement = (UIElement?)OwnerPanel ?? this;
            var currentPos = e.GetPosition(referenceElement);
            var dx = currentPos.X - _mouseDownPos.X;
            var dy = currentPos.Y - _mouseDownPos.Y;

            if (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)
            {
                _isMouseDown = false;

                // Primary-axis drag (X for horizontal strip, Y for vertical strip) enters reorder mode.
                var isPrimaryDrag = OwnerPanel.IsVerticalTabStrip
                    ? Math.Abs(dy) > Math.Abs(dx)
                    : Math.Abs(dx) > Math.Abs(dy);
                if (isPrimaryDrag && OwnerPanel.Items.Count > 1)
                {
                    StartReorderDrag();
                }
                // Cross-axis drag tears off / moves floating window.
                else if (CanDragFloatingWindow())
                {
                    StartFloatingWindowDrag();
                }
                else if (CanTearOffToFloatingWindow())
                {
                    TearOffToFloatingWindow();
                }

                e.Handled = true;
            }
        }
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        if (_isCloseButtonHovered)
        {
            _isCloseButtonHovered = false;
            InvalidateVisual();
        }
    }

    private void BeginReorderDragVisual()
    {
        if (_hasReorderVisualState)
            return;

        _reorderOriginalRenderTransform = RenderTransform;
        _reorderOriginalOpacity = Opacity;
        _reorderOriginalZIndex = Panel.GetZIndex(this);
        _hasReorderVisualState = true;

        _reorderDragTransform.X = 0;
        _reorderDragTransform.Y = 0;
        RenderTransform = _reorderDragTransform;
        Opacity = Math.Min(_reorderOriginalOpacity, 0.94);
        Panel.SetZIndex(this, 10000);
    }

    private void UpdateReorderDragVisual(Point panelPosition)
    {
        if (OwnerPanel == null || !_hasReorderVisualState)
            return;

        var deltaX = panelPosition.X - _mouseDownPos.X;
        var deltaY = panelPosition.Y - _mouseDownPos.Y;

        // Keep drag preview inside the strip flow direction to avoid jumpy visuals.
        if (OwnerPanel.IsVerticalTabStrip)
        {
            _reorderDragTransform.X = 0;
            _reorderDragTransform.Y = deltaY;
        }
        else
        {
            _reorderDragTransform.X = deltaX;
            _reorderDragTransform.Y = 0;
        }
    }

    private void EndReorderDragVisual()
    {
        _reorderDragTransform.X = 0;
        _reorderDragTransform.Y = 0;

        if (!_hasReorderVisualState)
            return;

        RenderTransform = _reorderOriginalRenderTransform;
        Opacity = _reorderOriginalOpacity;
        Panel.SetZIndex(this, _reorderOriginalZIndex);
        _hasReorderVisualState = false;
    }

    /// <summary>
    /// Enters reorder preview mode. Captures the mouse and records which tab is being dragged.
    /// The tab follows the cursor along the strip while the move target is tracked.
    /// </summary>
    private void StartReorderDrag()
    {
        if (OwnerPanel == null) return;

        var index = OwnerPanel.Items.IndexOf(this);
        if (index < 0) return;

        _isReorderDragging = true;
        CaptureMouse();
        BeginReorderDragVisual();

        OwnerPanel.ReorderDragItemIndex = index;
        OwnerPanel.ReorderInsertIndex = -1;
    }

    /// <summary>
    /// Finishes reorder preview and commits the tab move to the tracked insertion position.
    /// </summary>
    private void FinishReorderDrag()
    {
        _isReorderDragging = false;
        EndReorderDragVisual();
        ReleaseMouseCapture();

        if (OwnerPanel == null) return;

        var currentIndex = OwnerPanel.ReorderDragItemIndex;
        var insertIndex = OwnerPanel.ReorderInsertIndex;

        // Clear reorder state
        OwnerPanel.ReorderDragItemIndex = -1;
        OwnerPanel.ReorderInsertIndex = -1;

        // Perform the move
        if (insertIndex >= 0 && currentIndex >= 0
            && insertIndex != currentIndex && insertIndex != currentIndex + 1)
        {
            // After removing from currentIndex, indices shift:
            // if inserting after the original position, subtract 1
            var targetIndex = insertIndex > currentIndex ? insertIndex - 1 : insertIndex;
            targetIndex = Math.Clamp(OwnerPanel.Items.Count - 1, 0, targetIndex);
            OwnerPanel.MoveItem(currentIndex, targetIndex);
        }
    }

    /// <summary>
    /// Cancels the reorder preview and cleans up state.
    /// </summary>
    private void CancelReorderDrag()
    {
        _isReorderDragging = false;
        EndReorderDragVisual();
        ReleaseMouseCapture();

        if (OwnerPanel != null)
        {
            OwnerPanel.ReorderDragItemIndex = -1;
            OwnerPanel.ReorderInsertIndex = -1;
        }
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();

        if (_isReorderDragging)
            CancelReorderDrag();

        if (_isDraggingFloatingWindow)
        {
            _isDraggingFloatingWindow = false;
            DockManager.ClearHighlight();
            _floatingDragWindow = null;
        }
    }

    /// <summary>
    /// Starts dragging the floating window that contains this DockItem.
    /// The window follows the mouse cursor, and dock target highlights are shown.
    /// </summary>
    private void StartFloatingWindowDrag()
    {
        _floatingDragWindow = DockManager.FindParentWindow(this);
        if (_floatingDragWindow == null) return;

        CaptureMouse();
        _isDraggingFloatingWindow = true;

        GetCursorPos(out _floatingDragStartCursor);
        GetWindowRect(_floatingDragWindow.Handle, out _floatingDragStartWindowRect);
    }

    /// <summary>
    /// Called on a newly created DockItem to immediately begin floating window drag.
    /// Allows seamless transition from tear-off to window dragging.
    /// </summary>
    internal void BeginFloatingDrag(Window floatingWindow)
    {
        if (floatingWindow == null) return;

        _floatingDragWindow = floatingWindow;
        _isDraggingFloatingWindow = true;

        // Capture initial positions before CaptureMouse(), which may trigger
        // re-entrant mouse events that clear _floatingDragWindow.
        GetCursorPos(out _floatingDragStartCursor);
        GetWindowRect(floatingWindow.Handle, out _floatingDragStartWindowRect);
        CaptureMouse();
    }

    /// <summary>
    /// Finishes the floating window drag. If the cursor is over a dock target,
    /// transfers the content back to that panel and closes the floating window.
    /// </summary>
    private void FinishFloatingWindowDrag()
    {
        _isDraggingFloatingWindow = false;
        ReleaseMouseCapture();

        // Final position update with the actual cursor location at release time,
        // so a stale _currentDockPosition from an earlier mousemove doesn't cause
        // an unintended dock when the cursor has since moved off the indicator button.
        GetCursorPos(out var finalCursor);
        DockManager.UpdateHighlight(finalCursor.X, finalCursor.Y, OwnerPanel);

        var (targetPanel, targetLayout, dockPosition) = DockManager.FinishHighlight();

        if (_floatingDragWindow != null && dockPosition != DockPosition.None)
        {
            // Extract content from floating window
            var content = Content;
            var headerText = Header?.ToString() ?? "Panel";
            var floatingPanel = OwnerPanel;
            Content = null;

            // Defensive: ensure content is fully detached from the floating panel
            if (content is UIElement ce && ce.VisualParent is DockTabPanel oldPanel)
                oldPanel.ReleaseContentElement(ce);

            var windowToClose = _floatingDragWindow;
            _floatingDragWindow = null;

            // Create the new DockItem for the target location
            var newItem = CreateTransferredDockItem(content, headerText);

            bool dockSucceeded = false;

            try
            {
                switch (dockPosition)
                {
                    case DockPosition.Center:
                        if (targetPanel != null)
                        {
                            targetPanel.Items.Add(newItem);
                            targetPanel.SelectTab(newItem);
                            dockSucceeded = true;
                        }
                        break;

                    case DockPosition.Left:
                    case DockPosition.Right:
                    case DockPosition.Top:
                    case DockPosition.Bottom:
                        if (targetPanel != null)
                            dockSucceeded = DockToSide(targetPanel, newItem, dockPosition);
                        break;

                    case DockPosition.EdgeLeft:
                    case DockPosition.EdgeRight:
                    case DockPosition.EdgeTop:
                    case DockPosition.EdgeBottom:
                        if (targetLayout != null)
                            dockSucceeded = DockToEdge(targetLayout, newItem, dockPosition);
                        break;
                }
            }
            catch
            {
                dockSucceeded = false;
            }

            if (dockSucceeded)
            {
                // Clean up floating panel before closing window
                if (floatingPanel != null)
                    DockManager.Unregister(floatingPanel);
                SuppressFloatingWindowRestore();
                windowToClose.Close();
            }
            else
            {
                // Dock failed 閳?restore content to floating window to prevent content loss
                newItem.Content = null;
                Content = content;
                _floatingDragWindow = windowToClose;
            }
        }
        else
        {
            _floatingDragWindow = null;
        }
    }

    /// <summary>
    /// Docks a new item alongside a target panel by splitting it.
    /// </summary>
    private static bool DockToSide(DockTabPanel targetPanel, DockItem newItem, DockPosition position)
    {
        var newTabPanel = CreateCompatibleDockTabPanel(targetPanel);
        newTabPanel.Items.Add(newItem);

        var parent = targetPanel.VisualParent;

        if (parent is DockSplitPanel splitPanel)
        {
            splitPanel.InsertPane(targetPanel, newTabPanel, position);
            return true;
        }
        else if (parent is ContentControl contentParent)
        {
            // Target is direct child of DockLayout or Window 閳?wrap in a new split panel
            contentParent.Content = null;

            var orientation = position is DockPosition.Left or DockPosition.Right
                ? Orientation.Horizontal
                : Orientation.Vertical;

            var wrapper = new DockSplitPanel { Orientation = orientation };
            var insertBefore = position is DockPosition.Left or DockPosition.Top;

            if (insertBefore)
            {
                wrapper.Children.Add(newTabPanel);
                wrapper.Children.Add(targetPanel);
            }
            else
            {
                wrapper.Children.Add(targetPanel);
                wrapper.Children.Add(newTabPanel);
            }

            contentParent.Content = wrapper;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Docks a new item at the edge of the root layout.
    /// </summary>
    private static bool DockToEdge(DockLayout layout, DockItem newItem, DockPosition position)
    {
        var canonicalPos = position switch
        {
            DockPosition.EdgeLeft => DockPosition.Left,
            DockPosition.EdgeRight => DockPosition.Right,
            DockPosition.EdgeTop => DockPosition.Top,
            DockPosition.EdgeBottom => DockPosition.Bottom,
            _ => position,
        };

        var orientation = canonicalPos is DockPosition.Left or DockPosition.Right
            ? Orientation.Horizontal
            : Orientation.Vertical;

        var insertBefore = canonicalPos is DockPosition.Left or DockPosition.Top;

        var existingContent = layout.Content as UIElement;
        var styleSource = FindFirstDockTabPanel(existingContent);
        var newTabPanel = CreateCompatibleDockTabPanel(styleSource);
        newTabPanel.Items.Add(newItem);
        layout.Content = null;

        if (existingContent is DockSplitPanel existingSplit && existingSplit.Orientation == orientation)
        {
            // Same orientation 閳?insert at the edge
            if (insertBefore)
                existingSplit.Children.Insert(0, newTabPanel);
            else
                existingSplit.Children.Add(newTabPanel);

            DockSplitPanel.SetSize(newTabPanel, new GridLength(200, GridUnitType.Pixel));
            layout.Content = existingSplit;
        }
        else
        {
            // Wrap in a new split panel
            var wrapper = new DockSplitPanel { Orientation = orientation };
            DockSplitPanel.SetSize(newTabPanel, new GridLength(200, GridUnitType.Pixel));

            if (insertBefore)
            {
                wrapper.Children.Add(newTabPanel);
                if (existingContent != null) wrapper.Children.Add(existingContent);
            }
            else
            {
                if (existingContent != null) wrapper.Children.Add(existingContent);
                wrapper.Children.Add(newTabPanel);
            }

            layout.Content = wrapper;
        }

        return true;
    }

    /// <summary>
    /// Tears this DockItem off from its parent DockTabPanel into a floating window.
    /// </summary>
    private void TearOffToFloatingWindow()
    {
        if (!CanTearOffToFloatingWindow())
            return;

        var panel = OwnerPanel;
        if (panel == null) return;
        var restoreContext = CaptureFloatingRestoreContext(panel, this);

        // Capture the content and header before removing from panel
        var content = Content;
        var headerText = Header?.ToString() ?? "Panel";

        // Remove content from the DockItem so it can be re-parented
        Content = null;

        // Remove this DockItem from the panel
        panel.CloseItem(this);

        // Get screen cursor position for window placement
        GetCursorPos(out var cursorPt);

        // Create floating window
        var floatingWindow = new Window
        {
            Title = headerText,
            Width = 400,
            Height = 300,
            Background = ResolveBrush("OneBackgroundPrimary", "WindowBackground", s_fallbackWindowBackgroundBrush),
            SystemBackdrop = WindowBackdropType.Mica
        };

        // Wrap content in a DockTabPanel for consistent look
        var tabPanel = CreateCompatibleDockTabPanel(panel);
        tabPanel.IsFloating = true;
        var newItem = CreateTransferredDockItem(content, headerText);
        tabPanel.Items.Add(newItem);
        floatingWindow.Content = tabPanel;
        if (!newItem.CanClose)
            newItem.AttachFloatingWindowRestoreHandler(floatingWindow, restoreContext);

        floatingWindow.Show();

        // Position the window so the cursor lands on the tab header area
        if (floatingWindow.Handle != nint.Zero)
        {
            var dpi = floatingWindow.DpiScale;
            var titleBarHeight = 32.0; // Default window title bar height in DIPs
            var tabStripMidY = titleBarHeight + tabPanel.TabStripHeight / 2;
            var tabMidX = 40.0; // Approximate cursor offset into the tab text

            var physX = (int)(cursorPt.X - tabMidX * dpi);
            var physY = (int)(cursorPt.Y - tabStripMidY * dpi);
            var physW = (int)(400 * dpi);
            var physH = (int)(300 * dpi);
            SetWindowPos(floatingWindow.Handle, nint.Zero,
                physX, physY, physW, physH,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        // Immediately start floating drag on the new DockItem so user can
        // seamlessly continue dragging the floating window without releasing the mouse
        newItem.BeginFloatingDrag(floatingWindow);
    }

    private static DockTabPanel CreateCompatibleDockTabPanel(DockTabPanel? source)
    {
        var panel = new DockTabPanel();
        if (source == null)
            return panel;

        panel.TabStripPlacement = source.TabStripPlacement;
        panel.TabStripHeight = source.TabStripHeight;
        panel.TabStripBackground = source.TabStripBackground;
        panel.TabStripBorderBrush = source.TabStripBorderBrush;
        return panel;
    }

    private static DockTabPanel? FindFirstDockTabPanel(UIElement? root)
    {
        if (root is DockTabPanel tabPanel)
            return tabPanel;
        if (root is not DockSplitPanel splitPanel)
            return null;

        foreach (var child in splitPanel.Children)
        {
            if (FindFirstDockTabPanel(child) is DockTabPanel found)
                return found;
        }

        return null;
    }

    private DockItem CreateTransferredDockItem(object? content, string headerText)
    {
        return new DockItem
        {
            Header = headerText,
            Content = content,
            CanClose = CanClose,
            CanFloat = CanFloat,
            SelectedBackground = SelectedBackground,
            HoverBackground = HoverBackground,
            IndicatorBrush = IndicatorBrush,
            Foreground = Foreground,
            FontSize = FontSize,
            FontFamily = FontFamily,
            Padding = Padding
        };
    }

    private static FloatingRestoreContext CaptureFloatingRestoreContext(DockTabPanel sourcePanel, DockItem sourceItem)
    {
        var sourceTabIndex = sourcePanel.Items.IndexOf(sourceItem);
        if (sourceTabIndex < 0)
            sourceTabIndex = sourcePanel.Items.Count;

        if (sourcePanel.VisualParent is DockSplitPanel splitParent)
        {
            return new FloatingRestoreContext(
                sourcePanel,
                sourceTabIndex,
                splitParent,
                splitParent.Children.IndexOf(sourcePanel),
                DockSplitPanel.GetSize(sourcePanel),
                null);
        }

        return new FloatingRestoreContext(
            sourcePanel,
            sourceTabIndex,
            null,
            -1,
            GridLength.Star,
            sourcePanel.VisualParent as ContentControl);
    }

    private void AttachFloatingWindowRestoreHandler(Window floatingWindow, FloatingRestoreContext restoreContext)
    {
        DetachFloatingWindowRestoreHandler();
        _floatingRestoreContext = restoreContext;
        _floatingWindowOwner = floatingWindow;
        _suppressRestoreOnFloatingWindowClose = false;
        _floatingWindowClosingHandler = OnFloatingWindowOwnerClosing;
        floatingWindow.Closing += _floatingWindowClosingHandler;
    }

    private void DetachFloatingWindowRestoreHandler()
    {
        if (_floatingWindowOwner != null && _floatingWindowClosingHandler != null)
            _floatingWindowOwner.Closing -= _floatingWindowClosingHandler;

        _floatingWindowOwner = null;
        _floatingWindowClosingHandler = null;
    }

    private void SuppressFloatingWindowRestore()
    {
        _suppressRestoreOnFloatingWindowClose = true;
        _floatingRestoreContext = null;
    }

    private void OnFloatingWindowOwnerClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_suppressRestoreOnFloatingWindowClose || _floatingRestoreContext == null || CanClose)
        {
            DetachFloatingWindowRestoreHandler();
            return;
        }

        if (!TryRestoreFloatingItemToOriginalDock())
        {
            e.Cancel = true;
            return;
        }

        DetachFloatingWindowRestoreHandler();
    }

    private bool TryRestoreFloatingItemToOriginalDock()
    {
        var restoreContext = _floatingRestoreContext;
        if (restoreContext == null)
            return true;

        if (!EnsureSourcePanelAttached(restoreContext))
            return false;

        var targetPanel = restoreContext.SourcePanel;
        if (targetPanel.VisualParent == null)
            return false;

        var content = Content;
        var headerText = Header?.ToString() ?? "Panel";
        Content = null;

        if (content is UIElement contentElement && contentElement.VisualParent is DockTabPanel oldPanel)
            oldPanel.ReleaseContentElement(contentElement);

        var restoredItem = CreateTransferredDockItem(content, headerText);
        var insertIndex = Math.Clamp(restoreContext.SourceTabIndex, 0, targetPanel.Items.Count);

        try
        {
            targetPanel.Items.Insert(insertIndex, restoredItem);
            targetPanel.SelectTab(restoredItem);
            _floatingRestoreContext = null;
            return true;
        }
        catch
        {
            restoredItem.Content = null;
            Content = content;
            return false;
        }
    }

    private static bool EnsureSourcePanelAttached(FloatingRestoreContext restoreContext)
    {
        var sourcePanel = restoreContext.SourcePanel;
        if (sourcePanel.VisualParent != null)
            return true;

        if (restoreContext.SourceSplitParent != null)
        {
            var splitParent = restoreContext.SourceSplitParent;
            if (splitParent.Children.IndexOf(sourcePanel) < 0)
            {
                var insertIndex = Math.Clamp(restoreContext.SourcePaneIndex, 0, splitParent.Children.Count);
                splitParent.Children.Insert(insertIndex, sourcePanel);
                DockSplitPanel.SetSize(sourcePanel, restoreContext.SourcePaneSize);
            }

            splitParent.InvalidateMeasure();
            splitParent.InvalidateArrange();
            splitParent.InvalidateVisual();
            return sourcePanel.VisualParent != null;
        }

        if (restoreContext.SourceContentHost != null)
        {
            var host = restoreContext.SourceContentHost;
            if (ReferenceEquals(host.Content, sourcePanel))
                return true;

            if (host.Content == null)
            {
                host.Content = sourcePanel;
                return true;
            }
        }

        return false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var headerText = Header?.ToString() ?? "";
        var fontSize = FontSize > 0 ? FontSize : 12;
        var fontFamily = !string.IsNullOrEmpty(FontFamily) ? FontFamily : FrameworkElement.DefaultFontFamilyName;
        var formatted = new FormattedText(headerText, fontFamily, fontSize);
        TextMeasurement.MeasureText(formatted);

        var padding = GetEffectivePadding();
        var desiredWidth = formatted.Width + padding.Left + padding.Right;

        // Add space for close button
        if (CanClose)
            desiredWidth += 20;

        var isVerticalStrip = OwnerPanel?.IsVerticalTabStrip == true;
        var width = double.IsInfinity(availableSize.Width)
            ? desiredWidth
            : isVerticalStrip
                ? availableSize.Width
                : Math.Min(desiredWidth, availableSize.Width);

        var minHeight = Math.Max(20, Math.Ceiling(fontSize + 10));
        var height = double.IsInfinity(availableSize.Height)
            ? minHeight
            : Math.Max(minHeight, availableSize.Height);

        return new Size(Math.Max(width, 40), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        InvalidateVisual();
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var activeTextBrush = ResolveActiveTextBrush();
        var inactiveTextBrush = ResolveInactiveTextBrush();
        var closeButtonHoverBrush = ResolveCloseButtonHoverBrush();
        var closeButtonPressedBrush = ResolveCloseButtonPressedBrush();
        var indicatorBrush = ResolveIndicatorBrush();
        var dragDimBrush = ResolveDragDimBrush();
        var padding = GetEffectivePadding();

        // Background based on state
        Brush bgBrush;
        if (IsSelected)
            bgBrush = ResolveSelectedBackgroundBrush();
        else if (IsMouseOver)
            bgBrush = ResolveHoverBackgroundBrush();
        else
            bgBrush = Background ?? s_transparentBrush;

        var placement = OwnerPanel?.TabStripPlacement ?? Dock.Top;
        var tabCornerRadius = ResolveTabCornerRadius(ActualWidth, ActualHeight + 1);
        var activeCorners = placement switch
        {
            Dock.Bottom => new CornerRadius(0, 0, tabCornerRadius, tabCornerRadius),
            Dock.Left => new CornerRadius(tabCornerRadius, 0, 0, tabCornerRadius),
            Dock.Right => new CornerRadius(0, tabCornerRadius, tabCornerRadius, 0),
            _ => new CornerRadius(tabCornerRadius, tabCornerRadius, 0, 0),
        };

        if (IsSelected)
        {
            var selectedRect = placement switch
            {
                Dock.Bottom => new Rect(0, -1, ActualWidth, ActualHeight + 1),
                Dock.Left => new Rect(0, 0, ActualWidth + 1, ActualHeight),
                Dock.Right => new Rect(-1, 0, ActualWidth + 1, ActualHeight),
                _ => new Rect(0, 0, ActualWidth, ActualHeight + 1),
            };
            dc.DrawRoundedRectangle(bgBrush, null, selectedRect, activeCorners);
        }
        else if (IsMouseOver)
        {
            dc.DrawRoundedRectangle(bgBrush, null, bounds, activeCorners);
        }
        else
        {
            dc.DrawRectangle(bgBrush, null, bounds);
        }

        // Header text
        if (CreateHeaderTextLayout(ResolveHeaderTextBrush(activeTextBrush, inactiveTextBrush), ActualWidth, ActualHeight) is FormattedText text)
        {
            var textX = padding.Left;
            var textY = (ActualHeight - text.Height) / 2;
            dc.PushClip(new RectangleGeometry(bounds));
            dc.DrawText(text, new Point(textX, textY));
            dc.Pop();
        }

        // Close button (X) 閳?only show when selected or hovered
        if (CanClose && (IsSelected || IsMouseOver))
        {
            var closeSize = 14.0;
            var closeX = ActualWidth - closeSize - 6;
            var closeY = (ActualHeight - closeSize) / 2;
            _closeButtonRect = new Rect(closeX - 2, closeY - 2, closeSize + 4, closeSize + 4);

            // Close button background on hover/pressed
            if (_isCloseButtonPressed || _isCloseButtonHovered)
            {
                var closeBg = _isCloseButtonPressed ? closeButtonPressedBrush : closeButtonHoverBrush;
                dc.DrawRoundedRectangle(closeBg, null, _closeButtonRect, 3, 3);
            }

            // Draw X
            var xBrush = (_isCloseButtonHovered || _isCloseButtonPressed) ? activeTextBrush : inactiveTextBrush;
            if (_closeButtonPen == null || _closeButtonPenBrush != xBrush)
            {
                _closeButtonPenBrush = xBrush;
                _closeButtonPen = new Pen(xBrush, 1);
            }
            var xPen = _closeButtonPen;
            var margin = 3.0;
            dc.DrawLine(xPen, new Point(closeX + margin, closeY + margin), new Point(closeX + closeSize - margin, closeY + closeSize - margin));
            dc.DrawLine(xPen, new Point(closeX + closeSize - margin, closeY + margin), new Point(closeX + margin, closeY + closeSize - margin));
        }
        else
        {
            _isCloseButtonPressed = false;
            _closeButtonRect = default;
        }

        // Selected tab border is now drawn by DockTabPanel as part of the unified border path

        // Dragging tab preview: highlight the tab itself instead of drawing an insertion line.
        if (_isReorderDragging)
        {
            if (_dragIndicatorPen == null || _dragIndicatorPenBrush != indicatorBrush)
            {
                _dragIndicatorPenBrush = indicatorBrush;
                _dragIndicatorPen = new Pen(indicatorBrush, 1)
                {
                    LineJoin = PenLineJoin.Round
                };
            }
            var dragPen = _dragIndicatorPen;
            dc.DrawRoundedRectangle(null, dragPen, bounds, activeCorners);
            dc.DrawRectangle(dragDimBrush, null, bounds);
        }
    }

    private Thickness GetEffectivePadding()
    {
        var padding = Padding;
        if (padding.Left == 0 && padding.Top == 0 && padding.Right == 0 && padding.Bottom == 0)
            return new Thickness(10, 5, 10, 5);
        return padding;
    }

    private double ResolveTabCornerRadius(double width, double height)
    {
        var radius = Math.Max(
            Math.Max(CornerRadius.TopLeft, CornerRadius.TopRight),
            Math.Max(CornerRadius.BottomRight, CornerRadius.BottomLeft));

        if (radius <= 0)
            radius = DefaultSelectedTabCornerRadius;

        var maxAllowed = Math.Max(0, Math.Min(width, height) / 2.0);
        return Math.Min(radius, maxAllowed);
    }



    internal FormattedText? CreateHeaderTextLayoutForTesting(double availableWidth, double availableHeight)
    {
        return CreateHeaderTextLayout(ResolveActiveTextBrush(), availableWidth, availableHeight);
    }

    private Brush ResolveHeaderTextBrush(Brush activeTextBrush, Brush inactiveTextBrush)
    {
        return IsSelected || IsMouseOver
            ? activeTextBrush
            : inactiveTextBrush;
    }

    private FormattedText? CreateHeaderTextLayout(Brush textBrush, double availableWidth, double availableHeight)
    {
        var headerText = Header?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(headerText))
            return null;

        var fontSize = FontSize > 0 ? FontSize : 12;
        var fontFamily = !string.IsNullOrEmpty(FontFamily) ? FontFamily : FrameworkElement.DefaultFontFamilyName;
        if (!ShouldConstrainHeaderTextWidth())
        {
            var naturalText = new FormattedText(headerText, fontFamily, fontSize)
            {
                Foreground = textBrush
            };
            TextMeasurement.MeasureText(naturalText);
            return naturalText;
        }

        var padding = GetEffectivePadding();
        var closeSpace = CanClose ? 20.0 : 0.0;
        var textWidth = Math.Max(0, availableWidth - padding.Left - padding.Right - closeSpace);
        if (textWidth <= 0)
            return null;

        var textHeight = GetSingleLineHeaderHeight(fontFamily, fontSize, availableHeight, padding);
        var displayText = TrimHeaderTextToFit(headerText, fontFamily, fontSize, textWidth, textHeight);
        if (string.IsNullOrEmpty(displayText))
            return null;

        var text = new FormattedText(displayText, fontFamily, fontSize)
        {
            Foreground = textBrush,
            MaxTextWidth = textWidth,
            MaxTextHeight = textHeight,
            Trimming = TextTrimming.CharacterEllipsis
        };
        TextMeasurement.MeasureText(text);
        return text;
    }

    private bool ShouldConstrainHeaderTextWidth()
    {
        return double.IsFinite(MaxWidth);
    }

    private static double GetSingleLineHeaderHeight(string fontFamily, double fontSize, double availableHeight, Thickness padding)
    {
        var contentHeight = availableHeight - padding.Top - padding.Bottom;
        if (!double.IsFinite(contentHeight))
            contentHeight = TextMeasurement.GetLineHeight(fontFamily, fontSize);

        var singleLineHeight = TextMeasurement.GetLineHeight(fontFamily, fontSize);
        return Math.Max(1, Math.Min(Math.Max(1, contentHeight), singleLineHeight));
    }

    private static string TrimHeaderTextToFit(string headerText, string fontFamily, double fontSize, double maxWidth, double maxHeight)
    {
        if (MeasureHeaderTextWidth(headerText, fontFamily, fontSize, maxHeight) <= maxWidth)
            return headerText;

        if (MeasureHeaderTextWidth(HeaderEllipsis, fontFamily, fontSize, maxHeight) > maxWidth)
            return string.Empty;

        int low = 0;
        int high = headerText.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var candidate = headerText.Substring(0, mid) + HeaderEllipsis;
            if (MeasureHeaderTextWidth(candidate, fontFamily, fontSize, maxHeight) <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return low > 0
            ? headerText.Substring(0, low) + HeaderEllipsis
            : HeaderEllipsis;
    }

    private static double MeasureHeaderTextWidth(string text, string fontFamily, double fontSize, double maxHeight)
    {
        var measured = new FormattedText(text, fontFamily, fontSize)
        {
            MaxTextHeight = maxHeight
        };
        TextMeasurement.MeasureText(measured);
        return measured.Width;
    }

    internal Brush ResolveSelectedBackgroundBrush()
    {
        var isFocused = OwnerPanel?.IsPanelFocused == true;
        if (isFocused)
            return ResolveBrush("OneTabActiveBackgroundFocused", "DockTabItemSelectedBackgroundFocused", s_fallbackSelectedBackgroundBrush);

        if (HasLocalValue(SelectedBackgroundProperty) && SelectedBackground != null)
            return SelectedBackground;

        return ResolveBrush("OneTabActiveBackground", "DockTabItemSelectedBackground", s_fallbackSelectedBackgroundBrush);
    }

    private Brush ResolveHoverBackgroundBrush()
    {
        if (HasLocalValue(HoverBackgroundProperty) && HoverBackground != null)
            return HoverBackground;

        return ResolveBrush("OneTabHoverBackground", "DockTabItemHoverBackground", s_fallbackHoverBackgroundBrush);
    }

    private Brush ResolveActiveTextBrush()
    {
        if (TryFindResource("OneTabActiveText") is Brush primary)
            return primary;
        if (TryFindResource("OneTextPrimary") is Brush onePrimary)
            return onePrimary;
        if (TryFindResource("TextPrimary") is Brush secondary)
            return secondary;
        return s_fallbackActiveTextBrush;
    }

    private Brush ResolveInactiveTextBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return ResolveBrush("OneTabInactiveText", "TextSecondary", s_fallbackInactiveTextBrush);
    }

    private Brush ResolveCloseButtonHoverBrush()
    {
        return ResolveBrush("OneSurfaceHover", "ControlBackgroundHover", s_fallbackCloseButtonHoverBrush);
    }

    private Brush ResolveCloseButtonPressedBrush()
    {
        return ResolveBrush("OneSurfaceActive", "ControlBackgroundPressed", s_fallbackCloseButtonPressedBrush);
    }

    private Brush ResolveIndicatorBrush()
    {
        return IndicatorBrush ?? ResolveBrush("OneTabActiveBorder", "AccentBrush", s_fallbackIndicatorBrush);
    }

    private Brush ResolveDragDimBrush()
    {
        return ResolveBrush("OneOverlayBackdrop", "HighlightBackground", s_fallbackDragDimBrush);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    private sealed class FloatingRestoreContext
    {
        public DockTabPanel SourcePanel { get; }
        public int SourceTabIndex { get; }
        public DockSplitPanel? SourceSplitParent { get; }
        public int SourcePaneIndex { get; }
        public GridLength SourcePaneSize { get; }
        public ContentControl? SourceContentHost { get; }

        public FloatingRestoreContext(
            DockTabPanel sourcePanel,
            int sourceTabIndex,
            DockSplitPanel? sourceSplitParent,
            int sourcePaneIndex,
            GridLength sourcePaneSize,
            ContentControl? sourceContentHost)
        {
            SourcePanel = sourcePanel;
            SourceTabIndex = sourceTabIndex;
            SourceSplitParent = sourceSplitParent;
            SourcePaneIndex = sourcePaneIndex;
            SourcePaneSize = sourcePaneSize;
            SourceContentHost = sourceContentHost;
        }
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    #endregion
}
