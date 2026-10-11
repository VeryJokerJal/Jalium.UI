using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Caption relationships, identity and actions through real AppKit AX objects.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowCaptionAccessibilityChecks
{
    private static readonly string[] s_names =
    [
        "custom caption relationships share the actual control-tree objects",
        "zoom and restore update the name without replacing the AX object",
        "hidden caption buttons reject cached Press and recover their identity",
        "hidden title bar removes every caption relationship",
        "disabled owner preserves readable caption references without actions",
        "ResizeMode and explicit button visibility govern caption relationships",
        "custom template replacement invalidates old caption references",
        "native/custom title bar transitions preserve AppKit native buttons",
        "caption Close preserves cancellation and destroys safely exactly once",
        "window hide/reuse and CSS visibility reject cached caption actions"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < s_names.Length; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-caption-accessibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill(); failed++;
                Console.Error.WriteLine($"FAIL: caption AX case {index} timed out");
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window caption accessibility host checks: {s_names.Length - failed}/{s_names.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-caption-accessibility-case=".Length), out int index)
            || (uint)index >= s_names.Length) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new TextBox { Text = "保留中文编辑内容🙂", Height = 40 };
        var fakeClose = new TitleBarButton { Kind = TitleBarButtonKind.Close, Height = 32 };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(editor); panel.Children.Add(fakeClose);
        var window = new Window
        {
            Title = "标题栏无障碍验证", Width = 560, Height = 420, Content = panel,
            TitleBarStyle = WindowTitleBarStyle.Custom, ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var application = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool acceptClose = false;
        try
        {
            window.Show(); Pump(window);
            var native = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
            var buttons = CaptionControls(window);
            Tag(buttons);
            var root = Runtime.GetNSObject<NSView>(window.Handle)!.AccessibilityChildren!
                .OfType<NSAccessibilityElement>().Single();
            var close = Find(root, "caption-close");
            var minimize = Find(root, "caption-minimize");
            var zoom = Find(root, "caption-zoom");
            Require(Relation(native, 0)?.Handle == close.Handle
                && Relation(native, 1)?.Handle == minimize.Handle
                && Relation(native, 2)?.Handle == zoom.Handle,
                "custom Window lacks caption relationships or references a different control");
            int closing = 0;
            window.Closing += (_, e) => { closing++; e.Cancel = !acceptClose; };
            switch (index)
            {
                case 0:
                    Require(close.AccessibilityLabel == "Close" && minimize.AccessibilityLabel == "Minimize"
                        && zoom.AccessibilityLabel == "Maximize", "caption names expose template IDs");
                    Require(close.AccessibilitySubrole == "AXCloseButton" && minimize.AccessibilitySubrole == "AXMinimizeButton"
                        && zoom.AccessibilitySubrole == "AXZoomButton", "caption subroles do not identify Window actions");
                    Require(close.AccessibilityWindow?.Handle == native.Handle && zoom.AccessibilityParent != null,
                        "caption ancestry lost its actual Window");
                    window.Resources["TitleBarCloseButtonName"] = "关闭窗口";
                    Require(close.AccessibilityLabel == "关闭窗口", "caption name does not use scoped localization resources");
                    AutomationProperties.SetName(buttons[0], "关闭当前文档");
                    Require(close.AccessibilityLabel == "关闭当前文档", "caption default overrode explicit automation name");
                    AutomationProperties.SetName(fakeClose, "正文里的关闭按钮");
                    Require(Relation(native, 0)?.Handle == close.Handle, "content caption lookalike replaced the real Window action");
                    break;
                case 1:
                    var restoreBounds = window.RestoreBounds;
                    Require(Press(zoom) && window.WindowState == WindowState.Maximized, "caption zoom did not maximize");
                    Pump(window);
                    Require(Relation(native, 2)?.Handle == zoom.Handle && zoom.AccessibilityLabel == "Restore"
                        && zoom.AccessibilitySubrole == "AXZoomButton", "restore replaced identity or retained the old name");
                    Require(Press(zoom) && window.WindowState == WindowState.Normal, "caption restore did not restore");
                    Require(editor.Text == "保留中文编辑内容🙂" && window.RestoreBounds == restoreBounds,
                        "zoom/restore lost content or normal geometry");
                    break;
                case 2:
                    var axes = new[] { close, minimize, zoom };
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        buttons[i].Visibility = Visibility.Collapsed;
                        Require(Relation(native, i) == null && !Press(axes[i]), "hidden caption remained callable");
                        buttons[i].Visibility = Visibility.Visible;
                        Require(Relation(native, i)?.Handle == axes[i].Handle, "reshown caption lost its AX identity");
                    }
                    Require(closing == 0 && window.WindowState == WindowState.Normal, "cached hidden caption mutated Window state");
                    break;
                case 3:
                    window.IsShowTitleBar = false;
                    Require(Empty(native) && !Press(close) && !Press(minimize) && !Press(zoom),
                        "hidden title bar kept caption references or actions");
                    window.IsShowTitleBar = true; Pump(window);
                    Require(Relation(native, 0)?.Handle == close.Handle && Relation(native, 2)?.Handle == zoom.Handle,
                        "restored title bar changed caption identity");
                    break;
                case 4:
                    window.IsEnabled = false;
                    Require(Relation(native, 0)?.Handle == close.Handle && Relation(native, 1)?.Handle == minimize.Handle
                        && Relation(native, 2)?.Handle == zoom.Handle, "disabled Window dropped readable caption references");
                    Require(!close.AccessibilityEnabled && !minimize.AccessibilityEnabled && !zoom.AccessibilityEnabled
                        && !Press(close) && !Press(minimize) && !Press(zoom) && closing == 0,
                        "disabled caption accepted actions");
                    window.IsEnabled = true;
                    Require(Press(close) && closing == 1 && window.Handle != 0, "reenabled caption did not preserve canceled Close");
                    break;
                case 5:
                    window.ResizeMode = ResizeMode.NoResize;
                    Require(Relation(native, 0)?.Handle == close.Handle && Relation(native, 1) == null
                        && Relation(native, 2) == null && !Press(minimize) && !Press(zoom), "NoResize kept caption resize actions");
                    window.ResizeMode = ResizeMode.CanMinimize;
                    Require(Relation(native, 1)?.Handle == minimize.Handle && Relation(native, 2) == null,
                        "CanMinimize lost minimize or exposed maximize");
                    window.ResizeMode = ResizeMode.CanResize;
                    window.IsShowMinimizeButton = false; window.IsShowMaximizeButton = false; window.IsShowCloseButton = false;
                    Require(Empty(native), "explicit caption visibility flags were ignored");
                    break;
                case 6:
                    var template = new ControlTemplate(typeof(TitleBar));
                    template.SetVisualTree(() =>
                    {
                        var content = new StackPanel { Orientation = Orientation.Horizontal };
                        content.Children.Add(new TitleBarButton { Name = "PART_CloseButton" });
                        content.Children.Add(new TitleBarButton { Name = "PART_MinimizeButton" });
                        content.Children.Add(new TitleBarButton { Name = "PART_MaximizeButton" });
                        return content;
                    });
                    window.TitleBar!.Template = template; window.TitleBar.ApplyTemplate(); Pump(window);
                    Require(!Press(close) && !Press(minimize) && !Press(zoom), "detached template captions remained callable");
                    var newButtons = CaptionControls(window); Tag(newButtons);
                    Require(Relation(native, 0) is { } nextClose && nextClose.Handle != close.Handle
                        && nextClose.AccessibilityLabel == "Close" && Press(nextClose) && closing == 1,
                        "replacement template did not publish its actual Close action");
                    break;
                case 7:
                    var inputView = Runtime.GetNSObject<NSView>(window.Handle)!;
                    editor.Focus();
                    Require(editor.IsKeyboardFocused && native.MakeFirstResponder(inputView),
                        "caption style focus fixture cannot focus its editor");
                    window.TitleBarStyle = WindowTitleBarStyle.Native; Pump(window);
                    Require(native.FirstResponder?.Handle == inputView.Handle && editor.IsKeyboardFocused,
                        "native title bar transition lost the editor or its input responder");
                    Require(!Press(close) && !Press(zoom), "removed custom title bar remained callable");
                    // AppKit exposes the widget's Cell as its AX element.
                    Require(native.AccessibilityCloseButton?.Handle == native.StandardWindowButton(NSWindowButton.CloseButton)?.Cell?.Handle
                        && native.AccessibilityMinimizeButton?.Handle == native.StandardWindowButton(NSWindowButton.MiniaturizeButton)?.Cell?.Handle
                        && native.AccessibilityZoomButton?.Handle == native.StandardWindowButton(NSWindowButton.ZoomButton)?.Cell?.Handle,
                        "managed captions displaced native AppKit Window buttons");
                    window.TitleBarStyle = WindowTitleBarStyle.Custom; Pump(window);
                    Require(native.FirstResponder?.Handle == inputView.Handle && editor.IsKeyboardFocused,
                        "custom title bar transition lost the editor or its input responder");
                    Require(Relation(native, 0) is { } newClose && newClose.Handle != close.Handle
                        && newClose.AccessibilitySubrole == "AXCloseButton", "new custom title bar reused a detached caption");
                    break;
                case 8:
                    int closed = 0; window.Closed += (_, _) => closed++;
                    Require(Press(close) && closing == 1 && closed == 0 && window.Handle != 0,
                        "caption Close bypassed Closing cancellation");
                    acceptClose = true;
                    Require(Press(close) && closing == 2 && closed == 1 && window.Handle == 0,
                        "caption Close failed accepted destruction");
                    Require(Empty(native) && !Press(close) && !Press(zoom) && closed == 1,
                        "destroyed Window retained caption relationships or actions");
                    break;
                case 9:
                    window.Hide();
                    Require(Empty(native) && !Press(close) && !Press(zoom), "hidden Window retained caption actions");
                    window.Show(); Pump(window);
                    Require(Relation(native, 0)?.Handle == close.Handle, "reused Window changed caption identity");
                    Css.SetStyle(window.TitleBar!, "visibility:hidden");
                    Pump(window);
                    Require(Relation(native, 0)?.Handle == close.Handle && Relation(native, 2)?.Handle == zoom.Handle,
                        "CSS unexpectedly overrode the caption's explicit native visibility");
                    // Window's presentation flags set native local Visibility.
                    // Remove those explicit values to exercise CSS inheritance.
                    window.TitleBar!.ClearValue(UIElement.VisibilityProperty);
                    foreach (var button in buttons) button.ClearValue(UIElement.VisibilityProperty);
                    Pump(window);
                    Require(Empty(native) && !Press(close) && !Press(zoom), "CSS hidden caption ancestor retained actions");
                    Css.SetStyle(window.TitleBar!, "visibility:visible"); Pump(window);
                    Require(Relation(native, 0)?.Handle == close.Handle && Press(close) && closing == 1,
                        "CSS restored caption did not recover its identity and action");
                    break;
            }
            Console.WriteLine($"PASS: {index}: {s_names[index]}");
            acceptClose = true;
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {index}: {s_names[index]}: {error}");
            return 1;
        }
        finally { acceptClose = true; window.Close(); application.Shutdown(); }
    }

    private static TitleBarButton[] CaptionControls(Window window)
    {
        var buttons = Descendants(window.TitleBar!).OfType<TitleBarButton>().ToArray();
        return [buttons.Single(b => b.Kind == TitleBarButtonKind.Close),
            buttons.Single(b => b.Kind == TitleBarButtonKind.Minimize),
            buttons.Single(b => b.Kind is TitleBarButtonKind.Maximize or TitleBarButtonKind.Restore)];
    }
    private static IEnumerable<Visual> Descendants(Visual parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (VisualTreeHelper.GetChild(parent, i) is not Visual child) continue;
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Tag(TitleBarButton[] buttons)
    {
        string[] ids = ["caption-close", "caption-minimize", "caption-zoom"];
        for (int i = 0; i < buttons.Length; i++) AutomationProperties.SetAutomationId(buttons[i], ids[i]);
    }
    private static NSAccessibilityElement? Relation(NSWindow window, int index) => (index switch
    {
        0 => window.AccessibilityCloseButton,
        1 => window.AccessibilityMinimizeButton,
        _ => window.AccessibilityZoomButton
    }) as NSAccessibilityElement;
    private static bool Empty(NSWindow window) => Relation(window, 0) == null
        && Relation(window, 1) == null && Relation(window, 2) == null;
    private static NSAccessibilityElement Find(NSAccessibilityElement node, string id)
    {
        if (node.AccessibilityIdentifier == id) return node;
        foreach (var child in node.AccessibilityChildren?.OfType<NSAccessibilityElement>() ?? [])
        {
            try { return Find(child, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"AX caption not found: {id}");
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout(); Dispatcher.GetForCurrentThread().ProcessQueue(); window.UpdateLayout();
    }
    private static bool Press(NSAccessibilityElement element)
    {
        bool accepted = element.AccessibilityPerformPress();
        Dispatcher.GetForCurrentThread().ProcessQueue();
        return accepted;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
