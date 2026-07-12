using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace Jalium.UI;

/// <summary>
/// Provides type-safe access to data stored in an <see cref="IDataObject"/>.
/// </summary>
public interface ITypedDataObject : IDataObject
{
    bool TryGetData<T>([NotNullWhen(true), MaybeNullWhen(false)] out T data);

    bool TryGetData<T>(
        string format,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data);

    bool TryGetData<T>(
        string format,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data);

    bool TryGetData<T>(
        string format,
        Func<TypeName, Type?> resolver,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data);
}

/// <summary>
/// Provides type-safe retrieval extensions for any <see cref="IDataObject"/>.
/// </summary>
public static class DataObjectExtensions
{
    public static bool TryGetData<T>(
        this IDataObject dataObject,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data) =>
        GetTypedDataObjectOrThrow(dataObject).TryGetData(out data);

    public static bool TryGetData<T>(
        this IDataObject dataObject,
        string format,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data) =>
        GetTypedDataObjectOrThrow(dataObject).TryGetData(format, out data);

    public static bool TryGetData<T>(
        this IDataObject dataObject,
        string format,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data) =>
        GetTypedDataObjectOrThrow(dataObject).TryGetData(format, autoConvert, out data);

    public static bool TryGetData<T>(
        this IDataObject dataObject,
        string format,
        Func<TypeName, Type?> resolver,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data) =>
        GetTypedDataObjectOrThrow(dataObject).TryGetData(format, resolver, autoConvert, out data);

    private static ITypedDataObject GetTypedDataObjectOrThrow(IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        if (dataObject is not ITypedDataObject typedDataObject)
        {
            throw new NotSupportedException(
                $"The data object type '{dataObject.GetType().FullName}' does not implement {nameof(ITypedDataObject)}.");
        }

        return typedDataObject;
    }
}
