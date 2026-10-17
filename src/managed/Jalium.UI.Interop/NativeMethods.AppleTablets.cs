using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal static partial class NativeMethods
{
    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_apple_window_set_tablet_callback")]
    internal static partial int AppleWindowSetTabletCallback(nint window, nint callback, nint userData);
}
