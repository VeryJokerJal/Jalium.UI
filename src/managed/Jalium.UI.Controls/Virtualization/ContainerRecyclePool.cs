namespace Jalium.UI.Controls.Virtualization;

/// <summary>
/// Type-aware recycle pool for item containers.
/// </summary>
internal sealed class ContainerRecyclePool
{
    private readonly Dictionary<Type, Stack<DependencyObject>> _pools = new();

    public int Count { get; private set; }

    public void Clear()
    {
        _pools.Clear();
        Count = 0;
    }

    public void Push(DependencyObject container)
    {
        var type = container.GetType();
        if (!_pools.TryGetValue(type, out var pool))
        {
            pool = new Stack<DependencyObject>();
            _pools[type] = pool;
        }

        pool.Push(container);
        Count++;
    }

    public bool TryPop(Type preferredType, out DependencyObject? container)
    {
        if (_pools.TryGetValue(preferredType, out var exact) && exact.Count > 0)
        {
            container = exact.Pop();
            Count--;
            return true;
        }

        foreach (var entry in _pools)
        {
            if (entry.Value.Count > 0)
            {
                container = entry.Value.Pop();
                Count--;
                return true;
            }
        }

        container = null;
        return false;
    }
}

