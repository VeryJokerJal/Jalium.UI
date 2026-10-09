using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Jalium.UI.MacOS;

/// <summary>Calls actual AppKit Terminate, including its modal Later path, in isolated processes.</summary>
internal static class WindowNativeQuitChecks
{
    private const int CaseCount = 32;

    internal static int RunAll()
    {
        int failures = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add($"--window-native-quit-case={index}");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            bool ended = process.WaitForExit(15_000);
            if (!ended) { process.Kill(); process.WaitForExit(); }
            string stdout = output.GetAwaiter().GetResult();
            Console.Write(stdout);
            Console.Error.Write(errors.GetAwaiter().GetResult());
            if (!ended || process.ExitCode != 0 || !stdout.Contains("PASS: actual AppKit quit", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"FAIL: native quit case {index}: exit={process.ExitCode}, ended={ended}");
                failures++;
            }
        }
        Console.WriteLine($"macOS actual AppKit quit host checks: {CaseCount - failures}/{CaseCount} passed");
        return failures == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-native-quit-case=".Length), out int index) || index < 0 || index >= CaseCount)
            return 2;
        bool nativeEntry = index >= 16;
        int kind = index % 8;
        var style = index % 16 < 8 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        string name = $"{style}: " + (kind switch
        {
            0 => "outer Closing cancels actual Terminate and fresh quit runs",
            1 => "outer Closing throws after actual Terminate and fresh quit runs",
            2 => "accepted outer Closing retries actual termination after returning",
            3 => "owned Closing cancels actual Terminate before its owner closes",
            4 => "nested actual Terminate returns while the original quit continues",
            5 => "late cancellation keeps earlier render teardown from stopping AppKit",
            6 => "actual Later waits for resource teardown before accepting quit",
            _ => "actual Later rejects a new window and permits a fresh quit"
        }) + (nativeEntry ? " [Objective-C root entry]" : " [managed root entry]");
        JaliumMacApplication.Initialize();
        var native = NSApplication.SharedApplication;
        string principal = NSBundle.MainBundle.ObjectForInfoDictionary("NSPrincipalClass")?.ToString()
            ?? "JaliumMacApplication";
        string actualClass = new Class(ObjectGetClass(native.Handle)).Name ?? "<unknown>";
        Require(actualClass == principal, $"Expected principal '{principal}', got '{actualClass}'");
        Console.WriteLine($"Actual AppKit principal: {actualClass}");
        void RequestQuit()
        {
            if (nativeEntry) TerminateNative(native.Handle, Selector.GetHandle("terminate:"), 0);
            else native.Terminate(null);
        }
        native.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var first = CreateWindow("Actual Terminate Closing", style);
        var other = CreateWindow("Actual Terminate remaining", style);
        if (kind == 3) first.Owner = other;
        var application = new Application { MainWindow = kind is 3 or 5 ? other : first, ShutdownMode = ShutdownMode.OnMainWindowClose };
        var renderState = typeof(Window).GetField("_renderState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var finish = typeof(Window).GetMethod("CompletePendingManagedTeardown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int callbackCount = 0, returnedTerminateCount = 0, freshClosingCount = 0;
        bool otherClosedDuringCallback = false, outerCloseReturned = false, checkedCancellation = false;
        Exception? failure = null;
        Window? newWindow = null;
        using var host = new NativeQuitTestDelegate();
        native.Delegate = host;
        EventHandler<CancelEventArgs> callback = (_, args) =>
        {
            callbackCount++;
            native.Terminate(null);
            returnedTerminateCount++;
            otherClosedDuringCallback |= kind != 4 && other.Handle == 0;
            args.Cancel = kind is 0 or 1 or 3 or 5;
            if (kind == 1) throw new InvalidOperationException("Actual Terminate outer Closing failed");
        };
        EventHandler<CancelEventArgs> freshCancel = (_, args) => { freshClosingCount++; args.Cancel = true; };
        first.Closing += callback;
        if (kind is 4 or 6 or 7) other.Closing += callback;
        host.CheckTermination = () =>
        {
            Require(kind is 2 or 4 or 6, "Rejected Closing terminated the process");
            Require(first.Handle == 0 && other.Handle == 0 && !otherClosedDuringCallback,
                $"Actual termination preceded resource release or the original callback: first={first.Handle}, other={other.Handle}, callbacks={callbackCount}, returned={returnedTerminateCount}, replies={string.Join(',', host.Replies)}");
            Require(returnedTerminateCount == (kind is 4 or 6 ? 2 : 1), "Nested Terminate never returned to Closing");
            Require(kind != 2 || outerCloseReturned, "Accepted termination retry ran before the outer Close returned");
            Require(kind switch
            {
                4 => host.Replies.Count == 1 && host.Replies[0] is NSApplicationTerminateReply.Now or NSApplicationTerminateReply.Later,
                6 => host.Replies.SequenceEqual([NSApplicationTerminateReply.Later]),
                _ => host.Replies.SequenceEqual([NSApplicationTerminateReply.Cancel, NSApplicationTerminateReply.Now])
            },
                "Unexpected actual termination replies");
            Require(application.ShutdownMode == ShutdownMode.OnMainWindowClose, "Actual quit changed the public policy");
            Console.WriteLine($"PASS: actual AppKit quit {name}; replies={string.Join(',', host.Replies)}");
            Console.Out.Flush();
        };
        try
        {
            if (kind == 5) { other.Show(); first.Show(); }
            else { first.Show(); other.Show(); }
            new Thread(() =>
            {
                Thread.Sleep(8_000);
                Console.Error.WriteLine($"FAIL: actual AppKit quit {name} did not return or complete");
                Environment.Exit(93);
            }) { IsBackground = true, Name = "Actual AppKit quit watchdog" }.Start();
            native.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    string runningClass = new Class(ObjectGetClass(native.Handle)).Name ?? "<unknown>";
                    Require(NSApplication.SharedApplication.Handle == native.Handle && native.IsKindOfClass(new Class(principal)),
                        $"The configured principal changed before quit: expected={principal}, actual={runningClass}, initial={native.Handle}, current={NSApplication.SharedApplication.Handle}");
                    Console.WriteLine($"AppKit principal after showing windows: {runningClass}");
                    if (kind is 6 or 7)
                    {
                        renderState.SetValue(first, 1 << 1);
                        native.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                Require(host.Replies.SequenceEqual([NSApplicationTerminateReply.Later]) &&
                                    first.Handle != 0 && other.Handle == 0 && returnedTerminateCount == 2,
                                    "Actual Later did not keep accepted resources alive while Closing returned");
                                if (kind == 7)
                                {
                                    newWindow = CreateWindow("Window opened while AppKit waited", style);
                                    newWindow.Closing += freshCancel;
                                    newWindow.Show();
                                }
                                renderState.SetValue(first, 0);
                                finish.Invoke(first, null);
                            }
                            catch (Exception exception)
                            {
                                Console.Error.WriteLine($"FAIL: actual AppKit Later completion: {exception}");
                                Environment.Exit(1);
                            }
                        });
                        RequestQuit();
                        Require(kind == 7 && newWindow != null && newWindow.Handle != 0 && returnedTerminateCount == 2 &&
                            first.Handle == 0 && other.Handle == 0, "Actual Later returned before rejecting the new window");
                        RequestQuit();
                        Require(freshClosingCount == 1 && newWindow != null && newWindow.Handle != 0 && host.Replies.SequenceEqual(
                            [NSApplicationTerminateReply.Later, NSApplicationTerminateReply.Cancel]),
                            "Fresh quit after Actual Later rejection was blocked or lost the new window");
                        checkedCancellation = true;
                        application.Shutdown(91);
                        return;
                    }
                    if (kind == 5) renderState.SetValue(other, 1 << 1);
                    if (kind == 4) { RequestQuit(); throw new Exception("Accepted original quit returned"); }
                    if (kind == 1)
                    {
                        try { first.Close(); throw new Exception("Outer Closing exception was swallowed"); }
                        catch (InvalidOperationException exception) when (exception.Message == "Actual Terminate outer Closing failed") { }
                    }
                    else first.Close();
                    outerCloseReturned = true;
                    if (kind == 2) return;
                    Require(returnedTerminateCount == 1 && callbackCount == 1 && !otherClosedDuringCallback,
                        "Actual Terminate blocked Closing or closed another window prematurely");
                    Require(first.Handle != 0 && other.Handle != 0 && !first.IsCloseRequestedForPlatformTermination,
                        "Rejected actual quit destroyed a live window");
                    void CheckFreshQuit()
                    {
                        try
                        {
                            first.Closing -= callback;
                            first.Closing += freshCancel;
                            RequestQuit();
                            Require(freshClosingCount == 1 && host.Replies.SequenceEqual(
                                [NSApplicationTerminateReply.Cancel, NSApplicationTerminateReply.Cancel]),
                                "Later quit was still latched to the rejected Closing");
                            Require(first.Handle != 0 && (kind == 5 ? other.Handle == 0 : other.Handle != 0),
                                "Fresh cancellation lost surviving windows");
                            checkedCancellation = true;
                            application.Shutdown(91);
                        }
                        catch (Exception exception) { failure = exception; application.Shutdown(91); }
                    }
                    native.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            if (kind == 5)
                            {
                                renderState.SetValue(other, 0);
                                finish.Invoke(other, null);
                                // An early automatic quit would stop the real run
                                // loop before this timer can retry the cancelled quit.
                                NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(0.2), _ => CheckFreshQuit());
                            }
                            else CheckFreshQuit();
                        }
                        catch (Exception exception) { failure = exception; application.Shutdown(91); }
                    });
                }
                catch (Exception exception) { failure = exception; application.Shutdown(91); }
            });
            var run = typeof(NativeMethods).GetMethod("PlatformRunMessageLoop", BindingFlags.Static | BindingFlags.NonPublic)!;
            int exit = (int)run.Invoke(null, null)!;
            Require(kind is 0 or 1 or 3 or 5 or 7 && exit == 91 && checkedCancellation && failure == null,
                $"Actual AppKit quit did not finish as expected: loop={exit}, checked={checkedCancellation}, failure={failure}");
            Console.WriteLine($"PASS: actual AppKit quit {name}; replies={string.Join(',', host.Replies)}; loop={exit}");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"FAIL: actual AppKit quit {name}: {exception}"); return 1; }
        finally
        {
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            first.Closing -= callback;
            first.Closing -= freshCancel;
            other.Closing -= callback;
            if (newWindow != null) { newWindow.Closing -= freshCancel; newWindow.Close(); }
            renderState.SetValue(first, 0);
            finish.Invoke(first, null);
            renderState.SetValue(other, 0);
            finish.Invoke(other, null);
            first.Close();
            other.Close();
        }
    }

    private static Window CreateWindow(string title, WindowTitleBarStyle style) => new()
    {
        Title = title, Width = 320, Height = 240, TitleBarStyle = style, ShowActivated = false,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
        Content = new TextBlock { Text = title, Margin = new Thickness(16) }
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_getClass")]
    private static extern nint ObjectGetClass(nint instance);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void TerminateNative(nint instance, nint selector, nint sender);
}

[Register("JaliumMacNativeQuitTestDelegate")]
internal sealed class NativeQuitTestDelegate : JaliumMacApplicationDelegate
{
    internal readonly List<NSApplicationTerminateReply> Replies = [];
    internal Action? CheckTermination;
    protected override JaliumApp CreateHostedApp() => throw new NotSupportedException("Native quit checks supply their own Application.");
    public override void DidFinishLaunching(NSNotification notification)
    {
        ConfigureNativeWindowMenu(NSApplication.SharedApplication);
    }
    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => false;
    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        var reply = base.ApplicationShouldTerminate(sender);
        Replies.Add(reply);
        return reply;
    }
    public override void WillTerminate(NSNotification notification)
    {
        try { CheckTermination?.Invoke(); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: actual AppKit quit termination checks: {exception}");
            Environment.Exit(1);
        }
        base.WillTerminate(notification);
    }
}
