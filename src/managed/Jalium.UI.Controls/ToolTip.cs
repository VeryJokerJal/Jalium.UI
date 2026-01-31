using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a tooltip that displays information about an element.
/// </summary>
public class ToolTip : ContentControl
{
    private Popup? _popup;
    private UIElement? _placementTarget;
    private System.Threading.Timer? _showTimer;
    private System.Threading.Timer? _hideTimer;
    private bool _isTimerRunning;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ToolTip),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(ToolTip),
            new PropertyMetadata(PlacementMode.Mouse));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(ToolTip),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(ToolTip),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the InitialShowDelay dependency property.
    /// </summary>
    public static readonly DependencyProperty InitialShowDelayProperty =
        DependencyProperty.Register(nameof(InitialShowDelay), typeof(int), typeof(ToolTip),
            new PropertyMetadata(400));

    /// <summary>
    /// Identifies the ShowDuration dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowDurationProperty =
        DependencyProperty.Register(nameof(ShowDuration), typeof(int), typeof(ToolTip),
            new PropertyMetadata(5000));

    #endregion

    #region Attached Properties

    /// <summary>
    /// Identifies the ToolTip attached property.
    /// </summary>
    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.RegisterAttached("ToolTip", typeof(object), typeof(ToolTip),
            new PropertyMetadata(null, OnToolTipChanged));

    /// <summary>
    /// Gets the tooltip for the specified element.
    /// </summary>
    public static object? GetToolTip(DependencyObject element)
    {
        return element.GetValue(ToolTipProperty);
    }

    /// <summary>
    /// Sets the tooltip for the specified element.
    /// </summary>
    public static void SetToolTip(DependencyObject element, object? value)
    {
        element.SetValue(ToolTipProperty, value);
    }

    private static void OnToolTipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            // Unsubscribe from old events
            element.MouseEnter -= OnElementMouseEnter;
            element.MouseLeave -= OnElementMouseLeave;

            if (e.NewValue != null)
            {
                // Subscribe to new events
                element.MouseEnter += OnElementMouseEnter;
                element.MouseLeave += OnElementMouseLeave;
            }
        }
    }

    private static void OnElementMouseEnter(object? sender, RoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var toolTipValue = GetToolTip(element);
            if (toolTipValue != null)
            {
                // Get mouse position from the event if available
                var position = Point.Zero;
                if (e is Input.MouseEventArgs mouseArgs)
                {
                    position = mouseArgs.Position;
                }
                ToolTipService.ShowToolTip(element, toolTipValue, position);
            }
        }
    }

    private static void OnElementMouseLeave(object? sender, RoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            ToolTipService.HideToolTip(element);
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the tooltip is open.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets how the tooltip is positioned.
    /// </summary>
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement position.
    /// </summary>
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement position.
    /// </summary>
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the time in milliseconds before the tooltip is shown.
    /// </summary>
    public int InitialShowDelay
    {
        get => (int)GetValue(InitialShowDelayProperty);
        set => SetValue(InitialShowDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the time in milliseconds the tooltip remains visible.
    /// </summary>
    public int ShowDuration
    {
        get => (int)GetValue(ShowDurationProperty);
        set => SetValue(ShowDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the tooltip is positioned.
    /// </summary>
    public UIElement? PlacementTarget
    {
        get => _placementTarget;
        set => _placementTarget = value;
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the tooltip is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs when the tooltip is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    public ToolTip()
    {
        // Default styling - modern dark theme
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 4, 8, 4);
        Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolTip toolTip)
        {
            if ((bool)e.NewValue)
            {
                toolTip.OpenToolTip();
            }
            else
            {
                toolTip.CloseToolTip();
            }
        }
    }

    private void OpenToolTip()
    {
        if (_popup == null)
        {
            _popup = new Popup
            {
                Child = this,
                PlacementTarget = _placementTarget,
                Placement = Placement,
                HorizontalOffset = HorizontalOffset,
                VerticalOffset = VerticalOffset + 16, // Default offset below cursor
                StaysOpen = false
            };
        }

        _popup.IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);

        // Start auto-hide timer
        StartHideTimer();
    }

    private void CloseToolTip()
    {
        StopTimers();

        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    internal void StartShowTimer(Point mousePosition)
    {
        StopTimers();
        _isTimerRunning = true;

        _showTimer = new System.Threading.Timer(_ =>
        {
            if (_isTimerRunning)
            {
                // Use dispatcher to update on UI thread
                IsOpen = true;
            }
        }, null, InitialShowDelay, System.Threading.Timeout.Infinite);
    }

    private void StartHideTimer()
    {
        _hideTimer = new System.Threading.Timer(_ =>
        {
            IsOpen = false;
        }, null, ShowDuration, System.Threading.Timeout.Infinite);
    }

    internal void StopTimers()
    {
        _isTimerRunning = false;
        _showTimer?.Dispose();
        _showTimer = null;
        _hideTimer?.Dispose();
        _hideTimer = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure content
        var child = Content;
        if (child == null) return new Size(0, 0);

        if (child is FrameworkElement fe)
        {
            // Account for padding
            var contentSize = new Size(
                Math.Max(0, availableSize.Width - Padding.Left - Padding.Right),
                Math.Max(0, availableSize.Height - Padding.Top - Padding.Bottom));

            fe.Measure(contentSize);
            return new Size(
                fe.DesiredSize.Width + Padding.Left + Padding.Right,
                fe.DesiredSize.Height + Padding.Top + Padding.Bottom);
        }

        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var child = Content;
        if (child is FrameworkElement fe)
        {
            var contentRect = new Rect(
                Padding.Left,
                Padding.Top,
                Math.Max(0, finalSize.Width - Padding.Left - Padding.Right),
                Math.Max(0, finalSize.Height - Padding.Top - Padding.Bottom));
            fe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext drawingContext)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        // Draw background
        if (Background != null)
        {
            drawingContext.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.Left > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            drawingContext.DrawRectangle(null, pen, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        base.OnRender(drawingContextObj);
    }
}

/// <summary>
/// Provides static methods for managing tooltips.
/// </summary>
public static class ToolTipService
{
    private static ToolTip? _currentToolTip;
    private static UIElement? _currentOwner;

    /// <summary>
    /// Cleans up all active tooltip timers. Called during application shutdown.
    /// </summary>
    internal static void Cleanup()
    {
        if (_currentToolTip != null)
        {
            _currentToolTip.StopTimers();
            _currentToolTip = null;
            _currentOwner = null;
        }
    }

    /// <summary>
    /// Shows a tooltip for the specified element.
    /// </summary>
    public static void ShowToolTip(UIElement owner, object content, Point mousePosition)
    {
        // Close any existing tooltip
        HideToolTip(_currentOwner);

        _currentOwner = owner;

        // Create or get the tooltip
        if (content is ToolTip existingToolTip)
        {
            _currentToolTip = existingToolTip;
        }
        else
        {
            _currentToolTip = new ToolTip();

            // If content is a string, wrap it in TextBlock
            if (content is string text)
            {
                _currentToolTip.Content = new TextBlock { Text = text };
            }
            else if (content is UIElement uiContent)
            {
                _currentToolTip.Content = uiContent;
            }
        }

        _currentToolTip.PlacementTarget = owner;
        _currentToolTip.StartShowTimer(mousePosition);
    }

    /// <summary>
    /// Hides the tooltip for the specified element.
    /// </summary>
    public static void HideToolTip(UIElement? owner)
    {
        if (owner == _currentOwner && _currentToolTip != null)
        {
            _currentToolTip.StopTimers();
            _currentToolTip.IsOpen = false;
            _currentToolTip = null;
            _currentOwner = null;
        }
    }
}
