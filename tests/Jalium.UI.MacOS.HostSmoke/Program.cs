using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.MacOS;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("macos15.0")]

JaliumMacApplication.Initialize();

if (args.Length == 1 && args[0] == "--window-file-dialogs")
    return WindowFileDialogChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-file-dialog-case=", StringComparison.Ordinal))
    return WindowFileDialogChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--text-font-transforms")
    return TextAccessibilityStyleChecks.RunFontTransforms();
if (args.Length == 1 && args[0].StartsWith("--text-font-transform-case=", StringComparison.Ordinal))
    return TextAccessibilityStyleChecks.RunFontTransformCase(args[0]);

if (args.Length == 1 && args[0] == "--text-font-matching")
    return TextAccessibilityStyleChecks.RunFontMatching();
if (args.Length == 1 && args[0].StartsWith("--text-font-matching-case=", StringComparison.Ordinal))
    return TextAccessibilityStyleChecks.RunFontMatchingCase(args[0]);

if (args.Length == 1 && args[0] == "--text-accessibility-styles")
    return TextAccessibilityStyleChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-grapheme-keys")
    return WindowGraphemeKeyChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-grapheme-key-case=", StringComparison.Ordinal))
    return WindowGraphemeKeyChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--text-accessibility-style-case=", StringComparison.Ordinal))
    return TextAccessibilityStyleChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--accessibility-clipping")
    return AccessibilityClippingChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--accessibility-clipping-case=", StringComparison.Ordinal))
    return AccessibilityClippingChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-drag-representations")
    return WindowDragRepresentationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-drag-representation-case=", StringComparison.Ordinal))
    return WindowDragRepresentationChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--text-accessibility-appkit-probe")
    return TextAccessibilityNavigationChecks.ProbeAppKit();
if (args.Length == 1 && args[0] == "--text-accessibility-navigation")
    return TextAccessibilityNavigationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--text-accessibility-navigation-case=", StringComparison.Ordinal))
    return TextAccessibilityNavigationChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--path-rendering")
    return PathRenderingChecks.RunAll();

if (args.Length == 1 && args[0] == "--window-visibility")
    return WindowVisibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-visibility-case=", StringComparison.Ordinal))
    return WindowVisibilityChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-opacity")
    return WindowOpacityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-opacity-case=", StringComparison.Ordinal))
    return WindowOpacityChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-background-alpha")
    return WindowBackgroundAlphaChecks.RunAll();

if (args.Length == 1 && args[0] == "--window-font")
    return WindowFontChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-font-case=", StringComparison.Ordinal))
    return WindowFontChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-editing-actions")
    return WindowEditingActionChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-editing-actions-case=", StringComparison.Ordinal))
    return WindowEditingActionChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-editing-menu")
    return WindowEditingMenuChecks.RunAll();
