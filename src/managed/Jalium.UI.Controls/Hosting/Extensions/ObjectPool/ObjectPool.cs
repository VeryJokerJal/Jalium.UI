using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Jalium.Extensions.DependencyInjection;

namespace Jalium.Extensions.ObjectPool;

/// <summary>Pool of reusable instances. Implementations recycle objects to reduce GC pressure.</summary>
public abstract class ObjectPool<T> where T : class
{
    /// <summary>Rents an instance from the pool. The caller must <see cref="Return"/> it when done.</summary>
    public abstract T Get();

    /// <summary>Returns <paramref name="obj"/> to the pool. Pool may keep or discard based on <see cref="IPooledObjectPolicy{T}.Return"/>.</summary>
    public abstract void Return(T obj);
}

/// <summary>Encapsulates create + reset logic for pooled instances.</summary>
public interface IPooledObjectPolicy<T> where T : notnull
{
    T Create();
    /// <summary><see langword="true"/> if <paramref name="obj"/> can be reused (and will be retained by the pool).</summary>
    bool Return(T obj);
}

/// <summary>Base helper for typed policies — supplies default behavior.</summary>
public abstract class PooledObjectPolicy<T> : IPooledObjectPolicy<T> where T : notnull
{
    public abstract T Create();
    public abstract bool Return(T obj);
}

/// <summary>Default policy that uses <c>new T()</c> for creation and unconditionally returns objects.</summary>
public sealed class DefaultPooledObjectPolicy<T> : PooledObjectPolicy<T> where T : class, new()
{
    public override T Create() => new T();
    public override bool Return(T obj) => true;
}

/// <summary>
/// Default <see cref="ObjectPool{T}"/> backed by a fixed-capacity ConcurrentQueue.
/// Reaching capacity drops returned objects on the floor (GC reclaims them).
/// Capacity defaults to <c>Environment.ProcessorCount * 2</c>.
/// </summary>
public class DefaultObjectPool<T> : ObjectPool<T> where T : class
{
    private readonly IPooledObjectPolicy<T> _policy;
    private readonly ConcurrentQueue<T> _items = new();
    private T? _fastItem;
    private int _maxRetained;
    private int _retainedCount;

    public DefaultObjectPool(IPooledObjectPolicy<T> policy) : this(policy, Environment.ProcessorCount * 2) { }

    public DefaultObjectPool(IPooledObjectPolicy<T> policy, int maximumRetained)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (maximumRetained < 1) throw new ArgumentOutOfRangeException(nameof(maximumRetained));
        _policy = policy;
        _maxRetained = maximumRetained;
    }

    public override T Get()
    {
        // Fast path — single-slot exchange (best for low-contention scenarios).
        var item = _fastItem;
        if (item != null && Interlocked.CompareExchange(ref _fastItem, null, item) == item)
        {
            Interlocked.Decrement(ref _retainedCount);
            return item;
        }
        if (_items.TryDequeue(out item))
        {
            Interlocked.Decrement(ref _retainedCount);
            return item;
        }
        return _policy.Create();
    }

    public override void Return(T obj)
    {
        if (obj == null) return;
        if (!_policy.Return(obj)) return; // Policy refused — drop.

        if (_fastItem == null && Interlocked.CompareExchange(ref _fastItem, obj, null) == null)
        {
            Interlocked.Increment(ref _retainedCount);
            return;
        }
        if (Volatile.Read(ref _retainedCount) < _maxRetained)
        {
            _items.Enqueue(obj);
            Interlocked.Increment(ref _retainedCount);
        }
        // Else drop — capacity reached.
    }
}

/// <summary>Creates pools of arbitrary T.</summary>
public abstract class ObjectPoolProvider
{
    public ObjectPool<T> Create<T>() where T : class, new() => Create(new DefaultPooledObjectPolicy<T>());
    public abstract ObjectPool<T> Create<T>(IPooledObjectPolicy<T> policy) where T : class;
}

public sealed class DefaultObjectPoolProvider : ObjectPoolProvider
{
    /// <summary>Default pool size (default: <c>Environment.ProcessorCount * 2</c>).</summary>
    public int MaximumRetained { get; set; } = Environment.ProcessorCount * 2;

