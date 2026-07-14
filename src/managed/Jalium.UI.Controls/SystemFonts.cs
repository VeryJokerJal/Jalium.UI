using System.Diagnostics;
using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Exposes the system fonts used by window captions, menus, messages, status text,
/// and icon titles.
/// </summary>
public static class SystemFonts
{
    private const uint SpiGetIconTitleLogFont = 0x001F;
    private const uint SpiGetNonClientMetrics = 0x0029;
    private const int LogPixelsY = 90;

    private static Lazy<SystemFontSet> s_fonts = CreateFontCache();
    private static readonly object s_keyLock = new();
    private static readonly Dictionary<string, ResourceKey> s_keyCache = new(StringComparer.Ordinal);

    static SystemFonts()
    {
        if (!OperatingSystem.IsLinux())
            return;

        LinuxDesktopSettings.SettingsChanged += static (_, _) =>
            Interlocked.Exchange(ref s_fonts, CreateFontCache());
        LinuxDesktopSettings.EnsureMonitoring();
    }

    private static Lazy<SystemFontSet> CreateFontCache() => new(
        ReadSystemFonts,
        LazyThreadSafetyMode.ExecutionAndPublication);

    #region System font values

    public static double IconFontSize => s_fonts.Value.Icon.Size;
    public static FontFamily IconFontFamily => s_fonts.Value.Icon.Family;
    public static FontStyle IconFontStyle => s_fonts.Value.Icon.Style;
    public static FontWeight IconFontWeight => s_fonts.Value.Icon.Weight;
    public static TextDecorationCollection IconFontTextDecorations => s_fonts.Value.Icon.TextDecorations;

    public static double CaptionFontSize => s_fonts.Value.Caption.Size;
    public static FontFamily CaptionFontFamily => s_fonts.Value.Caption.Family;
    public static FontStyle CaptionFontStyle => s_fonts.Value.Caption.Style;
    public static FontWeight CaptionFontWeight => s_fonts.Value.Caption.Weight;
    public static TextDecorationCollection CaptionFontTextDecorations => s_fonts.Value.Caption.TextDecorations;

    public static double SmallCaptionFontSize => s_fonts.Value.SmallCaption.Size;
    public static FontFamily SmallCaptionFontFamily => s_fonts.Value.SmallCaption.Family;
    public static FontStyle SmallCaptionFontStyle => s_fonts.Value.SmallCaption.Style;
    public static FontWeight SmallCaptionFontWeight => s_fonts.Value.SmallCaption.Weight;
    public static TextDecorationCollection SmallCaptionFontTextDecorations => s_fonts.Value.SmallCaption.TextDecorations;

    public static double MenuFontSize => s_fonts.Value.Menu.Size;
    public static FontFamily MenuFontFamily => s_fonts.Value.Menu.Family;
    public static FontStyle MenuFontStyle => s_fonts.Value.Menu.Style;
    public static FontWeight MenuFontWeight => s_fonts.Value.Menu.Weight;
    public static TextDecorationCollection MenuFontTextDecorations => s_fonts.Value.Menu.TextDecorations;

    public static double StatusFontSize => s_fonts.Value.Status.Size;
    public static FontFamily StatusFontFamily => s_fonts.Value.Status.Family;
    public static FontStyle StatusFontStyle => s_fonts.Value.Status.Style;
    public static FontWeight StatusFontWeight => s_fonts.Value.Status.Weight;
    public static TextDecorationCollection StatusFontTextDecorations => s_fonts.Value.Status.TextDecorations;

    public static double MessageFontSize => s_fonts.Value.Message.Size;
    public static FontFamily MessageFontFamily => s_fonts.Value.Message.Family;
    public static FontStyle MessageFontStyle => s_fonts.Value.Message.Style;
    public static FontWeight MessageFontWeight => s_fonts.Value.Message.Weight;
    public static TextDecorationCollection MessageFontTextDecorations => s_fonts.Value.Message.TextDecorations;

    #endregion

    #region System font resource keys

    public static ResourceKey IconFontSizeKey => GetResourceKey(nameof(IconFontSize));
    public static ResourceKey IconFontFamilyKey => GetResourceKey(nameof(IconFontFamily));
    public static ResourceKey IconFontStyleKey => GetResourceKey(nameof(IconFontStyle));
    public static ResourceKey IconFontWeightKey => GetResourceKey(nameof(IconFontWeight));
    public static ResourceKey IconFontTextDecorationsKey => GetResourceKey(nameof(IconFontTextDecorations));

