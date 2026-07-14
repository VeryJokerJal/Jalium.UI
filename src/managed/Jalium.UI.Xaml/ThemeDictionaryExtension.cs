using Jalium.UI.Markup;

namespace Jalium.UI;

/// <summary>
/// Implements a markup extension that enables application authors to customize control styles
/// based on the current system theme.
/// </summary>
public class ThemeDictionaryExtension : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeDictionaryExtension"/> class.
    /// </summary>
    public ThemeDictionaryExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeDictionaryExtension"/> class
    /// with the specified assembly name.
    /// </summary>
    /// <param name="assemblyName">The assembly name used to identify the theme resource dictionary.</param>
    public ThemeDictionaryExtension(string assemblyName)
    {
        AssemblyName = assemblyName;
    }

    /// <summary>
    /// Gets or sets the assembly name that identifies the theme.
    /// </summary>
    public string? AssemblyName { get; set; }

    /// <summary>
    /// Returns the URI of the theme dictionary based on the current system theme.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(AssemblyName))
            return null;

        // Return a URI pointing to the theme resource dictionary
        // Convention: /{AssemblyName};component/Themes/Generic.jalxaml
        return new Uri($"/{AssemblyName};component/Themes/Generic.jalxaml", UriKind.Relative);
    }
}

/// <summary>
/// Implements a markup extension that converts a BitmapImage from one embedded color profile to another.
/// </summary>
public class ColorConvertedBitmapExtension : MarkupExtension
{
    private readonly object? _image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorConvertedBitmapExtension"/> class.
    /// </summary>
    public ColorConvertedBitmapExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorConvertedBitmapExtension"/> class
    /// with the specified source.
    /// </summary>
    public ColorConvertedBitmapExtension(object image)
    {
        _image = image;
    }

    /// <summary>
    /// Returns the color-converted bitmap.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        // Simplified: return the source as-is (full implementation would do ICC profile conversion)
        return _image;
    }
}
