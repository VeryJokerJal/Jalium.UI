using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

/// <summary>Clipping queries through actual AppKit elements on the main thread.</summary>
internal static class AccessibilityClippingChecks
{
    private static readonly string[] Names = ["partial ancestor clip", "compound clip holes",
        "transformed and mutable clips", "scrolled descendants remain accessible", "partial clip edges", "hidden and singular ancestors"];

    internal static int RunAll()
    {
        int failed = 0, count = Names.Length * 6;
        for (int index = 0; index < count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--accessibility-clipping-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS accessibility clipping host checks: {count - failed}/{count} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument[(argument.IndexOf('=') + 1)..], out int index) || (uint)index >= Names.Length * 6) return 2;
        int kind = index % Names.Length, editorKind = index / Names.Length % 3;
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Control editor = editorKind switch { 0 => new TextBox { AcceptsReturn = true, Text = "A🙂中文 first\nsecond line" },
            1 => new RichTextBox(), _ => new EditControl { Text = "A🙂中文 first\nsecond line", IsScrollInertiaEnabled = false } };
        if (editor is RichTextBox rich) rich.SetPlainText("A🙂中文 first\nsecond line");
        editor.Height = 100; editor.FontSize = 16;
        AutomationProperties.SetAutomationId(editor, "clipped-editor");
        var parent = new Border { Child = editor, Margin = new Thickness(24) };
        ScrollViewer? scroll = null;
        if (kind == 3)
        {
            parent.Child = null;
            var stack = new StackPanel(); stack.Children.Add(new Border { Height = 340 }); stack.Children.Add(editor);
            scroll = new ScrollViewer { Content = stack, Height = 140, Padding = new Thickness(12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible, IsScrollInertiaEnabled = false };
        }
        var window = new Window { Width = 420, Height = 290, ShowActivated = false, Content = (UIElement?)scroll ?? parent,
            TitleBarStyle = index < Names.Length * 3 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom };
        try
        {
            window.Show(); window.UpdateLayout();
            var nativeView = Runtime.GetNSObject<NSView>(window.Handle)!;
            var root = (NSAccessibilityElement)nativeView.AccessibilityChildren![0];
            var ax = Find(root, "clipped-editor");
            var peer = editor.GetAutomationPeer()!;
            var provider = (ITextProvider)peer.GetPattern(PatternInterface.Text)!;
            var source = ((AutomationTextProvider)provider).Source;
            string original = source.Text;
            Rect glyph = kind == 3 ? Rect.Empty : source.GetBoundingRectangles(1, 2).Single();
            if (kind != 3) Require(glyph.Width > 1 && glyph.Height > 1, "fixture has no actual shaped emoji");
            Point localOrigin = kind == 3 ? default : RelativeToParent(new Point(glyph.Left, glyph.Top));
            switch (kind)
            {
                case 0:
                    parent.Clip = new RectangleGeometry(new(localOrigin.X, localOrigin.Y + glyph.Height / 2, glyph.Width / 2, glyph.Height / 2));
                    Rect partial = source.GetBoundingRectangles(1, 2).Single();
                    Require(Close(partial.Width, glyph.Width / 2) && Close(partial.Height, glyph.Height / 2), "partial text frame ignores ancestor clip");
                    var nativeFrame = ax.GetAccessibilityFrame(new NSRange(1, 2));
                    Require(Close(nativeFrame.Width, glyph.Width / 2) && Close(nativeFrame.Height, glyph.Height / 2), "AppKit frame ignores partial crop");
                    Require(!provider.GetVisibleRanges().Any(range => range.GetText(-1).Contains("second", StringComparison.Ordinal)), "cropped line is advertised as visible");
                    Require(ax.AccessibilityVisibleCharacterRange.Location == 1 && ax.AccessibilityVisibleCharacterRange.Length == 2, "AppKit visible envelope includes cropped text");
                    break;
                case 1:
                    var hole = new Rect(localOrigin.X - .5, localOrigin.Y - .5, glyph.Width + 1, glyph.Height + 1);
                    var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
                    mask.Children.Add(new RectangleGeometry(new Rect(parent.RenderSize))); mask.Children.Add(new RectangleGeometry(hole));
                    parent.Clip = mask;
                    Require(source.GetBoundingRectangles(1, 2).Count == 0 && ax.GetAccessibilityFrame(new NSRange(1, 2)).Height == 0, "a clip hole is treated as filled");
                    Require(!string.Concat(provider.GetVisibleRanges().Select(range => range.GetText(-1))).Contains("🙂", StringComparison.Ordinal), "hidden emoji remains in visible ranges");
                    var hiddenPoint = CocoaPoint(new(glyph.Left + glyph.Width / 2, glyph.Top + glyph.Height / 2));
                    Require(ax.GetAccessibilityRange(hiddenPoint).Location == nint.MaxValue, "point in clip hole produces a glyph");
                    Require(provider.RangeFromPoint(editor.PointToScreen(new(glyph.Left + glyph.Width / 2, glyph.Top + glyph.Height / 2))) == null, "canonical hit ignores hole");
                    mask.FillRule = FillRule.Nonzero;
                    Require(source.GetBoundingRectangles(1, 2).Count == 1, "changing winding rule leaves a stale clip");
                    break;
                case 2:
                    parent.RenderTransform = new RotateTransform(11);
                    parent.RenderOffset = new Point(17, 6);
                    editor.RenderTransform = new ScaleTransform(1.4, 1.2);
                    localOrigin = RelativeToParent(new Point(glyph.Left, glyph.Top));
                    var changingClip = new RectangleGeometry(new Rect(localOrigin.X, localOrigin.Y, glyph.Width * .7, glyph.Height * 1.2));
                    parent.Clip = changingClip;
                    var transformed = source.GetBoundingRectangles(1, 2).Single();
                    Require(Close(transformed.Width, glyph.Width / 2), "ancestor rotation, scale or render offset changes local crop");
                    var shown = CocoaPoint(new(transformed.Left + transformed.Width / 2, transformed.Top + transformed.Height / 2));
                    Require(ax.GetAccessibilityRange(shown).Location == 1, "visible transformed emoji cannot be hit");
                    var hidden = CocoaPoint(new(glyph.Right - .1, glyph.Top + glyph.Height / 2));
                    Require(ax.GetAccessibilityRange(hidden).Location == nint.MaxValue, "cropped transformed glyph remains hittable");
                    changingClip.Transform = new TranslateTransform(1000, 1000);
                    Require(provider.GetVisibleRanges().Length == 0 && peer.IsOffscreen(), "mutated geometry transform is cached");
                    changingClip.Transform = null; parent.Clip = null;
                    Require(provider.GetVisibleRanges().Length > 0 && !peer.IsOffscreen(), "removing transform clip cannot restore visibility");
                    parent.RenderTransform = null; editor.RenderTransform = new RotateTransform(30);
                    Point centre = RelativeToParent(new(glyph.Left + glyph.Width / 2, glyph.Top + glyph.Height / 2));
                    parent.Clip = new RectangleGeometry(new Rect(centre.X - 3, centre.Y - 3, 6, 6));
                    var rotatedFrame = ax.GetAccessibilityFrame(new NSRange(1, 2));
                    Require(Close(rotatedFrame.Width, 6) && Close(rotatedFrame.Height, 6), "reprojected local clip bounds expand beyond the parent mask");
                    double[] publicFrame = provider.DocumentRange.FindText("🙂", false, false)!.GetBoundingRectangles();
                    Require(Close(publicFrame[2], 6 * window.DpiScale) && Close(publicFrame[3], 6 * window.DpiScale), "canonical screen bounds expand beyond the parent mask");
                    break;
                case 3:
                    Require(peer.IsOffscreen() && source.GetBoundingRectangles(1, 2).Count == 0 && provider.GetVisibleRanges().Length == 0,
                        "ancestor scroll does not make descendant text offscreen");
                    Require(ax.AccessibilityElement && ax.AccessibilityValue?.ToString() == original
                        && Find(root, "clipped-editor").Handle == ax.Handle, "scrolled text is removed from accessibility tree");
                    Require(!WalkVisible(root).Any(item => item.Handle == ax.Handle), "offscreen descendant remains in visible children");
                    scroll!.ScrollToVerticalOffset(330); window.UpdateLayout();
                    Require(!peer.IsOffscreen() && provider.GetVisibleRanges().Length > 0 && ax.GetAccessibilityFrame(new NSRange(1, 2)).Height > 0,
                        "ancestor scroll cannot reveal cached text element");
                    Require(Find(root, "clipped-editor").Handle == ax.Handle, "scrolling changes native accessibility identity");
                    break;
                case 4:
                    parent.Margin = new Thickness(140, 24, 24, 24); window.UpdateLayout();
                    parent.ClipToBounds = true; parent.ClipToBoundsEdges = ClipEdges.Top | ClipEdges.Bottom;
                    editor.RenderTransform = new TranslateTransform(-100, 0);
                    Require(source.GetBoundingRectangles(1, 2).Count == 1, "open horizontal clip edges crop text");
                    parent.ClipToBoundsEdges = ClipEdges.All;
                    Require(source.GetBoundingRectangles(1, 2).Count == 0, "closing horizontal edges leaves text visible");
                    parent.ClipToBounds = false;
                    // The translated editor stays inside the native client for this glyph.
                    editor.RenderTransform = new TranslateTransform(0, 0);
                    Require(source.GetBoundingRectangles(1, 2).Count == 1, "disabling bounds clip leaves stale geometry");
                    parent.Clip = Geometry.Parse("M0,0 L300,0 L0,200 Z");
                    Require(!peer.IsOffscreen() && !double.IsNaN(peer.GetClickablePoint().X), "partly visible control has no usable click point");
                    break;
                case 5:
                    parent.Visibility = Visibility.Hidden;
                    Require(peer.IsOffscreen() && provider.GetVisibleRanges().Length == 0 && provider.DocumentRange.GetBoundingRectangles().Length == 0,
                        "hidden ancestor leaves canonical text visible");
                    Require(!ax.AccessibilityElement, "hidden subtree stays exposed");
                    parent.Visibility = Visibility.Visible;
                    parent.RenderTransform = new ScaleTransform(0, 1);
                    Require(peer.IsOffscreen() && source.GetBoundingRectangles(1, 2).Count == 0, "singular ancestor invents visible geometry");
                    parent.RenderTransform = null;
                    Require(!peer.IsOffscreen() && Find(root, "clipped-editor").Handle == ax.Handle, "restoring ancestor changes identity");
                    break;
            }
            Require(source.Text == original, "geometry query edited document");
            Console.WriteLine($"PASS: {window.TitleBarStyle}: {editor.GetType().Name}: {Names[kind]}"); return 0;

            Point RelativeToParent(Point point)
            {
                Require(parent.GetRenderMatrix().TryInvert(out var inverse), "fixture parent matrix is singular");
                return inverse.Transform(editor.GetRenderMatrix().Transform(point));
            }
            CoreGraphics.CGPoint CocoaPoint(Point point)
            {
                Point rootPoint = editor.GetRenderMatrix().Transform(point);
                var windowPoint = nativeView.ConvertPointToView(new(rootPoint.X, rootPoint.Y), null);
                return nativeView.Window!.ConvertPointToScreen(windowPoint);
            }
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {window.TitleBarStyle}: {editor.GetType().Name}: {Names[kind]}: {error}"); return 1; }
        finally { if (window.Handle != 0) window.Close(); }
    }

    private static bool Close(double actual, double expected) => Math.Abs(actual - expected) < .15;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static NSAccessibilityElement Find(NSAccessibilityElement root, string id) => Walk(root).Single(item => item.AccessibilityIdentifier == id);
    private static IEnumerable<NSAccessibilityElement> Walk(NSAccessibilityElement root)
    { yield return root; foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>()) foreach (var item in Walk(child)) yield return item; }
    private static IEnumerable<NSAccessibilityElement> WalkVisible(NSAccessibilityElement root)
    { yield return root; foreach (var child in (root.AccessibilityVisibleChildren ?? []).OfType<NSAccessibilityElement>()) foreach (var item in WalkVisible(child)) yield return item; }
}
