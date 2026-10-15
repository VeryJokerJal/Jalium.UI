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
    private static readonly Dictionary<Type, List<CommandBinding>> _classCommandBindings = new();
    private static readonly Dictionary<Type, List<InputBinding>> _classInputBindings = new();

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
            if (!_classCommandBindings.TryGetValue(type, out var bindings))
                _classCommandBindings[type] = bindings = [];
            bindings.Add(commandBinding);
        }

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
            if (!_classInputBindings.TryGetValue(type, out var bindings))
                _classInputBindings[type] = bindings = [];
            bindings.Add(inputBinding);
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

    private static TBinding[] GetClassBindings<TBinding>(Dictionary<Type, List<TBinding>> registry,
        Type type, bool newestFirst = false)
    {
        List<TBinding>? snapshot = null;
        lock (_syncLock)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                if (!registry.TryGetValue(current, out var bindings)) continue;
                snapshot ??= [];
                if (newestFirst)
                    for (int i = bindings.Count - 1; i >= 0; i--) snapshot.Add(bindings[i]);
                else snapshot.AddRange(bindings);
            }
        }
        return snapshot?.ToArray() ?? [];
    }

    internal static void InvokeCommandBindings(UIElement element, object sender, RoutedEventArgs args,
        CommandBindingCollection? instanceBindings)
    {
        if (args.Handled || !args.IsCommandInputCurrent) return;
        ICommand? command = args switch
        {
            CanExecuteRoutedEventArgs query when args.RoutedEvent == CanExecuteEvent || args.RoutedEvent == PreviewCanExecuteEvent => query.Command,
            ExecutedRoutedEventArgs execution when args.RoutedEvent == ExecutedEvent || args.RoutedEvent == PreviewExecutedEvent => execution.Command,
            _ => null
        };
        if (command == null) return;
        // Capture registrations before user handlers run, and release the lock
        // before any command or gesture callback can register another binding.
        var local = instanceBindings?.Cast<CommandBinding>().ToArray() ?? [];
        var inherited = GetClassBindings(_classCommandBindings, element.GetType());
        foreach (var binding in local.Concat(inherited))
        {
            if (args.Handled || !args.IsCommandInputCurrent) return;
            if (!ReferenceEquals(binding.Command, command)) continue;
            if (args is CanExecuteRoutedEventArgs query)
            {
                if (args.RoutedEvent == PreviewCanExecuteEvent) binding.OnPreviewCanExecute(sender, query);
                else binding.OnCanExecute(sender, query);
            }
            else if (args is ExecutedRoutedEventArgs execution)
            {
                if (args.RoutedEvent == PreviewExecutedEvent) binding.OnPreviewExecuted(sender, execution);
                else binding.OnExecuted(sender, execution);
            }
        }
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

        // Each node resolves its own instance/class bindings before moving to
        // an ancestor. The command target remains the original input target.
        var owners = keyRoute ?? [element];
        foreach (var visual in owners)
        {
            if (visual is not UIElement owner) continue;
            var candidate = FindInputCommand(owner, args, IsCurrent);
            if (!IsCurrent()) return true;
            if (candidate.Command == null) continue;
            var target = candidate.Target ?? element;
            bool canExecute;
            bool continueRouting = false;
            if (candidate.Command is RoutedCommand routed)
                canExecute = routed.CanExecute(candidate.Parameter, target, IsCurrent, out continueRouting);
            else canExecute = candidate.Command.CanExecute(candidate.Parameter);
            if (!IsCurrent()) return true;
            if (canExecute)
            {
                if (candidate.Command is RoutedCommand command)
                    command.Execute(candidate.Parameter, target, IsCurrent);
                else candidate.Command.Execute(candidate.Parameter);
                return true;
            }
            if (!continueRouting) return false;
        }

        // Retain the existing shared application gestures for commands exposed
        // by routed event handlers rather than a CommandBinding collection.
        foreach (var command in GetAllRoutedCommands())
        {
            foreach (InputGesture gesture in command.InputGestures.Cast<InputGesture>().ToArray())
            {
                bool matches = gesture.Matches(element, args);
                if (!IsCurrent()) return true;
                if (!matches) continue;
                bool canExecute = command.CanExecute(null, element, IsCurrent, out _);
                if (!IsCurrent()) return true;
                if (canExecute)
                {
                    command.Execute(null, element, IsCurrent);
                    return true;
                }
            }
        }
        return false;
    }

    private readonly record struct InputCommand(ICommand? Command, IInputElement? Target, object? Parameter);

    private static InputCommand FindInputCommand(UIElement owner, InputEventArgs args, Func<bool> isCurrent)
    {
        var classInputs = GetClassBindings(_classInputBindings, owner.GetType(), newestFirst: true);
        var localCommands = owner.CommandBindings.Cast<CommandBinding>().ToArray();
        var classCommands = GetClassBindings(_classCommandBindings, owner.GetType());
        var binding = owner.InputBindings.FindMatch(owner, args, isCurrent);
        if (!isCurrent()) return default;
        if (binding?.Command is not { } command)
        {
            binding = null;
            foreach (var candidate in classInputs)
            {
                bool matches = candidate.Gesture?.Matches(owner, args) == true;
                if (!isCurrent()) return default;
                if (matches && candidate.Command != null) { binding = candidate; break; }
            }
        }
        if (binding?.Command is { } bound)
            return new(bound, binding.CommandTarget, binding.CommandParameter);

        foreach (var candidate in localCommands.Concat(classCommands))
        {
            if (candidate.Command is not RoutedCommand routed) continue;
            foreach (InputGesture gesture in routed.InputGestures.Cast<InputGesture>().ToArray())
            {
                bool matches = gesture.Matches(owner, args);
                if (!isCurrent()) return default;
                if (matches) return new(routed, null, null);
            }
        }
        return default;
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
