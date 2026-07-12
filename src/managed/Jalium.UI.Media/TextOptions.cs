namespace Jalium.UI.Media;

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
    /// the native side resolves to <see cref="TextRenderingMode.Grayscale"/> on
    /// every platform (Windows / macOS / Android / iOS / Linux). High-DPI
    /// screens, RenderTargetBitmap resampling, ScaleTransform, and the
    /// Vello / Impeller / Vulkan / software backends all interact badly with
    /// ClearType sub-pixel fringes, so Grayscale is the safe universal
    /// default — opt in to <see cref="TextRenderingMode.ClearType"/>
    /// explicitly when you want Windows-style sub-pixel rendering for a
    /// pure-desktop, 100% DPI-aligned UI.
    /// </summary>
    /// <remarks>
    /// Per-element <see cref="TextRenderingModeProperty"/> overrides are now
    /// honoured end-to-end: the value flows through <see cref="FormattedText"/>
    /// to <c>NativeTextFormat</c> to the backend glyph atlas, and D3D12 / Vulkan
    /// rasterize each format in its own mode within the same frame (separate
    /// glyph cache buckets per (glyph, mode) tuple). The process-wide setter
    /// remains the right knob for whole-UI policy; per-element overrides win
    /// when a specific element needs a different mode (e.g. a Grayscale text
    /// panel rendered into a ClearType chrome).
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void EnsureNativeBridgeAwake()
    {
        if (System.Threading.Interlocked.Exchange(ref s_bridgeWakeAttempted, 1) != 0)
            return;

        Jalium.UI.Interop.TextRenderingBridge.EnsureInitialized();
    }

    /// <summary>
    /// Identifies the <c>TextOptions.TextFormattingMode</c> attached property.
    /// </summary>
    /// <remarks>
    /// Registered with <see cref="FrameworkPropertyMetadataOptions.AffectsMeasure"/> |
    /// <see cref="FrameworkPropertyMetadataOptions.AffectsRender"/> |
    /// <see cref="FrameworkPropertyMetadataOptions.Inherits"/> and a
    /// <see cref="ValidateValueCallback"/> that rejects enum values outside
    /// <see cref="TextFormattingMode.Ideal"/>/<see cref="TextFormattingMode.Display"/> —
    /// matching WPF (<c>System.Windows.Media.TextOptions.TextFormattingModeProperty</c>).
    /// Switching between Ideal and Display changes both glyph metrics (measure pass) and
    /// rasterization (render pass) and the value walks down the visual tree like
    /// <see cref="Control.FontFamily"/>, so all three flags are load-bearing.
    /// </remarks>
    public static readonly DependencyProperty TextFormattingModeProperty =
        DependencyProperty.RegisterAttached(
            "TextFormattingMode",
            typeof(TextFormattingMode),
            typeof(TextOptions),
            new FrameworkPropertyMetadata(
                TextFormattingMode.Ideal,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits),
            IsTextFormattingModeValid);

    /// <summary>
    /// Identifies the <c>TextOptions.TextRenderingMode</c> attached property.
    /// </summary>
    /// <remarks>
    /// Registered with <see cref="FrameworkPropertyMetadataOptions.AffectsRender"/> |
    /// <see cref="FrameworkPropertyMetadataOptions.Inherits"/> and a
    /// <see cref="ValidateValueCallback"/> that gates the four valid
    /// <see cref="TextRenderingMode"/> members — matching WPF
    /// (<c>System.Windows.Media.TextOptions.TextRenderingModeProperty</c>). The
    /// process-wide <see cref="ProcessTextRenderingMode"/> currently dominates because
    /// the native glyph atlas can rasterize only one mode at a time per frame; the
    /// per-element store is kept so authoring tools can read/write the value and so
    /// downstream per-element honouring can land without an API break.
    /// </remarks>
    public static readonly DependencyProperty TextRenderingModeProperty =
        DependencyProperty.RegisterAttached(
            "TextRenderingMode",
            typeof(TextRenderingMode),
            typeof(TextOptions),
            new FrameworkPropertyMetadata(
                TextRenderingMode.Auto,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits),
            ValidateEnums.IsTextRenderingModeValid);

    /// <summary>
    /// Identifies the <c>TextOptions.TextHintingMode</c> attached property.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="TextOptionsInternal.TextHintingModeProperty"/> via
    /// <see cref="DependencyProperty.AddOwner(System.Type, PropertyMetadata?)"/> so that
    /// XAML can address it as either <c>TextOptions.TextHintingMode</c> or the internal
    /// alias the framework uses for animation passes — same pattern WPF uses
    /// (<c>System.Windows.Media.TextOptions.TextHintingModeProperty =
    /// TextOptionsInternal.TextHintingModeProperty.AddOwner(typeof(TextOptions))</c>).
    /// </remarks>
    public static readonly DependencyProperty TextHintingModeProperty =
        TextOptionsInternal.TextHintingModeProperty.AddOwner(typeof(TextOptions));

    internal static bool IsTextFormattingModeValid(object? valueObject)
    {
        if (valueObject is not TextFormattingMode mode)
            return false;
        // Cheaper than Enum.IsDefined (no reflection / boxed allocations) and
        // matches WPF's hand-rolled validator verbatim.
        return mode is TextFormattingMode.Ideal or TextFormattingMode.Display;
    }

    /// <summary>
    /// Sets the <see cref="TextFormattingModeProperty"/> on the specified element.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static void SetTextFormattingMode(DependencyObject element, TextFormattingMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextFormattingModeProperty, value);
    }

    /// <summary>
    /// Reads the <see cref="TextFormattingModeProperty"/> from the specified element.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static TextFormattingMode GetTextFormattingMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TextFormattingMode)(element.GetValue(TextFormattingModeProperty) ?? TextFormattingMode.Ideal);
    }

    /// <summary>
    /// Sets the <see cref="TextRenderingModeProperty"/> on the specified element.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static void SetTextRenderingMode(DependencyObject element, TextRenderingMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextRenderingModeProperty, value);
    }

    /// <summary>
    /// Reads the <see cref="TextRenderingModeProperty"/> from the specified element.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static TextRenderingMode GetTextRenderingMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TextRenderingMode)(element.GetValue(TextRenderingModeProperty) ?? TextRenderingMode.Auto);
    }

    /// <summary>
    /// Sets the <see cref="TextHintingModeProperty"/> on the specified element.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static void SetTextHintingMode(DependencyObject element, TextHintingMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextHintingModeProperty, value);
    }

    /// <summary>
    /// Reads the <see cref="TextHintingModeProperty"/> from the specified element.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static TextHintingMode GetTextHintingMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TextHintingMode)(element.GetValue(TextHintingModeProperty) ?? TextHintingMode.Auto);
    }
}

