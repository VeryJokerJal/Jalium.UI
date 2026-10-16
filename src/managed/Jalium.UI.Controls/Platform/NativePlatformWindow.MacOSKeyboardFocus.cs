using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

internal sealed partial class NativePlatformWindow : IPlatformKeyboardFocus
{
    bool IPlatformKeyboardFocus.TrySetKeyboardFocus() => OperatingSystem.IsMacOS() &&
        _handle != nint.Zero && NativeMethods.AppleWindowSetKeyboardFocus(_handle) == 0;
}
