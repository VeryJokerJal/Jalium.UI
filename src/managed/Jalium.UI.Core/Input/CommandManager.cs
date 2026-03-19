using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Provides command related utility methods that register CommandBinding and InputBinding objects.
/// </summary>
public static class CommandManager
{
    private static readonly object _syncLock = new();
    private static readonly List<WeakReference<CommandBinding>> _classCommandBindings = new();
    private static readonly List<WeakReference<InputBinding>> _classInputBindings = new();

    /// <summary>
    /// Occurs when the command manager detects conditions that might change
    /// the ability of a command to execute.
    /// </summary>
    public static event EventHandler? RequerySuggested;

    /// <summary>
    /// Identifies the PreviewExecuted attached event.
    /// </summary>
    public static readonly RoutedEvent PreviewExecutedEvent = RoutedCommand.PreviewExecutedEvent;

    /// <summary>
    /// Identifies the Executed attached event.
    /// </summary>
    public static readonly RoutedEvent ExecutedEvent = RoutedCommand.ExecutedEvent;

    /// <summary>
    /// Identifies the PreviewCanExecute attached event.
    /// </summary>
    public static readonly RoutedEvent PreviewCanExecuteEvent = RoutedCommand.PreviewCanExecuteEvent;

    /// <summary>
    /// Identifies the CanExecute attached event.
    /// </summary>
    public static readonly RoutedEvent CanExecuteEvent = RoutedCommand.CanExecuteEvent;

