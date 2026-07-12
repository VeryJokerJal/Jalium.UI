using System.Runtime.InteropServices;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Contains static properties for system-defined colors and brushes that correspond to Windows display elements.
/// </summary>
public static partial class SystemColors
{
    private const string WindowTextColorResourceKey = "SystemColorWindowTextColor";
    private const string WindowTextBrushResourceKey = "SystemColorWindowTextColorBrush";
    private const string WindowColorResourceKey = "SystemColorWindowColor";
    private const string WindowBrushResourceKey = "SystemColorWindowColorBrush";
    private const string ButtonFaceColorResourceKey = "SystemColorButtonFaceColor";
    private const string ButtonFaceBrushResourceKey = "SystemColorButtonFaceColorBrush";
    private const string ButtonTextColorResourceKey = "SystemColorButtonTextColor";
    private const string ButtonTextBrushResourceKey = "SystemColorButtonTextColorBrush";
    private const string HighlightColorResourceKey = "SystemColorHighlightColor";
    private const string HighlightBrushResourceKey = "SystemColorHighlightColorBrush";
    private const string HighlightTextColorResourceKey = "SystemColorHighlightTextColor";
    private const string HighlightTextBrushResourceKey = "SystemColorHighlightTextColorBrush";
    private const string HotlightColorResourceKey = "SystemColorHotlightColor";
    private const string HotlightBrushResourceKey = "SystemColorHotlightColorBrush";
    private const string GrayTextColorResourceKey = "SystemColorGrayTextColor";
    private const string GrayTextBrushResourceKey = "SystemColorGrayTextColorBrush";

    private const string AccentColorResourceKey = "SystemAccentColor";
    private const string AccentColorLight1ResourceKey = "SystemAccentColorLight1";
    private const string AccentColorLight2ResourceKey = "SystemAccentColorLight2";
    private const string AccentColorLight3ResourceKey = "SystemAccentColorLight3";
    private const string AccentColorDark1ResourceKey = "SystemAccentColorDark1";
    private const string AccentColorDark2ResourceKey = "SystemAccentColorDark2";
    private const string AccentColorDark3ResourceKey = "SystemAccentColorDark3";

    private static readonly object s_cacheLock = new();
    private static readonly Dictionary<string, SolidColorBrush> s_brushCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ResourceKey> s_keyCache = new(StringComparer.Ordinal);

    #region Win32 system color constants

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

    private static Color ColorFromSysColor(int index)
    {
        if (!OperatingSystem.IsWindows())
        {
            // SystemColors is the final resource fallback on non-Windows hosts, so this
            // path must remain deterministic and must never attempt to import user32.dll.
            return index switch
            {
                COLOR_WINDOW or COLOR_MENU or COLOR_INFOBK => Color.White,
                COLOR_WINDOWTEXT or COLOR_MENUTEXT or COLOR_INFOTEXT or COLOR_BTNTEXT => Color.Black,
                COLOR_HIGHLIGHT or COLOR_HOTLIGHT or COLOR_MENUHILIGHT => Color.FromRgb(0x00, 0x78, 0xD4),
                COLOR_HIGHLIGHTTEXT or COLOR_CAPTIONTEXT => Color.White,
                COLOR_BTNFACE or COLOR_SCROLLBAR => Color.FromRgb(0xF0, 0xF0, 0xF0),
                COLOR_GRAYTEXT => Color.FromRgb(0x6D, 0x6D, 0x6D),
                _ => Color.FromRgb(0x80, 0x80, 0x80),
            };
        }

        uint colorRef = GetSysColor(index);
        return Color.FromRgb(
            (byte)(colorRef & 0xFF),
            (byte)((colorRef >> 8) & 0xFF),
            (byte)((colorRef >> 16) & 0xFF));
    }

    #endregion

    #region Colors

