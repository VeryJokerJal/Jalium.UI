using System.Collections;
using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Locks in the process-wide text-measurement result cache in
/// <see cref="TextMeasurement"/>. Before it, the only width cache was a
/// per-instance dictionary on each TextBlock, which misses on every recycled
/// virtualization container (each row holds a unique string). These tests prove
/// an identical (text, font, size, weight, style, constraint) measured through a
/// *different* FormattedText instance does not create a second entry, and that
/// <see cref="TextMeasurement.ClearCache"/> drops the cache (so a font change
/// can't serve stale sizes).
///
/// The cache is process-wide, so it may also hold unrelated strings measured by
/// GPU prewarm / theme loading. The assertions therefore count only the specific
/// text each test owns, which is immune to that background noise.
/// </summary>
[Collection("Application")]
public sealed class TextMeasureResultCacheTests : IDisposable
{
    public TextMeasureResultCacheTests() => ResetState();

    public void Dispose() => ResetState();

    [Fact]
    public void MeasureText_IdenticalTextAcrossInstances_DoesNotDuplicateCacheEntry()
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);

        var first = new FormattedText("Recycled row 42", "Segoe UI", 14) { FontWeight = 400 };
        Assert.True(TextMeasurement.MeasureText(first));
        Assert.True(first.IsMeasured);
        Assert.Equal(1, CountEntriesWithText("Recycled row 42"));

        // A DIFFERENT FormattedText instance with the same measure signature must
        // NOT add a second entry — exactly the recycled-container case the
        // per-instance TextBlock width cache could never satisfy.
        var second = new FormattedText("Recycled row 42", "Segoe UI", 14) { FontWeight = 400 };
        Assert.True(TextMeasurement.MeasureText(second));
        Assert.True(second.IsMeasured);
        Assert.Equal(1, CountEntriesWithText("Recycled row 42"));

        // ...and it returns the same metrics the first measurement produced.
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.LineHeight, second.LineHeight);
        Assert.Equal(first.Baseline, second.Baseline);
    }

    [Fact]
    public void MeasureText_DifferentFontSize_KeysSeparately()
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);

        Assert.True(TextMeasurement.MeasureText(new FormattedText("Same text here", "Segoe UI", 12)));
        Assert.True(TextMeasurement.MeasureText(new FormattedText("Same text here", "Segoe UI", 24)));

        // Font size is part of the key, so the same string at two sizes is two entries.
        Assert.Equal(2, CountEntriesWithText("Same text here"));
    }

    [Fact]
    public void ClearCache_DropsMeasureResults()
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);

        Assert.True(TextMeasurement.MeasureText(new FormattedText("Drop me", "Segoe UI", 13)));
        Assert.True(CountEntriesWithText("Drop me") >= 1);

        TextMeasurement.ClearCache();
        // Count only our text: a concurrent GPU-prewarm measurement could re-seed
        // unrelated strings right after the clear, but ours must be gone.
        Assert.Equal(0, CountEntriesWithText("Drop me"));
    }

    // Counts cache entries whose key text equals <paramref name="text"/>. Reflects
    // the (private, nested) MeasureKey struct's public Text field. Snapshots the
    // keys defensively so a background measurement mutating the shared cache mid
    // enumeration can't throw.
    private static int CountEntriesWithText(string text)
    {
        var cache = GetMeasureCache();
        object[] keys;
        while (true)
        {
            try
            {
                keys = cache.Keys.Cast<object>().ToArray();
                break;
            }
            catch (InvalidOperationException)
            {
                // Collection mutated during enumeration — retry the snapshot.
            }
        }

        var count = 0;
        foreach (var key in keys)
        {
            var field = key.GetType().GetField("Text");
            if (field?.GetValue(key) is string t && string.Equals(t, text, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static IDictionary GetMeasureCache()
    {
        var field = typeof(TextMeasurement).GetField("_measureCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return Assert.IsAssignableFrom<IDictionary>(value);
    }

    private static void ResetState()
    {
        TextMeasurement.ClearCache();
        RenderContext.Current?.Dispose();
    }
}
