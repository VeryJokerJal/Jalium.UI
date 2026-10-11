using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;

namespace Jalium.UI.MacOS;

internal static class WindowSystemMenuChecks
{
    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < 4; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-system-menu-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL: Window menu case {index} timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window system menu host checks: {passed}/4 passed");
        return passed == 4 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-system-menu-case=".Length), out int index) || (uint)index >= 4) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        NSApplication.SharedApplication.FinishLaunching();
        NativeMethods.PlatformPollEvents();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new TextBox { Text = "菜单返回后保留编辑🙂", Height = 48 };
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(editor);
        var window = new Window
        {
            Title = "Window 菜单与坐标验证", Left = 300, Top = 220, Width = 560, Height = 420,
            TitleBarStyle = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            Content = panel, ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            window.Show(); Pump(window);
            if (index / 2 == 0) CheckCoordinates(window, editor);
            else CheckMenu(window, editor);
            Console.WriteLine($"PASS {index % 2}: {(index / 2 == 0 ? "client and screen coordinates" : "public menu, caption and cancellation")}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally { window.Close(); }
    }

    private static void CheckCoordinates(Window window, TextBox editor)
    {
        WindowTitleBarStyle original = window.TitleBarStyle;
        foreach (var style in new[] { original, original == WindowTitleBarStyle.Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom })
        {
            window.TitleBarStyle = style;
            window.Left += 31; window.Top += 23; Pump(window);
            NSView view = Runtime.GetNSObject<NSView>(window.Handle)!;
            NSWindow native = view.Window!;
            CGPoint nativeOrigin = native.ConvertPointToScreen(view.ConvertPointToView(CGPoint.Empty, null));
            double top = (double)NSScreen.Screens[0].Frame.GetMaxY();
            double scale = (double)native.BackingScaleFactor;
            var expectedOrigin = new Point(Math.Round((double)nativeOrigin.X * scale), Math.Round((top - (double)nativeOrigin.Y) * scale));
            Require(((IWindowHost)window).TryGetClientOriginOnScreen(out Point actualOrigin), "client origin is unavailable");
            Near(actualOrigin, expectedOrigin, "actual AppKit client origin");
            var local = new Point(37, 49);
            Near(window.PointToScreen(local), new Point(expectedOrigin.X + local.X * scale, expectedOrigin.Y + local.Y * scale), "Window screen conversion");
            Near(window.PointFromScreen(window.PointToScreen(local)), local, "Window round trip");
            var childPoint = new Point(7, 9);
            Point rootPoint = ((Visual)editor).TransformToAncestor((Visual)window).Transform(childPoint);
            Near(editor.PointToScreen(childPoint), new Point(expectedOrigin.X + rootPoint.X * scale, expectedOrigin.Y + rootPoint.Y * scale), "descendant screen conversion");
            Near(editor.PointFromScreen(editor.PointToScreen(childPoint)), childPoint, "descendant round trip");
        }
    }

    private static void CheckMenu(Window window, TextBox editor)
    {
        int began = 0, ended = 0, closeRequests = 0;
        var cancellationTimers = new List<NSTimer>();
        window.Closing += (_, args) => { closeRequests++; args.Cancel = true; };
        using var begin = NSNotificationCenter.DefaultCenter.AddObserver(NSMenu.DidBeginTrackingNotification, note =>
        {
            began++;
            if (note.Object is NSMenu menu)
            {
                // BeginTracking is delivered before the native tracking loop
                // is ready to cancel. Run the cancellation inside that loop.
                var timer = NSTimer.CreateTimer(.01, _ => menu.CancelTrackingWithoutAnimation());
                cancellationTimers.Add(timer);
                NSRunLoop.Current.AddTimer(timer, NSRunLoopMode.EventTracking);
            }
        });
        using var end = NSNotificationCenter.DefaultCenter.AddObserver(NSMenu.DidEndTrackingNotification, _ => ended++);
        Point screen = window.PointToScreen(new Point(48, 80));
        SystemCommands.ShowSystemMenu(window, screen);
        Require(began == 1 && ended == 1 && closeRequests == 0, "public API did not track and cancel a native menu");
        Require(editor.Text == "菜单返回后保留编辑🙂" && window.Handle != 0, "menu cancellation lost content or closed target");
        Require(typeof(Window).GetField("_fallbackSystemMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is null,
            "macOS menu used the managed fallback");
        window.HasSystemMenu = false;
        SystemCommands.ShowSystemMenu(window, screen);
        Require(began == 1, "HasSystemMenu=false opened a menu");
        window.HasSystemMenu = true;
        window.IsEnabled = false; SystemCommands.ShowSystemMenu(window, screen); window.IsEnabled = true;
        window.Hide(); SystemCommands.ShowSystemMenu(window, screen); window.Show(); Pump(window);
        Require(began == 1, "hidden or disabled window opened a menu");
        if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            var dispatch = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            dispatch.Invoke(window, [new PlatformEvent { Type = PlatformEventType.MouseUp, Button = 1,
                MouseX = (float)(120 * ((IWindowHost)window).DpiScale), MouseY = (float)(15 * ((IWindowHost)window).DpiScale) }]);
            Require(began == 2 && ended == 2, "custom caption right-click did not open a native menu");
        }
    }

    private static void Pump(Window window)
    {
        Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
        window.UpdateLayout(); Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(Point actual, Point expected, string description)
    {
        Require(Math.Abs(actual.X - expected.X) < .01 && Math.Abs(actual.Y - expected.Y) < .01,
            $"{description}: actual={actual}, expected={expected}");
    }
}
