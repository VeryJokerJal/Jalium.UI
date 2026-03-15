using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a specialized command bar that provides layout for AppBarButton and related command elements.
/// </summary>
public class CommandBar : Control
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(61, 61, 61));

    private StackPanel? _primaryItemsPanel;
    private StackPanel? _secondaryItemsPanel;
    private Button? _moreButton;
    private Popup? _overflowPopup;
    private Border? _overflowBorder;
    private bool _isSyncingPopupState;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(CommandBar),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the ClosedDisplayMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ClosedDisplayModeProperty =
        DependencyProperty.Register(nameof(ClosedDisplayMode), typeof(CommandBarClosedDisplayMode), typeof(CommandBar),
            new PropertyMetadata(CommandBarClosedDisplayMode.Compact));

    /// <summary>
    /// Identifies the DefaultLabelPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DefaultLabelPositionProperty =
        DependencyProperty.Register(nameof(DefaultLabelPosition), typeof(CommandBarDefaultLabelPosition), typeof(CommandBar),
            new PropertyMetadata(CommandBarDefaultLabelPosition.Bottom));

    /// <summary>
    /// Identifies the OverflowButtonVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty OverflowButtonVisibilityProperty =
        DependencyProperty.Register(nameof(OverflowButtonVisibility), typeof(CommandBarOverflowButtonVisibility), typeof(CommandBar),
            new PropertyMetadata(CommandBarOverflowButtonVisibility.Auto));

    /// <summary>
    /// Identifies the IsSticky dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsStickyProperty =
        DependencyProperty.Register(nameof(IsSticky), typeof(bool), typeof(CommandBar),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets the collection of primary command elements for the CommandBar.
    /// </summary>
    public ObservableCollection<ICommandBarElement> PrimaryCommands { get; } = new();

    /// <summary>
    /// Gets the collection of secondary command elements for the CommandBar.
    /// </summary>
    public ObservableCollection<ICommandBarElement> SecondaryCommands { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the CommandBar is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the display mode when the CommandBar is closed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CommandBarClosedDisplayMode ClosedDisplayMode
    {
        get => (CommandBarClosedDisplayMode)(GetValue(ClosedDisplayModeProperty) ?? CommandBarClosedDisplayMode.Compact);
        set => SetValue(ClosedDisplayModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the default label position for primary commands.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public CommandBarDefaultLabelPosition DefaultLabelPosition
    {
        get => (CommandBarDefaultLabelPosition)(GetValue(DefaultLabelPositionProperty) ?? CommandBarDefaultLabelPosition.Bottom);
        set => SetValue(DefaultLabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the overflow button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CommandBarOverflowButtonVisibility OverflowButtonVisibility
    {
        get => (CommandBarOverflowButtonVisibility)(GetValue(OverflowButtonVisibilityProperty) ?? CommandBarOverflowButtonVisibility.Auto);
        set => SetValue(OverflowButtonVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the CommandBar remains open after a command is invoked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSticky
    {
        get => (bool)GetValue(IsStickyProperty)!;
        set => SetValue(IsStickyProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the CommandBar starts to open.
    /// </summary>
    public event EventHandler? Opening;

    /// <summary>
    /// Occurs after the CommandBar is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs when the CommandBar starts to close.
    /// </summary>
    public event EventHandler? Closing;

    /// <summary>
    /// Occurs after the CommandBar is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    /// <summary>
    /// Initializes a new instance of the CommandBar class.
    /// </summary>
    public CommandBar()
    {
        Focusable = true;
        PrimaryCommands.CollectionChanged += OnPrimaryCommandsChanged;
        SecondaryCommands.CollectionChanged += OnSecondaryCommandsChanged;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureVisualTree();
        UpdateMoreButtonVisibility();

        if (_primaryItemsPanel != null)
        {
            // Update compact state based on DefaultLabelPosition
            foreach (var cmd in PrimaryCommands)
            {
                if (cmd is AppBarButton btn)
                    btn.IsCompact = DefaultLabelPosition == CommandBarDefaultLabelPosition.Collapsed;
                else if (cmd is AppBarToggleButton toggle)
                    toggle.IsCompact = DefaultLabelPosition == CommandBarDefaultLabelPosition.Collapsed;
            }

            _primaryItemsPanel.Measure(availableSize);
        }

        if (_moreButton != null)
        {
            _moreButton.Measure(new Size(40, availableSize.Height));
        }

        var width = (_primaryItemsPanel?.DesiredSize.Width ?? 0) +
                    (_moreButton?.Visibility == Visibility.Visible ? _moreButton.DesiredSize.Width : 0);
        var height = Math.Max(_primaryItemsPanel?.DesiredSize.Height ?? 0,
                              _moreButton?.DesiredSize.Height ?? 0);

        return new Size(
            Math.Min(width, availableSize.Width),
            Math.Clamp(height, 48, availableSize.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var moreButtonWidth = _moreButton?.Visibility == Visibility.Visible ? 40.0 : 0;
        var primaryWidth = finalSize.Width - moreButtonWidth;

        _primaryItemsPanel?.Arrange(new Rect(0, 0, primaryWidth, finalSize.Height));

        if (_moreButton?.Visibility == Visibility.Visible)
        {
            _moreButton.Arrange(new Rect(primaryWidth, 0, 40, finalSize.Height));
        }

        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        // Draw background
        var bg = ResolveBackgroundBrush();
        dc.DrawRectangle(bg, null, new Rect(RenderSize));

        // Draw bottom border
        var borderBrush = ResolveBorderBrush();
        var pen = new Jalium.UI.Media.Pen(borderBrush, 1);
        dc.DrawLine(pen, new Point(0, RenderSize.Height), new Point(RenderSize.Width, RenderSize.Height));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == BackgroundProperty || e.Property == BorderBrushProperty)
        {
            UpdateOverflowChrome();
        }
    }

    private void EnsureVisualTree()
    {
        if (_primaryItemsPanel != null) return;

        _primaryItemsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        AddVisualChild(_primaryItemsPanel);

        foreach (var cmd in PrimaryCommands)
        {
            if (cmd is FrameworkElement element)
                _primaryItemsPanel.Children.Add(element);
        }

        // More button ("...")
        _moreButton = new Button
        {
            Content = "\u22EF", // 閳?horizontal ellipsis
            Width = 40,
            Focusable = true,
            Background = Jalium.UI.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        _moreButton.Click += OnMoreButtonClick;
        AddVisualChild(_moreButton);

        // Overflow popup
        _overflowPopup = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = _moreButton,
            StaysOpen = false
        };
        _overflowPopup.Closed += OnOverflowPopupClosed;

        _secondaryItemsPanel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var cmd in SecondaryCommands)
        {
            if (cmd is FrameworkElement element)
                _secondaryItemsPanel.Children.Add(element);
        }

        _overflowBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = _secondaryItemsPanel,
            MinWidth = 200
        };
        UpdateOverflowChrome();
        _overflowPopup.Child = _overflowBorder;
        AddVisualChild(_overflowPopup);
    }

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            var count = 0;
            if (_primaryItemsPanel != null) count++;
            if (_moreButton != null) count++;
            if (_overflowPopup != null) count++;
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index == 0) return _primaryItemsPanel;
        if (index == 1) return _moreButton;
        if (index == 2) return _overflowPopup;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private bool ShouldShowMoreButton()
    {
        return OverflowButtonVisibility switch
        {
            CommandBarOverflowButtonVisibility.Visible => true,
            CommandBarOverflowButtonVisibility.Collapsed => false,
            _ => SecondaryCommands.Count > 0 // Auto
        };
    }

    private void OnMoreButtonClick(object? sender, EventArgs e)
    {
        if (!ShouldShowMoreButton())
            return;

        IsOpen = !IsOpen;
    }

    private void OnOverflowPopupClosed(object? sender, EventArgs e)
    {
        if (_isSyncingPopupState || !IsOpen)
            return;

        IsOpen = false;
    }

    private void UpdateMoreButtonVisibility()
    {
        if (_moreButton == null)
            return;

        _moreButton.Visibility = ShouldShowMoreButton() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateOverflowChrome()
    {
        if (_overflowBorder == null)
            return;

        _overflowBorder.Background = ResolveOverflowBackgroundBrush();
        _overflowBorder.BorderBrush = ResolveBorderBrush();
    }

    private Brush ResolveBackgroundBrush()
    {
        return Background
            ?? TryFindResource("CommandBarBackground") as Brush
            ?? TryFindResource("SurfaceBackground") as Brush
            ?? s_fallbackBackgroundBrush;
    }

    private Brush ResolveOverflowBackgroundBrush()
    {
        return Background
            ?? TryFindResource("CommandBarOverflowBackground") as Brush
            ?? TryFindResource("CommandBarBackground") as Brush
            ?? TryFindResource("SurfaceBackground") as Brush
            ?? s_fallbackBackgroundBrush;
    }

    private Brush ResolveBorderBrush()
    {
        return BorderBrush
            ?? TryFindResource("CommandBarBorderBrush") as Brush
            ?? TryFindResource("ControlBorder") as Brush
            ?? s_fallbackBorderBrush;
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CommandBar bar)
        {
            var isOpen = (bool)(e.NewValue ?? false);
            bar._isSyncingPopupState = true;
            try
            {
                if (isOpen)
                {
                    if (!bar.ShouldShowMoreButton())
                    {
                        if (bar._overflowPopup != null)
                            bar._overflowPopup.IsOpen = false;
                        return;
                    }

                    bar.Opening?.Invoke(bar, EventArgs.Empty);
                    if (bar._overflowPopup != null)
                        bar._overflowPopup.IsOpen = true;
                    bar.Opened?.Invoke(bar, EventArgs.Empty);
                }
                else
                {
                    bar.Closing?.Invoke(bar, EventArgs.Empty);
                    if (bar._overflowPopup != null)
                        bar._overflowPopup.IsOpen = false;
                    bar.Closed?.Invoke(bar, EventArgs.Empty);
                }
            }
            finally
            {
                bar._isSyncingPopupState = false;
            }
        }
    }

    private void OnPrimaryCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_primaryItemsPanel == null) return;

        _primaryItemsPanel.Children.Clear();
        foreach (var cmd in PrimaryCommands)
        {
            if (cmd is FrameworkElement element)
                _primaryItemsPanel.Children.Add(element);
        }
        InvalidateMeasure();
    }

    private void OnSecondaryCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_secondaryItemsPanel != null)
        {
            _secondaryItemsPanel.Children.Clear();
            foreach (var cmd in SecondaryCommands)
            {
                if (cmd is FrameworkElement element)
                    _secondaryItemsPanel.Children.Add(element);
            }
        }

        if (SecondaryCommands.Count == 0 && IsOpen)
        {
            IsOpen = false;
        }

        UpdateMoreButtonVisibility();
        InvalidateMeasure();
    }
}
