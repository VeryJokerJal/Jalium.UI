namespace Jalium.UI;

/// <summary>
/// Specifies which property of a type is the content property.
/// This attribute is used by XAML parsers to determine where to place content children.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ContentPropertyAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the content property.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPropertyAttribute"/> class.
    /// </summary>
    /// <param name="name">The name of the content property.</param>
    public ContentPropertyAttribute(string name)
    {
        Name = name;
    }
}
