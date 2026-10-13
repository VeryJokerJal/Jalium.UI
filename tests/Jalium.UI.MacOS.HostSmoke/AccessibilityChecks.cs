using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Interop;
using Jalium.UI.Threading;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>End-to-end AutomationPeer → C ABI → AppKit protocol checks.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class AccessibilityChecks
{
    private static readonly string[] s_names =
    [
        "native Window content tree and Chinese metadata",
        "editable text, UTF-16 selection and read-only protection",
        "secure password content never exposed",
        "toggle and radio selection actions",
        "range limits and increment/decrement actions",
        "screen-point geometry and hit testing",
        "removed/reinserted controls invalidate old AX objects",
        "hidden window rejects actions and reuses AX identity",
        "AX press can close its own window safely",
        "accepted Invoke contains queued user exceptions",
        "disabled owner rejects child actions and text writes",
        "tree expansion and selected children"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < s_names.Length; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--accessibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; Console.Error.WriteLine($"FAIL: AX case {index} timed out"); }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS accessibility host checks: {s_names.Length - failed}/{s_names.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--accessibility-case=".Length), out int index) || (uint)index >= s_names.Length) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Require(Marshal.SizeOf<MacOSAXRequest>() == 136, "managed AX ABI size differs");
        var button = new Button { Content = "保存中文", Height = 32 };
        var editor = new TextBox { Text = "原文", PlaceholderText = "请输入名称", Height = 32 };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "窗口无障碍验证", Height = 28 });
        panel.Children.Add(button);
        panel.Children.Add(editor);
        AutomationProperties.SetAutomationId(button, "save-command");
        AutomationProperties.SetHelpText(button, "保存当前内容");
        var window = new Window
        {
            Title = "中文无障碍窗口", Width = 480, Height = 500,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)), Content = panel
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            NSView view = Runtime.GetNSObject<NSView>(window.Handle)!;
            NSWindow nativeWindow = view.Window!;
            NSAccessibilityElement root = (view.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>().Single();
            NSAccessibilityElement save = Find(root, "save-command");
            NSAccessibilityElement text = FindByRole(root, "AXTextField");
            int clicks = 0;
            button.Click += (_, _) => clicks++;
            switch (index)
            {
                case 0:
                    Require(root.AccessibilityRole == "AXGroup" && root.AccessibilityLabel == window.Title, "content root duplicates/misses native Window role");
                    Require(root.AccessibilityParent?.Handle == nativeWindow.Handle && save.AccessibilityWindow?.Handle == nativeWindow.Handle, "native Window ancestry differs");
                    Require(save.AccessibilityRole == "AXButton" && save.AccessibilityLabel == "保存中文" && save.AccessibilityHelp == "保存当前内容", "Chinese metadata was lost");
                    Require(save.IsAccessibilitySelectorAllowed(new Selector("accessibilityPerformPress"))
                        && !save.IsAccessibilitySelectorAllowed(new Selector("accessibilityPerformDelete")), "unsupported action advertised");
                    Require(FindByRole(root, "AXStaticText").AccessibilityValue?.ToString() == "窗口无障碍验证", "static text value missing");
                    Require(Find(root, "save-command").Handle == save.Handle, "repeated AX tree query changes identity");
                    Require(Press(save) && clicks == 1, "AX press did not invoke button");
                    button.IsEnabled = false;
                    Require(!save.AccessibilityEnabled && !Press(save) && clicks == 1, "disabled button was invoked");
                    button.Content = new TextBlock { Text = "视觉内容名称" };
                    Require(save.AccessibilityLabel == "视觉内容名称", "visual button content lost its accessible name");
                    AutomationProperties.SetName(button, "显式操作名称");
                    Require(save.AccessibilityLabel == "显式操作名称", "visible text overrode explicit accessible name");
                    break;
                case 1:
                    text.AccessibilityValue = new NSString("中文🙂选区");
                    Require(editor.Text == "中文🙂选区" && text.AccessibilityNumberOfCharacters == 6, "AX value did not preserve UTF-16 Chinese/emoji");
                    Require(text.AccessibilityPlaceholderValue == "请输入名称", "placeholder missing");
                    text.AccessibilitySelectedTextRange = new NSRange(2, 2);
                    Require(editor.SelectionStart == 2 && editor.SelectionLength == 2 && text.AccessibilitySelectedText == "🙂", "AX selection units differ");
                    Require(text.GetAccessibilityString(new NSRange(0, 2)) == "中文", "AX text substring differs");
                    Require(text.GetAccessibilityString(new NSRange(99, 2)) == null, "invalid range read succeeded");
                    text.AccessibilitySelectedTextRange = new NSRange(99, 2);
                    Require(editor.SelectionStart == 2 && editor.SelectionLength == 2, "invalid selection changed control");
                    text.AccessibilityFocused = true;
                    Require(editor.IsKeyboardFocused, "AX focus request did not reach managed editor");
                    editor.IsReadOnly = true;
                    Require(!text.IsAccessibilitySelectorAllowed(new Selector("setAccessibilityValue:")), "read-only value advertises a setter");
                    text.AccessibilityValue = new NSString("禁止修改");
                    Require(editor.Text == "中文🙂选区", "read-only text was modified");
                    break;
                case 2:
                    var password = new PasswordBox { Password = "不可泄露🙂", PlaceholderText = "密码", Height = 32 };
                    AutomationProperties.SetAutomationId(password, "password");
                    panel.Children.Add(password); window.UpdateLayout();
                    var secure = Find(root, "password");
                    Require(secure.AccessibilitySubrole == "AXSecureTextField" && secure.AccessibilityProtectedContent, "secure text role missing");
                    Require(secure.AccessibilityValue?.ToString() == string.Empty && secure.AccessibilityNumberOfCharacters == 0, "password content or length exposed");
                    Require(secure.GetAccessibilityString(new NSRange(0, 1)) == null && !secure.IsAccessibilitySelectorAllowed(new Selector("accessibilitySelectedTextRange")), "password text API exposed");
                    secure.AccessibilityValue = new NSString("新密码");
                    Require(password.Password == "新密码" && secure.AccessibilityValue?.ToString() == string.Empty, "secure write leaked content");
                    break;
                case 3:
                    var check = new CheckBox { Content = "接受条款", IsThreeState = true, Height = 32 };
                    var radio = new RadioButton { Content = "选项一", Height = 32 };
                    AutomationProperties.SetAutomationId(check, "toggle"); AutomationProperties.SetAutomationId(radio, "radio");
                    panel.Children.Add(check); panel.Children.Add(radio); window.UpdateLayout();
                    var axCheck = Find(root, "toggle"); var axRadio = Find(root, "radio");
                    Require(axCheck.AccessibilityRole == "AXCheckBox" && axCheck.AccessibilityPerformPress() && check.IsChecked == true, "checkbox action missing");
                    check.IsChecked = null;
                    Require(((NSNumber)axCheck.AccessibilityValue!).Int32Value == 2, "indeterminate state missing");
                    Require(axRadio.AccessibilityPerformPress() && axRadio.AccessibilityPerformPress() && radio.IsChecked == true && axRadio.AccessibilitySelected, "radio press toggled selected radio off");
                    axRadio.AccessibilitySelected = false;
                    Require(radio.IsChecked == false, "selection removal did not reach provider");
                    break;
                case 4:
                    var slider = new Slider { Minimum = 0, Maximum = 10, SmallChange = 2, Value = 4, Height = 32 };
                    var progress = new ProgressBar { Minimum = 0, Maximum = 10, Value = 3, Height = 20 };
                    AutomationProperties.SetAutomationId(slider, "range"); AutomationProperties.SetAutomationId(progress, "progress");
                    panel.Children.Add(slider); panel.Children.Add(progress); window.UpdateLayout();
                    var axRange = Find(root, "range"); var axProgress = Find(root, "progress");
                    Require(axRange.AccessibilityRole == "AXSlider" && ((NSNumber)axRange.AccessibilityMinValue!).DoubleValue == 0 && ((NSNumber)axRange.AccessibilityMaxValue!).DoubleValue == 10, "range bounds missing");
                    Require(axRange.AccessibilityPerformIncrement() && slider.Value == 6 && axRange.AccessibilityPerformDecrement() && slider.Value == 4, "range actions differ");
                    axRange.AccessibilityValue = NSNumber.FromDouble(99);
                    Require(slider.Value == 10, "AX range write escaped maximum");
                    Require(!axProgress.IsAccessibilitySelectorAllowed(new Selector("setAccessibilityValue:")) && !axProgress.AccessibilityPerformIncrement(), "read-only progress exposes edit action");
                    break;
                case 5:
                    Rect bounds = button.GetAutomationPeer()!.GetBoundingRectangle();
                    CGRect expected = nativeWindow.ConvertRectToScreen(view.ConvertRectToView(new CGRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), null));
                    CGRect actual = save.AccessibilityFrame;
                    Require(Near(actual.X, expected.X) && Near(actual.Y, expected.Y) && Near(actual.Width, expected.Width) && Near(actual.Height, expected.Height), "AX screen frame has wrong origin/Retina scale");
                    nint hit = HitTest(view.Handle, Selector.GetHandle("accessibilityHitTest:"), new CGPoint(actual.X + actual.Width / 2, actual.Y + actual.Height / 2));
                    Require(hit == save.Handle, "screen-point hit test did not find button");
                    editor.Select(0, 1);
                    CGRect rangeFrame = text.GetAccessibilityFrame(new NSRange(0, 1));
                    Require(rangeFrame.Width > 0 && rangeFrame.Height > 0 && rangeFrame.X >= text.AccessibilityFrame.X && rangeFrame.Y >= text.AccessibilityFrame.Y, "text range frame lacks control offset");
                    break;
                case 6:
                    panel.Children.Remove(button); window.UpdateLayout();
                    Require(!save.AccessibilityElement && !Press(save) && clicks == 0, "removed control's old AX object remained actionable");
                    panel.Children.Add(button); window.UpdateLayout();
                    var replacement = Find(root, "save-command");
                    Require(replacement.Handle != save.Handle && Press(replacement) && clicks == 1, "reinserted control reused a defunct AX object");
                    Require(!Press(save) && clicks == 1, "defunct AX object revived after reinsertion");
                    break;
                case 7:
                    nint handle = window.Handle;
                    window.Hide();
                    Require((view.AccessibilityChildren ?? []).Length == 0 && !Press(save) && clicks == 0, "hidden window remained actionable");
                    window.Show();
                    var reopenedRoot = (view.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>().Single();
                    Require(window.Handle == handle && reopenedRoot.Handle == root.Handle && Find(reopenedRoot, "save-command").Handle == save.Handle, "hide/reopen lost AX identity");
                    Require(Press(save) && clicks == 1, "reopened window stayed inaccessible");
                    break;
                case 8:
                    button.Click += (_, _) => window.Close();
                    Require(Press(save) && clicks == 1 && window.Handle == 0, "AX closing action failed");
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    Require(!save.AccessibilityElement && !Press(save) && save.AccessibilityParent == null, "closed window's AX object stayed live");
                    break;
                case 9:
                    RoutedEventHandler throwing = (_, _) => throw new InvalidOperationException("expected AX user action failure");
                    button.Click += throwing;
                    Require(Press(save) && window.Handle != 0, "queued user exception escaped or closed the window");
                    button.Click -= throwing;
                    Require(Press(save) && clicks == 2, "failed action broke subsequent AX calls");
                    break;
                case 10:
                    window.IsEnabled = false;
                    Require(!root.AccessibilityEnabled && !save.AccessibilityEnabled && !Press(save), "disabled Window's button remained actionable");
                    text.AccessibilityValue = new NSString("禁止修改");
                    Require(editor.Text == "原文" && clicks == 0, "disabled Window's editor was modified");
                    break;
                case 11:
                    var tree = new TreeView { Height = 120 };
                    var item = new TreeViewItem { Header = "父节点" };
                    item.Items.Add(new TreeViewItem { Header = "子节点" });
                    tree.Items.Add(item);
                    AutomationProperties.SetAutomationId(tree, "tree"); AutomationProperties.SetAutomationId(item, "tree-item");
                    panel.Children.Add(tree); window.UpdateLayout();
                    var axTree = Find(root, "tree"); var axItem = Find(root, "tree-item");
                    axItem.AccessibilityExpanded = true;
                    Require(item.IsExpanded && axItem.AccessibilityExpanded, "tree expansion did not reach provider");
                    window.UpdateLayout();
                    Require((axTree.AccessibilityRows ?? []).Length == 2 && axItem.AccessibilityLabel == "父节点", "outline rows or realized item name missing");
                    Require(axItem.AccessibilityPerformPress() && item.IsSelected && (axTree.AccessibilitySelectedChildren ?? []).Any(child => child.Handle == axItem.Handle), "tree selection missing");
                    axItem.AccessibilityExpanded = false; window.UpdateLayout();
                    Require(!item.IsExpanded && (axTree.AccessibilityRows ?? []).Length == 1, "collapsed outline kept disclosed rows");
                    break;
            }
            Console.WriteLine($"PASS: {s_names[index]}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {s_names[index]}: {error}"); return 1; }
        finally { if (window.Handle != 0) window.Close(); }
    }

    private static NSAccessibilityElement Find(NSAccessibilityElement root, string id) =>
        Walk(root).Single(element => element.AccessibilityIdentifier == id);
    private static NSAccessibilityElement FindByRole(NSAccessibilityElement root, string role) =>
        Walk(root).First(element => element.AccessibilityRole == role);
    private static IEnumerable<NSAccessibilityElement> Walk(NSAccessibilityElement root)
    {
        yield return root;
        foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>())
            foreach (var element in Walk(child)) yield return element;
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;
    private static bool Press(NSAccessibilityElement element)
    {
        bool accepted = element.AccessibilityPerformPress();
        Dispatcher.GetForCurrentThread().ProcessQueue();
        return accepted;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint HitTest(nint target, nint selector, CGPoint point);
}
