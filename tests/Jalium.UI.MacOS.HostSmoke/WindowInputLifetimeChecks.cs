using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ObjCRuntime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowInputLifetimeChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendTablet(nint view, int kind, uint device, int tool, int entering, uint buttons,
        float pressure, float tiltX, float tiltY, float rotation, double x, double y, double time, int button, int clicks);

    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 12) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_INPUT_LIFETIME_OUTPUT")!;
        string bridge = Environment.GetEnvironmentVariable("JALIUM_GESTURE_TEST_BRIDGE")!;
        Directory.CreateDirectory(output);
        nint library = NativeLibrary.Load(bridge);
        var send = Marshal.GetDelegateForFunctionPointer<SendTablet>(NativeLibrary.GetExport(library, "jalium_test_apple_tablet_send"));
        bool custom = index % 2 != 0, software = index >= 6;
        string kind = new[] { "owner", "overlay", "native" }[index % 6 / 2];
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new LifetimeDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var context = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (!software) context.DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var editor = new TextBox { Text = "重入验证 · 中文🙂", Height = 42 };
        AutomationProperties.SetName(editor, "输入后继续编辑");
        var target = new Border { Height = 110, Focusable = true, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Child = new TextBlock { Text = "滚轮与笔输入 · Miii 中文🙂", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        AutomationProperties.SetName(target, "滚轮与笔输入测试区域");
        target.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        target.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = "窗口输入重入", FontSize = 18 });
        content.Children.Add(editor); content.Children.Add(target);
        content.Children.Add(new TextBlock { Text = "旧输入停止后，新输入与编辑仍应可用。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        var subject = new Border { Width = 360, Height = 300, Child = content,
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        subject.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        subject.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        var area = new Canvas { Height = 360 }; var anchor = new Button { Content = "输入区域", Width = 100, Height = 40 };
        area.Children.Add(anchor);
        if (kind == "owner") { area.Children.Remove(anchor); area.Children.Add(subject); Canvas.SetLeft(subject, 5); Canvas.SetTop(subject, 5); }
        var successor = new Border { Width = 120, Height = 40, Focusable = true, CornerRadius = new CornerRadius(6),
            Child = new TextBlock { Text = "后继输入", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        successor.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        AutomationProperties.SetName(successor, "后继输入区域");
        area.Children.Add(successor); Canvas.SetLeft(successor, 5); Canvas.SetTop(successor, 320);
        var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "窗口、弹窗与输入有效期", FontSize = 22 }); root.Children.Add(area);
        using var owner = new ProbeWindow { Title = "Jalium Window v298 · 输入有效期", Width = kind == "owner" ? 760 : 420,
            Height = 520, MinWidth = 420, MinHeight = 500,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Application.Current!.MainWindow = owner; owner.Show(); owner.UpdateLayout();
        Popup? popup = null;
        if (kind != "owner")
        {
            Canvas.SetLeft(anchor, kind == "native" ? area.ActualWidth - 20 : 5); Canvas.SetTop(anchor, 5);
            popup = new Popup { Child = subject, PlacementTarget = anchor, Placement = PlacementMode.Right,
                PreferExternalWindow = kind == "native", ShouldConstrainToRootBounds = kind == "overlay", StaysOpen = true };
            popup.IsOpen = true; owner.UpdateLayout();
        }
        var checks = new List<string>(); var traces = new List<object>(); var scenarios = new List<object>(); var failures = new List<string>();
        const int expected = 75; var focus = new List<object>(); int unavailable = 0;
        int completed = 0; bool running = false; var ready = System.Diagnostics.Stopwatch.StartNew();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        void Observe(UIElement element, bool bring = true)
        {
            var surface = Window.GetMacOSPhysicalWindow(element)!; var view = Runtime.GetNSObject<NSView>(surface.Handle)!;
            if (bring)
            {
                view.Window!.MakeKeyAndOrderFront(null); app.ActivateIgnoringOtherApps(true); view.Window.MakeFirstResponder(view);
                element.Focus(); owner.UpdateLayout(); surface.UpdateLayout(); NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            }
            int frontmost = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1;
            bool active = app.Active && frontmost == Environment.ProcessId && view.Window!.IsKeyWindow && view.Window.FirstResponder?.Handle == view.Handle;
            focus.Add(new { active, frontmost, processId = Environment.ProcessId, appActive = app.Active,
                nativeKey = view.Window!.IsKeyWindow, responder = view.Window.FirstResponder?.Handle.ToString(), ownResponder = view.Handle.ToString() });
            if (!active) unavailable++;
            Require(active && ReferenceEquals(Keyboard.FocusedElement, element), "own key responder and logical focus agree with the system foreground");
        }
        void Attempt(string name, Action action)
        {
            int first = checks.Count; Exception? error = null;
            try { Observe(target); action(); Observe(target); completed++; } catch (Exception failure) { error = failure; failures.Add(name + ": " + failure); }
            scenarios.Add(new { name, passed = error == null, checks = checks.Skip(first).ToArray(), error = error?.ToString() });
        }
        void Finish()
        {
                popup?.SetCurrentValue(Popup.IsOpenProperty, false); owner.Close();
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    index, custom, kind, software, completed, expected, passed = failures.Count == 0 && unavailable == 0 && completed == expected,
                    requireForeground = true, foregroundQualified = unavailable == 0 && focus.Count > 0, focus, foregroundUnavailable = unavailable,
                    renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD"), ownAppKitSamples = true,
                    managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(UIElement).Assembly.Location,
                        typeof(JaliumMacApplication).Assembly.Location, typeof(WindowInputLifetimeChecks).Assembly.Location },
                    checks, failures, scenarios, traces, processId = Environment.ProcessId,
                    physicalInput = false, desktopScreenshot = false, mixedDPIDevice = false
                }));
                app.Stop(app); using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false);
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (running) return;
            var source = Window.GetMacOSPhysicalWindow(target);
            if (source == null || source.Handle == 0 || target.ActualWidth <= 0)
            {
                if (ready.Elapsed.TotalSeconds < 10) return;
                running = true; failures.Add("input host never became ready"); Finish(); return;
            }
            running = true;
            try
            {
                Require(source.IsMacOSPopupHost == (kind == "native"), "expected physical input host is installed");
                if (kind == "owner")
                {
                    var subjectBounds = new Rect(subject.TranslatePoint(default, area), new Size(subject.ActualWidth, subject.ActualHeight));
                    var successorBounds = new Rect(successor.TranslatePoint(default, area), new Size(successor.ActualWidth, successor.ActualHeight));
                    Require(!subjectBounds.IntersectsWith(successorBounds), "owner test targets occupy separate visible hit regions");
                }

                var view = Runtime.GetNSObject<NSView>(source.Handle)!;
                view.Window!.MakeKeyWindow(); Require(view.Window.MakeFirstResponder(view), "own content responder accepts input");
                var at = target.TranslatePoint(new Point(40, 40), source);
                foreach (bool momentum in new[] { false, true }) Attempt(momentum ? "wheel-momentum-cancel" : "wheel-end", () =>
                {
                    int later = 0, bubbles = 0, inner = 0, pointers = 0;
                    using var nested = new Wheel { At = view.ConvertPointToView(new CGPoint(at.X, at.Y), null), Vertical = 0,
                        Direct = momentum ? NSEventPhase.None : NSEventPhase.Ended,
                        Momentum = momentum ? NSEventPhase.Cancelled : NSEventPhase.None };
                    MouseWheelEventHandler first = (_, e) => { if (Math.Abs(e.VerticalDelta) > 1) view.ScrollWheel(nested); };
                    MouseWheelEventHandler second = (_, e) => { if (Math.Abs(e.VerticalDelta) > 1) later++; };
                    MouseWheelEventHandler bubble = (_, e) => { if (Math.Abs(e.VerticalDelta) > 1) bubbles++; else inner++; };
                    PointerWheelChangedEventHandler promoted = (_, _) => pointers++;
                    target.PreviewMouseWheel += first; target.PreviewMouseWheel += second; target.MouseWheel += bubble;
                    target.AddHandler(PointerEvents.PointerWheelChangedEvent, promoted);
                    try
                    {
                        using var outer = new Wheel { At = nested.At, Vertical = -11,
                            Direct = momentum ? NSEventPhase.None : NSEventPhase.Changed,
                            Momentum = momentum ? NSEventPhase.Changed : NSEventPhase.None };
                        view.ScrollWheel(outer);
                        Require(inner == 1, "new terminal wheel packet bubbles once");
                        Require(later == 0 && bubbles == 0 && pointers == 1, "old wheel stops before later handlers, bubbling and pointer promotion");
                        var screen = view.Window.ConvertPointToScreen(nested.At);
                        var ownerView = Runtime.GetNSObject<NSView>(owner.Handle)!;
                        var expected = ownerView.ConvertPointFromView(ownerView.Window!.ConvertPointFromScreen(screen), null);
                        Require(Math.Abs(owner.LastWheel.X - expected.X) < .01 && Math.Abs(owner.LastWheel.Y - expected.Y) < .01,
                            "forwarded owner hook matches independent AppKit client conversion");
                    }
                    finally
                    {
                        target.PreviewMouseWheel -= first; target.PreviewMouseWheel -= second; target.MouseWheel -= bubble;
                        target.RemoveHandler(PointerEvents.PointerWheelChangedEvent, promoted);
                    }
                });
                for (int phase = 0; phase < 3; phase++)
                {
                    int currentPhase = phase;
                    Attempt("tablet-replacement-" + phase, () =>
                    {
                        uint device = (uint)(5000 + index * 10 + currentPhase); double time = 100 + currentPhase;
                        void Send(int kind, float pressure = 0, uint buttons = 0, int entering = 1)
                        {
                            time += .02;
                            Require(send(source.Handle, kind, device, 1, entering, buttons, pressure, 0, 0, 0, at.X, at.Y, time, 0, 1) == 0,
                                "own NSView tablet SDK message is dispatched");
                        }
                        Send(0); if (currentPhase != 0) Send(2, .2f, 1);
                        bool nested = false; int later = 0, legacy = 0, replacement = 0; int? outerTimestamp = null;
                        bool Outer(PointerEventArgs e) => currentPhase == 2 || e.GetCurrentPoint(target).Properties.Pressure > .6f;
                        void Replace(PointerEventArgs e) { if (!nested && Outer(e)) { outerTimestamp = e.Timestamp; nested = true; Send(currentPhase == 2 ? 2 : 3, .35f, 1); } }
                        Delegate first = currentPhase switch
                        {
                            0 => new PointerDownEventHandler((_, e) => Replace(e)),
                            1 => new PointerMoveEventHandler((_, e) => Replace(e)),
                            _ => new PointerUpEventHandler((_, e) => Replace(e))
                        };
                        Delegate second = currentPhase switch
                        {
                            0 => new PointerDownEventHandler((_, e) => { if (e.Timestamp == outerTimestamp) later++; }),
                            1 => new PointerMoveEventHandler((_, e) => { if (e.Timestamp == outerTimestamp) later++; }),
                            _ => new PointerUpEventHandler((_, e) => { if (e.Timestamp == outerTimestamp) later++; })
                        };
                        RoutedEvent preview = currentPhase == 0 ? UIElement.PreviewPointerDownEvent : currentPhase == 1 ? UIElement.PreviewPointerMoveEvent : UIElement.PreviewPointerUpEvent;
                        PointerPressedEventHandler pressed = (_, e) => { if (e.Timestamp == outerTimestamp) legacy++; else replacement++; };
                        PointerMovedEventHandler moved = (_, e) => { if (e.Timestamp == outerTimestamp) legacy++; else replacement++; };
                        PointerReleasedEventHandler released = (_, _) => legacy++;
                        target.AddHandler(preview, first); target.AddHandler(preview, second);
                        target.AddHandler(UIElement.PointerPressedEvent, pressed); target.AddHandler(UIElement.PointerMovedEvent, moved); target.AddHandler(UIElement.PointerReleasedEvent, released);
                        try
                        {
                            Send(currentPhase == 0 ? 2 : currentPhase == 1 ? 3 : 4, currentPhase == 2 ? 0 : .65f, currentPhase == 2 ? 0u : 1u);
                            traces.Add(new { phase = currentPhase, nested, later, legacy, replacement, outerTimestamp });
                            Require(nested && replacement == 1, "replacement pen packet completes its own legacy event once");
                            Require(later == 0 && legacy == 0, "retired pen packet stops inside pointer promotion");
                        }
                        finally
                        {
                            target.RemoveHandler(preview, first); target.RemoveHandler(preview, second);
                            target.RemoveHandler(UIElement.PointerPressedEvent, pressed); target.RemoveHandler(UIElement.PointerMovedEvent, moved); target.RemoveHandler(UIElement.PointerReleasedEvent, released);
                            Send(4); Send(0, entering: 0);
                        }
                    });
                }

                // Mouse packets enter through the own NSView, never the HID queue.
                void MousePacket(UIElement element, NSEventType type)
                {
                    var surface = Window.GetMacOSPhysicalWindow(element)!; var own = Runtime.GetNSObject<NSView>(surface.Handle)!;
                    Point local = element.TranslatePoint(new Point(5, 5), surface);
                    using var sample = NSEvent.MouseEvent(type, own.ConvertPointToView(new CGPoint(local.X, local.Y), null), 0,
                        NSProcessInfo.ProcessInfo.SystemUptime, own.Window!.WindowNumber, null, 1, 1, type == NSEventType.LeftMouseUp ? 0 : 1)!;
                    if (type == NSEventType.LeftMouseDown) own.MouseDown(sample);
                    else if (type == NSEventType.LeftMouseUp) own.MouseUp(sample);
                    else own.MouseMoved(sample);
                }
                foreach (bool bubble in new[] { false, true })
                foreach (string mutation in new[] { "move", "wheel", "release" }) Attempt("mouse-down-" + bubble + "-" + mutation, () =>
                {
                    bool entered = false, injecting = false; int later = 0, ups = 0;
                    MouseButtonEventHandler first = (_, _) =>
                    {
                        if (entered) return; entered = true; injecting = true;
                        try
                        {
                            if (mutation == "wheel")
                            {
                                using var wheel = new Wheel { At = view.ConvertPointToView(new CGPoint(at.X, at.Y), null), Vertical = 1, Direct = NSEventPhase.Changed };
                                view.ScrollWheel(wheel);
                            }
                            else MousePacket(target, mutation == "move" ? NSEventType.MouseMoved : NSEventType.LeftMouseUp);
                        }
                        finally { injecting = false; }
                    };
                    MouseButtonEventHandler second = (_, _) => { if (!injecting) later++; };
                    PointerUpEventHandler up = (_, _) => ups++;
                    RoutedEvent route = bubble ? UIElement.MouseDownEvent : UIElement.PreviewMouseDownEvent;
                    target.AddHandler(route, first); target.AddHandler(route, second); target.AddHandler(UIElement.PointerUpEvent, up);
                    try
                    {
                        MousePacket(target, NSEventType.LeftMouseDown);
                        Require(entered && later == 0, "retired mouse down stops inside its route");
                        Require(target.GetValue(UIElement.IsPressedProperty) is false, "retired mouse down clears visual pressed state");
                        if (mutation != "release") MousePacket(target, NSEventType.LeftMouseUp);
                        Require(ups == 0, "a press that never reached PointerDown cannot emit PointerUp");
                    }
                    finally
                    {
                        target.RemoveHandler(route, first); target.RemoveHandler(route, second); target.RemoveHandler(UIElement.PointerUpEvent, up);
                        MousePacket(target, NSEventType.LeftMouseUp);
                    }
                });
                foreach (bool bubble in new[] { false, true })
                foreach (bool same in new[] { false, true }) Attempt("mouse-successor-" + bubble + "-" + same, () =>
                {
                    UIElement next = same ? target : successor; bool entered = false; int ups = 0;
                    MouseButtonEventHandler replace = (_, _) =>
                    {
                        if (entered) return; entered = true; Observe(next); MousePacket(next, NSEventType.LeftMouseDown);
                        Require(next.CaptureMouse(), "successor press can capture its own element");
                    };
                    PointerUpEventHandler release = (_, _) => ups++;
                    RoutedEvent route = bubble ? UIElement.MouseDownEvent : UIElement.PreviewMouseDownEvent;
                    target.AddHandler(route, replace); next.AddHandler(UIElement.PointerUpEvent, release);
                    try
                    {
                        MousePacket(target, NSEventType.LeftMouseDown);
                        Require(entered && next.GetValue(UIElement.IsPressedProperty) is true && ReferenceEquals(Mouse.Captured, next),
                            "old down unwinding preserves the successor pressed state and capture");
                        Observe(next, false); MousePacket(next, NSEventType.LeftMouseUp);
                        Require(ups == 1 && next.GetValue(UIElement.IsPressedProperty) is false && ReferenceEquals(Mouse.Captured, next),
                            "successor receives one paired release and keeps its explicit capture");
                    }
                    finally
                    {
                        target.RemoveHandler(route, replace); next.RemoveHandler(UIElement.PointerUpEvent, release);
                        next.ReleaseMouseCapture(); Observe(target); MousePacket(target, NSEventType.LeftMouseUp);
                    }
                });
                int scenario = 0;
                foreach (Stage stage in Stages)
                foreach (bool finishReplacement in new[] { false, true }) Attempt("stylus-" + stage.Name + "-" + finishReplacement, () =>
                {
                    uint device = (uint)(12000 + index * 100 + scenario++); double time = 2000 + scenario * 10;
                    bool rangeStage = stage.Name is "enter" || stage.Name.StartsWith("range-") || stage.Name.StartsWith("hover-enter-");
                    bool entered = false; int later = 0, oldPointers = 0, newPackets = 0, newUps = 0, newCancels = 0; int? outer = null, oldId = null, newId = null;
                    void Send(int packetKind, float pressure = 0, uint buttons = 0, int entering = 1, UIElement? element = null)
                    {
                        time += .02; var point = (element ?? target).TranslatePoint(new Point(5, 5), source);
                        Require(send(source.Handle, packetKind, device, 1, entering, buttons, pressure, 0, 0, 0, point.X, point.Y, time, 0, 1) == 0,
                            "own NSView stylus SDK sample is dispatched");
                    }
                    if (!rangeStage)
                    {
                        Send(0);
                        if (stage.Phase is 1 or 2 or 4 or 5) Send(2, .2f, stage.Name.StartsWith("barrel-up") ? 3u : 1u);
                    }
                    bool Matches(RoutedEventArgs e) => stage.Gesture == null || e is StylusSystemGestureEventArgs gesture && gesture.SystemGesture == stage.Gesture;
                    RoutedEventHandler replace = (_, e) =>
                    {
                        if (entered || !Matches(e)) return; entered = true; outer = ((StylusEventArgs)e).Timestamp; oldId = ((StylusEventArgs)e).StylusDevice!.Id;
                        // A physical range exit retires its contact ID. Re-enter
                        // proximity before the next physical down can be accepted.
                        if (stage.Phase == 4) Send(0);
                        Send(2, .35f, 1);
                        if (finishReplacement) { Send(4); Send(0, entering: 0); NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.02)); }
                    };
                    RoutedEventHandler observe = (_, e) => { if (Matches(e) && ((StylusEventArgs)e).Timestamp == outer) later++; };
                    PointerDownEventHandler down = (_, e) => { if (e.Timestamp == outer) oldPointers++; };
                    PointerMoveEventHandler move = (_, e) => { if (e.Timestamp == outer) oldPointers++; };
                    // AppKit derives contact transitions: another left-down
                    // sample during contact is a newer Move, not a second Down.
                    RoutedEvent replacementEvent = stage.Phase == 1 || stage.Phase == 0 && !rangeStage
                        ? UIElement.StylusMoveEvent : UIElement.StylusDownEvent;
                    RoutedEventHandler replacementTrace = (_, e) =>
                    { if (entered && e is StylusEventArgs pen && pen.Timestamp != outer) { newPackets++; newId = pen.StylusDevice!.Id; } };
                    PointerUpEventHandler up = (_, e) =>
                    { if (e.Timestamp == outer) oldPointers++; else if (newId == unchecked((int)e.GetCurrentPoint(target).PointerId)) newUps++; };
                    RoutedEventHandler cancel = (_, e) =>
                    {
                        var pen = (PointerEventArgs)e;
                        if (pen.Timestamp == outer) oldPointers++;
                        else if (newId == unchecked((int)pen.GetCurrentPoint(target).PointerId)) newCancels++;
                    };
                    target.AddHandler(stage.Event, replace); target.AddHandler(stage.Event, observe);
                    target.AddHandler(replacementEvent, replacementTrace);
                    target.AddHandler(UIElement.PointerDownEvent, down); target.AddHandler(UIElement.PointerMoveEvent, move);
                    target.AddHandler(UIElement.PointerUpEvent, up); target.AddHandler(UIElement.PointerCancelEvent, cancel);
                    try
                    {
                        if (rangeStage) { Send(0); Send(1); }
                        else if (stage.Phase == 0) Send(2, .65f, stage.Barrel ? 3u : 1u);
                        else if (stage.Phase == 1) Send(3, .65f, 1);
                        else if (stage.Phase == 2) Send(4);
                        else if (stage.Phase == 4) { Send(0, entering: 0); NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.02)); }
                        else Send(1, element: stage.Phase == 5 ? editor : null);
                        traces.Add(new { stage = stage.Name, finishReplacement, entered, later, oldPointers, outer, oldId, newId, newPackets, newUps, newCancels, replacementEvent = replacementEvent.Name });
                        Require(entered, "stylus stage was exercised: " + stage.Name);
                        Require(newPackets == 1 && newId.HasValue && oldId.HasValue, "replacement native pen packet reaches its own stylus route exactly once");
                        if (stage.Phase == 4)
                        {
                            Require(newId != oldId && later == 1 && oldPointers == 1 && newCancels == 0,
                                "distinct native range IDs complete old cancellation without canceling the new pen");
                            if (!finishReplacement)
                            {
                                Require(target.GetValue(UIElement.IsPressedProperty) is true, "new range keeps its compatibility mouse press");
                                Send(4);
                            }
                            Require(newUps == 1 && newCancels == 0, "new range receives one paired release");
                        }
                        else Require(newId == oldId && later == 0 && oldPointers == 0,
                            "same native pointer ID retires the old stylus route before later handlers and promotion");
                    }
                    finally
                    {
                        target.RemoveHandler(stage.Event, replace); target.RemoveHandler(stage.Event, observe);
                        target.RemoveHandler(replacementEvent, replacementTrace);
                        target.RemoveHandler(UIElement.PointerDownEvent, down); target.RemoveHandler(UIElement.PointerMoveEvent, move);
                        target.RemoveHandler(UIElement.PointerUpEvent, up); target.RemoveHandler(UIElement.PointerCancelEvent, cancel);
                        Send(4); Send(0, entering: 0); NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
                    }
                });
                Observe(editor);
                Require(editor.Focus(), "editor can regain logical focus after nested input");
                editor.Select(2, 3); Require(editor.Text == "重入验证 · 中文🙂" && Keyboard.FocusedElement == editor && editor.SelectionStart == 2 && editor.SelectionLength == 3,
                    "Chinese text, emoji and selection remain editable");
                Require(view.Window?.FirstResponder?.Handle == view.Handle, "native content responder remains ready for editing");
                using (var insertion = new NSString("验收🙂"))
                    InsertText(view.Handle, new ObjCRuntime.Selector("insertText:replacementRange:").Handle, insertion.Handle, new NSRange(nint.MaxValue, 0));
                Require(editor.Text == "重入验收🙂· 中文🙂" && editor.SelectionStart == 6 && editor.SelectionLength == 0,
                    "native NSTextInputClient insertion still replaces the selected Chinese text and emoji");
                editor.Undo(); Require(editor.Text == "重入验证 · 中文🙂", "native insertion retains editable undo history");
                owner.UpdateLayout(); source.UpdateLayout();

                var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(source.ActualWidth * source.DpiScale),
                    (int)Math.Ceiling(source.ActualHeight * source.DpiScale), source.DpiScale,
                    drawing => { drawing.DrawRectangle(source.Background, null, new Rect(0, 0, source.ActualWidth, source.ActualHeight)); source.Render(drawing); }, () => source.Handle != 0);
                Require(bitmap != null, "actual rendering backend provides a readback image");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap!));
                using (var file = File.Create(Path.Combine(output, "input.png"))) encoder.Save(file);
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                Finish();
            }
        });
        try { app.Run(); return failures.Count == 0 ? 0 : 1; }
        finally { timer.Invalidate(); owner.Close(); using var done = NSNotification.FromName(NSApplication.WillTerminateNotification, app); host.WillTerminate(done); app.Delegate = null!; }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void InsertText(nint receiver, nint selector, nint argument, NSRange replacement);

    private sealed record Stage(string Name, RoutedEvent Event, int Phase, SystemGesture? Gesture = null, bool Barrel = false);
    private static readonly Stage[] Stages =
    [
        new("enter", UIElement.StylusEnterEvent, 0),
        new("range-preview", UIElement.PreviewStylusInRangeEvent, 0), new("range-bubble", UIElement.StylusInRangeEvent, 0),
        new("hover-enter-preview", UIElement.PreviewStylusSystemGestureEvent, 0, SystemGesture.HoverEnter),
        new("hover-enter-bubble", UIElement.StylusSystemGestureEvent, 0, SystemGesture.HoverEnter),
        new("barrel-down-preview", UIElement.PreviewStylusButtonDownEvent, 0, Barrel: true),
        new("barrel-down-bubble", UIElement.StylusButtonDownEvent, 0, Barrel: true),
        new("tap-preview", UIElement.PreviewStylusSystemGestureEvent, 0, SystemGesture.Tap),
        new("tap-bubble", UIElement.StylusSystemGestureEvent, 0, SystemGesture.Tap),
        new("hold-enter-preview", UIElement.PreviewStylusSystemGestureEvent, 0, SystemGesture.HoldEnter),
        new("hold-enter-bubble", UIElement.StylusSystemGestureEvent, 0, SystemGesture.HoldEnter),
        new("down-preview", UIElement.PreviewStylusDownEvent, 0), new("down-bubble", UIElement.StylusDownEvent, 0),
        new("barrel-up-preview", UIElement.PreviewStylusButtonUpEvent, 1), new("barrel-up-bubble", UIElement.StylusButtonUpEvent, 1),
        new("drag-preview", UIElement.PreviewStylusSystemGestureEvent, 1, SystemGesture.Drag),
        new("drag-bubble", UIElement.StylusSystemGestureEvent, 1, SystemGesture.Drag),
        new("move-preview", UIElement.PreviewStylusMoveEvent, 1), new("move-bubble", UIElement.StylusMoveEvent, 1),
        new("hold-leave-preview", UIElement.PreviewStylusSystemGestureEvent, 2, SystemGesture.HoldLeave),
        new("hold-leave-bubble", UIElement.StylusSystemGestureEvent, 2, SystemGesture.HoldLeave),
        new("up-preview", UIElement.PreviewStylusUpEvent, 2), new("up-bubble", UIElement.StylusUpEvent, 2),
        new("air-preview", UIElement.PreviewStylusInAirMoveEvent, 3), new("air-bubble", UIElement.StylusInAirMoveEvent, 3),
        new("leave", UIElement.StylusLeaveEvent, 5),
        new("out-preview", UIElement.PreviewStylusOutOfRangeEvent, 4), new("out-bubble", UIElement.StylusOutOfRangeEvent, 4),
        new("hover-leave-preview", UIElement.PreviewStylusSystemGestureEvent, 4, SystemGesture.HoverLeave),
        new("hover-leave-bubble", UIElement.StylusSystemGestureEvent, 4, SystemGesture.HoverLeave)
    ];

    private sealed class ProbeWindow : Window, IDisposable
    {
        internal Point LastWheel;
        protected override bool OnPreviewWindowMouseWheel(int delta, Point point) { LastWheel = point; return false; }
        public void Dispose() => Close();
    }
    [Register("JaliumWindowInputLifetimeWheelV278")]
    private sealed class Wheel : NSEvent
    {
        internal CGPoint At; internal double Vertical; internal NSEventPhase Direct, Momentum;
        public override NSEventType Type => NSEventType.ScrollWheel;
        public override CGPoint LocationInWindow => At;
        public override NSEventModifierMask ModifierFlags => 0;
        public override NFloat ScrollingDeltaX => 0;
        public override NFloat ScrollingDeltaY => (NFloat)Vertical;
        public override bool HasPreciseScrollingDeltas => true;
        public override NSEventPhase Phase => Direct;
        public override NSEventPhase MomentumPhase => Momentum;
    }
    private sealed class LifetimeDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}
