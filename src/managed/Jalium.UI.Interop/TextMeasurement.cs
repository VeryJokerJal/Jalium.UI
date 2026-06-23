using System.Globalization;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Provides text measurement services using native DirectWrite font metrics.
/// </summary>
public static class TextMeasurement
{
    // Cache text formats to avoid recreating them for every measurement.
    // Keep this bounded to prevent unbounded native memory growth over long sessions.
    private const int MaxFormatCacheEntries = 256;

    private sealed class FormatCacheEntry
    {
        public FormatCacheEntry(NativeTextFormat format, LinkedListNode<string> lruNode)
        {
            Format = format;
            LruNode = lruNode;
        }

        public NativeTextFormat Format { get; }
        public LinkedListNode<string> LruNode { get; }
    }

    private static readonly Dictionary<string, FormatCacheEntry> _formatCache = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> _lruKeys = new();
    private static readonly object _lock = new();

    // 字体度量（行高 / ascent / descent / baseline 等）对固定 (字体族, 字号, 粗细, 样式) 是常量，
    // 但底层 GetFontMetrics 每次都 P/Invoke 进 DirectWrite。TextBlock.OnRender / MeasureOverride 每帧
    // 都会调 GetLineHeight/GetFontMetrics（无结果缓存），在 UI 控件密集（如设置面板上百个 TextBlock）
    // 且首帧布局多轮迭代时，会堆出几千~上万次 native 调用 → 明显卡顿。这里按配置缓存度量结果，
    // 把重复调用降为一次 O(1) 字典命中（度量是常量，缓存安全；上限防止长会话无界增长）。
    private const int MaxMetricsCacheEntries = 512;
    private static readonly Dictionary<string, TextMetrics> _metricsCache = new(StringComparer.Ordinal);
    private static readonly object _metricsLock = new();

    // 文本「测量结果」缓存。MeasureText 之前对每个 (文本, 字体, 字号, 粗细, 样式, 约束) 都要 P/Invoke 进
    // DirectWrite 做一次完整的 CreateTextLayout + 整形（昂贵）。虚拟化大列表滚动时，每个新实例化的行单元
    // 都会冷测量，回滚或重复文本更是反复测量同一串。这里按完整测量签名缓存 TextMetrics 结果：命中即 O(1)
    // 返回，免去 DWrite 整形。键用 struct 避免命中路径分配字符串；LRU 有界，唯一文本不会无界增长。
    private const int MaxMeasureCacheEntries = 4096;

