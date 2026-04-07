using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Factory for creating platform-specific implementations.
/// Uses RuntimeInformation to detect the current platform at startup.
/// </summary>
internal static class PlatformFactory
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool s_isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    // Android detection via runtime check
    private static readonly bool s_isAndroid = RuntimeInformation.RuntimeIdentifier?.Contains("android",
        StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>True if running on Windows.</summary>
    public static bool IsWindows => s_isWindows;

    /// <summary>True if running on Linux (desktop, not Android).</summary>
    public static bool IsLinux => s_isLinux && !s_isAndroid;

    /// <summary>True if running on Android.</summary>
    public static bool IsAndroid => s_isAndroid;

    /// <summary>
    /// Creates a platform window using the appropriate native backend.
    /// On Windows, this returns null — Window.cs continues to use its
    /// existing Win32 code path. On Linux/Android, returns a NativePlatformWindow.
    /// </summary>
    public static IPlatformWindow? CreateWindow(
        string title, int x, int y, int width, int height, uint style, nint parent)
    {
        if (s_isWindows)
        {
            // Windows uses the existing Win32 code path in Window.cs directly.
            // Return null to signal that the caller should use the legacy path.
            return null;
        }

        return new NativePlatformWindow(title, x, y, width, height, style, parent);
    }

    /// <summary>
    /// Initializes the native platform library. Must be called before
    /// creating any platform windows on non-Windows platforms.
    /// </summary>
    public static void InitializePlatform()
    {
        if (!s_isWindows)
        {
            int result = Interop.NativeMethods.PlatformInit();
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to initialize platform library (error {result}).");
        }
    }

    /// <summary>
    /// Runs the platform event loop. On Windows, this is a no-op
    /// (Window.cs handles the Win32 message loop directly).
    /// </summary>
    public static int RunMessageLoop()
    {
        if (s_isWindows)
            return 0; // Handled by existing Win32 code

        return Interop.NativeMethods.PlatformRunMessageLoop();
    }

    /// <summary>
    /// Signals the event loop to exit.
    /// </summary>
    public static void QuitMessageLoop(int exitCode = 0)
    {
        if (!s_isWindows)
            Interop.NativeMethods.PlatformQuit(exitCode);
    }
}
