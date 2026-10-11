using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static class WindowGraphemeKeyChecks
{
    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < 8; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-grapheme-key-case={index}");
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(30_000)) { child.Kill(); child.WaitForExit(); failed++; }
            else if (child.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS EditControl grapheme key host checks: {8 - failed}/8 passed"); return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-grapheme-key-case=".Length), out int index) || (uint)index >= 8) return 2;
        JaliumMacApplication.Initialize(); NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new EditControl { Text = "A🙂Z", FontFamily = "Arial", FontSize = 21, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
        var window = new Window { Content = editor, Width = 420, Height = 260, ShowActivated = false,
            TitleBarStyle = index < 4 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            window.Show(); window.UpdateLayout(); if (!editor.Focus()) throw new InvalidOperationException("editor focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            void Key(bool forward, bool shift = false, bool deletion = false)
            {
                ushort scan = deletion ? forward ? (ushort)0x75 : (ushort)0x33 : forward ? (ushort)0x7c : (ushort)0x7b;
                string text = deletion ? forward ? "\uf728" : "\u007f" : forward ? "\uf703" : "\uf702";
                WindowTextNavigationChecks.Send(view, scan, text, shift ? NSEventModifierMask.ShiftKeyMask : 0);
            }
            switch (index % 4)
            {
                case 0:
                    editor.Text = "Ae\u0301Z"; editor.Select(1, 2); Key(false, shift: true);
                    Require(editor.CaretOffset == 1 && editor.SelectionLength == 0, "Shift-Left splits the combining sequence");
                    Key(true, shift: true); Require(editor.SelectedText == "e\u0301", "Shift-Right splits the combining sequence");
                    break;
                case 1:
                    foreach (string cluster in new[] { "🙂", "👋🏿", "🇨🇳", "👩‍👩‍👧‍👦", "1️⃣" })
                    {
                        editor.Text = "A" + cluster + "Z"; editor.CaretOffset = 1; Key(true, shift: true);
                        Require(editor.SelectedText == cluster, "Shift-Right splits " + cluster);
                        Key(false, shift: true); Require(editor.CaretOffset == 1 && editor.SelectionLength == 0, "reverse selection splits " + cluster);
                        editor.CaretOffset = 1 + cluster.Length; Key(false, deletion: true);
                        Require(editor.Text == "AZ", "Backspace splits " + cluster); editor.Undo(); Require(editor.Text == "A" + cluster + "Z", "Unicode undo loses text");
                    }
                    break;
                case 2:
                    editor.Text = "Aone e\u0301🙂Z"; editor.Select(1, editor.Text.Length - 2); Key(false);
                    Require(editor.CaretOffset == 1 && editor.SelectionLength == 0, "Left does not collapse to the selected range start");
                    editor.Select(1, editor.Text.Length - 2); Key(true);
                    Require(editor.CaretOffset == editor.Text.Length - 1 && editor.SelectionLength == 0, "Right does not collapse to the selected range end");
                    break;
                case 3:
                    foreach (bool deletion in new[] { false, true }) foreach (bool forward in new[] { false, true })
                    {
                        editor.Text = "A\r\nZ"; editor.CaretOffset = forward ? 1 : 3; Key(forward, deletion: deletion);
                        Require(editor.Text == (deletion ? "AZ" : "A\r\nZ") && editor.CaretOffset == (deletion || !forward ? 1 : 3), "CRLF is split by navigation/deletion");
                    }
                    break;
            }
            Console.WriteLine($"PASS: {window.TitleBarStyle}: grapheme key case {index % 4}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL grapheme key case {index}: {error}"); return 1; }
        finally { window.Close(); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
