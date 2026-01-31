using Jalium.UI;
using Jalium.UI.Controls;
using System.Reflection;

namespace Jalium.UI.Markup;

/// <summary>
/// Base class for XAML markup extensions.
/// </summary>
public abstract class MarkupExtension
{
    /// <summary>
    /// Returns the value to use for the target property.
    /// </summary>
    /// <param name="serviceProvider">A service provider that can provide services for the markup extension.</param>
    /// <returns>The value to use for the property where the extension is applied.</returns>
    public abstract object? ProvideValue(IServiceProvider serviceProvider);
}

/// <summary>
/// Provides services for markup extensions during XAML parsing.
/// </summary>
public interface IProvideValueTarget
{
    /// <summary>
    /// Gets the target object of the markup extension.
    /// </summary>
    object? TargetObject { get; }

    /// <summary>
    /// Gets the target property of the markup extension.
    /// </summary>
    object? TargetProperty { get; }
}

/// <summary>
/// Implementation of IProvideValueTarget for XAML parsing.
/// </summary>
internal class ProvideValueTarget : IProvideValueTarget
{
    public object? TargetObject { get; set; }
    public object? TargetProperty { get; set; }
}

/// <summary>
/// Service provider for markup extensions.
/// </summary>
internal class MarkupExtensionServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public void AddService(Type serviceType, object service)
    {
        _services[serviceType] = service;
    }

    public object? GetService(Type serviceType)
    {
        return _services.GetValueOrDefault(serviceType);
    }
}

/// <summary>
/// Provides access to the ambient resource dictionaries during XAML parsing.
/// </summary>
public interface IAmbientResourceProvider
{
    /// <summary>
    /// Tries to find a resource by key in the ambient resource dictionaries.
    /// </summary>
    bool TryGetResource(object key, out object? value);
}

/// <summary>
/// Implementation of IAmbientResourceProvider that searches through a stack of resource dictionaries.
/// </summary>
internal class AmbientResourceProvider : IAmbientResourceProvider
{
    private readonly List<ResourceDictionary> _resourceDictionaries = new();

    public void AddResourceDictionary(ResourceDictionary dictionary)
    {
        _resourceDictionaries.Add(dictionary);
    }

    public bool TryGetResource(object key, out object? value)
    {
        // Search in reverse order (most recent first)
        for (int i = _resourceDictionaries.Count - 1; i >= 0; i--)
        {
            if (_resourceDictionaries[i].TryGetValue(key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }
}

/// <summary>
/// XAML markup extension for data binding.
/// </summary>
public class BindingExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the path to the binding source property.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the binding source.
    /// </summary>
    public object? Source { get; set; }

    /// <summary>
    /// Gets or sets the name of the element to use as the binding source.
    /// </summary>
    public string? ElementName { get; set; }

    /// <summary>
    /// Gets or sets the binding mode.
    /// </summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>
    /// Gets or sets the converter.
    /// </summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>
    /// Gets or sets the converter parameter.
    /// </summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// Gets or sets the update source trigger.
    /// </summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; } = UpdateSourceTrigger.Default;

    /// <summary>
    /// Gets or sets the fallback value.
    /// </summary>
    public object? FallbackValue { get; set; }

    /// <summary>
    /// Gets or sets the target null value.
    /// </summary>
    public object? TargetNullValue { get; set; }

    /// <summary>
    /// Gets or sets the string format.
    /// </summary>
    public string? StringFormat { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingExtension"/> class.
    /// </summary>
    public BindingExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingExtension"/> class with the specified path.
    /// </summary>
    /// <param name="path">The binding path.</param>
    public BindingExtension(string path)
    {
        Path = path;
    }

    /// <inheritdoc />
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Path = string.IsNullOrEmpty(Path) ? null : new PropertyPath(Path),
            Source = Source,
            ElementName = ElementName,
            Mode = Mode,
            Converter = Converter,
            ConverterParameter = ConverterParameter,
            UpdateSourceTrigger = UpdateSourceTrigger,
            FallbackValue = FallbackValue,
            TargetNullValue = TargetNullValue,
            StringFormat = StringFormat
        };

        // If we have target information, set up the binding now
        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (provideValueTarget?.TargetObject is DependencyObject targetObject &&
            provideValueTarget?.TargetProperty is DependencyProperty targetProperty)
        {
            return targetObject.SetBinding(targetProperty, binding);
        }

        // Return the binding itself if we can't set it up now
        return binding;
    }
}

/// <summary>
/// XAML markup extension for static resources.
/// </summary>
public class StaticResourceExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public object? ResourceKey { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticResourceExtension"/> class.
    /// </summary>
    public StaticResourceExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticResourceExtension"/> class with the specified key.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    public StaticResourceExtension(object resourceKey)
    {
        ResourceKey = resourceKey;
    }