    public static ResourceKey CaptionFontSizeKey => GetResourceKey(nameof(CaptionFontSize));
    public static ResourceKey CaptionFontFamilyKey => GetResourceKey(nameof(CaptionFontFamily));
    public static ResourceKey CaptionFontStyleKey => GetResourceKey(nameof(CaptionFontStyle));
    public static ResourceKey CaptionFontWeightKey => GetResourceKey(nameof(CaptionFontWeight));
    public static ResourceKey CaptionFontTextDecorationsKey => GetResourceKey(nameof(CaptionFontTextDecorations));

    public static ResourceKey SmallCaptionFontSizeKey => GetResourceKey(nameof(SmallCaptionFontSize));
    public static ResourceKey SmallCaptionFontFamilyKey => GetResourceKey(nameof(SmallCaptionFontFamily));
    public static ResourceKey SmallCaptionFontStyleKey => GetResourceKey(nameof(SmallCaptionFontStyle));
    public static ResourceKey SmallCaptionFontWeightKey => GetResourceKey(nameof(SmallCaptionFontWeight));
    public static ResourceKey SmallCaptionFontTextDecorationsKey => GetResourceKey(nameof(SmallCaptionFontTextDecorations));

    public static ResourceKey MenuFontSizeKey => GetResourceKey(nameof(MenuFontSize));
    public static ResourceKey MenuFontFamilyKey => GetResourceKey(nameof(MenuFontFamily));
    public static ResourceKey MenuFontStyleKey => GetResourceKey(nameof(MenuFontStyle));
    public static ResourceKey MenuFontWeightKey => GetResourceKey(nameof(MenuFontWeight));
    public static ResourceKey MenuFontTextDecorationsKey => GetResourceKey(nameof(MenuFontTextDecorations));

    public static ResourceKey StatusFontSizeKey => GetResourceKey(nameof(StatusFontSize));
    public static ResourceKey StatusFontFamilyKey => GetResourceKey(nameof(StatusFontFamily));
    public static ResourceKey StatusFontStyleKey => GetResourceKey(nameof(StatusFontStyle));
    public static ResourceKey StatusFontWeightKey => GetResourceKey(nameof(StatusFontWeight));
    public static ResourceKey StatusFontTextDecorationsKey => GetResourceKey(nameof(StatusFontTextDecorations));

    public static ResourceKey MessageFontSizeKey => GetResourceKey(nameof(MessageFontSize));
    public static ResourceKey MessageFontFamilyKey => GetResourceKey(nameof(MessageFontFamily));
    public static ResourceKey MessageFontStyleKey => GetResourceKey(nameof(MessageFontStyle));
    public static ResourceKey MessageFontWeightKey => GetResourceKey(nameof(MessageFontWeight));
    public static ResourceKey MessageFontTextDecorationsKey => GetResourceKey(nameof(MessageFontTextDecorations));

    #endregion

