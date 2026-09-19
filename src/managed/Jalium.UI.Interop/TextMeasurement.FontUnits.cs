namespace Jalium.UI.Interop;

public static partial class TextMeasurement
{
    private sealed record FontUnitCache(long Epoch, Dictionary<FontMetricsKey, FontUnitMetrics> Entries);
    private static FontUnitCache _fontUnitMetricsCache = new(0, new());

    /// <summary>Gets font-relative rulers from the same native font selection used for drawing.</summary>
    public static FontUnitMetrics GetFontUnitMetrics(string fontFamily, double fontSize, int fontWeight = 400, int fontStyle = 0)
    {
        fontFamily ??= string.Empty;
        var captured = Volatile.Read(ref _fontUnitMetricsCache);
        var context = RenderContext.Current;
        if (context is null || !context.IsValid) return FontUnitMetrics.Fallback(fontSize);
        var key = new FontMetricsKey(context.Generation, fontFamily, fontSize, fontWeight, fontStyle);
        if (captured.Entries.TryGetValue(key, out var hit)) return hit;
        var format = GetOrCreateFormat(context, fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (!TryInvokeFormatWithDisposedRetry(
                context,
                fontFamily,
                (float)fontSize,
                fontWeight,
                fontStyle,
                format,
                state: 0,
                static (candidate, _) => candidate.GetFontUnitMetrics(),
                out var metrics))
        {
            return FontUnitMetrics.Fallback(fontSize);
        }
        lock (_metricsWriteLock)
        {
            var current = Volatile.Read(ref _fontUnitMetricsCache);
            if (captured.Epoch != current.Epoch) return metrics;
            if (current.Entries.TryGetValue(key, out var winner)) return winner;
            if (current.Entries.Count >= MaxMetricsCacheEntries) return metrics;
            var entries = new Dictionary<FontMetricsKey, FontUnitMetrics>(current.Entries) { [key] = metrics };
            Volatile.Write(ref _fontUnitMetricsCache, new FontUnitCache(current.Epoch, entries));
        }
        return metrics;
    }
}