    public static Color ActiveBorderColor => ColorFromSysColor(COLOR_ACTIVEBORDER);
    public static Color ActiveCaptionColor => ColorFromSysColor(COLOR_ACTIVECAPTION);
    public static Color ActiveCaptionTextColor => ColorFromSysColor(COLOR_CAPTIONTEXT);
    public static Color AppWorkspaceColor => ColorFromSysColor(COLOR_APPWORKSPACE);
    public static Color ControlColor => ResolveColor(ButtonFaceColorResourceKey, ButtonFaceBrushResourceKey, ColorFromSysColor(COLOR_BTNFACE));
    public static Color ControlDarkColor => ColorFromSysColor(COLOR_BTNSHADOW);
    public static Color ControlDarkDarkColor => ColorFromSysColor(COLOR_3DDKSHADOW);
    public static Color ControlLightColor => ColorFromSysColor(COLOR_3DLIGHT);
    public static Color ControlLightLightColor => ColorFromSysColor(COLOR_BTNHIGHLIGHT);
    public static Color ControlTextColor => ResolveColor(ButtonTextColorResourceKey, ButtonTextBrushResourceKey, ColorFromSysColor(COLOR_BTNTEXT));
    public static Color DesktopColor => ColorFromSysColor(COLOR_BACKGROUND);
    public static Color GradientActiveCaptionColor => ColorFromSysColor(COLOR_GRADIENTACTIVECAPTION);
    public static Color GradientInactiveCaptionColor => ColorFromSysColor(COLOR_GRADIENTINACTIVECAPTION);
    public static Color GrayTextColor => ResolveColor(GrayTextColorResourceKey, GrayTextBrushResourceKey, ColorFromSysColor(COLOR_GRAYTEXT));
    public static Color HighlightColor => ResolveColor(HighlightColorResourceKey, HighlightBrushResourceKey, ColorFromSysColor(COLOR_HIGHLIGHT));
    public static Color HighlightTextColor => ResolveColor(HighlightTextColorResourceKey, HighlightTextBrushResourceKey, ColorFromSysColor(COLOR_HIGHLIGHTTEXT));
    public static Color HotTrackColor => ResolveColor(HotlightColorResourceKey, HotlightBrushResourceKey, ColorFromSysColor(COLOR_HOTLIGHT));
    public static Color InactiveBorderColor => ColorFromSysColor(COLOR_INACTIVEBORDER);
    public static Color InactiveCaptionColor => ColorFromSysColor(COLOR_INACTIVECAPTION);
    public static Color InactiveCaptionTextColor => ColorFromSysColor(COLOR_INACTIVECAPTIONTEXT);
    public static Color InfoColor => ColorFromSysColor(COLOR_INFOBK);
    public static Color InfoTextColor => ColorFromSysColor(COLOR_INFOTEXT);
    public static Color MenuColor => ColorFromSysColor(COLOR_MENU);
    public static Color MenuBarColor => ColorFromSysColor(COLOR_MENUBAR);
    public static Color MenuHighlightColor => ResolveColor(HighlightColorResourceKey, HighlightBrushResourceKey, ColorFromSysColor(COLOR_MENUHILIGHT));
    public static Color MenuTextColor => ColorFromSysColor(COLOR_MENUTEXT);
    public static Color ScrollBarColor => ColorFromSysColor(COLOR_SCROLLBAR);
    public static Color WindowColor => ResolveColor(WindowColorResourceKey, WindowBrushResourceKey, ColorFromSysColor(COLOR_WINDOW));
    public static Color WindowFrameColor => ColorFromSysColor(COLOR_WINDOWFRAME);
    public static Color WindowTextColor => ResolveColor(WindowTextColorResourceKey, WindowTextBrushResourceKey, ColorFromSysColor(COLOR_WINDOWTEXT));

