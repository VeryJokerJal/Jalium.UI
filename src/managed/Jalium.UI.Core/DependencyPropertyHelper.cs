namespace Jalium.UI;

/// <summary>
/// Provides a single helper method that reports the property system source of a dependency property value.
/// </summary>
public static class DependencyPropertyHelper
{
    public static ValueSource GetValueSource(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        ArgumentNullException.ThrowIfNull(dependencyProperty);
        return dependencyObject.GetValueSourceInternal(dependencyProperty);
    }
}

public readonly struct ValueSource
{
    public ValueSource(BaseValueSource baseValueSource, bool isExpression, bool isAnimated, bool isCoerced)
    {
        BaseValueSource = baseValueSource;
        IsExpression = isExpression;
        IsAnimated = isAnimated;
        IsCoerced = isCoerced;
    }

    public BaseValueSource BaseValueSource { get; }
    public bool IsExpression { get; }
    public bool IsAnimated { get; }
    public bool IsCoerced { get; }
}

public enum BaseValueSource
{
    Unknown = 0,
    Default = 1,
    Inherited = 2,
    DefaultStyle = 3,
    DefaultStyleTrigger = 4,
    Style = 5,
    TemplateTrigger = 6,
    StyleTrigger = 7,
    ImplicitStyleReference = 8,
    ParentTemplate = 9,
    ParentTemplateTrigger = 10,
    Local = 11,
}
