using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Provides text measurement services using native DirectWrite font metrics.
/// </summary>
public static class TextMeasurement
{
    // Cache text formats to avoid recreating them for every measurement
    private static readonly Dictionary<string, NativeTextFormat> _formatCache = new();
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
    /// Clears the text format cache. Call this when fonts are changed or memory needs to be freed.
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            foreach (var format in _formatCache.Values)
            {
                format.Dispose();
            }
            _formatCache.Clear();
        }
    }

    private static NativeTextFormat? GetOrCreateFormat(RenderContext context, string fontFamily, float fontSize, int fontWeight, int fontStyle = 0)
    {
        var key = $"{fontFamily}_{fontSize}_{fontWeight}_{fontStyle}";

        lock (_lock)
        {
            if (_formatCache.TryGetValue(key, out var cached) && cached.IsValid)
            {
                return cached;
            }

            try
            {
                var format = context.CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
                _formatCache[key] = format;
                return format;
            }
            catch
            {
                return null;
            }
        }
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
