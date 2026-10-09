using AppKit;
using CoreGraphics;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class WindowWordNavigationChecks
{
    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < 20; ++index)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-word-navigation-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window word navigation host checks: {20 - failed}/20 passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-word-navigation-case=".Length), out int index) || (uint)index >= 20)
            return 2;
        int kind = index < 9 ? index % 3 : new[] { 0, 2, 2, 2, 2, 1, 0, 1, 2, 2, 0 }[index - 9];
        int mode = index < 9 ? index / 3 : index < 15 ? 3 : 4;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        UIElement editor = kind switch
        {
            0 => new TextBox { AcceptsReturn = true },
            1 => new EditControl(),
            _ => new RichTextBox()
        };
        var window = new Window { Content = editor, Width = 600, Height = 340,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false };
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            window.Show(); window.UpdateLayout(); Require(editor.Focus(), "editor focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var support = (IImeSupport)editor;
            if (index == 19)
            {
                CheckNoWrapPixels();
            }
            else if (mode == 0)
            {
                foreach (var (text, initial, right, expected) in new[]
                {
                    ("中文输入窗口", 0, true, 2), ("日本語の編集", 0, true, 2),
                    ("ภาษาไทยยินดีต้อนรับ", 0, true, 7), ("don't stop", 0, true, 5),
                    ("one 👩‍👩‍👧‍👦 two", 4, true, 19), ("שלום עולם", 0, false, 4)
                })
                {
                    SetText(editor, text); window.UpdateLayout();
                    foreach (bool shift in new[] { false, true })
                    {
                        Require(support.TrySetImeSelection(initial, 0), "selection reset rejected");
                        Send(view, right ? (ushort)0x7c : (ushort)0x7b, right ? "\uf703" : "\uf702",
                            NSEventModifierMask.AlternateKeyMask | (shift ? NSEventModifierMask.ShiftKeyMask : 0));
                        Require(support.TryGetImeSurroundingText(out var after) && after.CursorIndex == expected &&
                            after.AnchorIndex == (shift ? initial : expected),
                            $"Option word mismatch: text={text}, shift={shift}, initial={initial}, actual={after.AnchorIndex}/{after.CursorIndex}, expected={(shift ? initial : expected)}/{expected}");
                    }
                }
                SetText(editor, "alpha beta gamma"); window.UpdateLayout();
                Require(support.TrySetImeSelection(0, 5), "word selection reset rejected");
                Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask);
                Require(support.TryGetImeSurroundingText(out var selected) && selected.CursorIndex == 10 &&
                    selected.AnchorIndex == 10, "Option movement only collapsed the selection");
            }
            else if (mode == 1)
            {
                SetText(editor, "中文输入窗口"); window.UpdateLayout();
                Require(support.TrySetImeSelection(0, 0), "deletion selection reset rejected");
                Send(view, 0x75, "\uf728", NSEventModifierMask.AlternateKeyMask);
                Require(ReadText(editor) == "输入窗口" + (kind == 2 ? Environment.NewLine : ""), "native Option-Delete removed wrong word");
                Send(view, 0x06, "z", NSEventModifierMask.CommandKeyMask);
                Require(ReadText(editor) == "中文输入窗口" + (kind == 2 ? Environment.NewLine : ""), "native Undo did not restore word");
                if (editor is EditControl code) code.IsReadOnly = true;
                else ((TextBoxBase)editor).IsReadOnly = true;
                Send(view, 0x75, "\uf728", NSEventModifierMask.AlternateKeyMask);
                Require(ReadText(editor) == "中文输入窗口" + (kind == 2 ? Environment.NewLine : ""), "word deletion bypassed read-only guard");
            }
            else if (mode == 4)
            {
                var control = (Control)editor; control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
                control.FontFamily = new Jalium.UI.Media.FontFamily("Helvetica"); control.FontSize = 20;
                if (index < 17)
                {
                    string text = string.Concat(Enumerable.Repeat("MMMM ", 3000)) + "abc אבגדה 123 xyz tail";
                    if (editor is TextBox plain) plain.TextWrapping = TextWrapping.NoWrap;
                    if (editor is EditControl code) code.ShowLineNumbers = false;
                    SetText(editor, text); window.UpdateLayout();
                    foreach (bool shift in new[] { false, true })
                    {
                        Require(support.TrySetImeSelection(1, 0), "long-line selection rejected");
                        Send(view, 0x7c, "\uf703", NSEventModifierMask.CommandKeyMask | (shift ? NSEventModifierMask.ShiftKeyMask : 0));
                        Require(support.TryGetImeSurroundingText(out var end) && end.CursorIndex == text.Length &&
                            end.AnchorIndex == (shift ? 1 : text.Length), "Command-Right stopped at an artificial soft wrap");
                        Require(support.TrySetImeSelection(15000, 0), "long-line word selection rejected");
                        Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask | (shift ? NSEventModifierMask.ShiftKeyMask : 0));
                        Require(support.TryGetImeSurroundingText(out var word) && word.CursorIndex == 15003 &&
                            word.AnchorIndex == (shift ? 15000 : 15003), "long-line Option-Right used stale word geometry");
                    }
                }
                else
                {
                    SetText(editor, "שלום עולם abc def"); var rich = (RichTextBox)editor;
                    rich.Document.FontFamily = "Helvetica"; rich.Document.FontSize = 20;
                    var paragraph = (Paragraph)rich.Document.Blocks[0]; paragraph.Margin = new Thickness(0);
                    foreach (Run run in paragraph.Inlines) run.FontSize = 20;
                    FrameworkElement ancestor = window;
                    if (index == 18)
                    {
                        window.Content = null; var panel = new StackPanel(); panel.Children.Add(editor);
                        window.Content = panel; ancestor = panel;
                    }
                    ancestor.FlowDirection = FlowDirection.LeftToRight; window.UpdateLayout(); Require(editor.Focus(), "inherited focus rejected");
                    Require(support.TrySetImeSelection(0, 0), "inherited selection rejected");
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask);
                    Require(support.TryGetImeSurroundingText(out var word) && word.CursorIndex == 4, "ancestor LTR was treated as natural RTL");
                    ancestor.ClearValue(FrameworkElement.FlowDirectionProperty);
                    Require(support.TrySetImeSelection(0, 0), "natural selection rejected");
                    Send(view, 0x7b, "\uf702", NSEventModifierMask.AlternateKeyMask);
                    Require(support.TryGetImeSurroundingText(out word) && word.CursorIndex == 4, "cleared direction failed to restore natural RTL");
                }
            }
            else if (mode == 3)
            {
                const string mixed = "abc אבגדה 123 xyz العربية 中文 words tail";
                var control = (Control)editor;
                control.Width = 130; control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
                control.FontFamily = new Jalium.UI.Media.FontFamily("Helvetica"); control.FontSize = 20;
                if (editor is TextBox plain) plain.TextWrapping = TextWrapping.Wrap;
                if (editor is EditControl code) code.ShowLineNumbers = false;
                string text = index is 11 or 14 ? "שלום עולם" : mixed;
                SetText(editor, text);
                if (editor is RichTextBox rich)
                {
                    rich.Document.FontFamily = "Helvetica"; rich.Document.FontSize = 20;
                    var paragraph = (Paragraph)rich.Document.Blocks[0]; paragraph.Margin = new Thickness(0);
                    if (index == 11) paragraph.FlowDirection = FlowDirection.LeftToRight;
                    if (index is 12 or 13) paragraph.FlowDirection = FlowDirection.RightToLeft;
                    if (index == 13)
                    {
                        paragraph.Inlines.Clear();
                        paragraph.Inlines.Add(new Run(text[..2]) { FontSize = 20 });
                        paragraph.Inlines.Add(new Run(text[2..14]) { FontFamily = new Jalium.UI.Media.FontFamily("Helvetica-Bold"), FontSize = 40 });
                        paragraph.Inlines.Add(new Run(text[14..]) { FontSize = 20 });
                    }
                    else foreach (Run run in paragraph.Inlines) run.FontSize = 20;
                }
                window.UpdateLayout();
                int initial = index is 11 or 14 ? 0 : 5;
                bool right = index is 11 or 12 or 13;
                foreach (bool shift in new[] { false, true })
                {
                    Require(support.TrySetImeSelection(initial, 0), "shaped word selection rejected");
                    Send(view, right ? (ushort)0x7c : (ushort)0x7b, right ? "\uf703" : "\uf702",
                        NSEventModifierMask.AlternateKeyMask | (shift ? NSEventModifierMask.ShiftKeyMask : 0));
                    Require(support.TryGetImeSurroundingText(out var selected) && selected.CursorIndex == 4 &&
                        selected.AnchorIndex == (shift ? initial : 4),
                        $"shaped word case={index} shift={shift} actual={selected.AnchorIndex}/{selected.CursorIndex}");
                }
                if (index is 9 or 10)
                {
                    control.Width = 240; window.UpdateLayout();
                    Require(support.TrySetImeSelection(5, 0), "resize selection rejected");
                    Send(view, 0x7b, "\uf702", NSEventModifierMask.AlternateKeyMask);
                    Require(support.TryGetImeSurroundingText(out var selected) && selected.CursorIndex == 3,
                        "word movement retained stale wrapped geometry after resize");
                }
            }
            else
            {
                foreach (var (text, hit, start, length) in new[]
                {
                    ("中文输入窗口", 3, 2, 2), ("日本語の編集", 1, 0, 3),
                    ("ภาษาไทยยินดีต้อนรับ", 8, 7, 5), ("one 👩‍👩‍👧‍👦 two", 4, 4, 11)
                })
                {
                    SetText(editor, text); window.UpdateLayout();
                    Require(support.TrySetImeSelection(0, 0), "mouse selection reset rejected");
                    Require(support.TryGetImeTextRangeGeometry(hit, 1, false, out var geometry), "mouse glyph geometry unavailable");
                    var rect = geometry.Rectangle;
                    CGPoint point = new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                    CGPoint nativePoint = view.ConvertPointToView(point, null);
                    for (int click = 1; click <= 2; ++click)
                    {
                        using var down = NSEvent.MouseEvent(NSEventType.LeftMouseDown, nativePoint, 0,
                            10 + click * .1, view.Window!.WindowNumber, null, click, click, .5f)!;
                        using var up = NSEvent.MouseEvent(NSEventType.LeftMouseUp, nativePoint, 0,
                            10.05 + click * .1, view.Window.WindowNumber, null, click, click, .5f)!;
                        view.MouseDown(down);
                        if (click == 2)
                        {
                            Require(support.TryGetImeSurroundingText(out var word) && word.AnchorIndex == start &&
                                word.CursorIndex == start + length,
                                $"native double-click mismatch: {text}, actual={word.AnchorIndex}/{word.CursorIndex}, expected={start}/{start + length}");
                            Require(support.TryGetImeTextRangeGeometry(text.Length - 1, 1, false, out var last),
                                "drag glyph geometry unavailable");
                            CGPoint dragPoint = view.ConvertPointToView(new CGPoint(last.Rectangle.X + last.Rectangle.Width * .8,
                                last.Rectangle.Y + last.Rectangle.Height / 2), null);
                            using var drag = NSEvent.MouseEvent(NSEventType.LeftMouseDragged, dragPoint, 0,
                                10.24, view.Window.WindowNumber, null, 3, 2, .5f)!;
                            view.MouseDragged(drag);
                        }
                        view.MouseUp(up);
                    }
                    Require(support.TryGetImeSurroundingText(out var after) && after.AnchorIndex == start &&
                        after.CursorIndex == text.Length,
                        $"native word drag mismatch: {text}, actual={after.AnchorIndex}/{after.CursorIndex}, expected={start}/{text.Length}");
                }
            }
            Console.WriteLine($"PASS: native Window word navigation case {index}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: native Window word navigation case {index}: {error}"); return 1; }
        finally { window.Close(); }
    }

    private static void CheckNoWrapPixels()
    {
        string text = string.Concat(Enumerable.Repeat("MMMM ", 3000)) + "abc אבגדה 123 xyz tail";
        var context = RenderContext.Current!;
        using var font = context.CreateTextFormat("Helvetica", 20);
        typeof(NativeTextFormat).Assembly.GetType("Jalium.UI.Interop.NativeMethods")!
            .GetMethod("TextFormatSetWordWrapping", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [font.Handle, 1]);
        font.SetSubpixelPositioning(true);
        Require(font.HitTestTextPosition(text, 130, 1000, (uint)(text.Length - 4), false, out var tail), "tail reference unavailable");
        using var ink = context.CreateSolidBrush(0, 0, 0);
        foreach (int dpi in new[] { 1, 2 })
        {
            using var view = new NSView(new CGRect(0, 0, 320, 96));
            var create = typeof(RenderContext).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Single(method => method.Name == "CreateRenderTarget" && method.GetParameters()[0].ParameterType == typeof(NativeSurfaceDescriptor));
            using var target = (RenderTarget)create.Invoke(context, [NativeSurfaceDescriptor.ForMacOSView(view.Handle), 320 * dpi, 96 * dpi])!;
            target.SetDpi(96 * dpi, 96 * dpi);
            using var drawing = new RenderTargetDrawingContext(target, context);
            byte[] Capture(bool managed, bool scaled = false, bool wrap = false)
            {
                Require(target.RequestReadback() == JaliumResult.Ok, "no-wrap readback unavailable");
                target.BeginDraw(); target.Clear(1, 1, 1);
                float x = 12 - tail.CaretX;
                if (managed)
                {
                    if (scaled) drawing.PushTransform(new Jalium.UI.Media.ScaleTransform(1.25, 1.25));
                    drawing.DrawText(new Jalium.UI.Media.FormattedText(text, "Helvetica", 20)
                    { Foreground = Jalium.UI.Media.Brushes.Black, MaxTextWidth = wrap ? 130 : double.MaxValue,
                        MaxTextHeight = 30 }, new Point(x, 12));
                    if (scaled) drawing.Pop();
                }
                else target.DrawText(text, font, x, 12, 10000, 30, ink);
                target.EndDraw();
                var bytes = new byte[320 * dpi * 96 * dpi * 4];
                Require(target.FetchReadback(bytes, (uint)(320 * dpi * 4), out int width, out int height) == JaliumResult.Ok &&
                    width == 320 * dpi && height == 96 * dpi, "no-wrap readback dimensions differ");
                return bytes;
            }
            var reference = Capture(false); var pixels = Capture(true);
            int InkPixels(byte[] bytes) => Enumerable.Range(0, bytes.Length / 4).Count(i => bytes[i * 4] < 240);
            Require(InkPixels(pixels) > 30, "long-line tail vanished beyond fallback drawing width");
            Require(pixels.SequenceEqual(reference), "managed NoWrap pixels disagree with native whole-line rendering");
            Capture(true, wrap: true);
            Require(pixels.SequenceEqual(Capture(true)), "wrapped draw polluted NoWrap cache");
            var scaled = Capture(true, scaled: true);
            Require(InkPixels(scaled) > 30, "scaled long-line tail vanished");
            if (Environment.GetEnvironmentVariable("JALIUM_NOWRAP_CAPTURE_DIR") is string directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, $"managed-nowrap-{dpi}x.bgra"), pixels);
                File.WriteAllBytes(Path.Combine(directory, $"managed-nowrap-{dpi}x-scaled.bgra"), scaled);
                File.WriteAllText(Path.Combine(directory, $"managed-nowrap-{dpi}x.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { Width = 320 * dpi, Height = 96 * dpi, InkPixels = InkPixels(pixels),
                        ScaledInkPixels = InkPixels(scaled), NativeReferencePixelsEqual = true }));
            }
        }
    }

    private static void SetText(UIElement editor, string text)
    {
        if (editor is TextBox box) box.Text = text;
        else if (editor is EditControl code) code.Text = text;
        else ((RichTextBox)editor).Document = FlowDocument.FromText(text);
    }

    private static string ReadText(UIElement editor) => editor switch
    {
        TextBox box => box.Text, EditControl code => code.Text,
        RichTextBox rich => rich.Document.GetText(), _ => ""
    };

    private static void Send(NSView view, ushort scan, string characters, NSEventModifierMask flags)
    {
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1,
            view.Window!.WindowNumber, null, characters, characters, false, scan)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 2,
            view.Window.WindowNumber, null, characters, characters, false, scan)!;
        view.KeyDown(down); view.KeyUp(up);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