    private readonly struct MeasureKey : IEquatable<MeasureKey>
    {
        public readonly string Text;
        public readonly string FontFamily;
        public readonly float FontSize;
        public readonly int FontWeight;
        public readonly int FontStyle;
        public readonly float MaxWidth;
        public readonly float MaxHeight;

        public MeasureKey(string text, string fontFamily, float fontSize, int fontWeight, int fontStyle, float maxWidth, float maxHeight)
        {
            Text = text;
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontWeight = fontWeight;
            FontStyle = fontStyle;
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
        }

        public bool Equals(MeasureKey other) =>
            FontSize == other.FontSize
            && FontWeight == other.FontWeight
            && FontStyle == other.FontStyle
            && MaxWidth == other.MaxWidth
            && MaxHeight == other.MaxHeight
            && string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
            && string.Equals(Text, other.Text, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is MeasureKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Text, StringComparer.Ordinal);
            hash.Add(FontFamily, StringComparer.Ordinal);
            hash.Add(FontSize);
            hash.Add(FontWeight);
            hash.Add(FontStyle);
            hash.Add(MaxWidth);
            hash.Add(MaxHeight);
            return hash.ToHashCode();
        }
    }

    private sealed class MeasureCacheEntry
    {
        public MeasureCacheEntry(TextMetrics metrics, LinkedListNode<MeasureKey> lruNode)
        {
            Metrics = metrics;
            LruNode = lruNode;
        }

        public TextMetrics Metrics { get; }
        public LinkedListNode<MeasureKey> LruNode { get; }
    }

    private static readonly Dictionary<MeasureKey, MeasureCacheEntry> _measureCache = new();
    private static readonly LinkedList<MeasureKey> _measureLruKeys = new();
    private static readonly object _measureLock = new();

    /// <summary>
    /// Measures text and populates the FormattedText with accurate metrics.
    /// Uses DirectWrite for WPF-style text measurement with actual font metrics.
    /// </summary>
    /// <param name="formattedText">The formatted text to measure.</param>
    /// <returns>True if native measurement was used; false if fallback was used.</returns>
    public static bool MeasureText(FormattedText formattedText)
    {
        if (formattedText == null || string.IsNullOrEmpty(formattedText.Text))
        {
            return false;
        }

        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
        {
            // No render context available, use approximate measurement
            ApproximateMeasurement(formattedText);
            return false;
        }

        var format = GetOrCreateFormat(context, formattedText.FontFamily, (float)formattedText.FontSize, formattedText.FontWeight, formattedText.FontStyle);
        if (format == null || !format.IsValid)
        {
            ApproximateMeasurement(formattedText);
            return false;
        }

        // Determine max width/height for measurement
        var maxWidth = formattedText.MaxTextWidth;
        var maxHeight = formattedText.MaxTextHeight;

        if (double.IsInfinity(maxWidth) || double.IsNaN(maxWidth) || maxWidth <= 0)
            maxWidth = 100000;
        if (double.IsInfinity(maxHeight) || double.IsNaN(maxHeight) || maxHeight <= 0)
            maxHeight = 100000;

        // 先查测量结果缓存：命中即免去一次 DirectWrite CreateTextLayout + 整形（虚拟化滚动的主成本）。
        var measureKey = new MeasureKey(
            formattedText.Text,
            formattedText.FontFamily ?? string.Empty,
            (float)formattedText.FontSize,
            formattedText.FontWeight,
            formattedText.FontStyle,
            (float)maxWidth,
            (float)maxHeight);

        lock (_measureLock)
        {
            if (_measureCache.TryGetValue(measureKey, out var cachedEntry))
            {
                TouchMeasureKey(cachedEntry.LruNode);
                ApplyMetrics(formattedText, cachedEntry.Metrics);
                return true;
            }
        }

        var metrics = format.MeasureText(formattedText.Text, (float)maxWidth, (float)maxHeight);

        lock (_measureLock)
        {
            if (!_measureCache.ContainsKey(measureKey))
            {
                var lruNode = _measureLruKeys.AddLast(measureKey);
                _measureCache[measureKey] = new MeasureCacheEntry(metrics, lruNode);
                TrimMeasureCacheIfNeeded();
            }
        }

        ApplyMetrics(formattedText, metrics);
        return true;
    }

    private static void ApplyMetrics(FormattedText formattedText, TextMetrics metrics)
    {
        formattedText.Width = metrics.Width;
        formattedText.Height = metrics.Height;
        formattedText.LineHeight = metrics.LineHeight;
        formattedText.Ascent = metrics.Ascent;
        formattedText.Descent = metrics.Descent;
        formattedText.LineGap = metrics.LineGap;
        formattedText.Baseline = metrics.Baseline;
        formattedText.LineCount = (int)metrics.LineCount;
        formattedText.IsMeasured = true;
    }

    private static void TouchMeasureKey(LinkedListNode<MeasureKey> node)
    {
        if (!ReferenceEquals(node, _measureLruKeys.Last))
        {
            _measureLruKeys.Remove(node);
            _measureLruKeys.AddLast(node);
        }
    }

    private static void TrimMeasureCacheIfNeeded()
    {
        while (_measureCache.Count > MaxMeasureCacheEntries && _measureLruKeys.First is { } oldest)
        {
            _measureLruKeys.RemoveFirst();
            _measureCache.Remove(oldest.Value);
        }
    }

    /// <summary>
    /// Gets font metrics for a specific font configuration without measuring text.
    /// Useful for determining line height before text content is known.
    /// </summary>
    /// <param name="fontFamily">The font family name.</param>
    /// <param name="fontSize">The font size in DIPs.</param>
    /// <param name="fontWeight">The font weight (400 = normal, 700 = bold).</param>
    /// <param name="fontStyle">The font style (0 = normal, 1 = italic, 2 = oblique).</param>
    /// <returns>Text metrics containing font information.</returns>
    public static TextMetrics GetFontMetrics(string fontFamily, double fontSize, int fontWeight = 400, int fontStyle = 0)
    {
        // 先查度量缓存（按配置常量缓存，命中即 O(1) 返回，免去每帧 native DWrite 调用）。
        var key = MetricsKey(fontFamily, fontSize, fontWeight, fontStyle);
        lock (_metricsLock)
        {
            if (_metricsCache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
        {
            // 无渲染上下文：返回近似度量，且不缓存（上下文稍后可能就绪，下次应走真实度量）。
            return new TextMetrics
            {
                LineHeight = (float)(fontSize * 1.2),
                Ascent = (float)fontSize,
                Descent = (float)(fontSize * 0.2),
                LineGap = 0,
                Baseline = (float)fontSize
            };
        }

        var format = GetOrCreateFormat(context, fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (format == null || !format.IsValid)
        {
            return new TextMetrics
            {
                LineHeight = (float)(fontSize * 1.2),
                Ascent = (float)fontSize,
                Descent = (float)(fontSize * 0.2),
                LineGap = 0,
                Baseline = (float)fontSize
            };
        }

        var metrics = format.GetFontMetrics();
        lock (_metricsLock)
        {
            // 上限保护：配置种类极少（一个 UI 通常就几种字体×字号），正常远不到上限；满了就不再增长。
            if (_metricsCache.Count < MaxMetricsCacheEntries)
            {
                _metricsCache[key] = metrics;
            }
        }
        return metrics;
    }

    private static string MetricsKey(string fontFamily, double fontSize, int fontWeight, int fontStyle)
        => string.Create(CultureInfo.InvariantCulture, $"{fontFamily}|{fontSize:R}|{fontWeight}|{fontStyle}");

    /// <summary>
    /// Gets the natural line height for a font using WPF-style calculation.
    /// Line height = Ascent + Descent + LineGap
    /// </summary>
    /// <param name="fontFamily">The font family name.</param>
    /// <param name="fontSize">The font size in DIPs.</param>
    /// <param name="fontWeight">The font weight (400 = normal, 700 = bold).</param>
    /// <param name="fontStyle">The font style (0 = normal, 1 = italic, 2 = oblique).</param>
    /// <returns>The natural line height in DIPs.</returns>
    public static double GetLineHeight(string fontFamily, double fontSize, int fontWeight = 400, int fontStyle = 0)
    {
        var metrics = GetFontMetrics(fontFamily, fontSize, fontWeight, fontStyle);
        return metrics.LineHeight;
    }

    /// <summary>
    /// Hit-tests a point against a text layout to determine the character at that position.
    /// Uses DirectWrite's native hit testing for pixel-accurate results.
    /// </summary>
    /// <param name="text">The full text to test against.</param>
    /// <param name="fontFamily">The font family name.</param>
    /// <param name="fontSize">The font size in DIPs.</param>
    /// <param name="pointX">The X coordinate to test.</param>
    /// <param name="result">The hit test result.</param>
    /// <returns>True if the hit test succeeded; false if native context is unavailable.</returns>
    public static bool HitTestPoint(string text, string fontFamily, double fontSize, float pointX, out TextHitTestResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
            return false;

        var format = GetOrCreateFormat(context, fontFamily, (float)fontSize, 400);
        if (format == null || !format.IsValid)
            return false;

        return format.HitTestPoint(text, 100000f, 100000f, pointX, 0f, out result);
    }

    /// <summary>
    /// Wrap-aware variant of <see cref="HitTestPoint"/>. Pass the same
    /// <paramref name="maxWidth"/> the renderer uses so (pointX, pointY) is
    /// interpreted inside the wrapped layout — this is what makes mouse drag
    /// selection land on the character the user actually clicked when the
    /// paragraph has wrapped to multiple visual rows.
    /// </summary>
    public static bool HitTestPointWrapped(
        string text,
        string fontFamily,
        double fontSize,
        int fontWeight,
        int fontStyle,
        float maxWidth,
        float pointX,
        float pointY,
        out TextHitTestResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
            return false;

        var format = GetOrCreateFormat(context, fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (format == null || !format.IsValid)
            return false;

        return format.HitTestPoint(text, maxWidth, 100000f, pointX, pointY, out result);
    }

    /// <summary>
    /// Gets the caret X position for a given character index within a text layout.
    /// Uses DirectWrite's native hit testing for pixel-accurate results.
    /// </summary>
    /// <param name="text">The full text of the layout.</param>
    /// <param name="fontFamily">The font family name.</param>
    /// <param name="fontSize">The font size in DIPs.</param>
    /// <param name="textPosition">The character index.</param>
    /// <param name="isTrailingHit">If true, returns the trailing edge of the character; otherwise the leading edge.</param>
    /// <param name="result">The hit test result with caret position.</param>
    /// <returns>True if the query succeeded; false if native context is unavailable.</returns>
    public static bool HitTestTextPosition(string text, string fontFamily, double fontSize, uint textPosition, bool isTrailingHit, out TextHitTestResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
            return false;

        var format = GetOrCreateFormat(context, fontFamily, (float)fontSize, 400);
        if (format == null || !format.IsValid)
            return false;

        return format.HitTestTextPosition(text, 100000f, 100000f, textPosition, isTrailingHit, out result);
    }

    /// <summary>
    /// Wrap-aware variant of <see cref="HitTestTextPosition"/>. Passing the
    /// same <paramref name="maxWidth"/> the renderer uses gives back the
    /// caret (x, y) inside the wrapped layout, so callers can paint per-row
    /// selection / caret rectangles that line up with the glyphs as the user
    /// actually sees them.
    /// </summary>
    public static bool HitTestTextPositionWrapped(
        string text,
        string fontFamily,
        double fontSize,
        int fontWeight,
        int fontStyle,
        float maxWidth,
        uint textPosition,
        bool isTrailingHit,
        out TextHitTestResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
            return false;

        var format = GetOrCreateFormat(context, fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (format == null || !format.IsValid)
            return false;

        // Height is intentionally unbounded — we only care about horizontal
        // wrapping; height clipping would truncate the y of later rows.
        return format.HitTestTextPosition(text, maxWidth, 100000f, textPosition, isTrailingHit, out result);
    }

    /// <summary>
    /// Clears the text format cache. Call this when fonts are changed or memory needs to be freed.
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            foreach (var entry in _formatCache.Values)
            {
                entry.Format.Dispose();
            }
            _formatCache.Clear();
            _lruKeys.Clear();
        }

        // Measured sizes depend on the (now-changed) fonts, so drop the result cache too.
        lock (_measureLock)
        {
            _measureCache.Clear();
            _measureLruKeys.Clear();
        }

        // Font-face metrics (ascent / descent / lineHeight) are font-derived as well,
        // so a "fonts changed" clear must drop them too — otherwise GetFontMetrics /
        // GetLineHeight keep serving line heights resolved from the stale font set.
        lock (_metricsLock)
        {
            _metricsCache.Clear();
        }
    }

    private static NativeTextFormat? GetOrCreateFormat(RenderContext context, string fontFamily, float fontSize, int fontWeight, int fontStyle = 0)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            fontFamily = FrameworkElement.DefaultFontFamilyName;
        }

        if (float.IsNaN(fontSize) || float.IsInfinity(fontSize) || fontSize <= 0)
        {
            fontSize = 12;
        }

        var key = BuildCacheKey(context.Generation, fontFamily, fontSize, fontWeight, fontStyle);

        lock (_lock)
        {
            if (_formatCache.TryGetValue(key, out var cached))
            {
                if (cached.Format.IsValid)
                {
                    TouchCachedKey(cached.LruNode);
                    return cached.Format;
                }

                RemoveCachedEntry(key, cached);
            }

            try
            {
                var format = context.CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
                var lruNode = _lruKeys.AddLast(key);
                _formatCache[key] = new FormatCacheEntry(format, lruNode);
                TrimCacheIfNeeded();
                return format;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string BuildCacheKey(int contextGeneration, string fontFamily, float fontSize, int fontWeight, int fontStyle)
    {
        return string.Concat(
            contextGeneration.ToString(CultureInfo.InvariantCulture),
            "_",
            fontFamily,
            "_",
            fontSize.ToString("0.###", CultureInfo.InvariantCulture),
            "_",
            fontWeight.ToString(CultureInfo.InvariantCulture),
            "_",
            fontStyle.ToString(CultureInfo.InvariantCulture));
    }

    private static void TouchCachedKey(LinkedListNode<string> node)
    {
        if (!ReferenceEquals(node, _lruKeys.Last))
        {
            _lruKeys.Remove(node);
            _lruKeys.AddLast(node);
        }
    }

    private static void TrimCacheIfNeeded()
    {
        while (_formatCache.Count > MaxFormatCacheEntries && _lruKeys.First is { } oldest)
        {
            var oldestKey = oldest.Value;
            _lruKeys.RemoveFirst();
            if (_formatCache.TryGetValue(oldestKey, out var entry))
            {
                entry.Format.Dispose();
                _formatCache.Remove(oldestKey);
            }
        }
    }

    private static void RemoveCachedEntry(string key, FormatCacheEntry entry)
    {
        _formatCache.Remove(key);
        _lruKeys.Remove(entry.LruNode);
        entry.Format.Dispose();
    }

    private static void ApproximateMeasurement(FormattedText formattedText)
    {
        // Fallback approximate measurement when native is not available
        var text = formattedText.Text;
        var fontSize = formattedText.FontSize;

        // Approximate character dimensions
        var charWidth = fontSize * 0.6;
        var lineHeight = fontSize * 1.2;

        // Count lines
        var lines = text.Split('\n');
        var maxLineWidth = 0.0;

        foreach (var line in lines)
        {
            var lineWidth = line.Length * charWidth;
            if (lineWidth > maxLineWidth)
                maxLineWidth = lineWidth;
        }

        // Apply max width constraint
        var maxWidth = formattedText.MaxTextWidth;
        if (!double.IsInfinity(maxWidth) && maxWidth > 0 && maxLineWidth > maxWidth)
        {
            // Calculate wrapped line count
            var charsPerLine = Math.Max(1, (int)(maxWidth / charWidth));
            var totalLines = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    totalLines++;
                }
                else
                {
                    totalLines += (int)Math.Ceiling((double)line.Length / charsPerLine);
                }
            }
            formattedText.Width = Math.Min(maxLineWidth, maxWidth);
            formattedText.Height = totalLines * lineHeight;
            formattedText.LineCount = totalLines;
        }
        else
        {
            formattedText.Width = maxLineWidth;
            formattedText.Height = lines.Length * lineHeight;
            formattedText.LineCount = lines.Length;
        }

        formattedText.LineHeight = lineHeight;
        formattedText.Ascent = fontSize;
        formattedText.Descent = fontSize * 0.2;
        formattedText.LineGap = 0;
        formattedText.Baseline = fontSize;
        formattedText.IsMeasured = false;
    }
}
