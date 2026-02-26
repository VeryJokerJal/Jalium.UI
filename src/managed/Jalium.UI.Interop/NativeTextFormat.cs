namespace Jalium.UI.Interop;

/// <summary>
/// Text alignment options.
/// </summary>
public enum TextAlignment
{
    Leading = 0,
    Trailing = 1,
    Center = 2,
    Justified = 3
}

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
    private bool _disposed;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether the text format is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

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
    /// <param name="alignment">The text alignment.</param>
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
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            NativeMethods.TextFormatDestroy(_handle);
            _handle = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~NativeTextFormat()
    {
        // Do NOT call native destroy from finalizer.
        // The native context (DWrite factory) may already be destroyed,
        // causing stack overflow or access violation during shutdown.
        _disposed = true;
        _handle = nint.Zero;
    }
}