    public static Color AccentColor => ResolveAccentColor(AccentColorResourceKey, ThemeManager.CurrentAccentColor);
    public static Color AccentColorLight1 => ResolveAccentColor(AccentColorLight1ResourceKey, Blend(AccentColor, Color.White, 0.18));
    public static Color AccentColorLight2 => ResolveAccentColor(AccentColorLight2ResourceKey, Blend(AccentColor, Color.White, 0.34));
    public static Color AccentColorLight3 => ResolveAccentColor(AccentColorLight3ResourceKey, Blend(AccentColor, Color.White, 0.52));
    public static Color AccentColorDark1 => ResolveAccentColor(AccentColorDark1ResourceKey, Blend(AccentColor, Color.Black, 0.12));
    public static Color AccentColorDark2 => ResolveAccentColor(AccentColorDark2ResourceKey, Blend(AccentColor, Color.Black, 0.24));
    public static Color AccentColorDark3 => ResolveAccentColor(AccentColorDark3ResourceKey, Blend(AccentColor, Color.Black, 0.36));

    #endregion

    #region Color resource keys

    public static ResourceKey ActiveBorderColorKey => GetResourceKey(nameof(ActiveBorderColor));
    public static ResourceKey ActiveCaptionColorKey => GetResourceKey(nameof(ActiveCaptionColor));
    public static ResourceKey ActiveCaptionTextColorKey => GetResourceKey(nameof(ActiveCaptionTextColor));
    public static ResourceKey AppWorkspaceColorKey => GetResourceKey(nameof(AppWorkspaceColor));
    public static ResourceKey ControlColorKey => GetResourceKey(nameof(ControlColor));
    public static ResourceKey ControlDarkColorKey => GetResourceKey(nameof(ControlDarkColor));
    public static ResourceKey ControlDarkDarkColorKey => GetResourceKey(nameof(ControlDarkDarkColor));
    public static ResourceKey ControlLightColorKey => GetResourceKey(nameof(ControlLightColor));
    public static ResourceKey ControlLightLightColorKey => GetResourceKey(nameof(ControlLightLightColor));
    public static ResourceKey ControlTextColorKey => GetResourceKey(nameof(ControlTextColor));
    public static ResourceKey DesktopColorKey => GetResourceKey(nameof(DesktopColor));
    public static ResourceKey GradientActiveCaptionColorKey => GetResourceKey(nameof(GradientActiveCaptionColor));
    public static ResourceKey GradientInactiveCaptionColorKey => GetResourceKey(nameof(GradientInactiveCaptionColor));
    public static ResourceKey GrayTextColorKey => GetResourceKey(nameof(GrayTextColor));
    public static ResourceKey HighlightColorKey => GetResourceKey(nameof(HighlightColor));
    public static ResourceKey HighlightTextColorKey => GetResourceKey(nameof(HighlightTextColor));
    public static ResourceKey HotTrackColorKey => GetResourceKey(nameof(HotTrackColor));
    public static ResourceKey InactiveBorderColorKey => GetResourceKey(nameof(InactiveBorderColor));
    public static ResourceKey InactiveCaptionColorKey => GetResourceKey(nameof(InactiveCaptionColor));
    public static ResourceKey InactiveCaptionTextColorKey => GetResourceKey(nameof(InactiveCaptionTextColor));
    public static ResourceKey InfoColorKey => GetResourceKey(nameof(InfoColor));
    public static ResourceKey InfoTextColorKey => GetResourceKey(nameof(InfoTextColor));
    public static ResourceKey MenuColorKey => GetResourceKey(nameof(MenuColor));
    public static ResourceKey MenuBarColorKey => GetResourceKey(nameof(MenuBarColor));
    public static ResourceKey MenuHighlightColorKey => GetResourceKey(nameof(MenuHighlightColor));
    public static ResourceKey MenuTextColorKey => GetResourceKey(nameof(MenuTextColor));
    public static ResourceKey ScrollBarColorKey => GetResourceKey(nameof(ScrollBarColor));
    public static ResourceKey WindowColorKey => GetResourceKey(nameof(WindowColor));
    public static ResourceKey WindowFrameColorKey => GetResourceKey(nameof(WindowFrameColor));
    public static ResourceKey WindowTextColorKey => GetResourceKey(nameof(WindowTextColor));

