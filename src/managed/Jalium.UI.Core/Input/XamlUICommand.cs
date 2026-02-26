namespace Jalium.UI.Input;

/// <summary>
/// Provides a base class for defining the command behavior of an interactive UI element
/// that performs an action when invoked, such as sending an email, deleting an item, or submitting a form.
/// </summary>
public class XamlUICommand : DependencyObject, ICommand
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(XamlUICommand),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the IconSource dependency property.
    /// </summary>
    public static readonly DependencyProperty IconSourceProperty =
        DependencyProperty.Register(nameof(IconSource), typeof(object), typeof(XamlUICommand),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Description dependency property.
    /// </summary>
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(XamlUICommand),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the AccessKey dependency property.
    /// </summary>
    public static readonly DependencyProperty AccessKeyProperty =
        DependencyProperty.Register(nameof(AccessKey), typeof(string), typeof(XamlUICommand),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(XamlUICommand),
            new PropertyMetadata(null, OnCommandChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the label for this command.
    /// </summary>
    public string Label
    {
        get => (string?)GetValue(LabelProperty) ?? string.Empty;
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon source for this command.
    /// </summary>
    public object? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets a description for this command.
    /// </summary>
    public string Description
    {
        get => (string?)GetValue(DescriptionProperty) ?? string.Empty;
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets the access key (mnemonic) for this command.
    /// </summary>
    public string AccessKey
    {
        get => (string?)GetValue(AccessKeyProperty) ?? string.Empty;
        set => SetValue(AccessKeyProperty, value);
    }

    /// <summary>
    /// Gets or sets an ICommand that this XamlUICommand delegates to.
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets the collection of keyboard accelerators for this command.
    /// </summary>
    public InputGestureCollection KeyboardAccelerators { get; } = new();

    #endregion

    #region Events

    /// <summary>
    /// Occurs when changes occur that affect whether the command should execute.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Occurs when a command execution is requested.
    /// </summary>
    public event EventHandler<ExecuteRequestedEventArgs>? ExecuteRequested;

    /// <summary>
    /// Occurs when a CanExecute query is requested.
    /// </summary>
    public event EventHandler<CanExecuteRequestedEventArgs>? CanExecuteRequested;

    #endregion

    #region ICommand Implementation

    /// <summary>
    /// Determines whether this command can execute in its current state.
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        // First check delegated command
        if (Command != null)
            return Command.CanExecute(parameter);

        // Then check event handlers
        var args = new CanExecuteRequestedEventArgs(parameter);
        CanExecuteRequested?.Invoke(this, args);
        return args.CanExecute;
    }

    /// <summary>
    /// Executes the command.
    /// </summary>
    public void Execute(object? parameter)
    {
        // First try delegated command
        if (Command != null)
        {
            if (Command.CanExecute(parameter))
                Command.Execute(parameter);
            return;
        }

        // Then invoke event handlers
        ExecuteRequested?.Invoke(this, new ExecuteRequestedEventArgs(parameter));
    }

    /// <summary>
    /// Raises the CanExecuteChanged event.
    /// </summary>
    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Private Methods

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not XamlUICommand self) return;

        if (e.OldValue is ICommand oldCommand)
            oldCommand.CanExecuteChanged -= self.OnInnerCanExecuteChanged;

        if (e.NewValue is ICommand newCommand)
            newCommand.CanExecuteChanged += self.OnInnerCanExecuteChanged;

        self.NotifyCanExecuteChanged();
    }

    private void OnInnerCanExecuteChanged(object? sender, EventArgs e)
    {
        NotifyCanExecuteChanged();
    }

    #endregion
}

/// <summary>
/// Provides event data for the ExecuteRequested event.
/// </summary>
public sealed class ExecuteRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the command parameter.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Initializes a new instance of the ExecuteRequestedEventArgs class.
    /// </summary>
    public ExecuteRequestedEventArgs(object? parameter)
    {
        Parameter = parameter;
    }
}

/// <summary>
/// Provides event data for the CanExecuteRequested event.
/// </summary>
public sealed class CanExecuteRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the command parameter.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the command can execute.
    /// </summary>
    public bool CanExecute { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the CanExecuteRequestedEventArgs class.
    /// </summary>
    public CanExecuteRequestedEventArgs(object? parameter)
    {
        Parameter = parameter;
    }
}
