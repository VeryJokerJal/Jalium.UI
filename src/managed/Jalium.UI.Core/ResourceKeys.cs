using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Markup;

namespace Jalium.UI;

/// <summary>
/// Base class for keys that identify resources in an assembly.
/// </summary>
[MarkupExtensionReturnType(typeof(ResourceKey))]
public abstract class ResourceKey : MarkupExtension
{
    /// <summary>Gets the assembly that owns the resource.</summary>
    public abstract Assembly? Assembly { get; }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Matches MarkupExtension.ProvideValue; this implementation returns the resource key without reflection.")]
    [RequiresDynamicCode("Matches MarkupExtension.ProvideValue; this implementation emits no dynamic code.")]
    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>
/// Identifies a resource owned by a component type.
/// </summary>
[TypeConverter(typeof(ComponentResourceKeyConverter))]
public class ComponentResourceKey : ResourceKey
{
    private Type? _typeInTargetAssembly;
    private object? _resourceId;
    private bool _typeInTargetAssemblyInitialized;
    private bool _resourceIdInitialized;

    /// <summary>Initializes an empty key for XAML construction.</summary>
    public ComponentResourceKey()
    {
    }

    /// <summary>Initializes a fully specified component resource key.</summary>
    public ComponentResourceKey(Type typeInTargetAssembly, object resourceId)
    {
        ArgumentNullException.ThrowIfNull(typeInTargetAssembly);
        ArgumentNullException.ThrowIfNull(resourceId);

        _typeInTargetAssembly = typeInTargetAssembly;
        _typeInTargetAssemblyInitialized = true;
        _resourceId = resourceId;
        _resourceIdInitialized = true;
    }

    /// <summary>Gets or initializes the type in the target assembly.</summary>
    public Type? TypeInTargetAssembly
    {
        get => _typeInTargetAssembly;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_typeInTargetAssemblyInitialized)
            {
                throw new InvalidOperationException("The component resource key type cannot be changed after initialization.");
            }

            _typeInTargetAssembly = value;
            _typeInTargetAssemblyInitialized = true;
        }
    }

    /// <inheritdoc />
    public override Assembly? Assembly => _typeInTargetAssembly?.Assembly;

    /// <summary>Gets or initializes the resource identifier.</summary>
    public object? ResourceId
    {
        get => _resourceId;
        set
        {
            if (_resourceIdInitialized)
            {
                throw new InvalidOperationException("The component resource key identifier cannot be changed after initialization.");
            }

            _resourceId = value;
            _resourceIdInitialized = true;
        }
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is ComponentResourceKey other
        && Equals(_typeInTargetAssembly, other._typeInTargetAssembly)
        && Equals(_resourceId, other._resourceId);

    /// <inheritdoc />
    public override int GetHashCode() =>
        (_typeInTargetAssembly?.GetHashCode() ?? 0) ^ (_resourceId?.GetHashCode() ?? 0);

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "TargetType={0} ID={1}",
        _typeInTargetAssembly?.FullName ?? "null",
        _resourceId?.ToString() ?? "null");
}

/// <summary>
/// Base class for data-oriented template resource keys.
/// </summary>
[TypeConverter(typeof(TemplateKeyConverter))]
public abstract class TemplateKey : ResourceKey, ISupportInitialize
{
    private object? _dataType;
    private readonly TemplateType _templateType;
    private bool _initializing;

    /// <summary>Initializes an unassigned template key.</summary>
    protected TemplateKey(TemplateType templateType)
    {
        _templateType = templateType;
    }

    /// <summary>Initializes a template key for the supplied data type.</summary>
    protected TemplateKey(TemplateType templateType, object dataType)
    {
        ValidateDataType(dataType, nameof(dataType));
        _templateType = templateType;
        _dataType = dataType;
    }

    /// <summary>Gets or initializes the type for which the template is designed.</summary>
    public object? DataType
    {
        get => _dataType;
        set
        {
            if (!_initializing)
            {
                throw new InvalidOperationException("DataType can only be assigned during initialization.");
            }

            if (_dataType != null && !Equals(value, _dataType))
            {
                throw new InvalidOperationException("DataType cannot be changed after it has been initialized.");
            }

            ValidateDataType(value, nameof(value));
            _dataType = value;
        }
    }

    /// <inheritdoc />
    public override Assembly? Assembly => (_dataType as Type)?.Assembly;

    void ISupportInitialize.BeginInit() => _initializing = true;

    void ISupportInitialize.EndInit()
    {
        if (_dataType == null)
        {
            throw new InvalidOperationException($"DataType must have a value for {GetType().Name}.");
        }

        _initializing = false;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TemplateKey other
        && _templateType == other._templateType
        && Equals(_dataType, other._dataType);

    /// <inheritdoc />
    public override int GetHashCode() => (int)_templateType + (_dataType?.GetHashCode() ?? 0);

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}({1})",
        GetType().Name,
        _dataType?.ToString() ?? "null");

    /// <summary>Distinguishes the supported template key families.</summary>
    protected enum TemplateType
    {
        DataTemplate,
        TableTemplate,
    }

    internal static void ValidateDataType(object? dataType, string parameterName)
    {
        if (dataType == null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (dataType is not Type && dataType is not string)
        {
            throw new ArgumentException("A template data type must be a Type or a string.", parameterName);
        }

        if (Equals(dataType, typeof(object)))
        {
            throw new ArgumentException("System.Object is not a valid template data type.", parameterName);
        }
    }
}

/// <summary>
/// Identifies the implicit data template for a data type or XML tag name.
/// </summary>
public class DataTemplateKey : TemplateKey
{
    /// <summary>Initializes an unassigned key for XAML construction.</summary>
    public DataTemplateKey()
        : base(TemplateType.DataTemplate)
    {
    }

    /// <summary>Initializes a key for the supplied data type.</summary>
    public DataTemplateKey(object dataType)
        : base(TemplateType.DataTemplate, dataType)
    {
    }
}
