using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Paragraph alignment options.
/// </summary>
public enum ParagraphAlignment
{
    Near = 0,
    Far = 1,
    Center = 2
}

/// <summary>
/// Text trimming mode options.
/// </summary>
public enum TextTrimmingMode
{
    /// <summary>
    /// No trimming.
    /// </summary>
    None = 0,

    /// <summary>
    /// Trim at character boundary with ellipsis.
    /// </summary>
    CharacterEllipsis = 1,

    /// <summary>
    /// Trim at word boundary with ellipsis.
    /// </summary>
    WordEllipsis = 2
}

/// <summary>
/// Represents a native text format for rendering text.
/// </summary>
public sealed class NativeTextFormat : IDisposable
{
    private nint _handle;
    private int _disposed; // 0 = not disposed, 1 = disposed (Interlocked for thread-safety)
    private TextTrimmingMode? _currentTrimming;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether the text format is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string FontFamily { get; }

    /// <summary>
    /// Gets the font size.
    /// </summary>
    public float FontSize { get; }

    /// <summary>
    /// Gets the font weight.
    /// </summary>
    public int FontWeight { get; }

    /// <summary>
    /// Gets the font style.
    /// </summary>
    public int FontStyle { get; }

    /// <summary>
    /// Gets or sets the access sequence for LRU eviction.
    /// </summary>
    internal long LastAccessSequence { get; set; }

    internal NativeTextFormat(RenderContext context, string fontFamily, float fontSize, int fontWeight, int fontStyle)
    {
        FontFamily = fontFamily;
        FontSize = fontSize;
        FontWeight = fontWeight;
        FontStyle = fontStyle;

        _handle = NativeMethods.TextFormatCreate(context.Handle, fontFamily, fontSize, fontWeight, fontStyle);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create text format");
        }
    }

    /// <summary>
    /// Sets the text alignment.
    /// </summary>
    /// <param name="alignment">The text alignment. Mapped directly to DWRITE_TEXT_ALIGNMENT (Left=Leading, Right=Trailing, Center, Justify=Justified).</param>
    public void SetTextAlignment(TextAlignment alignment)
    {
        ThrowIfDisposed();
        NativeMethods.TextFormatSetAlignment(_handle, (int)alignment);
    }

    /// <summary>
    /// Sets the paragraph alignment.
    /// </summary>
    /// <param name="alignment">The paragraph alignment.</param>
    public void SetParagraphAlignment(ParagraphAlignment alignment)
    {
        ThrowIfDisposed();
        NativeMethods.TextFormatSetParagraphAlignment(_handle, (int)alignment);
    }

    /// <summary>
    /// Sets the text trimming mode.
    /// </summary>
    /// <param name="trimming">The trimming mode.</param>
    public void SetTrimming(TextTrimmingMode trimming)
    {
        ThrowIfDisposed();
        if (_currentTrimming == trimming)
        {
            return;
        }
        _currentTrimming = trimming;
        NativeMethods.TextFormatSetTrimming(_handle, (int)trimming);
    }

    /// <summary>
    /// Measures text and returns metrics including dimensions and font information.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="maxWidth">The maximum layout width.</param>
    /// <param name="maxHeight">The maximum layout height.</param>
    /// <returns>Text metrics including width, height, line height, and font metrics.</returns>
    public TextMetrics MeasureText(string text, float maxWidth, float maxHeight)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(text))
        {
            // Return font metrics only for empty text
            return GetFontMetrics();
        }

        var result = NativeMethods.TextFormatMeasureText(_handle, text, text.Length, maxWidth, maxHeight, out var metrics);
        if (result != 0)
        {
            // Fallback to approximate values on error
            metrics = new TextMetrics
            {
                Width = 0,
                Height = FontSize * 1.2f,
                LineHeight = FontSize * 1.2f,
                Baseline = FontSize,
                Ascent = FontSize,
                Descent = FontSize * 0.2f,
                LineGap = 0,
                LineCount = 1
            };
        }
        return metrics;
    }

    /// <summary>
    /// Hit-tests a point against a text layout to determine the character at that position.
    /// </summary>
    /// <param name="text">The full text of the layout.</param>
    /// <param name="maxWidth">The maximum layout width.</param>
    /// <param name="maxHeight">The maximum layout height.</param>
    /// <param name="pointX">The X coordinate to test.</param>
    /// <param name="pointY">The Y coordinate to test.</param>
    /// <param name="result">The hit test result.</param>
    /// <returns>True if the hit test succeeded.</returns>
    public bool HitTestPoint(string text, float maxWidth, float maxHeight, float pointX, float pointY, out TextHitTestResult result)
    {
        ThrowIfDisposed();
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var hr = NativeMethods.TextFormatHitTestPoint(_handle, text, text.Length, maxWidth, maxHeight, pointX, pointY, out result);
        return hr == 0;
    }

    /// <summary>
    /// Gets the caret position for a given text index within a layout.
    /// </summary>
    /// <param name="text">The full text of the layout.</param>
    /// <param name="maxWidth">The maximum layout width.</param>
    /// <param name="maxHeight">The maximum layout height.</param>
    /// <param name="textPosition">The character index.</param>
    /// <param name="isTrailingHit">Whether to get the trailing edge position.</param>
    /// <param name="result">The hit test result with caret position.</param>
    /// <returns>True if the query succeeded.</returns>
    public bool HitTestTextPosition(string text, float maxWidth, float maxHeight, uint textPosition, bool isTrailingHit, out TextHitTestResult result)
    {
        ThrowIfDisposed();
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var hr = NativeMethods.TextFormatHitTestTextPosition(_handle, text, text.Length, maxWidth, maxHeight, textPosition, isTrailingHit ? 1 : 0, out result);
        return hr == 0;
    }

    /// <summary>
    /// Gets font metrics without measuring text.
    /// This is useful for determining line height before text content is known.
    /// </summary>
    /// <returns>Font metrics including ascent, descent, line gap, and natural line height.</returns>
    public TextMetrics GetFontMetrics()
    {
        ThrowIfDisposed();
        var result = NativeMethods.TextFormatGetFontMetrics(_handle, out var metrics);
        if (result != 0)
        {
            // Fallback to approximate values on error
            metrics = new TextMetrics
            {
                Width = 0,
                Height = 0,
                LineHeight = FontSize * 1.2f,
                Baseline = FontSize,
                Ascent = FontSize,
                Descent = FontSize * 0.2f,
                LineGap = 0,
                LineCount = 0
            };
        }
        return metrics;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        var handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            NativeMethods.TextFormatDestroy(handle);
        }

        GC.SuppressFinalize(this);
    }

    ~NativeTextFormat()
    {
        // Do NOT call native destroy from finalizer.
        // The native context (DWrite factory) may already be destroyed,
        // causing stack overflow or access violation during shutdown.
        Volatile.Write(ref _disposed, 1);
        Volatile.Write(ref _handle, nint.Zero);
    }
}
