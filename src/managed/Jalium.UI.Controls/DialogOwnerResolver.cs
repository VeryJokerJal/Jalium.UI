using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

internal static class DialogOwnerResolver
{
    internal static nint Resolve(nint explicitOwner = default)
    {
        if (explicitOwner != nint.Zero)
        {
            return explicitOwner;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var activeWindowHandle = GetActiveWindow();
            if (activeWindowHandle != nint.Zero)
            {
                return activeWindowHandle;
            }
        }

        var mainWindowHandle = Jalium.UI.Application.Current?.MainWindow?.Handle ?? nint.Zero;
        if (mainWindowHandle != nint.Zero)
        {
            return mainWindowHandle;
        }

        return nint.Zero;
    }

    internal static Window? ResolveWindow(Window? explicitOwner = null)
    {
        if (explicitOwner != null)
        {
            return explicitOwner;
        }

        var ownerHandle = Resolve();
        if (ownerHandle != nint.Zero && Window.TryGetOpenWindow(ownerHandle) is { } openWindow)
        {
            return openWindow;
        }

        return Jalium.UI.Application.Current?.MainWindow;
    }

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();
}
