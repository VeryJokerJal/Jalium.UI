using System.Collections;
using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class TextMeasurementRenderContextTests : IDisposable
{
    public TextMeasurementRenderContextTests()
    {
        ResetState();
    }

    [Fact]
    public void MeasureText_WhenRenderContextIsReplaced_ShouldDropStaleCachedFormats()
    {
        var firstContext = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);

        var firstText = new FormattedText("Menu", "Segoe UI", 13);
        Assert.True(TextMeasurement.MeasureText(firstText));
        Assert.Single(GetFormatCache().Keys.Cast<object>());

        var firstKey = GetOnlyCacheKey();
        Assert.Equal(firstContext.Generation, GetCacheKeyProperty<int>(firstKey, "ContextGeneration"));
        Assert.Equal("Segoe UI", GetCacheKeyProperty<string>(firstKey, "FontFamily"));

        var secondContext = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(firstContext, secondContext);
        Assert.Empty(GetFormatCache());

        var secondText = new FormattedText("Menu", "Segoe UI", 13);
        Assert.True(TextMeasurement.MeasureText(secondText));
        Assert.Single(GetFormatCache().Keys.Cast<object>());

        var secondKey = GetOnlyCacheKey();
        Assert.Equal(secondContext.Generation, GetCacheKeyProperty<int>(secondKey, "ContextGeneration"));
        Assert.Equal("Segoe UI", GetCacheKeyProperty<string>(secondKey, "FontFamily"));
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void MeasureText_WarmFormatAndResultHits_DoNotAllocateKeys()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var text = new FormattedText("Stable measurement", "Segoe UI", 14.25);
        Assert.True(TextMeasurement.MeasureText(text));

        for (int i = 0; i < 100; i++)
        {
            _ = TextMeasurement.MeasureText(text);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = TextMeasurement.MeasureText(text);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, 1024);
    }

    [Fact]
    public void GetFontMetrics_WarmHits_DoNotRepublishCacheState()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var expected = TextMeasurement.GetFontMetrics("Segoe UI", 13.25, 400, 0);
        var warmState = GetMetricsCacheState();

        for (int i = 0; i < 10_000; i++)
        {
            AssertMetricsEqual(expected, TextMeasurement.GetFontMetrics("Segoe UI", 13.25, 400, 0));
        }

        Assert.Same(warmState, GetMetricsCacheState());
    }

    [Fact]
    public void GetFontMetrics_WarmHits_DoNotAllocateKeys()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        _ = TextMeasurement.GetFontMetrics("Segoe UI", 16.25, 400, 1);
        for (int i = 0; i < 100; i++)
        {
            _ = TextMeasurement.GetFontMetrics("Segoe UI", 16.25, 400, 1);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = TextMeasurement.GetFontMetrics("Segoe UI", 16.25, 400, 1);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, 1024);
    }

    [Fact]
    public void ClearCache_DropsMetricsState_AndNextLookupRepopulatesIt()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        _ = TextMeasurement.GetFontMetrics("Segoe UI", 15.5, 600, 1);
        var populatedState = GetMetricsCacheState();
        Assert.True(GetMetricsEntryCount(populatedState) > 0);

        TextMeasurement.ClearCache();

        var emptyState = GetMetricsCacheState();
        Assert.NotSame(populatedState, emptyState);
        Assert.Equal(0, GetMetricsEntryCount(emptyState));

        _ = TextMeasurement.GetFontMetrics("Segoe UI", 15.5, 600, 1);
        var repopulatedState = GetMetricsCacheState();
        Assert.NotSame(emptyState, repopulatedState);
        Assert.True(GetMetricsEntryCount(repopulatedState) > 0);
    }

    [Fact]
    public void GetFontMetrics_ConcurrentWarmHits_AreStable()
    {
        _ = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var expected = TextMeasurement.GetFontMetrics("Segoe UI", 17.75, 700, 0);
        var warmState = GetMetricsCacheState();
        var results = new TextMetrics[2_000];

        Parallel.For(0, results.Length, i =>
        {
            results[i] = TextMeasurement.GetFontMetrics("Segoe UI", 17.75, 700, 0);
        });

        Assert.All(results, actual => AssertMetricsEqual(expected, actual));
        Assert.Same(warmState, GetMetricsCacheState());
    }

    public void Dispose()
    {
        ResetState();
    }

    private static IDictionary GetFormatCache()
    {
        var field = typeof(TextMeasurement).GetField("_formatCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return Assert.IsAssignableFrom<IDictionary>(value);
    }

    private static object GetOnlyCacheKey()
    {
        var cache = GetFormatCache();
        Assert.Single(cache.Keys);
        var key = cache.Keys.Cast<object>().Single();
        Assert.True(key.GetType().IsValueType);
        return key;
    }

    private static T GetCacheKeyProperty<T>(object key, string propertyName)
        => Assert.IsType<T>(key.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(key));

    private static object GetMetricsCacheState()
    {
        var field = typeof(TextMeasurement).GetField(
            "_metricsCacheState",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return value!;
    }

    private static int GetMetricsEntryCount(object state)
    {
        var entriesProperty = state.GetType().GetProperty(
            "Entries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(entriesProperty);
        var entries = entriesProperty!.GetValue(state);
        Assert.NotNull(entries);
        var countProperty = entries!.GetType().GetProperty("Count");
        Assert.NotNull(countProperty);
        return Assert.IsType<int>(countProperty!.GetValue(entries));
    }

    private static void AssertMetricsEqual(TextMetrics expected, TextMetrics actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.WidthIncludingTrailingWhitespace, actual.WidthIncludingTrailingWhitespace);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.LineHeight, actual.LineHeight);
        Assert.Equal(expected.Baseline, actual.Baseline);
        Assert.Equal(expected.Ascent, actual.Ascent);
        Assert.Equal(expected.Descent, actual.Descent);
        Assert.Equal(expected.LineGap, actual.LineGap);
        Assert.Equal(expected.LineCount, actual.LineCount);
    }

    private static void ResetState()
    {
        TextMeasurement.ClearCache();
        RenderContext.Current?.Dispose();
    }
}
