using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Hosts popup menu items with optional up/down repeat buttons when content overflows vertically.
/// </summary>
internal sealed class MenuPopupScrollHost : Control
{
    private const double DefaultScrollButtonHeight = 18.0;
    private const double ScrollStepPerClick = ScrollViewer.LineScrollAmount * 2.0;
    private const double WheelNotch = 120.0;
    private const string LineButtonStyleKey = "ScrollBarLineButtonStyle";
    private static readonly SolidColorBrush s_buttonForegroundFallback = new(Color.FromRgb(220, 220, 220));
    private static readonly SolidColorBrush s_buttonBackgroundFallback = new(Color.FromRgb(45, 45, 48));

    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;
    private readonly RepeatButton _scrollUpButton;
    private readonly RepeatButton _scrollDownButton;

    private double _lastMeasuredContentHeight;
    private bool _isOverflowing;

    public MenuPopupScrollHost()
    {
        _itemsPanel = new StackPanel { Orientation = Orientation.Vertical };
        _scrollViewer = new ScrollViewer
        {
            Content = _itemsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = false,
            IsScrollInertiaEnabled = false
        };
        _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;

        _scrollUpButton = CreateScrollButton(-1);
        _scrollDownButton = CreateScrollButton(1);
        _scrollUpButton.Visibility = Visibility.Collapsed;
        _scrollDownButton.Visibility = Visibility.Collapsed;

        AddVisualChild(_scrollUpButton);
        AddVisualChild(_scrollViewer);
        AddVisualChild(_scrollDownButton);

        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler), true);
    }

    /// <summary>
    /// Gets the panel that stores menu item children.
    /// </summary>
    public StackPanel ItemsPanel => _itemsPanel;

    public override int VisualChildrenCount => 3;

    public override Visual? GetVisualChild(int index)
    {
        return index switch
        {
            0 => _scrollUpButton,
            1 => _scrollViewer,
            2 => _scrollDownButton,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var widthConstraint = availableSize.Width;
        if (double.IsNaN(widthConstraint) || widthConstraint < 0)
        {
            widthConstraint = 0;
        }

        _itemsPanel.Measure(new Size(widthConstraint, double.PositiveInfinity));
        _lastMeasuredContentHeight = _itemsPanel.DesiredSize.Height;

        _scrollViewer.Measure(availableSize);
        _scrollUpButton.Measure(new Size(widthConstraint, DefaultScrollButtonHeight));
        _scrollDownButton.Measure(new Size(widthConstraint, DefaultScrollButtonHeight));

        return _scrollViewer.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _isOverflowing = _lastMeasuredContentHeight > finalSize.Height + 0.5;
        var buttonHeight = _isOverflowing
            ? Math.Min(DefaultScrollButtonHeight, finalSize.Height * 0.5)
            : 0;

        if (_isOverflowing && buttonHeight > 0)
        {
            var viewportHeight = Math.Max(0, finalSize.Height - (buttonHeight * 2));
            _scrollUpButton.Arrange(new Rect(0, 0, finalSize.Width, buttonHeight));
            _scrollViewer.Arrange(new Rect(0, buttonHeight, finalSize.Width, viewportHeight));
            _scrollDownButton.Arrange(new Rect(0, buttonHeight + viewportHeight, finalSize.Width, buttonHeight));
        }
        else
        {
            _scrollUpButton.Visibility = Visibility.Collapsed;
            _scrollDownButton.Visibility = Visibility.Collapsed;
            _scrollUpButton.Arrange(new Rect(0, 0, 0, 0));
            _scrollViewer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            _scrollDownButton.Arrange(new Rect(0, 0, 0, 0));
            if (_scrollViewer.VerticalOffset > 0)
            {
                _scrollViewer.ScrollToVerticalOffset(0);
            }
        }

        UpdateScrollButtonState();
        return finalSize;
    }

    private RepeatButton CreateScrollButton(int direction)
    {
        var button = new RepeatButton
        {
            Height = DefaultScrollButtonHeight,
            MinWidth = 0,
            MinHeight = 0,
            Focusable = false,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = ResolveButtonBackgroundBrush(),
            Foreground = ResolveButtonForegroundBrush(),
            CornerRadius = new CornerRadius(0)
        };

        ApplyLineButtonVisual(button, direction);
        button.Click += (_, _) => ScrollBy(direction * ScrollStepPerClick);
        return button;
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtonState();
    }

    private void OnMouseWheelHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseWheelEventArgs wheelArgs)
        {
            return;
        }

        if (!_isOverflowing || wheelArgs.Delta == 0)
        {
            return;
        }

        var notches = wheelArgs.Delta / WheelNotch;
        if (Math.Abs(notches) < double.Epsilon)
        {
            return;
        }

        var delta = -notches * ScrollStepPerClick;
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + delta);
        wheelArgs.Handled = true;
    }

    private void ScrollBy(double delta)
    {
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + delta);
    }

    private void UpdateScrollButtonState()
    {
        if (!_isOverflowing || !_scrollViewer.CanScrollVertically)
        {
            _scrollUpButton.Visibility = Visibility.Collapsed;
            _scrollDownButton.Visibility = Visibility.Collapsed;
            _scrollUpButton.IsEnabled = false;
            _scrollDownButton.IsEnabled = false;
            return;
        }

        var canScrollUp = _scrollViewer.VerticalOffset > 0.5;
        var canScrollDown = _scrollViewer.VerticalOffset < _scrollViewer.ScrollableHeight - 0.5;

        // Keep both arrow rows visible for stable menu layout. Disabled arrow indicates edge.
        _scrollUpButton.Visibility = Visibility.Visible;
        _scrollDownButton.Visibility = Visibility.Visible;
        _scrollUpButton.IsEnabled = canScrollUp;
        _scrollDownButton.IsEnabled = canScrollDown;
    }

    private void ApplyLineButtonVisual(RepeatButton button, int direction)
    {
        button.Tag = direction < 0 ? "Up" : "Down";
        if (ResolveStyleResource(LineButtonStyleKey) is Style style)
        {
            button.Style = style;
            button.Content = null;
            // Let style setters drive visuals.
            button.Background = null;
            button.BorderBrush = null;
            return;
        }

        button.Content = direction < 0 ? "\uE70E" : "\uE70D";
        button.FontFamily = "Segoe MDL2 Assets";
        button.FontSize = 10;
    }

    private Style? ResolveStyleResource(object resourceKey)
    {
        if (TryFindResource(resourceKey) is Style localStyle)
            return localStyle;

        var app = Jalium.UI.Application.Current;
        if (app?.Resources != null &&
            app.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Style appStyle)
        {
            return appStyle;
        }

        return null;
    }

    private Brush ResolveButtonBackgroundBrush()
    {
        if (TryFindResource("OnePopupBackground") is Brush popupBackground)
        {
            return popupBackground;
        }

        if (TryFindResource("MenuFlyoutPresenterBackground") is Brush menuBackground)
        {
            return menuBackground;
        }

        return s_buttonBackgroundFallback;
    }

    private Brush ResolveButtonForegroundBrush()
    {
        if (TryFindResource("MenuFlyoutItemForeground") is Brush menuFlyoutForeground)
        {
            return menuFlyoutForeground;
        }

        if (TryFindResource("MenuItemForeground") is Brush menuItemForeground)
        {
            return menuItemForeground;
        }

        return s_buttonForegroundFallback;
    }
}
