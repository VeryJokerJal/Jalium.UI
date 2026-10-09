using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Real AppKit text queries and writes for the custom-rendered code editor.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class EditControlAccessibilityChecks
{
    private static readonly string[] Names =
    [
        "empty/single/multiline text area and focus",
        "UTF-16 value writes preserve undo/redo",
        "grapheme selection and invalid ranges",
        "read-only and disabled owners",
        "visible text geometry and scroll into view",
        "hidden, removed and closed AX identity",
        "value and selection notifications",
        "throwing and closing text callbacks"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < Names.Length * 2; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--editor-accessibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS EditControl accessibility host checks: {Names.Length * 2 - failed}/{Names.Length * 2} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--editor-accessibility-case=".Length), out int index)
            || (uint)index >= Names.Length * 2) return 2;
        int kind = index % Names.Length;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new EditControl { Text = "原文🙂\n第二行", Height = 180, IsScrollInertiaEnabled = false };
        AutomationProperties.SetAutomationId(editor, "code-editor");
        AutomationProperties.SetName(editor, "中文代码编辑器");
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = "代码编辑器原生无障碍", Height = 28 });
        panel.Children.Add(editor);
        var window = new Window
        {
            Width = 540, Height = 420, ShowActivated = false, Content = panel,
            TitleBarStyle = index < Names.Length ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom
        };
        try
        {
            window.Show(); window.UpdateLayout();
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var root = (view.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>().Single();
            var text = Find(root);
            Require(text.AccessibilityRole == "AXTextArea" && text.AccessibilityValue?.ToString() == editor.Text,
                "code editor lacks its multiline text value");
            switch (kind)
            {
                case 0:
                    foreach (string value in new[] { "", "一行🙂", "中文\r\n下一行🙂" })
                    {
                        editor.Text = value;
                        Require(text.AccessibilityRole == "AXTextArea" && text.AccessibilityValue?.ToString() == value
                            && text.AccessibilityNumberOfCharacters == value.Length, "empty/single/multiline role or UTF-16 length differs");
                    }
                    text.AccessibilityFocused = true;
                    Require(editor.IsKeyboardFocused
                        && text.AccessibilityFocused == (NSApplication.SharedApplication.KeyWindow?.Handle == view.Window!.Handle)
                        && text.AccessibilityLabel == "中文代码编辑器", "AX focus or name missing");
                    break;
                case 1:
                    string original = editor.Text;
                    text.AccessibilityValue = new NSString("中文🙂\r\n新值");
                    Require(editor.Text == "中文🙂\r\n新值" && text.AccessibilityNumberOfCharacters == 8 && editor.CanUndo, "value replacement is not an undoable UTF-16 edit");
                    editor.Undo(); Require(editor.Text == original && editor.CanRedo, "AX replacement lost undo history");
                    editor.Redo(); Require(text.AccessibilityValue?.ToString() == "中文🙂\r\n新值", "redo differs from AX value");
                    text.AccessibilityValue = new NSString("");
                    Require(editor.Text.Length == 0 && text.AccessibilityNumberOfCharacters == 0, "empty AX value failed");
                    editor.Undo(); Require(editor.Text == "中文🙂\r\n新值", "empty replacement cannot be undone");
                    break;
                case 2:
                    editor.Text = "A🙂e\u0301中\r\n尾";
                    text.AccessibilitySelectedTextRange = new NSRange(2, 1);
                    Require(editor.SelectionStart == 1 && editor.SelectionLength == 2 && text.AccessibilitySelectedText == "🙂", "selection splits emoji");
                    text.AccessibilitySelectedTextRange = new NSRange(4, 1);
                    Require(editor.SelectionStart == 3 && editor.SelectionLength == 2 && text.AccessibilitySelectedText == "e\u0301", "selection splits combining grapheme");
                    Require(text.GetAccessibilityString(new NSRange(1, 2)) == "🙂" && text.GetAccessibilityString(new NSRange(99, 2)) == null, "substring range validation differs");
                    text.AccessibilitySelectedTextRange = new NSRange(99, 2);
                    Require(editor.SelectionStart == 3 && editor.SelectionLength == 2, "invalid range changed selection");
                    editor.IsReadOnly = true;
                    text.AccessibilitySelectedTextRange = new NSRange(5, 1);
                    Require(text.AccessibilitySelectedText == "中", "read-only selection is unavailable");
                    break;
                case 3:
                    string before = editor.Text;
                    editor.IsReadOnly = true;
                    Require(!text.IsAccessibilitySelectorAllowed(new Selector("setAccessibilityValue:")), "read-only advertises writable value");
                    text.AccessibilityValue = new NSString("forbidden");
                    Require(editor.Text == before, "read-only value changed");
                    editor.IsReadOnly = false; window.IsEnabled = false;
                    Require(!text.AccessibilityEnabled && !text.IsAccessibilitySelectorAllowed(new Selector("setAccessibilityValue:")), "disabled owner advertises editing");
                    text.AccessibilityValue = new NSString("disabled"); text.AccessibilitySelectedTextRange = new NSRange(0, 2);
                    Require(editor.Text == before && editor.SelectionLength == 0, "disabled owner permits mutation or selection");
                    window.IsEnabled = true;
                    Require(text.IsAccessibilitySelectorAllowed(new Selector("setAccessibilityValue:")), "reenabled editor remains read-only");
                    break;
                case 4:
                    var frame = text.GetAccessibilityFrame(new NSRange(0, 2));
                    Require(frame.Width > 1 && frame.Height > 1 && frame.X > text.AccessibilityFrame.X
                        && frame.Y >= text.AccessibilityFrame.Y && frame.Y + frame.Height <= text.AccessibilityFrame.Y + text.AccessibilityFrame.Height + 0.1,
                        "text bounds omit gutter, viewport or screen translation");
                    editor.Text = string.Join('\n', Enumerable.Range(1, 100).Select(line => $"第{line}行🙂"));
                    window.UpdateLayout();
                    int tail = editor.Text.LastIndexOf("第100行", StringComparison.Ordinal);
                    Require(text.GetAccessibilityFrame(new NSRange(tail, 2)).Height == 0, "offscreen text fabricates a visible frame");
                    var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                    provider.DocumentRange.FindText("第100行", false, false)!.ScrollIntoView(false);
                    window.UpdateLayout();
                    var scrolled = text.GetAccessibilityFrame(new NSRange(tail, 2));
                    Require(scrolled.Width > 1 && scrolled.Height > 1, "text range ScrollIntoView did not reveal its text");
                    Require(text.GetAccessibilityFrame(new NSRange(0, 2)).Height == 0, "scrolling left stale top-line geometry");
                    editor.LoadText("header {\n hidden🙂\n}\nlast"); window.UpdateLayout();
                    int hidden = editor.Text.IndexOf("hidden", StringComparison.Ordinal);
                    Require(editor.ToggleFold(1), "fixture did not create a brace fold");
                    Require(text.GetAccessibilityFrame(new NSRange(hidden, 2)).Height == 0, "folded text fabricates visible geometry");
                    Require(editor.ToggleFold(1) && text.GetAccessibilityFrame(new NSRange(hidden, 2)).Height > 1,
                        "unfolding left stale hidden text geometry");
                    break;
                case 5:
                    window.Hide(); text.AccessibilityValue = new NSString("hidden");
                    Require(!text.AccessibilityElement && editor.Text == "原文🙂\n第二行", "hidden editor remains actionable");
                    window.Show(); window.UpdateLayout();
                    Require(Find(root).Handle == text.Handle, "hide/show changed text identity");
                    panel.Children.Remove(editor); window.UpdateLayout();
                    text.AccessibilityValue = new NSString("removed");
                    Require(!text.AccessibilityElement && editor.Text == "原文🙂\n第二行", "removed editor remains writable");
                    panel.Children.Add(editor); window.UpdateLayout();
                    var replacement = Find(root);
                    Require(replacement.Handle != text.Handle, "reinserted editor revived defunct AX identity");
                    replacement.AccessibilityValue = new NSString("reinserted");
                    Require(editor.Text == "reinserted", "reinserted editor cannot be edited");
                    window.Close(); replacement.AccessibilityValue = new NSString("closed");
                    Require(!replacement.AccessibilityElement && editor.Text == "reinserted", "closed editor remains writable");
                    break;
                case 6:
                    var previous = AutomationPeer.EventSink;
                    var sink = new RecordingSink(editor, previous); AutomationPeer.EventSink = sink;
                    try
                    {
                        editor.Text = "通知🙂";
                        editor.Select(2, 2); editor.CaretOffset = 0;
                        Require(sink.Values.Any(change => change.Before == "原文🙂\n第二行" && change.After == "通知🙂"), "text assignment emits no value change");
                        Require(sink.Events.Contains(AutomationEvents.TextPatternOnTextChanged)
                            && sink.Events.Count(e => e == AutomationEvents.TextPatternOnTextSelectionChanged) == 2,
                            "text or caret/selection notifications are missing or duplicated");
                    }
                    finally { AutomationPeer.EventSink = previous; }
                    break;
                case 7:
                    EventHandler<Jalium.UI.Controls.Editor.DocumentChangeEventArgs> throwing = (_, _) => throw new InvalidOperationException("expected editor callback failure");
                    editor.TextChanged += throwing;
                    text.AccessibilityValue = new NSString("throw write");
                    editor.TextChanged -= throwing;
                    Require(editor.Text == "throw write" && text.AccessibilityValue?.ToString() == "throw write", "callback failure escaped native query or lost document state");
                    text.AccessibilityValue = new NSString("after failure");
                    Require(editor.Text == "after failure", "failed callback broke subsequent text writes");
                    editor.TextChanged += (_, _) => window.Close();
                    text.AccessibilityValue = new NSString("closing write");
                    Require(window.Handle == 0 && !text.AccessibilityElement, "reentrant text write did not close safely");
                    break;
            }
            Console.WriteLine($"PASS: {window.TitleBarStyle}: {Names[kind]}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {window.TitleBarStyle}: {Names[kind]}: {error}"); return 1; }
        finally { if (window.Handle != 0) window.Close(); }
    }

    private static NSAccessibilityElement Find(NSAccessibilityElement root) => Walk(root).Single(element => element.AccessibilityIdentifier == "code-editor");
    private static IEnumerable<NSAccessibilityElement> Walk(NSAccessibilityElement root)
    {
        yield return root;
        foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>())
            foreach (var element in Walk(child)) yield return element;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class RecordingSink(EditControl owner, IAutomationEventSink? previous) : IAutomationEventSink
    {
        internal List<AutomationEvents> Events { get; } = [];
        internal List<(string? Before, string? After)> Values { get; } = [];
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        { previous?.OnAutomationEventRaised(peer, eventId); if (ReferenceEquals(peer.Owner, owner)) Events.Add(eventId); }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        { previous?.OnPropertyChangedRaised(peer, property, oldValue, newValue); if (ReferenceEquals(peer.Owner, owner) && property == AutomationProperty.ValueProperty) Values.Add((oldValue as string, newValue as string)); }
        public void OnFocusChanged(AutomationPeer peer) => previous?.OnFocusChanged(peer);
    }
}
