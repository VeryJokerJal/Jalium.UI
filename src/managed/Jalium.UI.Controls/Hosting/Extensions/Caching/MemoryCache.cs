using System.Collections.Concurrent;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Options;
using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Caching.Memory;

/// <summary>
/// In-process key/value cache. Mirrors <c>Microsoft.Extensions.Caching.Memory.MemoryCache</c>
/// behavior: lock-free reads + lazy expiration scan + capacity-driven compaction.
/// </summary>
public sealed class MemoryCache : IMemoryCache
{
    private readonly ConcurrentDictionary<object, CacheEntry> _entries = new();
    private readonly MemoryCacheOptions _options;
    private readonly ISystemClock _clock;
    private DateTimeOffset _lastExpirationScan;
    private long _currentSize;
    private bool _disposed;
    private readonly object _scanLock = new();

    public MemoryCache(MemoryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _clock = options.Clock ?? new SystemClock();
        _lastExpirationScan = _clock.UtcNow;
    }

    public MemoryCache(IOptions<MemoryCacheOptions> options) : this(options.Value) { }

    public int Count => _entries.Count;

    public long Size => Interlocked.Read(ref _currentSize);

    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        StartScanForExpiredItems();

        if (_entries.TryGetValue(key, out var entry))
        {
            var now = _clock.UtcNow;
            if (entry.CheckExpired(now))
            {
                RemoveEntry(entry, EvictionReason.Expired);
                value = null;
                return false;
            }
            entry.LastAccessed = now;
            value = entry.Value;
            return true;
        }
        value = null;
        return false;
    }

    public ICacheEntry CreateEntry(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();
        return new CacheEntry(this, key);
    }

    public void Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();
        if (_entries.TryRemove(key, out var entry))
        {
            UpdateSizeOnRemoval(entry);
            entry.InvokeEvictionCallbacks(EvictionReason.Removed);
        }
    }

    public void Compact(double percentage)
    {
        if (percentage <= 0 || percentage > 1) throw new ArgumentOutOfRangeException(nameof(percentage));
        var target = (int)(_entries.Count * percentage);
        if (target <= 0) return;

        // LRU first, but pinned (NeverRemove) entries excluded.
        var candidates = _entries.Values
            .Where(e => e.Priority != CacheItemPriority.NeverRemove)
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.LastAccessed)
            .Take(target)
            .ToList();

        foreach (var c in candidates) RemoveEntry(c, EvictionReason.Capacity);
    }

    internal void SetEntry(CacheEntry entry)
    {
        if (_disposed) { entry.InvokeEvictionCallbacks(EvictionReason.Removed); return; }

        var now = _clock.UtcNow;
        entry.LastAccessed = now;

        // Resolve absolute expiration from relative-to-now.
        if (entry.AbsoluteExpirationRelativeToNow.HasValue)
            entry.AbsoluteExpiration = now + entry.AbsoluteExpirationRelativeToNow.Value;

        // Honor size limit on insert.
        if (_options.SizeLimit.HasValue)
        {
            if (!entry.Size.HasValue)
                throw new InvalidOperationException("Cache entries must specify Size when MemoryCacheOptions.SizeLimit is set.");
            if (entry.Size.Value > _options.SizeLimit.Value)
                throw new InvalidOperationException($"Cache entry size {entry.Size} exceeds SizeLimit {_options.SizeLimit}.");
        }

        // Replace existing under the same key.
        if (_entries.TryGetValue(entry.Key, out var existing))
        {
            UpdateSizeOnRemoval(existing);
            existing.InvokeEvictionCallbacks(EvictionReason.Replaced);
        }

        if (entry.CheckExpired(now))
        {
            entry.InvokeEvictionCallbacks(EvictionReason.Expired);
            return;
        }

        if (entry.Size.HasValue)
            Interlocked.Add(ref _currentSize, entry.Size.Value);

        _entries[entry.Key] = entry;

        // Wire change-token callbacks for token-based invalidation.
        foreach (var token in entry.ExpirationTokens)
        {
            if (!token.HasChanged && token.ActiveChangeCallbacks)
            {
                var reg = token.RegisterChangeCallback(static state =>
                {
                    var entry = (CacheEntry)state!;
                    entry.Owner.RemoveEntry(entry, EvictionReason.TokenExpired);
                }, entry);
                entry.AddTokenRegistration(reg);
            }
        }

        // Honor SizeLimit by compaction (defer to next read; expensive immediate sweep avoided).
        if (_options.SizeLimit.HasValue && Interlocked.Read(ref _currentSize) > _options.SizeLimit.Value)
        {
            Compact(_options.CompactionPercentage);
        }
    }

    private void RemoveEntry(CacheEntry entry, EvictionReason reason)
    {
        if (_entries.TryRemove(new KeyValuePair<object, CacheEntry>(entry.Key, entry)))
        {
            UpdateSizeOnRemoval(entry);
            entry.InvokeEvictionCallbacks(reason);
        }
    }

    private void UpdateSizeOnRemoval(CacheEntry entry)
    {
        if (entry.Size.HasValue) Interlocked.Add(ref _currentSize, -entry.Size.Value);
    }

    private void StartScanForExpiredItems()
    {
        var now = _clock.UtcNow;
        if (now - _lastExpirationScan < _options.ExpirationScanFrequency) return;
        if (!Monitor.TryEnter(_scanLock)) return;
        try
        {
            if (now - _lastExpirationScan < _options.ExpirationScanFrequency) return;
            _lastExpirationScan = now;
            ThreadPool.QueueUserWorkItem(static state =>
            {
                var (cache, scanTime) = ((MemoryCache, DateTimeOffset))state!;
                foreach (var kv in cache._entries)
                {
                    if (kv.Value.CheckExpired(scanTime))
                        cache.RemoveEntry(kv.Value, EvictionReason.Expired);
                }
            }, (this, now));
        }
        finally { Monitor.Exit(_scanLock); }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryCache));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _entries.Clear();
    }
}

