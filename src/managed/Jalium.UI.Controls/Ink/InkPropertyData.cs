using System.Collections;

namespace Jalium.UI.Ink;

internal static class InkPropertyData
{
    private static readonly HashSet<Type> s_supportedScalarTypes =
    [
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(bool), typeof(char),
        typeof(string), typeof(decimal), typeof(DateTime),
    ];

    internal static void Validate(Guid id, object? value)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The property identifier cannot be empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();
        if (s_supportedScalarTypes.Contains(type))
            return;
        if (type.IsArray && type.GetArrayRank() == 1 &&
            type.GetElementType() != typeof(sbyte) &&
            type.GetElementType() != typeof(string) &&
            s_supportedScalarTypes.Contains(type.GetElementType()!))
        {
            return;
        }

        throw new ArgumentException(
            $"Values of type '{type.FullName}' cannot be stored as ink property data.",
            nameof(value));
    }

    internal static object CloneValue(object value) => value is Array array ? array.Clone() : value;

    internal static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.GetType() != right.GetType())
            return false;
        if (left is Array leftArray && right is Array rightArray)
            return StructuralComparisons.StructuralEqualityComparer.Equals(leftArray, rightArray);
        return left.Equals(right);
    }

    internal static bool DictionariesEqual(
        IReadOnlyDictionary<Guid, object> left,
        IReadOnlyDictionary<Guid, object> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach ((Guid id, object value) in left)
        {
            if (!right.TryGetValue(id, out object? other) || !ValuesEqual(value, other))
                return false;
        }
        return true;
    }
}
