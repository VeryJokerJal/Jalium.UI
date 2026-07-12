namespace Jalium.UI.Markup;

/// <summary>Identifies the property associated with the XAML <c>xml:lang</c> directive.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class XmlLangPropertyAttribute : Attribute
{
    public XmlLangPropertyAttribute(string? name)
    {
        Name = name;
    }

    public string? Name { get; }
}
