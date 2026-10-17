using System.Runtime.InteropServices;

namespace Jalium.UI.Core.Platform;

/// <summary>
/// Non-blocking pump for the jalium.native.platform event queue (X11/Wayland on
/// Linux, AppKit on macOS, ALooper on Android). Nested dispatcher frames (PushFrame,
/// DispatcherOperation.Wait) must keep draining window-system events while they
/// spin, otherwise input, resize, paint and IME stall for the whole nested wait.
/// </summary>
internal static partial class NativePlatformEventPump
{
    private const string PlatformLib = JaliumNativeLibraryNames.Platform;

    private static volatile bool s_unavailable;

    /// <summary>
    /// Drains pending platform events without blocking. Safe to call when the
    /// native platform library is absent (headless unit tests, design tools):
    /// the first failure latches and later calls become no-ops.
    /// </summary>
    public static void PollEvents()
    {
        if (s_unavailable)
            return;

        // A secondary dispatcher owns its managed work, not NSApplication's
        // event queue. Do not enter AppKit or latch a native-load failure here.
        if (OperatingSystem.IsMacOS() && PThreadMain() == 0)
            return;

        try
        {
            _ = PlatformPollEvents();
        }
        catch (DllNotFoundException)
        {
            s_unavailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            s_unavailable = true;
        }
    }

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_poll_events")]
    private static partial int PlatformPollEvents();

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pthread_main_np")]
    private static partial int PThreadMain();
}
