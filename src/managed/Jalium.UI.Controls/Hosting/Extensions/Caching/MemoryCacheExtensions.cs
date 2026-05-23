using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Options;

namespace Jalium.Extensions.Caching.Memory;

/// <summary>Convenience surface for <see cref="IMemoryCache"/>. Mirrors MS extension methods.</summary>
public static class CacheExtensions
{
    public static object? Get(this IMemoryCache cache, object key)
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.TryGetValue(key, out var v);
        return v;
    }

    public static TItem? Get<TItem>(this IMemoryCache cache, object key)
    {
        if (cache.TryGetValue(key, out var v) && v is TItem t) return t;
        return default;
    }

    public static bool TryGetValue<TItem>(this IMemoryCache cache, object key, out TItem? value)
    {
        if (cache.TryGetValue(key, out var v) && v is TItem t)
        {
            value = t;
            return true;
        }
        value = default;
        return false;
    }

    public static TItem Set<TItem>(this IMemoryCache cache, object key, TItem value)
    {
        using var entry = cache.CreateEntry(key);
        entry.Value = value;
        return value;
    }

    public static TItem Set<TItem>(this IMemoryCache cache, object key, TItem value, DateTimeOffset absoluteExpiration)
    {
        using var entry = cache.CreateEntry(key);
        entry.AbsoluteExpiration = absoluteExpiration;
        entry.Value = value;
        return value;
    }

    public static TItem Set<TItem>(this IMemoryCache cache, object key, TItem value, TimeSpan absoluteExpirationRelativeToNow)
    {
        using var entry = cache.CreateEntry(key);
        entry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
        entry.Value = value;
        return value;
    }

    public static TItem Set<TItem>(this IMemoryCache cache, object key, TItem value, Jalium.Extensions.Primitives.IChangeToken expirationToken)
    {
        using var entry = cache.CreateEntry(key);
        entry.ExpirationTokens.Add(expirationToken);
        entry.Value = value;
        return value;
    }

    public static TItem Set<TItem>(this IMemoryCache cache, object key, TItem value, MemoryCacheEntryOptions? options)
    {
        using var entry = cache.CreateEntry(key);
        if (options != null) entry.SetOptions(options);
        entry.Value = value;
        return value;
    }

    public static TItem? GetOrCreate<TItem>(this IMemoryCache cache, object key, Func<ICacheEntry, TItem> factory)
    {
        if (!cache.TryGetValue(key, out var result))
        {
            using var entry = cache.CreateEntry(key);
            result = factory(entry);
            entry.Value = result;
        }
        return (TItem?)result;
    }

    public static async Task<TItem?> GetOrCreateAsync<TItem>(this IMemoryCache cache, object key, Func<ICacheEntry, Task<TItem>> factory)
    {
        if (!cache.TryGetValue(key, out var result))
        {
            using var entry = cache.CreateEntry(key);
            result = await factory(entry).ConfigureAwait(false);
            entry.Value = result;
        }
        return (TItem?)result;
    }
}

public static class MemoryCacheEntryExtensions
{
    public static ICacheEntry SetAbsoluteExpiration(this ICacheEntry entry, DateTimeOffset absolute)
    { entry.AbsoluteExpiration = absolute; return entry; }
    public static ICacheEntry SetAbsoluteExpiration(this ICacheEntry entry, TimeSpan relative)
    { entry.AbsoluteExpirationRelativeToNow = relative; return entry; }
    public static ICacheEntry SetSlidingExpiration(this ICacheEntry entry, TimeSpan offset)
    { entry.SlidingExpiration = offset; return entry; }
    public static ICacheEntry SetPriority(this ICacheEntry entry, CacheItemPriority priority)
    { entry.Priority = priority; return entry; }
    public static ICacheEntry SetSize(this ICacheEntry entry, long size)
    { entry.Size = size; return entry; }
    public static ICacheEntry SetValue(this ICacheEntry entry, object? value)
    { entry.Value = value; return entry; }
    public static ICacheEntry AddExpirationToken(this ICacheEntry entry, Jalium.Extensions.Primitives.IChangeToken expirationToken)
    { entry.ExpirationTokens.Add(expirationToken); return entry; }
    public static ICacheEntry RegisterPostEvictionCallback(this ICacheEntry entry, PostEvictionDelegate callback, object? state = null)
    {
        entry.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration { EvictionCallback = callback, State = state });
        return entry;
    }
    public static ICacheEntry SetOptions(this ICacheEntry entry, MemoryCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        entry.AbsoluteExpiration = options.AbsoluteExpiration;
        entry.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
        entry.SlidingExpiration = options.SlidingExpiration;
        foreach (var t in options.ExpirationTokens) entry.ExpirationTokens.Add(t);
        foreach (var cb in options.PostEvictionCallbacks) entry.PostEvictionCallbacks.Add(cb);
        entry.Priority = options.Priority;
        entry.Size = options.Size;
        return entry;
    }
}

public static class MemoryCacheServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions();
        services.TryAddSingleton<IMemoryCache, MemoryCache>();
        return services;
    }

    public static IServiceCollection AddMemoryCache(this IServiceCollection services, Action<MemoryCacheOptions> setupAction)
    {
        ArgumentNullException.ThrowIfNull(setupAction);
        services.AddMemoryCache();
        services.Configure(setupAction);
        return services;
    }
}
