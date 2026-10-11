using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Jalium.UI.MacOS;

/// <summary>Quit negotiation with real windows and an actual AppKit message loop; no desktop activation.</summary>
internal static class WindowQuitChecks
{
    private const int CaseCount = 18;
    private const int PlannedExitCode = 91;
    private static readonly FieldInfo RenderState = typeof(Window).GetField("_renderState", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo FinishTeardown = typeof(Window).GetMethod("CompletePendingManagedTeardown", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static int RunAll()
    {
        int failures = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-quit-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                process.WaitForExit();
                Console.Error.WriteLine($"FAIL: quit case {index} timed out");
                failures++;
            }
            else if (process.ExitCode != 0) failures++;
        }
        Console.WriteLine($"macOS Window quit host checks: {CaseCount - failures}/{CaseCount} passed");
        return failures == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-quit-case=".Length), out int index) || index < 0 || index >= CaseCount)
            return 2;
        if (index >= 8) return RunReentrantCase(index - 8);
        int kind = index % 4;
        var style = index < 4 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        JaliumMacApplication.Initialize();
        var nativeApplication = NSApplication.SharedApplication;
        nativeApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var main = CreateWindow("Quit policy main", style);
        var other = CreateWindow("Quit policy independent", style);
        var application = new Application
        {
            MainWindow = main,
            ShutdownMode = kind == 0 ? ShutdownMode.OnExplicitShutdown :
                kind == 1 ? ShutdownMode.OnLastWindowClose : ShutdownMode.OnMainWindowClose
        };
        var originalPolicy = application.ShutdownMode;
        var chosenPolicy = kind == 0 ? ShutdownMode.OnLastWindowClose :
            kind == 1 ? ShutdownMode.OnExplicitShutdown : originalPolicy;
        ShutdownMode? observed = null;
        int mainClosing = 0, otherClosing = 0;
        EventHandler<CancelEventArgs> mainCallback = (_, args) =>
        {
            mainClosing++;
            observed = application.ShutdownMode;
            if (kind <= 1) application.ShutdownMode = chosenPolicy;
            args.Cancel = kind == 1;
        };
        EventHandler<CancelEventArgs> otherCallback = (_, args) =>
        {
            otherClosing++;
            if (kind == 0) application.ShutdownMode = chosenPolicy;
            if (kind == 2) args.Cancel = true;
            if (kind == 3) throw new InvalidOperationException("Later window rejected quit with an exception");
        };
        main.Closing += mainCallback;
        other.Closing += otherCallback;
        string name = $"{style}: " + (kind switch
        {
            0 => "accepted closes preserve policy and leave quit to AppKit",
            1 => "cancelled close preserves callback policy and live windows",
            2 => "later cancellation cannot stop the loop during earlier deferred teardown",
            _ => "later exception cannot stop the loop during earlier deferred teardown"
        });
        Exception? failure = null;
        bool checkedQuit = false, plannedExit = false;
        using var host = new MenuTestDelegate();
        try
        {
            main.Show();
            other.Show();
            nativeApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (kind >= 2) RenderState.SetValue(main, 1 << 1);
                    var reply = host.ApplicationShouldTerminate(nativeApplication);
                    Require(reply == (kind == 0 ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel),
                        $"Unexpected quit reply {reply}");
                    if (kind == 0)
                        Require(main.Handle == 0 && other.Handle == 0 && mainClosing == 1 && otherClosing == 1,
                            "Accepted quit did not close both windows once");
                    else if (kind == 1)
                        Require(main.Handle != 0 && other.Handle != 0 && mainClosing == 1 && otherClosing == 0,
                            "Cancelled quit continued to another window");
                    else
                    {
                        Require(main.Handle != 0 && other.Handle != 0, "Earlier deferred close was not pending");
                        RenderState.SetValue(main, 0);
                        FinishTeardown.Invoke(main, null);
                        Require(main.Handle == 0 && other.Handle != 0 && mainClosing == 1 && otherClosing == 1,
                            "Cancel or exception lost the accepted close or the surviving independent window");
                    }
                    Require(observed == originalPolicy, $"Closing read {observed} instead of public policy {originalPolicy}");
                    Require(application.ShutdownMode == chosenPolicy, "Quit replaced the application's latest policy");
                    checkedQuit = true;
                }
                catch (Exception exception) { failure = exception; }
            });
            using var timer = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(0.4), _ =>
            {
                plannedExit = true;
                application.Shutdown(PlannedExitCode);
            });
            var runLoop = typeof(NativeMethods).GetMethod("PlatformRunMessageLoop", BindingFlags.Static | BindingFlags.NonPublic)!;
            int exit = (int)runLoop.Invoke(null, null)!;
            Require(exit == PlannedExitCode && plannedExit, $"Message loop stopped early: exit={exit}, sentinel={plannedExit}");
            Require(failure == null && checkedQuit, $"Quit assertions failed: {failure}");
            Console.WriteLine($"PASS: {name}; original={originalPolicy}; current={application.ShutdownMode}; loop={exit}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {name}: {exception}");
            if (failure != null) Console.Error.WriteLine(failure);
            return 1;
        }
        finally
        {
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            main.Closing -= mainCallback;
            other.Closing -= otherCallback;
            RenderState.SetValue(main, 0);
            FinishTeardown.Invoke(main, null);
            main.Close();
            other.Close();
        }
    }

    private static int RunReentrantCase(int index)
    {
        int kind = index % 5;
        var style = index < 5 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        JaliumMacApplication.Initialize();
        var nativeApplication = NSApplication.SharedApplication;
        nativeApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var first = CreateWindow("Reentrant Closing window", style);
        var other = CreateWindow("Reentrant Quit remaining window", style);
        if (kind == 4) first.Owner = other;
        var application = new Application
        {
            MainWindow = kind == 4 ? other : first,
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        MacOSWindowCloseResult? initial = null;
        bool otherClosedDuringCallback = false;
        EventHandler<CancelEventArgs> callback = (_, args) =>
        {
            initial = request.Begin();
            otherClosedDuringCallback = other.Handle == 0;
            args.Cancel = kind is 0 or 1 or 4;
            if (kind == 1) throw new InvalidOperationException("Outer Closing failed after requesting quit");
        };
        first.Closing += callback;
        string name = $"{style}: quit inside Closing " + (kind switch
        {
            0 => "waits for cancellation and permits retry",
            1 => "waits for callback failure and permits retry",
            2 => "continues only after the accepted callback returns",
            3 => "waits for accepted resource teardown and replies once",
            _ => "preserves an owned window's cancellation before closing its owner"
        });
        Exception? failure = null;
        bool checkedQuit = false, plannedExit = false;
        try
        {
            first.Show();
            other.Show();
            nativeApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (kind == 3) RenderState.SetValue(first, 1 << 1);
                    if (kind == 1)
                    {
                        try { first.Close(); throw new Exception("Outer Closing exception was swallowed"); }
                        catch (InvalidOperationException exception) when (exception.Message == "Outer Closing failed after requesting quit") { }
                    }
                    else first.Close();
                    Require(initial == MacOSWindowCloseResult.Pending, $"Quit did not wait for the current callback: {initial}");
                    Require(!otherClosedDuringCallback, "Quit closed a later window or owner before the active Closing callback returned");
                    if (kind is 0 or 1 or 4)
                    {
                        Require(request.Result == MacOSWindowCloseResult.Cancelled && replies.SequenceEqual([false]),
                            $"Outer cancellation/failure stranded quit: result={request.Result}, replies={string.Join(',', replies)}");
                        Require(first.Handle != 0 && other.Handle != 0, "Rejected quit destroyed a surviving window");
                        first.Closing -= callback;
                        using var retry = new MacOSWindowCloseRequest(application, _ => throw new Exception("Unexpected deferred retry reply"));
                        Require(retry.Begin() == MacOSWindowCloseResult.Complete && first.Handle == 0 && other.Handle == 0,
                            "Rejected reentrant quit could not be retried");
                    }
                    else
                    {
                        if (kind == 3)
                        {
                            Require(request.Result == MacOSWindowCloseResult.Pending && replies.Count == 0 &&
                                first.Handle != 0 && other.Handle == 0, "Deferred reentrant quit completed before resources were released");
                            RenderState.SetValue(first, 0);
                            FinishTeardown.Invoke(first, null);
                            FinishTeardown.Invoke(first, null);
                        }
                        Require(request.Result == MacOSWindowCloseResult.Complete && replies.SequenceEqual([true]) &&
                            first.Handle == 0 && other.Handle == 0, "Accepted reentrant quit failed to complete exactly once");
                    }
                    Require(application.ShutdownMode == ShutdownMode.OnMainWindowClose, "Reentrant quit changed the public shutdown policy");
                    checkedQuit = true;
                }
                catch (Exception exception) { failure = exception; }
            });
            using var timer = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(0.4), _ =>
            {
                plannedExit = true;
                application.Shutdown(PlannedExitCode);
            });
            var runLoop = typeof(NativeMethods).GetMethod("PlatformRunMessageLoop", BindingFlags.Static | BindingFlags.NonPublic)!;
            int exit = (int)runLoop.Invoke(null, null)!;
            Require(exit == PlannedExitCode && plannedExit, $"Message loop stopped early: exit={exit}, sentinel={plannedExit}");
            Require(failure == null && checkedQuit, $"Reentrant quit assertions failed: {failure}");
            Console.WriteLine($"PASS: {name}; loop={exit}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {name}: {exception}");
            return 1;
        }
        finally
        {
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            first.Closing -= callback;
            RenderState.SetValue(first, 0);
            FinishTeardown.Invoke(first, null);
            first.Close();
            other.Close();
        }
    }

    private static Window CreateWindow(string title, WindowTitleBarStyle style) => new()
    {
        Title = title, Width = 320, Height = 240,
        TitleBarStyle = style, ShowActivated = false,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
        Content = new TextBlock { Text = title, Margin = new Thickness(16) }
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
