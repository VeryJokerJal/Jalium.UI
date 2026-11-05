using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Threading;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static class AccessibilityFocusChecks
{
    internal static bool Wait(Window window, UIElement target)
    {
        // Native AX focus deliberately leaves the AX callback before activating
        // its physical window. Drain the real queue before asserting the result.
        var elapsed = Stopwatch.StartNew();
        while (!target.IsKeyboardFocused && elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            NativeMethods.PlatformPollEvents();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            window.UpdateLayout();
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
        }
        return target.IsKeyboardFocused;
    }
}
