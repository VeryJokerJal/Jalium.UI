using System.Text;
using Jalium.Extensions.Caching.Memory;
using Jalium.Extensions.FileSystemGlobbing;
using Jalium.Extensions.Logging;
using Jalium.Extensions.ObjectPool;
using Jalium.Extensions.Primitives;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class ObjectPoolTests
{
    private sealed class Tracked { public int Hits; public override string ToString() => $"Tracked#{Hits}"; }
    private sealed class TrackedPolicy : PooledObjectPolicy<Tracked>
    {
        public override Tracked Create() => new();
        public override bool Return(Tracked obj) { obj.Hits++; return true; }
    }

    [Fact]
    public void DefaultObjectPool_ReusesReturnedInstance()
    {
        var pool = new DefaultObjectPool<Tracked>(new TrackedPolicy());
        var a = pool.Get();
        pool.Return(a);
        var b = pool.Get();
        Assert.Same(a, b);
        Assert.Equal(1, b.Hits);
    }

    [Fact]
    public void StringBuilderPool_ClearsOnReturn()
    {
        var pool = new DefaultObjectPoolProvider().CreateStringBuilderPool();
        var sb = pool.Get();
        sb.Append("hello");
        pool.Return(sb);
        var sb2 = pool.Get();
        Assert.Equal(0, sb2.Length);
        Assert.Same(sb, sb2);
    }

    [Fact]
    public void DefaultObjectPool_RespectsMaxRetained()
    {
        // maxRetained=1: 第1个进 fast slot,第2个进队列,第3个被丢弃
        var pool = new DefaultObjectPool<Tracked>(new TrackedPolicy(), maximumRetained: 1);
        var a = pool.Get(); var b = pool.Get(); var c = pool.Get();
        pool.Return(a); pool.Return(b); pool.Return(c);
        // 容量 1 限制 — pool 拥有至多 1 个实例
        var x = pool.Get();
        var y = pool.Get();
        Assert.NotSame(x, y); // y 必是新建,因为只有 1 个被保留
    }
}

public sealed class MemoryCacheTests
{
    [Fact]
    public void Set_Get_RoundTrip()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("k", 42);
        Assert.True(cache.TryGetValue("k", out var v));
        Assert.Equal(42, v);
    }

    [Fact]
    public void AbsoluteExpiration_RemovesAfterDeadline()
    {
        var clock = new TestClock { Now = DateTimeOffset.UtcNow };
        using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        cache.Set("k", "v", clock.Now + TimeSpan.FromSeconds(10));
        Assert.True(cache.TryGetValue("k", out _));
        clock.Now += TimeSpan.FromSeconds(11);
        Assert.False(cache.TryGetValue("k", out _));
    }

    [Fact]
    public void SlidingExpiration_RenewsOnAccess()
    {
        var clock = new TestClock { Now = DateTimeOffset.UtcNow };
        using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        using (var e = cache.CreateEntry("k")) { e.Value = "v"; e.SlidingExpiration = TimeSpan.FromSeconds(5); }
        // 4s after — refresh
        clock.Now += TimeSpan.FromSeconds(4);
        Assert.True(cache.TryGetValue("k", out _));
        // Another 4s — within sliding window from last access
        clock.Now += TimeSpan.FromSeconds(4);
        Assert.True(cache.TryGetValue("k", out _));
        // 6s of inactivity — expires
        clock.Now += TimeSpan.FromSeconds(6);
        Assert.False(cache.TryGetValue("k", out _));
    }

    [Fact]
    public void Remove_InvokesPostEvictionCallback()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        EvictionReason? captured = null;
        using (var e = cache.CreateEntry("k"))
        {
            e.Value = "v";
            e.RegisterPostEvictionCallback((_, _, reason, _) => captured = reason);
        }
        cache.Remove("k");
        Assert.Equal(EvictionReason.Removed, captured);
    }

    [Fact]
    public void GetOrCreate_FactoryRunsOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        int calls = 0;
        for (int i = 0; i < 5; i++)
        {
            cache.GetOrCreate("k", _ => { calls++; return 7; });
        }
        Assert.Equal(1, calls);
    }

    private sealed class TestClock : ISystemClock { public DateTimeOffset Now { get; set; } public DateTimeOffset UtcNow => Now; }
}

