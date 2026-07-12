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

    /// <summary>
    /// Determines whether a value supplied by an element's framework template is a dynamic
    /// resource reference.
    /// </summary>
    public static bool IsTemplatedValueDynamic(
        DependencyObject elementInTemplate,
        DependencyProperty dependencyProperty)
    {
        ArgumentNullException.ThrowIfNull(elementInTemplate);
        ArgumentNullException.ThrowIfNull(dependencyProperty);

        if (elementInTemplate is not FrameworkElement frameworkElement ||
            frameworkElement.TemplatedParent == null)
        {
            throw new ArgumentException(
                "Element must belong to a FrameworkTemplate instance.",
                nameof(elementInTemplate));
        }

        var source = frameworkElement.GetValueSourceInternal(dependencyProperty).BaseValueSource;
        return source is BaseValueSource.ParentTemplate or BaseValueSource.ParentTemplateTrigger &&
            DynamicResourceBindingOperations.TryGetDynamicResourceKey(
                frameworkElement,
                dependencyProperty,
                out _);
    }
}

public readonly struct ValueSource
{
    public ValueSource(
        BaseValueSource baseValueSource,
        bool isExpression,
        bool isAnimated,
        bool isCoerced,
        bool isCurrent = false)
    {
        BaseValueSource = baseValueSource;
        IsExpression = isExpression;
        IsAnimated = isAnimated;
        IsCoerced = isCoerced;
        IsCurrent = isCurrent;
    }

    public BaseValueSource BaseValueSource { get; }
    public bool IsExpression { get; }
    public bool IsAnimated { get; }
    public bool IsCoerced { get; }
    public bool IsCurrent { get; }

    /// <inheritdoc />
    public override int GetHashCode() => BaseValueSource.GetHashCode();

    /// <inheritdoc />
#pragma warning disable CS8765
    public override bool Equals(object o) =>
        o is ValueSource other
        && BaseValueSource == other.BaseValueSource
        && IsExpression == other.IsExpression
        && IsAnimated == other.IsAnimated
        && IsCoerced == other.IsCoerced;
#pragma warning restore CS8765

    public static bool operator ==(ValueSource vs1, ValueSource vs2) => vs1.Equals(vs2);

    public static bool operator !=(ValueSource vs1, ValueSource vs2) => !vs1.Equals(vs2);
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
