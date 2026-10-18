using AppKit;
using Foundation;
using Jalium.UI.Controls.Platform;
using ObjCRuntime;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class MacOSWindowThemes
{
    private static uint _scheme = 2;
    private static int _leases;
    private static int _generation;
    private static IDisposable? _appearanceObserver;
    private static NSObject? _colorsObserver;
    private static NSObject? _accessibilityObserver;
    private static bool _increaseContrast;
    private static bool _reduceTransparency;
    private static bool _pending;
    private static bool _forceNotification;

    internal static void Configure()
    {
        PlatformThemeIntegration.ReadColorScheme = () => Volatile.Read(ref _scheme);
        PlatformThemeIntegration.ReadHighContrast = () => Volatile.Read(ref _increaseContrast);
        PlatformThemeIntegration.ApplyWindowAppearance = Apply;
        bool changed = ReadPreferences();
        PlatformThemeIntegration.MonitorAppearance = Monitor;
        Application.Current?.EnsurePlatformAppearanceMonitoring();
        foreach (var window in Window.SnapshotOpenWindows())
            window.EnsurePlatformAppearanceMonitoring();
        if (changed && _leases != 0) ScheduleRefresh(_generation, forceNotification: true);
    }

    internal static IDisposable Monitor()
    {
        if (_leases++ == 0)
        {
            try
            {
                int generation = ++_generation;
                bool changed = ReadPreferences();
                var app = NSApplication.SharedApplication;
                _appearanceObserver = app.AddObserver("effectiveAppearance", NSKeyValueObservingOptions.New,
                    _ => ScheduleRefresh(generation));
                _colorsObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSColor.SystemColorsChanged,
                    _ => ScheduleRefresh(generation));
                _accessibilityObserver = NSWorkspace.SharedWorkspace.NotificationCenter.AddObserver(
                    new NSString("NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification"),
                    _ => ScheduleRefresh(generation));
                ScheduleRefresh(generation, forceNotification: changed);
            }
            catch
            {
                new MonitorLease().Dispose();
                throw;
            }
        }
        return new MonitorLease();
    }

    private static void ScheduleRefresh(int generation, bool forceNotification = false)
    {
        var app = NSApplication.SharedApplication;
        app.BeginInvokeOnMainThread(() =>
        {
            if (generation != _generation || _leases == 0) return;
            _forceNotification |= forceNotification;
            if (_pending) return;
            _pending = true;
            app.BeginInvokeOnMainThread(() =>
            {
                if (generation != _generation || _leases == 0) return;
                _pending = false;
                bool notify = _forceNotification;
                _forceNotification = false;
                if (ReadPreferences() || notify)
                    SystemParameters.NotifyPlatformAppearanceChanged(Volatile.Read(ref _scheme));
            });
        });
    }

    private static bool ReadPreferences()
    {
        var appearance = NSApplication.SharedApplication.EffectiveAppearance;
        string? match = appearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]);
        uint scheme = match == NSAppearance.NameDarkAqua ? 1u : 2u;
        var workspace = NSWorkspace.SharedWorkspace;
        bool contrast = workspace.AccessibilityDisplayShouldIncreaseContrast;
        bool transparency = workspace.AccessibilityDisplayShouldReduceTransparency;
        bool changed = scheme != _scheme || contrast != _increaseContrast || transparency != _reduceTransparency;
        Volatile.Write(ref _scheme, scheme);
        _increaseContrast = contrast;
        _reduceTransparency = transparency;
        return changed;
    }

    private static void Apply(Window window, object? themeKey)
    {
        var view = Runtime.GetNSObject<NSView>(window.Handle);
        if (view?.Window is not { } native) return;
        string? name = themeKey switch
        {
            "Light" => NSAppearance.NameAqua,
            "Dark" => NSAppearance.NameDarkAqua,
            "HighContrast" => _scheme == 1 ? NSAppearance.NameAccessibilityHighContrastDarkAqua : NSAppearance.NameAccessibilityHighContrastAqua,
            _ => null
        };
        if (native.Appearance?.Name == name) return;
        NSAppearance? requested = null;
        if (name != null)
        {
            using var nativeName = new NSString(name);
            requested = NSAppearance.GetAppearance(nativeName);
        }
        native.Appearance = requested;
    }

    private sealed class MonitorLease : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--_leases != 0) return;
            ++_generation;
            _pending = false;
            _forceNotification = false;
            _appearanceObserver?.Dispose();
            _appearanceObserver = null;
            if (_colorsObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_colorsObserver);
                _colorsObserver.Dispose();
                _colorsObserver = null;
            }
            if (_accessibilityObserver != null)
            {
                NSWorkspace.SharedWorkspace.NotificationCenter.RemoveObserver(_accessibilityObserver);
                _accessibilityObserver.Dispose();
                _accessibilityObserver = null;
            }
        }
    }
}
