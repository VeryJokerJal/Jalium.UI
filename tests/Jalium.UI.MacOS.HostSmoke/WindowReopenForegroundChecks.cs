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
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jalium.UI.MacOS;

/// <summary>Foreground reopen delegate checks; activation is selected through the actual UI first. This is not a Dock click fixture.</summary>
internal static class WindowReopenForegroundChecks
{
    private const string CasePrefix = "--window-reopen-foreground-case=";

    internal static int RunAll() => RunCases(Enumerable.Range(0, 8).ToArray());

    internal static int RunCase(string argument) =>
        int.TryParse(argument.AsSpan(CasePrefix.Length), out int index) && (uint)index < 8
            ? RunCases([index]) : 2;

    private static int RunCases(int[] indices)
    {
        string? root = Environment.GetEnvironmentVariable("JALIUM_MACOS_REOPEN_GATE_ROOT");
        if (string.IsNullOrEmpty(root))
        {
            Console.Error.WriteLine("Foreground reopen checks require JALIUM_MACOS_REOPEN_GATE_ROOT and actual desktop selection.");
            return 77;
        }
        Directory.CreateDirectory(root);
        int gateSeconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_REOPEN_GATE_SECONDS"), out int seconds)
            && seconds is >= 30 and <= 900 ? seconds : 180;
        string resultPath = Path.Combine(root, "reopen-foreground.jsonl");
        using var results = new StreamWriter(new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        JaliumMacApplication.Initialize();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new MenuTestDelegate();
        host.Configure(app);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        int passed = 0;
        bool launched = false;
        bool started = false;
        NSWindow? gate = null;
        NSButton? start = null;
        NSTimer? gateTimer = null;
        using var launching = NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidFinishLaunchingNotification, _ =>
        {
            launched = true;
            // Return to NSApp.run before asking for desktop selection. A
            // blocking poll inside the launch callback cannot establish the
            // same foreground conditions as the normal AppKit event loop.
            gate = new NSWindow(new CGRect(240, 240, 500, 240), NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false) { Title = "窗口重开与焦点验证" };
            var heading = NSTextField.CreateLabel("点击开始后，检查两种标题栏的 8 个重开场景。");
            heading.Frame = new CGRect(24, 160, 452, 48);
            gate.ContentView!.AddSubview(heading);
            start = new NSButton(new CGRect(24, 80, 452, 44)) { Title = "开始前台重开检查", BezelStyle = NSBezelStyle.Rounded };
            gate.ContentView.AddSubview(start);
            string nonce = Guid.NewGuid().ToString("N");
            string ready = Path.Combine(root, $"ready-startup-{nonce}.json");
            string accepted = Path.Combine(root, $"accepted-startup-{nonce}.json");
            var timer = Stopwatch.StartNew();
            bool requested = false;
            TimeSpan? foregroundSince = null;
            const NSEventModifierMask activationKeys = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask
                | NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask;
            void RecordGate(string path, bool requested)
            {
                var value = new WindowReopenForegroundGateSnapshot(Environment.ProcessId, -1, nonce,
                    gate.Title, app.Active, gate.IsKeyWindow, gate.IsMainWindow,
                    (long)gate.ContentView.Handle, (long)gate.Handle, requested,
                    app.ActivationPolicy.ToString(), (long)app.Handle, (long)NSApplication.SharedApplication.Handle,
                    (ulong)NSEvent.CurrentModifierFlags, foregroundSince is { } since ? (timer.Elapsed - since).TotalMilliseconds : 0);
                File.WriteAllText(path, JsonSerializer.Serialize(value,
                    WindowReopenForegroundJsonContext.Default.WindowReopenForegroundGateSnapshot));
            }
            void StartWhenForeground()
            {
                if (started) return;
                if (!requested || !app.Active || !gate.IsKeyWindow || !gate.IsMainWindow ||
                    (NSEvent.CurrentModifierFlags & activationKeys) != 0)
                {
                    foregroundSince = null;
                    return;
                }
                foregroundSince ??= timer.Elapsed;
                // App switching must finish before a test starts minimizing
                // windows. Keep the foreground requirements unchanged.
                if (timer.Elapsed - foregroundSince.Value < TimeSpan.FromSeconds(1)) return;
                started = true;
                RecordGate(accepted, true);
                start.Enabled = false;
                gateTimer?.Invalidate();
                gate.OrderOut(app);
                app.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        foreach (int index in indices)
                            if (RunScenario(index, app, host, results) == 0) passed++;
                    }
                    finally { Quit(); }
                });
            }
            start.Activated += (_, _) =>
            {
                requested = true;
                Console.WriteLine($"FOREGROUND_REOPEN_START_REQUEST active={app.Active} key={gate.IsKeyWindow} main={gate.IsMainWindow}");
                RecordGate(ready, true);
                // AppKit's activation notifications may follow the button
                // action. Retain that request until the real foreground
                // state arrives, without accepting an inactive application.
                app.Activate();
                gate.MakeKeyAndOrderFront(app);
                StartWhenForeground();
            };
            gateTimer = NSTimer.CreateRepeatingScheduledTimer(.1, _ =>
            {
                RecordGate(ready, requested);
                StartWhenForeground();
                if (!started && timer.Elapsed >= TimeSpan.FromSeconds(gateSeconds))
                {
                    Console.Error.WriteLine("Foreground startup gate was not selected; no reopen assertions ran.");
                    gateTimer?.Invalidate();
                    Quit();
                }
            });
            app.Activate();
            gate.MakeKeyAndOrderFront(app);
            RecordGate(ready, false);
            Console.WriteLine($"FOREGROUND_REOPEN_READY pid={Environment.ProcessId} nonce={nonce}");
        }, app);
        int exit = (int)typeof(NativeMethods).GetMethod("PlatformRunMessageLoop", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;
        gateTimer?.Invalidate();
        gate?.Close();
        start?.Dispose();
        gateTimer?.Dispose();
        gate?.Dispose();
        Require(exit == 0 && launched, "foreground checks did not complete a successful AppKit launch");
        if (!started)
        {
            Console.Error.WriteLine("macOS foreground Window reopen host checks: not run; actual desktop selection is required.");
            return 77;
        }
        Console.WriteLine($"macOS foreground Window reopen host checks: {passed}/{indices.Length} passed");
        return passed == indices.Length ? 0 : 1;

        void Quit() => typeof(NativeMethods).GetMethod("PlatformQuit", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [0]);
    }

    private static int RunScenario(int index, NSApplication app, MenuTestDelegate host, StreamWriter results)
    {
        bool custom = index % 2 != 0;
        int mode = index / 2;
        bool modal = mode == 3;
        string scenario = mode switch { 0 => "隐藏", 1 => "普通最小化", 2 => "最大化后最小化", _ => "模态最小化" };
        var titleBar = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native;
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var editor = new TextBox { Text = "Reopen 中文 Miii 🙂 é", MinHeight = 48, FontSize = 16 };
        AutomationProperties.SetName(editor, "前台重开验证编辑框");
        var label = new TextBlock { Text = "重开后应保留编辑内容、选区与焦点。", FontSize = 15, Foreground = ink };
        AutomationProperties.SetLabeledBy(editor, label);
        var status = new TextBlock
        {
            Text = "正在检查；请保持当前桌面状态。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink
        };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "窗口重开与焦点", FontSize = 26, Foreground = ink });
        body.Children.Add(label);
        body.Children.Add(editor);
        body.Children.Add(status);
        var owner = CreateWindow("重开检查所属窗口", titleBar, new TextBlock { Text = "模态检查期间此窗口保持禁用。" });
        var window = modal ? CreateWindow($"前台重开 · {(custom ? "自定义" : "原生")} · {scenario}", titleBar, body) : owner;
        if (!modal)
        {
            window.Title = $"前台重开 · {(custom ? "自定义" : "原生")} · {scenario}";
            window.Content = body;
        }
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.MainWindow = owner;
        int closes = 0;
        window.Closing += (_, _) => closes++;
        Exception? failure = null;
        try
        {
            owner.Show(); Pump();
            if (modal)
            {
                window.Owner = owner;
                window.Shown += (_, _) => app.BeginInvokeOnMainThread(() =>
                {
                    try { Verify(); }
                    catch (Exception error) { failure = error; }
                    finally { window.DialogResult = true; }
                });
                Require(window.ShowDialog() == true, "modal completion failed");
                Require(owner.IsEnabled, "modal return did not restore its owner");
            }
            else Verify();
            if (failure != null) throw failure;
            Console.WriteLine($"PASS: {titleBar}: {scenario}: two reopens retain actual focus, selection, geometry and undo history");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL foreground reopen {index}: {error}");
            return 1;
        }
        finally
        {
            window.Close();
            if (!ReferenceEquals(owner, window)) owner.Close();
        }

        void Verify()
        {
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var native = view.Window!;
            window.Activate();
            bool initialForeground = WaitUntil(() => app.Active && native.IsKeyWindow && native.IsMainWindow && window.IsActive);
            if (!initialForeground) Capture("initial-foreground-failed", -1);
            Require(initialForeground,
                "the foreground scenario did not acquire its initial key and main window");
            Require(editor.Focus(), "initial editor focus failed");
            if (mode == 2)
            {
                window.WindowState = WindowState.Maximized;
                Require(WaitUntil(() => window.WindowState == WindowState.Maximized), "initial maximization did not complete");
            }
            Require(native.FirstResponder?.Handle == view.Handle, "initial native input view is not the first responder");
            string before = editor.Text;
            var support = (IImeSupport)editor;
            Require(support.TryReplaceImeText(before.Length, 0, " · 追加🙂"), "initial undoable edit failed");
            string edited = editor.Text;
            editor.Select(before.Length - 3, 3);
            int selectionStart = editor.SelectionStart, selectionLength = editor.SelectionLength;
            nint handle = window.Handle, nativeHandle = native.Handle;
            var bounds = window.RestoreBounds;
            var requestedState = mode == 2 ? WindowState.Maximized : WindowState.Normal;
            Require(app.Active && native.IsKeyWindow && native.IsMainWindow, "initial foreground selection was not retained");
            for (int repetition = 0; repetition < 2; repetition++)
            {
                Capture("before", repetition);
                if (mode == 0)
                {
                    window.Hide();
                    Require(window.Visibility == Visibility.Hidden && !native.IsVisible, "Window.Hide did not hide the same native window");
                }
                else
                {
                    window.WindowState = WindowState.Minimized;
                    Require(WaitUntil(() => native.IsMiniaturized), "native miniaturization did not finish");
                }
                Capture("unavailable", repetition);
                Require(!host.ApplicationShouldHandleReopen(app, mode != 0), "handled reopen permits default untitled handling");
                bool restored = WaitUntil(() => app.Active && native.IsVisible && !native.IsMiniaturized
                    && native.IsKeyWindow && native.IsMainWindow && window.IsActive
                    && editor.IsKeyboardFocused && native.FirstResponder?.Handle == view.Handle
                    && window.WindowState == requestedState);
                if (!restored) Capture("reopen-failed", repetition);
                Require(restored, "reopen did not restore actual foreground, editor and native responder focus");
                Require(window.Handle == handle && native.Handle == nativeHandle && closes == 0,
                    "reopen closed or recreated the native window");
                Require(window.Visibility == Visibility.Visible && editor.Text == edited
                    && editor.SelectionStart == selectionStart && editor.SelectionLength == selectionLength,
                    "reopen changed visibility, edited text or selection");
                Require(window.RestoreBounds == bounds, "reopen changed the normal restoration geometry");
                if (modal) Require(window.IsModal && window.IsEnabled && !owner.IsEnabled,
                    "reopen ended modality or enabled its owner");
                Capture("restored", repetition);
            }
            // Go through AppKit's existing native input view, without focusing
            // the editor again. This also exercises the native key routing.
            using var key = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, 0, 1,
                native.WindowNumber, null, "N", "N", false, 45)!;
            native.SendEvent(key);
            Pump();
            string typed = edited.Remove(selectionStart, selectionLength).Insert(selectionStart, "N");
            Require(editor.Text == typed, "the restored native responder did not route text into the editor");
            Require(app.SendAction(new Selector("undo:"), view, null) && editor.Text == edited,
                "continued editing did not retain undo history");
            Require(app.SendAction(new Selector("undo:"), view, null) && editor.Text == before,
                "reopen lost the edit made before suspension");
            Require(app.SendAction(new Selector("redo:"), view, null) && editor.Text == edited,
                "reopen lost redo history");
            Capture("edited-and-undone", 2);

            void Capture(string phase, int repetition)
            {
                var snapshot = new WindowReopenForegroundSnapshot(Environment.ProcessId, DateTimeOffset.UtcNow,
                    index, phase, repetition, titleBar.ToString(), scenario, app.Active,
                    (long)window.Handle, (long)native.Handle, native.IsKeyWindow, native.IsMainWindow,
                    native.IsVisible, native.IsMiniaturized, native.FirstResponder?.Handle == view.Handle,
                    window.IsActive, window.IsModal, owner.IsEnabled, editor.IsKeyboardFocused,
                    editor.Text, editor.SelectionStart, editor.SelectionLength, window.WindowState.ToString(),
                    window.Visibility.ToString(), window.RestoreBounds.ToString());
                results.WriteLine(JsonSerializer.Serialize(snapshot,
                    WindowReopenForegroundJsonContext.Default.WindowReopenForegroundSnapshot));
                results.Flush();
            }
        }
    }

    private static Window CreateWindow(string title, WindowTitleBarStyle style, UIElement content) => new()
    {
        Title = title, Width = 480, Height = 360, ShowActivated = false,
        TitleBarStyle = style, Content = content,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
    };

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.ProcessQueue();
        NativeMethods.PlatformPollEvents();
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(8)) Pump();
        return condition();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed record WindowReopenForegroundSnapshot(int Pid, DateTimeOffset Utc, int Case, string Phase,
    int Repetition, string TitleBar, string Scenario, bool AppActive, long ContentHandle, long NativeHandle,
    bool NativeKey, bool NativeMain, bool NativeVisible, bool NativeMinimized, bool ViewFirstResponder,
    bool WindowActive, bool Modal, bool OwnerEnabled, bool EditorFocused, string Text, int SelectionStart,
    int SelectionLength, string State, string Visibility, string RestorationGeometry);

internal sealed record WindowReopenForegroundGateSnapshot(int Pid, int Case, string Nonce, string Title,
    bool AppActive, bool NativeKey, bool NativeMain, long ContentHandle, long NativeHandle, bool UserRequested,
    string ActivationPolicy, long ApplicationHandle, long SharedApplicationHandle, ulong Modifiers, double ForegroundStableMs);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WindowReopenForegroundSnapshot))]
[JsonSerializable(typeof(WindowReopenForegroundGateSnapshot))]
internal partial class WindowReopenForegroundJsonContext : JsonSerializerContext;