    public static ResourceKey AccentColorKey => GetResourceKey(nameof(AccentColor));
    public static ResourceKey AccentColorLight1Key => GetResourceKey(nameof(AccentColorLight1));
    public static ResourceKey AccentColorLight2Key => GetResourceKey(nameof(AccentColorLight2));
    public static ResourceKey AccentColorLight3Key => GetResourceKey(nameof(AccentColorLight3));
    public static ResourceKey AccentColorDark1Key => GetResourceKey(nameof(AccentColorDark1));
    public static ResourceKey AccentColorDark2Key => GetResourceKey(nameof(AccentColorDark2));
    public static ResourceKey AccentColorDark3Key => GetResourceKey(nameof(AccentColorDark3));

    #endregion

    #region Brushes

    public static SolidColorBrush ActiveBorderBrush => GetSystemBrush(nameof(ActiveBorderBrush), null, ActiveBorderColor);
    public static SolidColorBrush ActiveCaptionBrush => GetSystemBrush(nameof(ActiveCaptionBrush), null, ActiveCaptionColor);
    public static SolidColorBrush ActiveCaptionTextBrush => GetSystemBrush(nameof(ActiveCaptionTextBrush), null, ActiveCaptionTextColor);
    public static SolidColorBrush AppWorkspaceBrush => GetSystemBrush(nameof(AppWorkspaceBrush), null, AppWorkspaceColor);
    public static SolidColorBrush ControlBrush => GetSystemBrush(nameof(ControlBrush), ButtonFaceBrushResourceKey, ControlColor);
    public static SolidColorBrush ControlDarkBrush => GetSystemBrush(nameof(ControlDarkBrush), null, ControlDarkColor);
    public static SolidColorBrush ControlDarkDarkBrush => GetSystemBrush(nameof(ControlDarkDarkBrush), null, ControlDarkDarkColor);
    public static SolidColorBrush ControlLightBrush => GetSystemBrush(nameof(ControlLightBrush), null, ControlLightColor);
    public static SolidColorBrush ControlLightLightBrush => GetSystemBrush(nameof(ControlLightLightBrush), null, ControlLightLightColor);
    public static SolidColorBrush ControlTextBrush => GetSystemBrush(nameof(ControlTextBrush), ButtonTextBrushResourceKey, ControlTextColor);
    public static SolidColorBrush DesktopBrush => GetSystemBrush(nameof(DesktopBrush), null, DesktopColor);
    public static SolidColorBrush GradientActiveCaptionBrush => GetSystemBrush(nameof(GradientActiveCaptionBrush), null, GradientActiveCaptionColor);
    public static SolidColorBrush GradientInactiveCaptionBrush => GetSystemBrush(nameof(GradientInactiveCaptionBrush), null, GradientInactiveCaptionColor);
    public static SolidColorBrush GrayTextBrush => GetSystemBrush(nameof(GrayTextBrush), GrayTextBrushResourceKey, GrayTextColor);
    public static SolidColorBrush HighlightBrush => GetSystemBrush(nameof(HighlightBrush), HighlightBrushResourceKey, HighlightColor);
    public static SolidColorBrush HighlightTextBrush => GetSystemBrush(nameof(HighlightTextBrush), HighlightTextBrushResourceKey, HighlightTextColor);
    public static SolidColorBrush HotTrackBrush => GetSystemBrush(nameof(HotTrackBrush), HotlightBrushResourceKey, HotTrackColor);
    public static SolidColorBrush InactiveBorderBrush => GetSystemBrush(nameof(InactiveBorderBrush), null, InactiveBorderColor);
    public static SolidColorBrush InactiveCaptionBrush => GetSystemBrush(nameof(InactiveCaptionBrush), null, InactiveCaptionColor);
    public static SolidColorBrush InactiveCaptionTextBrush => GetSystemBrush(nameof(InactiveCaptionTextBrush), null, InactiveCaptionTextColor);
    public static SolidColorBrush InfoBrush => GetSystemBrush(nameof(InfoBrush), null, InfoColor);
    public static SolidColorBrush InfoTextBrush => GetSystemBrush(nameof(InfoTextBrush), null, InfoTextColor);
    public static SolidColorBrush MenuBrush => GetSystemBrush(nameof(MenuBrush), null, MenuColor);
    public static SolidColorBrush MenuBarBrush => GetSystemBrush(nameof(MenuBarBrush), null, MenuBarColor);
    public static SolidColorBrush MenuHighlightBrush => GetSystemBrush(nameof(MenuHighlightBrush), null, MenuHighlightColor);
    public static SolidColorBrush MenuTextBrush => GetSystemBrush(nameof(MenuTextBrush), null, MenuTextColor);
    public static SolidColorBrush ScrollBarBrush => GetSystemBrush(nameof(ScrollBarBrush), null, ScrollBarColor);
    public static SolidColorBrush WindowBrush => GetSystemBrush(nameof(WindowBrush), WindowBrushResourceKey, WindowColor);
    public static SolidColorBrush WindowFrameBrush => GetSystemBrush(nameof(WindowFrameBrush), null, WindowFrameColor);
    public static SolidColorBrush WindowTextBrush => GetSystemBrush(nameof(WindowTextBrush), WindowTextBrushResourceKey, WindowTextColor);

