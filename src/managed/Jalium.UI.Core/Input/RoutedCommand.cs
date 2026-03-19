using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Defines a command that is routed through the element tree and can be handled by CommandBindings.
/// </summary>
public class RoutedCommand : ICommand
{
    private readonly InputGestureCollection _inputGestures;

    /// <summary>
    /// Identifies the PreviewExecuted routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewExecutedEvent =
        EventManager.RegisterRoutedEvent("PreviewExecuted", RoutingStrategy.Tunnel,
            typeof(ExecutedRoutedEventHandler), typeof(RoutedCommand));

    /// <summary>
    /// Identifies the Executed routed event.
    /// </summary>
    public static readonly RoutedEvent ExecutedEvent =
        EventManager.RegisterRoutedEvent("Executed", RoutingStrategy.Bubble,
            typeof(ExecutedRoutedEventHandler), typeof(RoutedCommand));

    /// <summary>
    /// Identifies the PreviewCanExecute routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewCanExecuteEvent =
        EventManager.RegisterRoutedEvent("PreviewCanExecute", RoutingStrategy.Tunnel,
            typeof(CanExecuteRoutedEventHandler), typeof(RoutedCommand));

    /// <summary>
    /// Identifies the CanExecute routed event.
    /// </summary>
    public static readonly RoutedEvent CanExecuteEvent =
        EventManager.RegisterRoutedEvent("CanExecute", RoutingStrategy.Bubble,
            typeof(CanExecuteRoutedEventHandler), typeof(RoutedCommand));

    /// <summary>
    /// Initializes a new instance of the RoutedCommand class.
    /// </summary>
    public RoutedCommand()
    {
        Name = string.Empty;
        OwnerType = typeof(RoutedCommand);
        _inputGestures = new InputGestureCollection();
    }

    /// <summary>
    /// Initializes a new instance of the RoutedCommand class with the specified name and owner type.
    /// </summary>
    /// <param name="name">The name of the command.</param>
    /// <param name="ownerType">The type that registers the command.</param>
    public RoutedCommand(string name, Type ownerType)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        OwnerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
        _inputGestures = new InputGestureCollection();
    }

    /// <summary>
    /// Initializes a new instance of the RoutedCommand class with the specified name, owner type, and input gestures.
    /// </summary>
    /// <param name="name">The name of the command.</param>
    /// <param name="ownerType">The type that registers the command.</param>
    /// <param name="inputGestures">The input gestures that invoke the command.</param>
    public RoutedCommand(string name, Type ownerType, InputGestureCollection inputGestures)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        OwnerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
        _inputGestures = inputGestures ?? new InputGestureCollection();
    }

    /// <summary>
    /// Gets the name of the command.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the type that registers the command.
    /// </summary>
    public Type OwnerType { get; }

    /// <summary>
    /// Gets the collection of input gestures that invoke this command.
    /// </summary>
    public InputGestureCollection InputGestures => _inputGestures;

    /// <summary>
    /// Occurs when changes occur that affect whether the command should execute.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Determines whether the command can execute in its current state.
    /// </summary>
    /// <param name="parameter">Data used by the command.</param>
    /// <returns>true if this command can be executed; otherwise, false.</returns>
    public bool CanExecute(object? parameter)
    {
        return CanExecute(parameter, null);
    }

    /// <summary>
    /// Determines whether the command can execute on the specified target.
    /// </summary>
    /// <param name="parameter">Data used by the command.</param>
    /// <param name="target">The input element on which to check the command.</param>
    /// <returns>true if the command can execute on the target; otherwise, false.</returns>
    public bool CanExecute(object? parameter, IInputElement? target)
    {
        // target ??= FocusManager.GetFocusedElement(); // Would get focused element

        if (target is not UIElement uiElement)
            return false;

        var args = new CanExecuteRoutedEventArgs(this, parameter)
        {
            RoutedEvent = CanExecuteEvent
        };

        uiElement.RaiseEvent(args);

        return args.CanExecute;
    }

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="parameter">Data used by the command.</param>
    public void Execute(object? parameter)
    {
        Execute(parameter, null);
    }

    /// <summary>
    /// Executes the command on the specified target.
    /// </summary>
    /// <param name="parameter">Data used by the command.</param>
    /// <param name="target">The input element on which to execute the command.</param>
    public void Execute(object? parameter, IInputElement? target)
    {
        // target ??= FocusManager.GetFocusedElement(); // Would get focused element

        if (target is not UIElement uiElement)
            return;

        // First check if command can execute
        if (!CanExecute(parameter, target))
            return;

        // Raise PreviewExecuted (tunnel)
        var previewArgs = new ExecutedRoutedEventArgs(this, parameter)
        {
            RoutedEvent = PreviewExecutedEvent
        };
        uiElement.RaiseEvent(previewArgs);

        if (previewArgs.Handled)
            return;

        // Raise Executed (bubble)
        var args = new ExecutedRoutedEventArgs(this, parameter)
        {
            RoutedEvent = ExecutedEvent
        };
        uiElement.RaiseEvent(args);
    }

    /// <summary>
    /// Raises the CanExecuteChanged event.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Invalidates the command's CanExecute state.
    /// </summary>
    public static void InvalidateRequerySuggested()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}

/// <summary>
/// Delegate for ExecutedRoutedEvent handlers.
/// </summary>
public delegate void ExecutedRoutedEventHandler(object sender, ExecutedRoutedEventArgs e);

/// <summary>
/// Delegate for CanExecuteRoutedEvent handlers.
/// </summary>
public delegate void CanExecuteRoutedEventHandler(object sender, CanExecuteRoutedEventArgs e);
