using System.Runtime.InteropServices;

namespace Jalium.UI.Core.Platform;

/// <summary>
/// Non-blocking pump for the jalium.native.platform event queue (X11/Wayland on
/// Linux, ALooper on Android). Nested dispatcher frames (PushFrame,
/// DispatcherOperation.Wait) must keep draining window-system events while they
/// spin, otherwise input, resize, paint and IME stall for the whole nested wait.
/// </summary>
internal static partial class NativePlatformEventPump
{
    private const string PlatformLib = "jalium.native.platform";

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
}
