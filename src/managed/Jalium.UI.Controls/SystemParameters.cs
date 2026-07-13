using System.Runtime.Versioning;
using System.ComponentModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Microsoft.Win32;

namespace Jalium.UI;

/// <summary>
/// Describes the current runtime environment.
/// </summary>
[Flags]
public enum SystemEnvironmentKind
{
    Unknown = 0,
    Windows = 1 << 0,
    MacOS = 1 << 1,
    IOS = 1 << 2,
    Android = 1 << 3,
    Linux = 1 << 4,
    Browser = 1 << 5,
    FreeBSD = 1 << 6,
    MacCatalyst = 1 << 7,
    TvOS = 1 << 8,
    Wasi = 1 << 9,
    WatchOS = 1 << 10,
    VirtualMachine = 1 << 11
}

/// <summary>
/// Contains properties that you can use to query system settings.
/// </summary>
public static partial class SystemParameters
{
    private const string BiosRegistryPath = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string HyperVGuestRegistryPath = @"SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters";
    private static readonly Lazy<SystemEnvironmentKind> s_currentEnvironment = new(DetectCurrentEnvironment);
    private static readonly string[] s_virtualMachineSignatures =
    [
        "virtual",
        "vmware",
        "virtualbox",
        "hyper-v",
        "hyperv",
        "kvm",
        "qemu",
        "xen",
        "parallels",
        "bhyve",
        "bochs"
    ];

    /// <summary>
    /// Gets the current runtime environment flags.
    /// </summary>
    public static SystemEnvironmentKind CurrentEnvironment => s_currentEnvironment.Value;

    /// <summary>
    /// Gets a value indicating whether the current environment is Windows.
    /// </summary>
    public static bool IsWindows => HasEnvironment(SystemEnvironmentKind.Windows);

    /// <summary>
    /// Gets a value indicating whether the current environment is macOS.
    /// </summary>
    public static bool IsMacOS => HasEnvironment(SystemEnvironmentKind.MacOS);

    /// <summary>
    /// Gets a value indicating whether the current environment is iOS.
    /// </summary>
    public static bool IsIOS => HasEnvironment(SystemEnvironmentKind.IOS);

    /// <summary>
    /// Gets a value indicating whether the current environment is Android.
    /// </summary>
    public static bool IsAndroid => HasEnvironment(SystemEnvironmentKind.Android);

    /// <summary>
    /// Gets a value indicating whether the current environment is Linux.
    /// </summary>
    public static bool IsLinux => HasEnvironment(SystemEnvironmentKind.Linux);

    /// <summary>
    /// Gets a value indicating whether the current environment is Browser.
    /// </summary>
    public static bool IsBrowser => HasEnvironment(SystemEnvironmentKind.Browser);

    /// <summary>
    /// Gets a value indicating whether the current environment is FreeBSD.
    /// </summary>
    public static bool IsFreeBSD => HasEnvironment(SystemEnvironmentKind.FreeBSD);

    /// <summary>
    /// Gets a value indicating whether the current environment is Mac Catalyst.
    /// </summary>
    public static bool IsMacCatalyst => HasEnvironment(SystemEnvironmentKind.MacCatalyst);

    /// <summary>
    /// Gets a value indicating whether the current environment is tvOS.
    /// </summary>
    public static bool IsTvOS => HasEnvironment(SystemEnvironmentKind.TvOS);

    /// <summary>
    /// Gets a value indicating whether the current environment is WASI.
    /// </summary>
    public static bool IsWasi => HasEnvironment(SystemEnvironmentKind.Wasi);

    /// <summary>
    /// Gets a value indicating whether the current environment is watchOS.
    /// </summary>
    public static bool IsWatchOS => HasEnvironment(SystemEnvironmentKind.WatchOS);

    /// <summary>
    /// Gets a value indicating whether the current process is running in a virtual machine.
    /// </summary>
    public static bool IsVirtualMachine => HasEnvironment(SystemEnvironmentKind.VirtualMachine);