    public static SolidColorBrush InactiveSelectionHighlightBrush =>
        SystemParameters.HighContrast ? HighlightBrush : ControlBrush;

    public static SolidColorBrush InactiveSelectionHighlightTextBrush =>
        SystemParameters.HighContrast ? HighlightTextBrush : ControlTextBrush;

    public static SolidColorBrush AccentColorBrush => GetAccentBrush(nameof(AccentColorBrush), AccentColor);
    public static SolidColorBrush AccentColorLight1Brush => GetAccentBrush(nameof(AccentColorLight1Brush), AccentColorLight1);
    public static SolidColorBrush AccentColorLight2Brush => GetAccentBrush(nameof(AccentColorLight2Brush), AccentColorLight2);
    public static SolidColorBrush AccentColorLight3Brush => GetAccentBrush(nameof(AccentColorLight3Brush), AccentColorLight3);
    public static SolidColorBrush AccentColorDark1Brush => GetAccentBrush(nameof(AccentColorDark1Brush), AccentColorDark1);
    public static SolidColorBrush AccentColorDark2Brush => GetAccentBrush(nameof(AccentColorDark2Brush), AccentColorDark2);
    public static SolidColorBrush AccentColorDark3Brush => GetAccentBrush(nameof(AccentColorDark3Brush), AccentColorDark3);

    #endregion

    #region Brush resource keys

