using Jalium.UI.Input;

namespace Jalium.UI.Controls;

internal static class MouseInputModifiers
{
    internal static ModifierKeys ToLogical(ModifierKeys physical) =>
        OperatingSystem.IsMacOS() && (physical & ModifierKeys.Windows) != 0 &&
        (physical & ModifierKeys.Control) == 0
            ? (physical & ~ModifierKeys.Windows) | ModifierKeys.Control
            : physical;
}
