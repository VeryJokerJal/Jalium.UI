using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Native Window AX state during actual managed modal lifecycles.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowAccessibilityChecks
{
    private static readonly string[] s_names =
    [
        "native Window enabled state follows managed disable/restore",
        "ShowDialog exposes modal dialog semantics and native ancestry",
        "native AX close preserves canceled modal state and closes safely",
        "hidden dialog reuses its native Window as a modeless window",
        "nested dialogs expose modal and disabled states independently",
        "declared modeless dialog subrole does not imply modality",
        "AX Invoke returns before opening nested modal windows",
        "queued AX Invoke rejects a hidden window",
        "queued AX Invoke rejects a disabled control",
        "queued AX Invoke rejects a removed control",
        "queued AX Invoke rejects CSS hidden content",
        "queued AX Invoke rejects display exit content",
        "queued AX Invoke rejects a closed window",
        "queued AX Invoke contains user exceptions and remains usable"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < s_names.Length; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-accessibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill(); failed++;
                Console.Error.WriteLine($"FAIL: Window AX case {index} timed out");
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window accessibility host checks: {s_names.Length - failed}/{s_names.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-accessibility-case=".Length), out int index)
            || (uint)index >= s_names.Length) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var owner = CreateWindow("中文所属窗口");
        var dialog = CreateWindow("中文对话框");
        var nested = CreateWindow("嵌套对话框");
        var application = new Application { MainWindow = owner, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            owner.Show();
            var ownerNative = Native(owner);
            if (index >= 6)
            {
                CheckDeferredInvoke(index, owner, dialog, nested);
            }
            else if (index == 0)
            {
                Require(ownerNative.AccessibilityRole == "AXWindow" && ownerNative.AccessibilityEnabled
                    && !ownerNative.AccessibilityModal, "normal native Window AX state differs");
                owner.IsEnabled = false;
                Require(!ownerNative.AccessibilityEnabled && !Root(owner).AccessibilityEnabled,
                    "disabled managed Window is still AX enabled");
                Require(!ownerNative.StandardWindowButton(NSWindowButton.CloseButton).AccessibilityEnabled,
                    "disabled Window close button is AX enabled");
                owner.IsEnabled = true;
                Require(ownerNative.AccessibilityEnabled && Root(owner).AccessibilityEnabled,
                    "enabled Window was not restored in AX");
            }
            else if (index == 5)
            {
                AutomationProperties.SetIsDialog(dialog, true);
                dialog.Show();
                var native = Native(dialog);
                Require(native.AccessibilitySubrole == "AXDialog" && !native.AccessibilityModal,
                    "declared modeless dialog role/modality differs");
                AutomationProperties.SetIsDialog(dialog, false);
                Require(native.AccessibilitySubrole == "AXStandardWindow" && !native.AccessibilityModal,
                    "cleared dialog annotation left stale native AX state");
            }
            else
            {
                dialog.Owner = owner;
                int shown = 0, closing = 0;
                if (index == 2) dialog.Closing += (_, e) => { closing++; e.Cancel = closing == 1; };
                dialog.Shown += (_, _) =>
                {
                    shown++;
                    var native = Native(dialog);
                    if (index == 3 && shown == 2)
                    {
                        Require(!native.AccessibilityModal && native.AccessibilitySubrole == "AXStandardWindow"
                            && !UIElementAutomationPeer.CreatePeerForElement(dialog)!.IsDialog(), "modeless reuse retained modal AX state");
                        return;
                    }
                    Require(native.AccessibilityRole == "AXWindow" && native.AccessibilityModal
                        && native.AccessibilitySubrole == "AXDialog" && UIElementAutomationPeer.CreatePeerForElement(dialog)!.IsDialog(),
                        $"modal Window role differs: role={native.AccessibilityRole}; subrole={native.AccessibilitySubrole}; modal={native.AccessibilityModal}");
                    Require(native.AccessibilityEnabled && !ownerNative.AccessibilityEnabled,
                        "dialog/owner AX enabled states do not follow modality");
                    var root = Root(dialog);
                    Require(root.AccessibilityRole == "AXGroup" && root.AccessibilityLabel == dialog.Title
                        && root.AccessibilityParent?.Handle == native.Handle
                        && root.AccessibilityWindow?.Handle == native.Handle
                        && root.AccessibilityTopLevelUIElement?.Handle == native.Handle,
                        "dialog content lost native Window accessibility ancestry");
                    Require(native.ParentWindow?.Handle == ownerNative.Handle
                        && ownerNative.ChildWindows?.Any(child => child.Handle == native.Handle) == true,
                        "modal native ownership differs");
                    if (index == 2)
                    {
                        var close = native.StandardWindowButton(NSWindowButton.CloseButton);
                        _ = close.AccessibilityPerformPress();
                        Require(closing == 1 && dialog.Handle != 0 && native.AccessibilityModal
                            && native.AccessibilitySubrole == "AXDialog" && !ownerNative.AccessibilityEnabled,
                            "canceled native AX close ended modality");
                        _ = close.AccessibilityPerformPress();
                        Require(closing == 2 && dialog.Handle == 0, "accepted native AX close did not close managed dialog");
                        return;
                    }
                    if (index == 4)
                    {
                        nested.Owner = dialog;
                        nested.Shown += (_, _) =>
                        {
                            var secondNative = Native(nested);
                            Require(secondNative.AccessibilityModal && secondNative.AccessibilitySubrole == "AXDialog"
                                && secondNative.AccessibilityEnabled && native.AccessibilityModal
                                && !native.AccessibilityEnabled && !ownerNative.AccessibilityEnabled,
                                "nested modality lost disabled outer dialog state");
                            Require(secondNative.ParentWindow?.Handle == native.Handle, "nested native parent differs");
                            nested.Hide();
                        };
                        Require(nested.ShowDialog() == false, "hidden nested dialog result differs");
                        Require(native.AccessibilityModal && native.AccessibilityEnabled && !ownerNative.AccessibilityEnabled,
                            "nested return restored the wrong Window AX state");
                    }
                    dialog.Hide();
                };
                Require(dialog.ShowDialog() == false, "modal dismissal result differs");
                Require(ownerNative.AccessibilityEnabled, "modal return did not restore owner AX enabled state");
                if (index != 2)
                {
                    var native = Native(dialog);
                    Require(!native.AccessibilityModal && native.AccessibilitySubrole == "AXStandardWindow"
                        && !UIElementAutomationPeer.CreatePeerForElement(dialog)!.IsDialog(), "hidden dialog retained modal semantics");
                    if (index == 3)
                    {
                        nint handle = dialog.Handle;
                        dialog.Show();
                        Require(dialog.Handle == handle && shown == 2 && native.AccessibilityEnabled,
                            "modeless reuse changed Window identity or enabled state");
                    }
                }
            }
            Console.WriteLine($"PASS: {s_names[index]}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {s_names[index]}: {exception}");
            return 1;
        }
        finally { nested.Close(); dialog.Close(); owner.Close(); }
    }

    private static void CheckDeferredInvoke(int index, Window owner, Window dialog, Window nested)
    {
        var button = new Button { Content = "打开对话框", Height = 32 };
        AutomationProperties.SetAutomationId(button, "modal-invoke");
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(button); owner.Content = panel; owner.UpdateLayout();
        var action = Find(Root(owner), "modal-invoke");
        int calls = 0;
        button.Click += (_, _) => calls++;
        if (index == 6)
        {
            bool returned = false, modalReturned = false, innerReturned = false;
            button.Click += (_, _) =>
            {
                Require(returned, "AX Invoke entered user code before returning to AppKit");
                dialog.Owner = owner;
                dialog.Shown += (_, _) =>
                {
                    Require(!owner.IsEnabled && Native(dialog).AccessibilityModal, "queued Invoke did not enter modal state");
                    RequireOrdinaryCapabilities(Root(dialog));
                    Dispatcher.GetForCurrentThread().BeginInvoke(() =>
                    {
                        nested.Owner = dialog;
                        nested.Shown += (_, _) =>
                        {
                            Require(!dialog.IsEnabled && !owner.IsEnabled, "nested Invoke enabled an outer window");
                            RequireOrdinaryCapabilities(Root(nested));
                            Dispatcher.GetForCurrentThread().BeginInvoke(() => nested.DialogResult = true);
                        };
                        innerReturned = nested.ShowDialog() == true;
                        Require(dialog.IsEnabled && !owner.IsEnabled, "nested Invoke restored the wrong window");
                        dialog.DialogResult = false;
                    });
                };
                modalReturned = dialog.ShowDialog() == false;
            };
            Require(action.AccessibilityPerformPress(), "AX Invoke was not accepted");
            returned = true;
            Require(calls == 0, "AX Invoke ran synchronously");
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Require(calls == 1 && modalReturned && innerReturned && owner.IsEnabled,
                "queued Invoke or nested modal result/restoration differs");
            return;
        }
        if (index == 13)
        {
            RoutedEventHandler throwing = (_, _) => throw new InvalidOperationException("expected deferred AX failure");
            button.Click += throwing;
            Require(action.AccessibilityPerformPress() && calls == 0, "throwing Invoke did not return before user code");
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Require(calls == 1 && owner.Handle != 0, "queued user exception escaped or closed the window");
            button.Click -= throwing;
            Require(action.AccessibilityPerformPress(), "subsequent Invoke was rejected");
            Dispatcher.GetForCurrentThread().ProcessQueue();
            Require(calls == 2, "queued failure broke subsequent Invoke");
            return;
        }
        Require(action.AccessibilityPerformPress() && calls == 0, "AX Invoke ran before dispatch");
        switch (index)
        {
            case 7: owner.Hide(); break;
            case 8: button.IsEnabled = false; break;
            case 9: panel.Children.Remove(button); break;
            case 10: Jalium.UI.Styling.Css.SetStyle(button, "visibility: hidden"); break;
            case 11:
                Jalium.UI.Styling.Css.SetStyle(button, "display: block; transition: display 60s allow-discrete");
                Jalium.UI.Styling.Css.SetStyle(button, "display: none; transition: display 60s allow-discrete");
                break;
            case 12: owner.Close(); break;
        }
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Require(calls == 0, "queued Invoke ran after its target became unavailable");
    }

    private static NSAccessibilityElement Find(NSAccessibilityElement node, string id)
    {
        if (node.AccessibilityIdentifier == id) return node;
        foreach (var child in node.AccessibilityChildren?.OfType<NSAccessibilityElement>() ?? [])
        {
            try { return Find(child, id); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"AX element not found: {id}");
    }

    private static void RequireOrdinaryCapabilities(NSAccessibilityElement root)
    {
        foreach (var element in new[] { root }.Concat(root.AccessibilityChildren?.OfType<NSAccessibilityElement>() ?? []))
            foreach (string selector in new[] { "isAccessibilitySelected", "isAccessibilityExpanded", "setAccessibilitySelected:", "setAccessibilityExpanded:", "setAccessibilityValue:" })
                Require(!element.IsAccessibilitySelectorAllowed(new Selector(selector)), $"ordinary modal content advertises {selector}");
    }

    private static Window CreateWindow(string title) => new()
    {
        Title = title, Width = 360, Height = 240,
        TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
        Content = new TextBlock { Text = title, Margin = new Thickness(16) }
    };
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
    private static NSAccessibilityElement Root(Window window) =>
        Runtime.GetNSObject<NSView>(window.Handle)!.AccessibilityChildren!.OfType<NSAccessibilityElement>().Single();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