    // Window metrics. Win32 pixel measurements are normalized to device-independent pixels.
    public static double BorderWidth => MetricDip(SM_CXBORDER, 1);
    public static double CaptionHeight => MetricDip(SM_CYCAPTION, 22);
    public static double CaptionWidth => MetricDip(SM_CXSIZE, 22);
    public static Thickness WindowResizeBorderThickness
    {
        get
        {
            var horizontal = MetricDip(SM_CXFRAME, 4) + MetricDip(SM_CXPADDEDBORDER, 0);
            var vertical = MetricDip(SM_CYFRAME, 4) + MetricDip(SM_CXPADDEDBORDER, 0);
            return new Thickness(horizontal, vertical, horizontal, vertical);
        }
    }

    public static Thickness WindowNonClientFrameThickness
    {
        get
        {
            var resize = WindowResizeBorderThickness;
            return new Thickness(resize.Left, resize.Top + CaptionHeight, resize.Right, resize.Bottom);
        }
    }

    // UI element sizes.
    public static double SmallIconWidth => MetricDip(SM_CXSMICON, 16);
    public static double SmallIconHeight => MetricDip(SM_CYSMICON, 16);
    public static double IconWidth => MetricDip(SM_CXICON, 32);
    public static double IconHeight => MetricDip(SM_CYICON, 32);
    public static double MenuBarHeight => MetricDip(SM_CYMENU, 20);
    public static double ScrollWidth => MetricDip(SM_CXVSCROLL, 17);
    public static double ScrollHeight => MetricDip(SM_CYHSCROLL, 17);
    public static double HorizontalScrollBarButtonWidth => MetricDip(SM_CXHSCROLL, 17);
    public static double VerticalScrollBarButtonHeight => MetricDip(SM_CYVSCROLL, 17);
    public static double HorizontalScrollBarHeight => MetricDip(SM_CYHSCROLL, 17);
    public static double VerticalScrollBarWidth => MetricDip(SM_CXVSCROLL, 17);
    public static double HorizontalScrollBarThumbWidth => MetricDip(SM_CXHTHUMB, 8);
    public static double VerticalScrollBarThumbHeight => MetricDip(SM_CYVTHUMB, 8);

    public static double CursorWidth => MetricDip(SM_CXCURSOR, 32);
    public static double CursorHeight => MetricDip(SM_CYCURSOR, 32);

    // Input settings.
    public static int DoubleClickTime => GetDoubleClickTimeOrDefault();
    public static TimeSpan MouseHoverTime => TimeSpan.FromMilliseconds(GetSpiUInt(SPI_GETMOUSEHOVERTIME, 400));
    public static double MouseHoverWidth => PixelDip(GetSpiUInt(SPI_GETMOUSEHOVERWIDTH, 4));
    public static double MouseHoverHeight => PixelDip(GetSpiUInt(SPI_GETMOUSEHOVERHEIGHT, 4));
    public static double MinimumHorizontalDragDistance => Math.Max(1, MetricDip(SM_CXDRAG, 4));
    public static double MinimumVerticalDragDistance => Math.Max(1, MetricDip(SM_CYDRAG, 4));

    // Screen information. Non-Windows platforms answer through the
    // jalium.native.platform monitor ABI (XRandR / wl_output).
    public static double PrimaryScreenWidth => GetPrimaryScreenDimensionDip(width: true, 1920);
    public static double PrimaryScreenHeight => GetPrimaryScreenDimensionDip(width: false, 1080);
    public static double VirtualScreenWidth => OperatingSystem.IsWindows()
        ? MetricDip(SM_CXVIRTUALSCREEN, 1920)
        : GetPrimaryScreenDimensionDip(width: true, 1920);
    public static double VirtualScreenHeight => OperatingSystem.IsWindows()
        ? MetricDip(SM_CYVIRTUALSCREEN, 1080)
        : GetPrimaryScreenDimensionDip(width: false, 1080);
    public static double VirtualScreenLeft => MetricDipAllowZero(SM_XVIRTUALSCREEN, 0);
    public static double VirtualScreenTop => MetricDipAllowZero(SM_YVIRTUALSCREEN, 0);
    public static Rect WorkArea => GetWorkArea();
    public static bool IsTabletPC => GetMetricBool(SM_TABLETPC, false);

