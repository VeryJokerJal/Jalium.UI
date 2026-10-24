using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI;

public partial class Window
{
    internal override object? GetResourceThemeKeyOverride()
    {
        // High contrast remains an application-wide accessibility override.
        if (Equals(ResourceDictionary.CurrentThemeKey, "HighContrast"))
            return null;
#pragma warning disable WPF0001
        if (_themeMode == ThemeMode.Light) return "Light";
        if (_themeMode == ThemeMode.Dark) return "Dark";
        if (_themeMode == ThemeMode.System)
            return SystemParameters.PrefersDarkColorScheme ? "Dark" : "Light";
#pragma warning restore WPF0001
        return null;
    }

    private void UpdateWindowThemeResources()
    {
        ResourceLookup.InvalidateResourceCache();
        RefreshRoot(this);
        // A native popup is a separate visual root, but its resources follow its
        // placement target. Ordinary owned windows keep their own application scope.
        foreach (var window in SnapshotOpenWindows())
            if (!ReferenceEquals(window, this) && window.IsMacOSPopupHost &&
                window.Content is FrameworkElement content && OwnsThemeScope(content))
                RefreshRoot(window);
        foreach (var popup in PopupWindow.SnapshotOpenPopupWindows())
            if (OwnsThemeScope(popup)) RefreshRoot(popup);
        InvalidateVisual();

        static void RefreshRoot(FrameworkElement root)
        {
            try { root.NotifyThemeResourcesChangedFromRoot(); }
            catch (Exception error)
            { System.Diagnostics.Debug.WriteLine($"Jalium.UI: window theme notification failed: {error}"); }
            finally { if (root is Window window) window.ApplyWindowThemeAppearance(); }
        }
    }

    private bool OwnsThemeScope(FrameworkElement element)
    {
        FrameworkElement? current = element;
        for (int depth = 0; current != null && depth < 2048; depth++)
        {
            if (ReferenceEquals(current, this)) return true;
            if (current is Window { IsMacOSPopupHost: false }) return false;
            var redirected = ResourceLookup.AncestorRedirectLookup?.Invoke(current);
            current = redirected != null && !ReferenceEquals(redirected, current) ? redirected : current.FrameworkParent;
        }
        return false;
    }

    internal static void RefreshAppearanceWithoutApplication()
    {
        foreach (var window in SnapshotOpenWindows()) window.UpdateWindowThemeResources();
    }

    internal void EnsurePlatformAppearanceMonitoring()
    {
        if (_platformWindow is NativePlatformWindow native) native.EnsureAppearanceMonitoring();
    }

    private void OnWindowThemeResourcesChanged(object? sender, EventArgs args) => ApplyWindowThemeAppearance();

    private void ApplyWindowThemeAppearance()
    {
        if (!OperatingSystem.IsMacOS() || Handle == 0) return;
        using var scope = ResourceLookup.EnterThemeLookup(Content as FrameworkElement ?? this);
        PlatformThemeIntegration.ApplyWindowAppearance?.Invoke(this, scope.ThemeKey);
    }
}
