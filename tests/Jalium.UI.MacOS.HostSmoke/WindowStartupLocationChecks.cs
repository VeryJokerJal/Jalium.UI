using AppKit;
using CoreGraphics;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static class WindowStartupLocationChecks
{
    private const int Count = 14;
    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-startup-location-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: startup-location case timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window startup location host checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-startup-location-case=".Length), out int index) || (uint)index >= Count) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        NSApplication.SharedApplication.FinishLaunching();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window? owner = null;
        Window window = Create(index % 2 == 0, 420, 300);
        try
        {
            if (index < 4 || index is 8 or 9)
            {
                owner = Create(index < 4 ? index / 2 == 0 : true, 680, 560);
                owner.Show(); Pump(owner);
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                CGRect work = Native(owner).Screen!.VisibleFrame;
                if (index is 8 or 9)
                {
                    owner.Left = (double)(work.X + work.Width - 100);
                    owner.Top = (double)(NSScreen.Screens[0].Frame.GetMaxY() - work.GetMaxY() + 80);
                }
                CGRect parent = Native(owner).Frame;
                window.Show(); Pump(window);
                CGRect actual = Native(window).Frame;
                if (index < 4) Near(Center(actual), Center(parent), "outer frames of mixed title bars");
                else Require(actual.X >= work.X - .01 && actual.GetMaxX() <= work.GetMaxX() + .01 &&
                    actual.Y >= work.Y - .01 && actual.GetMaxY() <= work.GetMaxY() + .01,
                    $"owner-centered frame is outside the work area: {actual} / {work}");
            }
            else if (index is 10 or 11)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Show(); Pump(window);
                double scale = (double)Native(window).BackingScaleFactor;
                var topLeft = new CGPoint(Native(window).Frame.X, NSScreen.Screens[0].Frame.GetMaxY() - Native(window).Frame.GetMaxY());
                Near(topLeft, new CGPoint(200 / scale, 180 / scale), "ownerless CenterOwner preserves manual position");
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (index is 12 or 13) window.SourceInitialized += (_, _) => window.WindowState = WindowState.Maximized;
                window.Show(); Pump(window);
                if (index is 12 or 13) { window.WindowState = WindowState.Normal; Pump(window); }
                if (index is 6 or 7)
                {
                    window.Left += 137; window.Top += 89; Pump(window);
                    CGRect moved = Native(window).Frame;
                    window.Hide(); window.Show(); Pump(window);
                    Near(Center(Native(window).Frame), Center(moved), "Hide/Show preserves the moved window");
                }
                else Near(Center(Native(window).Frame), Center(Native(window).Screen!.VisibleFrame),
                    index < 12 ? "outer frame is centered in screen work area" : "SourceInitialized maximize preserves centered restore frame");
            }
            CGRect finalFrame = Native(window).Frame;
            Near(new CGPoint(window.Left, window.Top),
                new CGPoint(finalFrame.X, NSScreen.Screens[0].Frame.GetMaxY() - finalFrame.GetMaxY()),
                "managed position agrees with the real outer frame");
            Console.WriteLine($"PASS {index}: startup placement and native geometry");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error.Message}"); return 1; }
        finally { window.Close(); owner?.Close(); }
    }

    private static Window Create(bool native, double width, double height) => new()
    {
        Title = "窗口初始定位验证", Left = 200, Top = 180, Width = width, Height = height,
        TitleBarStyle = native ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
        ShowActivated = false, Content = new TextBox { Text = "定位后保留编辑🙂", Margin = new Thickness(24), Height = 48 },
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
    };
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
    private static CGPoint Center(CGRect rect) => new(rect.GetMidX(), rect.GetMidY());
    private static void Near(CGPoint actual, CGPoint expected, string message) => Require(
        Math.Abs((double)(actual.X - expected.X)) <= 1 && Math.Abs((double)(actual.Y - expected.Y)) <= 1,
        $"{message}: actual={actual}, expected={expected}");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(Window window)
    {
        Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
        window.UpdateLayout(); Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
    }
}
