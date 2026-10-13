using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jalium.UI.MacOS;

internal static class WindowFullScreenFailureChecks
{
    private const int Count = 16;

    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-fullscreen-failure-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: fullscreen failure recovery timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window fullscreen failure host checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-fullscreen-failure-case=".Length), out int index) ||
            (uint)index >= Count) return 2;
        bool nativeTitleBar = (index & 1) == 0, maximized = (index & 2) != 0;
        int scenario = index >> 2;
        WindowState initial = maximized ? WindowState.Maximized : WindowState.Normal;
        WindowState target = maximized ? WindowState.Normal : WindowState.Maximized;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        NSApplication.SharedApplication.FinishLaunching();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var editor = new TextBox { Text = "全屏失败后保留中文🙂", Margin = new Thickness(24), Height = 48 };
        var window = new Window
        {
            Title = "全屏失败恢复验证", Left = 200, Top = 180, Width = 640, Height = 480,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            ShowActivated = false, Content = editor, Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        Window? replacement = null;
        try
        {
            window.Show(); Pump(window); editor.Focus();
            var native = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
            var nativeDelegate = native.WeakDelegate ?? throw new InvalidOperationException("the owned native window has no delegate");
            Rect restore = window.RestoreBounds;
            if (maximized) { window.WindowState = initial; Pump(window); }
            Require(native.MakeFirstResponder(Runtime.GetNSObject<NSView>(window.Handle)),
                "the owned input view refused initial focus");
            // Replay the owned delegate's failure contract. This is not an
            // induced OS failure or evidence of a real Spaces transition.
            using var notification = NSNotification.FromName(NSWindow.WillEnterFullScreenNotification, native);
            SendDelegate(nativeDelegate.Handle, Selector.GetHandle("windowWillEnterFullScreen:"), notification.Handle);
            // The real desktop probe observed AppKit resetting firstResponder
            // to NSWindow while the managed TextBox remained focused.
            native.MakeFirstResponder(null);
            bool callback = false, premature = false;
            if (scenario == 1 || scenario == 3)
            {
                window.StateChanged += (_, _) =>
                {
                    if (callback || window.WindowState != initial) return;
                    callback = true;
                    if (scenario == 3)
                    {
                        window.Close();
                        replacement = new Window
                        {
                            Title = "全屏失败替代窗口", Left = 400, Top = 300, Width = 520, Height = 360,
                            TitleBarStyle = window.TitleBarStyle, ShowActivated = false,
                            Content = new TextBox { Text = "替代窗口内容🙂" }, Background = window.Background
                        };
                        replacement.Show();
                    }
                    else
                    {
                        var frame = native.Frame;
                        window.WindowState = target;
                        premature = native.Frame != frame;
                    }
                };
            }
            bool accepted = false;
            if (scenario == 2) accepted = window.Activate();
            else window.WindowState = target;
            SendDelegate(nativeDelegate.Handle, Selector.GetHandle("windowDidFailToEnterFullScreen:"), native.Handle);
            if (scenario == 3)
            {
                Require(Wait(window, () => replacement is not null), "failure did not reach the close callback");
                Pump(window);
                Require(window.Handle == 0 && replacement!.WindowState == WindowState.Normal &&
                    replacement.Width == 520 && replacement.Height == 360 &&
                    ((TextBox)replacement.Content!).Text == "替代窗口内容🙂", "failure completion changed the replacement");
            }
            else
            {
                WindowState expected = scenario == 2 ? initial : target;
                Require(Wait(window, () => window.WindowState == expected &&
                    native.IsZoomed == (expected == WindowState.Maximized)), "failure lost the managed/native state request");
                Require(scenario != 1 || callback && !premature, "failure callback changed the frame before unwinding");
                if (scenario == 2) Require(accepted && native.FirstResponder == Runtime.GetNSObject<NSView>(window.Handle),
                    "failure did not retain the real input responder");
                Require(window.RestoreBounds == restore && ReferenceEquals(window.Content, editor) &&
                    editor.Text == "全屏失败后保留中文🙂", "failure changed restore bounds or editor content");
                window.WindowState = WindowState.Normal; Pump(window); editor.Focus();
                Require(Math.Abs(window.Width - restore.Width) <= 1 && Math.Abs(window.Height - restore.Height) <= 1 &&
                    editor.IsKeyboardFocused, "failure prevented normal restoration or managed editing focus");
            }
            Console.WriteLine($"PASS {index}: {window.TitleBarStyle}, initial={initial}, scenario={scenario}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally { replacement?.Close(); window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendDelegate(nint receiver, nint selector, nint argument);
    private static bool Wait(Window window, Func<bool> condition)
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
