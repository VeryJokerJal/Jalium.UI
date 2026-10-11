using AppKit;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.Versioning;
using Selector = ObjCRuntime.Selector;

namespace Jalium.UI.MacOS;

/// <summary>Real NSMenu automatic validation, without mutating the general pasteboard.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowEditingMenuChecks
{
    private static readonly string[] Actions = ["copy:", "cut:", "paste:", "selectAll:", "undo:", "redo:"];
    private const int CaseCount = 14;

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < CaseCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-editing-menu-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill(); process.WaitForExit(); failed++;
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window editing menu host checks: {CaseCount - failed}/{CaseCount} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-editing-menu-case=".Length), out int index)
            || (uint)index >= CaseCount) return 2;
        int kind = index % 7;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        UIElement editor = kind switch
        {
            0 => new TextBox { Text = "native menu text" },
            1 => new RichTextBox(FlowDocument.FromText("native menu text")),
            2 => new EditControl { Text = "native menu text" },
            3 => new PasswordBox { Password = "fixture private text" },
            4 => new Button { Content = "non-editing target" },
            5 => new Terminal { IsReadOnly = true, AutoStartShell = false },
            _ => new HexEditor { Data = [0x41, 0x42, 0x43] }
        };
        var sibling = new Button { Content = "focus outside editor" };
        var panel = new StackPanel(); panel.Children.Add(editor); panel.Children.Add(sibling);
        var window = new Window
        {
            Content = panel, Width = 500, Height = 380, ShowActivated = false,
            TitleBarStyle = index < 7 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom
        };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var failures = new List<string>();
        try
        {
            window.Show(); window.UpdateLayout();
            Require(editor.Focus(), "editor focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            using var menu = new NSMenu("local editing validation") { AutoEnablesItems = true };
            foreach (string action in Actions)
                menu.AddItem(new NSMenuItem(action, new Selector(action), string.Empty) { Target = view });
            int keyEvents = 0;
            editor.PreviewKeyDown += (_, _) => keyEvents++;
            editor.PreviewKeyUp += (_, _) => keyEvents++;
            bool Enabled(string action)
            {
                menu.Update();
                return menu.Items.Single(item => item.Action?.Name == action).Enabled;
            }
            void Check(string action, bool expected, string context)
            {
                bool actual = Enabled(action);
                if (actual != expected) failures.Add($"{context}: {action} expected={expected}, actual={actual}");
            }
            foreach (string action in new[] { "copy:", "cut:", "undo:", "redo:" })
                Check(action, false, "initial state");
            if (kind == 4)
                foreach (string action in Actions) Check(action, false, "non-editor");
            else
            {
                if (kind != 5) Check("selectAll:", true, "nonempty editor");
                Require(NSApplication.SharedApplication.SendAction(new Selector("selectAll:"), view, null),
                    "native selectAll action rejected");
                Check("copy:", kind is 0 or 1 or 2 or 6, "selection");
                Check("cut:", kind < 3, "selection");
                SetReadOnly(editor, true);
                foreach (string action in new[] { "cut:", "paste:", "undo:", "redo:" })
                    Check(action, false, "read-only");
                Check("copy:", kind is 0 or 1 or 2 or 6, "read-only selection");
                if (kind != 5) Check("selectAll:", true, "read-only selection");
                SetReadOnly(editor, false);
                if (kind < 3)
                {
                    var support = (IImeSupport)editor;
                    Require(support.TrySetImeSelection(0, 0), "clear selection rejected");
                    Check("copy:", false, "selection cleared");
                    Check("cut:", false, "selection cleared");
                    Require(support.TryReplaceImeText(0, 0, "x"), "undoable text replacement rejected");
                    Check("undo:", true, "after edit");
                    Check("redo:", false, "after edit");
                    Require(NSApplication.SharedApplication.SendAction(new Selector("undo:"), view, null),
                        "native undo rejected");
                    Check("redo:", true, "after undo");
                    SetReadOnly(editor, true);
                    Check("redo:", false, "read-only redo history");
                    SetReadOnly(editor, false);
                    Check("redo:", true, "editable redo history");
                }
            }
            int beforeQueries = keyEvents;
            Require(sibling.Focus(), "sibling focus rejected");
            foreach (string action in Actions) Check(action, false, "focus moved to button");
            Require(keyEvents == beforeQueries, "menu validation dispatched key events");
            Require(editor.Focus(), "editor refocus rejected");
            window.IsEnabled = false;
            foreach (string action in Actions) Check(action, false, "disabled window");
            window.IsEnabled = true;
            window.Hide();
            foreach (string action in Actions) Check(action, false, "hidden window");
            window.Show(); window.UpdateLayout();
            Require(editor.Focus(), "reshown editor focus rejected");
            if (kind is 0 or 1 or 2 or 3 or 6) Check("selectAll:", true, "reshown window");
            if (kind == 4)
            {
                bool allowed = false, throwing = false, closeDuringQuery = false;
                int executed = 0;
                editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
                    (_, e) => { executed++; e.Handled = true; },
                    (_, e) =>
                    {
                        if (throwing) throw new InvalidOperationException("fixture CanExecute exception");
                        if (closeDuringQuery) window.Close();
                        e.CanExecute = allowed; e.Handled = true;
                    }));
                Check("copy:", false, "custom routed command disabled");
                allowed = true;
                Check("copy:", true, "custom routed command enabled");
                Require(executed == 0, "validation executed custom command");
                Require(NSApplication.SharedApplication.SendAction(new Selector("copy:"), view, null),
                    "native custom copy action rejected");
                Require(executed == 1, "native action did not execute the routed command");
                KeyEventHandler intercept = (_, e) => { if (e.Key == Key.C) e.Handled = true; };
                editor.PreviewKeyDown += intercept;
                Require(NSApplication.SharedApplication.SendAction(new Selector("copy:"), view, null),
                    "intercepted native custom copy action rejected");
                Require(executed == 1, "handled key also executed the routed command");
                editor.PreviewKeyDown -= intercept;
                throwing = true;
                Check("copy:", false, "throwing custom CanExecute");
                throwing = false; closeDuringQuery = true;
                Check("copy:", false, "custom CanExecute closed window");
                Require(window.Handle == 0, "query callback failed to close window");
            }
            window.Close();
            foreach (string action in Actions) Check(action, false, "retained view after close");
            Require(failures.Count == 0, string.Join("; ", failures));
            Console.WriteLine($"PASS: native editing menu case {index}"); return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: native editing menu case {index}: {exception}"); return 1;
        }
        finally { window.Close(); }
    }

    private static void SetReadOnly(UIElement element, bool value)
    {
        switch (element)
        {
            case TextBoxBase text: text.IsReadOnly = value; break;
            case EditControl edit: edit.IsReadOnly = value; break;
            case PasswordBox password: password.IsReadOnly = value; break;
            case Terminal terminal: terminal.IsReadOnly = value; break;
            case HexEditor hex: hex.IsReadOnly = value; break;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
