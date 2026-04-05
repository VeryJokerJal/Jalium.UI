using System.Runtime.InteropServices;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Contains static properties for system-defined colors and brushes that correspond to Windows display elements.
/// </summary>
public static partial class SystemColors
{
    private const string WindowTextBrushResourceKey = "SystemColorWindowTextColorBrush";
    private const string WindowBrushResourceKey = "SystemColorWindowColorBrush";
    private const string ButtonFaceBrushResourceKey = "SystemColorButtonFaceColorBrush";
    private const string ButtonTextBrushResourceKey = "SystemColorButtonTextColorBrush";
    private const string HighlightBrushResourceKey = "SystemColorHighlightColorBrush";
    private const string HighlightTextBrushResourceKey = "SystemColorHighlightTextColorBrush";
    private const string HotlightBrushResourceKey = "SystemColorHotlightColorBrush";
    private const string GrayTextBrushResourceKey = "SystemColorGrayTextColorBrush";

    #region Win32 System Color Constants

    private const int COLOR_SCROLLBAR = 0;
    private const int COLOR_BACKGROUND = 1;
    private const int COLOR_ACTIVECAPTION = 2;
    private const int COLOR_INACTIVECAPTION = 3;
    private const int COLOR_MENU = 4;
    private const int COLOR_WINDOW = 5;
    private const int COLOR_WINDOWFRAME = 6;
    private const int COLOR_MENUTEXT = 7;
    private const int COLOR_WINDOWTEXT = 8;
    private const int COLOR_CAPTIONTEXT = 9;
    private const int COLOR_ACTIVEBORDER = 10;
    private const int COLOR_INACTIVEBORDER = 11;
    private const int COLOR_APPWORKSPACE = 12;
    private const int COLOR_HIGHLIGHT = 13;
    private const int COLOR_HIGHLIGHTTEXT = 14;
    private const int COLOR_BTNFACE = 15;
    private const int COLOR_BTNSHADOW = 16;
    private const int COLOR_GRAYTEXT = 17;
    private const int COLOR_BTNTEXT = 18;
    private const int COLOR_INACTIVECAPTIONTEXT = 19;
    private const int COLOR_BTNHIGHLIGHT = 20;
    private const int COLOR_3DDKSHADOW = 21;
    private const int COLOR_3DLIGHT = 22;
    private const int COLOR_INFOTEXT = 23;
    private const int COLOR_INFOBK = 24;
    private const int COLOR_HOTLIGHT = 26;
    private const int COLOR_GRADIENTACTIVECAPTION = 27;
    private const int COLOR_GRADIENTINACTIVECAPTION = 28;
    private const int COLOR_MENUHILIGHT = 29;
    private const int COLOR_MENUBAR = 30;

    [LibraryImport("user32.dll")]
    private static partial uint GetSysColor(int nIndex);

    /// <summary>
    /// Converts a Win32 COLORREF (0x00BBGGRR) to a Jalium.UI Color.
    /// </summary>
    private static Color ColorFromSysColor(int index)
    {
        uint cr = GetSysColor(index);
        byte r = (byte)(cr & 0xFF);
        byte g = (byte)((cr >> 8) & 0xFF);
        byte b = (byte)((cr >> 16) & 0xFF);
        return Color.FromRgb(r, g, b);
    }

    #endregion

