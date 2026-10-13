using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Queries the actual AppKit text accessibility surface, including wrapped layout.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class TextAccessibilityNavigationChecks
{
    private static readonly string[] Names =
    ["UTF-16 glyph and CRLF line ranges", "visual lines after wrapping or folding",
     "viewport ranges and requested scrolling", "screen point and ancestor transforms",
     "insertion line and undoable selected text", "read-only, disabled and defunct capabilities",
     "selection array validation and reentrant text callbacks"];

    internal static int RunAll()
    {
        int failed = 0, count = Names.Length * 6;
        for (int index = 0; index < count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--text-accessibility-navigation-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS text accessibility navigation host checks: {count - failed}/{count} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int ProbeAppKit()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var native = new NSWindow(new CGRect(100, 100, 280, 160), NSWindowStyle.Titled,
            NSBackingStore.Buffered, false);
        using var text = new NSTextView(new CGRect(0, 0, 280, 160));
        text.Font = NSFont.SystemFontOfSize(16)!;
        native.ContentView = text;
        foreach (string value in new[] { "", "A🙂e\u0301中\r\n尾\n", "office ffi 👩‍👩‍👧‍👦 العربية", string.Join(' ', Enumerable.Repeat("wrapped中文🙂", 12)) })
        {
            text.Value = value;
            text.LayoutManager?.EnsureLayoutForTextContainer(text.TextContainer!);
            Console.WriteLine($"NSTextView UTF16={value.Length}");
            for (int index = 0; index <= value.Length; index++)
                Console.WriteLine($"index={index}, line={text.GetAccessibilityLine(index)}, glyph={Format(text.GetAccessibilityRange((nint)index))}");
            for (int line = 0; line < 12; line++)
            {
                NSRange range = text.GetAccessibilityRangeForLine(line);
                Console.WriteLine($"line={line}, range={Format(range)}");
                if (range.Location == nint.MaxValue) break;
            }
            Console.WriteLine($"visible={Format(text.AccessibilityVisibleCharacterRange)}, caretLine={text.AccessibilityInsertionPointLineNumber}");
        }
        return 0;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--text-accessibility-navigation-case=".Length), out int index)
            || (uint)index >= Names.Length * 6) return 2;
        int kind = index % Names.Length, editorKind = index / Names.Length % 3;
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Control editor = editorKind switch
        {
            0 => new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap },
            1 => new RichTextBox(),
            _ => new EditControl { IsScrollInertiaEnabled = false }
        };
        editor.Height = 100; editor.FontSize = 16;
        AutomationProperties.SetAutomationId(editor, "navigation-editor");
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(editor);
        var password = new PasswordBox { Password = "secret", Height = 32 };
        AutomationProperties.SetAutomationId(password, "secure-editor"); panel.Children.Add(password);
        var window = new Window { Width = 420, Height = 290, ShowActivated = false, Content = panel,
            TitleBarStyle = index < Names.Length * 3 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom };
        try
        {
            SetText(editor, "A🙂e\u0301中\r\n尾\n");
            window.Show(); window.UpdateLayout();
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var root = (NSAccessibilityElement)view.AccessibilityChildren![0];
            var ax = Find(root, "navigation-editor");
            var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
            string originalText = GetText(editor);
            int secondStart = originalText.IndexOf('尾');
            int thirdStart = originalText.IndexOf('\n', secondStart) + 1;
            switch (kind)
            {
                case 0:
                    Require(ax.GetAccessibilityLine(secondStart) == 1 && ax.GetAccessibilityLine(thirdStart) == 2, "line lookup ignores paragraph breaks or terminal empty line");
                    Range(ax.GetAccessibilityRangeForLine(0), 0, secondStart, "first line including newline");
                    Range(ax.GetAccessibilityRangeForLine(1), secondStart, thirdStart - secondStart, "LF range");
                    Range(ax.GetAccessibilityRangeForLine(2), thirdStart, originalText.Length - thirdStart, "terminal empty line");
                    Range(ax.GetAccessibilityRange((nint)2), 1, 2, "emoji interior");
                    Range(ax.GetAccessibilityRange((nint)4), 3, 2, "combining grapheme");
                    Require(ax.GetAccessibilityRangeForLine(-1).Location == nint.MaxValue
                        && ax.GetAccessibilityRange((nint)99).Location == nint.MaxValue, "invalid indexes fabricate ranges");
                    SetText(editor, ""); window.UpdateLayout();
                    Range(ax.GetAccessibilityRangeForLine(0), 0, 0, "empty document line");
                    Require(ax.GetAccessibilityLine(0) == 0, "empty document line index differs");
                    Range(ax.AccessibilityVisibleCharacterRange, 0, 0, "empty document visible caret");
                    break;
                case 1:
                    if (editor is EditControl code)
                    {
                        code.LoadText("header {\n hidden🙂\n}\nlast"); window.UpdateLayout();
                        Require(code.ToggleFold(1), "fixture fold failed");
                        int last = code.Text.IndexOf("last", StringComparison.Ordinal);
                        int line = (int)ax.GetAccessibilityLine(last);
                        Range(ax.GetAccessibilityRangeForLine(line), last, 4, "folded visible last line");
                        Require(ax.GetAccessibilityFrame(new NSRange(code.Text.IndexOf("hidden", StringComparison.Ordinal), 2)).Height == 0, "hidden folded text remains visible");
                        Require(code.ToggleFold(1) && ax.GetAccessibilityLine(last) > line, "unfold did not invalidate visual line map");
                    }
                    else
                    {
                        if (editor is TextBox box) box.TextWrapping = TextWrapping.Wrap;
                        SetText(editor, string.Join(' ', Enumerable.Repeat("中文🙂 wrap", 28))); window.UpdateLayout();
                        NSRange first = ax.GetAccessibilityRangeForLine(0), second = ax.GetAccessibilityRangeForLine(1);
                        Require(first.Length > 0 && first.Length < (nint)GetText(editor).Length
                            && second.Location == first.Location + first.Length && second.Length > 0, "soft wrapping is reported as one logical line");
                        Require(ax.GetAccessibilityLine((nint)second.Location) == 1, "soft-wrap boundary uses preceding row");
                        ax.AccessibilitySelectedTextRange = new NSRange(0, 0); editor.Focus();
                        Send(view, 0x7c, "\uf703", NSEventModifierMask.CommandKeyMask);
                        Require(ax.AccessibilitySelectedTextRange.Location == first.Length && ax.AccessibilityInsertionPointLineNumber == 0,
                            "insertion line loses backward affinity at a visual row end");
                        window.Width = 620; window.UpdateLayout();
                        Require(ax.GetAccessibilityRangeForLine(0).Length > first.Length, "resizing left stale wrapped line ranges");
                    }
                    break;
                case 2:
                    SetText(editor, string.Join('\n', Enumerable.Range(1, 80).Select(n => $"第{n}行 中文🙂 abc"))); window.UpdateLayout();
                    string text = GetText(editor);
                    NSRange visible = ax.AccessibilityVisibleCharacterRange;
                    Require(visible.Length > 0 && visible.Length < (nint)text.Length, "AX visible range fabricates entire document");
                    Require(provider.GetVisibleRanges().Length > 0 && provider.GetVisibleRanges().Sum(r => r.GetText(-1).Length) < text.Length, "canonical visible ranges fabricate entire document");
                    var selected = ax.AccessibilitySelectedTextRange;
                    int tail = text.LastIndexOf("第80行", StringComparison.Ordinal);
                    provider.DocumentRange.FindText("第80行", false, false)!.ScrollIntoView(false); window.UpdateLayout();
                    Require(ax.AccessibilityVisibleCharacterRange.Location > visible.Location
                        && ax.GetAccessibilityFrame(new NSRange(tail, 2)).Height > 0, "ScrollIntoView ignores requested range");
                    Range(ax.AccessibilitySelectedTextRange, (int)selected.Location, (int)selected.Length, "scroll changed selection");
                    Require(ax.GetAccessibilityFrame(new NSRange(0, 1)).Height == 0, "offscreen range retains geometry");
                    break;
                case 3:
                    panel.RenderTransform = new ScaleTransform(1.15, 1.1); editor.RenderTransform = new TranslateTransform(11, 7);
                    window.UpdateLayout();
                    CGRect glyph = ax.GetAccessibilityFrame(new NSRange(1, 2));
                    Require(glyph.Width > 1 && glyph.Height > 1, "glyph screen frame unavailable");
                    // Probe both halves: native AX asks for the glyph, not the nearest caret insertion.
                    Range(ax.GetAccessibilityRange(new CGPoint(glyph.X + glyph.Width * .25, glyph.Y + glyph.Height * .5)), 1, 2, "glyph leading half");
                    Range(ax.GetAccessibilityRange(new CGPoint(glyph.X + glyph.Width * .75, glyph.Y + glyph.Height * .5)), 1, 2, "glyph trailing half");
                    Require(ax.GetAccessibilityRange(new CGPoint(double.NaN, 0)).Location == nint.MaxValue, "nonfinite point accepted");
                    var source = ((AutomationTextProvider)provider).Source;
                    var local = source.GetBoundingRectangles(1, 2).Single();
                    Point leading = editor.PointToScreen(new Point(local.X + local.Width * .2, local.Y + local.Height * .5));
                    var fromPoint = provider.RangeFromPoint(leading)!;
                    fromPoint.ExpandToEnclosingUnit(Automation.Text.TextUnit.Character);
                    Require(fromPoint.GetText(-1) == "🙂", "canonical screen point ignores native DPI or render transforms");
                    double[] screenBounds = provider.DocumentRange.FindText("🙂", false, false)!.GetBoundingRectangles();
                    Point corner = editor.PointToScreen(new Point(local.Left, local.Top));
                    Require(Math.Abs(screenBounds[0] - corner.X) < .1 && Math.Abs(screenBounds[1] - corner.Y) < .1,
                        "canonical range frame uses root points instead of screen coordinates");
                    break;
                case 4:
                    ax.AccessibilityInsertionPointLineNumber = 1;
                    Range(ax.AccessibilitySelectedTextRange, secondStart, 0, "insertion line setter");
                    Require(ax.AccessibilityInsertionPointLineNumber == 1, "insertion line getter differs");
                    ax.AccessibilitySelectedTextRange = new NSRange(1, 2);
                    ax.AccessibilitySelectedText = "替换e\u0301";
                    string expected = originalText[..1] + "替换e\u0301" + originalText[3..];
                    Require(GetText(editor) == expected, "selected text replacement lost UTF-16 content");
                    Undo(editor); Require(GetText(editor) == originalText, "selected text edit bypasses undo");
                    Redo(editor); Require(GetText(editor) == expected, "selected text redo differs");
                    break;
                case 5:
                    var secure = Find(root, "secure-editor");
                    Require(!secure.IsAccessibilitySelectorAllowed(new Selector("accessibilityRangeForIndex:"))
                        && !secure.IsAccessibilitySelectorAllowed(new Selector("accessibilityVisibleCharacterRange")), "password exposes text navigation");
                    SetReadOnly(editor, true);
                    Require(ax.IsAccessibilitySelectorAllowed(new Selector("accessibilityRangeForLine:"))
                        && !ax.RespondsToSelector(new Selector("setAccessibilitySelectedText:")), "read-only discovery differs");
                    ax.AccessibilitySelectedTextRange = new NSRange(1, 2);
                    Require(ax.AccessibilitySelectedText == "🙂", "read-only cannot select text");
                    ax.AccessibilitySelectedText = "forbidden";
                    Require(GetText(editor) == originalText, "read-only selected text changed");
                    window.IsEnabled = false;
                    ax.AccessibilityInsertionPointLineNumber = 1; ax.AccessibilitySelectedTextRange = new NSRange(0, 1);
                    Range(ax.AccessibilitySelectedTextRange, 1, 2, "disabled selection changed");
                    window.IsEnabled = true; window.Hide();
                    Require(ax.GetAccessibilityRangeForLine(0).Location == nint.MaxValue, "hidden control remains navigable");
                    window.Show(); window.UpdateLayout();
                    Require(Find(root, "navigation-editor").Handle == ax.Handle, "show changed AX identity");
                    panel.Children.Remove(editor); window.UpdateLayout();
                    Require(ax.GetAccessibilityRange((nint)1).Location == nint.MaxValue, "removed control remains navigable");
                    break;
                case 6:
                    ax.AccessibilitySelectedTextRanges = [NSValue.FromRange(new NSRange(1, 2))];
                    Require(ax.AccessibilitySelectedText == "🙂" && ax.AccessibilitySelectedTextRanges.Length == 1, "single selection array failed");
                    ax.AccessibilitySelectedTextRanges = [NSValue.FromRange(new NSRange(0, 1)), NSValue.FromRange(new NSRange(3, 2))];
                    Range(ax.AccessibilitySelectedTextRange, 1, 2, "disjoint request discarded part of selection");
                    ax.AccessibilitySelectedTextRange = new NSRange(nint.MaxValue, 1);
                    Range(ax.AccessibilitySelectedTextRange, 1, 2, "oversized range changed selection");
                    ax.AccessibilitySelectedTextRange = new NSRange(0, 1);
                    Action remove = OnTextChanged(editor, () => throw new InvalidOperationException("expected AX callback failure"));
                    ax.AccessibilitySelectedText = "T"; remove();
                    Require(GetText(editor).StartsWith('T'), "throwing handler lost selected text edit");
                    ax.AccessibilitySelectedTextRange = new NSRange(0, 1);
                    _ = OnTextChanged(editor, window.Close);
                    ax.AccessibilitySelectedText = "C";
                    Require(window.Handle == 0 && !ax.AccessibilityElement
                        && ax.GetAccessibilityRangeForLine(0).Location == nint.MaxValue, "reentrant selection edit revived closed text element");
                    break;
            }
            Console.WriteLine($"PASS: {window.TitleBarStyle}: {editor.GetType().Name}: {Names[kind]}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {window.TitleBarStyle}: {editor.GetType().Name}: {Names[kind]}: {error}"); return 1; }
        finally { if (window.Handle != 0) window.Close(); }
    }

    private static void SetText(Control editor, string text)
    { if (editor is TextBox box) box.Text = text; else if (editor is EditControl code) code.LoadText(text); else ((RichTextBox)editor).SetPlainText(text); }
    private static string GetText(Control editor) => editor switch { TextBox box => box.Text, EditControl code => code.Text, _ => ((RichTextBox)editor).GetPlainText() };
    private static void SetReadOnly(Control editor, bool value)
    { if (editor is TextBox box) box.IsReadOnly = value; else if (editor is EditControl code) code.IsReadOnly = value; else ((RichTextBox)editor).IsReadOnly = value; }
    private static void Undo(Control editor) { if (editor is EditControl code) code.Undo(); else ((Jalium.UI.Controls.Primitives.TextBoxBase)editor).Undo(); }
    private static void Redo(Control editor) { if (editor is EditControl code) code.Redo(); else ((Jalium.UI.Controls.Primitives.TextBoxBase)editor).Redo(); }
    private static Action OnTextChanged(Control editor, Action action)
    {
        if (editor is EditControl code)
        { EventHandler<Controls.Editor.DocumentChangeEventArgs> handler = (_, _) => action(); code.TextChanged += handler; return () => code.TextChanged -= handler; }
        var box = (Controls.Primitives.TextBoxBase)editor;
        TextChangedEventHandler routed = (_, _) => action(); box.TextChanged += routed; return () => box.TextChanged -= routed;
    }
    private static void Send(NSView view, ushort scan, string characters, NSEventModifierMask flags)
    {
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1,
            view.Window!.WindowNumber, null, characters, characters, false, scan)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 2,
            view.Window.WindowNumber, null, characters, characters, false, scan)!;
        view.KeyDown(down); view.KeyUp(up);
    }
    private static string Format(NSRange range) => $"{range.Location}:{range.Length}";
    private static void Range(NSRange actual, int start, int length, string name) => Require(actual.Location == (nint)start && actual.Length == (nint)length, $"{name}: expected {start}:{length}, got {Format(actual)}");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static NSAccessibilityElement Find(NSAccessibilityElement root, string id) => Walk(root).Single(e => e.AccessibilityIdentifier == id);
    private static IEnumerable<NSAccessibilityElement> Walk(NSAccessibilityElement root)
    { yield return root; foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>()) foreach (var item in Walk(child)) yield return item; }
}
