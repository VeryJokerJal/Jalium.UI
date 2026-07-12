using System.Runtime.Serialization;

namespace Jalium.UI;

/// <summary>
/// The exception thrown when a resource reference cannot find its resource key.
/// </summary>
[Serializable]
public class ResourceReferenceKeyNotFoundException : InvalidOperationException
{
    private readonly object? _resourceKey;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ResourceReferenceKeyNotFoundException"/> class.
    /// </summary>
    public ResourceReferenceKeyNotFoundException()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified message and missing resource key.
    /// </summary>
    public ResourceReferenceKeyNotFoundException(string? message, object? resourceKey)
        : base(message)
    {
        _resourceKey = resourceKey;
    }

#pragma warning disable SYSLIB0051 // Formatter-based serialization is required by the WPF-compatible API.
    /// <summary>
    /// Initializes a new instance from serialized data.
    /// </summary>
    protected ResourceReferenceKeyNotFoundException(
        SerializationInfo info,
        StreamingContext context)
        : base(info, context)
    {
        _resourceKey = info.GetValue("Key", typeof(object));
    }
#pragma warning restore SYSLIB0051

    /// <summary>
    /// Gets the resource key that could not be found.
    /// </summary>
    public object? Key => _resourceKey;

#pragma warning disable CS0672 // Override is required by the WPF-compatible API.
#pragma warning disable SYSLIB0051 // Formatter-based serialization is required by the WPF-compatible API.
    /// <inheritdoc />
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue("Key", _resourceKey);
    }
#pragma warning restore SYSLIB0051
#pragma warning restore CS0672
}
