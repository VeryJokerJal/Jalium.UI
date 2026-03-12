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
public static class SystemParameters
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

    // Window metrics
    public static double BorderWidth => 1.0;
    public static double CaptionHeight => 22.0;
    public static double CaptionWidth => 22.0;
    public static Thickness WindowResizeBorderThickness => new Thickness(4);
    public static Thickness WindowNonClientFrameThickness => new Thickness(8, 30, 8, 8);

    // UI element sizes
    public static double SmallIconWidth => 16.0;
    public static double SmallIconHeight => 16.0;
    public static double IconWidth => 32.0;
    public static double IconHeight => 32.0;
    public static double MenuBarHeight => 20.0;
    public static double ScrollWidth => 17.0;
    public static double ScrollHeight => 17.0;
    public static double HorizontalScrollBarButtonWidth => 17.0;
    public static double VerticalScrollBarButtonHeight => 17.0;
    public static double HorizontalScrollBarHeight => 17.0;
    public static double VerticalScrollBarWidth => 17.0;
    public static double HorizontalScrollBarThumbWidth => 8.0;
    public static double VerticalScrollBarThumbHeight => 8.0;

    // Cursor sizes
    public static double CursorWidth => 32.0;
    public static double CursorHeight => 32.0;

    // Mouse settings
    public static int DoubleClickTime => 500;
    public static int MouseHoverTime => 400;
    public static double MouseHoverWidth => 4.0;
    public static double MouseHoverHeight => 4.0;

    // Drag settings
    public static double MinimumHorizontalDragDistance => 4.0;
    public static double MinimumVerticalDragDistance => 4.0;

    // Screen
    public static double PrimaryScreenWidth => 1920.0;
    public static double PrimaryScreenHeight => 1080.0;
    public static double VirtualScreenWidth => 1920.0;
    public static double VirtualScreenHeight => 1080.0;
    public static double VirtualScreenLeft => 0.0;
    public static double VirtualScreenTop => 0.0;
    public static Rect WorkArea => new Rect(0, 0, 1920, 1040);
    public static bool IsTabletPC => false;

    // Visual effects
    public static bool ClientAreaAnimation => true;
    public static bool DropShadow => true;
    public static bool FlatMenu => true;
    public static int ForegroundFlashCount => 3;
    public static bool GradientCaptions => true;
    public static bool HighContrast => false;
    public static bool MenuAnimation => true;
    public static bool MenuDropAlignment => false;
    public static bool SelectionFade => true;
    public static bool StylusHotTracking => true;
    public static bool ToolTipAnimation => true;
    public static bool UIEffects => true;

    // Focus
    public static int CaretWidth => 1;

    // Theme info
    public static bool IsGlassEnabled => true;

    // Caret blink rate
    public static int CaretBlinkTime => 530;

    // Wheel scroll lines
    public static int WheelScrollLines => 3;

    // Power
    public static bool PowerLineStatus => true; // AC power

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
