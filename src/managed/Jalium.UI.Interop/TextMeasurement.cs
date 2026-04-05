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

        var metrics = format.MeasureText(formattedText.Text, (float)maxWidth, (float)maxHeight);

        formattedText.Width = metrics.Width;
        formattedText.Height = metrics.Height;
        formattedText.LineHeight = metrics.LineHeight;
        formattedText.Ascent = metrics.Ascent;
        formattedText.Descent = metrics.Descent;
        formattedText.LineGap = metrics.LineGap;
        formattedText.Baseline = metrics.Baseline;
        formattedText.LineCount = (int)metrics.LineCount;
        formattedText.IsMeasured = true;

        return true;
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
        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
        {
            // Return approximate metrics
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

        return format.GetFontMetrics();
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
