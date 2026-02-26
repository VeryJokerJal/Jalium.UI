using Jalium.UI.Controls;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Base class for controls that can switch states, such as CheckBox and RadioButton.
/// </summary>
public class ToggleButton : ButtonBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool?), typeof(ToggleButton),
            new PropertyMetadata(false, OnIsCheckedChanged));

    /// <summary>
    /// Identifies the IsThreeState dependency property.
    /// </summary>
    public static readonly DependencyProperty IsThreeStateProperty =
        DependencyProperty.Register(nameof(IsThreeState), typeof(bool), typeof(ToggleButton),
            new PropertyMetadata(false));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Checked routed event.
    /// </summary>
    public static readonly RoutedEvent CheckedEvent =
        EventManager.RegisterRoutedEvent(nameof(Checked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToggleButton));

    /// <summary>
    /// Identifies the Unchecked routed event.
    /// </summary>
    public static readonly RoutedEvent UncheckedEvent =
        EventManager.RegisterRoutedEvent(nameof(Unchecked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToggleButton));

    /// <summary>
    /// Identifies the Indeterminate routed event.
    /// </summary>
    public static readonly RoutedEvent IndeterminateEvent =
        EventManager.RegisterRoutedEvent(nameof(Indeterminate), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToggleButton));

    /// <summary>
    /// Occurs when the ToggleButton is checked.
    /// </summary>
    public event RoutedEventHandler Checked
    {
        add => AddHandler(CheckedEvent, value);
        remove => RemoveHandler(CheckedEvent, value);
    }

    /// <summary>
    /// Occurs when the ToggleButton is unchecked.
    /// </summary>
    public event RoutedEventHandler Unchecked
    {
        add => AddHandler(UncheckedEvent, value);
        remove => RemoveHandler(UncheckedEvent, value);
    }

    /// <summary>
    /// Occurs when the ToggleButton enters the indeterminate state.
    /// </summary>
    public event RoutedEventHandler Indeterminate
    {
        add => AddHandler(IndeterminateEvent, value);
        remove => RemoveHandler(IndeterminateEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the ToggleButton is checked.
    /// </summary>
    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the ToggleButton supports three states (checked, unchecked, indeterminate).
    /// </summary>
    public bool IsThreeState
    {
        get => (bool)GetValue(IsThreeStateProperty)!;
        set => SetValue(IsThreeStateProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ToggleButton"/> class.
    /// </summary>
    public ToggleButton()
    {
    }

    #endregion

    #region Click Handling

    /// <inheritdoc />
    protected override void OnClick()
    {
        OnToggle();
        base.OnClick();
    }

    /// <summary>
    /// Called when the button is toggled.
    /// </summary>
    protected virtual void OnToggle()
    {
        if (IsThreeState)
        {
            // Cycle: unchecked -> checked -> indeterminate -> unchecked
            if (IsChecked == false)
                IsChecked = true;
            else if (IsChecked == true)
                IsChecked = null;
            else
                IsChecked = false;
        }
        else
        {
            // Two-state: toggle between checked and unchecked
            IsChecked = IsChecked != true;
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleButton toggleButton)
        {
            toggleButton.OnIsCheckedChanged((bool?)e.OldValue, (bool?)e.NewValue);
        }
    }

    /// <summary>
    /// Called when the IsChecked property changes.
    /// </summary>
    protected virtual void OnIsCheckedChanged(bool? oldValue, bool? newValue)
    {
        InvalidateVisual();

        if (newValue == true)
        {
            OnChecked(new RoutedEventArgs(CheckedEvent, this));
        }
        else if (newValue == false)
        {
            OnUnchecked(new RoutedEventArgs(UncheckedEvent, this));
        }
        else
        {
            OnIndeterminate(new RoutedEventArgs(IndeterminateEvent, this));
        }
    }

    /// <summary>
    /// Called when the ToggleButton is checked.
    /// </summary>
    protected virtual void OnChecked(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Called when the ToggleButton is unchecked.
    /// </summary>
    protected virtual void OnUnchecked(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Called when the ToggleButton enters the indeterminate state.
    /// </summary>
    protected virtual void OnIndeterminate(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    #endregion
}
