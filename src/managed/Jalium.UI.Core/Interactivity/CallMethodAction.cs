using System.Reflection;

namespace Jalium.UI.Interactivity;

/// <summary>
/// Calls a method on a specified object when invoked.
/// </summary>
public sealed class CallMethodAction : TriggerAction<DependencyObject>
{
    /// <summary>
    /// Identifies the TargetObject dependency property.
    /// </summary>
    public static readonly DependencyProperty TargetObjectProperty =
        DependencyProperty.Register(nameof(TargetObject), typeof(object), typeof(CallMethodAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the MethodName dependency property.
    /// </summary>
    public static readonly DependencyProperty MethodNameProperty =
        DependencyProperty.Register(nameof(MethodName), typeof(string), typeof(CallMethodAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the object that exposes the method of interest.
    /// </summary>
    public object? TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the method to invoke.
    /// </summary>
    public string? MethodName
    {
        get => (string?)GetValue(MethodNameProperty);
        set => SetValue(MethodNameProperty, value);
    }

    /// <summary>
    /// Invokes the action.
    /// </summary>
    /// <param name="parameter">The parameter to the action.</param>
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
public sealed class ChangePropertyAction : TriggerAction<DependencyObject>
{
    /// <summary>
    /// Identifies the TargetObject dependency property.
    /// </summary>
    public static readonly DependencyProperty TargetObjectProperty =
        DependencyProperty.Register(nameof(TargetObject), typeof(object), typeof(ChangePropertyAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the PropertyName dependency property.
    /// </summary>
    public static readonly DependencyProperty PropertyNameProperty =
        DependencyProperty.Register(nameof(PropertyName), typeof(string), typeof(ChangePropertyAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(object), typeof(ChangePropertyAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the object that exposes the property of interest.
    /// </summary>
    public object? TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the property to change.
    /// </summary>
    public string? PropertyName
    {
        get => (string?)GetValue(PropertyNameProperty);
        set => SetValue(PropertyNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to set on the property.
    /// </summary>
    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Invokes the action.
    /// </summary>
    /// <param name="parameter">The parameter to the action.</param>
    protected override void Invoke(object? parameter)
    {
        var target = TargetObject ?? AssociatedObject;
        if (target == null || string.IsNullOrEmpty(PropertyName))
            return;

        // Handle DependencyProperty
        if (target is DependencyObject depObj)
        {
            var dpField = target.GetType().GetField(PropertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty dp)
            {
                depObj.SetValue(dp, ConvertValue(Value, dp.PropertyType));
                return;
            }
        }

        // Handle regular property
        var property = target.GetType().GetProperty(PropertyName);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, ConvertValue(Value, property.PropertyType));
        }
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
