namespace Jalium.UI.Input;

/// <summary>
/// Provides data for the CanExecute and PreviewCanExecute routed events.
/// </summary>
public sealed class CanExecuteRoutedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the CanExecuteRoutedEventArgs class.
    /// </summary>
    /// <param name="command">The command associated with this event.</param>
    /// <param name="parameter">The command parameter.</param>
    public CanExecuteRoutedEventArgs(ICommand command, object? parameter)
    {
        Command = command;
        Parameter = parameter;
    }

    /// <summary>
    /// Gets the command associated with this event.
    /// </summary>
    public ICommand Command { get; }

    /// <summary>
    /// Gets the command parameter.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the command can execute.
    /// </summary>
    public bool CanExecute { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the input routed event that invoked
    /// the command should continue to route through the element tree.
    /// </summary>
    public bool ContinueRouting { get; set; }

    /// <summary>
    /// Invokes the event handler in a type-specific way.
    /// </summary>
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is CanExecuteRoutedEventHandler handler)
        {
            handler(genericTarget, this);
        }
        else
        {
            base.InvokeEventHandler(genericHandler, genericTarget);
        }
    }
}
