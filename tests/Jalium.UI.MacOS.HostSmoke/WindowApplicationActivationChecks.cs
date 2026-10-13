using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jalium.UI.MacOS;

/// <summary>Observes real desktop app switches without supplying activation or deactivation events.</summary>
internal static class WindowApplicationActivationChecks
{
    private const string CasePrefix = "--window-application-activation-case=";

    internal static int RunAll() => RunCases(Enumerable.Range(0, 4).ToArray());
    internal static int RunCase(string argument) =>
        int.TryParse(argument.AsSpan(CasePrefix.Length), out int index) && (uint)index < 4
            ? RunCases([index]) : 2;

    private static int RunCases(int[] indices)
    {
        string? root = Environment.GetEnvironmentVariable("JALIUM_MACOS_ACTIVATION_ROOT");
        if (string.IsNullOrEmpty(root))
        {
            Console.Error.WriteLine("Application activation checks require JALIUM_MACOS_ACTIVATION_ROOT and real desktop switching.");
            return 77;
        }
        Directory.CreateDirectory(root);
        int phaseSeconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_ACTIVATION_SECONDS"), out int seconds)
            && seconds is >= 30 and <= 900 ? seconds : 180;
        using var results = new StreamWriter(new FileStream(Path.Combine(root, "application-activation.jsonl"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        JaliumMacApplication.Initialize();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new MenuTestDelegate();
        host.Configure(app);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool launched = false;
        int completed = 0, passed = 0, entered = 0;
        using var terminating = NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.WillTerminateNotification, _ =>
        {
            RecordCompletion("native-termination");
            if (passed != indices.Length)
                Console.Error.WriteLine($"Application activation checks interrupted: {passed}/{indices.Length} passed, {completed} completed, {entered} started.");
        }, app);
        using var launching = NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidFinishLaunchingNotification, _ =>
        {
            launched = true;
            app.BeginInvokeOnMainThread(Next);
        }, app);
        int exit = (int)typeof(NativeMethods).GetMethod("PlatformRunMessageLoop", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;
        RecordCompletion("message-loop-return");
        if (exit != 0 || !launched) return 1;
        if (entered == 0)
        {
            Console.Error.WriteLine("macOS application activation host checks: not run; actual system foreground selection is required.");
            return 77;
        }
        Console.WriteLine($"macOS application activation host checks: {passed}/{indices.Length} passed");
        return passed == indices.Length ? 0 : 1;

        void RecordCompletion(string reason)
        {
            var summary = new WindowApplicationActivationCompletion(Environment.ProcessId, DateTimeOffset.UtcNow,
                reason, indices.Length, entered, completed, passed, launched && passed == indices.Length);
            File.WriteAllText(Path.Combine(root, "completion.json"), JsonSerializer.Serialize(summary,
                WindowApplicationActivationJsonContext.Default.WindowApplicationActivationCompletion));
            results.Flush();
        }

        void Next()
        {
            if (completed == indices.Length)
            {
                typeof(NativeMethods).GetMethod("PlatformQuit", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [0]);
                return;
            }
            int index = indices[completed];
            RunScenario(index, app, application, results, phaseSeconds, () => entered++,
                () => { completed++; passed++; app.BeginInvokeOnMainThread(Next); }, error =>
            {
                Console.Error.WriteLine($"FAIL application activation {index}: {error}");
                completed = entered == 0 ? indices.Length : completed + 1;
                app.BeginInvokeOnMainThread(Next);
            });
        }
    }

    private static void RunScenario(int index, NSApplication app, Application application, StreamWriter results,
        int phaseSeconds, Action entered, Action succeeded, Action<Exception> failed)
    {
        bool modal = index >= 2;
        var titleBar = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var status = new TextBlock { Text = "点击开始，等待提示后切换到其他应用。", FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = ink };
        var label = new TextBlock { Text = "恢复后继续编辑，选区与撤销历史应保留。", FontSize = 15, Foreground = ink };
        var editor = new TextBox { Text = "Activation 中文 Miii 🙂 é", FontSize = 16, MinHeight = 48 };
        AutomationProperties.SetName(editor, "应用切换验证编辑框");
        AutomationProperties.SetLabeledBy(editor, label);
        var start = new Button { Content = "开始应用切换检查", MinHeight = 44 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "应用切换与编辑恢复", FontSize = 28, Foreground = ink });
        body.Children.Add(label);
        body.Children.Add(editor);
        body.Children.Add(status);
        body.Children.Add(start);
        var owner = CreateWindow("应用切换检查所属窗口", titleBar, new TextBlock { Text = "模态检查期间，此窗口保持禁用。" });
        var window = modal ? CreateWindow("", titleBar, body) : owner;
        window.Title = $"应用切换 · {(titleBar == WindowTitleBarStyle.Native ? "原生" : "自定义")} · {(modal ? "模态" : "普通")}";
        window.Content = body;
        application.MainWindow = owner;
        int activations = 0, deactivations = 0, baselineActivations = 0, baselineDeactivations = 0, cycle = 0;
        window.Activated += (_, _) => activations++;
        window.Deactivated += (_, _) => deactivations++;
        string phase = "ready", before = editor.Text, edited = "";
        int selectionStart = 0, selectionLength = 0;
        bool requested = false, finished = false;
        Exception? completionError = null;
        var timer = Stopwatch.StartNew();
        TimeSpan phaseStarted = timer.Elapsed;
        TimeSpan? stableSince = null;
        NSTimer? observation = null;
        NSView? view = null;
        NSWindow? native = null;
        nint handle = 0, nativeHandle = 0;
        Rect restoreBounds = default;
        start.Click += (_, _) =>
        {
            requested = true;
            status.Text = "正在确认系统前台；请保持此窗口 1 秒。";
            editor.Focus();
        };
        window.Shown += (_, _) =>
        {
            view = Runtime.GetNSObject<NSView>(window.Handle)!;
            native = view.Window!;
            observation = NSTimer.CreateRepeatingScheduledTimer(.1, _ =>
            {
                if (finished) return;
                try { Observe(); }
                catch (Exception error) { Capture("failed"); Finish(error); }
            });
            Capture("ready");
        };
        owner.Show();
        if (modal)
        {
            window.Owner = owner;
            window.ShowDialog();
            if (!finished) Finish(new InvalidOperationException("The modal scenario closed before finishing its checks."));
            if (completionError == null && !owner.IsEnabled)
                completionError = new InvalidOperationException("The modal owner was not reenabled after returning.");
            owner.Close();
            Complete();
        }

        void Observe()
        {
            Capture(phase, append: false);
            Require(timer.Elapsed - phaseStarted < TimeSpan.FromSeconds(phaseSeconds), $"Timed out in {phase}; real desktop switching is required.");
            bool selectedBySystem = NSRunningApplication.CurrentApplication.Active
                && NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier == Environment.ProcessId;
            bool actualActive = app.Active && native!.IsKeyWindow;
            bool modifiersReleased = (NSEvent.CurrentModifierFlags & (NSEventModifierMask.CommandKeyMask |
                NSEventModifierMask.ControlKeyMask | NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask)) == 0;
            bool reached = phase switch
            {
                "ready" => requested && selectedBySystem && actualActive && native.IsMainWindow && window.IsActive && modifiersReleased,
                "await-background" => !selectedBySystem && !app.Active,
                _ => selectedBySystem && actualActive && native.IsMainWindow && modifiersReleased
            };
            if (!reached) { stableSince = null; return; }
            stableSince ??= timer.Elapsed;
            if (timer.Elapsed - stableSince.Value < TimeSpan.FromSeconds(1)) return;
            if (phase == "ready")
            {
                entered();
                Require(editor.Focus(), "Initial editor focus failed.");
                Require(((IImeSupport)editor).TryReplaceImeText(before.Length, 0, " · 追加🙂"), "Initial undoable edit failed.");
                edited = editor.Text;
                editor.Select(before.Length - 3, 3);
                selectionStart = editor.SelectionStart; selectionLength = editor.SelectionLength;
                handle = window.Handle; nativeHandle = native.Handle; restoreBounds = window.RestoreBounds;
                baselineActivations = activations; baselineDeactivations = deactivations;
                start.IsEnabled = false;
                Capture("initial-foreground");
                SetPhase("await-background", "请切换到其他应用，等待 1 秒后再返回。");
                return;
            }
            Require(window.IsActive == actualActive, "Window.IsActive disagrees with the actual application and key window.");
            Require(window.Handle == handle && native.Handle == nativeHandle && window.RestoreBounds == restoreBounds,
                "Application switching changed native identity or restoration geometry.");
            Require(editor.Text == edited && editor.SelectionStart == selectionStart && editor.SelectionLength == selectionLength,
                "Application switching changed text or selection.");
            if (modal) Require(window.IsModal && !owner.IsEnabled, "Application switching released the modal owner.");
            if (phase == "await-background")
            {
                Require(activations == baselineActivations + cycle && deactivations == baselineDeactivations + cycle + 1,
                    "Leaving the application did not publish exactly one deactivation.");
                Capture("background");
                SetPhase("await-foreground", "请返回此窗口，保留编辑框当前的选区。");
                return;
            }
            Require(editor.IsKeyboardFocused && native.FirstResponder?.Handle == view!.Handle,
                "Returning to the application did not restore the editor and native responder.");
            Require(activations == baselineActivations + cycle + 1 && deactivations == baselineDeactivations + cycle + 1,
                "Returning to the application did not publish exactly one activation.");
            Capture("returned-foreground");
            cycle++;
            if (cycle < 2) { SetPhase("await-background", "请再切换到其他应用，等待 1 秒后返回。"); return; }
            Require(app.SendAction(new Selector("undo:"), view, null) && editor.Text == before, "Native undo history was lost after application switching.");
            Require(app.SendAction(new Selector("redo:"), view, null) && editor.Text == edited, "Native redo history was lost after application switching.");
            Capture("undo-redo");
            Finish(null);
        }

        void SetPhase(string next, string message)
        {
            phase = next; phaseStarted = timer.Elapsed; stableSince = null; status.Text = message;
        }

        void Capture(string snapshotPhase, bool append = true)
        {
            var snapshot = new WindowApplicationActivationSnapshot(Environment.ProcessId, DateTimeOffset.UtcNow, index,
                snapshotPhase, cycle, titleBar.ToString(), modal, app.Active, NSRunningApplication.CurrentApplication.Active,
                NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? 0, native?.IsKeyWindow ?? false,
                native?.IsMainWindow ?? false, window.IsActive, activations, deactivations, (long)window.Handle,
                (long)(native?.Handle ?? 0), native?.FirstResponder?.Handle == view?.Handle, editor.IsKeyboardFocused,
                editor.Text, editor.SelectionStart, editor.SelectionLength, owner.IsEnabled, window.RestoreBounds.ToString());
            string serialized = JsonSerializer.Serialize(snapshot, WindowApplicationActivationJsonContext.Default.WindowApplicationActivationSnapshot);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(((FileStream)results.BaseStream).Name)!, "current.json"), serialized);
            if (append)
            {
                results.WriteLine(serialized);
                results.Flush();
            }
        }

