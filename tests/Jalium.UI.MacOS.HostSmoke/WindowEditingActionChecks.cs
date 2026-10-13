using AppKit;
using CoreGraphics;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Own-process AppKit responder actions and physical Control/Command separation.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowEditingActionChecks
{
    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < 5; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-editing-actions-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window editing action host checks: {5 - failed}/5 passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-editing-actions-case=".Length), out int index)
            || (uint)index >= 5) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        UIElement target = index switch
        {
            0 => new TextBox { Text = "first\n  alpha beta\nlast", AcceptsReturn = true },
            1 => new EditControl { Text = "first\n  alpha beta\nlast" },
            2 => new RichTextBox(FlowDocument.FromText("first\n  alpha beta\nlast")),
            4 => new PasswordBox { Password = "fixture secret" },
            _ => new Button { Content = "native editing action receiver" }
        };
        var window = new Window
        {
            Content = target, Width = 420, Height = 260,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false
        };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            window.Show(); window.UpdateLayout();
            Require(target.Focus(), "managed target focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var events = new List<KeyEventArgs>();
            target.PreviewKeyDown += (_, e) => events.Add(e);
            target.PreviewKeyUp += (_, e) => events.Add(e);
            if (index < 3)
            {
                IImeSupport editor = (IImeSupport)target;
                Require(editor.TrySetImeSelection(14, 0), "selection reset rejected");
                SendAction(view, "selectAll:");
                Require(editor.TryGetImeSurroundingText(out var selected), "text snapshot rejected");
                int end = target is RichTextBox rich ? rich.Document.ContentEnd.DocumentOffset : selected.Text.Length;
                Require(selected.AnchorIndex == 0 && selected.CursorIndex == end,
                    $"native selectAll selected wrong range: kind={index}, cursor={selected.CursorIndex}, anchor={selected.AnchorIndex}, expectedEnd={end}");
                Require(events.Count == 2 && events.All(e => e.PhysicalModifiers == ModifierKeys.Windows),
                    "native selectAll must carry Command for both phases");

                Require(editor.TrySetImeSelection(14, 0), "physical Control selection reset rejected");
                SendKey(view, NSEventModifierMask.ControlKeyMask);
                Require(editor.TryGetImeSurroundingText(out var lineStart) &&
                    lineStart.CursorIndex == 6 && lineStart.AnchorIndex == 6,
                    "physical Control-A must still move to line start");

                Require(editor.TrySetImeSelection(14, 0), "physical Command selection reset rejected");
                SendKey(view, NSEventModifierMask.CommandKeyMask);
                Require(editor.TryGetImeSurroundingText(out var all) && all.CursorIndex == end && all.AnchorIndex == 0,
                    "physical Command-A must still select all");
            }
            else if (index == 4)
            {
                var password = (PasswordBox)target;
                Require(!((IImeSupport)password).TryGetImeSurroundingText(out _), "password exposed surrounding text");
                SendKey(view, NSEventModifierMask.ControlKeyMask);
                SendKey(view, 0, 0x33, "\u007f");
                Require(password.Password == "fixture secret", "physical Control-A must collapse at field start");
                SendAction(view, "selectAll:");
                SendKey(view, 0, 0x33, "\u007f");
                Require(password.Password.Length == 0, "native selectAll did not select the secure field");
                password.Password = "readonly fixture";
                password.IsReadOnly = true;
                SendAction(view, "selectAll:");
                SendKey(view, 0, 0x33, "\u007f");
                Require(password.Password == "readonly fixture", "native action bypassed secure field read-only guard");
                Require(!((IImeSupport)password).TryGetImeSurroundingText(out _), "native action exposed secure text");
            }
            else
            {
                // A non-editing receiver checks all six commands without reading
                // or writing the user's system pasteboard.
                foreach (var (action, key, modifiers) in new[]
                {
                    ("copy:", Key.C, ModifierKeys.Windows), ("cut:", Key.X, ModifierKeys.Windows),
                    ("paste:", Key.V, ModifierKeys.Windows), ("selectAll:", Key.A, ModifierKeys.Windows),
                    ("undo:", Key.Z, ModifierKeys.Windows), ("redo:", Key.Z, ModifierKeys.Windows | ModifierKeys.Shift)
                })
                {
                    int before = events.Count;
                    SendAction(view, action);
                    Require(events.Count == before + 2 && events[^2].RoutedEvent == Keyboard.PreviewKeyDownEvent &&
                        events[^1].RoutedEvent == Keyboard.PreviewKeyUpEvent &&
                        events[^2].Key == key && events[^1].Key == key &&
                        events[^2].PhysicalModifiers == modifiers && events[^1].PhysicalModifiers == modifiers,
                        $"native editing action key/modifier phases mismatch: {action}");
                    window.IsEnabled = false;
                    int disabledCount = events.Count;
                    SendAction(view, action);
                    Require(events.Count == disabledCount, $"disabled window accepted {action}");
                    window.IsEnabled = true;
                }
                window.Close();
                int closedCount = events.Count;
                SendAction(view, "selectAll:");
                Require(events.Count == closedCount, "closed window accepted late editing action");
            }
            Console.WriteLine($"PASS: native Window editing action case {index}"); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"FAIL: native Window editing action case {index}: {exception}"); return 1; }
        finally { window.Close(); }
    }

    private static void SendAction(NSView view, string action) =>
        Require(NSApplication.SharedApplication.SendAction(new Selector(action), view, null),
            $"AppKit rejected responder action {action}");

    private static void SendKey(NSView view, NSEventModifierMask flags, ushort scan = 0, string characters = "a")
    {
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1,
            view.Window!.WindowNumber, null, characters, characters, false, scan)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 2,
            view.Window.WindowNumber, null, characters, characters, false, scan)!;
        view.KeyDown(down); view.KeyUp(up);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
