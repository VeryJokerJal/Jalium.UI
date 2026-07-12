using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// WPF-compatible system metrics and settings backed by Win32 when available.
/// </summary>
public static partial class SystemParameters
{
    private static readonly object s_themeLock = new();
    private static readonly Lazy<ThemeInfo> s_themeInfo = new(ReadThemeInfo);
    private static readonly Lazy<Color> s_windowGlassColor = new(ReadWindowGlassColor);
    private static readonly Lazy<Brush> s_windowGlassBrush = new(
        static () => new SolidColorBrush(WindowGlassColor));

    /// <summary>
    /// Occurs when a cached system parameter is invalidated by the platform host.
    /// </summary>
    public static event PropertyChangedEventHandler? StaticPropertyChanged;

    /// <summary>
    /// Raises <see cref="StaticPropertyChanged"/> for platform message bridges and tests.
    /// A null or empty name means that every parameter may have changed.
    /// </summary>
    internal static void NotifyStaticPropertyChanged(string? propertyName)
        => StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));

    // Additional scalar settings and metrics present on WPF's SystemParameters surface.
    public static int Border => (int)Math.Clamp(GetSpiUInt(SPI_GETBORDER, 1), 1u, int.MaxValue);
    public static bool ComboBoxAnimation => GetSpiBool(SPI_GETCOMBOBOXANIMATION, true);
    public static PopupAnimation ComboBoxPopupAnimation
        => ComboBoxAnimation ? PopupAnimation.Slide : PopupAnimation.None;
    public static bool CursorShadow => GetSpiBool(SPI_GETCURSORSHADOW, true);
    public static bool DragFullWindows => GetSpiBool(SPI_GETDRAGFULLWINDOWS, true);
    public static double FixedFrameHorizontalBorderHeight => MetricDip(SM_CYFIXEDFRAME, 1);
    public static double FixedFrameVerticalBorderWidth => MetricDip(SM_CXFIXEDFRAME, 1);
    public static double FocusBorderHeight => MetricDip(SM_CYFOCUSBORDER, 1);
    public static double FocusBorderWidth => MetricDip(SM_CXFOCUSBORDER, 1);
    public static double FocusHorizontalBorderHeight => FocusBorderHeight;
    public static double FocusVerticalBorderWidth => FocusBorderWidth;
    public static double FullPrimaryScreenHeight => MetricDip(SM_CYFULLSCREEN, 1040);
    public static double FullPrimaryScreenWidth => MetricDip(SM_CXFULLSCREEN, 1920);
    public static bool HotTracking => GetSpiBool(SPI_GETHOTTRACKING, true);
    public static double IconGridHeight => MetricDip(SM_CYICONSPACING, 75);
    public static double IconGridWidth => MetricDip(SM_CXICONSPACING, 75);
    public static double IconHorizontalSpacing => IconGridWidth;
    public static bool IconTitleWrap => GetSpiBool(SPI_GETICONTITLEWRAP, true);
    public static double IconVerticalSpacing => IconGridHeight;
    public static bool IsImmEnabled => GetMetricBool(SM_IMMENABLED, false);
    public static bool IsMediaCenter => GetMetricBool(SM_MEDIACENTER, false);
    public static bool IsMenuDropRightAligned => MenuDropAlignment;
    public static bool IsMiddleEastEnabled => GetMetricBool(SM_MIDEASTENABLED, false);
    public static bool IsMousePresent => GetMetricBool(SM_MOUSEPRESENT, !OperatingSystem.IsBrowser());
    public static bool IsMouseWheelPresent => GetMetricBool(SM_MOUSEWHEELPRESENT, !OperatingSystem.IsBrowser());
    public static bool IsPenWindows => GetMetricBool(SM_PENWINDOWS, false);
    public static bool IsRemoteSession => GetMetricBool(SM_REMOTESESSION, false);
    public static bool IsRemotelyControlled => GetMetricBool(SM_REMOTECONTROL, false);
    public static bool IsSlowMachine => GetMetricBool(SM_SLOWMACHINE, false);
    public static double KanjiWindowHeight => MetricDip(SM_CYKANJIWINDOW, 18);
    public static bool KeyboardCues => GetSpiBool(SPI_GETKEYBOARDCUES, false);
    public static int KeyboardDelay => (int)Math.Min(3, GetSpiUInt(SPI_GETKEYBOARDDELAY, 1));
    public static bool KeyboardPreference => GetSpiBool(SPI_GETKEYBOARDPREF, false);
    public static int KeyboardSpeed => (int)Math.Min(31, GetSpiUInt(SPI_GETKEYBOARDSPEED, 31));
    public static bool ListBoxSmoothScrolling => GetSpiBool(SPI_GETLISTBOXSMOOTHSCROLLING, true);
    public static double MaximizedPrimaryScreenHeight => MetricDip(SM_CYMAXIMIZED, 1040);
    public static double MaximizedPrimaryScreenWidth => MetricDip(SM_CXMAXIMIZED, 1920);
    public static double MaximumWindowTrackHeight => MetricDip(SM_CYMAXTRACK, 1080);
    public static double MaximumWindowTrackWidth => MetricDip(SM_CXMAXTRACK, 1920);
    public static double MenuButtonHeight => MetricDip(SM_CYMENUSIZE, 19);
    public static double MenuButtonWidth => MetricDip(SM_CXMENUSIZE, 19);
    public static double MenuCheckmarkHeight => MetricDip(SM_CYMENUCHECK, 13);
    public static double MenuCheckmarkWidth => MetricDip(SM_CXMENUCHECK, 13);
    public static bool MenuFade => GetSpiBool(SPI_GETMENUFADE, true);
    public static double MenuHeight => MetricDip(SM_CYMENU, 20);
    public static PopupAnimation MenuPopupAnimation
        => !MenuAnimation ? PopupAnimation.None : MenuFade ? PopupAnimation.Fade : PopupAnimation.Scroll;
    public static int MenuShowDelay => (int)Math.Min(int.MaxValue, GetSpiUInt(SPI_GETMENUSHOWDELAY, 400));
    public static double MenuWidth => MetricDip(SM_CXMENUSIZE, 19);
    public static bool MinimizeAnimation => GetMinimizeAnimation();
    public static double MinimizedGridHeight => MetricDip(SM_CYMINSPACING, 160);
    public static double MinimizedGridWidth => MetricDip(SM_CXMINSPACING, 160);
    public static double MinimizedWindowHeight => MetricDip(SM_CYMINIMIZED, 22);
    public static double MinimizedWindowWidth => MetricDip(SM_CXMINIMIZED, 160);
    public static double MinimumWindowHeight => MetricDip(SM_CYMIN, 39);
    public static double MinimumWindowTrackHeight => MetricDip(SM_CYMINTRACK, 39);
    public static double MinimumWindowTrackWidth => MetricDip(SM_CXMINTRACK, 112);
    public static double MinimumWindowWidth => MetricDip(SM_CXMIN, 112);
    public static double ResizeFrameHorizontalBorderHeight => MetricDip(SM_CYFRAME, 4);
    public static double ResizeFrameVerticalBorderWidth => MetricDip(SM_CXFRAME, 4);
    public static bool ShowSounds => GetMetricBool(SM_SHOWSOUNDS, false);
    public static double SmallCaptionHeight => MetricDip(SM_CYSMCAPTION, 17);
    public static double SmallCaptionWidth => MetricDip(SM_CXSMSIZE, 17);
    public static double SmallWindowCaptionButtonHeight => MetricDip(SM_CYSMSIZE, 17);
    public static double SmallWindowCaptionButtonWidth => MetricDip(SM_CXSMSIZE, 17);
    public static bool SnapToDefaultButton => GetSpiBool(SPI_GETSNAPTODEFBUTTON, false);
    public static bool SwapButtons => GetMetricBool(SM_SWAPBUTTON, false);
    public static double ThickHorizontalBorderHeight => MetricDip(SM_CYFRAME, 4);
    public static double ThickVerticalBorderWidth => MetricDip(SM_CXFRAME, 4);
    public static double ThinHorizontalBorderHeight => MetricDip(SM_CYBORDER, 1);
    public static double ThinVerticalBorderWidth => MetricDip(SM_CXBORDER, 1);
    public static bool ToolTipFade => GetSpiBool(SPI_GETTOOLTIPFADE, true);
    public static PopupAnimation ToolTipPopupAnimation
        => ToolTipAnimation && ToolTipFade ? PopupAnimation.Fade : PopupAnimation.None;
    public static string UxThemeColor => s_themeInfo.Value.Color;
    public static string UxThemeName => s_themeInfo.Value.Name;
    public static double WindowCaptionButtonHeight => MetricDip(SM_CYSIZE, 22);
    public static double WindowCaptionButtonWidth => MetricDip(SM_CXSIZE, 22);
    public static double WindowCaptionHeight => CaptionHeight;
    public static CornerRadius WindowCornerRadius => GetWindowCornerRadius();
    public static Brush WindowGlassBrush => s_windowGlassBrush.Value;
    public static Color WindowGlassColor => s_windowGlassColor.Value;

    private static double MetricDip(int metric, int fallback)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            var value = GetSystemMetrics(metric);
            return value > 0 ? PixelDip((uint)value) : fallback;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return fallback;
        }
    }

    private static double MetricDipAllowZero(int metric, int fallback)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            return GetSystemMetrics(metric) * GetDpiScale();
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return fallback;
        }
    }

    private static double PixelDip(uint pixels) => pixels * GetDpiScale();

    private static double GetDpiScale()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 1.0;
        }

        try
        {
            var dpi = GetDpiForSystem();
            return dpi > 0 ? 96.0 / dpi : 1.0;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return 1.0;
        }
    }

    private static bool GetMetricBool(int metric, bool fallback)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            return GetSystemMetrics(metric) != 0;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return fallback;
        }
    }

    private static uint GetSpiUInt(uint action, uint fallback)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            return SystemParametersInfoUInt(action, 0, out var value, 0) ? value : fallback;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return fallback;
        }
    }

    private static bool GetSpiBool(uint action, bool fallback)
        => GetSpiUInt(action, fallback ? 1u : 0u) != 0;

    private static int GetDoubleClickTimeOrDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 500;
        }

        try
        {
            var value = GetDoubleClickTimeNative();
            return value is > 0 and <= int.MaxValue ? (int)value : 500;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return 500;
        }
    }

    private static int GetCaretBlinkTimeOrDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 530;
        }

        try
        {
            var value = GetCaretBlinkTimeNative();
            return value is > 0 and < int.MaxValue ? (int)value : 530;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return 530;
        }
    }

    private static Rect GetWorkArea()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (SystemParametersInfoRect(SPI_GETWORKAREA, 0, out var area, 0))
                {
                    var scale = GetDpiScale();
                    return new Rect(
                        area.Left * scale,
                        area.Top * scale,
                        Math.Max(0, area.Right - area.Left) * scale,
                        Math.Max(0, area.Bottom - area.Top) * scale);
                }
            }
            catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
            {
            }
        }

        return new Rect(0, 0, PrimaryScreenWidth, Math.Max(0, PrimaryScreenHeight - 40));
    }

    private static bool GetHighContrast()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var info = new HighContrastInfo { Size = (uint)Marshal.SizeOf<HighContrastInfo>() };
            return SystemParametersInfoHighContrast(SPI_GETHIGHCONTRAST, info.Size, ref info, 0)
                && (info.Flags & HCF_HIGHCONTRASTON) != 0;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return false;
        }
    }

    private static bool GetMinimizeAnimation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
            return SystemParametersInfoAnimation(SPI_GETANIMATION, info.Size, ref info, 0)
                ? info.MinAnimate != 0
                : true;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return true;
        }
    }

    private static PowerLineStatus GetPowerLineStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return global::Jalium.UI.PowerLineStatus.Unknown;
        }

        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                return status.ACLineStatus switch
                {
                    0 => global::Jalium.UI.PowerLineStatus.Offline,
                    1 => global::Jalium.UI.PowerLineStatus.Online,
                    _ => global::Jalium.UI.PowerLineStatus.Unknown,
                };
            }
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
        }

        return global::Jalium.UI.PowerLineStatus.Unknown;
    }

    private static ThemeInfo ReadThemeInfo()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ThemeInfo("Default", string.Empty);
        }

        lock (s_themeLock)
        {
            try
            {
                if (!IsThemeActive())
                {
                    return new ThemeInfo("Classic", string.Empty);
                }

                var file = new StringBuilder(260);
                var color = new StringBuilder(260);
                var size = new StringBuilder(260);
                if (GetCurrentThemeName(file, file.Capacity, color, color.Capacity, size, size.Capacity) == 0)
                {
                    var name = Path.GetFileNameWithoutExtension(file.ToString());
                    return new ThemeInfo(string.IsNullOrWhiteSpace(name) ? "Classic" : name, color.ToString());
                }
            }
            catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
            {
            }

            return new ThemeInfo("Classic", string.Empty);
        }
    }

    private static bool GetIsGlassEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return false;
        }
    }

    private static CornerRadius GetWindowCornerRadius()
    {
        if (UxThemeName.Equals("Luna", StringComparison.OrdinalIgnoreCase))
        {
            return new CornerRadius(6, 6, 0, 0);
        }

        if (UxThemeName.Equals("Aero", StringComparison.OrdinalIgnoreCase))
        {
            return IsGlassEnabled ? new CornerRadius(8) : new CornerRadius(6, 6, 0, 0);
        }

        return new CornerRadius(0);
    }

    private static Color ReadWindowGlassColor()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (DwmGetColorizationColor(out var argb, out var opaque) == 0)
                {
                    var alpha = opaque ? byte.MaxValue : (byte)(argb >> 24);
                    return Color.FromArgb(
                        alpha,
                        (byte)(argb >> 16),
                        (byte)(argb >> 8),
                        (byte)argb);
                }
            }
            catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
            {
            }
        }

        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    private readonly record struct ThemeInfo(string Name, string Color);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private const uint SPI_GETBORDER = 0x0005;
    private const uint SPI_GETKEYBOARDSPEED = 0x000A;
    private const uint SPI_GETKEYBOARDDELAY = 0x0016;
    private const uint SPI_GETICONTITLEWRAP = 0x0019;
    private const uint SPI_GETDRAGFULLWINDOWS = 0x0026;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SPI_GETSNAPTODEFBUTTON = 0x005F;
    private const uint SPI_GETMOUSEHOVERWIDTH = 0x0062;
    private const uint SPI_GETMOUSEHOVERHEIGHT = 0x0064;
    private const uint SPI_GETMOUSEHOVERTIME = 0x0066;
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068;
    private const uint SPI_GETMENUSHOWDELAY = 0x006A;
    private const uint SPI_GETKEYBOARDPREF = 0x0044;
    private const uint SPI_GETANIMATION = 0x0048;
    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint SPI_GETMENUANIMATION = 0x1002;
    private const uint SPI_GETCOMBOBOXANIMATION = 0x1004;
    private const uint SPI_GETLISTBOXSMOOTHSCROLLING = 0x1006;
    private const uint SPI_GETGRADIENTCAPTIONS = 0x1008;
    private const uint SPI_GETKEYBOARDCUES = 0x100A;
    private const uint SPI_GETHOTTRACKING = 0x100E;
    private const uint SPI_GETMENUFADE = 0x1012;
    private const uint SPI_GETSELECTIONFADE = 0x1014;
    private const uint SPI_GETTOOLTIPANIMATION = 0x1016;
    private const uint SPI_GETTOOLTIPFADE = 0x1018;
    private const uint SPI_GETCURSORSHADOW = 0x101A;
    private const uint SPI_GETFLATMENU = 0x1022;
    private const uint SPI_GETDROPSHADOW = 0x1024;
    private const uint SPI_GETUIEFFECTS = 0x103E;
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    private const uint SPI_GETFOREGROUNDFLASHCOUNT = 0x2004;
    private const uint SPI_GETCARETWIDTH = 0x2006;
    private const uint HCF_HIGHCONTRASTON = 0x00000001;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_CXVSCROLL = 2;
    private const int SM_CYHSCROLL = 3;
    private const int SM_CYCAPTION = 4;
    private const int SM_CXBORDER = 5;
    private const int SM_CYBORDER = 6;
    private const int SM_CXFIXEDFRAME = 7;
    private const int SM_CYFIXEDFRAME = 8;
    private const int SM_CYVTHUMB = 9;
    private const int SM_CXHTHUMB = 10;
    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;
    private const int SM_CXCURSOR = 13;
    private const int SM_CYCURSOR = 14;
    private const int SM_CYMENU = 15;
    private const int SM_CXFULLSCREEN = 16;
    private const int SM_CYFULLSCREEN = 17;
    private const int SM_CYKANJIWINDOW = 18;
    private const int SM_MOUSEPRESENT = 19;
    private const int SM_CYVSCROLL = 20;
    private const int SM_CXHSCROLL = 21;
    private const int SM_SWAPBUTTON = 23;
    private const int SM_CXMIN = 28;
    private const int SM_CYMIN = 29;
    private const int SM_CXSIZE = 30;
    private const int SM_CYSIZE = 31;
    private const int SM_CXFRAME = 32;
    private const int SM_CYFRAME = 33;
    private const int SM_CXMINTRACK = 34;
    private const int SM_CYMINTRACK = 35;
    private const int SM_CXICONSPACING = 38;
    private const int SM_CYICONSPACING = 39;
    private const int SM_MENUDROPALIGNMENT = 40;
    private const int SM_PENWINDOWS = 41;
    private const int SM_CXMINSPACING = 47;
    private const int SM_CYMINSPACING = 48;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const int SM_CYSMCAPTION = 51;
    private const int SM_CXSMSIZE = 52;
    private const int SM_CYSMSIZE = 53;
    private const int SM_CXMENUSIZE = 54;
    private const int SM_CYMENUSIZE = 55;
    private const int SM_CXMINIMIZED = 57;
    private const int SM_CYMINIMIZED = 58;
    private const int SM_CXMAXTRACK = 59;
    private const int SM_CYMAXTRACK = 60;
    private const int SM_CXMAXIMIZED = 61;
    private const int SM_CYMAXIMIZED = 62;
    private const int SM_CXDRAG = 68;
    private const int SM_CYDRAG = 69;
    private const int SM_SHOWSOUNDS = 70;
    private const int SM_CXMENUCHECK = 71;
    private const int SM_CYMENUCHECK = 72;
    private const int SM_SLOWMACHINE = 73;
    private const int SM_MIDEASTENABLED = 74;
    private const int SM_MOUSEWHEELPRESENT = 75;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_IMMENABLED = 82;
    private const int SM_CXFOCUSBORDER = 83;
    private const int SM_CYFOCUSBORDER = 84;
    private const int SM_TABLETPC = 86;
    private const int SM_MEDIACENTER = 87;
    private const int SM_CXPADDEDBORDER = 92;
    private const int SM_REMOTESESSION = 0x1000;
    private const int SM_REMOTECONTROL = 0x2001;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetDoubleClickTime")]
    private static partial uint GetDoubleClickTimeNative();

    [LibraryImport("user32.dll", EntryPoint = "GetCaretBlinkTime")]
    private static partial uint GetCaretBlinkTimeNative();

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoUInt(uint action, uint parameter, out uint value, uint update);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoRect(uint action, uint parameter, out NativeRect value, uint update);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoHighContrast(uint action, uint parameter, ref HighContrastInfo value, uint update);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoAnimation(uint action, uint parameter, ref AnimationInfo value, uint update);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out NativeSystemPowerStatus status);

    [LibraryImport("uxtheme.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsThemeActive();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentThemeName(
        StringBuilder themeFileName,
        int themeFileNameLength,
        StringBuilder colorBuff,
        int colorBuffLength,
        StringBuilder sizeBuff,
        int sizeBuffLength);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
