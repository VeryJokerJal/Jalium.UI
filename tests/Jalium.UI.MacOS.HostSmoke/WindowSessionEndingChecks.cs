using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jalium.UI.MacOS;

/// <summary>Own-process quit Apple events; never requests a desktop logout or shutdown.</summary>
internal static class WindowSessionEndingChecks
{
    private const int CaseCount = 24;
    private const uint CoreEventClass = 0x61657674, Quit = 0x71756974, QuitReason = 0x7768793f;

    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add($"--window-session-ending-case={index}");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            bool ended = process.WaitForExit(15_000);
            if (!ended) { process.Kill(); process.WaitForExit(); }
            string stdout = output.GetAwaiter().GetResult();
            Console.Write(stdout); Console.Error.Write(errors.GetAwaiter().GetResult());
            if (ended && process.ExitCode == 0 && stdout.Contains("PASS: actual AppKit session ending", StringComparison.Ordinal)) passed++;
            else Console.Error.WriteLine($"FAIL: session ending case {index}: ended={ended}, exit={process.ExitCode}");
        }
        Console.WriteLine($"macOS actual session-ending host checks: {passed}/{CaseCount} passed");
        return passed == CaseCount ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-session-ending-case=".Length), out int index) ||
            index < 0 || index >= CaseCount) return 2;
        if (index >= 20) return RunDeferredCase(index);
        int mode = index % 10;
        var style = index < 10 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        uint? reason = mode switch { 0 or >= 5 => 0x726c676f, 1 => 0x73687574, 2 => 0x72657374, 4 => 0x71756961, _ => null };
        ReasonSessionEnding? expected = mode switch
        { 0 or >= 5 => ReasonSessionEnding.Logoff, 1 or 2 => ReasonSessionEnding.Shutdown, _ => null };
        string name = $"{style}: mode={mode}, reason={reason:x8}";
        JaliumMacApplication.Initialize();
        var native = NSApplication.SharedApplication;
        native.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "Owned session-ending check", TitleBarStyle = style, Width = 360, Height = 240 };
        Window? other = mode == 8 ? new Window { Title = "Owned main window closed during session notification", TitleBarStyle = style, Width = 320, Height = 200 } : null;
        application.MainWindow = other ?? window;
        if (other != null) application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        using var host = new SessionEndingTestDelegate();
        native.Delegate = host;
        int appEvents = 0, windowEvents = 0, closing = 0, closed = 0, attempt = 1;
        bool checkedCancellation = false;
        var order = new List<string>();
        application.SessionEnding += (_, e) =>
        {
            appEvents++; order.Add("application");
            Require(e.ReasonSessionEnding == expected, "application received the wrong session reason");
            if (mode == 5 && attempt == 1) throw new InvalidOperationException("application session callback failed");
            if (mode == 7)
                Require(host.ApplicationShouldTerminate(native) == NSApplicationTerminateReply.Cancel,
                    "nested quit overtook the session-ending notification");
            if (attempt == 1 && mode is 0 or 7 or 8 or 9) e.Cancel = true;
        };
        window.SessionEnding += (_, e) =>
        {
            windowEvents++; order.Add("window");
            Require(e.ReasonSessionEnding == expected, "window received the wrong session reason");
            if (mode == 6 && attempt == 1) throw new InvalidOperationException("window session callback failed");
            if (mode == 8 && attempt == 1) other!.Close();
            if (mode == 1 && attempt == 1) e.Cancel = true;
        };
        window.Closing += (_, e) => { closing++; if (attempt == 1 && mode != 2) e.Cancel = true; };
        window.Closed += (_, _) => closed++;
        host.CheckTermination = () =>
        {
            Require(mode == 2 || checkedCancellation, "process terminated before cancellation was checked");
            Require(host.SawOwnedQuitEvent && host.LastReason == reason, "termination did not receive the owned quit event and its reason");
            int expectedEvents = expected.HasValue ? (mode == 9 ? 2 : 1) : 0;
            Require(appEvents == (mode == 6 ? 0 : expectedEvents) && windowEvents == expectedEvents,
                $"session events: application={appEvents}, window={windowEvents}, expected={expectedEvents}");
            string[] expectedOrder = mode == 6 ? ["window"] : mode == 9
                ? ["window", "application", "window", "application"] : ["window", "application"];
            Require(!expected.HasValue || order.SequenceEqual(expectedOrder), "session event order changed");
            Require(other == null || other.Handle == 0, "callback-closed main window retained resources");
            Require(closed == 1 && window.Handle == 0, "termination preceded resource teardown");
            Require(closing == (mode is 3 or 4 ? 2 : 1), $"unexpected Closing count {closing}");
            WritePass(index);
            Console.WriteLine($"PASS: actual AppKit session ending {name}");
        };
        using var watchdog = new System.Threading.Timer(_ =>
        { Console.Error.WriteLine($"FAIL: session-ending watchdog {name}"); Environment.Exit(2); }, null, 12_000, Timeout.Infinite);
        window.Show();
        other?.Show();
        using var send = NSTimer.CreateScheduledTimer(0.2, _ =>
        {
            try { Console.WriteLine("Sending owned Apple quit event"); SendQuitToSelf(reason); Console.WriteLine("Owned Apple quit event queued"); }
            catch (Exception ex) { Console.Error.WriteLine($"FAIL: send owned quit event: {ex}"); Environment.Exit(1); }
        });
        int ticks = 0;
        using var check = NSTimer.CreateRepeatingScheduledTimer(0.2, _ =>
        {
            if (checkedCancellation) return;
            if (host.QuitCalls == 0 && ++ticks < 40) return;
            if (mode == 2) return;
            try
            {
                Require(host.SawOwnedQuitEvent && host.LastReason == reason, "own-process quit Apple event did not reach AppKit with its reason");
                int expectedEvents = expected.HasValue ? 1 : 0;
                Require(appEvents == (mode == 6 ? 0 : expectedEvents) && windowEvents == expectedEvents,
                    $"cancelled session events: application={appEvents}, window={windowEvents}, expected={expectedEvents}");
                Require(closing == (expected.HasValue ? 0 : 1) && closed == 0 && window.Handle != 0 && window.IsVisible,
                    "cancelled session request closed or hid its window");
                Require(other == null || other.Handle == 0, "main window close did not complete while quit was cancelled");
                checkedCancellation = true; attempt = 2;
                if (mode == 9) SendQuitToSelf(reason);
                else native.Terminate(null);
            }
            catch (Exception ex) { Console.Error.WriteLine($"FAIL: actual session cancellation {name}: {ex}"); Environment.Exit(1); }
        });
        native.Run();
        Console.Error.WriteLine("FAIL: AppKit run loop returned without actual termination");
        return 1;
    }


    private static int RunDeferredCase(int index)
    {
        void State(string value)
        {
            Console.Error.WriteLine(value);
            if (Environment.GetEnvironmentVariable("JALIUM_TASKBAR_OUTPUT") is string output)
                File.AppendAllText(Path.Combine(output, "session-state.log"), value + "\n");
        }
        var style = index < 22 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        uint reason = index % 2 == 0 ? 0x726c676fu : 0x73687574u;
        JaliumMacApplication.Initialize();
        var native = NSApplication.SharedApplication;
        native.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var first = new Window { Title = "Session notification first", TitleBarStyle = style, Width = 360, Height = 240 };
        var later = new Window { Title = "Session notification accepted close", TitleBarStyle = style, Width = 320, Height = 200 };
        application.MainWindow = first;
        var render = typeof(Window).GetField("_renderState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var finish = typeof(Window).GetMethod("CompletePendingManagedTeardown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        int firstEvents = 0, laterEvents = 0, appEvents = 0, laterClosing = 0;
        bool checkedPending = false, finished = false;
        using var host = new SessionEndingTestDelegate();
        native.Delegate = host;
        first.SessionEnding += (_, _) =>
        {
            State("First session notification entered");
            firstEvents++;
            render.SetValue(later, 1 << 1);
            later.Close();
            Require(later.IsCloseRequestedForPlatformTermination && !later.IsClosedForPlatformTermination && later.Handle != 0,
                "close was not accepted with native resources deferred");
            State("Later close accepted; resources remain deferred");
        };
        later.SessionEnding += (_, args) => { laterEvents++; args.Cancel = true; };
        later.Closing += (_, _) => laterClosing++;
        application.SessionEnding += (_, _) => appEvents++;
        host.CheckTermination = () =>
        {
            Require(checkedPending && firstEvents == 1 && laterEvents == 0 && appEvents == 1 && laterClosing == 1,
                "a committed close received another session notification or vetoed quitting");
            Require(first.Handle == 0 && later.Handle == 0 && host.SawOwnedQuitEvent && host.LastReason == reason,
                "owned Apple quit did not wait for native resource teardown");
            finished = true;
            WritePass(index);
            Console.WriteLine($"PASS: actual AppKit session ending deferred close {index} {style}");
        };
        using var watchdog = new System.Threading.Timer(_ =>
        { State("FAIL: deferred session-ending watchdog"); Environment.Exit(2); }, null, 8_000, Timeout.Infinite);
        first.Show(); later.Show();
        using var send = NSTimer.CreateScheduledTimer(.2, _ =>
        {
            try { SendQuitToSelf(reason); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
        });
        using var release = NSTimer.CreateTimer(.6, _ =>
        {
            try
            {
                State("Checking pending AppKit termination");
                Require(host.SawOwnedQuitEvent && firstEvents == 1 && laterEvents == 0 && appEvents == 1,
                    "accepted deferred window vetoed the owned session-ending event");
                Require(first.Handle == 0 && later.Handle != 0 && later.IsCloseRequestedForPlatformTermination,
                    "AppKit Later did not keep only the deferred resources alive");
                checkedPending = true;
                render.SetValue(later, 0);
                finish.Invoke(later, null);
                State("Deferred teardown completed");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
        });
        // Terminate(Later) runs AppKit's modal loop until the pending reply.
        NSRunLoop.Main.AddTimer(release, NSRunLoopMode.Common);
        NSRunLoop.Main.AddTimer(release, NSRunLoopMode.ModalPanel);
        native.Run();
        return finished ? 0 : 1;
    }

    private static void WritePass(int index)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_TASKBAR_OUTPUT") is string output)
            File.WriteAllText(Path.Combine(output,"sdk-result.json"),$"{{\"case\":{index},\"passed\":true}}");
    }

    private static void SendQuitToSelf(uint? reason)
    {
        // The target is hard-coded to this process, even for system-reason payloads.
        using var target = NSAppleEventDescriptor.CurrentProcessDescriptor;
        nint value = CreateAppleEvent(Class.GetHandle("NSAppleEventDescriptor"),
            Selector.GetHandle("appleEventWithEventClass:eventID:targetDescriptor:returnID:transactionID:"),
            CoreEventClass, Quit, target.Handle, -1, 0);
        using var descriptor = Runtime.GetNSObject<NSAppleEventDescriptor>(value)
            ?? throw new InvalidOperationException("Apple event creation failed");
        if (reason is { } code)
        {
            using var why = NSAppleEventDescriptor.DescriptorWithEnumCode(code);
            descriptor.SetParamDescriptorforKeyword(why, QuitReason);
        }
        using var reply = descriptor.SendEvent(NSAppleEventSendOptions.NoReply | NSAppleEventSendOptions.NeverInteract, 3, out var error);
        if (error != null) throw new InvalidOperationException(error.ToString());
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint CreateAppleEvent(nint receiver, nint selector, uint eventClass, uint eventID,
        nint target, short returnID, int transactionID);
}

[Register("JaliumMacSessionEndingTestDelegate")]
internal sealed class SessionEndingTestDelegate : JaliumMacApplicationDelegate
{
    internal Action? CheckTermination;
    internal int QuitCalls;
    internal bool SawOwnedQuitEvent;
    internal uint? LastReason;
    protected override JaliumApp CreateHostedApp() => throw new NotSupportedException();
    public override void DidFinishLaunching(NSNotification notification)
    { Console.WriteLine("Owned session host finished launching"); ConfigureNativeWindowMenu(NSApplication.SharedApplication); }
    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => false;
    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        QuitCalls++;
        var current = NSAppleEventManager.SharedAppleEventManager.CurrentAppleEvent;
        using var why = current?.ParamDescriptorForKeyword(0x7768793f);
        uint? eventClass = (uint?)current?.EventClass, eventId = (uint?)current?.EventID;
        uint? reason = (uint?)why?.EnumCodeValue();
        Console.WriteLine($"Apple quit metadata: class={eventClass:x8}, id={eventId:x8}, reason={reason:x8}");
        if (eventClass == 0x61657674 && eventId == 0x71756974)
        { SawOwnedQuitEvent = true; LastReason = reason; }
        return base.ApplicationShouldTerminate(sender);
    }
    public override void WillTerminate(NSNotification notification)
    {
        try { CheckTermination?.Invoke(); }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL: session termination checks: {ex}"); Environment.Exit(1); }
        base.WillTerminate(notification);
    }
}
