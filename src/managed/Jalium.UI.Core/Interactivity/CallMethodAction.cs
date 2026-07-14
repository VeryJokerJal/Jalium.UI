using System.Reflection;

namespace Jalium.UI.Interactivity;

/// <summary>
/// Calls a method on a specified object when invoked.
/// </summary>
public sealed class CallMethodAction : BehaviorTriggerAction<DependencyObject>
{
    /// <summary>
    /// Identifies the TargetObject dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TargetObjectProperty =
        DependencyProperty.Register(nameof(TargetObject), typeof(object), typeof(CallMethodAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the MethodName dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MethodNameProperty =
        DependencyProperty.Register(nameof(MethodName), typeof(string), typeof(CallMethodAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the object that exposes the method of interest.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the method to invoke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string? MethodName
    {
        get => (string?)GetValue(MethodNameProperty);
        set => SetValue(MethodNameProperty, value);
    }

    /// <summary>
    /// Invokes the action.
    /// </summary>
    /// <param name="parameter">The parameter to the action.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("CallMethodAction reflects on the runtime target type to invoke MethodName.")]
    protected override void Invoke(object? parameter)
    {
        var target = TargetObject ?? AssociatedObject;
        if (target == null || string.IsNullOrEmpty(MethodName))
            return;

        var type = target.GetType();

        // Try to find a method with a parameter that matches the parameter type
        if (parameter != null)
        {
            var paramType = parameter.GetType();
            var methodWithParam = type.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Instance, null, new[] { paramType }, null);
            if (methodWithParam != null)
            {
                methodWithParam.Invoke(target, new[] { parameter });
                return;
            }
        }

        // Try to find a parameterless method
        var parameterlessMethod = type.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (parameterlessMethod != null)
        {
            parameterlessMethod.Invoke(target, null);
            return;
        }

        // Try to find any method with the name
        var anyMethod = type.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Instance);
        if (anyMethod != null)
        {
            var methodParams = anyMethod.GetParameters();
            if (methodParams.Length == 0)
            {
                anyMethod.Invoke(target, null);
            }
            else if (methodParams.Length == 1)
            {
                anyMethod.Invoke(target, new[] { parameter });
            }
        }
    }
}

/// <summary>
/// Changes a property on a target object to a specified value.
/// </summary>
public sealed class ChangePropertyAction : BehaviorTriggerAction<DependencyObject>
{
    /// <summary>
    /// Identifies the TargetObject dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TargetObjectProperty =
        DependencyProperty.Register(nameof(TargetObject), typeof(object), typeof(ChangePropertyAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the PropertyName dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PropertyNameProperty =
        DependencyProperty.Register(nameof(PropertyName), typeof(string), typeof(ChangePropertyAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(object), typeof(ChangePropertyAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the object that exposes the property of interest.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the property to change.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string? PropertyName
    {
        get => (string?)GetValue(PropertyNameProperty);
        set => SetValue(PropertyNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to set on the property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Invokes the action.
    /// </summary>
    /// <param name="parameter">The parameter to the action.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ChangePropertyAction sets a property on the target via reflection.")]
    protected override void Invoke(object? parameter)
    {
        var target = TargetObject ?? AssociatedObject;
        if (target == null || string.IsNullOrEmpty(PropertyName))
            return;

        // Resolve DependencyProperty via the AOT-safe registry.
        if (target is DependencyObject depObj)
        {
            var dp = DependencyProperty.FromName(target.GetType(), PropertyName);
            if (dp != null)
            {
                depObj.SetValue(dp, ConvertValue(Value, dp.PropertyType));
                return;
            }
        }

        // CLR property fallback via PropertyAccessorRegistry (typed setters preferred).
        PropertyAccessorRegistry.TryWriteProperty(target, PropertyName, ConvertValue(Value, GuessTargetType(target, PropertyName)));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyAccessorRegistry.TryGetPropertyInfo performs Type.GetProperty reflection on the runtime type of the target. " +
            "GuessTargetType is reached only from ChangePropertyAction.Invoke, the XAML behavior/BehaviorEventTrigger surface, which already carries the " +
            "[RequiresUnreferencedCode] contract ('ChangePropertyAction sets a property on the target via reflection.'). Consumers wiring a " +
            "ChangePropertyAction to a user-defined type under trimming/AOT must preserve that type's property members; this leaf merely propagates " +
            "the contract already declared at that public entry point.")]
    private static Type GuessTargetType(object target, string propertyName)
    {
        var prop = PropertyAccessorRegistry.TryGetPropertyInfo(target, propertyName);
        return prop?.PropertyType ?? typeof(object);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
            return value;

        // Try type conversion
        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }
}
