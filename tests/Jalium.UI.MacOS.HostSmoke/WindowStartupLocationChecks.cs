using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static class WindowStartupLocationChecks
{
    private const int Count = 20;
    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.Environment.Remove("JALIUM_MACOS_STARTUP_OBSERVE_SECONDS");
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
        int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_STARTUP_OBSERVE_SECONDS"), out int seconds);
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = seconds > 0
            ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Accessory;
        NSApplication.SharedApplication.FinishLaunching();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window? owner = null;
        (int width, int height) = index < 14 ? (420, 300) : ((index - 14) / 2) switch
        {
            0 => (421, 301), 1 => (420, 301), _ => (421, 300)
        };
        Window window = Create(index % 2 == 0, width, height);
        application.MainWindow = window;
        CGRect initialFrame = default;
        bool initialized = false;
        window.SourceInitialized += (_, _) => { initialFrame = Native(window).Frame; initialized = true; };
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
                Require(initialized, "startup frame was not captured before placement");
                if (index < 4) VerifyFrame(actual, AppKitPlacement(window, initialFrame, parent, work),
                    "outer frames of mixed title bars");
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
                NSScreen selected = StartupScreen();
                CGRect work = selected.VisibleFrame;
                if (index is 12 or 13) window.SourceInitialized += (_, _) => window.WindowState = WindowState.Maximized;
                window.Show(); Pump(window);
                Require(initialized, "startup frame was not captured before placement");
                if (index is 12 or 13) { window.WindowState = WindowState.Normal; Pump(window); }
                if (index is 6 or 7)
                {
                    window.Left += 137; window.Top += 89; Pump(window);
                    CGRect moved = Native(window).Frame;
                    window.Hide(); window.Show(); Pump(window);
                    VerifyFrame(Native(window).Frame, moved, "Hide/Show preserves the moved window");
                }
                else VerifyFrame(Native(window).Frame, AppKitPlacement(window, initialFrame, work, work),
                    index is 12 or 13 ? "SourceInitialized maximize preserves centered restore frame"
                        : "outer frame is centered at AppKit's native pixel alignment");
            }
            CGRect finalFrame = Native(window).Frame;
            Near(new CGPoint(window.Left, window.Top),
                new CGPoint(finalFrame.X, NSScreen.Screens[0].Frame.GetMaxY() - finalFrame.GetMaxY()),
                "managed position agrees with the real outer frame");
            Console.WriteLine($"PASS {index}: startup placement and native geometry; frame={finalFrame}; scale={Native(window).BackingScaleFactor}");
            if (seconds > 0) Observe(window, seconds);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error.Message}"); return 1; }
        finally { window.Close(); owner?.Close(); application.Shutdown(); }
    }

    private static Window Create(bool native, double width, double height) => new()
    {
        Title = "窗口初始定位验证", Left = 200, Top = 180, Width = width, Height = height,
        TitleBarStyle = native ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
        ShowActivated = false, Content = new TextBox { Text = "定位后保留编辑🙂", Margin = new Thickness(24), Height = 48 },
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
    };
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
    private static NSScreen StartupScreen()
    {
        CGPoint point = NSEvent.CurrentMouseLocation;
        NSScreen? nearest = null;
        double distance = double.PositiveInfinity;
        foreach (NSScreen screen in NSScreen.Screens)
        {
            CGRect frame = screen.Frame;
            if (screen.VisibleFrame.IsEmpty) continue;
            if (frame.Contains(point)) return screen;
            double dx = Math.Max(Math.Max((double)(frame.X - point.X), 0), (double)(point.X - frame.GetMaxX()));
            double dy = Math.Max(Math.Max((double)(frame.Y - point.Y), 0), (double)(point.Y - frame.GetMaxY()));
            double candidate = dx * dx + dy * dy;
            if (candidate < distance) { nearest = screen; distance = candidate; }
        }
        return nearest ?? throw new InvalidOperationException("no real display is available for startup placement");
    }
    private static CGRect AppKitPlacement(Window window, CGRect normalFrame, CGRect reference, CGRect work)
    {
        double width = (double)normalFrame.Width, height = (double)normalFrame.Height;
        double x = width > (double)work.Width ? (double)work.X : Math.Clamp(
            (double)reference.GetMidX() - width / 2, (double)work.X, (double)work.GetMaxX() - width);
        double y = height > (double)work.Height ? (double)work.GetMaxY() - height : Math.Clamp(
            (double)reference.GetMidY() - height / 2, (double)work.Y, (double)work.GetMaxY() - height);
        var requested = new CGRect(x, y, width, height);
        NSWindowStyle style = Native(window).StyleMask;
        using var control = new NSWindow(NSWindow.ContentRectFor(requested, style), style, NSBackingStore.Buffered, false);
        control.SetFrame(requested, false);
        CGRect aligned = control.Frame;
        control.Close();
        Console.WriteLine($"PLACEMENT: initial={normalFrame}; reference={reference}; work={work}; requested={requested}; AppKit={aligned}");
        return aligned;
    }
    private static void VerifyFrame(CGRect actual, CGRect expected, string message) => Require(
        Math.Abs((double)(actual.X - expected.X)) < .01 && Math.Abs((double)(actual.Y - expected.Y)) < .01 &&
        Math.Abs((double)(actual.Width - expected.Width)) < .01 && Math.Abs((double)(actual.Height - expected.Height)) < .01,
        $"{message}: actual={actual}, expected={expected}, mouse={NSEvent.CurrentMouseLocation}, screens=" +
        string.Join("; ", NSScreen.Screens.Select(s => $"frame={s.Frame},work={s.VisibleFrame},scale={s.BackingScaleFactor}")));
    private static void Observe(Window window, int seconds)
    {
        var editor = new TextBox { Text = "定位后保留编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "定位验证编辑框");
        var finish = new Button { Content = "结束定位验证", Height = 40, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "窗口初始定位", FontSize = 22, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = "Tab 切换焦点，Shift+Tab 返回。", FontSize = 15, Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(editor); panel.Children.Add(finish);
        window.Title = "Jalium Window Startup v142";
        window.Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26));
        window.Content = panel;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        finish.Click += (_, _) => window.Close();
        int tabs = 0;
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) Console.WriteLine($"OBSERVE Tab {++tabs}: modifiers={e.KeyboardModifiers}; text={editor.Text}");
        };
        window.Activate(); Pump(window);
        Require(editor.Focus(), "startup observer could not focus its editor");
        Console.WriteLine($"OBSERVE READY: style={window.TitleBarStyle}; frame={Native(window).Frame}; text={editor.Text}");
        var elapsed = Stopwatch.StartNew();
        while (!closed && elapsed.Elapsed < TimeSpan.FromSeconds(seconds)) Pump(window);
        Console.WriteLine($"OBSERVE COMPLETE: tabs={tabs}; retained text={editor.Text}; closed={closed}");
    }
    private static void Near(CGPoint actual, CGPoint expected, string message) => Require(
        Math.Abs((double)(actual.X - expected.X)) <= 1 && Math.Abs((double)(actual.Y - expected.Y)) <= 1,
        $"{message}: actual={actual}, expected={expected}");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(Window window)
    {
        NativeMethods.PlatformPollEvents(); Dispatcher.CurrentDispatcher.ProcessQueue(); window.UpdateLayout();
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
    }
}
