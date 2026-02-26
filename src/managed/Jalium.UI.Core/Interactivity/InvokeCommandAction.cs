using Jalium.UI.Input;

namespace Jalium.UI.Interactivity;

/// <summary>
/// Executes a specified ICommand when invoked.
/// </summary>
public sealed class InvokeCommandAction : TriggerAction<DependencyObject>
{
    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(InvokeCommandAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(InvokeCommandAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TriggerParameterPath dependency property.
    /// </summary>
    public static readonly DependencyProperty TriggerParameterPathProperty =
        DependencyProperty.Register(nameof(TriggerParameterPath), typeof(string), typeof(InvokeCommandAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the command to execute when invoked.
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command parameter.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets a property path on the trigger's parameter to use as the command parameter.
    /// </summary>
    public string? TriggerParameterPath
    {
        get => (string?)GetValue(TriggerParameterPathProperty);
        set => SetValue(TriggerParameterPathProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to pass the event args to the command.
    /// </summary>
    public bool PassEventArgsToCommand { get; set; }

    /// <summary>
    /// Invokes the action.
    /// </summary>
    /// <param name="parameter">The parameter to the action.</param>
    protected override void Invoke(object? parameter)
    {
        var command = Command;
        if (command == null)
            return;

        object? commandParameter = ResolveParameter(parameter);

        if (command.CanExecute(commandParameter))
        {
            command.Execute(commandParameter);
        }
    }

    private object? ResolveParameter(object? triggerParameter)
    {
        // If CommandParameter is explicitly set, use it
        if (ReadLocalValue(CommandParameterProperty) != DependencyProperty.UnsetValue)
        {
            return CommandParameter;
        }

        // If TriggerParameterPath is set, extract the property from the trigger parameter
        if (!string.IsNullOrEmpty(TriggerParameterPath) && triggerParameter != null)
        {
            return GetPropertyValue(triggerParameter, TriggerParameterPath);
        }

        // If PassEventArgsToCommand is true, pass the trigger parameter
        if (PassEventArgsToCommand)
        {
            return triggerParameter;
        }

        // Default to CommandParameter (which may be null)
        return CommandParameter;
    }

    private static object? GetPropertyValue(object obj, string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
            return obj;

        var current = obj;
        var parts = propertyPath.Split('.');

        foreach (var part in parts)
        {
            if (current == null)
                return null;

            var type = current.GetType();
            var property = type.GetProperty(part);
            if (property == null)
                return null;

            current = property.GetValue(current);
        }

        return current;
    }
}
