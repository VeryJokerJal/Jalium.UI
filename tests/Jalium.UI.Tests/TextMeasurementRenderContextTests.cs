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
        Assert.StartsWith($"{firstContext.Generation}_Segoe UI_", firstKey);

        var secondContext = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(firstContext, secondContext);
        Assert.Empty(GetFormatCache());

        var secondText = new FormattedText("Menu", "Segoe UI", 13);
        Assert.True(TextMeasurement.MeasureText(secondText));
        Assert.Single(GetFormatCache().Keys.Cast<object>());

        var secondKey = GetOnlyCacheKey();
        Assert.StartsWith($"{secondContext.Generation}_Segoe UI_", secondKey);
        Assert.NotEqual(firstKey, secondKey);
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

    private static string GetOnlyCacheKey()
    {
        var cache = GetFormatCache();
        Assert.Single(cache.Keys);
        return Assert.IsType<string>(cache.Keys.Cast<object>().Single());
    }

    private static void ResetState()
    {
        TextMeasurement.ClearCache();
        RenderContext.Current?.Dispose();
    }
}
