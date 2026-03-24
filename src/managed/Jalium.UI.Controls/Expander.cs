using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a header and has a collapsible content area.
/// </summary>
public class Expander : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ExpanderAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsExpanded dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(Expander),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(Expander),
            new PropertyMetadata(null, OnHeaderChanged));

    /// <summary>
    /// Identifies the ExpandDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ExpandDirectionProperty =
        DependencyProperty.Register(nameof(ExpandDirection), typeof(ExpandDirection), typeof(Expander),
            new PropertyMetadata(ExpandDirection.Down, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the HeaderBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderBackgroundProperty =
        DependencyProperty.Register(nameof(HeaderBackground), typeof(Brush), typeof(Expander),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Expanded routed event.
    /// </summary>
    public static readonly RoutedEvent ExpandedEvent =
        EventManager.RegisterRoutedEvent(nameof(Expanded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(Expander));

    /// <summary>
    /// Identifies the Collapsed routed event.
    /// </summary>
    public static readonly RoutedEvent CollapsedEvent =
        EventManager.RegisterRoutedEvent(nameof(Collapsed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(Expander));

    /// <summary>
    /// Occurs when the expander is expanded.
    /// </summary>
    public event RoutedEventHandler Expanded
    {
        add => AddHandler(ExpandedEvent, value);
        remove => RemoveHandler(ExpandedEvent, value);
    }

    /// <summary>
    /// Occurs when the expander is collapsed.
    /// </summary>
    public event RoutedEventHandler Collapsed
    {
        add => AddHandler(CollapsedEvent, value);
        remove => RemoveHandler(CollapsedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the expander is expanded.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the direction in which the content area expands.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ExpandDirection ExpandDirection
    {
        get => (ExpandDirection)GetValue(ExpandDirectionProperty)!;
        set => SetValue(ExpandDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the header area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Brush? HeaderBackground
    {
        get => (Brush?)GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Expander"/> class.
    /// </summary>
    public Expander()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        UseTemplateContentManagement();

        // Register keyboard handler
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Template Parts

    private Border? _headerBorder;
    private Border? _contentBorder;
    private Shapes.Path? _chevron;
    private DispatcherTimer? _animationTimer;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        // Unsubscribe from old header border
        if (_headerBorder != null)
        {
            _headerBorder.RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnHeaderMouseDown));
        }

        base.OnApplyTemplate();
        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
        _contentBorder = GetTemplateChild("PART_ContentBorder") as Border;
        _chevron = GetTemplateChild("PART_Chevron") as Shapes.Path;

        // Subscribe to header border click
        if (_headerBorder != null)
        {
            _headerBorder.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnHeaderMouseDown));
        }

        // Sync initial state without animation
        if (_contentBorder != null)
        {
            if (IsExpanded)
            {
                _contentBorder.Visibility = Visibility.Visible;
                if (_chevron != null)
                {
                    var rt = _chevron.RenderTransform as RotateTransform ?? new RotateTransform();
                    rt.Angle = 90;
                    _chevron.RenderTransformOrigin = new Point(0.5, 0.5);
                    _chevron.RenderTransform = rt;
                }
            }
            else
            {
                _contentBorder.Visibility = Visibility.Collapsed;
            }
        }
    }

    #endregion

    #region Input Handling

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            Toggle();
            e.Handled = true;
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            Toggle();
            e.Handled = true;
        }
    }

    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Expander expander)
        {
            expander.OnExpandedChanged((bool)e.OldValue, (bool)e.NewValue);
        }
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Expander expander)
        {
            expander.InvalidateMeasure();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Expander expander)
        {
            expander.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Expander expander)
        {
            expander.InvalidateVisual();
        }
    }

    /// <summary>
    /// Called when the expanded state changes.
    /// </summary>
    protected void OnExpandedChanged(bool oldValue, bool newValue)
    {
        if (_contentBorder != null)
        {
            if (newValue)
            {
                _animationTimer = ExpandCollapseAnimator.AnimateExpand(
                    _contentBorder, _animationTimer, _chevron);
            }
            else
            {
                _animationTimer = ExpandCollapseAnimator.AnimateCollapse(
                    _contentBorder, _animationTimer, _chevron);
            }
        }
        else
        {
            // Fallback: no template applied yet, just toggle visibility
            InvalidateMeasure();
        }

        if (newValue)
        {
            RaiseEvent(new RoutedEventArgs(ExpandedEvent, this));
        }
        else
        {
            RaiseEvent(new RoutedEventArgs(CollapsedEvent, this));
        }
    }

    #endregion
}

/// <summary>
/// Specifies the direction in which an Expander expands.
/// </summary>
public enum ExpandDirection
{
    /// <summary>
    /// The content expands downward.
    /// </summary>
    Down,

    /// <summary>
    /// The content expands upward.
    /// </summary>
    Up,

    /// <summary>
    /// The content expands to the left.
    /// </summary>
    Left,

    /// <summary>
    /// The content expands to the right.
    /// </summary>
    Right
}
