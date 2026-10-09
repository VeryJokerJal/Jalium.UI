using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>
/// Real AppKit/renderer lifecycle checks run in separate main-thread processes.
/// Application activation is prohibited; these do not replace desktop UI QA.
/// </summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowLifecycleChecks
{
    private const int CaseCount = 18;

    internal static int RunAll()
    {
        int failures = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                failures++;
                Console.Error.WriteLine($"FAIL: Window lifecycle case {index} timed out");
            }
            else if (process.ExitCode != 0) failures++;
        }
        Console.WriteLine($"macOS Window host lifecycle checks: {CaseCount - failures}/{CaseCount} passed");
        return failures == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-case=".Length), out int index) || index < 0 || index >= CaseCount)
            return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        if (index >= 11) return RunOwnerCase(index);
        Window window = index >= 9 ? new PropertyRequestWindow() : new Window();
        window.Title = "Window lifecycle check";
        window.Width = 320;
        window.Height = 240;
        window.TitleBarStyle = WindowTitleBarStyle.Native;
        window.ShowActivated = false;
        window.Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc));
        window.Content = new TextBlock { Text = "Window lifecycle check", Margin = new Thickness(16) };
        int source = 0, loaded = 0, rendered = 0, shown = 0, closed = 0;
        window.SourceInitialized += (_, _) => source++;
        window.Loaded += (_, _) => loaded++;
        window.ContentRendered += (_, _) => rendered++;
        window.Shown += (_, _) => shown++;
        window.Closed += (_, _) => closed++;
        if (index == 6) window.WindowState = WindowState.Maximized;
        switch (index)
        {
            case 0: window.SourceInitialized += (_, _) => window.Close(); break;
            case 1: window.SourceInitialized += (_, _) => window.Hide(); break;
            case 2: window.Loaded += (_, _) => window.Close(); break;
            case 3: window.ContentRendered += (_, _) => window.Close(); break;
            case 4: window.Shown += (_, _) => window.Close(); break;
            case 5: window.SourceInitialized += (_, _) => window.WindowState = WindowState.Maximized; break;
            case 6: window.SourceInitialized += (_, _) => window.WindowState = WindowState.Normal; break;
        }
        var names = new[]
        {
            "SourceInitialized close", "SourceInitialized hide and reuse", "Loaded close",
            "ContentRendered close", "Shown close", "SourceInitialized maximize", "SourceInitialized restore",
            "Native StateChanged requests restore", "Native StateChanged requests minimize",
            "Native property override requests restore", "Native property override requests minimize"
        };
        try
        {
            window.Show();
            Require(source == 1, "SourceInitialized count differs");
            if (index == 1)
            {
                var handle = window.Handle;
                Require(handle != 0 && window.Visibility == Visibility.Hidden, "source callback did not preserve a hidden window");
                Require(loaded == 0 && rendered == 0 && shown == 0 && closed == 0, "hidden startup continued showing");
                window.Show();
                Require(window.Handle == handle && window.Visibility == Visibility.Visible && window.IsLoaded, "hidden source window could not be reused");
                Require(source == 1 && loaded == 1 && rendered == 1 && shown == 1, "reuse raised the wrong lifecycle events");
                window.Close();
                Require(closed == 1 && window.Handle == 0 && !window.IsLoaded, "reused window did not close once");
            }
            else if (index >= 7)
            {
                var platform = (IPlatformWindow)typeof(Window).GetField("_platformWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(window)!;
                var requested = index is 7 or 9 ? WindowState.Normal : WindowState.Minimized;
                if (window is PropertyRequestWindow propertyWindow) propertyWindow.Requested = requested;
                else window.StateChanged += (_, _) =>
                {
                    if (window.WindowState == WindowState.Maximized) window.WindowState = requested;
                };
                // Enter through the native API, as a system window action does,
                // so the application callback runs inside the native state sync.
                platform.SetState(WindowState.Maximized);
                Require(window.WindowState == requested, "callback did not retain the managed request");
                // AppKit finishes miniaturization asynchronously. Observe the
                // completed native state while keeping its event loop running.
                Require(WaitUntil(() => platform.GetState() == requested),
                    "callback request did not reach the native window");
                Require(window.WindowState == requested, "native completion replaced the callback request");
                Require(window.RestoreBounds.Width == 320 && window.RestoreBounds.Height == 240,
                    "state callback lost the initial normal restore size");
            }
            else if (index >= 5)
            {
                Require(window.Handle != 0 && window.IsLoaded && closed == 0, "source state callback lost the live window");
                Require(window.WindowState == (index == 5 ? WindowState.Maximized : WindowState.Normal),
                    "Show overwrote the state requested by SourceInitialized");
                Require(loaded == 1 && rendered == 1 && shown == 1, "source state callback changed lifecycle event counts");
                Require(window.RestoreBounds.Width == 320 && window.RestoreBounds.Height == 240,
                    "source state callback lost the initial normal restore size");
            }
            else
            {
                Require(window.Handle == 0 && !window.IsLoaded && closed == 1, "closed callback left a live or loaded window");
                Require(loaded == (index >= 2 ? 1 : 0), "Loaded fired after source close or fired more than once");
                Require(rendered == (index >= 3 ? 1 : 0), "ContentRendered fired after close or fired more than once");
                Require(shown == (index == 4 ? 1 : 0), "Shown fired after close or fired more than once");
            }
            Console.WriteLine($"PASS: {names[index]}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {names[index]}: {exception}");
            Console.Error.WriteLine($"handle={window.Handle}; loaded={window.IsLoaded}; events={source}/{loaded}/{rendered}/{shown}/{closed}");
            Console.Error.WriteLine($"state={window.WindowState}; size={window.Width}x{window.Height}; restore={window.RestoreBounds}");
            if (window.Handle != 0 && Runtime.GetNSObject<NSView>(window.Handle) is { } view)
                Console.Error.WriteLine($"backingScale={view.Window?.BackingScaleFactor}; frame={view.Window?.Frame}; viewBounds={view.Bounds}");
            return 1;
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int RunOwnerCase(int index)
    {
        static Window CreateWindow(string title) => new()
        {
            Title = title, Width = 320, Height = 240,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
            Content = new TextBlock { Text = title, Margin = new Thickness(16) }
        };
        var owner = CreateWindow("First dialog owner");
        var other = CreateWindow("Second dialog owner");
        var dialog = CreateWindow("Reused dialog");
        var application = new Application { MainWindow = owner, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string name = index switch
        {
            11 => "Hidden dialog rebinds inferred owner",
            12 => "Modeless reuse clears inferred owner",
            13 => "Visible modal owner cannot change",
            14 => "Hidden owned window retains explicit parent",
            15 => "Visible property restores hidden owned parent",
            16 => "Activate restores hidden owned parent",
            _ => "SourceInitialized owner change is preserved"
        };
        try
        {
            owner.Show();
            other.Show();
            int shown = 0;
            if (index >= 13) dialog.Owner = owner;
            if (index == 17) dialog.SourceInitialized += (_, _) => dialog.Owner = other;
            dialog.Shown += (_, _) =>
            {
                shown++;
                var native = Runtime.GetNSObject<NSView>(dialog.Handle)!.Window!;
                var expected = index == 17 ? Runtime.GetNSObject<NSView>(other.Handle)!.Window : shown == 1 || index >= 13
                    ? Runtime.GetNSObject<NSView>(owner.Handle)!.Window
                    : index == 11 ? Runtime.GetNSObject<NSView>(other.Handle)!.Window : null;
                Require((native.ParentWindow?.Handle ?? 0) == (expected?.Handle ?? 0),
                    "native parent differs from the current dialog or modeless owner");
                if (index == 13)
                {
                    bool rejected = false;
                    try { dialog.Owner = other; }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected && ReferenceEquals(dialog.Owner, owner),
                        "visible modal owner change was accepted");
                    Require(native.ParentWindow?.Handle == expected?.Handle,
                        "rejected owner change altered the native parent");
                }
                dialog.Hide();
            };
            if (index is 14 or 15 or 16) dialog.Show();
            else Require(dialog.ShowDialog() == false, "hidden dialog did not return False");
            var handle = dialog.Handle;
            Require(owner.IsEnabled && other.IsEnabled && handle != 0, "modal hide did not restore windows or retain its handle");
            if (index is not 13 and not 17)
            {
                application.MainWindow = other;
                if (index == 11) Require(dialog.ShowDialog() == false, "reused dialog did not return False");
                else if (index == 15) dialog.Visibility = Visibility.Visible;
                else if (index == 16) _ = dialog.Activate();
                else dialog.Show();
                Require(shown == (index >= 15 ? 1 : 2) && dialog.Handle == handle,
                    "reuse changed the native handle or Shown count");
                if (index >= 15)
                    Require(Runtime.GetNSObject<NSView>(dialog.Handle)!.Window!.ParentWindow?.Handle ==
                        Runtime.GetNSObject<NSView>(owner.Handle)!.Window!.Handle,
                        "visibility or activation lost the explicit native parent");
            }
            Console.WriteLine($"PASS: {name}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {name}: {exception}");
            return 1;
        }
        finally
        {
            dialog.Close();
            other.Close();
            owner.Close();
        }
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(8))
        {
            NativeMethods.PlatformPollEvents();
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        }
        return condition();
    }

    private sealed class PropertyRequestWindow : Window
    {
        internal WindowState? Requested;
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == WindowStateProperty && WindowState == WindowState.Maximized && Requested is { } requested)
                WindowState = requested;
        }
    }
}
