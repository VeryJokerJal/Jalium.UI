namespace Jalium.UI.Input;

/// <summary>
/// Binds a RoutedCommand to the event handlers that implement the command.
/// </summary>
public sealed class CommandBinding
{
    /// <summary>
    /// Initializes a new instance of the CommandBinding class.
    /// </summary>
    public CommandBinding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the CommandBinding class with the specified command.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    public CommandBinding(ICommand command)
    {
        Command = command;
    }

    /// <summary>
    /// Initializes a new instance of the CommandBinding class with the specified command and executed handler.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="executed">The handler for the Executed event.</param>
    public CommandBinding(ICommand command, ExecutedRoutedEventHandler executed)
    {
        Command = command;
        Executed += executed;
    }

    /// <summary>
    /// Initializes a new instance of the CommandBinding class with the specified command, executed handler, and can execute handler.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="executed">The handler for the Executed event.</param>
    /// <param name="canExecute">The handler for the CanExecute event.</param>
    public CommandBinding(ICommand command, ExecutedRoutedEventHandler executed, CanExecuteRoutedEventHandler canExecute)
    {
        Command = command;
        Executed += executed;
        CanExecute += canExecute;
    }

    /// <summary>
    /// Gets or sets the command associated with this binding.
    /// </summary>
    public ICommand? Command { get; set; }

    /// <summary>
    /// Occurs when the command associated with this CommandBinding executes.
    /// </summary>
    public event ExecutedRoutedEventHandler? Executed;

    /// <summary>
    /// Occurs before the command associated with this CommandBinding executes.
    /// </summary>
    public event ExecutedRoutedEventHandler? PreviewExecuted;

    /// <summary>
    /// Occurs when the command associated with this CommandBinding initiates a check to determine whether the command can be executed.
    /// </summary>
    public event CanExecuteRoutedEventHandler? CanExecute;

    /// <summary>
    /// Occurs when the command associated with this CommandBinding initiates a check to determine whether the command can be executed (tunnel).
    /// </summary>
    public event CanExecuteRoutedEventHandler? PreviewCanExecute;

    /// <summary>
    /// Raises the PreviewExecuted event.
    /// </summary>
    internal void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        PreviewExecuted?.Invoke(sender, e);
    }

    /// <summary>
    /// Raises the Executed event.
    /// </summary>
    internal void OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        Executed?.Invoke(sender, e);
    }

    /// <summary>
    /// Raises the PreviewCanExecute event.
    /// </summary>
    internal void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        PreviewCanExecute?.Invoke(sender, e);
    }

    /// <summary>
    /// Raises the CanExecute event.
    /// </summary>
    internal void OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        CanExecute?.Invoke(sender, e);
        if (e.CanExecute)
        {
            e.Handled = true;
        }
    }
}
