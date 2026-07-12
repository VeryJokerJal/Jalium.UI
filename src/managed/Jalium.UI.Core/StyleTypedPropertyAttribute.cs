namespace Jalium.UI;

/// <summary>
/// Associates a style-valued property with the type targeted by that style.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StyleTypedPropertyAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StyleTypedPropertyAttribute"/> class.
    /// </summary>
    public StyleTypedPropertyAttribute()
    {
    }

    /// <summary>
    /// Gets or sets the name of the style-valued property.
    /// </summary>
    public string Property { get; set; } = null!;

    /// <summary>
    /// Gets or sets the target type of styles assigned to <see cref="Property"/>.
    /// </summary>
    public Type StyleTargetType { get; set; } = null!;
}
