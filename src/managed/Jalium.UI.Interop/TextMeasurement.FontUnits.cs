namespace Jalium.UI.Interop;

public static partial class TextMeasurement
{
    private sealed record FontUnitCache(long Epoch, Dictionary<FontMetricsKey, FontUnitMetrics> Entries);
    private static FontUnitCache _fontUnitMetricsCache = new(0, new());
    private sealed record FontMathCache(long Epoch, Dictionary<FontMetricsKey, FontMathConstants> Entries);
    private static FontMathCache _fontMathConstantsCache = new(0, new());

    /// <summary>Gets font-relative rulers from the same native font selection used for drawing.</summary>
    public static FontUnitMetrics GetFontUnitMetrics(string fontFamily, double fontSize, int fontWeight = 400, int fontStyle = 0)
    {
        if (fontSize == 0) return FontUnitMetrics.Fallback(0);
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

    /// <summary>Gets script-size constants from the selected native font face.</summary>
    public static FontMathConstants GetFontMathConstants(string fontFamily, int fontWeight = 400, int fontStyle = 0)
    {
        fontFamily ??= string.Empty;
        var context = RenderContext.Current;
        if (context is null || !context.IsValid) return FontMathConstants.Fallback;
        var captured = Volatile.Read(ref _fontMathConstantsCache);
        const float probeSize = 14;
        var key = new FontMetricsKey(context.Generation, fontFamily, probeSize, fontWeight, fontStyle);
        if (captured.Entries.TryGetValue(key, out var hit)) return hit;
        var format = GetOrCreateFormat(context, fontFamily, probeSize, fontWeight, fontStyle);
        if (!TryInvokeFormatWithDisposedRetry(
                context, fontFamily, probeSize, fontWeight, fontStyle, format,
                state: 0, static (candidate, _) => candidate.GetFontMathConstants(), out var constants))
            return FontMathConstants.Fallback;
        lock (_metricsWriteLock)
        {
            var current = Volatile.Read(ref _fontMathConstantsCache);
            if (captured.Epoch != current.Epoch) return constants;
            if (current.Entries.TryGetValue(key, out var winner)) return winner;
            if (current.Entries.Count >= MaxMetricsCacheEntries) return constants;
            var entries = new Dictionary<FontMetricsKey, FontMathConstants>(current.Entries) { [key] = constants };
            Volatile.Write(ref _fontMathConstantsCache, new FontMathCache(current.Epoch, entries));
        }
        return constants;
    }
}