    /// <summary>
    /// Resolves a key exposed by this class to its current system font value.
    /// </summary>
    internal static bool TryGetResource(object resourceKey, out object? resource)
    {
        if (resourceKey is not ComponentResourceKey
            {
                TypeInTargetAssembly: { } ownerType,
                ResourceId: string resourceId,
            }
            || ownerType != typeof(SystemFonts))
        {
            resource = null;
            return false;
        }

        resource = resourceId switch
        {
            nameof(IconFontSize) => IconFontSize,
            nameof(IconFontFamily) => IconFontFamily,
            nameof(IconFontStyle) => IconFontStyle,
            nameof(IconFontWeight) => IconFontWeight,
            nameof(IconFontTextDecorations) => IconFontTextDecorations,
            nameof(CaptionFontSize) => CaptionFontSize,
            nameof(CaptionFontFamily) => CaptionFontFamily,
            nameof(CaptionFontStyle) => CaptionFontStyle,
            nameof(CaptionFontWeight) => CaptionFontWeight,
            nameof(CaptionFontTextDecorations) => CaptionFontTextDecorations,
            nameof(SmallCaptionFontSize) => SmallCaptionFontSize,
            nameof(SmallCaptionFontFamily) => SmallCaptionFontFamily,
            nameof(SmallCaptionFontStyle) => SmallCaptionFontStyle,
            nameof(SmallCaptionFontWeight) => SmallCaptionFontWeight,
            nameof(SmallCaptionFontTextDecorations) => SmallCaptionFontTextDecorations,
            nameof(MenuFontSize) => MenuFontSize,
            nameof(MenuFontFamily) => MenuFontFamily,
            nameof(MenuFontStyle) => MenuFontStyle,
            nameof(MenuFontWeight) => MenuFontWeight,
            nameof(MenuFontTextDecorations) => MenuFontTextDecorations,
            nameof(StatusFontSize) => StatusFontSize,
            nameof(StatusFontFamily) => StatusFontFamily,
            nameof(StatusFontStyle) => StatusFontStyle,
            nameof(StatusFontWeight) => StatusFontWeight,
            nameof(StatusFontTextDecorations) => StatusFontTextDecorations,
            nameof(MessageFontSize) => MessageFontSize,
            nameof(MessageFontFamily) => MessageFontFamily,
            nameof(MessageFontStyle) => MessageFontStyle,
            nameof(MessageFontWeight) => MessageFontWeight,
            nameof(MessageFontTextDecorations) => MessageFontTextDecorations,
            _ => null,
        };

        return resource != null;
    }

    private static ResourceKey GetResourceKey(string resourceId)
    {
        lock (s_keyLock)
        {
            if (!s_keyCache.TryGetValue(resourceId, out var key))
            {
                key = new ComponentResourceKey(typeof(SystemFonts), resourceId);
                s_keyCache.Add(resourceId, key);
            }

            return key;
        }
    }

    private static SystemFontSet ReadSystemFonts()
    {
        var fallback = CreateFallbackFonts();
        if (OperatingSystem.IsLinux())
        {
            LinuxSystemFontSettings settings = LinuxDesktopSettings.SystemFontSettings;
            return new SystemFontSet(
                CreateFont(settings.Caption.Value, fallback.Caption),
                CreateFont(settings.SmallCaption.Value, fallback.SmallCaption),
                CreateFont(settings.Menu.Value, fallback.Menu),
                CreateFont(settings.Status.Value, fallback.Status),
                CreateFont(settings.Message.Value, fallback.Message),
                CreateFont(settings.Icon.Value, fallback.Icon));
        }
        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        var dpi = ReadSystemDpi();
        var caption = fallback.Caption;
        var smallCaption = fallback.SmallCaption;
        var menu = fallback.Menu;
        var status = fallback.Status;
        var message = fallback.Message;
        var icon = fallback.Icon;

        try
        {
            var metrics = new NonClientMetrics
            {
                Size = (uint)Marshal.SizeOf<NonClientMetrics>(),
            };

            if (SystemParametersInfoNonClientMetrics(
                SpiGetNonClientMetrics,
                metrics.Size,
                ref metrics,
                0))
            {
                caption = CreateFont(metrics.CaptionFont, dpi, caption);
                smallCaption = CreateFont(metrics.SmallCaptionFont, dpi, smallCaption);
                menu = CreateFont(metrics.MenuFont, dpi, menu);
                status = CreateFont(metrics.StatusFont, dpi, status);
                message = CreateFont(metrics.MessageFont, dpi, message);
            }
        }
        catch (Exception) when (!Debugger.IsAttached)
        {
            // Keep the deterministic platform fallback if the host cannot expose
            // desktop non-client metrics (for example, a restricted Windows sandbox).
        }

        try
        {
            var iconFont = new LogFont();
            if (SystemParametersInfoIconTitleFont(
                SpiGetIconTitleLogFont,
                (uint)Marshal.SizeOf<LogFont>(),
                ref iconFont,
                0))
            {
                icon = CreateFont(iconFont, dpi, icon);
            }
        }
        catch (Exception) when (!Debugger.IsAttached)
        {
            // Keep the deterministic fallback when icon-title metrics are unavailable.
        }

        return new SystemFontSet(caption, smallCaption, menu, status, message, icon);
    }

    private static SystemFontSet CreateFallbackFonts()
    {
        var familyName = FrameworkElement.DefaultFontFamilyName;
        return new SystemFontSet(
            CreateFallbackFont(familyName, 14.0),
            CreateFallbackFont(familyName, 11.0),
            CreateFallbackFont(familyName, 14.0),
            CreateFallbackFont(familyName, 14.0),
            CreateFallbackFont(familyName, 14.0),
            CreateFallbackFont(familyName, 9.0));
    }

