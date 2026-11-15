using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Specifies the CSS <c>appearance</c> keyword (CSS Basic User Interface Module Level 4).
/// </summary>
public enum CssAppearance
{
    Auto = 0,
    None = 1,
    Textfield = 2,
    MenulistButton = 3,
    Button = 4,
    Checkbox = 5,
    Radio = 6,
    Listbox = 7,
    Menulist = 8,
    Meter = 9,
    ProgressBar = 10,
    PushButton = 11,
    Searchfield = 12,
    SliderHorizontal = 13,
    SquareButton = 14,
    Textarea = 15,
}

/// <summary>
/// Represents the CSS <c>accent-color</c> value (<c>auto</c> or an explicit <see cref="Color"/>).
/// </summary>
public readonly record struct CssAccentColor(bool IsAuto, Color Color)
{
    public static readonly CssAccentColor Auto = new(true, Color.FromRgb(0x38, 0xBD, 0xF8));

    public static CssAccentColor FromColor(Color color) => new(false, color);

    public Color ResolveColor(Color? systemFallback = null) =>
        IsAuto ? (systemFallback ?? Color.FromRgb(0x38, 0xBD, 0xF8)) : Color;
}

/// <summary>
/// Specifies the CSS <c>resize</c> mode (CSS Basic User Interface Module Level 4).
/// </summary>
public enum CssResize
{
    None = 0,
    Both = 1,
    Horizontal = 2,
    Vertical = 3,
    Block = 4,
    Inline = 5,
}

/// <summary>
/// Specifies the CSS <c>touch-action</c> flags (Pointer Events Level 3).
/// </summary>
[Flags]
public enum CssTouchAction
{
    None = 0,
    Auto = 1 << 0,
    Manipulation = 1 << 1,
    PanX = 1 << 2,
    PanLeft = 1 << 3,
    PanRight = 1 << 4,
    PanY = 1 << 5,
    PanUp = 1 << 6,
    PanDown = 1 << 7,
    PinchZoom = 1 << 8,
}

/// <summary>
/// Specifies the CSS <c>color-scheme</c> flags (CSS Color Adjustment Module Level 1).
/// </summary>
[Flags]
public enum CssColorSchemeFlags
{
    Normal = 0,
    Light = 1 << 0,
    Dark = 1 << 1,
    Only = 1 << 2,
    LightDark = Light | Dark,
    OnlyLight = Light | Only,
    OnlyDark = Dark | Only,
}

/// <summary>
/// Specifies the CSS <c>forced-color-adjust</c> mode (CSS Color Adjustment Module Level 1).
/// </summary>
public enum CssForcedColorAdjust
{
    Auto = 0,
    None = 1,
    PreserveParentColor = 2,
}

/// <summary>
/// Specifies the CSS <c>print-color-adjust</c> mode (CSS Color Adjustment Module Level 1).
/// </summary>
public enum CssPrintColorAdjust
{
    Economy = 0,
    Exact = 1,
}

/// <summary>
/// Represents the CSS <c>text-size-adjust</c> value (<c>auto</c>, <c>none</c>, or a non-negative percentage factor).
/// </summary>
public readonly record struct CssTextSizeAdjust(bool IsAuto, bool IsNone, double Factor)
{
    public static readonly CssTextSizeAdjust Auto = new(true, false, 1.0);
    public static readonly CssTextSizeAdjust None = new(false, true, 1.0);

    public static CssTextSizeAdjust FromPercentage(double percentage) =>
        new(false, false, Math.Max(0, percentage / 100.0));
}

/// <summary>
/// Resolved surface palette for native control chrome under <c>color-scheme</c> and <c>accent-color</c>.
/// </summary>
public readonly record struct CssNativeChromePalette(
    string EffectiveTheme,
    Color SurfaceColor,
    Color BorderColor,
    Color TextColor,
    Color AccentColor,
    Color ScrollBarThumbColor,
    Color ScrollBarTrackColor);