    public static ResourceKey ActiveBorderBrushKey => GetResourceKey(nameof(ActiveBorderBrush));
    public static ResourceKey ActiveCaptionBrushKey => GetResourceKey(nameof(ActiveCaptionBrush));
    public static ResourceKey ActiveCaptionTextBrushKey => GetResourceKey(nameof(ActiveCaptionTextBrush));
    public static ResourceKey AppWorkspaceBrushKey => GetResourceKey(nameof(AppWorkspaceBrush));
    public static ResourceKey ControlBrushKey => GetResourceKey(nameof(ControlBrush));
    public static ResourceKey ControlDarkBrushKey => GetResourceKey(nameof(ControlDarkBrush));
    public static ResourceKey ControlDarkDarkBrushKey => GetResourceKey(nameof(ControlDarkDarkBrush));
    public static ResourceKey ControlLightBrushKey => GetResourceKey(nameof(ControlLightBrush));
    public static ResourceKey ControlLightLightBrushKey => GetResourceKey(nameof(ControlLightLightBrush));
    public static ResourceKey ControlTextBrushKey => GetResourceKey(nameof(ControlTextBrush));
    public static ResourceKey DesktopBrushKey => GetResourceKey(nameof(DesktopBrush));
    public static ResourceKey GradientActiveCaptionBrushKey => GetResourceKey(nameof(GradientActiveCaptionBrush));
    public static ResourceKey GradientInactiveCaptionBrushKey => GetResourceKey(nameof(GradientInactiveCaptionBrush));
    public static ResourceKey GrayTextBrushKey => GetResourceKey(nameof(GrayTextBrush));
    public static ResourceKey HighlightBrushKey => GetResourceKey(nameof(HighlightBrush));
    public static ResourceKey HighlightTextBrushKey => GetResourceKey(nameof(HighlightTextBrush));
    public static ResourceKey HotTrackBrushKey => GetResourceKey(nameof(HotTrackBrush));
    public static ResourceKey InactiveBorderBrushKey => GetResourceKey(nameof(InactiveBorderBrush));
    public static ResourceKey InactiveCaptionBrushKey => GetResourceKey(nameof(InactiveCaptionBrush));
    public static ResourceKey InactiveCaptionTextBrushKey => GetResourceKey(nameof(InactiveCaptionTextBrush));
    public static ResourceKey InfoBrushKey => GetResourceKey(nameof(InfoBrush));
    public static ResourceKey InfoTextBrushKey => GetResourceKey(nameof(InfoTextBrush));
    public static ResourceKey MenuBrushKey => GetResourceKey(nameof(MenuBrush));
    public static ResourceKey MenuBarBrushKey => GetResourceKey(nameof(MenuBarBrush));
    public static ResourceKey MenuHighlightBrushKey => GetResourceKey(nameof(MenuHighlightBrush));
    public static ResourceKey MenuTextBrushKey => GetResourceKey(nameof(MenuTextBrush));
    public static ResourceKey ScrollBarBrushKey => GetResourceKey(nameof(ScrollBarBrush));
    public static ResourceKey WindowBrushKey => GetResourceKey(nameof(WindowBrush));
    public static ResourceKey WindowFrameBrushKey => GetResourceKey(nameof(WindowFrameBrush));
    public static ResourceKey WindowTextBrushKey => GetResourceKey(nameof(WindowTextBrush));

    public static ResourceKey InactiveSelectionHighlightBrushKey =>
        FrameworkCompatibilityPreferences.GetAreInactiveSelectionHighlightBrushKeysSupported()
            ? GetResourceKey(nameof(InactiveSelectionHighlightBrush))
            : ControlBrushKey;

    public static ResourceKey InactiveSelectionHighlightTextBrushKey =>
        FrameworkCompatibilityPreferences.GetAreInactiveSelectionHighlightBrushKeysSupported()
            ? GetResourceKey(nameof(InactiveSelectionHighlightTextBrush))
            : ControlTextBrushKey;

    public static ResourceKey AccentColorBrushKey => GetResourceKey(nameof(AccentColorBrush));
    public static ResourceKey AccentColorLight1BrushKey => GetResourceKey(nameof(AccentColorLight1Brush));
    public static ResourceKey AccentColorLight2BrushKey => GetResourceKey(nameof(AccentColorLight2Brush));
    public static ResourceKey AccentColorLight3BrushKey => GetResourceKey(nameof(AccentColorLight3Brush));
    public static ResourceKey AccentColorDark1BrushKey => GetResourceKey(nameof(AccentColorDark1Brush));
    public static ResourceKey AccentColorDark2BrushKey => GetResourceKey(nameof(AccentColorDark2Brush));
    public static ResourceKey AccentColorDark3BrushKey => GetResourceKey(nameof(AccentColorDark3Brush));

    #endregion

    private static ResourceKey GetResourceKey(string resourceId)
    {
        lock (s_cacheLock)
        {
            if (!s_keyCache.TryGetValue(resourceId, out var key))
            {
                key = new ComponentResourceKey(typeof(SystemColors), resourceId);
                s_keyCache.Add(resourceId, key);
            }

            return key;
        }
    }