public sealed class StringSegmentTests
{
    [Fact]
    public void Subsegment_ReturnsCorrectRange()
    {
        var s = new StringSegment("hello world", 6, 5);
        Assert.Equal(5, s.Length);
        Assert.Equal("world", s.ToString());
        var sub = s.Subsegment(0, 3);
        Assert.Equal("wor", sub.ToString());
    }

    [Fact]
    public void Equals_RespectsComparison()
    {
        var a = new StringSegment("Foo");
        Assert.True(a.Equals("FOO", StringComparison.OrdinalIgnoreCase));
        Assert.False(a.Equals("FOO", StringComparison.Ordinal));
    }

    [Fact]
    public void Trim_StripsWhitespace()
    {
        var s = new StringSegment("  hi  ");
        Assert.Equal("hi", s.Trim().ToString());
    }
}

public sealed class StringTokenizerTests
{
    [Fact]
    public void Iterates_CommaSeparated()
    {
        var t = new StringTokenizer("a,b,,c", new[] { ',' });
        var parts = t.Select(s => s.ToString()).ToArray();
        Assert.Equal(new[] { "a", "b", "", "c" }, parts);
    }
}

public sealed class StringValuesTests
{
    [Fact]
    public void EmptyAndSingleValue_NoAllocation_Semantics()
    {
        StringValues empty = default;
        Assert.Equal(0, empty.Count);
        Assert.True(StringValues.IsNullOrEmpty(empty));

        StringValues one = "hello";
        Assert.Equal(1, one.Count);
        Assert.Equal("hello", one[0]);

        StringValues many = new[] { "a", "b", "c" };
        Assert.Equal(3, many.Count);
        Assert.Equal("a,b,c", many.ToString());
    }

    [Fact]
    public void Concat_MergesArrays()
    {
        var combined = StringValues.Concat("x", new[] { "y", "z" });
        Assert.Equal(new[] { "x", "y", "z" }, combined.ToArray());
    }
}

public sealed class GlobbingTests
{
    [Fact]
    public void StarStar_MatchesAtAnyDepth()
    {
        var m = new Matcher();
        m.AddInclude("**/*.cs");
        Assert.True(m.Match("a.cs").HasMatches);
        Assert.True(m.Match("a/b.cs").HasMatches);
        Assert.True(m.Match("a/b/c/d.cs").HasMatches);
        Assert.False(m.Match("a.txt").HasMatches);
    }

    [Fact]
    public void Exclude_FiltersInclude()
    {
        var m = new Matcher();
        m.AddInclude("**/*.cs");
        m.AddExclude("**/bin/**");
        Assert.True(m.Match("src/foo.cs").HasMatches);
        Assert.False(m.Match("bin/foo.cs").HasMatches);
        Assert.False(m.Match("a/bin/b/foo.cs").HasMatches);
    }

    [Fact]
    public void SingleStar_DoesNotCrossSlash()
    {
        var m = new Matcher();
        m.AddInclude("foo/*.cs");
        Assert.True(m.Match("foo/a.cs").HasMatches);
        Assert.False(m.Match("foo/bar/a.cs").HasMatches);
    }
}

public sealed class LoggerMessageRuntimeTests
{
    [Fact]
    public void Define_NoArgs_LogsFormattedMessage()
    {
        var capture = new CapturingLogger();
        var log = LoggerMessage.Define(LogLevel.Information, new EventId(1), "Hello world");
        log(capture, null);
        Assert.Single(capture.Entries);
        Assert.Equal("Hello world", capture.Entries[0].Message);
        Assert.Equal(LogLevel.Information, capture.Entries[0].Level);
        Assert.Equal(1, capture.Entries[0].EventId.Id);
    }

    [Fact]
    public void Define_TwoArgs_InterpolatesPlaceholders()
    {
        var capture = new CapturingLogger();
        var log = LoggerMessage.Define<int, string>(LogLevel.Warning, new EventId(7, "Boom"), "User {UserId} from {Host}");
        log(capture, 42, "localhost", null);
        Assert.Equal("User 42 from localhost", capture.Entries[0].Message);
    }

    [Fact]
    public void Define_RespectsIsEnabled()
    {
        var capture = new CapturingLogger { Enabled = false };
        var log = LoggerMessage.Define<int>(LogLevel.Error, 0, "x={X}");
        log(capture, 1, null);
        Assert.Empty(capture.Entries);
    }

    private sealed class CapturingLogger : ILogger
    {
        public bool Enabled { get; set; } = true;
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