    /// <inheritdoc />
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (ResourceKey == null)
            return null;

        // First, check ambient resources (resources being parsed in current XAML context)
        var ambientProvider = serviceProvider?.GetService(typeof(IAmbientResourceProvider)) as IAmbientResourceProvider;
        if (ambientProvider != null && ambientProvider.TryGetResource(ResourceKey, out var ambientValue))
        {
            return ambientValue;
        }

        // Get the target object to search from
        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var targetObject = provideValueTarget?.TargetObject;

        // Search for the resource in the visual tree
        if (targetObject is FrameworkElement fe)
        {
            var result = ResourceLookup.FindResource(fe, ResourceKey);
            if (result != null)
                return result;
        }

        // Fall back to application resources
        if (Controls.Application.Current?.Resources != null &&
            Controls.Application.Current.Resources.TryGetValue(ResourceKey, out var appValue))
        {
            return appValue;
        }

        // Resource not found - throw exception like WPF does
        throw new XamlParseException($"Cannot find resource named '{ResourceKey}'. Resource names are case sensitive.");
    }
}

/// <summary>
/// XAML markup extension for x:Null.
/// </summary>
public class NullExtension : MarkupExtension
{
    /// <inheritdoc />
    public override object? ProvideValue(IServiceProvider serviceProvider) => null;
}

/// <summary>
/// XAML markup extension for template binding.
/// Binds a property of a template element to a property of the templated parent.
/// </summary>
public class TemplateBindingExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the property path to bind to on the templated parent.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the converter.
    /// </summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>
    /// Gets or sets the converter parameter.
    /// </summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateBindingExtension"/> class.
    /// </summary>
    public TemplateBindingExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateBindingExtension"/> class with the specified path.
    /// </summary>
    /// <param name="path">The property path.</param>
    public TemplateBindingExtension(string path)
    {
        Path = path;
    }

    /// <inheritdoc />
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Path))
            return null;

        // Return a deferred template binding that will be resolved
        // when the templated parent is set
        return new DeferredTemplateBinding(Path, Converter, ConverterParameter);
    }
}

/// <summary>
/// XAML markup extension for x:Type.
/// </summary>
public class TypeExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the type name.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Gets or sets the type.
    /// </summary>
    public Type? Type { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeExtension"/> class.
    /// </summary>
    public TypeExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeExtension"/> class with the specified type name.
    /// </summary>
    /// <param name="typeName">The type name.</param>
    public TypeExtension(string typeName)
    {
        TypeName = typeName;
    }

    /// <inheritdoc />
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (Type != null)
            return Type;

        if (string.IsNullOrEmpty(TypeName))
            return null;

        // Try to resolve the type
        return System.Type.GetType(TypeName);
    }
}

/// <summary>
/// Parser for markup extension syntax (e.g., "{Binding Path=Name}").
/// </summary>
internal static class MarkupExtensionParser
{
    /// <summary>
    /// Tries to parse a markup extension from an attribute value.
    /// </summary>
    public static bool TryParse(string value, object targetObject, PropertyInfo? targetProperty, out object? result)
    {
        return TryParse(value, targetObject, targetProperty, null, out result);
    }

    /// <summary>
    /// Tries to parse a markup extension from an attribute value with ambient resource support.
    /// </summary>
    public static bool TryParse(string value, object targetObject, PropertyInfo? targetProperty, IAmbientResourceProvider? ambientProvider, out object? result)
    {
        result = null;

        if (string.IsNullOrEmpty(value))
            return false;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            return false;

        // Extract the extension content
        var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (string.IsNullOrEmpty(content))
            return false;

        // Find the extension name
        var spaceIndex = content.IndexOf(' ');
        var extensionName = spaceIndex >= 0 ? content.Substring(0, spaceIndex) : content;
        var parameters = spaceIndex >= 0 ? content.Substring(spaceIndex + 1).Trim() : string.Empty;

        // Create the markup extension
        var extension = CreateMarkupExtension(extensionName, parameters);
        if (extension == null)
            return false;

        // Provide the value
        var serviceProvider = new MarkupExtensionServiceProvider();

        // Add ambient resource provider if available
        if (ambientProvider != null)
        {
            serviceProvider.AddService(typeof(IAmbientResourceProvider), ambientProvider);
        }

        // Find the DependencyProperty if the target is a DependencyObject
        DependencyProperty? dp = null;
        if (targetObject is DependencyObject && targetProperty != null)
        {
            dp = FindDependencyProperty(targetObject.GetType(), targetProperty.Name);
        }

        var provideValueTarget = new ProvideValueTarget
        {
            TargetObject = targetObject,
            TargetProperty = dp
        };
        serviceProvider.AddService(typeof(IProvideValueTarget), provideValueTarget);

        result = extension.ProvideValue(serviceProvider);
        return true;
    }

