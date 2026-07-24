using System.Runtime.Versioning;
using System.ComponentModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Threading;

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

    static SystemParameters()
    {
        if (!OperatingSystem.IsLinux())
            return;

        LinuxDesktopSettings.SettingsChanged += static (_, _) =>
        {
            void Notify()
            {
                NotifyStaticPropertyChanged(null);
                foreach (Window window in Window.SnapshotOpenWindows())
                    window.RaisePlatformSystemSettingsChanged();
            }
            var dispatcher = Dispatcher.MainDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(Notify);
            else
                Notify();
        };
        LinuxDesktopSettings.EnsureMonitoring();
    }

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

    public static double CursorWidth => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.CursorSize
        : MetricDip(SM_CXCURSOR, 32);
    public static double CursorHeight => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.CursorSize
        : MetricDip(SM_CYCURSOR, 32);

    // Input settings.
    public static int DoubleClickTime => GetDoubleClickTimeOrDefault();
    internal static double MouseDoubleClickDistance => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.DoubleClickDistance
        : 4;
    public static TimeSpan MouseHoverTime => TimeSpan.FromMilliseconds(GetSpiUInt(SPI_GETMOUSEHOVERTIME, 400));
    public static double MouseHoverWidth => PixelDip(GetSpiUInt(SPI_GETMOUSEHOVERWIDTH, 4));
    public static double MouseHoverHeight => PixelDip(GetSpiUInt(SPI_GETMOUSEHOVERHEIGHT, 4));
    public static double MinimumHorizontalDragDistance => OperatingSystem.IsLinux()
        ? Math.Max(1, LinuxDesktopSettings.DragThreshold)
        : Math.Max(1, MetricDip(SM_CXDRAG, 4));
    public static double MinimumVerticalDragDistance => OperatingSystem.IsLinux()
        ? Math.Max(1, LinuxDesktopSettings.DragThreshold)
        : Math.Max(1, MetricDip(SM_CYDRAG, 4));

    // Screen information. Non-Windows platforms answer through the
    // jalium.native.platform monitor ABI (XRandR / wl_output).
    public static double PrimaryScreenWidth => GetPrimaryScreenDimensionDip(width: true, 1920);
    public static double PrimaryScreenHeight => GetPrimaryScreenDimensionDip(width: false, 1080);
    public static double VirtualScreenWidth => OperatingSystem.IsWindows()
        ? MetricDip(SM_CXVIRTUALSCREEN, 1920)
        : GetVirtualScreenRect().Width;
    public static double VirtualScreenHeight => OperatingSystem.IsWindows()
        ? MetricDip(SM_CYVIRTUALSCREEN, 1080)
        : GetVirtualScreenRect().Height;
    public static double VirtualScreenLeft => OperatingSystem.IsWindows()
        ? MetricDipAllowZero(SM_XVIRTUALSCREEN, 0)
        : GetVirtualScreenRect().X;
    public static double VirtualScreenTop => OperatingSystem.IsWindows()
        ? MetricDipAllowZero(SM_YVIRTUALSCREEN, 0)
        : GetVirtualScreenRect().Y;
    public static Rect WorkArea => GetWorkArea();
    public static bool IsTabletPC => GetMetricBool(SM_TABLETPC, false);

    // Visual and accessibility effects.
    public static bool ClientAreaAnimation => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.AnimationsEnabled
        : GetSpiBool(SPI_GETCLIENTAREAANIMATION, true);
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
    public static bool UIEffects => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.AnimationsEnabled
        : GetSpiBool(SPI_GETUIEFFECTS, true);

    public static double CaretWidth => PixelDip(GetSpiUInt(SPI_GETCARETWIDTH, 1));
    public static bool IsGlassEnabled => GetIsGlassEnabled();
    public static int CaretBlinkTime => GetCaretBlinkTimeOrDefault();
    public static int WheelScrollLines => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.WheelScrollLines
        : (int)Math.Min(int.MaxValue, GetSpiUInt(SPI_GETWHEELSCROLLLINES, 3));
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

    #region Resources

    private static readonly object s_resourceLock = new();
    private static readonly Dictionary<string, ResourceKey> s_resourceKeys = new(StringComparer.Ordinal);
    private static readonly Style s_focusVisualStyle = new();
    private static readonly Style s_navigationChromeDownLevelStyle = new();
    private static readonly Style s_navigationChromeStyle = new();

    public static ResourceKey BorderKey => GetResourceKey(nameof(Border));
    public static ResourceKey BorderWidthKey => GetResourceKey(nameof(BorderWidth));
    public static ResourceKey CaptionHeightKey => GetResourceKey(nameof(CaptionHeight));
    public static ResourceKey CaptionWidthKey => GetResourceKey(nameof(CaptionWidth));
    public static ResourceKey CaretWidthKey => GetResourceKey(nameof(CaretWidth));
    public static ResourceKey ClientAreaAnimationKey => GetResourceKey(nameof(ClientAreaAnimation));
    public static ResourceKey ComboBoxAnimationKey => GetResourceKey(nameof(ComboBoxAnimation));
    public static ResourceKey ComboBoxPopupAnimationKey => GetResourceKey(nameof(ComboBoxPopupAnimation));
    public static ResourceKey CursorHeightKey => GetResourceKey(nameof(CursorHeight));
    public static ResourceKey CursorShadowKey => GetResourceKey(nameof(CursorShadow));
    public static ResourceKey CursorWidthKey => GetResourceKey(nameof(CursorWidth));
    public static ResourceKey DragFullWindowsKey => GetResourceKey(nameof(DragFullWindows));
    public static ResourceKey DropShadowKey => GetResourceKey(nameof(DropShadow));
    public static ResourceKey FixedFrameHorizontalBorderHeightKey => GetResourceKey(nameof(FixedFrameHorizontalBorderHeight));
    public static ResourceKey FixedFrameVerticalBorderWidthKey => GetResourceKey(nameof(FixedFrameVerticalBorderWidth));
    public static ResourceKey FlatMenuKey => GetResourceKey(nameof(FlatMenu));
    public static ResourceKey FocusBorderHeightKey => GetResourceKey(nameof(FocusBorderHeight));
    public static ResourceKey FocusBorderWidthKey => GetResourceKey(nameof(FocusBorderWidth));
    public static ResourceKey FocusHorizontalBorderHeightKey => GetResourceKey(nameof(FocusHorizontalBorderHeight));
    public static ResourceKey FocusVerticalBorderWidthKey => GetResourceKey(nameof(FocusVerticalBorderWidth));
    public static ResourceKey FocusVisualStyleKey => GetResourceKey("FocusVisualStyle");
    public static ResourceKey ForegroundFlashCountKey => GetResourceKey(nameof(ForegroundFlashCount));
    public static ResourceKey FullPrimaryScreenHeightKey => GetResourceKey(nameof(FullPrimaryScreenHeight));
    public static ResourceKey FullPrimaryScreenWidthKey => GetResourceKey(nameof(FullPrimaryScreenWidth));
    public static ResourceKey GradientCaptionsKey => GetResourceKey(nameof(GradientCaptions));
    public static ResourceKey HighContrastKey => GetResourceKey(nameof(HighContrast));
    public static ResourceKey HorizontalScrollBarButtonWidthKey => GetResourceKey(nameof(HorizontalScrollBarButtonWidth));
    public static ResourceKey HorizontalScrollBarHeightKey => GetResourceKey(nameof(HorizontalScrollBarHeight));
    public static ResourceKey HorizontalScrollBarThumbWidthKey => GetResourceKey(nameof(HorizontalScrollBarThumbWidth));
    public static ResourceKey HotTrackingKey => GetResourceKey(nameof(HotTracking));
    public static ResourceKey IconGridHeightKey => GetResourceKey(nameof(IconGridHeight));
    public static ResourceKey IconGridWidthKey => GetResourceKey(nameof(IconGridWidth));
    public static ResourceKey IconHeightKey => GetResourceKey(nameof(IconHeight));
    public static ResourceKey IconHorizontalSpacingKey => GetResourceKey(nameof(IconHorizontalSpacing));
    public static ResourceKey IconTitleWrapKey => GetResourceKey(nameof(IconTitleWrap));
    public static ResourceKey IconVerticalSpacingKey => GetResourceKey(nameof(IconVerticalSpacing));
    public static ResourceKey IconWidthKey => GetResourceKey(nameof(IconWidth));
    public static ResourceKey IsImmEnabledKey => GetResourceKey(nameof(IsImmEnabled));
    public static ResourceKey IsMediaCenterKey => GetResourceKey(nameof(IsMediaCenter));
    public static ResourceKey IsMenuDropRightAlignedKey => GetResourceKey(nameof(IsMenuDropRightAligned));
    public static ResourceKey IsMiddleEastEnabledKey => GetResourceKey(nameof(IsMiddleEastEnabled));
    public static ResourceKey IsMousePresentKey => GetResourceKey(nameof(IsMousePresent));
    public static ResourceKey IsMouseWheelPresentKey => GetResourceKey(nameof(IsMouseWheelPresent));
    public static ResourceKey IsPenWindowsKey => GetResourceKey(nameof(IsPenWindows));
    public static ResourceKey IsRemoteSessionKey => GetResourceKey(nameof(IsRemoteSession));
    public static ResourceKey IsRemotelyControlledKey => GetResourceKey(nameof(IsRemotelyControlled));
    public static ResourceKey IsSlowMachineKey => GetResourceKey(nameof(IsSlowMachine));
    public static ResourceKey IsTabletPCKey => GetResourceKey(nameof(IsTabletPC));
    public static ResourceKey KanjiWindowHeightKey => GetResourceKey(nameof(KanjiWindowHeight));
    public static ResourceKey KeyboardCuesKey => GetResourceKey(nameof(KeyboardCues));
    public static ResourceKey KeyboardDelayKey => GetResourceKey(nameof(KeyboardDelay));
    public static ResourceKey KeyboardPreferenceKey => GetResourceKey(nameof(KeyboardPreference));
    public static ResourceKey KeyboardSpeedKey => GetResourceKey(nameof(KeyboardSpeed));
    public static ResourceKey ListBoxSmoothScrollingKey => GetResourceKey(nameof(ListBoxSmoothScrolling));
    public static ResourceKey MaximizedPrimaryScreenHeightKey => GetResourceKey(nameof(MaximizedPrimaryScreenHeight));
    public static ResourceKey MaximizedPrimaryScreenWidthKey => GetResourceKey(nameof(MaximizedPrimaryScreenWidth));
    public static ResourceKey MaximumWindowTrackHeightKey => GetResourceKey(nameof(MaximumWindowTrackHeight));
    public static ResourceKey MaximumWindowTrackWidthKey => GetResourceKey(nameof(MaximumWindowTrackWidth));
    public static ResourceKey MenuAnimationKey => GetResourceKey(nameof(MenuAnimation));
    public static ResourceKey MenuBarHeightKey => GetResourceKey(nameof(MenuBarHeight));
    public static ResourceKey MenuButtonHeightKey => GetResourceKey(nameof(MenuButtonHeight));
    public static ResourceKey MenuButtonWidthKey => GetResourceKey(nameof(MenuButtonWidth));
    public static ResourceKey MenuCheckmarkHeightKey => GetResourceKey(nameof(MenuCheckmarkHeight));
    public static ResourceKey MenuCheckmarkWidthKey => GetResourceKey(nameof(MenuCheckmarkWidth));
    public static ResourceKey MenuDropAlignmentKey => GetResourceKey(nameof(MenuDropAlignment));
    public static ResourceKey MenuFadeKey => GetResourceKey(nameof(MenuFade));
    public static ResourceKey MenuHeightKey => GetResourceKey(nameof(MenuHeight));
    public static ResourceKey MenuPopupAnimationKey => GetResourceKey(nameof(MenuPopupAnimation));
    public static ResourceKey MenuShowDelayKey => GetResourceKey(nameof(MenuShowDelay));
    public static ResourceKey MenuWidthKey => GetResourceKey(nameof(MenuWidth));
    public static ResourceKey MinimizeAnimationKey => GetResourceKey(nameof(MinimizeAnimation));
    public static ResourceKey MinimizedGridHeightKey => GetResourceKey(nameof(MinimizedGridHeight));
    public static ResourceKey MinimizedGridWidthKey => GetResourceKey(nameof(MinimizedGridWidth));
    public static ResourceKey MinimizedWindowHeightKey => GetResourceKey(nameof(MinimizedWindowHeight));
    public static ResourceKey MinimizedWindowWidthKey => GetResourceKey(nameof(MinimizedWindowWidth));
    public static ResourceKey MinimumWindowHeightKey => GetResourceKey(nameof(MinimumWindowHeight));
    public static ResourceKey MinimumWindowTrackHeightKey => GetResourceKey(nameof(MinimumWindowTrackHeight));
    public static ResourceKey MinimumWindowTrackWidthKey => GetResourceKey(nameof(MinimumWindowTrackWidth));
    public static ResourceKey MinimumWindowWidthKey => GetResourceKey(nameof(MinimumWindowWidth));
    public static ResourceKey MouseHoverHeightKey => GetResourceKey(nameof(MouseHoverHeight));
    public static ResourceKey MouseHoverTimeKey => GetResourceKey(nameof(MouseHoverTime));
    public static ResourceKey MouseHoverWidthKey => GetResourceKey(nameof(MouseHoverWidth));
    public static ResourceKey NavigationChromeDownLevelStyleKey => GetResourceKey("NavigationChromeDownLevelStyle");
    public static ResourceKey NavigationChromeStyleKey => GetResourceKey("NavigationChromeStyle");
    public static ResourceKey PowerLineStatusKey => GetResourceKey(nameof(PowerLineStatus));
    public static ResourceKey PrimaryScreenHeightKey => GetResourceKey(nameof(PrimaryScreenHeight));
    public static ResourceKey PrimaryScreenWidthKey => GetResourceKey(nameof(PrimaryScreenWidth));
    public static ResourceKey ResizeFrameHorizontalBorderHeightKey => GetResourceKey(nameof(ResizeFrameHorizontalBorderHeight));
    public static ResourceKey ResizeFrameVerticalBorderWidthKey => GetResourceKey(nameof(ResizeFrameVerticalBorderWidth));
    public static ResourceKey ScrollHeightKey => GetResourceKey(nameof(ScrollHeight));
    public static ResourceKey ScrollWidthKey => GetResourceKey(nameof(ScrollWidth));
    public static ResourceKey SelectionFadeKey => GetResourceKey(nameof(SelectionFade));
    public static ResourceKey ShowSoundsKey => GetResourceKey(nameof(ShowSounds));
    public static ResourceKey SmallCaptionHeightKey => GetResourceKey(nameof(SmallCaptionHeight));
    public static ResourceKey SmallCaptionWidthKey => GetResourceKey(nameof(SmallCaptionWidth));
    public static ResourceKey SmallIconHeightKey => GetResourceKey(nameof(SmallIconHeight));
    public static ResourceKey SmallIconWidthKey => GetResourceKey(nameof(SmallIconWidth));
    public static ResourceKey SmallWindowCaptionButtonHeightKey => GetResourceKey(nameof(SmallWindowCaptionButtonHeight));
    public static ResourceKey SmallWindowCaptionButtonWidthKey => GetResourceKey(nameof(SmallWindowCaptionButtonWidth));
    public static ResourceKey SnapToDefaultButtonKey => GetResourceKey(nameof(SnapToDefaultButton));
    public static ResourceKey StylusHotTrackingKey => GetResourceKey(nameof(StylusHotTracking));
    public static ResourceKey SwapButtonsKey => GetResourceKey(nameof(SwapButtons));
    public static ResourceKey ThickHorizontalBorderHeightKey => GetResourceKey(nameof(ThickHorizontalBorderHeight));
    public static ResourceKey ThickVerticalBorderWidthKey => GetResourceKey(nameof(ThickVerticalBorderWidth));
    public static ResourceKey ThinHorizontalBorderHeightKey => GetResourceKey(nameof(ThinHorizontalBorderHeight));
    public static ResourceKey ThinVerticalBorderWidthKey => GetResourceKey(nameof(ThinVerticalBorderWidth));
    public static ResourceKey ToolTipAnimationKey => GetResourceKey(nameof(ToolTipAnimation));
    public static ResourceKey ToolTipFadeKey => GetResourceKey(nameof(ToolTipFade));
    public static ResourceKey ToolTipPopupAnimationKey => GetResourceKey(nameof(ToolTipPopupAnimation));
    public static ResourceKey UIEffectsKey => GetResourceKey(nameof(UIEffects));
    public static ResourceKey VerticalScrollBarButtonHeightKey => GetResourceKey(nameof(VerticalScrollBarButtonHeight));
    public static ResourceKey VerticalScrollBarThumbHeightKey => GetResourceKey(nameof(VerticalScrollBarThumbHeight));
    public static ResourceKey VerticalScrollBarWidthKey => GetResourceKey(nameof(VerticalScrollBarWidth));
    public static ResourceKey VirtualScreenHeightKey => GetResourceKey(nameof(VirtualScreenHeight));
    public static ResourceKey VirtualScreenLeftKey => GetResourceKey(nameof(VirtualScreenLeft));
    public static ResourceKey VirtualScreenTopKey => GetResourceKey(nameof(VirtualScreenTop));
    public static ResourceKey VirtualScreenWidthKey => GetResourceKey(nameof(VirtualScreenWidth));
    public static ResourceKey WheelScrollLinesKey => GetResourceKey(nameof(WheelScrollLines));
    public static ResourceKey WindowCaptionButtonHeightKey => GetResourceKey(nameof(WindowCaptionButtonHeight));
    public static ResourceKey WindowCaptionButtonWidthKey => GetResourceKey(nameof(WindowCaptionButtonWidth));
    public static ResourceKey WindowCaptionHeightKey => GetResourceKey(nameof(WindowCaptionHeight));
    public static ResourceKey WorkAreaKey => GetResourceKey(nameof(WorkArea));

    internal static bool TryGetResource(object resourceKey, out object? resource)
    {
        if (resourceKey is not ComponentResourceKey
            {
                TypeInTargetAssembly: { } ownerType,
                ResourceId: string resourceId,
            }
            || ownerType != typeof(SystemParameters))
        {
            resource = null;
            return false;
        }

        resource = resourceId switch
        {
            nameof(Border) => Border,
            nameof(BorderWidth) => BorderWidth,
            nameof(CaptionHeight) => CaptionHeight,
            nameof(CaptionWidth) => CaptionWidth,
            nameof(CaretWidth) => CaretWidth,
            nameof(ClientAreaAnimation) => ClientAreaAnimation,
            nameof(ComboBoxAnimation) => ComboBoxAnimation,
            nameof(ComboBoxPopupAnimation) => ComboBoxPopupAnimation,
            nameof(CursorHeight) => CursorHeight,
            nameof(CursorShadow) => CursorShadow,
            nameof(CursorWidth) => CursorWidth,
            nameof(DragFullWindows) => DragFullWindows,
            nameof(DropShadow) => DropShadow,
            nameof(FixedFrameHorizontalBorderHeight) => FixedFrameHorizontalBorderHeight,
            nameof(FixedFrameVerticalBorderWidth) => FixedFrameVerticalBorderWidth,
            nameof(FlatMenu) => FlatMenu,
            nameof(FocusBorderHeight) => FocusBorderHeight,
            nameof(FocusBorderWidth) => FocusBorderWidth,
            nameof(FocusHorizontalBorderHeight) => FocusHorizontalBorderHeight,
            nameof(FocusVerticalBorderWidth) => FocusVerticalBorderWidth,
            "FocusVisualStyle" => s_focusVisualStyle,
            nameof(ForegroundFlashCount) => ForegroundFlashCount,
            nameof(FullPrimaryScreenHeight) => FullPrimaryScreenHeight,
            nameof(FullPrimaryScreenWidth) => FullPrimaryScreenWidth,
            nameof(GradientCaptions) => GradientCaptions,
            nameof(HighContrast) => HighContrast,
            nameof(HorizontalScrollBarButtonWidth) => HorizontalScrollBarButtonWidth,
            nameof(HorizontalScrollBarHeight) => HorizontalScrollBarHeight,
            nameof(HorizontalScrollBarThumbWidth) => HorizontalScrollBarThumbWidth,
            nameof(HotTracking) => HotTracking,
            nameof(IconGridHeight) => IconGridHeight,
            nameof(IconGridWidth) => IconGridWidth,
            nameof(IconHeight) => IconHeight,
            nameof(IconHorizontalSpacing) => IconHorizontalSpacing,
            nameof(IconTitleWrap) => IconTitleWrap,
            nameof(IconVerticalSpacing) => IconVerticalSpacing,
            nameof(IconWidth) => IconWidth,
            nameof(IsImmEnabled) => IsImmEnabled,
            nameof(IsMediaCenter) => IsMediaCenter,
            nameof(IsMenuDropRightAligned) => IsMenuDropRightAligned,
            nameof(IsMiddleEastEnabled) => IsMiddleEastEnabled,
            nameof(IsMousePresent) => IsMousePresent,
            nameof(IsMouseWheelPresent) => IsMouseWheelPresent,
            nameof(IsPenWindows) => IsPenWindows,
            nameof(IsRemoteSession) => IsRemoteSession,
            nameof(IsRemotelyControlled) => IsRemotelyControlled,
            nameof(IsSlowMachine) => IsSlowMachine,
            nameof(IsTabletPC) => IsTabletPC,
            nameof(KanjiWindowHeight) => KanjiWindowHeight,
            nameof(KeyboardCues) => KeyboardCues,
            nameof(KeyboardDelay) => KeyboardDelay,
            nameof(KeyboardPreference) => KeyboardPreference,
            nameof(KeyboardSpeed) => KeyboardSpeed,
            nameof(ListBoxSmoothScrolling) => ListBoxSmoothScrolling,
            nameof(MaximizedPrimaryScreenHeight) => MaximizedPrimaryScreenHeight,
            nameof(MaximizedPrimaryScreenWidth) => MaximizedPrimaryScreenWidth,
            nameof(MaximumWindowTrackHeight) => MaximumWindowTrackHeight,
            nameof(MaximumWindowTrackWidth) => MaximumWindowTrackWidth,
            nameof(MenuAnimation) => MenuAnimation,
            nameof(MenuBarHeight) => MenuBarHeight,
            nameof(MenuButtonHeight) => MenuButtonHeight,
            nameof(MenuButtonWidth) => MenuButtonWidth,
            nameof(MenuCheckmarkHeight) => MenuCheckmarkHeight,
            nameof(MenuCheckmarkWidth) => MenuCheckmarkWidth,
            nameof(MenuDropAlignment) => MenuDropAlignment,
            nameof(MenuFade) => MenuFade,
            nameof(MenuHeight) => MenuHeight,
            nameof(MenuPopupAnimation) => MenuPopupAnimation,
            nameof(MenuShowDelay) => MenuShowDelay,
            nameof(MenuWidth) => MenuWidth,
            nameof(MinimizeAnimation) => MinimizeAnimation,
            nameof(MinimizedGridHeight) => MinimizedGridHeight,
            nameof(MinimizedGridWidth) => MinimizedGridWidth,
            nameof(MinimizedWindowHeight) => MinimizedWindowHeight,
            nameof(MinimizedWindowWidth) => MinimizedWindowWidth,
            nameof(MinimumWindowHeight) => MinimumWindowHeight,
            nameof(MinimumWindowTrackHeight) => MinimumWindowTrackHeight,
            nameof(MinimumWindowTrackWidth) => MinimumWindowTrackWidth,
            nameof(MinimumWindowWidth) => MinimumWindowWidth,
            nameof(MouseHoverHeight) => MouseHoverHeight,
            nameof(MouseHoverTime) => MouseHoverTime,
            nameof(MouseHoverWidth) => MouseHoverWidth,
            "NavigationChromeDownLevelStyle" => s_navigationChromeDownLevelStyle,
            "NavigationChromeStyle" => s_navigationChromeStyle,
            nameof(PowerLineStatus) => PowerLineStatus,
            nameof(PrimaryScreenHeight) => PrimaryScreenHeight,
            nameof(PrimaryScreenWidth) => PrimaryScreenWidth,
            nameof(ResizeFrameHorizontalBorderHeight) => ResizeFrameHorizontalBorderHeight,
            nameof(ResizeFrameVerticalBorderWidth) => ResizeFrameVerticalBorderWidth,
            nameof(ScrollHeight) => ScrollHeight,
            nameof(ScrollWidth) => ScrollWidth,
            nameof(SelectionFade) => SelectionFade,
            nameof(ShowSounds) => ShowSounds,
            nameof(SmallCaptionHeight) => SmallCaptionHeight,
            nameof(SmallCaptionWidth) => SmallCaptionWidth,
            nameof(SmallIconHeight) => SmallIconHeight,
            nameof(SmallIconWidth) => SmallIconWidth,
            nameof(SmallWindowCaptionButtonHeight) => SmallWindowCaptionButtonHeight,
            nameof(SmallWindowCaptionButtonWidth) => SmallWindowCaptionButtonWidth,
            nameof(SnapToDefaultButton) => SnapToDefaultButton,
            nameof(StylusHotTracking) => StylusHotTracking,
            nameof(SwapButtons) => SwapButtons,
            nameof(ThickHorizontalBorderHeight) => ThickHorizontalBorderHeight,
            nameof(ThickVerticalBorderWidth) => ThickVerticalBorderWidth,
            nameof(ThinHorizontalBorderHeight) => ThinHorizontalBorderHeight,
            nameof(ThinVerticalBorderWidth) => ThinVerticalBorderWidth,
            nameof(ToolTipAnimation) => ToolTipAnimation,
            nameof(ToolTipFade) => ToolTipFade,
            nameof(ToolTipPopupAnimation) => ToolTipPopupAnimation,
            nameof(UIEffects) => UIEffects,
            nameof(VerticalScrollBarButtonHeight) => VerticalScrollBarButtonHeight,
            nameof(VerticalScrollBarThumbHeight) => VerticalScrollBarThumbHeight,
            nameof(VerticalScrollBarWidth) => VerticalScrollBarWidth,
            nameof(VirtualScreenHeight) => VirtualScreenHeight,
            nameof(VirtualScreenLeft) => VirtualScreenLeft,
            nameof(VirtualScreenTop) => VirtualScreenTop,
            nameof(VirtualScreenWidth) => VirtualScreenWidth,
            nameof(WheelScrollLines) => WheelScrollLines,
            nameof(WindowCaptionButtonHeight) => WindowCaptionButtonHeight,
            nameof(WindowCaptionButtonWidth) => WindowCaptionButtonWidth,
            nameof(WindowCaptionHeight) => WindowCaptionHeight,
            nameof(WorkArea) => WorkArea,
            _ => DependencyProperty.UnsetValue,
        };

        if (ReferenceEquals(resource, DependencyProperty.UnsetValue))
        {
            resource = null;
            return false;
        }

        return true;
    }

    private static ResourceKey GetResourceKey(string resourceId)
    {
        lock (s_resourceLock)
        {
            if (!s_resourceKeys.TryGetValue(resourceId, out var key))
            {
                key = new ComponentResourceKey(typeof(SystemParameters), resourceId);
                s_resourceKeys.Add(resourceId, key);
            }

            return key;
        }
    }

    #endregion

    #region WpfParity

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
    public static int KeyboardDelay => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.KeyboardDelay
        : (int)Math.Min(3, GetSpiUInt(SPI_GETKEYBOARDDELAY, 1));
    public static bool KeyboardPreference => GetSpiBool(SPI_GETKEYBOARDPREF, false);
    public static int KeyboardSpeed => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.KeyboardSpeed
        : (int)Math.Min(31, GetSpiUInt(SPI_GETKEYBOARDSPEED, 31));
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
    public static int MenuShowDelay => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.MenuShowDelay
        : (int)Math.Min(int.MaxValue, GetSpiUInt(SPI_GETMENUSHOWDELAY, 400));
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
    public static bool SwapButtons => OperatingSystem.IsLinux()
        ? LinuxDesktopSettings.SwapButtons
        : GetMetricBool(SM_SWAPBUTTON, false);
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

    /// <summary>
    /// Queries the primary monitor through the jalium.native.platform monitor
    /// ABI (XRandR on X11, wl_output on Wayland). Returns false on Windows
    /// (user32 paths own those values), before platform init, or headless.
    /// </summary>
    internal static bool TryGetPrimaryPlatformMonitor(
        out Jalium.UI.Interop.NativeMethods.NativeMonitorInfo monitor)
    {
        monitor = default;
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var count = Jalium.UI.Interop.NativeMethods.PlatformGetMonitorCount();
            var haveFirst = false;
            for (var i = 0; i < count; i++)
            {
                if (Jalium.UI.Interop.NativeMethods.PlatformGetMonitorInfo(i, out var candidate) != 0 ||
                    candidate.Width <= 0 || candidate.Height <= 0)
                {
                    continue;
                }

                if (candidate.IsPrimary != 0)
                {
                    monitor = candidate;
                    return true;
                }

                if (!haveFirst)
                {
                    monitor = candidate;
                    haveFirst = true;
                }
            }

            return haveFirst;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static double PlatformScreenDip(bool width, double fallback)
    {
        if (TryGetPrimaryPlatformMonitor(out var monitor))
        {
            var scale = monitor.Scale > 0 ? monitor.Scale : 1.0f;
            return (width ? monitor.Width : monitor.Height) / scale;
        }

        return fallback;
    }

    private static Rect GetVirtualScreenRect()
    {
        if (!OperatingSystem.IsLinux())
            return new Rect(0, 0, PrimaryScreenWidth, PrimaryScreenHeight);

        try
        {
            var count = Jalium.UI.Interop.NativeMethods.PlatformGetMonitorCount();
            var wayland = Jalium.UI.Interop.NativeMethods.PlatformGetCurrent() ==
                (int)Jalium.UI.Interop.NativePlatform.LinuxWayland;
            var hasMonitor = false;
            double left = 0;
            double top = 0;
            double right = 0;
            double bottom = 0;
            for (var index = 0; index < count; index++)
            {
                if (Jalium.UI.Interop.NativeMethods.PlatformGetMonitorInfo(index, out var monitor) != 0 ||
                    monitor.Width <= 0 || monitor.Height <= 0)
                {
                    continue;
                }

                var logical = ConvertPlatformMonitorRect(monitor, workArea: false, wayland);
                var monitorLeft = logical.X;
                var monitorTop = logical.Y;
                var monitorRight = logical.Right;
                var monitorBottom = logical.Bottom;
                if (!hasMonitor)
                {
                    left = monitorLeft;
                    top = monitorTop;
                    right = monitorRight;
                    bottom = monitorBottom;
                    hasMonitor = true;
                }
                else
                {
                    left = Math.Min(left, monitorLeft);
                    top = Math.Min(top, monitorTop);
                    right = Math.Max(right, monitorRight);
                    bottom = Math.Max(bottom, monitorBottom);
                }
            }

            if (hasMonitor)
                return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return new Rect(0, 0, PrimaryScreenWidth, PrimaryScreenHeight);
    }

    /// <summary>
    /// Converts the intentionally mixed-unit Linux monitor ABI to DIPs. X11
    /// origins and sizes are physical pixels, while Wayland origins are already
    /// compositor-logical and only mode/work sizes remain physical pixels.
    /// Kept internal so unit conversion can be verified without a live display.
    /// </summary>
    internal static Rect ConvertPlatformMonitorRect(
        Jalium.UI.Interop.NativeMethods.NativeMonitorInfo monitor,
        bool workArea,
        bool wayland)
    {
        var scale = monitor.Scale > 0 && float.IsFinite(monitor.Scale)
            ? monitor.Scale
            : 1.0f;
        var rawX = workArea ? monitor.WorkX : monitor.X;
        var rawY = workArea ? monitor.WorkY : monitor.Y;
        var rawWidth = workArea ? monitor.WorkWidth : monitor.Width;
        var rawHeight = workArea ? monitor.WorkHeight : monitor.Height;
        return new Rect(
            wayland ? rawX : rawX / scale,
            wayland ? rawY : rawY / scale,
            Math.Max(0, rawWidth / scale),
            Math.Max(0, rawHeight / scale));
    }

    internal static double GetPrimaryScreenDimensionDip(bool width, double fallback)
        => OperatingSystem.IsWindows()
            ? MetricDip(width ? SM_CXSCREEN : SM_CYSCREEN, (int)fallback)
            : PlatformScreenDip(width, fallback);

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
        if (OperatingSystem.IsLinux())
            return LinuxDesktopSettings.DoubleClickTime;
        if (!OperatingSystem.IsWindows())
            return 500;

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
        if (OperatingSystem.IsLinux())
            return LinuxDesktopSettings.CaretBlinkTime;
        if (!OperatingSystem.IsWindows())
            return 530;

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

        if (TryGetPrimaryPlatformMonitor(out var monitor) &&
            monitor.WorkWidth > 0 && monitor.WorkHeight > 0)
        {
            var wayland = false;
            try
            {
                wayland = Jalium.UI.Interop.NativeMethods.PlatformGetCurrent() ==
                    (int)Jalium.UI.Interop.NativePlatform.LinuxWayland;
            }
            catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
            {
            }
            return ConvertPlatformMonitorRect(monitor, workArea: true, wayland);
        }

        return new Rect(0, 0, PrimaryScreenWidth, Math.Max(0, PrimaryScreenHeight - 40));
    }

    private static bool GetHighContrast()
    {
        if (OperatingSystem.IsLinux())
            return LinuxDesktopSettings.HighContrast;
        if (!OperatingSystem.IsWindows())
            return false;

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
        if (OperatingSystem.IsLinux())
            return LinuxDesktopSettings.AnimationsEnabled;
        if (!OperatingSystem.IsWindows())
            return true;

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
        if (OperatingSystem.IsLinux())
            return LinuxDesktopSettings.ReadPowerLineStatus();
        if (!OperatingSystem.IsWindows())
            return global::Jalium.UI.PowerLineStatus.Unknown;

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
        if (OperatingSystem.IsLinux())
        {
            var theme = LinuxDesktopSettings.ThemeName;
            return new ThemeInfo(
                string.IsNullOrWhiteSpace(theme) ? "Default" : theme,
                LinuxDesktopSettings.HighContrast ? "HighContrast" : string.Empty);
        }

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

    #endregion
}
