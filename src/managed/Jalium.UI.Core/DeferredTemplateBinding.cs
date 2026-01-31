namespace Jalium.UI;

/// <summary>
/// Represents a template binding that is resolved later when the templated parent is known.
/// This class is used during XAML parsing to store template bindings that cannot be resolved
/// until the visual tree is connected to its templated parent.
/// </summary>
public class DeferredTemplateBinding
{
    /// <summary>
    /// Gets the property path to bind to on the templated parent.
    /// </summary>
    public string PropertyPath { get; }

    /// <summary>
    /// Gets the converter, if any.
    /// </summary>
    public IValueConverter? Converter { get; }

    /// <summary>
    /// Gets the converter parameter, if any.
    /// </summary>
    public object? ConverterParameter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeferredTemplateBinding"/> class.
    /// </summary>
    /// <param name="propertyPath">The property path.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="converterParameter">The converter parameter.</param>
    public DeferredTemplateBinding(string propertyPath, IValueConverter? converter = null, object? converterParameter = null)
    {
        PropertyPath = propertyPath;
        Converter = converter;
        ConverterParameter = converterParameter;
    }
}
