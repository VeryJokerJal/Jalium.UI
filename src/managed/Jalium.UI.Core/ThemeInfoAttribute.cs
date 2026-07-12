using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Specifies where an assembly stores theme-specific and generic resource dictionaries.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ThemeInfoAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeInfoAttribute"/> class.
    /// </summary>
    /// <param name="themeDictionaryLocation">The location of theme-specific resources.</param>
    /// <param name="genericDictionaryLocation">The location of generic resources.</param>
    public ThemeInfoAttribute(
        ResourceDictionaryLocation themeDictionaryLocation,
        ResourceDictionaryLocation genericDictionaryLocation)
    {
        ThemeDictionaryLocation = themeDictionaryLocation;
        GenericDictionaryLocation = genericDictionaryLocation;
    }

    /// <summary>
    /// Gets the location of theme-specific resources.
    /// </summary>
    public ResourceDictionaryLocation ThemeDictionaryLocation { get; }

    /// <summary>
    /// Gets the location of generic resources.
    /// </summary>
    public ResourceDictionaryLocation GenericDictionaryLocation { get; }

    internal static ThemeInfoAttribute? FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<ThemeInfoAttribute>();
    }
}
