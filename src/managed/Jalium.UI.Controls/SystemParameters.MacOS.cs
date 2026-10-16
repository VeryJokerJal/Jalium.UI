using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

public static partial class SystemParameters
{
    // Keep each platform's established SPI behavior. AppKit exposes one
    // accessibility preference for reducing substantial UI motion.
    private static bool GetAnimationPreference(uint action) => OperatingSystem.IsMacOS()
        ? !MacOSScrollBarSettings.PrefersReducedMotion
        : GetSpiBool(action, true);

    internal static void NotifyMacOSAnimationPreferencesChanged()
    {
        string[] names = [nameof(ClientAreaAnimation), nameof(ComboBoxAnimation),
            nameof(ListBoxSmoothScrolling), nameof(MenuAnimation), nameof(MinimizeAnimation),
            nameof(ToolTipAnimation), nameof(UIEffects), nameof(ComboBoxPopupAnimation),
            nameof(MenuPopupAnimation), nameof(ToolTipPopupAnimation)];
        // System keys are computed values. Refresh only their subscribers after
        // invalidating lookup caches; application dictionaries keep precedence.
        ResourceLookup.InvalidateResourceCache();
        DynamicResourceBindingOperations.RefreshForKeys(names.Select(GetResourceKey).ToHashSet<object>());
        foreach (string name in names)
            NotifyStaticPropertyChanged(name);
    }
}