if (args.Length == 1 && args[0] == "--editor-accessibility")
    return EditControlAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--editor-accessibility-case=", StringComparison.Ordinal))
    return EditControlAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-editing-menu-case=", StringComparison.Ordinal))
    return WindowEditingMenuChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-text-navigation")
    return WindowTextNavigationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-text-navigation-case=", StringComparison.Ordinal))
    return WindowTextNavigationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-tab-focus")
    return WindowTabFocusChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-input-boundaries")
    return WindowInputBoundaryChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-input-boundary-case=", StringComparison.Ordinal))
    return WindowInputBoundaryChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-tab-focus-case=", StringComparison.Ordinal))
    return WindowTabFocusChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-word-navigation")
    return WindowWordNavigationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-word-navigation-case=", StringComparison.Ordinal))
    return WindowWordNavigationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-lifecycle")
    return WindowLifecycleChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-minimize-transition")
    return WindowMinimizeTransitionChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-minimize-transition-case=", StringComparison.Ordinal))
    return WindowMinimizeTransitionChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-fullscreen-failure")
    return WindowFullScreenFailureChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-fullscreen-failure-case=", StringComparison.Ordinal))
    return WindowFullScreenFailureChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-startup-location")
    return WindowStartupLocationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-startup-location-case=", StringComparison.Ordinal))
    return WindowStartupLocationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-system-menu")
    return WindowSystemMenuChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-system-menu-case=", StringComparison.Ordinal))
    return WindowSystemMenuChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-application-menu")
    return WindowApplicationMenuChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-application-menu-case=", StringComparison.Ordinal))
    return WindowApplicationMenuChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-application-activation")
    return WindowApplicationActivationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-application-activation-case=", StringComparison.Ordinal))
    return WindowApplicationActivationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-reopen")
    return WindowReopenChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-reopen-foreground")
    return WindowReopenForegroundChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-reopen-foreground-case=", StringComparison.Ordinal))
    return WindowReopenForegroundChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-quit")
    return WindowQuitChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-native-quit")
    return WindowNativeQuitChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-native-quit-case=", StringComparison.Ordinal))
    return WindowNativeQuitChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-quit-case=", StringComparison.Ordinal))
    return WindowQuitChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-reopen-case=", StringComparison.Ordinal))
    return WindowReopenChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--accessibility")
    return AccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--accessibility-case=", StringComparison.Ordinal))
    return AccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--accessibility-semantics")
    return AccessibilitySemanticsChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--accessibility-semantics-case=", StringComparison.Ordinal))
    return AccessibilitySemanticsChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-accessibility")
    return WindowAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-accessibility-case=", StringComparison.Ordinal))
    return WindowAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-buttons-accessibility")
    return WindowButtonAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-buttons-accessibility-case=", StringComparison.Ordinal))
    return WindowButtonAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-caption-accessibility")
    return WindowCaptionAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-caption-accessibility-case=", StringComparison.Ordinal))
    return WindowCaptionAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-property-accessibility")
    return WindowPropertyAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-property-activation-accessibility")
    return WindowPropertyAccessibilityChecks.RunActivationAll();
if (args.Length == 1 && args[0].StartsWith("--window-property-accessibility-case=", StringComparison.Ordinal))
    return WindowPropertyAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-case=", StringComparison.Ordinal))
    return WindowLifecycleChecks.RunCase(args[0]);