    private static Color ResolveColor(string colorResourceKey, string brushResourceKey, Color fallback)
    {
        // Existing theme dictionaries expose both forms. A solid brush remains the
        // authoritative value for compatibility; gradients are intentionally rejected.
        if (TryResolveResource(brushResourceKey) is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return TryResolveResource(colorResourceKey) is Color color ? color : fallback;
    }

    private static Color ResolveAccentColor(string resourceKey, Color fallback) =>
        TryResolveResource(resourceKey) is Color color ? color : fallback;

    private static SolidColorBrush GetSystemBrush(string cacheKey, string? brushResourceKey, Color color)
    {
        if (brushResourceKey != null && TryResolveResource(brushResourceKey) is SolidColorBrush themeBrush)
        {
            return themeBrush;
        }

        return GetCachedBrush(cacheKey, color);
    }

    private static SolidColorBrush GetAccentBrush(string cacheKey, Color color) =>
        SystemParameters.HighContrast ? HighlightTextBrush : GetCachedBrush(cacheKey, color);

    private static SolidColorBrush GetCachedBrush(string cacheKey, Color color)
    {
        lock (s_cacheLock)
        {
            if (!s_brushCache.TryGetValue(cacheKey, out var brush))
            {
                brush = new SolidColorBrush(color);
                s_brushCache.Add(cacheKey, brush);
            }
            else if (brush.Color != color)
            {
                // Retain object identity across theme/accent changes while refreshing its value.
                brush.Color = color;
            }

            return brush;
        }
    }

    private static object? TryResolveResource(string resourceKey)
    {
        var resources = Application.Current?.Resources;
        return resources != null && resources.TryGetValue(resourceKey, out var resource)
            ? resource
            : null;
    }

    private static Color Blend(Color color, Color target, double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);

        static byte Lerp(byte from, byte to, double amount) =>
            (byte)Math.Clamp((int)Math.Round(from + ((to - from) * amount)), 0, 255);

        return Color.FromArgb(
            Lerp(color.A, target.A, factor),
            Lerp(color.R, target.R, factor),
            Lerp(color.G, target.G, factor),
            Lerp(color.B, target.B, factor));
    }