internal sealed class CacheEntry : ICacheEntry
{
    public MemoryCache Owner { get; }
    public object Key { get; }
    public object? Value { get; set; }
    public DateTimeOffset? AbsoluteExpiration { get; set; }
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
    public TimeSpan? SlidingExpiration { get; set; }
    public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();
    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = new List<PostEvictionCallbackRegistration>();
    public CacheItemPriority Priority { get; set; } = CacheItemPriority.Normal;
    public long? Size { get; set; }
    public DateTimeOffset LastAccessed { get; set; }
    private readonly List<IDisposable> _tokenRegistrations = new();
    private bool _committed;

    public CacheEntry(MemoryCache owner, object key) { Owner = owner; Key = key; }

    public void AddTokenRegistration(IDisposable reg) => _tokenRegistrations.Add(reg);

    public bool CheckExpired(DateTimeOffset now)
    {
        if (AbsoluteExpiration.HasValue && now >= AbsoluteExpiration.Value) return true;
        if (SlidingExpiration.HasValue && now - LastAccessed >= SlidingExpiration.Value) return true;
        for (int i = 0; i < ExpirationTokens.Count; i++)
            if (ExpirationTokens[i].HasChanged) return true;
        return false;
    }

    public void InvokeEvictionCallbacks(EvictionReason reason)
    {
        foreach (var reg in _tokenRegistrations) { try { reg.Dispose(); } catch { } }
        _tokenRegistrations.Clear();
        for (int i = 0; i < PostEvictionCallbacks.Count; i++)
        {
            var cb = PostEvictionCallbacks[i];
            try { cb.EvictionCallback?.Invoke(Key, Value, reason, cb.State); } catch { }
        }
    }

    public void Dispose()
    {
        if (_committed) return;
        _committed = true;
        Owner.SetEntry(this);
    }
}