// AppKit menu contracts run on the process main thread without displaying or
// activating any window. The native Window suite covers desktop transitions.
const int checkCount = 9;
if (args.Length == 0)
{
    // AppKit retains its registered Windows menu for the application's lifetime.
    // Each fixture needs a fresh NSApplication, so run cases in separate processes.
    int failedCases = 0;
    for (int index = 0; index < checkCount; index++)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add($"--menu-case={index}");
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            failedCases++;
            Console.Error.WriteLine($"FAIL: menu case {index} timed out");
        }
        else if (process.ExitCode != 0) failedCases++;
    }
    Console.WriteLine($"macOS host menu checks: {checkCount - failedCases}/{checkCount} passed");
    return failedCases == 0 ? 0 : 1;
}
if (args.Length != 1 || !args[0].StartsWith("--menu-case=", StringComparison.Ordinal)
    || !int.TryParse(args[0][12..], out int caseIndex) || caseIndex < 0 || caseIndex >= checkCount)
{
    Console.Error.WriteLine("Expected --menu-case=0 through --menu-case=8.");
    return 2;
}
JaliumMacApplication.Initialize();
var application = NSApplication.SharedApplication;
application.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
using var host = new MenuTestDelegate();
int failures = 0;
var checks = new (string Name, Action Run)[]
{
    ("null main menu gets Quit and Window actions", () =>
    {
        Reset(null);
        host.Configure(application);
        RequireQuit();
        RequireWindowActions();
    }),
    ("empty main menu gets an application menu", () =>
    {
        var menu = new NSMenu();
        Reset(menu);
        host.Configure(application);
        Require(application.MainMenu!.Handle == menu.Handle, "main menu was replaced");
        RequireQuit();
        RequireWindowActions();
    }),
    ("empty application submenu is filled and other menus survive", () =>
    {
        var appMenu = new NSMenu("Application");
        var fileMenu = new NSMenu("File");
        fileMenu.AddItem(new NSMenuItem("Custom File Action", new Selector("customFileAction:"), "n"));
        Reset(MainMenu(appMenu, fileMenu));
        host.Configure(application);
        Require(application.MainMenu!.Items[0].Submenu!.Handle == appMenu.Handle, "empty application menu was replaced");
        RequireQuit();
        Require(fileMenu.Items.Length == 1 && fileMenu.Items[0].Title == "Custom File Action", "custom menu changed");
    }),
    ("populated application submenu is preserved", () =>
    {
        var appMenu = new NSMenu("Custom Application");
        var custom = new NSMenuItem("Custom Quit", new Selector("customQuit:"), "q");
        appMenu.AddItem(custom);
        Reset(MainMenu(appMenu));
        host.Configure(application);
        Require(appMenu.Items.Length == 1 && appMenu.Items[0].Handle == custom.Handle, "custom application menu changed");
        RequireWindowActions();
    }),
    ("registered Window menu does not suppress default Quit", () =>
    {
        var appMenu = new NSMenu("Application");
        var windows = new NSMenu("Custom Windows");
        windows.AddItem(new NSMenuItem("Custom Window Action", new Selector("customWindowAction:"), string.Empty));
        Reset(MainMenu(appMenu, windows), windows);
        host.Configure(application);
        RequireQuit();
        Require(application.WindowsMenu!.Handle == windows.Handle && windows.Items.Length == 1, "registered Window menu changed");
    }),
    ("existing Window menu contents are preserved", () =>
    {
        var appMenu = new NSMenu("Application");
        var windows = new NSMenu("Window");
        var custom = new NSMenuItem("Custom Window Action", new Selector("customWindowAction:"), string.Empty);
        windows.AddItem(custom);
        Reset(MainMenu(appMenu, windows));
        host.Configure(application);
        RequireQuit();
        Require(application.WindowsMenu!.Handle == windows.Handle && windows.Items.Length == 1 && windows.Items[0].Handle == custom.Handle,
            "existing Window menu changed");
    }),
    ("registered Window-only menu gets a separate application menu", () =>
    {
        var windows = new NSMenu("Custom Windows");
        Reset(MainMenu(windows), windows);
        host.Configure(application);
        RequireQuit();
        Require(windows.Items.Length == 0 && application.WindowsMenu!.Handle == windows.Handle, "registered Window menu changed");
        Require(application.MainMenu!.Items[0].Submenu!.Handle != windows.Handle, "Quit was inserted into Window menu");
    }),
    ("named Window-only menu gets a separate application menu", () =>
    {
        var windows = new NSMenu("Window");
        Reset(MainMenu(windows));
        host.Configure(application);
        RequireQuit();
        Require(windows.Items.Length == 0 && application.WindowsMenu!.Handle == windows.Handle, "named Window menu changed");
        Require(application.MainMenu!.Items[0].Submenu!.Handle != windows.Handle, "Quit was inserted into Window menu");
    }),
    ("repeated configuration does not duplicate menus or actions", () =>
    {
        Reset(new NSMenu());
        host.Configure(application);
        var menu = application.MainMenu!;
        var windows = application.WindowsMenu!;
        int menuCount = menu.Items.Length;
        int windowItemCount = windows.Items.Length;
        int appItemCount = menu.Items[0].Submenu!.Items.Length;
        host.Configure(application);
        RequireQuit();
        Require(menu.Items.Length == menuCount && windows.Items.Length == windowItemCount &&
            menu.Items[0].Submenu!.Items.Length == appItemCount, "configuration duplicated items");
        Require(application.WindowsMenu!.Handle == windows.Handle, "configuration replaced Window menu");
    })
};

