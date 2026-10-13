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

/// <summary>Own-process NSEvent-to-managed text navigation, without foreground activation.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowTextNavigationChecks
{
    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < 7; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-text-navigation-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window text navigation host checks: {7 - failed}/7 passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-text-navigation-case=".Length), out int index)
            || (uint)index >= 7) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        UIElement editor = index switch
        {
            0 => new TextBox { Text = "first\n  alpha beta\nlast", AcceptsReturn = true },
            1 => new EditControl { Text = "first\n  alpha beta\nlast" },
            2 => new RichTextBox(FlowDocument.FromText("first\n  alpha beta\nlast")),
            4 => new TextBox { Text = "alpha beta gamma delta epsilon zeta eta theta iota kappa",
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Width = 150, Height = 170,
                FontFamily = new FontFamily("Helvetica"), FontSize = 18, Padding = new Thickness(4),
                BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left },
            5 => CreateWrappedRichText(),
            6 => CreateWrappedRichText(longRun: true),
            _ => new Button { Content = "physical modifier receiver" }
        };
        var window = new Window
        {
            Content = editor, Width = 420, Height = 260,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false
        };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            window.Show(); window.UpdateLayout();
            Require(editor.Focus(), "managed editor focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            if (index == 3)
            {
                var events = new List<KeyEventArgs>();
                editor.PreviewKeyDown += (_, e) => events.Add(e);
                editor.PreviewKeyUp += (_, e) => events.Add(e);
                for (int mask = 0; mask < 16; mask++)
                {
                    var flags = (mask & 1) != 0 ? NSEventModifierMask.ShiftKeyMask : (NSEventModifierMask)0;
                    if ((mask & 2) != 0) flags |= NSEventModifierMask.ControlKeyMask;
                    if ((mask & 4) != 0) flags |= NSEventModifierMask.AlternateKeyMask;
                    if ((mask & 8) != 0) flags |= NSEventModifierMask.CommandKeyMask;
                    int before = events.Count;
                    Send(view, 0x26, "j", flags);
                    Require(events.Count == before + 2, "native key phases missing");
                    var expected = ModifierKeys.None;
                    if ((mask & 1) != 0) expected |= ModifierKeys.Shift;
                    if ((mask & 2) != 0) expected |= ModifierKeys.Control;
                    if ((mask & 4) != 0) expected |= ModifierKeys.Alt;
                    if ((mask & 8) != 0) expected |= ModifierKeys.Windows;
                    Require(events[^2].PhysicalModifiers == expected && events[^1].PhysicalModifiers == expected,
                        $"native physical snapshot mismatch for {mask}");
                }
            }
            else if (index >= 4)
            {
                IImeSupport support = (IImeSupport)editor;
                Require(support.TryGetImeSurroundingText(out var text), "wrapped text unavailable");
                Require(support.TryGetImeTextRangeGeometry(0, text.Text.Length, false, out var first), "first row missing");
                Require(support.TryGetImeTextRangeGeometry(first.Length, text.Text.Length - first.Length, false,
                    out var second), "second row missing");
                Require(second.Rectangle.Y > first.Rectangle.Y, "fixture did not wrap");
                int initial = second.Start + 1, end = second.Start + second.Length;
                foreach (bool shift in new[] { false, true })
                {
                    Require(support.TrySetImeSelection(initial, 0), "wrapped selection reset rejected");
                    Rect caret = support.GetImeCaretRectangle();
                    var flags = NSEventModifierMask.CommandKeyMask | (shift ? NSEventModifierMask.ShiftKeyMask : 0);
                    Send(view, 0x7c, "\uf703", flags);
                    Require(support.TryGetImeSurroundingText(out var afterRight), "wrapped snapshot missing");
                    Require(afterRight.CursorIndex == end && afterRight.AnchorIndex == (shift ? initial : end),
                        "native Command-Right skipped the visual row");
                    Require(support.GetImeCaretRectangle().Y == caret.Y, "trailing caret moved to following row");
                    Send(view, 0x7b, "\uf702", flags);
                    Require(support.TryGetImeSurroundingText(out var afterLeft), "wrapped snapshot missing");
                    Require(afterLeft.CursorIndex == second.Start && afterLeft.AnchorIndex == (shift ? initial : second.Start),
                        "native Command-Left skipped the visual row");
                    Require(support.GetImeCaretRectangle().Y == caret.Y, "leading caret moved to another row");
                }
            }
            else
            {
                foreach (var (scan, characters, flags, destination) in new[]
                {
                    ((ushort)0x7b, "\uf702", NSEventModifierMask.CommandKeyMask, 6),
                    ((ushort)0x7c, "\uf703", NSEventModifierMask.CommandKeyMask, 18),
                    ((ushort)0x7e, "\uf700", NSEventModifierMask.CommandKeyMask, 0),
                    ((ushort)0x7b, "\uf702", NSEventModifierMask.AlternateKeyMask, 8),
                    ((ushort)0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask, 18),
                    ((ushort)0x00, "a", NSEventModifierMask.ControlKeyMask, 6),
                    ((ushort)0x0e, "e", NSEventModifierMask.ControlKeyMask, 18)
                })
                {
                    foreach (bool shift in new[] { false, true })
                    {
                        SetCaret(editor, 14);
                        // Reset both selection endpoints between independent native events.
                        Require(((IImeSupport)editor).TrySetImeSelection(14, 0), "selection reset rejected");
                        Send(view, scan, characters, flags | (shift ? NSEventModifierMask.ShiftKeyMask : 0));
                        Require(((IImeSupport)editor).TryGetImeSurroundingText(out var snapshot), "text snapshot rejected");
                        Require(snapshot.CursorIndex == destination && snapshot.AnchorIndex == (shift ? 14 : destination),
                            $"native caret/anchor mismatch: kind={index}, scan={scan}, shift={shift}, cursor={snapshot.CursorIndex}, anchor={snapshot.AnchorIndex}");
                    }
                }
            }
            Console.WriteLine($"PASS: native Window text navigation case {index}"); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"FAIL: native Window text navigation case {index}: {exception}"); return 1; }
        finally { window.Close(); }
    }

    internal static void Send(NSView view, ushort scan, string characters, NSEventModifierMask flags)
    {
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1,
            view.Window!.WindowNumber, null, characters, characters, false, scan)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 2,
            view.Window.WindowNumber, null, characters, characters, false, scan)!;
        view.KeyDown(down); view.KeyUp(up);
    }

    private static RichTextBox CreateWrappedRichText(bool longRun = false)
    {
        var paragraph = new Paragraph();
        foreach (string text in longRun ? new[] { "alpha beta gamma delta epsilon zeta eta theta iota kappa 中文 👩‍👩‍👧‍👦 e\u0301" } : new[] { "first row ", "second row ", "third row " })
            paragraph.Inlines.Add(new Run(text) { FontSize = 20 });
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica" };
        document.Blocks.Add(paragraph);
        return new RichTextBox(document) { Width = 150, Height = 170, Padding = new Thickness(4),
            BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
    }

    private static void SetCaret(UIElement editor, int offset)
    {
        switch (editor)
        {
            case RichTextBox rich: rich.CaretPosition = rich.Document.GetPositionAtOffset(offset, LogicalDirection.Forward); break;
            case TextBox text: text.CaretIndex = offset; break;
            case EditControl code: code.CaretOffset = offset; break;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
