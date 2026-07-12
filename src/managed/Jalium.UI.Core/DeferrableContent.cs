namespace Jalium.UI;

/// <summary>
/// Represents XAML content whose resource values can be created when first requested.
/// </summary>
/// <remarks>
/// Instances are produced by the Jalium XAML loader. The type is public because a XAML
/// object writer must be able to assign it to <see cref="ResourceDictionary.DeferrableContent"/>,
/// while construction remains an implementation detail of the loader.
/// </remarks>
public class DeferrableContent
{
    private readonly byte[] _payload;

    internal DeferrableContent()
        : this(Array.Empty<byte>())
    {
    }

    internal DeferrableContent(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _payload = payload.ToArray();
    }

    internal Stream OpenRead() => new MemoryStream(_payload, writable: false);

    internal int Length => _payload.Length;
}
