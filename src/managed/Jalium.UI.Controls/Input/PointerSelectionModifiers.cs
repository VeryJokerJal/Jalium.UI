using Jalium.UI.Input;

namespace Jalium.UI.Controls;

internal static class PointerSelectionModifiers
{
    // Pointer events retain physical Command separately from Control on macOS.
    // Keep Control compatibility for existing framework and synthetic events.
    internal static bool IsToggle(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Control) != 0 ||
        OperatingSystem.IsMacOS() && (modifiers & ModifierKeys.Windows) != 0;
}
