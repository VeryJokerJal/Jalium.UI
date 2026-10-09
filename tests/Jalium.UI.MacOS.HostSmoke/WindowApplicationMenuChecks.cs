using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

/// <summary>AppKit Command-H dispatch with real ordinary and modal windows; no Hide Others or Dock UI automation.</summary>
internal static class WindowApplicationMenuChecks
{
    internal static int RunAll()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JALIUM_MACOS_APPLICATION_MENU_GATE_ROOT")))
        {
            Console.Error.WriteLine("Application visibility checks require JALIUM_MACOS_APPLICATION_MENU_GATE_ROOT and foreground activation before each case.");
            return 77;
        }
        int passed = 0;
        for (int index = 0; index < 4; index++)
        {
            // Keep the application identity stable while native menus and
            // ordinary/modal windows are exercised in its real run loop.
            if (RunCase($"--window-application-menu-case={index}") == 0) passed++;
        }
        Console.WriteLine($"macOS application visibility menu host checks: {passed}/4 passed");
        return passed == 4 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-application-menu-case=".Length), out int index) || (uint)index >= 4) return 2;
        string? gateRoot = Environment.GetEnvironmentVariable("JALIUM_MACOS_APPLICATION_MENU_GATE_ROOT");
        if (string.IsNullOrEmpty(gateRoot)) return 77;
        JaliumMacApplication.Initialize();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new MenuTestDelegate();
        host.Configure(app);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new TextBox { Text = "隐藏前 · 中文 Miii 🙂 é", Height = 48 };
        var owner = CreateWindow("Application hide owner", index, new TextBlock { Text = "模态 owner" });
        var window = index < 2 ? owner : CreateWindow("Application hide modal", index, editor);
        if (index < 2) owner.Content = editor;
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.MainWindow = owner;
        int closes = 0, hidden = 0, restored = 0;
        window.Closing += (_, _) => closes++;
        using var hide = NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidHideNotification, _ => hidden++, app);
        using var unhide = NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidUnhideNotification, _ => restored++, app);
        Exception? failure = null;
        bool verified = false;
        try
        {
            owner.Show(); Pump();
            app.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (index >= 2)
                    {
                        window.Owner = owner;
                        window.Shown += (_, _) =>
                        {
                            try { Verify(); }
                            catch (Exception error) { failure = error; }
                            finally { window.DialogResult = true; }
                        };
                        Require(window.ShowDialog() == true, "modal completion failed");
                        Require(owner.IsEnabled, "modal completion left its owner disabled");
                    }
                    else Verify();
                    verified = failure == null;
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    typeof(NativeMethods).GetMethod("PlatformQuit", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [0]);
                }
            });
            int exit = (int)typeof(NativeMethods).GetMethod("PlatformRunMessageLoop", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null)!;
            Require(exit == 0, "native AppKit run loop returned a failure");
            if (failure != null) throw failure;
            Require(verified, "AppKit stopped before verification completed");
            Console.WriteLine($"PASS: {(index % 2 == 0 ? "Native" : "Custom")} {(index < 2 ? "ordinary" : "modal")} Command-H preserves window, text, selection and undo history");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {index}: {error}"); return 1; }
        finally
        {
            app.UnhideWithoutActivation();
            window.Close();
            if (!ReferenceEquals(owner, window)) owner.Close();
        }

        void Verify()
        {
            WaitForForegroundGate(window, app, gateRoot, index, 0);
            Require(editor.Focus(), "initial editor focus failed");
            var support = (IImeSupport)editor;
            string before = editor.Text;
            Require(support.TryReplaceImeText(before.Length, 0, " · 修改🙂"), "undoable replacement failed");
            editor.Select(3, 4);
            string edited = editor.Text;
            int start = editor.SelectionStart, length = editor.SelectionLength;
            nint handle = window.Handle;
            var view = Runtime.GetNSObject<NSView>(handle)!;
            nint nativeHandle = view.Window!.Handle;
            for (int repetition = 0; repetition < 2; repetition++)
            {
                if (repetition != 0) WaitForForegroundGate(window, app, gateRoot, index, repetition);
                Require(app.Active && view.Window.IsKeyWindow,
                    $"Command-H fixture did not become the active key window at repetition {repetition}: active={app.Active}, key={view.Window.IsKeyWindow}, hidden={app.Hidden}");
                app.MainMenu!.Update();
                Require(app.MainMenu.Items[0].Submenu!.Items.Single(item => item.Action?.Name == "hide:").Enabled,
                    "Hide action was disabled in the active application");
                using var key = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty,
                    NSEventModifierMask.CommandKeyMask, 1, view.Window.WindowNumber, null, "h", "h", false, 4)!;
                Require(app.MainMenu!.PerformKeyEquivalent(key), "AppKit did not dispatch Command-H through its menu");
                Require(WaitUntil(() => app.Hidden), "Command-H did not hide the application");
                CheckRetained();
                app.Unhide(app);
                Require(WaitUntil(() => !app.Hidden && view.Window.IsVisible),
                    $"native Unhide did not restore its existing window: hidden={app.Hidden}, visible={view.Window.IsVisible}, active={app.Active}, key={view.Window.IsKeyWindow}");
                CheckRetained();
            }
            Require(hidden == 2 && restored == 2, $"unexpected native hide/unhide notifications: {hidden}/{restored}");
            WaitForForegroundGate(window, app, gateRoot, index, 2);
            CheckRetained();
            Require(editor.Focus(), "restored editor focus failed");
            Require(app.SendAction(new Selector("undo:"), view, null) && editor.Text == before, "hide/unhide lost undo history");
            Require(app.SendAction(new Selector("redo:"), view, null) && editor.Text == edited, "hide/unhide lost redo history");

            void CheckRetained()
            {
                Require(window.Handle == handle && view.Window!.Handle == nativeHandle && closes == 0,
                    "application hide closed or recreated its window");
                Require(window.Visibility == Visibility.Visible && editor.Text == edited &&
                    editor.SelectionStart == start && editor.SelectionLength == length, "application hide changed managed visibility, text or selection");
                if (index >= 2) Require(window.IsModal && !owner.IsEnabled && window.IsEnabled,
                    "application hide ended its modal session or enabled the owner");
            }
        }
    }

    private static Window CreateWindow(string title, int index, UIElement content) => new()
    {
        Title = title, Width = 400, Height = 260, ShowActivated = false,
        TitleBarStyle = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)), Content = content
    };

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.ProcessQueue(); NativeMethods.PlatformPollEvents();
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
    }

    private static void WaitForForegroundGate(Window window, NSApplication app, string root, int index, int stage)
    {
        var native = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
        Directory.CreateDirectory(root);
        string nonce = Guid.NewGuid().ToString("N");
        string ready = Path.Combine(root, $"ready-{index}-{stage}.json");
        string permit = Path.Combine(root, $"permit-{index}-{stage}.json");
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_APPLICATION_MENU_GATE_SECONDS"), out int suppliedSeconds)
            && suppliedSeconds is > 0 and <= 600 ? suppliedSeconds : 90;
        window.Activate();
        var timer = Stopwatch.StartNew();
        string? last = null;
        while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            // Establish this foreground fixture's key selection after AppKit
            // has restored the application. Hide/Unhide does not promise key
            // selection, and this check does not exercise Window.Activate.
            if (app.Active && !native.IsKeyWindow) native.MakeKeyAndOrderFront(app);
            string snapshot = JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId, @case = index, stage, nonce,
                phase = "awaiting-foreground", title = window.Title,
                titlebar = window.TitleBarStyle.ToString(), appActive = app.Active,
                appHidden = app.Hidden, activationPolicy = app.ActivationPolicy.ToString(),
                applicationHandle = (long)app.Handle,
                sharedApplicationHandle = (long)NSApplication.SharedApplication.Handle,
                keyWindowHandle = (long)(app.KeyWindow?.Handle ?? 0),
                mainWindowHandle = (long)(app.MainWindow?.Handle ?? 0),
                nativeKey = native.IsKeyWindow, nativeMain = native.IsMainWindow,
                nativeVisible = native.IsVisible, canBecomeKey = native.CanBecomeKeyWindow,
                contentHandle = (long)window.Handle, nativeHandle = (long)native.Handle
            });
            if (snapshot != last) { File.WriteAllText(ready, snapshot); last = snapshot; }
            if (app.Active && native.IsKeyWindow && File.Exists(permit))
            {
                try
                {
                    using var supplied = JsonDocument.Parse(File.ReadAllText(permit));
                    var value = supplied.RootElement;
                    if (value.GetProperty("pid").GetInt32() == Environment.ProcessId &&
                        value.GetProperty("case").GetInt32() == index && value.GetProperty("stage").GetInt32() == stage &&
                        value.GetProperty("nonce").GetString() == nonce)
                    {
                        File.WriteAllText(Path.Combine(root, $"accepted-{index}-{stage}.json"), snapshot);
                        return;
                    }
                }
                catch (JsonException) { }
            }
            Pump();
        }
        throw new InvalidOperationException($"Foreground validation gate {index}-{stage} timed out; activation and a matching permit are required before assertions.");
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(4)) Pump();
        return condition();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