/// <summary>
/// Implements Milestone R3 Native UI &amp; User Preference CSS properties:
/// <c>appearance</c>, <c>accent-color</c>, <c>resize</c>, <c>touch-action</c>,
/// <c>color-scheme</c>, <c>forced-color-adjust</c>, <c>print-color-adjust</c>,
/// <c>text-size-adjust</c>, <c>zoom</c>, and enhanced <c>cursor</c> (<c>url(...) [x y], fallback</c>).
/// </summary>
public static class CssNativeUiProperties
{
    private sealed class NativeUiBoxState
    {
        public bool AppliedAppearanceBorderOverride;
        public bool AppliedTouchActionPanningOverride;
        public bool AppliedZoomTransformOverride;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, NativeUiBoxState> s_states = new();

    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.RegisterAttached(
        "Appearance",
        typeof(CssAppearance),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(CssAppearance.Auto, OnAppearanceChanged));

    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.RegisterAttached(
        "AccentColor",
        typeof(CssAccentColor),
        typeof(CssNativeUiProperties),
        new FrameworkPropertyMetadata(
            CssAccentColor.Auto,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ResizeProperty = DependencyProperty.RegisterAttached(
        "Resize",
        typeof(CssResize),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(CssResize.None));

    public static readonly DependencyProperty TouchActionProperty = DependencyProperty.RegisterAttached(
        "TouchAction",
        typeof(CssTouchAction),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(CssTouchAction.Auto, OnTouchActionChanged));

    public static readonly DependencyProperty ColorSchemeProperty = DependencyProperty.RegisterAttached(
        "ColorScheme",
        typeof(CssColorSchemeFlags),
        typeof(CssNativeUiProperties),
        new FrameworkPropertyMetadata(
            CssColorSchemeFlags.Normal,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForcedColorAdjustProperty = DependencyProperty.RegisterAttached(
        "ForcedColorAdjust",
        typeof(CssForcedColorAdjust),
        typeof(CssNativeUiProperties),
        new FrameworkPropertyMetadata(
            CssForcedColorAdjust.Auto,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PrintColorAdjustProperty = DependencyProperty.RegisterAttached(
        "PrintColorAdjust",
        typeof(CssPrintColorAdjust),
        typeof(CssNativeUiProperties),
        new FrameworkPropertyMetadata(
            CssPrintColorAdjust.Economy,
            FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty TextSizeAdjustProperty = DependencyProperty.RegisterAttached(
        "TextSizeAdjust",
        typeof(CssTextSizeAdjust),
        typeof(CssNativeUiProperties),
        new FrameworkPropertyMetadata(
            CssTextSizeAdjust.Auto,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.RegisterAttached(
        "Zoom",
        typeof(double),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(1.0, OnZoomChanged));

    public static readonly DependencyProperty CursorUrlProperty = DependencyProperty.RegisterAttached(
        "CursorUrl",
        typeof(string),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CursorHotspotProperty = DependencyProperty.RegisterAttached(
        "CursorHotspot",
        typeof(Point?),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FallbackCursorProperty = DependencyProperty.RegisterAttached(
        "FallbackCursor",
        typeof(Cursor),
        typeof(CssNativeUiProperties),
        new PropertyMetadata(null));

    public static CssAppearance GetAppearance(DependencyObject element) =>
        (CssAppearance)(element.GetValue(AppearanceProperty) ?? CssAppearance.Auto);

    public static void SetAppearance(DependencyObject element, CssAppearance value) =>
        element.SetValue(AppearanceProperty, value);

    public static CssAccentColor GetAccentColor(DependencyObject element) =>
        (CssAccentColor)(element.GetValue(AccentColorProperty) ?? CssAccentColor.Auto);

    public static void SetAccentColor(DependencyObject element, CssAccentColor value) =>
        element.SetValue(AccentColorProperty, value);

    public static CssResize GetResize(DependencyObject element) =>
        (CssResize)(element.GetValue(ResizeProperty) ?? CssResize.None);

    public static void SetResize(DependencyObject element, CssResize value) =>
        element.SetValue(ResizeProperty, value);

    public static CssTouchAction GetTouchAction(DependencyObject element) =>
        (CssTouchAction)(element.GetValue(TouchActionProperty) ?? CssTouchAction.Auto);

    public static void SetTouchAction(DependencyObject element, CssTouchAction value) =>
        element.SetValue(TouchActionProperty, value);

    public static CssColorSchemeFlags GetColorScheme(DependencyObject element) =>
        (CssColorSchemeFlags)(element.GetValue(ColorSchemeProperty) ?? CssColorSchemeFlags.Normal);

    public static void SetColorScheme(DependencyObject element, CssColorSchemeFlags value) =>
        element.SetValue(ColorSchemeProperty, value);

    public static CssForcedColorAdjust GetForcedColorAdjust(DependencyObject element) =>
        (CssForcedColorAdjust)(element.GetValue(ForcedColorAdjustProperty) ?? CssForcedColorAdjust.Auto);

    public static void SetForcedColorAdjust(DependencyObject element, CssForcedColorAdjust value) =>
        element.SetValue(ForcedColorAdjustProperty, value);

    public static CssPrintColorAdjust GetPrintColorAdjust(DependencyObject element) =>
        (CssPrintColorAdjust)(element.GetValue(PrintColorAdjustProperty) ?? CssPrintColorAdjust.Economy);

    public static void SetPrintColorAdjust(DependencyObject element, CssPrintColorAdjust value) =>
        element.SetValue(PrintColorAdjustProperty, value);

    public static CssTextSizeAdjust GetTextSizeAdjust(DependencyObject element) =>
        (CssTextSizeAdjust)(element.GetValue(TextSizeAdjustProperty) ?? CssTextSizeAdjust.Auto);

    public static void SetTextSizeAdjust(DependencyObject element, CssTextSizeAdjust value) =>
        element.SetValue(TextSizeAdjustProperty, value);

    public static double GetZoom(DependencyObject element) =>
        (double)(element.GetValue(ZoomProperty) ?? 1.0);

    public static void SetZoom(DependencyObject element, double value) =>
        element.SetValue(ZoomProperty, value);

    public static string? GetCursorUrl(DependencyObject element) =>
        element.GetValue(CursorUrlProperty) as string;

    public static void SetCursorUrl(DependencyObject element, string? value) =>
        element.SetValue(CursorUrlProperty, value);

    public static Point? GetCursorHotspot(DependencyObject element) =>
        element.GetValue(CursorHotspotProperty) as Point?;

    public static void SetCursorHotspot(DependencyObject element, Point? value) =>
        element.SetValue(CursorHotspotProperty, value);

    public static Cursor? GetFallbackCursor(DependencyObject element) =>
        element.GetValue(FallbackCursorProperty) as Cursor;

    public static void SetFallbackCursor(DependencyObject element, Cursor? value) =>
        element.SetValue(FallbackCursorProperty, value);

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Registers R3 native UI CSS properties into CssPropertyRegistry before style sheets are parsed.")]
    internal static void Hook()
        => CssPropertyRegistry.RegisterExtension(Register);

    public static void Register()
    {
        RegisterAppearanceProperty("appearance");
        RegisterAppearanceProperty("-webkit-appearance");
        RegisterAppearanceProperty("-moz-appearance");

        CssPropertyRegistry.Register(new()
        {
            Name = "accent-color",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = AccentColorProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseAccentColor(ref reader, out var accent) && reader.AtEnd
                    ? new CssImmediateValue(AccentColorProperty, accent)
                    : null,
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "resize",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ResizeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseResize(ref reader, out var resize) && reader.AtEnd
                    ? new CssImmediateValue(ResizeProperty, resize)
                    : null,
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "touch-action",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = TouchActionProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseTouchAction(ref reader, out var touchAction) && reader.AtEnd
                    ? new CssImmediateValue(TouchActionProperty, touchAction)
                    : null,
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "color-scheme",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ColorSchemeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseColorScheme(ref reader, out var scheme) && reader.AtEnd
                    ? new CssImmediateValue(ColorSchemeProperty, scheme)
                    : null,
        });

        RegisterForcedColorAdjustProperty("forced-color-adjust");
        RegisterForcedColorAdjustProperty("forced-colors-adjust");

        RegisterPrintColorAdjustProperty("print-color-adjust");
        RegisterPrintColorAdjustProperty("-webkit-print-color-adjust");

        RegisterTextSizeAdjustProperty("text-size-adjust");
        RegisterTextSizeAdjustProperty("-webkit-text-size-adjust");

        CssPropertyRegistry.Register(new()
        {
            Name = "zoom",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ZoomProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseZoom(ref reader, out var zoom) && reader.AtEnd
                    ? new CssImmediateValue(ZoomProperty, zoom)
                    : null,
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "cursor",
            Kind = CssPropertyKind.Longhand,
            TransitionTargetDpName = nameof(FrameworkElement.Cursor),
            StorageProperty = FrameworkElement.CursorProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!TryParseCursorReader(ref reader, out var cursor, out var url, out var hotspot, out var fallback) ||
                    !reader.AtEnd)
                {
                    return null;
                }

                return new CssImmediateValue(
                    new[] { FrameworkElement.CursorProperty, CursorUrlProperty, CursorHotspotProperty, FallbackCursorProperty },
                    new object?[] { cursor, url, hotspot, fallback });
            },
        });
    }

    private static void RegisterAppearanceProperty(string name)
    {
        CssPropertyRegistry.Register(new()
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = AppearanceProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseAppearance(ref reader, out var appearance) && reader.AtEnd
                    ? new CssImmediateValue(AppearanceProperty, appearance)
                    : null,
        });
    }

    private static void RegisterForcedColorAdjustProperty(string name)
    {
        CssPropertyRegistry.Register(new()
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ForcedColorAdjustProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseForcedColorAdjust(ref reader, out var val) && reader.AtEnd
                    ? new CssImmediateValue(ForcedColorAdjustProperty, val)
                    : null,
        });
    }

    private static void RegisterPrintColorAdjustProperty(string name)
    {
        CssPropertyRegistry.Register(new()
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PrintColorAdjustProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParsePrintColorAdjust(ref reader, out var val) && reader.AtEnd
                    ? new CssImmediateValue(PrintColorAdjustProperty, val)
                    : null,
        });
    }

    private static void RegisterTextSizeAdjustProperty(string name)
    {
        CssPropertyRegistry.Register(new()
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = TextSizeAdjustProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseTextSizeAdjust(ref reader, out var val) && reader.AtEnd
                    ? new CssImmediateValue(TextSizeAdjustProperty, val)
                    : null,
        });
    }

    internal static bool TryParseAppearance(ref CssTokenReader reader, out CssAppearance appearance)
    {
        appearance = CssAppearance.Auto;
        if (!reader.TryReadIdent(out var ident))
            return false;

        var kw = ident.ToString().ToLowerInvariant();
        switch (kw)
        {
            case "none":
                appearance = CssAppearance.None;
                return true;
            case "auto":
                appearance = CssAppearance.Auto;
                return true;
            case "textfield":
                appearance = CssAppearance.Textfield;
                return true;
            case "menulist-button":
                appearance = CssAppearance.MenulistButton;
                return true;
            case "button":
                appearance = CssAppearance.Button;
                return true;
            case "checkbox":
                appearance = CssAppearance.Checkbox;
                return true;
            case "radio":
                appearance = CssAppearance.Radio;
                return true;
            case "listbox":
                appearance = CssAppearance.Listbox;
                return true;
            case "menulist":
                appearance = CssAppearance.Menulist;
                return true;
            case "meter":
                appearance = CssAppearance.Meter;
                return true;
            case "progress-bar":
                appearance = CssAppearance.ProgressBar;
                return true;
            case "push-button":
                appearance = CssAppearance.PushButton;
                return true;
            case "searchfield":
                appearance = CssAppearance.Searchfield;
                return true;
            case "slider-horizontal":
                appearance = CssAppearance.SliderHorizontal;
                return true;
            case "square-button":
                appearance = CssAppearance.SquareButton;
                return true;
            case "textarea":
                appearance = CssAppearance.Textarea;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseAccentColor(ref CssTokenReader reader, out CssAccentColor accent)
    {
        accent = CssAccentColor.Auto;
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        {
            reader = probe;
            accent = CssAccentColor.Auto;
            return true;
        }

        if (CssColorParser.TryParseContextual(ref reader, out var color, out var current, out var deferred))
        {
            if (current || deferred is not null)
                return false;
            accent = CssAccentColor.FromColor(color);
            return true;
        }

        return false;
    }

    internal static bool TryParseResize(ref CssTokenReader reader, out CssResize resize)
    {
        resize = CssResize.None;
        if (!reader.TryReadIdent(out var ident))
            return false;

        var kw = ident.ToString().ToLowerInvariant();
        switch (kw)
        {
            case "none":
                resize = CssResize.None;
                return true;
            case "both":
                resize = CssResize.Both;
                return true;
            case "horizontal":
                resize = CssResize.Horizontal;
                return true;
            case "vertical":
                resize = CssResize.Vertical;
                return true;
            case "block":
                resize = CssResize.Block;
                return true;
            case "inline":
                resize = CssResize.Inline;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseTouchAction(ref CssTokenReader reader, out CssTouchAction touchAction)
    {
        touchAction = CssTouchAction.Auto;
        if (!reader.TryReadIdent(out var firstIdent))
            return false;

        var first = firstIdent.ToString().ToLowerInvariant();
        if (first == "auto")
        {
            touchAction = CssTouchAction.Auto;
            return reader.AtEnd;
        }
        if (first == "none")
        {
            touchAction = CssTouchAction.None;
            return reader.AtEnd;
        }
        if (first == "manipulation")
        {
            touchAction = CssTouchAction.Manipulation;
            return reader.AtEnd;
        }

        touchAction = CssTouchAction.None;
        var hasHorizontal = false;
        var hasVertical = false;
        var hasPinch = false;

        if (!TryMapTouchToken(first, ref touchAction, ref hasHorizontal, ref hasVertical, ref hasPinch))
            return false;

        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var nextIdent))
                return false;
            var kw = nextIdent.ToString().ToLowerInvariant();
            if (!TryMapTouchToken(kw, ref touchAction, ref hasHorizontal, ref hasVertical, ref hasPinch))
                return false;
        }

        return true;
    }

    private static bool TryMapTouchToken(
        string token,
        ref CssTouchAction flags,
        ref bool hasHorizontal,
        ref bool hasVertical,
        ref bool hasPinch)
    {
        switch (token)
        {
            case "pan-x":
                if (hasHorizontal) return false;
                hasHorizontal = true;
                flags |= CssTouchAction.PanX;
                return true;
            case "pan-left":
                if (hasHorizontal) return false;
                hasHorizontal = true;
                flags |= CssTouchAction.PanLeft;
                return true;
            case "pan-right":
                if (hasHorizontal) return false;
                hasHorizontal = true;
                flags |= CssTouchAction.PanRight;
                return true;
            case "pan-y":
                if (hasVertical) return false;
                hasVertical = true;
                flags |= CssTouchAction.PanY;
                return true;
            case "pan-up":
                if (hasVertical) return false;
                hasVertical = true;
                flags |= CssTouchAction.PanUp;
                return true;
            case "pan-down":
                if (hasVertical) return false;
                hasVertical = true;
                flags |= CssTouchAction.PanDown;
                return true;
            case "pinch-zoom":
                if (hasPinch) return false;
                hasPinch = true;
                flags |= CssTouchAction.PinchZoom;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseColorScheme(ref CssTokenReader reader, out CssColorSchemeFlags scheme)
    {
        scheme = CssColorSchemeFlags.Normal;
        if (!reader.TryReadIdent(out var firstIdent))
            return false;

        var first = firstIdent.ToString().ToLowerInvariant();
        if (first == "normal")
            return reader.AtEnd;

        var sawLightOrDark = false;
        if (!ApplyColorSchemeToken(first, ref scheme, ref sawLightOrDark))
            return false;

        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var nextIdent))
                return false;
            var kw = nextIdent.ToString().ToLowerInvariant();
            if (!ApplyColorSchemeToken(kw, ref scheme, ref sawLightOrDark))
                return false;
        }

        return sawLightOrDark;
    }

    private static bool ApplyColorSchemeToken(string token, ref CssColorSchemeFlags scheme, ref bool sawLightOrDark)
    {
        switch (token)
        {
            case "light":
                scheme |= CssColorSchemeFlags.Light;
                sawLightOrDark = true;
                return true;
            case "dark":
                scheme |= CssColorSchemeFlags.Dark;
                sawLightOrDark = true;
                return true;
            case "only":
                if ((scheme & CssColorSchemeFlags.Only) != 0)
                    return false;
                scheme |= CssColorSchemeFlags.Only;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseForcedColorAdjust(ref CssTokenReader reader, out CssForcedColorAdjust value)
    {
        value = CssForcedColorAdjust.Auto;
        if (!reader.TryReadIdent(out var ident))
            return false;

        var kw = ident.ToString().ToLowerInvariant();
        switch (kw)
        {
            case "auto":
                value = CssForcedColorAdjust.Auto;
                return true;
            case "none":
                value = CssForcedColorAdjust.None;
                return true;
            case "preserve-parent-color":
                value = CssForcedColorAdjust.PreserveParentColor;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParsePrintColorAdjust(ref CssTokenReader reader, out CssPrintColorAdjust value)
    {
        value = CssPrintColorAdjust.Economy;
        if (!reader.TryReadIdent(out var ident))
            return false;

        var kw = ident.ToString().ToLowerInvariant();
        switch (kw)
        {
            case "economy":
                value = CssPrintColorAdjust.Economy;
                return true;
            case "exact":
                value = CssPrintColorAdjust.Exact;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseTextSizeAdjust(ref CssTokenReader reader, out CssTextSizeAdjust value)
    {
        value = CssTextSizeAdjust.Auto;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            var kw = ident.ToString().ToLowerInvariant();
            if (kw == "auto")
            {
                reader = probe;
                value = CssTextSizeAdjust.Auto;
                return true;
            }
            if (kw == "none")
            {
                reader = probe;
                value = CssTextSizeAdjust.None;
                return true;
            }
            return false;
        }

        if (probe.TryReadNumber(out var num, out var unit) &&
            unit == CssUnit.Percent &&
            double.IsFinite(num) &&
            num >= 0)
        {
            reader = probe;
            value = CssTextSizeAdjust.FromPercentage(num);
            return true;
        }

        return false;
    }

    internal static bool TryParseZoom(ref CssTokenReader reader, out double zoom)
    {
        zoom = 1.0;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                zoom = 1.0;
                return true;
            }
            return false;
        }

        if (probe.TryReadNumber(out var num, out var unit) && double.IsFinite(num) && num > 0)
        {
            if (unit == CssUnit.None)
            {
                reader = probe;
                zoom = num;
                return true;
            }
            if (unit == CssUnit.Percent)
            {
                reader = probe;
                zoom = num / 100.0;
                return true;
            }
        }

        return false;
    }

    public static bool TryParseCursor(
        string cssText,
        out Cursor? cursor,
        out string? url,
        out Point? hotspot,
        out Cursor? fallback)
    {
        cursor = null;
        url = null;
        hotspot = null;
        fallback = null;

        if (string.IsNullOrWhiteSpace(cssText))
            return false;

        var reader = new CssTokenReader(cssText.AsSpan(), default);
        return TryParseCursorReader(ref reader, out cursor, out url, out hotspot, out fallback) && reader.AtEnd;
    }

    public static bool ApplyCursorCss(FrameworkElement element, string cssText)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!TryParseCursor(cssText, out var cursor, out var url, out var hotspot, out var fallback))
            return false;

        element.Cursor = cursor;
        SetCursorUrl(element, url);
        SetCursorHotspot(element, hotspot);
        SetFallbackCursor(element, fallback);
        return true;
    }

    internal static bool TryParseCursorReader(
        ref CssTokenReader reader,
        out Cursor? cursor,
        out string? url,
        out Point? hotspot,
        out Cursor? fallback)
    {
        cursor = null;
        url = null;
        hotspot = null;
        fallback = null;

        // Parse zero or more `<url> [<x> <y>]?,` entries followed by a mandatory fallback keyword.
        while (true)
        {
            var probe = reader;
            if (!probe.TryReadFunction(out var fn, out var fnArgs) ||
                !fn.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!CssShapeImageValue.TryReadUrl(ref fnArgs, out var parsedUrl) || string.IsNullOrWhiteSpace(parsedUrl))
                return false;

            Point? parsedHotspot = null;
            var coordProbe = probe;
            if (coordProbe.TryReadNumber(out var hx, out var hxUnit) && hxUnit == CssUnit.None && double.IsFinite(hx))
            {
                if (!coordProbe.TryReadNumber(out var hy, out var hyUnit) || hyUnit != CssUnit.None || !double.IsFinite(hy))
                    return false;
                parsedHotspot = new Point(hx, hy);
                probe = coordProbe;
            }

            if (!probe.TryReadComma())
                return false;

            // Keep the first valid custom URL + hotspot in the cascade list.
            if (url is null)
            {
                url = parsedUrl;
                hotspot = parsedHotspot;
            }

            reader = probe;
        }

        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
            return false;

        if (!TryMapCursorKeyword(ident, out var mappedCursor))
            return false;

        fallback = mappedCursor;
        cursor = mappedCursor;
        if (url is not null && File.Exists(url))
        {
            try
            {
                cursor = new Cursor(url);
            }
            catch
            {
                cursor = mappedCursor;
            }
        }

        return true;
    }

    internal static bool TryMapCursorKeyword(ReadOnlySpan<char> name, out Cursor? cursor)
    {
        cursor = null;
        if (Eq(name, "auto")) { return true; }
        if (Eq(name, "default")) { cursor = Cursors.Arrow; return true; }
        if (Eq(name, "pointer")) { cursor = Cursors.Hand; return true; }
        if (Eq(name, "text") || Eq(name, "vertical-text")) { cursor = Cursors.IBeam; return true; }
        if (Eq(name, "wait")) { cursor = Cursors.Wait; return true; }
        if (Eq(name, "progress")) { cursor = Cursors.AppStarting; return true; }
        if (Eq(name, "help")) { cursor = Cursors.Help; return true; }
        if (Eq(name, "move")) { cursor = Cursors.SizeAll; return true; }
        if (Eq(name, "none")) { cursor = Cursors.None; return true; }
        if (Eq(name, "crosshair") || Eq(name, "cell")) { cursor = Cursors.Cross; return true; }
        if (Eq(name, "not-allowed") || Eq(name, "no-drop")) { cursor = Cursors.No; return true; }
        if (Eq(name, "grab") || Eq(name, "grabbing")) { cursor = Cursors.Hand; return true; }
        if (Eq(name, "all-scroll")) { cursor = Cursors.ScrollAll; return true; }
        if (Eq(name, "col-resize") || Eq(name, "ew-resize") || Eq(name, "e-resize") || Eq(name, "w-resize"))
        {
            cursor = Cursors.SizeWE;
            return true;
        }
        if (Eq(name, "row-resize") || Eq(name, "ns-resize") || Eq(name, "n-resize") || Eq(name, "s-resize"))
        {
            cursor = Cursors.SizeNS;
            return true;
        }
        if (Eq(name, "nesw-resize") || Eq(name, "ne-resize") || Eq(name, "sw-resize"))
        {
            cursor = Cursors.SizeNESW;
            return true;
        }
        if (Eq(name, "nwse-resize") || Eq(name, "nw-resize") || Eq(name, "se-resize"))
        {
            cursor = Cursors.SizeNWSE;
            return true;
        }
        if (Eq(name, "context-menu") || Eq(name, "alias") || Eq(name, "copy") ||
            Eq(name, "zoom-in") || Eq(name, "zoom-out"))
        {
            cursor = Cursors.Arrow;
            return true;
        }

        return false;

        static bool Eq(ReadOnlySpan<char> text, string candidate)
            => text.Equals(candidate, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNativeChromeSuppressed(DependencyObject element) =>
        GetAppearance(element) == CssAppearance.None;

    public static SolidColorBrush GetResolvedAccentBrush(DependencyObject element, Color? systemFallback = null)
    {
        var accent = GetAccentColor(element);
        var brush = new SolidColorBrush(accent.ResolveColor(systemFallback));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    public static CssResize ResolvePhysicalResize(FrameworkElement element)
    {
        var resize = GetResize(element);
        if (resize is not (CssResize.Block or CssResize.Inline))
            return resize;

        var isVertical = CssWritingModeProperties.IsVertical(CssWritingModeProperties.GetWritingMode(element));
        return resize switch
        {
            CssResize.Block => isVertical ? CssResize.Horizontal : CssResize.Vertical,
            CssResize.Inline => isVertical ? CssResize.Vertical : CssResize.Horizontal,
            _ => resize,
        };
    }

    public static string ResolveEffectiveTheme(DependencyObject element, bool systemPrefersDark = false)
    {
        var scheme = GetColorScheme(element);
        var supportsLight = (scheme & CssColorSchemeFlags.Light) != 0;
        var supportsDark = (scheme & CssColorSchemeFlags.Dark) != 0;

        if (supportsDark && !supportsLight)
            return "dark";
        if (supportsLight && !supportsDark)
            return "light";
        return systemPrefersDark ? "dark" : "light";
    }

    public static CssNativeChromePalette ResolveChromeSurfacePalette(
        DependencyObject element,
        bool systemPrefersDark = false)
    {
        var theme = ResolveEffectiveTheme(element, systemPrefersDark);
        var isDark = theme == "dark";
        var accent = GetAccentColor(element).ResolveColor(
            isDark ? Color.FromRgb(0x38, 0xBD, 0xF8) : Color.FromRgb(0x02, 0x84, 0xC7));

        return isDark
            ? new CssNativeChromePalette(
                EffectiveTheme: "dark",
                SurfaceColor: Color.FromRgb(0x11, 0x16, 0x22),
                BorderColor: Color.FromRgb(0x2B, 0x35, 0x48),
                TextColor: Color.FromRgb(0xF1, 0xF5, 0xF9),
                AccentColor: accent,
                ScrollBarThumbColor: Color.FromRgb(0x47, 0x55, 0x69),
                ScrollBarTrackColor: Color.FromRgb(0x0F, 0x17, 0x2A))
            : new CssNativeChromePalette(
                EffectiveTheme: "light",
                SurfaceColor: Color.FromRgb(0xF8, 0xFA, 0xFC),
                BorderColor: Color.FromRgb(0xCB, 0xD5, 0xE1),
                TextColor: Color.FromRgb(0x0F, 0x17, 0x2A),
                AccentColor: accent,
                ScrollBarThumbColor: Color.FromRgb(0x94, 0xA3, 0xB8),
                ScrollBarTrackColor: Color.FromRgb(0xE2, 0xE8, 0xF0));
    }

    public static double GetEffectiveZoom(DependencyObject element)
    {
        var product = 1.0;
        Visual? cur = element as Visual;
        if (cur is null && element is DependencyObject dobj)
        {
            var z = GetZoom(dobj);
            return double.IsFinite(z) && z > 0 ? z : 1.0;
        }

        while (cur is not null)
        {
            var z = GetZoom(cur);
            if (double.IsFinite(z) && z > 0)
                product *= z;
            cur = cur.VisualParent;
        }

        return product;
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
            return;

        var state = s_states.GetOrCreateValue(fe);
        var appearance = (CssAppearance)(e.NewValue ?? CssAppearance.Auto);
        if (appearance == CssAppearance.None && fe is Control)
        {
            if (!fe.HasLocalValue(Control.BorderThicknessProperty))
            {
                fe.SetLayerValue(
                    Control.BorderThicknessProperty,
                    new Thickness(0),
                    DependencyObject.LayerValueSource.ParentTemplate,
                    allowAutoTransition: false);
                state.AppliedAppearanceBorderOverride = true;
            }
        }
        else if (state.AppliedAppearanceBorderOverride && fe is Control)
        {
            fe.ClearLayerValue(Control.BorderThicknessProperty, DependencyObject.LayerValueSource.ParentTemplate);
            state.AppliedAppearanceBorderOverride = false;
        }

        fe.InvalidateMeasure();
        fe.InvalidateVisual();
    }

    private static void OnTouchActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv)
            return;

        var state = s_states.GetOrCreateValue(sv);
        var action = (CssTouchAction)(e.NewValue ?? CssTouchAction.Auto);
        if (action == CssTouchAction.Auto)
        {
            if (state.AppliedTouchActionPanningOverride)
            {
                sv.ClearLayerValue(ScrollViewer.PanningModeProperty, DependencyObject.LayerValueSource.ParentTemplate);
                state.AppliedTouchActionPanningOverride = false;
            }
            return;
        }

        if (!sv.HasLocalValue(ScrollViewer.PanningModeProperty))
        {
            var hasH = (action & (CssTouchAction.PanX | CssTouchAction.PanLeft | CssTouchAction.PanRight | CssTouchAction.Manipulation)) != 0;
            var hasV = (action & (CssTouchAction.PanY | CssTouchAction.PanUp | CssTouchAction.PanDown | CssTouchAction.Manipulation)) != 0;
            var mode = (hasH, hasV) switch
            {
                (true, true) => PanningMode.Both,
                (true, false) => PanningMode.HorizontalOnly,
                (false, true) => PanningMode.VerticalOnly,
                _ => PanningMode.None,
            };

            sv.SetLayerValue(
                ScrollViewer.PanningModeProperty,
                mode,
                DependencyObject.LayerValueSource.ParentTemplate,
                allowAutoTransition: false);
            state.AppliedTouchActionPanningOverride = true;
        }
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
            return;

        var state = s_states.GetOrCreateValue(fe);
        var zoom = e.NewValue is double z && double.IsFinite(z) && z > 0 ? z : 1.0;

        if (Math.Abs(zoom - 1.0) > 1e-6)
        {
            if (!fe.HasLocalValue(FrameworkElement.LayoutTransformProperty))
            {
                fe.SetLayerValue(
                    FrameworkElement.LayoutTransformProperty,
                    new ScaleTransform(zoom, zoom),
                    DependencyObject.LayerValueSource.ParentTemplate,
                    allowAutoTransition: false);
                state.AppliedZoomTransformOverride = true;
            }
        }
        else if (state.AppliedZoomTransformOverride)
        {
            fe.ClearLayerValue(FrameworkElement.LayoutTransformProperty, DependencyObject.LayerValueSource.ParentTemplate);
            state.AppliedZoomTransformOverride = false;
        }

        fe.InvalidateMeasure();
        fe.InvalidateArrange();
        fe.InvalidateVisual();
    }
}
