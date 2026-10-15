using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Provides command related utility methods that register CommandBinding and InputBinding objects.
/// </summary>
public sealed class CommandManager
{
    private CommandManager()
    {
    }

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
    /// <param name="isInputCurrent">Rechecks the caller's captured input lifetime.</param>
    /// <param name="keyBindingScope">Optional ancestor boundary for native key bindings.</param>
    /// <returns>True if the input was processed by a command; otherwise, false.</returns>
    internal static bool ProcessInput(UIElement element, InputEventArgs args, Func<bool>? isInputCurrent = null,
        UIElement? keyBindingScope = null)
    {
        var inputRoot = args is MouseEventArgs ? GetInputRoot(element) : null;
        var keyRoute = args is KeyEventArgs && keyBindingScope != null
            ? CaptureKeyBindingRoute(element, keyBindingScope) : null;
        bool IsCurrent() => (isInputCurrent?.Invoke() ?? true) &&
            (keyRoute == null || IsKeyBindingRouteCurrent(keyRoute)) &&
            (inputRoot == null || element.IsVisible && element.IsEnabled &&
             ReferenceEquals(GetInputRoot(element), inputRoot) &&
             (inputRoot is not UIElement root || root.IsVisible && root.IsEnabled));
        if (!IsCurrent()) return false;

        // Matching and CanExecute may close or move the target. Consume input
        // from a retired route and never run a command in a replacement root.
        var binding = element.InputBindings.FindMatch(element, args);
        if (!IsCurrent()) return true;
        // Native key input is dispatched once after control handlers. Search
        // its captured ancestors before the shared built-in gesture fallback,
        // retaining the focused command target and the active modal boundary.
        if (binding?.Command == null && keyRoute != null)
        {
            for (int i = 1; i < keyRoute.Count; i++)
            {
                if (keyRoute[i] is not UIElement owner) continue;
                binding = owner.InputBindings.FindMatch(owner, args);
                if (!IsCurrent()) return true;
                if (binding?.Command != null) break;
            }
        }
        var boundCommand = binding?.Command;
        if (boundCommand != null)
        {
            var target = binding!.CommandTarget ?? element;
            var parameter = binding.CommandParameter;
            if (boundCommand is RoutedCommand routedCommand)
            {
                bool canExecute = routedCommand.CanExecute(parameter, target);
                if (!IsCurrent()) return true;
                if (canExecute)
                {
                    routedCommand.Execute(parameter, target, IsCurrent);
                    return true;
                }
            }
            else
            {
                bool canExecute = boundCommand.CanExecute(parameter);
                if (!IsCurrent()) return true;
                if (canExecute)
                {
                    boundCommand.Execute(parameter);
                    return true;
                }
            }
        }

        foreach (var command in GetAllRoutedCommands())
        {
            foreach (InputGesture gesture in command.InputGestures)
            {
                bool matches = gesture.Matches(element, args);
                if (!IsCurrent()) return true;
                if (matches)
                {
                    bool canExecute = command.CanExecute(null, element);
                    if (!IsCurrent()) return true;
                    if (canExecute)
                    {
                        command.Execute(null, element, IsCurrent);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static List<Jalium.UI.Media.Visual> CaptureKeyBindingRoute(UIElement element, UIElement scope)
    {
        List<Jalium.UI.Media.Visual> route = [];
        for (Jalium.UI.Media.Visual? current = element; current != null && route.Count < 4096; current = current.VisualParent)
        {
            route.Add(current);
            if (ReferenceEquals(current, scope)) return route;
        }
        // A detached or cyclic route cannot reach the requested input scope.
        return [];
    }

    private static bool IsKeyBindingRouteCurrent(List<Jalium.UI.Media.Visual> route)
    {
        if (route.Count == 0) return false;
        for (int i = 0; i + 1 < route.Count; i++)
            if (!ReferenceEquals(route[i].VisualParent, route[i + 1])) return false;
        return true;
    }

    private static Jalium.UI.Media.Visual GetInputRoot(UIElement element)
    {
        Jalium.UI.Media.Visual current = element;
        while (current.VisualParent is { } parent)
            current = parent;
        return current;
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
