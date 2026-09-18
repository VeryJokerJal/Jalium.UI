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
    private readonly object _lifetimeGate = new();
    private nint _handle;
    private int _disposed; // 0 = live, 1 = logically disposed; writes are serialized by _lifetimeGate
    private int _activeNativeUses;
    private RenderContext.BackendResourceLease? _contextLease;
    private TextTrimmingMode? _currentTrimming;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => Volatile.Read(ref _handle);

    /// <summary>
    /// Gets whether the text format is valid.
    /// </summary>
    public bool IsValid =>
        Volatile.Read(ref _handle) != nint.Zero &&
        Volatile.Read(ref _disposed) == 0;

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

        RenderContext.BackendResourceLease? lease = context.AcquireBackendResourceLease();
        try
        {
            _handle = NativeMethods.TextFormatCreate(context.Handle, fontFamily, fontSize, fontWeight, fontStyle);
            if (_handle == nint.Zero)
            {
                throw new InvalidOperationException("Failed to create text format");
            }

            _contextLease = lease;
            lease = null;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>
    /// Sets the text alignment.
    /// </summary>
    /// <param name="alignment">The text alignment. Mapped directly to DWRITE_TEXT_ALIGNMENT (Left=Leading, Right=Trailing, Center, Justify=Justified).</param>
    public void SetTextAlignment(TextAlignment alignment)
    {
        using var nativeUse = AcquireNativeUse();
        NativeMethods.TextFormatSetAlignment(nativeUse.Handle, (int)alignment);
    }

    /// <summary>
    /// Sets the paragraph alignment.
    /// </summary>
    /// <param name="alignment">The paragraph alignment.</param>
    public void SetParagraphAlignment(ParagraphAlignment alignment)
    {
        using var nativeUse = AcquireNativeUse();
        NativeMethods.TextFormatSetParagraphAlignment(nativeUse.Handle, (int)alignment);
    }

    /// <summary>
    /// Sets the text trimming mode.
    /// </summary>
    /// <param name="trimming">The trimming mode.</param>
    public void SetTrimming(TextTrimmingMode trimming)
    {
        using var nativeUse = AcquireNativeUse();
        if (_currentTrimming == trimming)
        {
            return;
        }
        _currentTrimming = trimming;
        NativeMethods.TextFormatSetTrimming(nativeUse.Handle, (int)trimming);
    }

    /// <summary>
    /// Sets the per-format text anti-alias mode. Mirrors the WPF
    /// <c>TextOptions.TextRenderingMode</c> attached property: each text
    /// element can independently choose Aliased / Grayscale / ClearType
    /// instead of inheriting the process-wide value. Auto delegates to
    /// <see cref="Jalium.UI.Media.TextOptions.ProcessTextRenderingMode"/>,
    /// which itself resolves to the platform default (ClearType on Windows,
    /// Grayscale elsewhere).
    /// </summary>
    public void SetTextRenderingMode(int mode)
    {
        using var nativeUse = AcquireNativeUse();
        NativeMethods.TextFormatSetTextRenderingMode(nativeUse.Handle, mode);
    }

    /// <summary>
    /// Sets the per-format text formatting mode. Mirrors the WPF
    /// <c>TextOptions.TextFormattingMode</c> attached property:
    /// 0 = Ideal (resolution-independent glyph metrics, WPF default);
    /// 1 = Display (pixel-snapped — sharper at small sizes, less uniform
    /// under DPI scaling).
    /// </summary>
    public void SetTextFormattingMode(int mode)
    {
        using var nativeUse = AcquireNativeUse();
        NativeMethods.TextFormatSetTextFormattingMode(nativeUse.Handle, mode);
    }

    /// <summary>
    /// Sets the per-format text hinting mode. Mirrors the WPF
    /// <c>TextOptions.TextHintingMode</c> attached property:
    /// 0 = Auto (backend decides via the font's gasp table);
    /// 1 = Fixed (full hinting — sharper static text);
    /// 2 = Animated (hinting suppressed — smoother sub-pixel motion).
    /// </summary>
    public void SetTextHintingMode(int mode)
    {
        using var nativeUse = AcquireNativeUse();
        NativeMethods.TextFormatSetTextHintingMode(nativeUse.Handle, mode);
    }

    /// <summary>
    /// Sets per-format sub-pixel glyph positioning. Off (default) snaps every
    /// glyph pen to a whole physical pixel (WPF Display-mode stability); on
    /// keeps 1/8-pixel phases measured from the final screen position so a run
    /// can slide or scale without per-glyph whole-pixel stepping. The glyph
    /// bitmaps stay crisp (point-sampled); only their placement gains
    /// sub-pixel accuracy.
    /// </summary>
    public void SetSubpixelPositioning(bool enabled)
    {
        using var nativeUse = AcquireNativeUse();
        NativeMethods.TextFormatSetSubpixelPositioning(nativeUse.Handle, enabled ? 1 : 0);
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
        using var nativeUse = AcquireNativeUse();
        if (string.IsNullOrEmpty(text))
        {
            // Return font metrics only for empty text
            return GetFontMetricsCore(nativeUse.Handle);
        }

        var result = NativeMethods.TextFormatMeasureText(
            nativeUse.Handle,
            text,
            text.Length,
            maxWidth,
            maxHeight,
            out var metrics);

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
        else if (metrics.WidthIncludingTrailingWhitespace < metrics.Width)
        {
            // Backends without a distinct trailing-whitespace metric fall back
            // to their regular width instead of publishing an invalid zero.
            metrics.WidthIncludingTrailingWhitespace = metrics.Width;
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
        using var nativeUse = AcquireNativeUse();
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var hr = NativeMethods.TextFormatHitTestPoint(
            nativeUse.Handle,
            text,
            text.Length,
            maxWidth,
            maxHeight,
            pointX,
            pointY,
            out result);
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
        using var nativeUse = AcquireNativeUse();
        result = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var hr = NativeMethods.TextFormatHitTestTextPosition(
            nativeUse.Handle,
            text,
            text.Length,
            maxWidth,
            maxHeight,
            textPosition,
            isTrailingHit ? 1 : 0,
            out result);
        return hr == 0;
    }

    /// <summary>
    /// Gets font metrics without measuring text.
    /// This is useful for determining line height before text content is known.
    /// </summary>
    /// <returns>Font metrics including ascent, descent, line gap, and natural line height.</returns>
    public TextMetrics GetFontMetrics()
    {
        using var nativeUse = AcquireNativeUse();
        return GetFontMetricsCore(nativeUse.Handle);
    }

    private TextMetrics GetFontMetricsCore(nint handle)
    {
        var result = NativeMethods.TextFormatGetFontMetrics(handle, out var metrics);
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

    /// <summary>Gets the x/cap heights and the unshaped zero/ideograph advances.</summary>
    public FontUnitMetrics GetFontUnitMetrics()
    {
        using var nativeUse = AcquireNativeUse();
        var metrics = new FontUnitMetrics { StructSize = 32 };
        try
        {
            if (NativeMethods.TextFormatGetFontUnitMetrics(nativeUse.Handle, ref metrics) == 0) return metrics;
        }
        catch (EntryPointNotFoundException) { }
        var line = GetFontMetricsCore(nativeUse.Handle);
        return FontUnitMetrics.Fallback(FontSize, line.Ascent, line.LineHeight);
    }

    internal void InvokeWithNativeUseForTesting(Action<nint> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var nativeUse = AcquireNativeUse();
        action(nativeUse.Handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        nint handle = nint.Zero;
        RenderContext.BackendResourceLease? contextLease = null;
        lock (_lifetimeGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            if (_activeNativeUses == 0)
            {
                DetachNativeResourcesLocked(out handle, out contextLease);
            }
        }

        try
        {
            ReleaseNativeResources(handle, contextLease, suppressExceptions: false);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    ~NativeTextFormat()
    {
        // Field initialization may fail before a native format is created. Such
        // an object is still finalizable, but has neither a gate nor resources.
        var lifetimeGate = _lifetimeGate;
        if (lifetimeGate == null)
        {
            return;
        }

        nint handle = nint.Zero;
        RenderContext.BackendResourceLease? contextLease = null;
        lock (lifetimeGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            if (_activeNativeUses == 0)
            {
                DetachNativeResourcesLocked(out handle, out contextLease);
            }
        }

        ReleaseNativeResources(handle, contextLease, suppressExceptions: true);
    }

    private NativeUse AcquireNativeUse()
    {
        if (!TryAcquireNativeUse(out var nativeUse))
        {
            throw new ObjectDisposedException(nameof(NativeTextFormat));
        }
        return nativeUse;
    }

    /// <summary>
    /// Pins the format through a caller's synchronous native operation. Drawing
    /// uses the non-throwing result to preserve its existing disposed-format no-op.
    /// </summary>
    internal bool TryAcquireNativeUse(out NativeUse nativeUse)
    {
        lock (_lifetimeGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _handle == nint.Zero)
            {
                nativeUse = default;
                return false;
            }

            _activeNativeUses++;
            nativeUse = new NativeUse(this, _handle);
            return true;
        }
    }

    private void ReleaseNativeUse()
    {
        nint handle = nint.Zero;
        RenderContext.BackendResourceLease? contextLease = null;
        lock (_lifetimeGate)
        {
            if (_activeNativeUses <= 0)
            {
                return;
            }

            _activeNativeUses--;
            if (_activeNativeUses == 0 && Volatile.Read(ref _disposed) != 0)
            {
                DetachNativeResourcesLocked(out handle, out contextLease);
            }
        }

        // Dispose may have logically retired the format while this call was in
        // flight. The last caller performs the deferred native destroy, but must
        // not turn a successful native operation into a cleanup exception.
        ReleaseNativeResources(handle, contextLease, suppressExceptions: true);
    }

    private void DetachNativeResourcesLocked(
        out nint handle,
        out RenderContext.BackendResourceLease? contextLease)
    {
        handle = Interlocked.Exchange(ref _handle, nint.Zero);
        contextLease = Interlocked.Exchange(ref _contextLease, null);
    }

    private static void ReleaseNativeResources(
        nint handle,
        RenderContext.BackendResourceLease? contextLease,
        bool suppressExceptions)
    {
        if (suppressExceptions)
        {
            try
            {
                if (handle != nint.Zero)
                {
                    NativeMethods.TextFormatDestroy(handle);
                }
            }
            catch
            {
                // Deferred/finalizer cleanup must not surface native failures.
            }

            try
            {
                contextLease?.Dispose();
            }
            catch
            {
                // Keep finalizer and in-flight-call cleanup exception-free.
            }

            return;
        }

        try
        {
            if (handle != nint.Zero)
            {
                NativeMethods.TextFormatDestroy(handle);
            }
        }
        finally
        {
            contextLease?.Dispose();
        }
    }

    internal ref struct NativeUse
    {
        private NativeTextFormat? _owner;

        internal NativeUse(NativeTextFormat owner, nint handle)
        {
            _owner = owner;
            Handle = handle;
        }

        internal nint Handle { get; }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.ReleaseNativeUse();
        }
    }
}
