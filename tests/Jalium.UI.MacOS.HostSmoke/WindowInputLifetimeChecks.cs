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
            Child = new TextBlock { Text = "滚轮与笔输入测试区域", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
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
        var area = new Canvas { Height = 320 }; var anchor = new Button { Content = "输入区域", Width = 100, Height = 40 };
        area.Children.Add(anchor);
        if (kind == "owner") { area.Children.Remove(anchor); area.Children.Add(subject); Canvas.SetLeft(subject, 5); Canvas.SetTop(subject, 5); }
        var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "窗口、弹窗与输入有效期", FontSize = 22 }); root.Children.Add(area);
        using var owner = new ProbeWindow { Title = "Jalium Window · 输入有效期", Width = kind == "owner" ? 760 : 420,
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
        int completed = 0; bool running = false; var ready = System.Diagnostics.Stopwatch.StartNew();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        void Attempt(string name, Action action)
        {
            int first = checks.Count; Exception? error = null;
            try { action(); completed++; } catch (Exception failure) { error = failure; failures.Add(name + ": " + failure); }
            scenarios.Add(new { name, passed = error == null, checks = checks.Skip(first).ToArray(), error = error?.ToString() });
        }
        void Finish()
        {
                popup?.SetCurrentValue(Popup.IsOpenProperty, false); owner.Close();
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    index, custom, kind, software, completed, expected = 5, passed = failures.Count == 0,
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
                Require(editor.Focus(), "editor can regain logical focus after nested input");
                editor.Select(2, 3); Require(editor.Text == "重入验证 · 中文🙂" && Keyboard.FocusedElement == editor && editor.SelectionStart == 2 && editor.SelectionLength == 3,
                    "Chinese text, emoji and selection remain editable");
                Require(view.Window?.FirstResponder?.Handle == view.Handle, "native content responder remains ready for editing");
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
