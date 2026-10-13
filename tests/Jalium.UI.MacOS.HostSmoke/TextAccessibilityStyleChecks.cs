using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.MacOS;

/// <summary>Uses the real AppKit protocols and RTF reader, rather than a copied serializer.</summary>
internal static partial class TextAccessibilityStyleChecks
{
    private static readonly string[] Names =
    ["font and color attributes", "style extents and UTF-16 boundaries", "RTF and Unicode round-trip",
     "dynamic styles and read-only queries", "offscreen and defunct text", "empty, invalid and private ranges"];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < Names.Length * 6; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--text-accessibility-style-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS text accessibility style host checks: {Names.Length * 6 - failed}/{Names.Length * 6} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--text-accessibility-style-case=".Length), out int index)
            || (uint)index >= Names.Length * 6) return 2;
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        int kind = index % Names.Length, type = index / Names.Length % 3;
        Control editor = type switch { 0 => new TextBox { AcceptsReturn = true }, 1 => new RichTextBox(), _ => new EditControl() };
        editor.FontFamily = new FontFamily("Arial"); editor.FontSize = 21;
        editor.Foreground = new SolidColorBrush(Color.FromRgb(0xa0, 0x12, 0x34)); editor.Height = 110;
        const string value = "A中文🙂e\u0301\r\nTail";
        SetText(editor, value);
        AutomationProperties.SetAutomationId(editor, "styled-editor");
        var password = new PasswordBox { Password = "secret", Height = 32 };
        AutomationProperties.SetAutomationId(password, "private-editor");
        var stack = new StackPanel { Margin = new Thickness(24) }; stack.Children.Add(editor); stack.Children.Add(password);
        var parent = new Border { Child = stack };
        var window = new Window { Width = 420, Height = 300, ShowActivated = false, Content = parent,
            TitleBarStyle = index < Names.Length * 3 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom };
        try
        {
            window.Show(); window.UpdateLayout();
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var root = (NSAccessibilityElement)view.AccessibilityChildren![0];
            var ax = Find(root, "styled-editor"); var secure = Find(root, "private-editor");
            string text = Source(editor).Text;
            using var first = ax.GetAccessibilityAttributedString(new NSRange(0, 1));
            Require(first is not null && Attributes(first).Count > 0, "attributed string has no text styles");
            switch (kind)
            {
                case 0:
                    Require(FontSize(first!) == 21, "font size does not match styled text");
                    Require(Attributes(first!)[(NSString)"AXForegroundColor"] is not null, "foreground is missing");
                    if (editor is RichTextBox rich)
                    {
                        var paragraph = new Paragraph();
                        paragraph.Inlines.Add(new Run("plain "));
                        paragraph.Inlines.Add(new Bold(new Underline(new Run("Bold🙂") { FontSize = 27 })));
                        rich.Document = new FlowDocument { FontFamily = new FontFamily("Arial"), FontSize = 21 };
                        rich.Document.Blocks.Add(paragraph); window.UpdateLayout();
                        using var mixed = ax.GetAccessibilityAttributedString(new NSRange(6, 6));
                        Require(mixed is not null && FontSize(mixed) == 27, "mixed run font is flattened");
                        Require(((NSDictionary)Attributes(mixed!)[(NSString)"AXFont"]!)[(NSString)"AXFontName"]!.ToString().Contains("Bold", StringComparison.Ordinal), "bold face is absent");
                        Require(((NSNumber)Attributes(mixed!)[(NSString)"AXUnderline"]!).Int32Value != 0, "nested underline is missing");
                    }
                    break;
                case 1:
                    Require(ax.GetAccessibilityStyleRange(1).Location == 0, "uniform style starts after document beginning");
                    Require(ax.GetAccessibilityStyleRange(text.Length).Location == 0
                        && ax.GetAccessibilityStyleRange(text.Length).Length == 0, "EOF style is not a zero-length range");
                    using (var half = ax.GetAccessibilityAttributedString(new NSRange(text.IndexOf("🙂", StringComparison.Ordinal) + 1, 1)))
                        Require(half is not null && half.Value == text.Substring(text.IndexOf("🙂", StringComparison.Ordinal) + 1, 1), "UTF-16 half-surrogate is changed");
                    if (editor is RichTextBox formatted)
                    {
                        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("same")); paragraph.Inlines.Add(new Run("same"));
                        paragraph.Inlines.Add(new Run("different🙂") { FontSize = 30 });
                        formatted.Document = new FlowDocument { FontSize = 21, FontFamily = new FontFamily("Arial") };
                        formatted.Document.Blocks.Add(paragraph); window.UpdateLayout();
                        var same = ax.GetAccessibilityStyleRange(5); Require(same.Location == 0 && same.Length == 8, "identical adjacent runs do not merge");
                        var different = ax.GetAccessibilityStyleRange(9); Require(different.Location == 8 && different.Length == 11, "format range crosses a font boundary");
                    }
                    break;
                case 2:
                    using (var data = ax.GetAccessibilityRtf(new NSRange(0, text.Length)))
                    {
                        Require(data is not null && data.Length > 0, "RTF data is missing");
                        using var decoded = Decode(data!);
                        // NSTextView's RTF writer/reader normalizes CRLF to LF
                        // and combining marks to NFC. AX substrings remain raw UTF-16.
                        Require(CanonicalRtfText(decoded.Value) == CanonicalRtfText(text), "RTF does not preserve Unicode text");
                        Require(Attributes(decoded)[(NSString)"NSFont"] is NSFont font && Math.Abs(font.PointSize - 21) < .01, "RTF font is lost");
                    }
                    using (var emoji = ax.GetAccessibilityRtf(new NSRange(text.IndexOf("🙂", StringComparison.Ordinal), 2)))
                    { Require(emoji is not null, "partial RTF is missing"); using var decoded = Decode(emoji!); Require(decoded.Value == "🙂", "RTF substring includes other text"); }
                    if (editor is RichTextBox styled)
                    {
                        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("base"));
                        paragraph.Inlines.Add(new Bold(new Underline(new Run("bold🙂") { FontSize = 27 })));
                        styled.Document = new FlowDocument { FontFamily = new FontFamily("Arial"), FontSize = 21 };
                        styled.Document.Blocks.Add(paragraph); window.UpdateLayout();
                        using var data = ax.GetAccessibilityRtf(new NSRange(4, 6)); Require(data is not null, "mixed RTF is missing");
                        using var decoded = Decode(data!); var attributes = Attributes(decoded);
                        Require(decoded.Value == "bold🙂" && attributes[(NSString)"NSFont"] is NSFont font
                            && Math.Abs(font.PointSize - 27) < .01 && font.FontName.Contains("Bold", StringComparison.Ordinal), "mixed RTF flattens the font");
                        Require(attributes[(NSString)"NSUnderline"] is NSNumber underline && underline.Int32Value != 0, "mixed RTF loses underline");
                    }
                    break;
                case 3:
                    SetReadOnly(editor, true);
                    Require(ax.IsAccessibilitySelectorAllowed(new Selector("accessibilityRTFForRange:")), "read-only text loses RTF capability");
                    if (editor is RichTextBox changed) changed.Document.FontSize = 25; else editor.FontSize = 25;
                    using (var changedAttributes = ax.GetAccessibilityAttributedString(new NSRange(0, 1))) Require(changedAttributes is not null && FontSize(changedAttributes) == 25, "style change is cached");
                    editor.IsEnabled = false;
                    using (var disabled = ax.GetAccessibilityAttributedString(new NSRange(0, 1))) Require(disabled is not null, "disabled document is unreadable");
                    Require(Source(editor).Text == text, "style query edits document"); break;
                case 4:
                    parent.Clip = new RectangleGeometry(new Rect(1000, 1000, 10, 10));
                    using (var clipped = ax.GetAccessibilityAttributedString(new NSRange(0, 1))) Require(clipped is not null && FontSize(clipped) == 21, "offscreen formatting is unreadable");
                    parent.Visibility = Visibility.Hidden;
                    Require(ax.GetAccessibilityAttributedString(new NSRange(0, 1)) is null && ax.GetAccessibilityRtf(new NSRange(0, 1)) is null, "hidden object returns formatting");
                    parent.Visibility = Visibility.Visible; parent.Clip = null;
                    Require(Find(root, "styled-editor").Handle == ax.Handle, "restoring visibility changes identity");
                    window.Close(); Require(ax.GetAccessibilityAttributedString(new NSRange(0, 1)) is null && ax.GetAccessibilityRtf(new NSRange(0, 1)) is null, "closed object returns formatting");
                    break;
                case 5:
                    Require(ax.GetAccessibilityAttributedString(new NSRange(text.Length + 1, 1)) is null, "invalid attributed range is accepted");
                    Require(ax.GetAccessibilityRtf(new NSRange(text.Length + 1, 1)) is null, "invalid RTF range is accepted");
                    Require(ax.GetAccessibilityRtf(new NSRange(text.Length, 0)) is null, "empty RTF differs from AppKit");
                    Require(ax.GetAccessibilityStyleRange(-1).Location == nint.MaxValue && ax.GetAccessibilityStyleRange(int.MaxValue).Location == nint.MaxValue, "invalid style index is accepted");
                    Require(secure.GetAccessibilityAttributedString(new NSRange(0, 1)) is null && secure.GetAccessibilityRtf(new NSRange(0, 1)) is null, "password text leaks formatting");
                    Require(!secure.IsAccessibilitySelectorAllowed(new Selector("accessibilityRTFForRange:")), "password exposes RTF capability");
                    SetText(editor, ""); window.UpdateLayout();
                    using (var empty = ax.GetAccessibilityAttributedString(new NSRange(0, 0))) Require(empty is not null && empty.Length == 0, "empty document cannot return attributed text");
                    Require(ax.GetAccessibilityStyleRange(0).Location == 0 && ax.GetAccessibilityStyleRange(0).Length == 0, "empty style range differs from AppKit"); break;
            }
            Console.WriteLine($"PASS: {window.TitleBarStyle}: {editor.GetType().Name}: {Names[kind]}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL style case {index}: {error}"); return 1; }
        finally { if (window.Handle != 0) window.Close(); }
    }

    private static NSDictionary Attributes(NSAttributedString value) => value.GetAttributes(0, out _);
    private static string CanonicalRtfText(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Normalize(NormalizationForm.FormC);
    private static double FontSize(NSAttributedString value) => ((NSNumber)((NSDictionary)Attributes(value)[(NSString)"AXFont"]!)[(NSString)"AXFontSize"]!).DoubleValue;
    private static NSAttributedString Decode(NSData data)
    {
        nint allocated = Send(GetClass("NSAttributedString"), Selector.GetHandle("alloc"));
        nint decoded = ReadRtf(allocated, Selector.GetHandle("initWithRTF:documentAttributes:"), data.Handle, 0);
        return Runtime.GetNSObject<NSAttributedString>(decoded, owns: true) ?? throw new InvalidOperationException("AppKit RTF reader rejected exported data");
    }
    private static IAutomationTextProviderSource Source(Control editor) => (IAutomationTextProviderSource)editor.GetAutomationPeer()!;
    private static void SetText(Control editor, string value)
    {
        if (editor is TextBox plain) plain.Text = value;
        else if (editor is RichTextBox rich) rich.Document = FlowDocument.FromText(value);
        else ((EditControl)editor).Text = value;
        if (editor is RichTextBox richText) { richText.Document.FontFamily = editor.FontFamily; richText.Document.FontSize = editor.FontSize; }
    }
    private static void SetReadOnly(Control editor, bool value)
    { if (editor is Controls.Primitives.TextBoxBase text) text.IsReadOnly = value; else ((EditControl)editor).IsReadOnly = value; }
    private static NSAccessibilityElement Find(NSAccessibilityElement root, string id) => Walk(root).Single(item => item.AccessibilityIdentifier == id);
    private static IEnumerable<NSAccessibilityElement> Walk(NSAccessibilityElement root)
    { yield return root; foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>()) foreach (var item in Walk(child)) yield return item; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static partial nint ReadRtf(nint receiver, nint selector, nint data, nint attributes);
}
