using System.ComponentModel;
using System.Xml;

namespace Jalium.UI.Markup;

/// <summary>
/// Specifies how expression values are written during XAML serialization.
/// </summary>
public enum XamlWriterMode
{
    /// <summary>The expression itself is serialized.</summary>
    Expression,

    /// <summary>The evaluated value of the expression is serialized.</summary>
    Value,
}

/// <summary>
/// Provides a mutable collection of services for XAML serialization infrastructure.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class ServiceProviders : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>Gets a registered service, or <see langword="null"/> when none exists.</summary>
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.GetValueOrDefault(serviceType);
    }

    /// <summary>Adds a service to this provider.</summary>
    public void AddService(Type serviceType, object service)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(service);

        if (!_services.TryGetValue(serviceType, out var existing))
        {
            _services.Add(serviceType, service);
            return;
        }

        if (!ReferenceEquals(existing, service))
            throw new ArgumentException("A different service is already registered for this service type.", nameof(serviceType));
    }
}

/// <summary>
/// Provides services and serialization options to XAML designers and serializers.
/// </summary>
public class XamlDesignerSerializationManager : ServiceProviders
{
    private XamlWriterMode _xamlWriterMode = XamlWriterMode.Value;
    private XmlWriter? _xmlWriter;

    /// <summary>Initializes a manager that writes through the supplied XML writer.</summary>
    public XamlDesignerSerializationManager(XmlWriter xmlWriter)
    {
        _xmlWriter = xmlWriter;
    }

    /// <summary>Gets or sets how expression values are serialized.</summary>
    public XamlWriterMode XamlWriterMode
    {
        get => _xamlWriterMode;
        set
        {
            if (value is not XamlWriterMode.Expression and not XamlWriterMode.Value)
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(XamlWriterMode));

            _xamlWriterMode = value;
        }
    }

    internal XmlWriter? XmlWriter => _xmlWriter;

    internal void ClearXmlWriter() => _xmlWriter = null;
}
