using System.Collections.Concurrent;

namespace Jalium.UI;

/// <summary>
/// Implements an underlying type cache for all DependencyObject derived types.
/// </summary>
public sealed class DependencyObjectType
{
    private static readonly ConcurrentDictionary<Type, DependencyObjectType> _typeMap = new();
    private static int _currentId;

    private DependencyObjectType(Type systemType)
    {
        SystemType = systemType;
        Id = Interlocked.Increment(ref _currentId);
        var baseType = systemType.BaseType;
        if (baseType != null && typeof(DependencyObject).IsAssignableFrom(baseType))
            BaseType = FromSystemType(baseType);
    }

    public int Id { get; }
    public Type SystemType { get; }
    public DependencyObjectType? BaseType { get; }
    public string Name => SystemType.Name;

    public static DependencyObjectType FromSystemType(Type systemType)
    {
        return _typeMap.GetOrAdd(systemType, t => new DependencyObjectType(t));
    }

    public bool IsSubclassOf(DependencyObjectType dependencyObjectType)
    {
        var t = this;
        while (t != null)
        {
            if (t == dependencyObjectType) return true;
            t = t.BaseType;
        }
        return false;
    }

    public bool IsInstanceOfType(DependencyObject dependencyObject)
    {
        return SystemType.IsInstanceOfType(dependencyObject);
    }

    public override int GetHashCode() => Id;
    public override string ToString() => SystemType.FullName ?? SystemType.Name;
}
