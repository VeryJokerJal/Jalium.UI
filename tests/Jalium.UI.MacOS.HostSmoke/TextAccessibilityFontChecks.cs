using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static partial class TextAccessibilityStyleChecks
{
    internal static int RunFontMatching()
    {
        int failed = 0;
        for (int i = 0; i < 12; i++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--text-font-matching-case={i}"); using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS text font matching host checks: {12-failed}/12 passed"); return failed == 0 ? 0 : 1;
    }

    internal static int RunFontMatchingCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--text-font-matching-case=".Length),out int index) || (uint)index >= 12) return 2;
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc"));
        Require(resource != null,"private collection fixture unavailable");
        int type = index % 3; string family = index % 6 < 3 ? "'Missing, Window121', 'Helvetica Neue'" : resource!.Family;
        Control editor = type switch { 0 => new TextBox { Text = "Miii" }, 1 => new RichTextBox(FlowDocument.FromText("Miii")), _ => new EditControl { Text = "Miii" } };
        editor.FontFamily = new FontFamily(family); editor.FontSize = 21; editor.FontWeight = FontWeights.Bold; editor.FontStretch = FontStretches.Condensed;
        if (editor is RichTextBox rich) { rich.Document.FontFamily = editor.FontFamily; rich.Document.FontSize = 21; rich.Document.FontWeight = editor.FontWeight; rich.Document.FontStretch = editor.FontStretch; }
        AutomationProperties.SetAutomationId(editor,"matched-font-editor");
        var window = new Window { Width = 420,Height = 260,ShowActivated = false,Content = editor,
            TitleBarStyle = index < 6 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom };
        try
        {
            window.Show(); window.UpdateLayout(); var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var ax = Find((NSAccessibilityElement)view.AccessibilityChildren![0],"matched-font-editor");
            Verify("HelveticaNeue-CondensedBold");
            editor.FontStretch = FontStretches.Normal;
            if (editor is RichTextBox changed) changed.Document.FontStretch = editor.FontStretch;
            window.UpdateLayout(); Verify("HelveticaNeue-Bold");
            Require(Source(editor).Text.StartsWith("Miii",StringComparison.Ordinal) && Source(editor).SelectionLength == 0,"font queries mutate text or selection");
            Console.WriteLine($"PASS: {window.TitleBarStyle}: {editor.GetType().Name}: {(index%6<3 ? "quoted list" : "private collection")} font matching"); return 0;

            void Verify(string expected)
            {
                using var attributed = ax.GetAccessibilityAttributedString(new NSRange(0,4));
                Require(attributed != null,"font attributes unavailable");
                string name = ((NSDictionary)Attributes(attributed!)[(NSString)"AXFont"]!)[(NSString)"AXFontName"]!.ToString();
                Require(name == expected,$"AX face {name} differs from drawn face {expected}");
                using var data = ax.GetAccessibilityRtf(new NSRange(0,4)); Require(data != null,"font RTF unavailable");
                using var decoded = Decode(data!);
                Require(decoded.Value == "Miii","font RTF changes text");
                var font = (NSFont)Attributes(decoded)[(NSString)"NSFont"]!;
                Require(font.FontName == expected && font.PointSize == 21,$"RTF face {font.FontName} differs from drawn face {expected}");
            }
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL font matching case {index}: {error}"); return 1; }
        finally { window.Close(); }
    }
}