    // Color properties — prefer theme resource, then Win32 system color, then hardcoded fallback.
    public static Color ActiveBorderColor => ColorFromSysColor(COLOR_ACTIVEBORDER);
    public static Color ActiveCaptionColor => ColorFromSysColor(COLOR_ACTIVECAPTION);
    public static Color ActiveCaptionTextColor => ColorFromSysColor(COLOR_CAPTIONTEXT);
    public static Color AppWorkspaceColor => ColorFromSysColor(COLOR_APPWORKSPACE);
    public static Color ControlColor => ResolveColor(ButtonFaceBrushResourceKey, ColorFromSysColor(COLOR_BTNFACE));
    public static Color ControlDarkColor => ColorFromSysColor(COLOR_BTNSHADOW);
    public static Color ControlDarkDarkColor => ColorFromSysColor(COLOR_3DDKSHADOW);
    public static Color ControlLightColor => ColorFromSysColor(COLOR_3DLIGHT);
    public static Color ControlLightLightColor => ColorFromSysColor(COLOR_BTNHIGHLIGHT);
    public static Color ControlTextColor => ResolveColor(ButtonTextBrushResourceKey, ColorFromSysColor(COLOR_BTNTEXT));
    public static Color DesktopColor => ColorFromSysColor(COLOR_BACKGROUND);
    public static Color GradientActiveCaptionColor => ColorFromSysColor(COLOR_GRADIENTACTIVECAPTION);
    public static Color GradientInactiveCaptionColor => ColorFromSysColor(COLOR_GRADIENTINACTIVECAPTION);
    public static Color GrayTextColor => ResolveColor(GrayTextBrushResourceKey, ColorFromSysColor(COLOR_GRAYTEXT));
    public static Color HighlightColor => ResolveColor(HighlightBrushResourceKey, ColorFromSysColor(COLOR_HIGHLIGHT));
    public static Color HighlightTextColor => ResolveColor(HighlightTextBrushResourceKey, ColorFromSysColor(COLOR_HIGHLIGHTTEXT));
    public static Color HotTrackColor => ResolveColor(HotlightBrushResourceKey, ColorFromSysColor(COLOR_HOTLIGHT));
    public static Color InactiveBorderColor => ColorFromSysColor(COLOR_INACTIVEBORDER);
    public static Color InactiveCaptionColor => ColorFromSysColor(COLOR_INACTIVECAPTION);
    public static Color InactiveCaptionTextColor => ColorFromSysColor(COLOR_INACTIVECAPTIONTEXT);
    public static Color InfoColor => ColorFromSysColor(COLOR_INFOBK);
    public static Color InfoTextColor => ColorFromSysColor(COLOR_INFOTEXT);
    public static Color MenuColor => ColorFromSysColor(COLOR_MENU);
    public static Color MenuBarColor => ColorFromSysColor(COLOR_MENUBAR);
    public static Color MenuHighlightColor => ResolveColor(HighlightBrushResourceKey, ColorFromSysColor(COLOR_MENUHILIGHT));
    public static Color MenuTextColor => ColorFromSysColor(COLOR_MENUTEXT);
    public static Color ScrollBarColor => ColorFromSysColor(COLOR_SCROLLBAR);
    public static Color WindowColor => ResolveColor(WindowBrushResourceKey, ColorFromSysColor(COLOR_WINDOW));
    public static Color WindowFrameColor => ColorFromSysColor(COLOR_WINDOWFRAME);
    public static Color WindowTextColor => ResolveColor(WindowTextBrushResourceKey, ColorFromSysColor(COLOR_WINDOWTEXT));

    // Brush properties (lazy-initialized)
    private static SolidColorBrush? _controlBrush;
    private static SolidColorBrush? _controlTextBrush;
    private static SolidColorBrush? _highlightBrush;
    private static SolidColorBrush? _highlightTextBrush;
    private static SolidColorBrush? _windowBrush;
    private static SolidColorBrush? _windowTextBrush;
    private static SolidColorBrush? _grayTextBrush;
    private static SolidColorBrush? _menuBrush;
    private static SolidColorBrush? _menuTextBrush;
    private static SolidColorBrush? _infoBrush;
    private static SolidColorBrush? _infoTextBrush;
    private static SolidColorBrush? _activeCaptionBrush;
    private static SolidColorBrush? _inactiveCaptionBrush;
    private static SolidColorBrush? _hotTrackBrush;
    private static SolidColorBrush? _scrollBarBrush;