    private static MarkupExtension? CreateMarkupExtension(string name, string parameters)
    {
        // Handle special x: namespace extensions
        if (name.StartsWith("x:"))
        {
            name = name.Substring(2);
        }

        return name.ToLowerInvariant() switch
        {
            "binding" => CreateBindingExtension(parameters),
            "staticresource" => CreateStaticResourceExtension(parameters),
            "templatebinding" => CreateTemplateBindingExtension(parameters),
            "null" => new NullExtension(),
            "type" => CreateTypeExtension(parameters),
            _ => null
        };
    }

    private static BindingExtension CreateBindingExtension(string parameters)
    {
        var extension = new BindingExtension();

        if (string.IsNullOrWhiteSpace(parameters))
            return extension;

        // Parse parameters
        var parts = SplitParameters(parameters);
        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex < 0)
            {
                // Positional parameter (first one is Path)
                extension.Path = part.Trim();
            }
            else
            {
                var paramName = part.Substring(0, equalsIndex).Trim();
                var paramValue = part.Substring(equalsIndex + 1).Trim();

                SetBindingParameter(extension, paramName, paramValue);
            }
        }

        return extension;
    }

    private static void SetBindingParameter(BindingExtension extension, string name, string value)
    {
        switch (name.ToLowerInvariant())
        {
            case "path":
                extension.Path = value;
                break;
            case "mode":
                if (Enum.TryParse<BindingMode>(value, true, out var mode))
                    extension.Mode = mode;
                break;
            case "updatesourcetrigger":
                if (Enum.TryParse<UpdateSourceTrigger>(value, true, out var trigger))
                    extension.UpdateSourceTrigger = trigger;
                break;
            case "elementname":
                extension.ElementName = value;
                break;
            case "fallbackvalue":
                extension.FallbackValue = value;
                break;
            case "targetnullvalue":
                extension.TargetNullValue = value;
                break;
            case "stringformat":
                extension.StringFormat = value;
                break;
            case "converterparameter":
                extension.ConverterParameter = value;
                break;
        }
    }

    private static StaticResourceExtension CreateStaticResourceExtension(string parameters)
    {
        var key = parameters.Trim();
        return new StaticResourceExtension(key);
    }

    private static TypeExtension CreateTypeExtension(string parameters)
    {
        var typeName = parameters.Trim();
        return new TypeExtension(typeName);
    }

    private static TemplateBindingExtension CreateTemplateBindingExtension(string parameters)
    {
        var extension = new TemplateBindingExtension();

        if (string.IsNullOrWhiteSpace(parameters))
            return extension;

        // Parse parameters - first positional parameter is Path
        var parts = SplitParameters(parameters);
        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex < 0)
            {
                // Positional parameter (first one is Path/Property name)
                extension.Path = part.Trim();
            }
            else
            {
                var paramName = part.Substring(0, equalsIndex).Trim();
                var paramValue = part.Substring(equalsIndex + 1).Trim();

                switch (paramName.ToLowerInvariant())
                {
                    case "path":
                    case "property":
                        extension.Path = paramValue;
                        break;
                    case "converterparameter":
                        extension.ConverterParameter = paramValue;
                        break;
                }
            }
        }

        return extension;
    }

    private static List<string> SplitParameters(string parameters)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var braceDepth = 0;
        var inQuote = false;

        foreach (var c in parameters)
        {
            if (c == '\'' || c == '"')
            {
                inQuote = !inQuote;
                current.Append(c);
            }
            else if (!inQuote && c == '{')
            {
                braceDepth++;
                current.Append(c);
            }
            else if (!inQuote && c == '}')
            {
                braceDepth--;
                current.Append(c);
            }
            else if (!inQuote && braceDepth == 0 && c == ',')
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString().Trim());
        }

        return result;
    }

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        var dpFieldName = propertyName + "Property";
        var field = type.GetField(dpFieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return field?.GetValue(null) as DependencyProperty;
    }
}
