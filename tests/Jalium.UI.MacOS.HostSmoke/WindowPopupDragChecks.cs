#pragma warning disable WPF0001
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Point = Jalium.UI.Point;
using Selector = ObjCRuntime.Selector;

namespace Jalium.UI.MacOS;

internal static class WindowPopupDragChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 12) return 2;
        int depth = index / 4; bool custom = index % 2 != 0, software = index % 4 >= 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_POPUP_DRAG_OUTPUT")!; Directory.CreateDirectory(output);
        bool requireForeground = Environment.GetEnvironmentVariable("JALIUM_POPUP_DRAG_REQUIRE_FOREGROUND") != "0";
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var render = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (!software) render.DefaultRenderingEngine = RenderingEngine.Impeller;
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var checks = new List<string>(); var failures = new List<string>(); var scenarios = new List<object>();
        var focus = new List<object>(); var monitors = new List<object>(); int unavailable = 0; bool done = false;
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Attempt(string name, Action body)
        {
            int start = checks.Count; Exception? error = null;
            try { body(); } catch (Exception ex) { error = ex; failures.Add(name + ": " + ex); }
            scenarios.Add(new { name, passed = error is null, checks = checks.Skip(start).ToArray(), error = error?.ToString() });
        }
        var editor = new TextBox { Text = "文字保留 Miii 中文🙂", Height = 40 };
        AutomationProperties.SetName(editor, "拖放验证编辑框");
        var anchor = new Button { Content = "显示拖放区域", Height = 42, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock { Text = "检查准备中", FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var ownerBody = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        ownerBody.Children.Add(new TextBlock { Text = "弹窗拖放与捕获", FontSize = 24 });
        ownerBody.Children.Add(new TextBlock { Text = "文件交付后保留文字，拖动结束后释放按压。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        ownerBody.Children.Add(editor); ownerBody.Children.Add(anchor);
        var ownerCanvas = Body(); ownerBody.Children.Add(ownerCanvas); ownerBody.Children.Add(status);
        var owner = new Window { Title = "Jalium Window v296 · 弹窗拖放 " + index, Width = custom ? 460 : 760, Height = 720,
            MinWidth = 440, MinHeight = 680, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Content = ownerBody, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        owner.SetResourceReference(Window.BackgroundProperty, "WindowBackground"); Application.Current!.MainWindow = owner;
        var popups = new List<Popup>(); Popup? sourcePopup = null; var sourceBody = ownerCanvas; UIElement nextAnchor = anchor;
        var targetText = new TextBlock { Text = "接收文件\nMiii 中文🙂", FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var target = Box(targetText); var same = Box(new TextBlock { Text = "后继操作\n文字保持", FontSize = 16, TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetName(target, "文件接收区域"); AutomationProperties.SetName(same, "后继操作区域");
        Border? sibling = null; int sequence = 100, previews = 0, bubbles = 0, drops = 0;
        IDataObject? retained = null, entering = null;
        Window Physical(UIElement element)
        {
            for (Visual? node = element; node is not null; node = node.VisualParent) if (node is Window window) return window;
            throw new InvalidOperationException("element has no presenting window");
        }
        NSView View(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!;
        void Pump()
        {
            NativeMethods.PlatformPollEvents(); Dispatcher.CurrentDispatcher.ProcessQueue(); owner.UpdateLayout();
            foreach (var popup in popups) if (popup.IsOpen) Physical((UIElement)popup.Child!).UpdateLayout();
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
        }
        void Observe(UIElement element)
        {
            var surface = Physical(element); var view = View(surface); var native = view.Window!;
            native.MakeKeyAndOrderFront(null); app.ActivateIgnoringOtherApps(true); native.MakeFirstResponder(view); element.Focus(); Pump();
            int frontmost = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1;
            bool active = app.Active && frontmost == Environment.ProcessId && native.IsKeyWindow && native.FirstResponder?.Handle == view.Handle;
            var point = element.TranslatePoint(new Point(5, 5), surface);
            focus.Add(new { active, frontmost, processId = Environment.ProcessId, appActive = app.Active,
                nativeKey = native.IsKeyWindow, responder = native.FirstResponder?.Handle.ToString(), ownResponder = view.Handle.ToString(),
                frame = native.Frame.ToString(), point = point.ToString() });
            if (!active) unavailable++;
            Require(ReferenceEquals(Keyboard.FocusedElement, element) && native.FirstResponder?.Handle == view.Handle,
                "logical focus agrees with the own native view responder");
            if (requireForeground) Require(active, "the actual native key responder agrees with the system foreground");
            Require(point.X >= 0 && point.Y >= 0 && point.X < surface.ActualWidth && point.Y < surface.ActualHeight,
                "the target lies inside its presenting physical window viewport");
        }
        void Capture(Window surface, string name)
        {
            surface.UpdateLayout(); double dpi = surface.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(surface.ActualWidth * dpi), (int)Math.Ceiling(surface.ActualHeight * dpi), dpi,
                drawing => { drawing.DrawRectangle(surface.Background, null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)); surface.Render(drawing); }, () => surface.Handle != 0);
            Require(bitmap is not null, "the requested normal renderer produces an actual readback");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap!));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        void Classify(DragEventArgs e, bool preview, bool drop)
        {
            if (preview) previews++; else bubbles++;
            var local = e.GetPosition(target); var surface = Physical(target); var root = target.TranslatePoint(new Point(5, 5), surface);
            Require(Math.Abs(local.X - 5) < .05 && Math.Abs(local.Y - 5) < .05, "native drag coordinates are relative to the requested target");
            Require(Math.Abs(e.GetPosition(surface).X - root.X) < .05 && Math.Abs(e.GetPosition(null).Y - root.Y) < .05,
                "the physical-root and null-relative drag coordinates retain their original space");
            var sourceView = View(surface); var ownerView = View(owner);
            var screenPoint = sourceView.Window!.ConvertPointToScreen(sourceView.ConvertPointToView(new CGPoint(root.X, root.Y), null));
            var expectedOwner = ownerView.ConvertPointFromView(ownerView.Window!.ConvertPointFromScreen(screenPoint), null);
            var ownerPoint = e.GetPosition(owner);
            Require(Math.Abs(ownerPoint.X - expectedOwner.X) < .05 && Math.Abs(ownerPoint.Y - expectedOwner.Y) < .05,
                "cross-window drag coordinates match independent AppKit screen-to-client conversion");
            Require((string?)e.Data.GetData(DataFormats.UnicodeText) == "交付文字🙂", "the real native pasteboard preserves UTF16 text");
            Require(e.Data.GetData(DataFormats.FileDrop) is string[] files && files.SequenceEqual(new[] { "/tmp/one file.txt", "/tmp/中文🙂.txt" }),
                "the real native pasteboard delivers both decoded file paths");
            e.Effects = DragDropEffects.Copy;
            if (!drop) { entering ??= e.Data; return; }
            if (preview) return;
            drops++; retained = e.Data; Require(ReferenceEquals(entering, retained), "a target visit preserves one data object through Drop");
        }
        target.PreviewDragEnter += (_, e) => Classify(e, true, false); target.DragEnter += (_, e) => Classify(e, false, false);
        target.PreviewDragOver += (_, e) => Classify(e, true, false); target.DragOver += (_, e) => Classify(e, false, false);
        target.PreviewDrop += (_, e) => Classify(e, true, true); target.Drop += (_, e) => Classify(e, false, true);
        void Reset()
        {
            if (target.VisualParent is Canvas old && !ReferenceEquals(old, sourceBody)) old.Children.Remove(target);
            if (!sourceBody.Children.Contains(target)) sourceBody.Children.Add(target);
            target.IsEnabled = true; target.Visibility = Visibility.Visible;
            if (sourcePopup is not null && !sourcePopup.IsOpen) sourcePopup.IsOpen = true;
            entering = retained = null; Pump(); Observe(target);
        }
        Border Sibling()
        {
            if (sibling is not null) return sibling;
            sibling = Box(new TextBlock { Text = "同一所属窗口\n独立后继弹窗", FontSize = 16, TextWrapping = TextWrapping.Wrap });
            var body = Body(); body.Children.Add(sibling); Canvas.SetLeft(sibling, 16); Canvas.SetTop(sibling, 70);
            var popup = new Popup { PlacementTarget = anchor, Child = body, PreferExternalWindow = true,
                ShouldConstrainToRootBounds = false, StaysOpen = true, PopupAnimation = PopupAnimation.None, Placement = PlacementMode.Right };
            popups.Add(popup); popup.IsOpen = true; Pump(); return sibling;
        }
        void Down(UIElement element)
        {
            Observe(element); var surface = Physical(element); var view = View(surface);
            var point = element.TranslatePoint(new Point(5, 5), surface);
            var nativePoint = view.ConvertPointToView(new CGPoint(point.X, point.Y), null);
            using var sample = NSEvent.MouseEvent(NSEventType.LeftMouseDown, nativePoint, 0, NSProcessInfo.ProcessInfo.SystemUptime,
                view.Window!.WindowNumber, null, 1, 1, 1);
            VoidMessage(view.Handle, new Selector("mouseDown:").Handle, sample!.Handle);
            Require(Mouse.LeftButton == MouseButtonState.Pressed && element.GetValue(UIElement.IsPressedProperty) is true,
                "an own-process AppKit mouse sample reaches the managed pressed state");
            Require(element.CaptureMouse() && ReferenceEquals(Mouse.Captured, element), "the presenting surface owns the requested capture");
        }
        void Complete(Window window)
        {
            try { typeof(Window).GetMethod("CompletePlatformDragInput", Private)!.Invoke(window, null); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); }
        }
        Action Completion(Window window)
        {
            var method = typeof(Window).GetMethod("CapturePlatformDragInputCompletion", Private);
            return method is null ? () => Complete(window) : (Action)method.Invoke(window, null)!;
        }
        void Replay(string? phase = null, Action? retirement = null, bool handled = false)
        {
            Reset(); var view = View(Physical(target)); var root = target.TranslatePoint(new Point(5, 5), Physical(target));
            using var board = NSPasteboard.CreateWithUniqueName();
            using var first = new NSPasteboardItem(); using var second = new NSPasteboardItem();
            first.SetStringForType("交付文字🙂", "public.utf8-plain-text"); first.SetStringForType("<b>保留🙂</b>", "public.html");
            first.SetStringForType("file:///tmp/one%20file.txt", "public.file-url"); second.SetStringForType("file:///tmp/中文%F0%9F%99%82.txt", "public.file-url");
            Require(board.WriteObjects(new[] { first, second }), "the unique owned drag pasteboard accepts its items");
            using var info = new JaliumOwnedDragInfoV143(board, view.ConvertPointToView(new CGPoint(root.X, root.Y), null), ++sequence);
            int changed = 0;
            DragEventHandler change = (_, e) => { changed++; if (handled) { e.Handled = true; e.Effects = DragDropEffects.Copy; } retirement?.Invoke(); };
            var route = phase == "enter" ? DragDrop.PreviewDragEnterEvent : phase == "over" ? DragDrop.PreviewDragOverEvent : DragDrop.PreviewDropEvent;
            int oldDrops = drops, oldBubbles = bubbles, oldPreviews = previews;
            if (phase is not null) target.AddHandler(route, change);
            try
            {
                nuint Enter() => Message(view.Handle, new Selector("draggingEntered:").Handle, info.Handle);
                nuint Over() => Message(view.Handle, new Selector("draggingUpdated:").Handle, info.Handle);
                nuint Drop() => Message(view.Handle, new Selector("performDragOperation:").Handle, info.Handle);
                if (phase == "enter") Require(Enter() == (handled ? (nuint)NSDragOperation.Copy : 0), "native Enter honors the current preview result");
                else
                {
                    Require(Enter() == (nuint)NSDragOperation.Copy, "actual native Enter selects Copy");
                    if (phase == "over") Require(Over() == (handled ? (nuint)NSDragOperation.Copy : 0), "native Over honors the current preview result");
                    else
                    {
                        Require(Over() == (nuint)NSDragOperation.Copy, "actual native Over selects Copy");
                        Require((Drop() != 0) == (phase is null || handled), "actual native Drop accepts only the current visit");
                    }
                }
                if (phase is null)
                {
                    Require(previews == oldPreviews + 3 && bubbles == oldBubbles + 3 && drops == oldDrops + 1,
                        "native Enter Over Drop each deliver exactly one preview and bubble");
                    board.ClearContents(); Require(retained?.GetData(DataFormats.Html) is string html && html == "<b>保留🙂</b>",
                        "Drop snapshots secondary formats before its pasteboard is retired");
                }
                else
                {
                    Require(changed == 1, "the real preview invokes its handled or retirement callback exactly once");
                    Require(bubbles == oldBubbles + (phase == "enter" ? 0 : phase == "over" ? 1 : 2),
                        "retired or handled previews never publish their old bubble");
                }
            }
            finally { if (phase is not null) target.RemoveHandler(route, change); board.ReleaseGlobally(); }
        }
        void MonitorProbes()
        {
            var native = View(owner).Window!; int monitor = Array.FindIndex(NSScreen.Screens, screen => screen.Handle == native.Screen!.Handle);
            Require(MonitorInfo(monitor, out var expected) == 0, "actual AppKit screen matches the native monitor ABI");
            var field = typeof(Window).GetField("_dpiScale", Private)!; double actual = owner.DpiScale;
            var popup = sourcePopup ?? new Popup { PlacementTarget = anchor };
            var parent = typeof(Popup).GetField("_parentWindow", Private)!; object? previous = parent.GetValue(popup); parent.SetValue(popup, owner);
            try
            {
                foreach (double scale in new[] { 1d, 1.5, 2d })
                {
                    field.SetValue(owner, scale);
                    var area = (Rect)typeof(Popup).GetMethod("GetWorkingArea", Private)!.Invoke(popup, null)!;
                    monitors.Add(new { scale, actualScale = actual, expected = new { expected.WorkX, expected.WorkY, expected.WorkWidth, expected.WorkHeight }, area = area.ToString() });
                    Require(Math.Abs(area.X - expected.WorkX) < .05 && Math.Abs(area.Y - expected.WorkY) < .05 &&
                        Math.Abs(area.Width - expected.WorkWidth) < .05 && Math.Abs(area.Height - expected.WorkHeight) < .05,
                        "popup work-area output returns the native physical rectangle for every owner coordinate scale");
                }
            }
            finally { field.SetValue(owner, actual); parent.SetValue(popup, previous); }
        }
        owner.Show(); owner.UpdateLayout(); owner.Activate(); editor.Focus();
        for (int level = 0; level < depth; level++)
        {
            var body = Body(); var next = new Button { Content = "下一层拖放区域", Width = 160, Height = 42 };
            body.Children.Add(next); Canvas.SetLeft(next, 16); Canvas.SetTop(next, 12);
            var popup = new Popup { PlacementTarget = nextAnchor, Child = body, PreferExternalWindow = true,
                ShouldConstrainToRootBounds = false, StaysOpen = true, PopupAnimation = PopupAnimation.None,
                Placement = level == 0 ? PlacementMode.Bottom : PlacementMode.Right };
            popups.Add(popup); popup.IsOpen = true; nextAnchor = next; sourcePopup = popup; sourceBody = body;
        }
        sourceBody.Children.Add(target); sourceBody.Children.Add(same); Canvas.SetLeft(target, 16); Canvas.SetTop(target, 70);
        Canvas.SetLeft(same, 170); Canvas.SetTop(same, 70); Pump();
        var clock = Stopwatch.StartNew();
        using var timer = NSTimer.CreateRepeatingScheduledTimer(.08, _ =>
        {
            if (done) return;
            try
            {
                var view = View(owner); view.Window!.MakeKeyAndOrderFront(null); app.ActivateIgnoringOtherApps(true);
                view.Window.MakeFirstResponder(view); owner.Activate(); editor.Focus(); Pump();
                if (requireForeground && (!app.Active || NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier != Environment.ProcessId))
                { if (clock.Elapsed.TotalSeconds < 15) return; throw new InvalidOperationException("foreground qualification unavailable"); }
                done = true;
                Attempt("actual-monitor-coordinate-scales", MonitorProbes);
                Attempt("initial-readback", () => { Observe(editor); Capture(owner, "owner"); Reset(); Capture(Physical(target), "before"); });
                Attempt("native-visit-and-snapshot", () => Replay());
                foreach (string phase in new[] { "enter", "over", "drop" })
                {
                    Attempt("handled-preview-" + phase, () => Replay(phase, handled: true));
                    Attempt("hidden-preview-" + phase, () => Replay(phase, () => target.Visibility = Visibility.Hidden));
                    Attempt("disabled-preview-" + phase, () => Replay(phase, () => target.IsEnabled = false));
                    Attempt("detached-preview-" + phase, () => Replay(phase, () => sourceBody.Children.Remove(target)));
                    Attempt("reparented-preview-" + phase, () =>
                    {
                        var other = Sibling(); Replay(phase, () => { sourceBody.Children.Remove(target); ((Canvas)other.VisualParent!).Children.Add(target); });
                    });
                    if (sourcePopup is not null)
                    {
                        Attempt("closed-native-popup-preview-" + phase, () => Replay(phase, () => sourcePopup.IsOpen = false));
                        Attempt("reopened-native-popup-preview-" + phase, () => Replay(phase, () => { sourcePopup.IsOpen = false; sourcePopup.IsOpen = true; }));
                    }
                }
                Attempt("source-capture-release", () =>
                {
                    Reset(); Down(target); int lost = 0, ups = 0, cancels = 0; MouseEventHandler released = (_, _) => lost++;
                    RoutedEventHandler canceled = (_, _) => cancels++;
                    target.PointerCancel += canceled;
                    target.LostMouseCapture += released; target.MouseUp += (_, _) => ups++;
                    try { Complete(Physical(target)); Require(lost == 1 && Mouse.Captured is null && Mouse.LeftButton == MouseButtonState.Released && target.GetValue(UIElement.IsPressedProperty) is false && ups == 0,
                        "source completion releases its capture and press once without synthesizing MouseUp");
                        Require(cancels == 1, "the promoted mouse pointer session receives exactly one cancellation"); }
                    finally { target.LostMouseCapture -= released; target.PointerCancel -= canceled; }
                });
                foreach (string destination in new[] { "same", "sibling" })
                Attempt("tracking-successor-" + destination, () =>
                {
                    Reset(); var next = destination == "same" ? same : Sibling(); Down(target); var complete = Completion(Physical(target));
                    target.ReleaseMouseCapture(); Down(next); complete();
                    Require(ReferenceEquals(Mouse.Captured, next) && Mouse.LeftButton == MouseButtonState.Pressed && next.GetValue(UIElement.IsPressedProperty) is true,
                        "the tracking loop's old finally preserves a later press and capture");
                    Complete(Physical(next));
                });
                foreach (string destination in new[] { "same", "target" })
                foreach (bool throwing in new[] { false, true })
                Attempt("callback-successor-" + destination + "-" + throwing, () =>
                {
                    Reset(); var next = destination == "target" ? target : same; Down(target);
                    int lost = 0, staleCancels = 0; var error = new InvalidOperationException("owned callback successor");
                    MouseEventHandler callback = (_, _) => { lost++; Down(next); if (throwing) throw error; };
                    RoutedEventHandler canceled = (_, _) => staleCancels++;
                    target.LostMouseCapture += callback; target.PointerCancel += canceled;
                    try
                    {
                        Exception? raised = null; try { Complete(Physical(target)); } catch (Exception e) { raised = e; }
                        Require(throwing ? ReferenceEquals(raised, error) : raised is null, "capture cleanup preserves the application's callback exception contract");
                        Require(lost == 1 && ReferenceEquals(Mouse.Captured, next) && Mouse.LeftButton == MouseButtonState.Pressed && next.GetValue(UIElement.IsPressedProperty) is true,
                            "LostMouseCapture may start a new press without old cleanup clearing it");
                        Require(staleCancels == 0, "the retired pointer cancellation cannot follow a replacement press on the same control");
                    }
                    finally { target.LostMouseCapture -= callback; target.PointerCancel -= canceled; Complete(Physical(next)); }
                });
                Attempt("final-editing-and-readback", () =>
                {
                    Replay(); Reset(); Require(editor.Text == "文字保留 Miii 中文🙂", "drag operations preserve the editor's exact text");
                    Observe(editor); editor.Select(editor.Text.Length, 0);
                    using (var insertion = new NSString(" 继续🙂")) InsertText(View(owner).Handle,
                        new Selector("insertText:replacementRange:").Handle, insertion.Handle, new NSRange(nint.MaxValue, 0));
                    Pump(); Require(editor.Text == "文字保留 Miii 中文🙂 继续🙂", "own native text insertion still edits the focused field after drag completion");
                    Require(target.GetAutomationPeer()!.GetName() == "文件接收区域", "the drop area exposes its authored accessibility name");
                    targetText.Text = "文件数据已接收\nMiii 中文🙂"; status.Text = failures.Count == 0 ? "文件交付、捕获和后继操作检查通过。" : "检查发现问题，请查看记录。";
                    Capture(Physical(target), "after"); Observe(target);
                });
            }
            catch (Exception error) { done = true; failures.Add("scene: " + error); }
            if (!done) return;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { index, depth, custom, software,
                passed = failures.Count == 0 && unavailable == 0, programmaticPassed = failures.Count == 0,
                foregroundQualified = unavailable == 0 && focus.Count > 0, requireForeground, checks, failures, scenarios, focus, monitors, foregroundUnavailable = unavailable,
                physicalInput = false, desktopScreenshot = false, operatingSystemTrackedDrag = false, ownAppKitSamples = true,
                actualMultipleDisplays = false, renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD") }));
            foreach (var popup in popups.AsEnumerable().Reverse()) popup.IsOpen = false;
            owner.Close(); host.AllowQuit = true; app.Terminate(app);
        });
        app.Run(); return failures.Count == 0 && unavailable == 0 ? 0 : 1;
    }

    private static Canvas Body()
    {
        var body = new Canvas { Width = 320, Height = 240 };
        body.SetResourceReference(Panel.BackgroundProperty, "CardBackgroundFillColorDefaultBrush"); return body;
    }
    private static Border Box(TextBlock text)
    {
        var box = new Border { Width = 140, Height = 76, AllowDrop = true, Focusable = true, Padding = new Thickness(10), BorderThickness = new Thickness(1), Child = text };
        box.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush"); return box;
    }
    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        private bool _started; internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Monitor
    {
        internal int X, Y, Width, Height, WorkX, WorkY, WorkWidth, WorkHeight; internal float Scale; internal int RefreshRate, IsPrimary;
    }
    [DllImport("jalium.native.platform", EntryPoint = "jalium_platform_get_monitor_info")]
    private static extern int MonitorInfo(int index, out Monitor monitor);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint Message(nint receiver, nint selector, nint argument);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void VoidMessage(nint receiver, nint selector, nint argument);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void InsertText(nint receiver, nint selector, nint argument, NSRange replacement);
}
