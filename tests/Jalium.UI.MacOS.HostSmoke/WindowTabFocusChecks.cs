using AppKit;
using CoreGraphics;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>AppKit Tab/Shift+Tab focus routing and editor indentation.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowTabFocusChecks
{
    private const int CaseCount = 3;

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-tab-focus-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window Tab focus host checks: {CaseCount - failed}/{CaseCount} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-tab-focus-case=".Length), out int index)
            || (uint)index >= CaseCount) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var first = Editor(index, "first 中文🙂");
        var second = Editor(index, "second 中文🙂");
        var disabled = new TextBox { Text = "disabled", IsEnabled = false };
        var hidden = new TextBox { Text = "hidden", Visibility = Visibility.Collapsed };
        var button = new Button { Content = "next focus target" };
        var panel = new StackPanel();
        foreach (var child in new UIElement[] { first, disabled, hidden, second, button })
            panel.Children.Add(child);
        var window = new Window
        {
            Content = panel, Width = 480, Height = 360,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false
        };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            window.Show();
            window.UpdateLayout();
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            string firstText = Text(first), secondText = Text(second);
            int changes = 0;
            first.TextChanged += (_, _) => changes++;
            second.TextChanged += (_, _) => changes++;
            Require(first.Focus(), "first editor focus rejected");
            for (int i = 0; i < 3; i++)
            {
                SendTab(view);
                Require(ReferenceEquals(Keyboard.FocusedElement, second), "Tab did not skip hidden/disabled editors");
                Require(FocusVisualManager.ShowFocusCues, "Tab did not enable keyboard focus cues");
                SendTab(view, reverse: true);
                Require(ReferenceEquals(Keyboard.FocusedElement, first), "Shift+Tab did not return to first editor");
            }
            Require(Text(first) == firstText && Text(second) == secondText && changes == 0,
                "focus navigation inserted text or raised TextChanged");

            second.IsReadOnly = true;
            SendTab(view);
            Require(ReferenceEquals(Keyboard.FocusedElement, second) && Text(second) == secondText,
                "read-only target did not remain focusable without editing");
            SendTab(view);
            Require(ReferenceEquals(Keyboard.FocusedElement, button), "Tab did not leave the read-only editor");
            SendTab(view, reverse: true);
            SendTab(view, reverse: true);
            Require(ReferenceEquals(Keyboard.FocusedElement, first), "reverse navigation did not restore focus");
            second.IsReadOnly = false;

            first.AcceptsTab = true;
            Require(((IImeSupport)first).TrySetImeSelection(0, 0), "indentation caret reset rejected");
            SendTab(view);
            Require(ReferenceEquals(Keyboard.FocusedElement, first) && Text(first) == "\t" + firstText && changes == 1,
                "AcceptsTab did not insert exactly once while retaining focus");
            first.Undo();
            Require(Text(first) == firstText, "indentation undo failed");
            first.Redo();
            Require(Text(first) == "\t" + firstText, "indentation redo failed");
            first.IsReadOnly = true;
            SendTab(view);
            Require(Text(first) == "\t" + firstText, "read-only AcceptsTab edited content");
            first.IsReadOnly = false;
            first.AcceptsTab = false;
            SendTab(view);
            Require(ReferenceEquals(Keyboard.FocusedElement, second), "turning off AcceptsTab did not restore navigation");
            // This inactive host deliberately has no live IME context. Character
            // entry after navigation is covered by the managed sequence tests
            // and the foreground Gallery interaction check.
            Require(disabled.Text == "disabled" && hidden.Text == "hidden", "skipped editors were modified");
            Console.WriteLine($"PASS: macOS Window Tab focus case {index}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: macOS Window Tab focus case {index}: {exception}");
            return 1;
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static TextBoxBase Editor(int index, string text) => index == 2
        ? new RichTextBox(FlowDocument.FromText(text)) { Height = 80 }
        : new TextBox { Text = text, AcceptsReturn = index == 1, Height = index == 1 ? 80 : 32 };

    private static string Text(TextBoxBase editor) => editor is RichTextBox rich
        ? rich.Document.GetText() : ((TextBox)editor).Text;

    private static void SendTab(NSView view, bool reverse = false) =>
        SendKey(view, 0x30, reverse ? "\u0019" : "\t", reverse ? NSEventModifierMask.ShiftKeyMask : 0);

    private static void SendKey(NSView view, ushort scan, string characters, NSEventModifierMask flags)
    {
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1,
            view.Window!.WindowNumber, null, characters, characters, false, scan)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 2,
            view.Window.WindowNumber, null, characters, characters, false, scan)!;
        view.KeyDown(down);
        view.KeyUp(up);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
