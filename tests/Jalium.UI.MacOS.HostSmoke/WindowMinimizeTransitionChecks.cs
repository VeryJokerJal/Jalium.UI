using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static class WindowMinimizeTransitionChecks
{
    private const int Count = 24;

    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-minimize-transition-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: minimize transition timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window minimize transition host checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-minimize-transition-case=".Length), out int index) || (uint)index >= Count) return 2;
        bool nativeTitleBar = (index & 1) == 0, initiallyMaximized = (index & 2) != 0;
        bool activation = index >= 16, completionActivation = activation && (index & 4) != 0;
        bool nativeAction = !activation && (index & 8) != 0;
        WindowState requested = activation ? (initiallyMaximized ? WindowState.Maximized : WindowState.Normal)
            : (index & 4) == 0 ? WindowState.Normal : WindowState.Maximized;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        NSApplication.SharedApplication.FinishLaunching();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var editor = new TextBox { Text = "最小化后保留中文编辑🙂", Margin = new Thickness(24), Height = 48 };
        var window = new Window
        {
            Title = "最小化队列验证", Left = 200, Top = 180, Width = 640, Height = 480,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            ShowActivated = false, Content = editor, Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        NSObject? began = null, completed = null;
        try
        {
            window.Show(); Pump(window);
            NSWindow native = Native(window);
            Rect restore = window.RestoreBounds;
            if (initiallyMaximized) { window.WindowState = WindowState.Maximized; Pump(window); }
            int willCount = 0, didCount = 0;
            bool activationAccepted = false;
            began = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillMiniaturizeNotification, _ =>
            {
                if (++willCount != 1) return;
                if (activation)
                {
                    if (!completionActivation) activationAccepted = window.Activate();
                    return;
                }
                window.WindowState = WindowState.Normal;
                window.WindowState = WindowState.Maximized;
                window.WindowState = requested;
            }, native);
            completed = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.DidMiniaturizeNotification, _ =>
            {
                didCount++;
                if (completionActivation) activationAccepted = window.Activate();
            }, native);
            if (nativeAction) native.PerformMiniaturize(null);
            else window.WindowState = WindowState.Minimized;
            Require(WaitUntil(window, () => didCount > 0 && window.WindowState == requested && !native.IsMiniaturized),
                $"latest request was lost: managed={window.WindowState}, miniaturized={native.IsMiniaturized}, will={willCount}, did={didCount}");
            Require(native.IsZoomed == (requested == WindowState.Maximized),
                "managed/native state differs after minimization completed");
            Require(willCount == 1 && didCount == 1 && window.RestoreBounds == restore,
                $"minimization restarted or changed normal geometry: {window.RestoreBounds} / {restore}");
            if (activation) Require(activationAccepted && native.FirstResponder == Runtime.GetNSObject<NSView>(window.Handle),
                "restored window did not accept activation or recover its real input responder");
            window.WindowState = WindowState.Normal; Pump(window);
            Near(new CGPoint(window.Left, window.Top), new CGPoint(restore.X, restore.Y), "normal position");
            Near(new CGPoint(window.Width, window.Height), new CGPoint(restore.Width, restore.Height), "normal client size");
            CGRect frame = native.Frame;
            Near(new CGPoint(window.Left, window.Top), new CGPoint(frame.X, NSScreen.Screens[0].Frame.GetMaxY() - frame.GetMaxY()),
                "managed position agrees with real outer frame");
            Require(ReferenceEquals(window.Content, editor) && editor.Text == "最小化后保留中文编辑🙂", "minimization replaced content");
            Console.WriteLine($"PASS {index}: {window.TitleBarStyle}, initial={(initiallyMaximized ? "Maximized" : "Normal")}, latest={requested}, entry={(activation ? (completionActivation ? "Activate on completion" : "Activate on start") : nativeAction ? "native" : "managed")}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally
        {
            if (began is not null) { NSNotificationCenter.DefaultCenter.RemoveObserver(began); began.Dispose(); }
            if (completed is not null) { NSNotificationCenter.DefaultCenter.RemoveObserver(completed); completed.Dispose(); }
            window.Close();
        }
    }

    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(CGPoint actual, CGPoint expected, string message) => Require(
        Math.Abs((double)(actual.X - expected.X)) <= 1 && Math.Abs((double)(actual.Y - expected.Y)) <= 1,
        $"{message}: actual={actual}, expected={expected}");
    private static bool WaitUntil(Window window, Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(8))
        {
            Pump(window);
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        }
        Pump(window); return condition();
    }
    private static void Pump(Window window)
    {
        Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
        window.UpdateLayout(); Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
    }
}