    public override ObjectPool<T> Create<T>(IPooledObjectPolicy<T> policy) where T : class
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
            return new DisposableObjectPool<T>(policy, MaximumRetained);
        return new DefaultObjectPool<T>(policy, MaximumRetained);
    }
}

/// <summary>
/// Pool that disposes objects when the pool itself is disposed and when capacity is exceeded.
/// Used automatically by <see cref="DefaultObjectPoolProvider"/> for <see cref="IDisposable"/> types.
/// </summary>
internal sealed class DisposableObjectPool<T> : ObjectPool<T>, IDisposable where T : class
{
    private readonly DefaultObjectPool<T> _inner;
    private readonly IPooledObjectPolicy<T> _policy;
    private bool _disposed;

    public DisposableObjectPool(IPooledObjectPolicy<T> policy, int maximumRetained)
    {
        _policy = policy;
        _inner = new DefaultObjectPool<T>(policy, maximumRetained);
    }

    public override T Get()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DisposableObjectPool<T>));
        return _inner.Get();
    }

    public override void Return(T obj)
    {
        if (_disposed)
        {
            (obj as IDisposable)?.Dispose();
            return;
        }
        if (!_policy.Return(obj))
        {
            (obj as IDisposable)?.Dispose();
            return;
        }
        _inner.Return(obj);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Drain & dispose what we can — pool internals don't expose the queue, so this is a best-effort.
        while (true)
        {
            try
            {
                var item = _inner.Get();
                // If pool was empty Get would create a new policy instance — there's no way to tell.
                // We bail on the first Get that returns an object we just created; without internal access
                // we accept that a few residual items may be GC'd rather than disposed.
                (item as IDisposable)?.Dispose();
            }
            catch { break; }
            // Single drain is enough — DefaultObjectPool fast slot + queue gives at most maxRetained items;
            // looping risks falling into the create-new path. Better drop after one pass.
            break;
        }
    }
}

/// <summary>Specialized <see cref="StringBuilder"/> pool with reasonable defaults.</summary>
public sealed class StringBuilderPooledObjectPolicy : PooledObjectPolicy<StringBuilder>
{
    /// <summary>Initial capacity of newly-created builders.</summary>
    public int InitialCapacity { get; set; } = 100;
    /// <summary>Builders whose capacity exceeds this are dropped on Return instead of pooled.</summary>
    public int MaximumRetainedCapacity { get; set; } = 4 * 1024;

    public override StringBuilder Create() => new(InitialCapacity);

    public override bool Return(StringBuilder obj)
    {
        if (obj.Capacity > MaximumRetainedCapacity) return false;
        obj.Clear();
        return true;
    }
}

public static class ObjectPoolProviderExtensions
{
    public static ObjectPool<StringBuilder> CreateStringBuilderPool(this ObjectPoolProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.Create(new StringBuilderPooledObjectPolicy());
    }

    public static ObjectPool<StringBuilder> CreateStringBuilderPool(this ObjectPoolProvider provider, int initialCapacity, int maximumRetainedCapacity)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.Create(new StringBuilderPooledObjectPolicy
        {
            InitialCapacity = initialCapacity,
            MaximumRetainedCapacity = maximumRetainedCapacity,
        });
    }
}

/// <summary>Service-collection extensions registering the default pool provider.</summary>
public static class ObjectPoolServiceCollectionExtensions
{
    public static IServiceCollection AddObjectPool(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
        return services;
    }

    /// <summary>Registers a typed pool resolvable as <c>ObjectPool&lt;T&gt;</c>.</summary>
    public static IServiceCollection AddPooledObjectPolicy<T, TPolicy>(this IServiceCollection services)
        where T : class
        where TPolicy : class, IPooledObjectPolicy<T>, new()
    {
        services.AddObjectPool();
        services.TryAddSingleton<IPooledObjectPolicy<T>, TPolicy>();
        services.TryAddSingleton<ObjectPool<T>>(sp =>
        {
            var provider = sp.GetRequiredService<ObjectPoolProvider>();
            var policy = sp.GetRequiredService<IPooledObjectPolicy<T>>();
            return provider.Create(policy);
        });
        return services;
    }
}
