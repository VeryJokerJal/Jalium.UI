using System.Runtime.InteropServices;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Platform;

internal static partial class MacOSScrollBarSettings
{
    private static bool? s_prefersOverlayScrollBars;
    private static bool? s_prefersReducedMotion;

    internal static bool PrefersOverlayScrollBars =>
        s_prefersOverlayScrollBars ??= ReadPreferredStyle();

    internal static bool PrefersReducedMotion => s_prefersReducedMotion ??= ReadReducedMotion();

    private static bool ReadReducedMotion()
    {
        if (!OperatingSystem.IsMacOS())
            return false;
        try { return PlatformPrefersReducedMotion() != 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static bool ReadPreferredStyle()
    {
        if (!OperatingSystem.IsMacOS())
            return false;
        try
        {
            return PlatformPrefersOverlayScrollBars() != 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    internal static void Refresh(Visual root)
    {
        s_prefersOverlayScrollBars = ReadPreferredStyle();
        s_prefersReducedMotion = ReadReducedMotion();
        ApplyToTree(root);
    }

    private static void ApplyToTree(Visual visual)
    {
        if (visual is ScrollViewer viewer)
            viewer.ApplyMacOSScrollBarPreferences(PrefersOverlayScrollBars);
        else if (visual is Primitives.ScrollBar scrollBar)
            scrollBar.ApplyMacOSScrollBarPreferences(PrefersOverlayScrollBars);

        int count = VisualTreeHelper.GetChildrenCount(visual);
        for (int index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                ApplyToTree(child);
        }
    }

    internal static void ApplyDefault(DependencyObject target, DependencyProperty property, bool value)
    {
        // Keep local values, bindings, template values and stylesheet choices authoritative.
        if (target.GetEffectiveValueLayer(property) is null or DependencyValueStore.Layer.Current)
            target.SetCurrentValue(property, value);
    }

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_platform_prefers_overlay_scrollbars")]
    private static partial int PlatformPrefersOverlayScrollBars();

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_platform_prefers_reduced_motion")]
    private static partial int PlatformPrefersReducedMotion();
}
