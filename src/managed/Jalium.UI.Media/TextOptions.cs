namespace Jalium.UI.Media;

/// <summary>
/// Specifies the formatting method for text.
/// </summary>
public enum TextFormattingMode
{
    /// <summary>
    /// Text is displayed with resolution-independent glyph ideal metrics.
    /// </summary>
    Ideal = 0,

    /// <summary>
    /// Text is displayed with metrics that produce glyphs snapped to the pixel grid on screen.
    /// </summary>
    Display = 1
}

/// <summary>
/// Specifies the rendering mode for text.
/// </summary>
public enum TextRenderingMode
{
    /// <summary>
    /// Text is rendered with the most appropriate rendering algorithm automatically.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Text is rendered with bilevel anti-aliasing.
    /// </summary>
    Aliased = 1,

    /// <summary>
    /// Text is rendered with grayscale anti-aliasing.
    /// </summary>
    Grayscale = 2,

    /// <summary>
    /// Text is rendered with ClearType anti-aliasing.
    /// </summary>
    ClearType = 3
}

/// <summary>
/// Specifies whether text hinting is on or off.
/// </summary>
public enum TextHintingMode
{
    /// <summary>
    /// The text rendering engine determines the best hinting mode automatically.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Hinting is performed on the text using fixed-point hinting values.
    /// </summary>
    Fixed = 1,

    /// <summary>
    /// Hinting is performed using animated values.
    /// </summary>
    Animated = 2
}

/// <summary>
/// Provides a set of attached properties that affects text rendering in an element.
/// </summary>
public static class TextOptions
{
    private static TextRenderingMode _processTextRenderingMode = TextRenderingMode.Auto;

    /// <summary>
    /// Gets or sets the process-wide text rendering mode. The setter raises
    /// <see cref="ProcessTextRenderingModeChanged"/> so the native interop layer
    /// (Jalium.UI.Interop) can forward the value to the native glyph atlas, which
    /// then switches between ClearType and Grayscale rasterization for every
    /// subsequent glyph. Default is <see cref="TextRenderingMode.Auto"/>, which
    /// the native side resolves to ClearType on Windows (matching every WPF /
    /// Win32 desktop app) and Grayscale on every other platform (macOS,
    /// Android, iOS, Linux). Set <see cref="TextRenderingMode.Grayscale"/>
    /// explicitly if you author high-DPI tooling or render text to an
    /// off-screen target that gets resampled — both scenarios make ClearType's
    /// sub-pixel fringe distracting.
    /// </summary>
    /// <remarks>
    /// Per-element <see cref="TextRenderingModeProperty"/> overrides are not yet
    /// honoured by the rendering pipeline — they are stored on the element but
    /// the native side currently consumes only the process-wide value. Setting
    /// the process-wide mode flips ClearType ↔ Grayscale for the whole UI
    /// within ~one frame, so the two modes coexist over the lifetime of the
    /// process rather than in the same frame.
    /// </remarks>
    public static TextRenderingMode ProcessTextRenderingMode
    {
        get => _processTextRenderingMode;
        set
        {
            // First touch: wake the native Interop bridge even when the IL
            // trimmer removed Jalium.UI.Interop.TextRenderingBridge's module
            // initializer (it is reachable only by metadata so trimming with
            // <TrimMode>full</TrimMode> can strip the call site). Reflection
            // is fenced behind Interlocked so a hot setter only pays the cost
            // once; failures are swallowed because the absence of Interop is
            // also a valid configuration (managed-only unit-test host).
            EnsureNativeBridgeAwake();

            if (_processTextRenderingMode == value)
                return;
            _processTextRenderingMode = value;
            ProcessTextRenderingModeChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Raised when <see cref="ProcessTextRenderingMode"/> changes. The
    /// Jalium.UI.Interop layer subscribes to forward the value into the native
    /// glyph atlas; consumers may also subscribe for their own bookkeeping.
    /// </summary>
    public static event Action<TextRenderingMode>? ProcessTextRenderingModeChanged;

    private static int s_bridgeWakeAttempted;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Bridge type lookup tolerates trimming via the catch block below.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Reflection target is preserved by DynamicDependency on TextRenderingBridge.")]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void EnsureNativeBridgeAwake()
    {
        if (System.Threading.Interlocked.Exchange(ref s_bridgeWakeAttempted, 1) != 0)
            return;
        try
        {
            var t = System.Type.GetType("Jalium.UI.Interop.TextRenderingBridge, Jalium.UI.Interop", throwOnError: false);
            t?.GetMethod("EnsureInitialized", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
              ?.Invoke(null, null);
        }
        catch
        {
            // Interop assembly absent (managed-only unit-test host) or the
            // bridge type was trimmed despite our DynamicDependency hints.
            // The set still raises ProcessTextRenderingModeChanged below;
            // any other subscriber gets the event normally.
        }
    }

    /// <summary>
    /// Identifies the TextFormattingMode attached property.
    /// </summary>
    public static readonly DependencyProperty TextFormattingModeProperty =
        DependencyProperty.RegisterAttached("TextFormattingMode", typeof(TextFormattingMode), typeof(TextOptions),
            new PropertyMetadata(TextFormattingMode.Ideal));

    /// <summary>
    /// Identifies the TextRenderingMode attached property.
    /// </summary>
    public static readonly DependencyProperty TextRenderingModeProperty =
        DependencyProperty.RegisterAttached("TextRenderingMode", typeof(TextRenderingMode), typeof(TextOptions),
            new PropertyMetadata(TextRenderingMode.Auto));

    /// <summary>
    /// Identifies the TextHintingMode attached property.
    /// </summary>
    public static readonly DependencyProperty TextHintingModeProperty =
        DependencyProperty.RegisterAttached("TextHintingMode", typeof(TextHintingMode), typeof(TextOptions),
            new PropertyMetadata(TextHintingMode.Auto));

    /// <summary>
    /// Gets the TextFormattingMode for the specified element.
    /// </summary>
    public static TextFormattingMode GetTextFormattingMode(DependencyObject element)
    {
        return (TextFormattingMode)(element.GetValue(TextFormattingModeProperty) ?? TextFormattingMode.Ideal);
    }

    /// <summary>
    /// Sets the TextFormattingMode for the specified element.
    /// </summary>
    public static void SetTextFormattingMode(DependencyObject element, TextFormattingMode value)
    {
        element.SetValue(TextFormattingModeProperty, value);
    }

    /// <summary>
    /// Gets the TextRenderingMode for the specified element.
    /// </summary>
    public static TextRenderingMode GetTextRenderingMode(DependencyObject element)
    {
        return (TextRenderingMode)(element.GetValue(TextRenderingModeProperty) ?? TextRenderingMode.Auto);
    }

    /// <summary>
    /// Sets the TextRenderingMode for the specified element.
    /// </summary>
    public static void SetTextRenderingMode(DependencyObject element, TextRenderingMode value)
    {
        element.SetValue(TextRenderingModeProperty, value);
    }

    /// <summary>
    /// Gets the TextHintingMode for the specified element.
    /// </summary>
    public static TextHintingMode GetTextHintingMode(DependencyObject element)
    {
        return (TextHintingMode)(element.GetValue(TextHintingModeProperty) ?? TextHintingMode.Auto);
    }

    /// <summary>
    /// Sets the TextHintingMode for the specified element.
    /// </summary>
    public static void SetTextHintingMode(DependencyObject element, TextHintingMode value)
    {
        element.SetValue(TextHintingModeProperty, value);
    }
}
