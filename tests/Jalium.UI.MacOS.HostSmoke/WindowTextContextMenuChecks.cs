using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using System.Diagnostics;
using System.Reflection;

namespace Jalium.UI.MacOS;

/// <summary>Own-process AppKit menu tracking and actions; no desktop input or clipboard mutation.</summary>
internal static class WindowTextContextMenuChecks
{
    private const int CaseCount = 28;
    private const string CasePrefix = "--window-text-context-menu-case=";

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add(CasePrefix + index);
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(30_000)) { child.Kill(); child.WaitForExit(); failed++; }
            else if (child.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS default text context menu host checks: {CaseCount - failed}/{CaseCount} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan(CasePrefix.Length), out int index) || (uint)index >= CaseCount) return 2;
        bool heldButtonCase = index >= 20;
        int kind = heldButtonCase ? (index - 20) % 4 : index % 10;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        const string sample = "中文🙂 alpha beta";
        UIElement editor = kind switch
        {
            1 or 5 => new RichTextBox(FlowDocument.FromText(sample)),
            2 or 6 => new EditControl { Text = sample },
            3 => new PasswordBox { Password = "fixture private text" },
            _ => new TextBox { Text = sample }
        };
        ((FrameworkElement)editor).Height = 160;
        var sibling = new Button { Content = "outside editor" };
        var panel = new StackPanel(); panel.Children.Add(editor); panel.Children.Add(sibling);
        var window = new Window
        {
            Title = $"Text context menu case {index}", Content = panel, Width = 500, Height = 380,
            ShowActivated = false, TitleBarStyle = index < 10 || index is >= 20 and < 24 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom
        };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        NSTimer? completion = null;
        Exception? failure = null;
        bool tracked = false, callbackRan = false;
        int keyEvents = 0;
        void State(string phase)
        {
            string text = $"V292-MENU {phase}: left={Mouse.LeftButton} right={Mouse.RightButton} capture={Mouse.Captured?.GetType().Name} editorCapture={ReferenceEquals(Mouse.Captured,editor)} active={window.IsActive} nativeActive={NSApplication.SharedApplication.Active} foreground={NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier} self={Environment.ProcessId} key={window.Handle}";
            Console.Error.WriteLine(text);
            if (Environment.GetEnvironmentVariable("JALIUM_TASKBAR_OUTPUT") is string output)
                File.AppendAllText(Path.Combine(output,"menu-state.log"),text + Environment.NewLine);
        }
        editor.LostMouseCapture += (_, _) => State("lost-capture");
        try
        {
            NSApplication.SharedApplication.FinishLaunching();
            window.Show(); window.UpdateLayout();
            Require(editor.Focus(), "editor focus rejected");
            if (editor is PasswordBox password) password.SelectAll();
            else Require(((IImeSupport)editor).TrySetImeSelection(0, kind == 9 ? 0 : 2), "selection rejected");
            if (editor is TextBoxBase text) text.IsReadOnly = kind is 4 or 5;
            if (editor is EditControl code) code.IsReadOnly = kind == 6;
            editor.PreviewKeyDown += (_, _) => keyEvents++;
            editor.PreviewKeyUp += (_, _) => keyEvents++;
            using var observer = NSNotificationCenter.DefaultCenter.AddObserver(NSMenu.DidBeginTrackingNotification, notice =>
            {
                if (notice.Object is not NSMenu menu || tracked) return;
                tracked = true;
                State("tracking-start");
                completion = NSTimer.CreateTimer(TimeSpan.FromMilliseconds(100), _ =>
                {
                    try
                    {
                        callbackRan = true;
                        State("tracking-callback");
                        if (kind == 7) Require(sibling.Focus(), "focus change rejected");
                        if (kind == 8) window.Close();
                        menu.Update();
                        NSMenuItem Item(PlatformTextEditingCommand command) => menu.Items.Single(item =>
                            item.Action?.Name == "performEditingCommand:" && item.Tag == (nint)command);
                        bool stale = kind is 7 or 8;
                        Require(Item(PlatformTextEditingCommand.Copy).Enabled == (!stale && kind is not (3 or 9)), "copy state");
                        Require(Item(PlatformTextEditingCommand.Cut).Enabled == (!stale && kind is not (3 or 4 or 5 or 6 or 9)), "cut state");
                        if (kind is 4 or 5 or 6) Require(!Item(PlatformTextEditingCommand.Paste).Enabled, "read-only paste");
                        var all = Item(PlatformTextEditingCommand.SelectAll);
                        Require(all.Enabled == !stale, "select-all state");
                        foreach (var item in menu.Items.Where(item => item.Action?.Name == "performEditingCommand:"))
                            Require(item.KeyEquivalent.Length == 0, "context menu duplicated a shortcut");
                        int before = keyEvents;
                        // Deliberately invoke even a disabled item: the retained
                        // action target itself must revalidate the original editor.
                        Require(NSApplication.SharedApplication.SendAction(all.Action!, all.Target, all), "registered menu target missing");
                        Require(stale ? keyEvents == before : keyEvents == before + 2, "stale action or missing native key pair");
                        if (!stale && editor is not PasswordBox)
                        {
                            bool allSelected = editor switch
                            {
                                TextBox text => text.SelectionLength == text.Text.Length,
                                RichTextBox rich => rich.Selection.Text.Contains(sample, StringComparison.Ordinal),
                                EditControl code => code.SelectionLength == code.Text.Length,
                                _ => false
                            };
                            Require(allSelected, "native action did not select all text");
                        }
                    }
                    catch (Exception error) { failure = error; }
                    finally { menu.CancelTracking(); }
                });
                NSRunLoop.Main.AddTimer(completion, NSRunLoopMode.EventTracking);
            });
            var position = editor.TranslatePoint(new Point(80, 32), window);
            bool controlClick = index % 2 == 0;
            uint heldBit = controlClick ? 2u : 1u;
            var dispatch = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Collections.IDictionary? pointerTargets = null;
            object? originalPointerTarget = null;
            if (heldButtonCase)
            {
                var heldPosition = controlClick ? sibling.TranslatePoint(new Point(10, 10), window) : position;
                dispatch.Invoke(window, [new PlatformEvent
                {
                    Type = PlatformEventType.MouseDown, MouseX = (float)(heldPosition.X * window.DpiScale),
                    MouseY = (float)(heldPosition.Y * window.DpiScale), Button = controlClick ? 1 : 0,
                    MouseButtons = heldBit, HasMouseButtonStates = true
                }]);
                Require(editor.CaptureMouse(), "held button capture rejected");
                object dispatcher = typeof(Window).GetField("_inputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                pointerTargets = (System.Collections.IDictionary)dispatcher.GetType()
                    .GetField("_activePointerTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dispatcher)!;
                originalPointerTarget = pointerTargets[1u];
                State("held-before-menu");
            }
            // Feed only this fixture's platform bridge; the native menu itself
            // tracks on AppKit's main thread. This is not physical desktop input.
            typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [new PlatformEvent
                {
                    Type = PlatformEventType.MouseDown,
                    MouseX = (float)(position.X * window.DpiScale), MouseY = (float)(position.Y * window.DpiScale),
                    Button = controlClick ? 0 : 1, Modifiers = controlClick ? 0x02 : 0,
                    MouseButtons = heldButtonCase ? 3u : controlClick ? 1u : 2u, HasMouseButtonStates = true
                }]);
            State("menu-returned");
            if (heldButtonCase) State($"pointer-before={originalPointerTarget?.GetType().Name} after={pointerTargets![1u]?.GetType().Name}");
            Require(tracked, "default native context menu was not tracked");
            if (!callbackRan && !NSApplication.SharedApplication.Active &&
                NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier != Environment.ProcessId)
            {
                if (Environment.GetEnvironmentVariable("JALIUM_TASKBAR_OUTPUT") is string unavailableOutput)
                    File.WriteAllText(Path.Combine(unavailableOutput,"sdk-result.json"),"{\"passed\":false,\"unavailable\":true,\"reason\":\"foreground-lost-before-native-action\"}");
                Console.Error.WriteLine("UNAVAILABLE: native menu lost foreground before its action callback");
                return 3;
            }
            Require(callbackRan, "native menu action callback was never exercised");

            if (heldButtonCase)
            {
                Require(ReferenceEquals(Mouse.Captured, editor) && ReferenceEquals(pointerTargets![1u], originalPointerTarget),
                    "native menu retired another button's capture or pointer session");
                Require(Mouse.LeftButton == (controlClick ? MouseButtonState.Released : MouseButtonState.Pressed) &&
                    Mouse.RightButton == (controlClick ? MouseButtonState.Pressed : MouseButtonState.Released),
                    "native menu changed another button's state");
                int releases = 0;
                editor.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => releases++), true);
                dispatch.Invoke(window, [new PlatformEvent
                {
                    Type = PlatformEventType.MouseUp, MouseX = (float)(position.X * window.DpiScale),
                    MouseY = (float)(position.Y * window.DpiScale), Button = controlClick ? 1 : 0,
                    MouseButtons = 0, HasMouseButtonStates = true
                }]);
                editor.ReleaseMouseCapture();
                Require(releases == 1 && !pointerTargets!.Contains(1u), "remaining button did not finish its original pointer session");
            }
            Require(Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released,
                "native menu retained its terminating mouse press");
            if (failure != null) throw failure;
            if (Environment.GetEnvironmentVariable("JALIUM_TASKBAR_OUTPUT") is string output)
                File.WriteAllText(Path.Combine(output,"sdk-result.json"),$"{{\"case\":{index},\"passed\":true}}");
            Console.WriteLine($"PASS: default text context menu case {index}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: default text context menu case {index}: {error}"); return 1;
        }
        finally { completion?.Invalidate(); completion?.Dispose(); window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
