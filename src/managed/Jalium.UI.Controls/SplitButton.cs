using System.Windows.Input;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that combines a primary action button with a secondary button that opens a flyout.
/// </summary>
public class SplitButton : ContentControl
{
    private Button? _primaryButton;
    private Button? _secondaryButton;
    private bool _isFlyoutOpen;
    private bool _isKeyDown;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SplitButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SplitButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Flyout dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FlyoutProperty =
        DependencyProperty.Register(nameof(Flyout), typeof(FlyoutBase), typeof(SplitButton),
            new PropertyMetadata(null, OnFlyoutChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the command to invoke when the primary button is clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the parameter to pass to the <see cref="Command"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the flyout shown by the secondary drop-down button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public FlyoutBase? Flyout
    {
        get => (FlyoutBase?)GetValue(FlyoutProperty);
        set => SetValue(FlyoutProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the primary action of the split button is invoked.
    /// </summary>
    public event SplitButtonClickEventHandler? Click;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="SplitButton"/> class.
    /// </summary>
    public SplitButton()
    {
        UseTemplateContentManagement();
        Focusable = true;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(KeyUpEvent, new KeyEventHandler(OnKeyUpHandler));
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_primaryButton != null)
        {
            _primaryButton.Click -= OnPrimaryButtonClick;
        }

        if (_secondaryButton != null)
        {
            _secondaryButton.Click -= OnSecondaryButtonClick;
        }

        _primaryButton = GetTemplateChild("PrimaryButton") as Button;
        _secondaryButton = GetTemplateChild("SecondaryButton") as Button;

        if (_primaryButton != null)
        {
            _primaryButton.Click += OnPrimaryButtonClick;


        }

        if (_secondaryButton != null)
        {
            _secondaryButton.Click += OnSecondaryButtonClick;
        }

        UpdateSecondaryButtonEnabledState();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == IsEnabledProperty)
        {
            UpdateSecondaryButtonEnabledState();
        }
    }

    /// <summary>
    /// Raises the <see cref="Click"/> event.
    /// </summary>
    protected virtual void OnClick(SplitButtonClickEventArgs args)
    {
        Click?.Invoke(this, args);
    }

    private void OnPrimaryButtonClick(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled)
            return;

        InvokePrimaryAction(executeCommand: true);
    }

    private void OnSecondaryButtonClick(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || Flyout == null)
            return;

        OpenFlyout();
    }

    private static void OnFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SplitButton splitButton)
            return;

        splitButton.OnFlyoutChanged((FlyoutBase?)e.OldValue, (FlyoutBase?)e.NewValue);
    }

    private void OnFlyoutChanged(FlyoutBase? oldFlyout, FlyoutBase? newFlyout)
    {
        if (oldFlyout != null)
        {
            if (oldFlyout.IsOpen)
            {
                oldFlyout.Hide();
            }
            oldFlyout.Opened -= OnFlyoutOpened;
            oldFlyout.Closed -= OnFlyoutClosed;
        }

        if (newFlyout != null)
        {
            newFlyout.Opened += OnFlyoutOpened;
            newFlyout.Closed += OnFlyoutClosed;
        }

        _isFlyoutOpen = newFlyout?.IsOpen == true;
        UpdateSecondaryButtonEnabledState();
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        _isFlyoutOpen = true;
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        _isFlyoutOpen = false;
    }

    private void OpenFlyout()
    {
        var flyout = Flyout;
        if (flyout == null)
            return;

        if (_isFlyoutOpen)
            return;

        if (!flyout.IsOpen)
        {
            flyout.ShowAt(this);
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            _isKeyDown = true;
            e.Handled = true;
        }
    }

    private void OnKeyUpHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            if (!_isKeyDown)
                return;

            _isKeyDown = false;
            InvokePrimaryAction(executeCommand: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && e.IsAltDown)
        {
            OpenFlyout();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F4)
        {
            OpenFlyout();
            e.Handled = true;
        }
    }

    private void InvokePrimaryAction(bool executeCommand)
    {
        var clickArgs = new SplitButtonClickEventArgs();
        OnClick(clickArgs);

        if (executeCommand)
        {
            ExecuteCommand();
        }
    }

    private void ExecuteCommand()
    {
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void UpdateSecondaryButtonEnabledState()
    {
        if (_secondaryButton == null)
            return;

        _secondaryButton.IsEnabled = IsEnabled && Flyout != null;
    }
}

/// <summary>
/// Represents event data for a <see cref="SplitButton"/> click.
/// </summary>
public sealed class SplitButtonClickEventArgs : EventArgs
{
}

/// <summary>
/// Represents the method that handles <see cref="SplitButton.Click"/> events.
/// </summary>
/// <param name="sender">The split button that raised the event.</param>
/// <param name="args">The event data.</param>
public delegate void SplitButtonClickEventHandler(SplitButton sender, SplitButtonClickEventArgs args);
