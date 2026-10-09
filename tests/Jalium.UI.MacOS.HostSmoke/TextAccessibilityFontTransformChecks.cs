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
    private const int FontTransformCases = 24;

    internal static int RunFontTransforms()
    {
        int failures = 0;
        for (int index = 0; index < FontTransformCases; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--text-font-transform-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failures++; }
            else if (process.ExitCode != 0) failures++;
        }
        Console.WriteLine($"macOS text font transform host checks: {FontTransformCases - failures}/{FontTransformCases} passed");
        return failures == 0 ? 0 : 1;
    }

    internal static int RunFontTransformCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--text-font-transform-case=".Length),out int index)
            || (uint)index >= FontTransformCases) return 2;
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/Supplemental/Andale Mono.ttf"));
        Require(resource != null,"private transform fixture unavailable");
        int type = index % 3, group = index / 3 % 4;
        Control editor = type switch
        {
            0 => new TextBox { Text = "Miii" },
            1 => new RichTextBox(FlowDocument.FromText("Miii")),
            _ => new EditControl { Text = "Miii" }
        };
        editor.FontFamily = new FontFamily(group == 2 ? resource!.Family : "SF Pro");
        editor.FontSize = 21;
        editor.FontWeight = group == 0 ? FontWeights.Bold : FontWeights.Normal;
        editor.FontStretch = group switch { 1 => FontStretches.SemiExpanded, 3 => FontStretches.Normal, _ => FontStretches.Condensed };
        var slanted = group is 1 or 2 ? FontStyles.Oblique : FontStyles.Italic;
        SetStyle(slanted);
        if (editor is RichTextBox rich)
        {
            rich.Document.FontFamily = editor.FontFamily; rich.Document.FontSize = 21;
            rich.Document.FontWeight = editor.FontWeight; rich.Document.FontStretch = editor.FontStretch;
        }
        AutomationProperties.SetAutomationId(editor,"font-transform-editor");
        var window = new Window
        {
            Width = 420, Height = 260, ShowActivated = false, Content = editor,
            TitleBarStyle = index < FontTransformCases / 2 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom
        };
        try
        {
            window.Show(); window.UpdateLayout();
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var ax = Find((NSAccessibilityElement)view.AccessibilityChildren![0],"font-transform-editor");
            ax.AccessibilitySelectedTextRange = new NSRange(1,2);
            var source = Source(editor); string text = source.Text;
            CheckSelection("initial AX selection");
            double shear = group == 3 ? 0 : Math.Tan(12 * Math.PI / 180);
            Verify(true,shear,new NSRange(0,4)); Verify(true,shear,new NSRange(1,2));
            CheckSelection("initial transform queries");
            SetStyle(FontStyles.Normal); window.UpdateLayout(); CheckSelection("normal style update"); Verify(false,0,new NSRange(0,4));
            CheckSelection("normal transform queries");
            SetStyle(slanted); window.UpdateLayout(); CheckSelection("slanted style update"); Verify(true,shear,new NSRange(0,4));
            CheckSelection("slanted transform queries");
            SetReadOnly(editor, true);
            CheckSelection("read-only update");
            Verify(true,shear,new NSRange(1,2));
            CheckSelection("read-only transform queries");
            Console.WriteLine($"PASS: {window.TitleBarStyle}: {editor.GetType().Name}: font transform group {group}");
            return 0;

            void CheckSelection(string step) => Require(source.Text == text && source.SelectionStart == 1 && source.SelectionLength == 2,
                $"{step}: text '{source.Text}', selection {source.SelectionStart}:{source.SelectionLength}; expected '{text}', 1:2");

            void Verify(bool italic,double expectedShear,NSRange range)
            {
                using var attributed = ax.GetAccessibilityAttributedString(range);
                Require(attributed != null,"transform AX attributes missing");
                var attributes = Attributes(attributed!);
                if (OperatingSystem.IsMacOSVersionAtLeast(26))
                    Require(attributes[(NSString)"AXFontItalic"] is NSNumber flag && flag.BoolValue == italic,
                        "AX does not report the actual slanted glyphs");
                using var data = ax.GetAccessibilityRtf(range); Require(data != null,"transform RTF missing");
                using var decoded = Decode(data!); var exported = Attributes(decoded);
                Require(decoded.Value == text.Substring((int)range.Location,(int)range.Length),"transform RTF changes text");
                var font = (NSFont)exported[(NSString)"NSFont"]!;
                Require(font.PointSize == 21,"transform RTF changes font size");
                if (group != 3)
                    Require(font.FontName == ((NSDictionary)attributes[(NSString)"AXFont"]!)[(NSString)"AXFontName"]!.ToString(),
                        "transform RTF changes the variable/private face");
                else if (italic) Require(font.FontName.Contains("Italic",StringComparison.Ordinal),"RTF loses the real italic face");
                double actualShear = exported[(NSString)"NSObliqueness"] is NSNumber value ? value.DoubleValue : 0;
                Require(Math.Abs(actualShear - expectedShear) < .0005,"RTF loses or doubles synthesized obliqueness");
                Require(exported[(NSString)"NSExpansion"] is not NSNumber expansion || expansion.DoubleValue == 0,
                    "RTF replaces variable width with glyph expansion");
            }
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL font transform case {index}: {error}"); return 1; }
        finally { window.Close(); }

        void SetStyle(FontStyle style)
        {
            editor.FontStyle = style;
            if (editor is RichTextBox rich) rich.Document.FontStyle = style;
        }
    }
}
