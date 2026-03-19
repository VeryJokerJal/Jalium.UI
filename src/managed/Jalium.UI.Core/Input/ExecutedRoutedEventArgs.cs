using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for the Executed and PreviewExecuted routed events.
/// </summary>
public sealed class ExecutedRoutedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the ExecutedRoutedEventArgs class.
    /// </summary>
    /// <param name="command">The command that was executed.</param>
    /// <param name="parameter">The command parameter.</param>
    public ExecutedRoutedEventArgs(ICommand command, object? parameter)
    {
        Command = command;
        Parameter = parameter;
    }

    /// <summary>
    /// Gets the command that was invoked.
    /// </summary>
    public ICommand Command { get; }

    /// <summary>
    /// Gets the command parameter.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Invokes the event handler in a type-specific way.
    /// </summary>
    protected internal override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is ExecutedRoutedEventHandler handler)
        {
            handler(genericTarget, this);
        }
        else
        {
            base.InvokeEventHandler(genericHandler, genericTarget);
        }
    }
}