    // Visual and accessibility effects.
    public static bool ClientAreaAnimation => GetSpiBool(SPI_GETCLIENTAREAANIMATION, true);
    public static bool DropShadow => GetSpiBool(SPI_GETDROPSHADOW, true);
    public static bool FlatMenu => GetSpiBool(SPI_GETFLATMENU, true);
    public static int ForegroundFlashCount => (int)GetSpiUInt(SPI_GETFOREGROUNDFLASHCOUNT, 3);
    public static bool GradientCaptions => GetSpiBool(SPI_GETGRADIENTCAPTIONS, true);
    public static bool HighContrast => GetHighContrast();
    public static bool MenuAnimation => GetSpiBool(SPI_GETMENUANIMATION, true);
    public static bool MenuDropAlignment => GetMetricBool(SM_MENUDROPALIGNMENT, false);
    public static bool SelectionFade => GetSpiBool(SPI_GETSELECTIONFADE, true);
    public static bool StylusHotTracking => HotTracking;
    public static bool ToolTipAnimation => GetSpiBool(SPI_GETTOOLTIPANIMATION, true);
    public static bool UIEffects => GetSpiBool(SPI_GETUIEFFECTS, true);

    public static double CaretWidth => PixelDip(GetSpiUInt(SPI_GETCARETWIDTH, 1));
    public static bool IsGlassEnabled => GetIsGlassEnabled();
    public static int CaretBlinkTime => GetCaretBlinkTimeOrDefault();
    public static int WheelScrollLines => (int)Math.Min(int.MaxValue, GetSpiUInt(SPI_GETWHEELSCROLLLINES, 3));
    public static PowerLineStatus PowerLineStatus => GetPowerLineStatus();

    private static bool HasEnvironment(SystemEnvironmentKind environment)
    {
        return (CurrentEnvironment & environment) == environment;
    }

    private static SystemEnvironmentKind DetectCurrentEnvironment()
    {
        var environment = SystemEnvironmentKind.Unknown;

        if (OperatingSystem.IsBrowser())
        {
            environment |= SystemEnvironmentKind.Browser;
        }

        if (OperatingSystem.IsWindows())
        {
            environment |= SystemEnvironmentKind.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            environment |= SystemEnvironmentKind.MacOS;
        }

        if (OperatingSystem.IsIOS())
        {
            environment |= SystemEnvironmentKind.IOS;
        }

        if (OperatingSystem.IsAndroid())
        {
            environment |= SystemEnvironmentKind.Android;
        }

        if (OperatingSystem.IsLinux())
        {
            environment |= SystemEnvironmentKind.Linux;
        }

        if (OperatingSystem.IsFreeBSD())
        {
            environment |= SystemEnvironmentKind.FreeBSD;
        }

        if (OperatingSystem.IsMacCatalyst())
        {
            environment |= SystemEnvironmentKind.MacCatalyst;
        }

        if (OperatingSystem.IsTvOS())
        {
            environment |= SystemEnvironmentKind.TvOS;
        }

        if (OperatingSystem.IsWasi())
        {
            environment |= SystemEnvironmentKind.Wasi;
        }

        if (OperatingSystem.IsWatchOS())
        {
            environment |= SystemEnvironmentKind.WatchOS;
        }

        if (DetectIsVirtualMachine())
        {
            environment |= SystemEnvironmentKind.VirtualMachine;
        }

        return environment;
    }

    private static bool DetectIsVirtualMachine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (RegistrySubKeyExists(HyperVGuestRegistryPath))
        {
            return true;
        }

        string?[] registryValues =
        [
            ReadLocalMachineString(BiosRegistryPath, "SystemManufacturer"),
            ReadLocalMachineString(BiosRegistryPath, "SystemProductName"),
            ReadLocalMachineString(BiosRegistryPath, "BIOSVendor"),
            ReadLocalMachineString(BiosRegistryPath, "BaseBoardManufacturer"),
            ReadLocalMachineString(BiosRegistryPath, "BaseBoardProduct")
        ];

        return registryValues.Any(ContainsVirtualMachineSignature);
    }

    internal static bool ContainsVirtualMachineSignature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return s_virtualMachineSignatures.Any(signature =>
            value.Contains(signature, StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    private static bool RegistrySubKeyExists(string subKeyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadLocalMachineString(string subKeyPath, string valueName)
    {
        try
        {
            return Registry.GetValue($@"HKEY_LOCAL_MACHINE\{subKeyPath}", valueName, null) as string;
        }
        catch
        {
            return null;
        }
    }
}