        void Finish(Exception? error)
        {
            if (finished) return;
            finished = true;
            completionError = error;
            observation?.Invalidate();
            if (modal && window.IsModal) window.DialogResult = error == null;
            else window.Close();
            if (!modal) Complete();
        }

        void Complete()
        {
            observation?.Dispose();
            if (completionError != null) failed(completionError);
            else
            {
                Console.WriteLine($"PASS: {titleBar}: {(modal ? "modal" : "normal")}: two app switches retain activation events, selection, focus and undo");
                succeeded();
            }
        }
    }

    private static Window CreateWindow(string title, WindowTitleBarStyle titleBar, UIElement content) => new()
    {
        Title = title, TitleBarStyle = titleBar, Width = 640, Height = 380, MinWidth = 540, MinHeight = 340,
        FontFamily = "PingFang SC", Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFC, 0xFD)), Content = content
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed record WindowApplicationActivationSnapshot(int Pid, DateTimeOffset Timestamp, int Index, string Phase,
    int Cycle, string TitleBar, bool Modal, bool ApplicationActive, bool RunningApplicationActive, int FrontmostPid,
    bool NativeKey, bool NativeMain, bool WindowActive,
    int Activations, int Deactivations, long ViewHandle, long NativeHandle, bool NativeFirstResponder, bool EditorFocused,
    string Text, int SelectionStart, int SelectionLength, bool OwnerEnabled, string RestoreBounds);

internal sealed record WindowApplicationActivationCompletion(int Pid, DateTimeOffset Timestamp, string Reason,
    int Expected, int Entered, int Completed, int Passed, bool Success);

[JsonSerializable(typeof(WindowApplicationActivationSnapshot))]
[JsonSerializable(typeof(WindowApplicationActivationCompletion))]
internal partial class WindowApplicationActivationJsonContext : JsonSerializerContext;