Require(checks.Length == checkCount, "menu smoke runner and fixture counts differ");
foreach (var (name, run) in checks.Skip(caseIndex).Take(1))
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
        Console.Error.WriteLine($"Before configuration: {host.BeforeConfiguration}");
        Console.Error.WriteLine("Main menu: " + string.Join(" | ", application.MainMenu?.Items.Select(item => $"{item.Title} -> {item.Submenu?.Title}") ?? []));
        Console.Error.WriteLine($"Window menu: {application.WindowsMenu?.Title}; items: " +
            string.Join(" | ", application.WindowsMenu?.Items.Select(item => $"{item.Title} [{item.Action?.Name}]") ?? []));
    }
}
return failures == 0 ? 0 : 1;

void Reset(NSMenu? menu, NSMenu? windows = null)
{
    // AppKit permits nil for MainMenu. The .NET binding rejects its null setter.
    NativeMenu.Set(application.Handle, new Selector("setMainMenu:").Handle, menu?.Handle ?? 0);
    if (windows != null) application.WindowsMenu = windows;
}

static NSMenu MainMenu(params NSMenu[] submenus)
{
    var menu = new NSMenu();
    // A menu item's title is also applied to its submenu by AppKit.
    foreach (var submenu in submenus) menu.AddItem(new NSMenuItem { Title = submenu.Title, Submenu = submenu });
    return menu;
}

void RequireQuit()
{
    var appMenu = application.MainMenu?.Items.FirstOrDefault()?.Submenu;
    var quit = appMenu?.Items.Where(item => item.Action?.Name == "terminate:").ToArray() ?? [];
    Require(quit.Length == 1, "application menu has no unique terminate: action");
    Require(quit[0].KeyEquivalent == "q" && quit[0].KeyEquivalentModifierMask == NSEventModifierMask.CommandKeyMask,
        "default Quit does not use Command-Q");
    foreach (var (action, key, modifiers) in new[]
    {
        ("hide:", "h", NSEventModifierMask.CommandKeyMask),
        ("hideOtherApplications:", "h", NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask),
        ("unhideAllApplications:", string.Empty, NSEventModifierMask.CommandKeyMask)
    })
    {
        var items = appMenu!.Items.Where(item => item.Action?.Name == action).ToArray();
        Require(items.Length == 1, $"application menu has no unique {action} action");
        Require(items[0].KeyEquivalent == key && items[0].KeyEquivalentModifierMask == modifiers,
            $"incorrect shortcut for {action}");
        Require(items[0].Target?.Handle == application.Handle, $"{action} does not target NSApplication");
    }
}

void RequireWindowActions()
{
    var windows = application.WindowsMenu;
    Require(windows != null, "Window menu is not registered");
    foreach (string action in new[] { "performMiniaturize:", "zoom:", "toggleFullScreen:", "performClose:", "arrangeInFront:" })
        Require(windows!.Items.Count(item => item.Action?.Name == action) == 1, $"missing or duplicate Window action {action}");
    var fullscreen = windows!.Items.Single(item => item.Action?.Name == "toggleFullScreen:");
    Require(fullscreen.KeyEquivalent == "f" && fullscreen.KeyEquivalentModifierMask == (NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask),
        "full screen does not use Control-Command-F");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

[Register("JaliumMacHostMenuTestDelegate")]
internal sealed class MenuTestDelegate : JaliumMacApplicationDelegate
{
    internal string BeforeConfiguration { get; private set; } = string.Empty;
    internal void Configure(NSApplication application)
    {
        BeforeConfiguration = $"windows={application.WindowsMenu?.Title}; " +
            string.Join(" | ", application.MainMenu?.Items.Select(item => $"{item.Title} -> {item.Submenu?.Title}") ?? []);
        ConfigureNativeWindowMenu(application);
    }
    protected override JaliumApp CreateHostedApp() => throw new NotSupportedException("The menu smoke test does not start a hosted application.");
}

internal static class NativeMenu
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    internal static extern void Set(nint receiver, nint selector, nint menu);
}
