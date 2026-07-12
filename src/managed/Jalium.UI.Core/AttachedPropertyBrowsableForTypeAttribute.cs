namespace Jalium.UI;

/// <summary>
/// Restricts an attached property to dependency objects derived from a
/// specified type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AttachedPropertyBrowsableForTypeAttribute : AttachedPropertyBrowsableAttribute
{
    private readonly Lazy<DependencyObjectType> _targetDependencyObjectType;

    /// <summary>
    /// Initializes a new instance for <paramref name="targetType"/>.
    /// </summary>
    public AttachedPropertyBrowsableForTypeAttribute(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        TargetType = targetType;
        _targetDependencyObjectType = new Lazy<DependencyObjectType>(
            () => DependencyObjectType.FromSystemType(TargetType),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Gets the type for which the attached property is browsable.
    /// </summary>
    public Type TargetType { get; }

    /// <inheritdoc />
    public override object TypeId => this;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is AttachedPropertyBrowsableForTypeAttribute other
            && TargetType == other.TargetType;
    }

    /// <inheritdoc />
    public override int GetHashCode() => TargetType.GetHashCode();

    internal override bool IsBrowsable(DependencyObject d, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(dp);

        return _targetDependencyObjectType.Value.IsInstanceOfType(d);
    }

    internal override bool UnionResults => true;
}
