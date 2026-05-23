using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Caching.Memory;

/// <summary>In-process cache contract — key/value entries with optional eviction policies.</summary>
public interface IMemoryCache : IDisposable
{
    bool TryGetValue(object key, out object? value);
    /// <summary>Creates a new entry. Setting properties on the entry then disposing it commits to the cache.</summary>
    ICacheEntry CreateEntry(object key);
    void Remove(object key);
}

/// <summary>
/// In-flight cache entry. Properties are mutable until <see cref="IDisposable.Dispose"/> is called,
/// at which point the entry is committed to its owning <see cref="IMemoryCache"/>.
/// </summary>
public interface ICacheEntry : IDisposable
{
    object Key { get; }
    object? Value { get; set; }
    DateTimeOffset? AbsoluteExpiration { get; set; }
    TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
    TimeSpan? SlidingExpiration { get; set; }
    IList<IChangeToken> ExpirationTokens { get; }
    IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; }
    CacheItemPriority Priority { get; set; }
    long? Size { get; set; }
}

public enum CacheItemPriority { Low, Normal, High, NeverRemove }

public enum EvictionReason
{
    None,
    Removed,        // Manually removed (or replaced).
    Replaced,       // Replaced via CreateEntry on existing key.
    Expired,        // Time-based.
    TokenExpired,   // One of ExpirationTokens fired.
    Capacity,       // Compaction (size-limit reached).
}

public delegate void PostEvictionDelegate(object key, object? value, EvictionReason reason, object? state);

public sealed class PostEvictionCallbackRegistration
{
    public PostEvictionDelegate? EvictionCallback { get; set; }
    public object? State { get; set; }
}

/// <summary>Snapshot of options applied to a <see cref="ICacheEntry"/>. Mirrors MS <c>MemoryCacheEntryOptions</c>.</summary>
public sealed class MemoryCacheEntryOptions
{
    public DateTimeOffset? AbsoluteExpiration { get; set; }
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
    public TimeSpan? SlidingExpiration { get; set; }
    public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();
    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = new List<PostEvictionCallbackRegistration>();
    public CacheItemPriority Priority { get; set; } = CacheItemPriority.Normal;
    public long? Size { get; set; }
}

public sealed class MemoryCacheOptions
{
    /// <summary>Soft size limit. When the sum of <see cref="ICacheEntry.Size"/> exceeds this value the cache compacts.</summary>
    public long? SizeLimit { get; set; }
    /// <summary>Fraction of entries to evict during compaction (0.0–1.0). Default 0.05 = 5%.</summary>
    public double CompactionPercentage { get; set; } = 0.05;
    /// <summary>How often <see cref="MemoryCache"/> scans for expired entries (default 1 minute).</summary>
    public TimeSpan ExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>System clock — mostly for unit tests.</summary>
    public ISystemClock? Clock { get; set; }
    /// <summary>When <see langword="true"/>, track entry insertion order for LRU compaction.</summary>
    public bool TrackLinkedCacheEntries { get; set; }
}

/// <summary>Indirection over <see cref="DateTimeOffset.UtcNow"/> — used for testable expiration.</summary>
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