    public static SolidColorBrush ControlBrush => ResolveSolidColorBrush(ButtonFaceBrushResourceKey, ref _controlBrush, ControlColor);
    public static SolidColorBrush ControlTextBrush => ResolveSolidColorBrush(ButtonTextBrushResourceKey, ref _controlTextBrush, ControlTextColor);
    public static SolidColorBrush HighlightBrush => ResolveSolidColorBrush(HighlightBrushResourceKey, ref _highlightBrush, HighlightColor);
    public static SolidColorBrush HighlightTextBrush => ResolveSolidColorBrush(HighlightTextBrushResourceKey, ref _highlightTextBrush, HighlightTextColor);
    public static SolidColorBrush WindowBrush => ResolveSolidColorBrush(WindowBrushResourceKey, ref _windowBrush, WindowColor);
    public static SolidColorBrush WindowTextBrush => ResolveSolidColorBrush(WindowTextBrushResourceKey, ref _windowTextBrush, WindowTextColor);
    public static SolidColorBrush GrayTextBrush => ResolveSolidColorBrush(GrayTextBrushResourceKey, ref _grayTextBrush, GrayTextColor);
    public static SolidColorBrush MenuBrush => _menuBrush ??= new SolidColorBrush(MenuColor);
    public static SolidColorBrush MenuTextBrush => _menuTextBrush ??= new SolidColorBrush(MenuTextColor);
    public static SolidColorBrush InfoBrush => _infoBrush ??= new SolidColorBrush(InfoColor);
    public static SolidColorBrush InfoTextBrush => _infoTextBrush ??= new SolidColorBrush(InfoTextColor);
    public static SolidColorBrush ActiveCaptionBrush => _activeCaptionBrush ??= new SolidColorBrush(ActiveCaptionColor);
    public static SolidColorBrush InactiveCaptionBrush => _inactiveCaptionBrush ??= new SolidColorBrush(InactiveCaptionColor);
    public static SolidColorBrush HotTrackBrush => ResolveSolidColorBrush(HotlightBrushResourceKey, ref _hotTrackBrush, HotTrackColor);
    public static SolidColorBrush ScrollBarBrush => _scrollBarBrush ??= new SolidColorBrush(ScrollBarColor);

    // ResourceKeys for use in XAML
    public static readonly object ControlBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(ControlBrush));
    public static readonly object ControlTextBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(ControlTextBrush));
    public static readonly object HighlightBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(HighlightBrush));
    public static readonly object HighlightTextBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(HighlightTextBrush));
    public static readonly object WindowBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(WindowBrush));
    public static readonly object WindowTextBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(WindowTextBrush));

    private static Color ResolveColor(string resourceKey, Color fallback)
    {
        if (TryResolveBrush(resourceKey) is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return fallback;
    }

    private static SolidColorBrush ResolveSolidColorBrush(string resourceKey, ref SolidColorBrush? fallbackCache, Color fallbackColor)
    {
        if (TryResolveBrush(resourceKey) is SolidColorBrush brush)
        {
            return brush;
        }

        return fallbackCache ??= new SolidColorBrush(fallbackColor);
    }

    private static Brush? TryResolveBrush(string resourceKey)
    {
        var app = Application.Current;
        if (app?.Resources != null &&
            app.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return null;
    }
}

/// <summary>
/// An object identifier for component resources.
/// </summary>
public sealed class ComponentResourceKey
{
    public ComponentResourceKey(Type typeInTargetAssembly, string resourceId)
    {
        TypeInTargetAssembly = typeInTargetAssembly;
        ResourceId = resourceId;
    }

    public Type TypeInTargetAssembly { get; }
    public string ResourceId { get; }

    public override bool Equals(object? obj) =>
        obj is ComponentResourceKey other &&
        TypeInTargetAssembly == other.TypeInTargetAssembly &&
        ResourceId == other.ResourceId;

    public override int GetHashCode() => HashCode.Combine(TypeInTargetAssembly, ResourceId);

    public override string ToString() => $"{TypeInTargetAssembly.Name}.{ResourceId}";
}
