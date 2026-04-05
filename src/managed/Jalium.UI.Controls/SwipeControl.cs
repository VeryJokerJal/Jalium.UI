using Jalium.UI.Input;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a container that provides access to contextual commands through touch interactions.
/// </summary>
public class SwipeControl : ContentControl
{
    private static readonly SolidColorBrush s_defaultSwipeBackgroundBrush = new(ThemeColors.Accent);
    private static readonly SolidColorBrush s_defaultSwipeForegroundBrush = new(ThemeColors.TextPrimary);
    private double _translateX;
    private bool _isDragging;
    private Point _dragStart;
    private const double SwipeThreshold = 60;
    private const double MaxSwipeDistance = 200;

    #region Dependency Properties

    /// <summary>
    /// Identifies the LeftItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftItemsProperty =
        DependencyProperty.Register(nameof(LeftItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the RightItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RightItemsProperty =
        DependencyProperty.Register(nameof(RightItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TopItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TopItemsProperty =
        DependencyProperty.Register(nameof(TopItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the BottomItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BottomItemsProperty =
        DependencyProperty.Register(nameof(BottomItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the left side.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? LeftItems
    {
        get => (SwipeItems?)GetValue(LeftItemsProperty);
        set => SetValue(LeftItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the right side.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? RightItems
    {
        get => (SwipeItems?)GetValue(RightItemsProperty);
        set => SetValue(RightItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the top.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? TopItems
    {
        get => (SwipeItems?)GetValue(TopItemsProperty);
        set => SetValue(TopItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the bottom.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? BottomItems
    {
        get => (SwipeItems?)GetValue(BottomItemsProperty);
        set => SetValue(BottomItemsProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the SwipeControl class.
    /// </summary>
    public SwipeControl()
    {
        ClipToBounds = true;
        AddHandler(MouseDownEvent, new Input.MouseButtonEventHandler(OnSwipeMouseDown));
        AddHandler(MouseMoveEvent, new Input.MouseEventHandler(OnSwipeMouseMove));
        AddHandler(MouseUpEvent, new Input.MouseButtonEventHandler(OnSwipeMouseUp));
    }

    /// <summary>
    /// Resets the control to its default position and closes any revealed items.
    /// </summary>
    public void Close()
    {
        _translateX = 0;
        _isDragging = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;

        // Draw swipe item backgrounds behind the content
        if (_translateX > 0 && LeftItems != null && LeftItems.Count > 0)
        {
            DrawSwipeItems(dc, LeftItems, 0, 0, _translateX, RenderSize.Height);
        }
        else if (_translateX < 0 && RightItems != null && RightItems.Count > 0)
        {
            DrawSwipeItems(dc, RightItems, RenderSize.Width + _translateX, 0, -_translateX, RenderSize.Height);
        }

        // Translate and draw content
        dc.PushTransform(new TranslateTransform { X = _translateX, Y = 0 });
        base.OnRender(drawingContext);
        dc.Pop();
    }

    private void DrawSwipeItems(DrawingContext dc, SwipeItems items, double x, double y, double width, double height)
    {
        if (items.Count == 0) return;

        var itemWidth = width / items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var bg = ResolveSwipeItemBackground(item);
            var fg = ResolveSwipeItemForeground(item);

            // Background
            dc.DrawRectangle(bg, null, new Rect(x + i * itemWidth, y, itemWidth, height));

            // Icon + Text
            var centerX = x + i * itemWidth + itemWidth / 2;
            var centerY = height / 2;

            if (item.IconSource is string iconStr && !string.IsNullOrEmpty(iconStr))
            {
                var iconFormatted = new FormattedText(iconStr, "Segoe MDL2 Assets", 18) { Foreground = fg };
                dc.DrawText(iconFormatted, new Point(centerX - iconFormatted.Width / 2, centerY - 16));
            }

            if (!string.IsNullOrEmpty(item.Text))
            {
                var textFormatted = new FormattedText(item.Text, FrameworkElement.DefaultFontFamilyName, 12) { Foreground = fg };
                dc.DrawText(textFormatted, new Point(centerX - textFormatted.Width / 2, centerY + 4));
            }
        }
    }

    private Brush ResolveSwipeItemBackground(SwipeItem item)
    {
        return item.Background
            ?? TryFindResource("AccentBrush") as Brush
            ?? s_defaultSwipeBackgroundBrush;
    }

    private Brush ResolveSwipeItemForeground(SwipeItem item)
    {
        return item.Foreground
            ?? TryFindResource("TextOnAccent") as Brush
            ?? TryFindResource("TextPrimary") as Brush
            ?? s_defaultSwipeForegroundBrush;
    }

    private void OnSwipeMouseDown(object sender, Input.MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnSwipeMouseMove(object sender, Input.MouseEventArgs e)
    {
        if (!_isDragging) return;

        var pos = e.GetPosition(this);
        var deltaX = pos.X - _dragStart.X;

        // Constrain to directions that have swipe items
        if (deltaX > 0 && (LeftItems == null || LeftItems.Count == 0)) deltaX = 0;
        if (deltaX < 0 && (RightItems == null || RightItems.Count == 0)) deltaX = 0;

        // Clamp
        _translateX = Math.Clamp(deltaX, -MaxSwipeDistance, MaxSwipeDistance);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnSwipeMouseUp(object sender, Input.MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        // Check if threshold was reached
        if (Math.Abs(_translateX) >= SwipeThreshold)
        {
            var items = _translateX > 0 ? LeftItems : RightItems;
            if (items != null && items.Mode == SwipeMode.Execute && items.Count > 0)
            {
                items[0].RaiseInvoked();
                Close();
            }
            else
            {
                // Snap to reveal position
                _translateX = _translateX > 0
                    ? Math.Min(MaxSwipeDistance, SwipeThreshold * 2)
                    : Math.Max(-MaxSwipeDistance, -SwipeThreshold * 2);
                InvalidateVisual();
            }
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }
}