    private static SystemFontInfo CreateFallbackFont(string familyName, double size) =>
        new(
            new FontFamily(familyName),
            size,
            FontStyles.Normal,
            FontWeights.Normal,
            CreateFrozenTextDecorations());

    private static SystemFontInfo CreateFont(
        LinuxSystemFontDescription description,
        SystemFontInfo fallback)
    {
        if (string.IsNullOrWhiteSpace(description.Family) ||
            !double.IsFinite(description.Size) || description.Size <= 0 ||
            description.Weight is < 1 or > 999)
        {
            return fallback;
        }

        return new SystemFontInfo(
            new FontFamily(description.Family),
            description.Size,
            description.Italic ? FontStyles.Italic : FontStyles.Normal,
            FontWeight.FromOpenTypeWeight(description.Weight),
            CreateFrozenTextDecorations());
    }

    private static SystemFontInfo CreateFont(LogFont logFont, uint dpi, SystemFontInfo fallback)
    {
        var faceName = logFont.FaceName?.TrimEnd('\0').Trim();
        var family = string.IsNullOrWhiteSpace(faceName)
            ? fallback.Family
            : new FontFamily(faceName);

        var size = fallback.Size;
        if (logFont.Height != 0 && dpi != 0)
        {
            var convertedSize = Math.Abs((long)logFont.Height) * 96L / dpi;
            if (convertedSize > 0)
            {
                size = convertedSize;
            }
        }

        var weight = logFont.Weight is >= 1 and <= 999
            ? FontWeight.FromOpenTypeWeight(logFont.Weight)
            : fallback.Weight;
        var decorations = new TextDecorationCollection();

        if (logFont.Underline != 0)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        if (logFont.StrikeOut != 0)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        decorations.Freeze();

        return new SystemFontInfo(
            family,
            size,
            logFont.Italic != 0 ? FontStyles.Italic : FontStyles.Normal,
            weight,
            decorations);
    }

    private static TextDecorationCollection CreateFrozenTextDecorations()
    {
        var decorations = new TextDecorationCollection();
        decorations.Freeze();
        return decorations;
    }

    private static uint ReadSystemDpi()
    {
        try
        {
            var dpi = GetDpiForSystem();
            if (dpi != 0)
            {
                return dpi;
            }
        }
        catch (Exception) when (!Debugger.IsAttached)
        {
        }

        nint deviceContext = 0;
        try
        {
            deviceContext = GetDC(0);
            if (deviceContext != 0)
            {
                var dpi = GetDeviceCaps(deviceContext, LogPixelsY);
                if (dpi > 0)
                {
                    return (uint)dpi;
                }
            }
        }
        catch (Exception) when (!Debugger.IsAttached)
        {
        }
        finally
        {
            if (deviceContext != 0)
            {
                _ = ReleaseDC(0, deviceContext);
            }
        }

        return 96;
    }

    private sealed record SystemFontInfo(
        FontFamily Family,
        double Size,
        FontStyle Style,
        FontWeight Weight,
        TextDecorationCollection TextDecorations);

    private sealed record SystemFontSet(
        SystemFontInfo Caption,
        SystemFontInfo SmallCaption,
        SystemFontInfo Menu,
        SystemFontInfo Status,
        SystemFontInfo Message,
        SystemFontInfo Icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharacterSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NonClientMetrics
    {
        public uint Size;
        public int BorderWidth;
        public int ScrollWidth;
        public int ScrollHeight;
        public int CaptionWidth;
        public int CaptionHeight;
        public LogFont CaptionFont;
        public int SmallCaptionWidth;
        public int SmallCaptionHeight;
        public LogFont SmallCaptionFont;
        public int MenuWidth;
        public int MenuHeight;
        public LogFont MenuFont;
        public LogFont StatusFont;
        public LogFont MessageFont;
        public int PaddedBorderWidth;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoNonClientMetrics(
        uint action,
        uint parameter,
        ref NonClientMetrics metrics,
        uint update);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoIconTitleFont(
        uint action,
        uint parameter,
        ref LogFont font,
        uint update);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint deviceContext, int index);
}
