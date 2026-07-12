namespace Jalium.UI.Markup;

/// <summary>
/// Identifies the property that supplies the implicit key when an object is added to a
/// resource dictionary without an explicit <c>x:Key</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DictionaryKeyPropertyAttribute : Attribute
{
    /// <summary>Initializes the attribute with the implicit-key property name.</summary>
    public DictionaryKeyPropertyAttribute(string? name)
    {
        Name = name;
    }

    /// <summary>Gets the name of the property that supplies the implicit key.</summary>
    public string? Name { get; }
}