/// <summary>
/// Centralised enum-range validators reused by <see cref="TextOptions"/>. Lives next to the
/// only call site to keep the public surface clean while still matching WPF's split between
/// the public <c>TextOptions</c> facade and the <c>System.Windows.Media.ValidateEnums</c>
/// helpers that gate every text-rendering attached property.
/// </summary>
internal static class ValidateEnums
{
    internal static bool IsTextRenderingModeValid(object? valueObject)
    {
        if (valueObject is not TextRenderingMode mode)
            return false;
        return mode is TextRenderingMode.Auto
            or TextRenderingMode.Aliased
            or TextRenderingMode.Grayscale
            or TextRenderingMode.ClearType;
    }

    internal static bool IsTextHintingModeValid(object? valueObject)
    {
        if (valueObject is not TextHintingMode mode)
            return false;
        return mode is TextHintingMode.Auto
            or TextHintingMode.Fixed
            or TextHintingMode.Animated;
    }
}

/// <summary>
/// Owns the canonical <see cref="DependencyProperty"/> instance for
/// <c>TextHintingMode</c> that <see cref="TextOptions.TextHintingModeProperty"/> aliases via
/// <see cref="DependencyProperty.AddOwner(System.Type, PropertyMetadata?)"/>. Mirrors the
/// WPF <c>MS.Internal.Media.TextOptionsInternal</c> type — the indirection exists so the
/// framework can register the property once, then share the same backing storage with
/// every consumer that needs to read or animate it.
/// </summary>
internal static class TextOptionsInternal
{
    public static readonly DependencyProperty TextHintingModeProperty =
        DependencyProperty.RegisterAttached(
            "TextHintingMode",
            typeof(TextHintingMode),
            typeof(TextOptionsInternal),
            new FrameworkPropertyMetadata(
                TextHintingMode.Auto,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits),
            ValidateEnums.IsTextHintingModeValid);
}

/// <summary>
/// Bridges <see cref="FormattedText"/> in the Core layer (which can't reference
/// <see cref="TextOptions"/> because Media is a downstream assembly) to the
/// attached-property values on a source element. Controls that want per-element
/// <c>TextOptions</c> to reach the native renderer call
/// <c>formattedText.ApplyTextOptionsFrom(this)</c> right after constructing
/// the <see cref="FormattedText"/>. Defaults (Auto/Ideal/Auto) are no-ops so
/// callers that haven't been retrofitted continue to render through the
/// process-wide fallback, matching their old behaviour exactly.
/// </summary>
public static class FormattedTextOptionsExtensions
{
    /// <summary>
    /// Reads the inherited <see cref="TextOptions"/> values from <paramref name="element"/>
    /// (or any of its ancestors, since the three properties are registered with
    /// <see cref="FrameworkPropertyMetadataOptions.Inherits"/>) and copies them
    /// into <paramref name="formattedText"/> as plain ints so
    /// <c>RenderTargetDrawingContext.DrawText</c> can push them straight to the
    /// native format without re-walking the visual tree.
    /// </summary>
    public static FormattedText ApplyTextOptionsFrom(this FormattedText formattedText, DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(formattedText);
        ArgumentNullException.ThrowIfNull(element);

        formattedText.TextRenderingMode  = (int)TextOptions.GetTextRenderingMode(element);
        formattedText.TextFormattingMode = (int)TextOptions.GetTextFormattingMode(element);
        formattedText.TextHintingMode    = (int)TextOptions.GetTextHintingMode(element);
        return formattedText;
    }
}