    /// <summary>
    /// Forces the CommandManager to raise the RequerySuggested event.
    /// </summary>
    public static void InvalidateRequerySuggested()
    {
        RequerySuggested?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Registers a CommandBinding for a specified type.
    /// </summary>
    /// <param name="type">The type for which to register the command binding.</param>
    /// <param name="commandBinding">The command binding to register.</param>
    public static void RegisterClassCommandBinding(Type type, CommandBinding commandBinding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(commandBinding);

        lock (_syncLock)
        {
            _classCommandBindings.Add(new WeakReference<CommandBinding>(commandBinding));
        }

        // Register handlers for the command's events
        EventManager.RegisterClassHandler(type, PreviewExecutedEvent, new ExecutedRoutedEventHandler(OnClassPreviewExecuted));
        EventManager.RegisterClassHandler(type, ExecutedEvent, new ExecutedRoutedEventHandler(OnClassExecuted));
        EventManager.RegisterClassHandler(type, PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler(OnClassPreviewCanExecute));
        EventManager.RegisterClassHandler(type, CanExecuteEvent, new CanExecuteRoutedEventHandler(OnClassCanExecute));
    }

    /// <summary>
    /// Registers an InputBinding for a specified type.
    /// </summary>
    /// <param name="type">The type for which to register the input binding.</param>
    /// <param name="inputBinding">The input binding to register.</param>
    public static void RegisterClassInputBinding(Type type, InputBinding inputBinding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(inputBinding);

        lock (_syncLock)
        {
            _classInputBindings.Add(new WeakReference<InputBinding>(inputBinding));
        }
    }

    /// <summary>
    /// Adds a handler for the PreviewExecuted attached event.
    /// </summary>
    public static void AddPreviewExecutedHandler(UIElement element, ExecutedRoutedEventHandler handler)
    {
        element?.AddHandler(PreviewExecutedEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the PreviewExecuted attached event.
    /// </summary>
    public static void RemovePreviewExecutedHandler(UIElement element, ExecutedRoutedEventHandler handler)
    {
        element?.RemoveHandler(PreviewExecutedEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the Executed attached event.
    /// </summary>
    public static void AddExecutedHandler(UIElement element, ExecutedRoutedEventHandler handler)
    {
        element?.AddHandler(ExecutedEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the Executed attached event.
    /// </summary>
    public static void RemoveExecutedHandler(UIElement element, ExecutedRoutedEventHandler handler)
    {
        element?.RemoveHandler(ExecutedEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the PreviewCanExecute attached event.
    /// </summary>
    public static void AddPreviewCanExecuteHandler(UIElement element, CanExecuteRoutedEventHandler handler)
    {
        element?.AddHandler(PreviewCanExecuteEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the PreviewCanExecute attached event.
    /// </summary>
    public static void RemovePreviewCanExecuteHandler(UIElement element, CanExecuteRoutedEventHandler handler)
    {
        element?.RemoveHandler(PreviewCanExecuteEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the CanExecute attached event.
    /// </summary>
    public static void AddCanExecuteHandler(UIElement element, CanExecuteRoutedEventHandler handler)
    {
        element?.AddHandler(CanExecuteEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the CanExecute attached event.
    /// </summary>
    public static void RemoveCanExecuteHandler(UIElement element, CanExecuteRoutedEventHandler handler)
    {
        element?.RemoveHandler(CanExecuteEvent, handler);
    }

    private static void OnClassPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var binding = FindCommandBinding(element, e.Command);
            if (binding != null)
            {
                binding.OnPreviewExecuted(sender, e);
                if (e.Handled)
                    return;
            }
        }
    }

    private static void OnClassExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var binding = FindCommandBinding(element, e.Command);
            if (binding != null)
            {
                binding.OnExecuted(sender, e);
                e.Handled = true;
            }
        }
    }

    private static void OnClassPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var binding = FindCommandBinding(element, e.Command);
            if (binding != null)
            {
                binding.OnPreviewCanExecute(sender, e);
            }
        }
    }

    private static void OnClassCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var binding = FindCommandBinding(element, e.Command);
            if (binding != null)
            {
                binding.OnCanExecute(sender, e);
            }
        }
    }

    private static CommandBinding? FindCommandBinding(UIElement element, ICommand? command)
    {
        if (command == null)
            return null;

        // First, check instance command bindings
        var binding = element.CommandBindings.FindBinding(command);
        if (binding != null)
            return binding;

        // Check class command bindings
        lock (_syncLock)
        {
            foreach (var weakRef in _classCommandBindings)
            {
                if (weakRef.TryGetTarget(out var classBinding) && classBinding.Command == command)
                    return classBinding;
            }
        }

        return null;
    }

    /// <summary>
    /// Processes input for commands.
    /// </summary>
    /// <param name="element">The element that received the input.</param>
    /// <param name="args">The input event arguments.</param>
    /// <returns>True if the input was processed by a command; otherwise, false.</returns>
    internal static bool ProcessInput(UIElement element, InputEventArgs args)
    {
        // Check instance input bindings
        var binding = element.InputBindings.FindMatch(element, args);
        if (binding?.Command != null)
        {
            var target = binding.CommandTarget ?? element;
            if (binding.Command is RoutedCommand routedCommand)
            {
                if (routedCommand.CanExecute(binding.CommandParameter, target))
                {
                    routedCommand.Execute(binding.CommandParameter, target);
                    return true;
                }
            }
            else if (binding.Command.CanExecute(binding.CommandParameter))
            {
                binding.Command.Execute(binding.CommandParameter);
                return true;
            }
        }

        // Check command's built-in input gestures for routed commands
        // Check all common application commands for input gesture matches
        foreach (var command in GetAllRoutedCommands())
        {
            foreach (var gesture in command.InputGestures)
            {
                if (gesture.Matches(element, args))
                {
                    if (command.CanExecute(null, element))
                    {
                        command.Execute(null, element);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<RoutedCommand> GetAllRoutedCommands()
    {
        // Return all application commands
        yield return ApplicationCommands.Cut;
        yield return ApplicationCommands.Copy;
        yield return ApplicationCommands.Paste;
        yield return ApplicationCommands.Delete;
        yield return ApplicationCommands.Undo;
        yield return ApplicationCommands.Redo;
        yield return ApplicationCommands.SelectAll;
        yield return ApplicationCommands.New;
        yield return ApplicationCommands.Open;
        yield return ApplicationCommands.Save;
        yield return ApplicationCommands.SaveAs;
        yield return ApplicationCommands.Close;
        yield return ApplicationCommands.Print;
        yield return ApplicationCommands.Find;
        yield return ApplicationCommands.Replace;
        yield return ApplicationCommands.Help;
    }
}