    /// <summary>
    /// Resolves every system color and brush resource key exposed by this type.
    /// Application and element lookup call this after their own dictionaries miss.
    /// </summary>
    internal static bool TryGetResource(object resourceKey, out object? resource)
    {
        if (resourceKey is not ComponentResourceKey
            {
                TypeInTargetAssembly: { } ownerType,
                ResourceId: string resourceId,
            }
            || ownerType != typeof(SystemColors))
        {
            resource = null;
            return false;
        }

        resource = resourceId switch
        {
            nameof(ActiveBorderColor) => ActiveBorderColor,
            nameof(ActiveCaptionColor) => ActiveCaptionColor,
            nameof(ActiveCaptionTextColor) => ActiveCaptionTextColor,
            nameof(AppWorkspaceColor) => AppWorkspaceColor,
            nameof(ControlColor) => ControlColor,
            nameof(ControlDarkColor) => ControlDarkColor,
            nameof(ControlDarkDarkColor) => ControlDarkDarkColor,
            nameof(ControlLightColor) => ControlLightColor,
            nameof(ControlLightLightColor) => ControlLightLightColor,
            nameof(ControlTextColor) => ControlTextColor,
            nameof(DesktopColor) => DesktopColor,
            nameof(GradientActiveCaptionColor) => GradientActiveCaptionColor,
            nameof(GradientInactiveCaptionColor) => GradientInactiveCaptionColor,
            nameof(GrayTextColor) => GrayTextColor,
            nameof(HighlightColor) => HighlightColor,
            nameof(HighlightTextColor) => HighlightTextColor,
            nameof(HotTrackColor) => HotTrackColor,
            nameof(InactiveBorderColor) => InactiveBorderColor,
            nameof(InactiveCaptionColor) => InactiveCaptionColor,
            nameof(InactiveCaptionTextColor) => InactiveCaptionTextColor,
            nameof(InfoColor) => InfoColor,
            nameof(InfoTextColor) => InfoTextColor,
            nameof(MenuColor) => MenuColor,
            nameof(MenuBarColor) => MenuBarColor,
            nameof(MenuHighlightColor) => MenuHighlightColor,
            nameof(MenuTextColor) => MenuTextColor,
            nameof(ScrollBarColor) => ScrollBarColor,
            nameof(WindowColor) => WindowColor,
            nameof(WindowFrameColor) => WindowFrameColor,
            nameof(WindowTextColor) => WindowTextColor,
            nameof(AccentColor) => AccentColor,
            nameof(AccentColorLight1) => AccentColorLight1,
            nameof(AccentColorLight2) => AccentColorLight2,
            nameof(AccentColorLight3) => AccentColorLight3,
            nameof(AccentColorDark1) => AccentColorDark1,
            nameof(AccentColorDark2) => AccentColorDark2,
            nameof(AccentColorDark3) => AccentColorDark3,

            nameof(ActiveBorderBrush) => ActiveBorderBrush,
            nameof(ActiveCaptionBrush) => ActiveCaptionBrush,
            nameof(ActiveCaptionTextBrush) => ActiveCaptionTextBrush,
            nameof(AppWorkspaceBrush) => AppWorkspaceBrush,
            nameof(ControlBrush) => ControlBrush,
            nameof(ControlDarkBrush) => ControlDarkBrush,
            nameof(ControlDarkDarkBrush) => ControlDarkDarkBrush,
            nameof(ControlLightBrush) => ControlLightBrush,
            nameof(ControlLightLightBrush) => ControlLightLightBrush,
            nameof(ControlTextBrush) => ControlTextBrush,
            nameof(DesktopBrush) => DesktopBrush,
            nameof(GradientActiveCaptionBrush) => GradientActiveCaptionBrush,
            nameof(GradientInactiveCaptionBrush) => GradientInactiveCaptionBrush,
            nameof(GrayTextBrush) => GrayTextBrush,
            nameof(HighlightBrush) => HighlightBrush,
            nameof(HighlightTextBrush) => HighlightTextBrush,
            nameof(HotTrackBrush) => HotTrackBrush,
            nameof(InactiveBorderBrush) => InactiveBorderBrush,
            nameof(InactiveCaptionBrush) => InactiveCaptionBrush,
            nameof(InactiveCaptionTextBrush) => InactiveCaptionTextBrush,
            nameof(InfoBrush) => InfoBrush,
            nameof(InfoTextBrush) => InfoTextBrush,
            nameof(MenuBrush) => MenuBrush,
            nameof(MenuBarBrush) => MenuBarBrush,
            nameof(MenuHighlightBrush) => MenuHighlightBrush,
            nameof(MenuTextBrush) => MenuTextBrush,
            nameof(ScrollBarBrush) => ScrollBarBrush,
            nameof(WindowBrush) => WindowBrush,
            nameof(WindowFrameBrush) => WindowFrameBrush,
            nameof(WindowTextBrush) => WindowTextBrush,
            nameof(InactiveSelectionHighlightBrush) => InactiveSelectionHighlightBrush,
            nameof(InactiveSelectionHighlightTextBrush) => InactiveSelectionHighlightTextBrush,
            nameof(AccentColorBrush) => AccentColorBrush,
            nameof(AccentColorLight1Brush) => AccentColorLight1Brush,
            nameof(AccentColorLight2Brush) => AccentColorLight2Brush,
            nameof(AccentColorLight3Brush) => AccentColorLight3Brush,
            nameof(AccentColorDark1Brush) => AccentColorDark1Brush,
            nameof(AccentColorDark2Brush) => AccentColorDark2Brush,
            nameof(AccentColorDark3Brush) => AccentColorDark3Brush,
            _ => null,
        };

        return resource != null;
    }
}
