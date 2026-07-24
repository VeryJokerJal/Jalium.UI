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
        public FormatCacheEntry(NativeTextFormat format, LinkedListNode<FormatKey> lruNode)
        {
            Format = format;
            LruNode = lruNode;
        }

        public NativeTextFormat Format { get; }
        public LinkedListNode<FormatKey> LruNode { get; }
    }

    private readonly struct FormatKey : IEquatable<FormatKey>
    {
        public FormatKey(int contextGeneration, string fontFamily, float fontSize, int fontWeight, int fontStyle)
        {
            ContextGeneration = contextGeneration;
            FontFamily = fontFamily;
            // Preserve the former "0.###" string-key coalescing without
            // allocating the formatted string on every warm lookup.
            FontSize = MathF.Round(fontSize, 3);
            FontWeight = fontWeight;
            FontStyle = fontStyle;
        }

        private int ContextGeneration { get; }
        private string FontFamily { get; }
        private float FontSize { get; }
        private int FontWeight { get; }
        private int FontStyle { get; }

        public bool Equals(FormatKey other) =>
            ContextGeneration == other.ContextGeneration &&
            FontSize.Equals(other.FontSize) &&
            FontWeight == other.FontWeight &&
            FontStyle == other.FontStyle &&
            string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is FormatKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ContextGeneration);
            hash.Add(FontFamily, StringComparer.Ordinal);
            hash.Add(FontSize);
            hash.Add(FontWeight);
            hash.Add(FontStyle);
            return hash.ToHashCode();
        }
    }

    private static readonly Dictionary<FormatKey, FormatCacheEntry> _formatCache = new();
    private static readonly LinkedList<FormatKey> _lruKeys = new();
    private static readonly object _lock = new();
    // CreateTextFormat 失败负缓存（key → 失败时刻 TickCount64，受 _lock 保护）。文本测量是每帧热路径：
    // 一个持续失败的字体配置若不记录，就会每帧重复 P/Invoke + 抛异常 + 静默吞掉，既卡顿又零证据。
    // 记录后：冷却期内直接返回 null（走近似度量回退），冷却期后允许一次重试；首次失败按 key 打一条
    // Console.Error 日志（含字体族/字号/异常消息），重试再失败不重复打。key 含 context.Generation
    // （见 BuildCacheKey），因此渲染上下文换代后负缓存自然失效、新上下文可重新尝试真实创建。
    private const int MaxFailedFormatEntries = 64;
    private const long FailedFormatRetryDelayMs = 5000;
    private static readonly Dictionary<FormatKey, long> _failedFormatKeys = new();
    // Immutable published snapshot for warm reads. Text measurement is a layout hot path, and
    // taking the format-cache monitor for every TextBlock made virtualized scrolling serialize
    // behind unrelated text work. Misses retain the bounded LRU under _lock.
    private static Dictionary<FormatKey, NativeTextFormat> _formatReadCache = new();

    // 字体度量（行高 / ascent / descent / baseline 等）对固定 (字体族, 字号, 粗细, 样式) 是常量，
    // 但底层 GetFontMetrics 每次都 P/Invoke 进 DirectWrite。TextBlock.OnRender / MeasureOverride 每帧
    // 都会调 GetLineHeight/GetFontMetrics（无结果缓存），在 UI 控件密集（如设置面板上百个 TextBlock）
    // 且首帧布局多轮迭代时，会堆出几千~上万次 native 调用 → 明显卡顿。这里按配置缓存度量结果，
    // 把重复调用降为一次 O(1) 字典命中（度量是常量，缓存安全；上限防止长会话无界增长）。
    private const int MaxMetricsCacheEntries = 512;

    private readonly struct FontMetricsKey : IEquatable<FontMetricsKey>
    {
        public FontMetricsKey(
            int contextGeneration,
            string fontFamily,
            double fontSize,
            int fontWeight,
            int fontStyle)
        {
            ContextGeneration = contextGeneration;
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontWeight = fontWeight;
            FontStyle = fontStyle;
        }

        private int ContextGeneration { get; }
        private string FontFamily { get; }
        private double FontSize { get; }
        private int FontWeight { get; }
        private int FontStyle { get; }

        public bool Equals(FontMetricsKey other) =>
            ContextGeneration == other.ContextGeneration &&
            FontSize.Equals(other.FontSize) &&
            FontWeight == other.FontWeight &&
            FontStyle == other.FontStyle &&
            string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is FontMetricsKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ContextGeneration);
            hash.Add(FontFamily, StringComparer.Ordinal);
            hash.Add(FontSize);
            hash.Add(FontWeight);
            hash.Add(FontStyle);
            return hash.ToHashCode();
        }
    }

    private sealed class MetricsCacheState
    {
        public MetricsCacheState(long epoch, Dictionary<FontMetricsKey, TextMetrics> entries)
        {
            Epoch = epoch;
            Entries = entries;
        }

        internal long Epoch { get; }
        internal Dictionary<FontMetricsKey, TextMetrics> Entries { get; }
    }

    // Published dictionaries are never mutated. Warm hits are therefore a plain
    // lock-free dictionary lookup; rare misses copy and publish under the gate.
    private static readonly object _metricsWriteLock = new();
    private static MetricsCacheState _metricsCacheState = new(0, new());

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

        var metrics = NormalizeTrailingWhitespaceMetrics(
            format,
            formattedText.Text,
            (float)maxWidth,
            (float)maxHeight);

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
        var widthIncludingTrailingWhitespace = Math.Max(
            metrics.Width,
            metrics.WidthIncludingTrailingWhitespace);
        formattedText.Width = metrics.Width;
        formattedText.WidthIncludingTrailingWhitespace = widthIncludingTrailingWhitespace;
        formattedText.Height = metrics.Height;
        formattedText.LineHeight = metrics.LineHeight;
        formattedText.Ascent = metrics.Ascent;
        formattedText.Descent = metrics.Descent;
        formattedText.LineGap = metrics.LineGap;
        formattedText.Baseline = metrics.Baseline;
        formattedText.LineCount = (int)metrics.LineCount;
        formattedText.IsMeasured = true;
    }

    private static TextMetrics NormalizeTrailingWhitespaceMetrics(
        NativeTextFormat format,
        string text,
        float maxWidth,
        float maxHeight)
    {
        var metrics = format.MeasureText(text, maxWidth, maxHeight);
        var trailingStart = text.Length;
        while (trailingStart > 0 && IsTrailingLayoutWhitespace(text[trailingStart - 1]))
        {
            trailingStart--;
        }

        if (trailingStart == text.Length ||
            metrics.WidthIncludingTrailingWhitespace > metrics.Width)
        {
            return metrics;
        }

        // Older native binaries and backends that expose only one width report
        // the trailing-space-inclusive value in Width. Recover WPF's trimmed
        // Width without penalizing updated backends, which return two distinct
        // values and leave through the fast path above.
        var widthIncludingTrailingWhitespace = Math.Max(
            metrics.Width,
            metrics.WidthIncludingTrailingWhitespace);
        metrics.Width = trailingStart == 0
            ? 0
            : format.MeasureText(text[..trailingStart], maxWidth, maxHeight).Width;
        metrics.WidthIncludingTrailingWhitespace = widthIncludingTrailingWhitespace;
        return metrics;
    }

    private static bool IsTrailingLayoutWhitespace(char value)
    {
        return value is not '\r' and not '\n' and not '\u00A0' and not '\u202F' and not '\u2060' and not '\uFEFF' &&
               char.IsWhiteSpace(value);
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
        fontFamily ??= string.Empty;
        // 先查度量缓存（按配置常量缓存，命中即 O(1) 返回，免去每帧 native DWrite 调用）。
        // Capture the cache state before the context. RenderContext replacement
        // publishes the new context before clearing text caches, so an in-flight
        // lookup can at worst populate a retired cache epoch.
        var capturedState = Volatile.Read(ref _metricsCacheState);
        var context = RenderContext.Current;
        FontMetricsKey key = default;
        if (context != null)
        {
            key = new FontMetricsKey(
                context.Generation,
                fontFamily,
                fontSize,
                fontWeight,
                fontStyle);
            if (capturedState.Entries.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

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
        lock (_metricsWriteLock)
        {
            // 上限保护：配置种类极少（一个 UI 通常就几种字体×字号），正常远不到上限；满了就不再增长。
            var currentState = Volatile.Read(ref _metricsCacheState);

            // ClearCache published a newer epoch while native metrics were being
            // resolved. Never repopulate it from this stale operation.
            if (currentState.Epoch != capturedState.Epoch)
            {
                return metrics;
            }

            if (currentState.Entries.TryGetValue(key, out var winner))
            {
                return winner;
            }

            if (currentState.Entries.Count >= MaxMetricsCacheEntries)
            {
                return metrics;
            }

            var nextEntries = new Dictionary<FontMetricsKey, TextMetrics>(currentState.Entries)
            {
                [key] = metrics
            };
            Volatile.Write(
                ref _metricsCacheState,
                new MetricsCacheState(currentState.Epoch, nextEntries));
        }
        return metrics;
    }

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
            Volatile.Write(
                ref _formatReadCache,
                new Dictionary<FormatKey, NativeTextFormat>());
            foreach (var entry in _formatCache.Values)
            {
                entry.Format.Dispose();
            }
            _formatCache.Clear();
            _lruKeys.Clear();
            // 字体集已变化：旧的 CreateTextFormat 失败判定不再成立，清掉负缓存
            // 让新字体集立即获得一次真实创建（而不是等冷却期满）。
            _failedFormatKeys.Clear();
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
        lock (_metricsWriteLock)
        {
            var currentState = Volatile.Read(ref _metricsCacheState);
            Volatile.Write(
                ref _metricsCacheState,
                new MetricsCacheState(currentState.Epoch + 1, new()));
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

        var key = new FormatKey(context.Generation, fontFamily, fontSize, fontWeight, fontStyle);

        var readSnapshot = Volatile.Read(ref _formatReadCache);
        if (readSnapshot.TryGetValue(key, out var readCached) && readCached.IsValid)
        {
            return readCached;
        }

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

            long now = Environment.TickCount64;
            if (_failedFormatKeys.TryGetValue(key, out var failedAtMs) &&
                now - failedAtMs < FailedFormatRetryDelayMs)
            {
                // 冷却期内的已知失败配置：直接走调用方的近似度量回退，不再每帧
                // 重复 P/Invoke + 抛异常。条目保留（不删除），冷却期满后放行一次
                // 真实重试；重试再失败只刷新时间戳、不重复打日志。
                return null;
            }

            try
            {
                var format = context.CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
                // 冷却期满后的重试成功了（或换代后同 key 恢复）——撤销负缓存。
                _failedFormatKeys.Remove(key);
                var lruNode = _lruKeys.AddLast(key);
                _formatCache[key] = new FormatCacheEntry(format, lruNode);
                TrimCacheIfNeeded();
                PublishFormatReadCache();
                return format;
            }
            catch (Exception ex)
            {
                bool firstFailure = !_failedFormatKeys.ContainsKey(key);
                if (firstFailure && _failedFormatKeys.Count >= MaxFailedFormatEntries)
                {
                    // 上限防御（大量不同失败配置或 generation 换代累积）：先清
                    // 过期项，仍满则整体清空。自愈型缓存，最坏代价是每 key 多一
                    // 次真实重试和一条重复日志。
                    PruneExpiredFailedFormatKeysLocked(now);
                    if (_failedFormatKeys.Count >= MaxFailedFormatEntries)
                    {
                        _failedFormatKeys.Clear();
                    }
                }

                _failedFormatKeys[key] = now;
                if (firstFailure)
                {
                    Console.Error.WriteLine(
                        $"[TextMeasurement] CreateTextFormat failed for '{fontFamily}' " +
                        $"{fontSize.ToString("0.###", CultureInfo.InvariantCulture)}px " +
                        $"(weight={fontWeight}, style={fontStyle}); using approximate metrics: {ex.Message}");
                }

                return null;
            }
        }
    }

    /// <summary>
    /// Removes negative-cache entries whose retry cooldown has elapsed (stale
    /// generations age out here too). Caller must hold <see cref="_lock"/>.
    /// </summary>
    private static void PruneExpiredFailedFormatKeysLocked(long nowMs)
    {
        List<FormatKey>? expired = null;
        foreach (var pair in _failedFormatKeys)
        {
            if (nowMs - pair.Value >= FailedFormatRetryDelayMs)
            {
                (expired ??= new List<FormatKey>()).Add(pair.Key);
            }
        }

        if (expired != null)
        {
            foreach (var expiredKey in expired)
            {
                _failedFormatKeys.Remove(expiredKey);
            }
        }
    }

    private static void TouchCachedKey(LinkedListNode<FormatKey> node)
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

    private static void PublishFormatReadCache()
    {
        var snapshot = new Dictionary<FormatKey, NativeTextFormat>(_formatCache.Count);
        foreach (var pair in _formatCache)
        {
            snapshot[pair.Key] = pair.Value.Format;
        }

        Volatile.Write(ref _formatReadCache, snapshot);
    }

    private static void RemoveCachedEntry(FormatKey key, FormatCacheEntry entry)
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
        var maxLineWidthIncludingTrailingWhitespace = 0.0;

        foreach (var line in lines)
        {
            var trimmedLength = line.Length;
            while (trimmedLength > 0 && IsTrailingLayoutWhitespace(line[trimmedLength - 1]))
            {
                trimmedLength--;
            }

            var lineWidth = trimmedLength * charWidth;
            var lineWidthIncludingTrailingWhitespace = line.Length * charWidth;
            if (lineWidth > maxLineWidth)
                maxLineWidth = lineWidth;
            if (lineWidthIncludingTrailingWhitespace > maxLineWidthIncludingTrailingWhitespace)
                maxLineWidthIncludingTrailingWhitespace = lineWidthIncludingTrailingWhitespace;
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
            formattedText.WidthIncludingTrailingWhitespace = Math.Min(
                maxLineWidthIncludingTrailingWhitespace, maxWidth);
            formattedText.Height = totalLines * lineHeight;
            formattedText.LineCount = totalLines;
        }
        else
        {
            formattedText.Width = maxLineWidth;
            formattedText.WidthIncludingTrailingWhitespace = maxLineWidthIncludingTrailingWhitespace;
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
