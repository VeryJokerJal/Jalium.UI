using AppKit;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Window AX default/cancel references use actual managed buttons.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowButtonAccessibilityChecks
{
    private static readonly string[] s_names =
    [
        "native default/cancel references preserve content AX identity and actions",
        "button annotations, disable, hide, removal and reinsertion update references",
        "Window hide, disable, reuse and destruction preserve safe button references",
        "modal default/cancel AX actions preserve Closing cancellation and results",
        "popup ContentDialog scopes default/cancel to its own buttons",
        "focused in-place ContentDialog scopes default/cancel to its own buttons",
        "keyboard lookup and AX references skip hidden button ancestors",
        "CSS-hidden default/cancel buttons cannot shadow visible Window actions",
        "CSS-visible descendants remain Window actions beneath a hidden CSS box",
        "collapsed CSS flex items gate explicitly visible Window action descendants",
        "exiting display transitions immediately exclude buttons and cached AX actions",
        "CSS-hidden inherited buttons remain excluded after removing exit styles"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < s_names.Length; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-buttons-accessibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill(); failed++;
                Console.Error.WriteLine($"FAIL: Window buttons AX case {index} timed out");
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window buttons accessibility host checks: {s_names.Length - failed}/{s_names.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-buttons-accessibility-case=".Length), out int index)
            || (uint)index >= s_names.Length) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var save = new Button { Content = "保存中文", IsDefault = true, Height = 32 };
        var cancel = new Button { Content = "取消编辑", IsCancel = true, Height = 32 };
        AutomationProperties.SetAutomationId(save, "window-default");
        AutomationProperties.SetAutomationId(cancel, "window-cancel");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBox { Text = "中文编辑内容", Height = 32 });
        panel.Children.Add(save); panel.Children.Add(cancel);
        var window = CreateWindow("窗口操作无障碍验证", panel);
        var application = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ContentDialog? contentDialog = null;
        try
        {
            window.Show(); window.UpdateLayout();
            var native = Native(window);
            var root = Root(window);
            var axSave = Find(root, "window-default");
            var axCancel = Find(root, "window-cancel");
            Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                "native Window does not reference its managed default/cancel buttons");
            int saves = 0, cancels = 0;
            save.Click += (_, _) => saves++;
            cancel.Click += (_, _) => cancels++;
            switch (index)
            {
                case 0:
                    Require(Default(native)!.AccessibilityLabel == "保存中文" && Cancel(native)!.AccessibilityLabel == "取消编辑",
                        "Window action references lost Chinese names");
                    Require(Default(native)!.AccessibilityWindow?.Handle == native.Handle
                        && Cancel(native)!.AccessibilityWindow?.Handle == native.Handle, "button Window ancestry differs");
                    Require(Press(Default(native)!) && Press(Cancel(native)!)
                        && saves == 1 && cancels == 1, "Window action references invoked the wrong buttons");
                    break;
                case 1:
                    save.IsEnabled = false;
                    Require(Default(native)?.Handle == axSave.Handle && !Default(native)!.AccessibilityEnabled
                        && !Press(Default(native)!) && saves == 0,
                        "disabled default button lost its reference or accepted an action");
                    save.IsEnabled = true; save.Visibility = Visibility.Collapsed;
                    Require(Default(native) == null, "hidden default button remained an accessible Window action");
                    save.Visibility = Visibility.Visible; save.IsDefault = false; cancel.IsDefault = true;
                    Require(Default(native)?.Handle == axCancel.Handle, "changed default annotation left stale reference");
                    panel.Children.Remove(cancel);
                    Require(Default(native) == null && Cancel(native) == null && !Press(axCancel),
                        "removed Window action remains callable");
                    panel.Children.Add(cancel); window.UpdateLayout();
                    Require(Default(native) is { } replacement && replacement.Handle != axCancel.Handle
                        && Cancel(native)?.Handle == replacement.Handle && Press(replacement) && cancels == 1,
                        "reinserted action revived its removed AX identity or lost the new one");
                    break;
                case 2:
                    window.Hide();
                    Require(Default(native) == null && Cancel(native) == null && !Press(axSave),
                        "hidden Window retains accessible actions");
                    window.Show();
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "Window reuse changed button AX identities");
                    window.IsEnabled = false;
                    Require(Default(native)?.Handle == axSave.Handle && !axSave.AccessibilityEnabled
                        && !Press(axSave) && !Press(axCancel),
                        "disabled Window button action succeeded");
                    window.IsEnabled = true; window.Close();
                    Require(Default(native) == null && Cancel(native) == null && !Press(axSave),
                        "closed Window exposes released action references");
                    break;
                case 3:
                    CheckModal(window, false); CheckModal(window, true);
                    break;
                case 4:
                case 5:
                    contentDialog = new ContentDialog
                    {
                        Title = "操作作用域", PrimaryButtonText = "对话框确认", CloseButtonText = "对话框取消",
                        DefaultButton = ContentDialogButton.Primary
                    };
                    if (index == 5) panel.Children.Add(contentDialog);
                    var task = contentDialog.ShowAsync(index == 5 ? ContentDialogPlacement.InPlace : ContentDialogPlacement.Popup);
                    Pump(window);
                    var insideDefault = Default(native) ?? throw new InvalidOperationException("ContentDialog default action is missing");
                    var insideCancel = Cancel(native) ?? throw new InvalidOperationException("ContentDialog cancel action is missing");
                    Require(insideDefault.AccessibilityLabel == "对话框确认" && insideCancel.AccessibilityLabel == "对话框取消"
                        && insideDefault.Handle != axSave.Handle && insideCancel.Handle != axCancel.Handle,
                        "ContentDialog Window action points to a background button");
                    Require(Press(insideCancel), "ContentDialog AX cancel was rejected");
                    Pump(window);
                    Require(task.IsCompletedSuccessfully && task.Result == ContentDialogResult.None && saves == 0 && cancels == 0,
                        "ContentDialog action did not dismiss only its own dialog");
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "dismissed ContentDialog did not restore Window actions");
                    Require(!Press(insideDefault) && !Press(insideCancel),
                        "dismissed ContentDialog AX actions remain callable");
                    break;
                case 6:
                    var hiddenDefault = new Button { Content = "隐藏的确认", IsDefault = true };
                    var hiddenCancel = new Button { Content = "隐藏的取消", IsCancel = true };
                    var hiddenPanel = new StackPanel { Visibility = Visibility.Collapsed };
                    hiddenPanel.Children.Add(hiddenDefault); hiddenPanel.Children.Add(hiddenCancel);
                    panel.Children.Insert(0, hiddenPanel);
                    var host = (IInputDispatcherHost)window;
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "AX references did not skip hidden button ancestors");
                    Require(ReferenceEquals(host.FindButton(window, button => button.IsDefault), save)
                        && ReferenceEquals(host.FindButton(window, button => button.IsCancel), cancel),
                        "keyboard lookup still chooses hidden default/cancel buttons");
                    save.Visibility = Visibility.Hidden; cancel.Visibility = Visibility.Collapsed;
                    Require(Default(native) == null && Cancel(native) == null
                        && host.FindButton(window, button => button.IsDefault) == null
                        && host.FindButton(window, button => button.IsCancel) == null,
                        "all-hidden action lookup retained a target");
                    break;
                case 7:
                    var fallbackDefault = new Button { Content = "可见的确认", IsDefault = true, Height = 32 };
                    var fallbackCancel = new Button { Content = "可见的取消", IsCancel = true, Height = 32 };
                    AutomationProperties.SetAutomationId(fallbackDefault, "fallback-default");
                    AutomationProperties.SetAutomationId(fallbackCancel, "fallback-cancel");
                    panel.Children.Add(fallbackDefault); panel.Children.Add(fallbackCancel); window.UpdateLayout();
                    var axFallbackDefault = Find(root, "fallback-default"); var axFallbackCancel = Find(root, "fallback-cancel");
                    Css.SetStyle(save, "visibility: hidden"); Css.SetStyle(cancel, "visibility: hidden"); window.UpdateLayout();
                    Require(save.Visibility == Visibility.Visible && !save.IsVisible && !cancel.IsVisible,
                        "CSS-hidden button fixture does not differ from native Visibility");
                    Require(Default(native)?.Handle == axFallbackDefault.Handle && Cancel(native)?.Handle == axFallbackCancel.Handle,
                        "CSS-hidden first button masks the visible Window default/cancel reference");
                    var cssHost = (IInputDispatcherHost)window;
                    Require(ReferenceEquals(cssHost.FindButton(window, button => button.IsDefault), fallbackDefault)
                        && ReferenceEquals(cssHost.FindButton(window, button => button.IsCancel), fallbackCancel),
                        "CSS-hidden button remains the keyboard target");
                    int fallbackSaves = 0, fallbackCancels = 0;
                    fallbackDefault.Click += (_, _) => fallbackSaves++; fallbackCancel.Click += (_, _) => fallbackCancels++;
                    DispatchKey(window, 0x0d); DispatchKey(window, 0x1b);
                    Require(saves == 0 && cancels == 0 && fallbackSaves == 1 && fallbackCancels == 1,
                        "actual input-host Enter/Escape did not invoke only the visible fallback actions");
                    Css.SetStyle(save, "visibility: visible"); Css.SetStyle(cancel, "visibility: visible"); window.UpdateLayout();
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "CSS reappearance changed the original button identity or priority");
                    break;
                case 8:
                    var cssPanel = new StackPanel();
                    var inheritedDefault = new Button { Content = "继承隐藏的确认", IsDefault = true };
                    var inheritedCancel = new Button { Content = "继承隐藏的取消", IsCancel = true };
                    var revealedDefault = new Button { Content = "显式显示的确认", IsDefault = true };
                    var revealedCancel = new Button { Content = "显式显示的取消", IsCancel = true };
                    AutomationProperties.SetAutomationId(revealedDefault, "revealed-default");
                    AutomationProperties.SetAutomationId(revealedCancel, "revealed-cancel");
                    cssPanel.Children.Add(inheritedDefault); cssPanel.Children.Add(inheritedCancel);
                    cssPanel.Children.Add(revealedDefault); cssPanel.Children.Add(revealedCancel); panel.Children.Insert(0, cssPanel);
                    window.UpdateLayout(); var axRevealedDefault = Find(root, "revealed-default"); var axRevealedCancel = Find(root, "revealed-cancel");
                    Css.SetStyle(cssPanel, "visibility: hidden"); Css.SetStyle(revealedDefault, "visibility: visible");
                    Css.SetStyle(revealedCancel, "visibility: visible"); window.UpdateLayout();
                    Require(!cssPanel.IsVisible && !inheritedDefault.IsVisible && revealedDefault.IsVisible && revealedCancel.IsVisible,
                        "CSS override fixture does not have mixed visibility");
                    Require(Default(native)?.Handle == axRevealedDefault.Handle && Cancel(native)?.Handle == axRevealedCancel.Handle,
                        "hidden CSS box masks its explicitly visible default/cancel descendants");
                    var overrideHost = (IInputDispatcherHost)window;
                    Require(ReferenceEquals(overrideHost.FindButton(window, button => button.IsDefault), revealedDefault)
                        && ReferenceEquals(overrideHost.FindButton(window, button => button.IsCancel), revealedCancel),
                        "keyboard lookup does not share CSS override rules with Window AX");
                    int revealedSaves = 0, revealedCancels = 0;
                    revealedDefault.Click += (_, _) => revealedSaves++; revealedCancel.Click += (_, _) => revealedCancels++;
                    DispatchKey(window, 0x0d); DispatchKey(window, 0x1b);
                    Require(revealedSaves == 1 && revealedCancels == 1 && saves == 0 && cancels == 0,
                        "input-host keys did not invoke the CSS-visible descendants");
                    cssPanel.Visibility = Visibility.Collapsed; window.UpdateLayout();
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "native collapsed ancestor does not gate CSS-visible descendants");
                    cssPanel.ClearValue(UIElement.VisibilityProperty); Css.SetStyle(cssPanel, "display: none"); window.UpdateLayout();
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "display:none ancestor does not gate CSS-visible descendants");
                    break;
                case 9:
                    var flex = new StackPanel(); var collapsedFlexItem = new StackPanel();
                    var flexDefault = new Button { Content = "折叠项目中的确认", IsDefault = true };
                    var flexCancel = new Button { Content = "折叠项目中的取消", IsCancel = true };
                    collapsedFlexItem.Children.Add(flexDefault); collapsedFlexItem.Children.Add(flexCancel);
                    flex.Children.Add(collapsedFlexItem); panel.Children.Insert(0, flex);
                    Css.SetStyle(flex, "display: flex"); Css.SetStyle(collapsedFlexItem, "visibility: collapse");
                    Css.SetStyle(flexDefault, "visibility: visible"); Css.SetStyle(flexCancel, "visibility: visible"); window.UpdateLayout();
                    Require(collapsedFlexItem.Visibility == Visibility.Visible && !flexDefault.IsVisible && !flexCancel.IsVisible,
                        "CSS collapsed flex fixture does not gate its visible override");
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                        "CSS collapsed flex item masks the visible Window actions");
                    var flexHost = (IInputDispatcherHost)window;
                    Require(ReferenceEquals(flexHost.FindButton(window, button => button.IsDefault), save)
                        && ReferenceEquals(flexHost.FindButton(window, button => button.IsCancel), cancel),
                        "keyboard lookup still selects a collapsed CSS flex descendant");
                    DispatchKey(window, 0x0d); DispatchKey(window, 0x1b);
                    Require(saves == 1 && cancels == 1, "collapsed flex subtree blocked actual Enter/Escape fallbacks");
                    break;
                case 10:
                    var exitingGroup = new StackPanel();
                    var exitingDefault = new Button { Content = "正在退出的确认", IsDefault = true };
                    var exitingCancel = new Button { Content = "正在退出的取消", IsCancel = true };
                    AutomationProperties.SetAutomationId(exitingDefault, "exiting-default");
                    AutomationProperties.SetAutomationId(exitingCancel, "exiting-cancel");
                    exitingGroup.Children.Add(exitingDefault); exitingGroup.Children.Add(exitingCancel);
                    panel.Children.Insert(0, exitingGroup);
                    Css.SetStyle(exitingGroup, "display: block; transition: display 60s allow-discrete"); Pump(window);
                    var axExitingDefault = Find(root, "exiting-default"); var axExitingCancel = Find(root, "exiting-cancel");
                    Require(Default(native)?.Handle == axExitingDefault.Handle && Cancel(native)?.Handle == axExitingCancel.Handle,
                        "exiting fixture was not initially the Window action pair");
                    int exitingSaves = 0, exitingCancels = 0;
                    exitingDefault.Click += (_, _) => exitingSaves++; exitingCancel.Click += (_, _) => exitingCancels++;
                    for (int transitionKind = 0; transitionKind < 2; transitionKind++)
                    {
                        if (transitionKind == 0) Css.SetStyle(exitingGroup, "display: none; transition: display 60s allow-discrete");
                        else
                        {
                            Css.SetStyle(exitingDefault, "display: block; transition: display 60s allow-discrete");
                            Css.SetStyle(exitingCancel, "display: block; transition: display 60s allow-discrete");
                            Css.SetStyle(exitingDefault, "display: none; transition: display 60s allow-discrete");
                            Css.SetStyle(exitingCancel, "display: none; transition: display 60s allow-discrete");
                        }
                        window.UpdateLayout();
                        Require(exitingDefault.IsVisible && exitingCancel.IsVisible
                            && CssDisplayProperties.IsExitInert(exitingDefault) && CssDisplayProperties.IsExitInert(exitingCancel),
                            "display exit fixture is not rendered and inert during its transition");
                        Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle,
                            "exiting display transition still shadows live Window actions");
                        Require(!Contains(root, "exiting-default") && !Contains(root, "exiting-cancel")
                            && !Press(axExitingDefault) && !Press(axExitingCancel),
                            "exiting controls or their cached AX actions remain exposed");
                        DispatchKey(window, 0x0d); DispatchKey(window, 0x1b);
                        Require(exitingSaves == 0 && exitingCancels == 0 && saves == transitionKind + 1 && cancels == transitionKind + 1,
                            "display exit did not route Enter/Escape exclusively to live fallbacks");
                        Css.SetStyle(exitingGroup, "display: block; transition: none");
                        Css.SetStyle(exitingDefault, "display: block; transition: none");
                        Css.SetStyle(exitingCancel, "display: block; transition: none"); window.UpdateLayout();
                        Require(Default(native)?.Handle == axExitingDefault.Handle && Cancel(native)?.Handle == axExitingCancel.Handle,
                            "canceling display exit lost the existing AX button identities");
                    }
                    break;
                case 11:
                    var reusedGroup = new StackPanel();
                    var reusedFlex = new StackPanel(); reusedFlex.Children.Add(reusedGroup);
                    var reusedDefault = new Button { Content = "重用后代确认", IsDefault = true };
                    var reusedCancel = new Button { Content = "重用后代取消", IsCancel = true };
                    AutomationProperties.SetAutomationId(reusedDefault, "reused-default");
                    AutomationProperties.SetAutomationId(reusedCancel, "reused-cancel");
                    reusedGroup.Children.Add(reusedDefault); reusedGroup.Children.Add(reusedCancel);
                    panel.Children.Insert(0, reusedFlex); Css.SetStyle(reusedFlex, "display: flex");
                    Css.SetStyle(reusedDefault, "visibility: visible"); Css.SetStyle(reusedCancel, "visibility: visible");
                    Css.SetStyle(reusedGroup, "display: block; visibility: visible; transition: display 60s allow-discrete"); Pump(window);
                    var axReusedDefault = Find(root, "reused-default"); var axReusedCancel = Find(root, "reused-cancel");
                    Css.SetStyle(reusedGroup, "display: none; visibility: visible; transition: display 60s allow-discrete");
                    Require(CssDisplayProperties.IsExitInert(reusedDefault), "reused fixture did not enter display exit");
                    Css.SetStyle(reusedGroup, "display: block; visibility: visible; transition: none");
                    Css.SetStyle(reusedGroup, "visibility: hidden");
                    Css.SetStyle(reusedDefault, string.Empty); Css.SetStyle(reusedCancel, string.Empty); Pump(window);
                    Require(!reusedDefault.IsVisible && !reusedCancel.IsVisible,
                        "display presentation overrides newly inherited CSS visibility");
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle
                        && !Contains(root, "reused-default") && !Contains(root, "reused-cancel")
                        && !Press(axReusedDefault) && !Press(axReusedCancel),
                        "reused hidden descendants retain native Window references or cached AX actions");
                    int reusedCalls = 0;
                    reusedDefault.Click += (_, _) => reusedCalls++; reusedCancel.Click += (_, _) => reusedCalls++;
                    DispatchKey(window, 0x0d); DispatchKey(window, 0x1b);
                    Require(reusedCalls == 0 && saves == 1 && cancels == 1,
                        "reused hidden descendants receive Enter/Escape");
                    Css.SetStyle(reusedDefault, "visibility: visible"); Css.SetStyle(reusedCancel, "visibility: visible"); Pump(window);
                    Require(Default(native)?.Handle == axReusedDefault.Handle && Cancel(native)?.Handle == axReusedCancel.Handle
                        && Press(axReusedDefault) && Press(axReusedCancel) && reusedCalls == 2,
                        "explicitly visible reused descendants lost native identities or actions");
                    Css.SetStyle(reusedDefault, string.Empty); Css.SetStyle(reusedCancel, string.Empty); Pump(window);
                    Require(Default(native)?.Handle == axSave.Handle && Cancel(native)?.Handle == axCancel.Handle
                        && !Contains(root, "reused-default") && !Contains(root, "reused-cancel"),
                        "clearing CSS visible overrides exposes inherited hidden descendants again");
                    break;
            }
            Console.WriteLine($"PASS: {s_names[index]}"); return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {s_names[index]}: {exception}"); return 1;
        }
        finally { contentDialog?.Hide(); window.Close(); }
    }

    private static void CheckModal(Window owner, bool useCancel)
    {
        var save = new Button { Content = "确认结果", IsDefault = true, Height = 32 };
        var cancel = new Button { Content = "取消结果", IsCancel = true, Height = 32 };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(save); panel.Children.Add(cancel);
        var dialog = CreateWindow("模态操作结果", panel); dialog.Owner = owner;
        save.Click += (_, _) => dialog.DialogResult = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        int closing = 0;
        dialog.Closing += (_, e) => { closing++; e.Cancel = closing == 1; };
        dialog.Shown += (_, _) =>
        {
            var native = Native(dialog);
            var action = useCancel ? Cancel(native) : Default(native);
            Require(action != null && Press(action), "modal Window action was not exposed");
            Require(closing == 1 && dialog.DialogResult == null && native.AccessibilityModal
                && !Native(owner).AccessibilityEnabled, "canceled action changed modal or owner state");
            Require(Press(action!) && closing == 2 && dialog.Handle == 0,
                "accepted Window action did not close the dialog");
        };
        try
        {
            Require(dialog.ShowDialog() == !useCancel && owner.IsEnabled,
                "default/cancel action result or owner restoration differs");
        }
        finally { dialog.Close(); }
    }

    private static Window CreateWindow(string title, UIElement content) => new()
    {
        Title = title, Width = 560, Height = 560, Content = content,
        TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
    };
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
    private static NSAccessibilityElement? Default(NSWindow window) => window.AccessibilityDefaultButton as NSAccessibilityElement;
    private static NSAccessibilityElement? Cancel(NSWindow window) => window.AccessibilityCancelButton as NSAccessibilityElement;
    private static NSAccessibilityElement Root(Window window) =>
        Runtime.GetNSObject<NSView>(window.Handle)!.AccessibilityChildren!.OfType<NSAccessibilityElement>().Single();
    private static NSAccessibilityElement Find(NSAccessibilityElement node, string id)
    {
        if (node.AccessibilityIdentifier == id) return node;
        foreach (var child in node.AccessibilityChildren?.OfType<NSAccessibilityElement>() ?? [])
        {
            try { return Find(child, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"AX element not found: {id}");
    }
    private static bool Contains(NSAccessibilityElement node, string id) => node.AccessibilityIdentifier == id
        || (node.AccessibilityChildren?.OfType<NSAccessibilityElement>() ?? []).Any(child => Contains(child, id));
    private static void Pump(Window window)
    {
        window.UpdateLayout(); Dispatcher.GetForCurrentThread().ProcessQueue(); window.UpdateLayout();
    }
    private static void DispatchKey(Window window, int keyCode) =>
        typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = keyCode }]);
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
