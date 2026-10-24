using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowMouseReleaseOwnershipChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendMouse(nint view, int kind, int button, uint buttons, double x, double y, double time);

    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 12) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_MOUSE_RELEASE_OUTPUT")!;
        Directory.CreateDirectory(output);
        nint library = NativeLibrary.Load(Environment.GetEnvironmentVariable("JALIUM_MOUSE_RELEASE_BRIDGE")!);
        var send = Marshal.GetDelegateForFunctionPointer<SendMouse>(NativeLibrary.GetExport(library, "jalium_test_mouse_release_send"));
        bool custom = index % 2 != 0, software = index >= 6;
        string kind = new[] { "owner", "overlay", "native" }[index % 6 / 2];
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var context = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (!software) context.DefaultRenderingEngine = RenderingEngine.Impeller;
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        Border Box(string text)
        {
            var label = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border { Width = 140, Height = 54, Focusable = true, CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1), Child = label };
            box.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            box.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            return box;
        }
        var target = Box("继续拖拽 · 中文🙂"); var same = Box("新的拖拽区域");
        var oldParent = new Canvas { Width = 160, Height = 76 }; oldParent.Children.Add(target);
        var nextParent = new Canvas { Width = 160, Height = 76 };
        var subject = new Canvas { Width = 380, Height = 230 };
        subject.Children.Add(oldParent); Canvas.SetLeft(oldParent, 10); Canvas.SetTop(oldParent, 20);
        subject.Children.Add(nextParent); Canvas.SetLeft(nextParent, 200); Canvas.SetTop(nextParent, 20);
        subject.Children.Add(same); Canvas.SetLeft(same, 10); Canvas.SetTop(same, 130);
        var area = new Canvas { Height = 280 }; var anchor = new Button { Content = "输入区域", Width = 100, Height = 40 };
        area.Children.Add(anchor);
        if (kind == "owner") { area.Children.Remove(anchor); area.Children.Add(subject); }
        var status = new TextBlock { Text = "正在检查鼠标按下与松开的归属", TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "多键拖拽与窗口输入", FontSize = 22 }); body.Children.Add(area); body.Children.Add(status);
        var owner = new Window { Title = "Jalium Window v307 · 鼠标会话", Width = kind == "owner" ? 760 : 440, Height = 430,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = body };
        Application.Current!.MainWindow = owner; owner.Show(); owner.UpdateLayout();
        Popup? popup = null;
        if (kind != "owner")
        {
            Canvas.SetLeft(anchor, kind == "native" ? area.ActualWidth - 20 : 5); Canvas.SetTop(anchor, 5);
            popup = new Popup { Child = subject, PlacementTarget = anchor, Placement = PlacementMode.Right, StaysOpen = true,
                PreferExternalWindow = kind == "native", ShouldConstrainToRootBounds = kind == "overlay" };
            popup.IsOpen = true; owner.UpdateLayout();
        }
        var scenarios = new List<object>(); var failures = new List<string>(); var observations = new List<object>();
        int completed = 0; double timestamp = 100;
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Window Surface(UIElement element) => Window.GetMacOSPhysicalWindow(element)!;
        WindowInputDispatcher Input() => (WindowInputDispatcher)typeof(Window).GetField("_inputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Surface(target))!;
        IDictionary Slots(string name) => (IDictionary)typeof(WindowInputDispatcher).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Input())!;
        void Contact(UIElement element)
        {
            Require(ReferenceEquals(Slots("_activePointerTargets")[(uint)1], element), "current press retains its pointer target");
            Require(Slots("_lastPointerPoints").Contains((uint)1), "current press retains its pointer sample");
        }
        void Packet(UIElement element, int eventKind, int button, uint buttons)
        {
            var surface = Surface(element); var at = element.TranslatePoint(new Point(8, 8), surface);
            Require(send(surface.Handle, eventKind, button, buttons, at.X, at.Y, timestamp += .02) == 0,
                "own NSView accepts the SDK message and restores the button query");
        }
        void Attempt(string name, Action action)
        {
            string? error = null;
            try { action(); completed++; } catch (Exception failure) { error = failure.ToString(); failures.Add(name + ": " + error); }
            finally
            {
                UIElement.MouseCapturedElement?.ReleaseMouseCapture(); Input().CleanupPointerSession(1);
                Mouse.UpdateState(Mouse.Position, null, MouseButtonStates.AllReleased);
                if (target.VisualParent is Panel parent) parent.Children.Remove(target);
                oldParent.Children.Add(target); owner.UpdateLayout(); Surface(target).UpdateLayout();
            }
            scenarios.Add(new { name, passed = error == null, error });
        }
        var ready = System.Diagnostics.Stopwatch.StartNew(); bool ran = false, finished = false;
        using var timer = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (finished) return;
            if (ran)
            {
                int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MOUSE_RELEASE_HOLD_SECONDS"), out int hold);
                if (ready.Elapsed.TotalSeconds < Math.Clamp(hold, 0, 60)) return;
                finished = true; popup?.SetCurrentValue(Popup.IsOpenProperty, false); owner.Close(); app.Stop(app);
                using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false); return;
            }
            if (ready.Elapsed.TotalSeconds < .3 || target.ActualWidth <= 0 || Surface(target)?.Handle == 0) return;
            ran = true;
            var surface = Surface(target); var view = Runtime.GetNSObject<NSView>(surface.Handle)!;
            observations.Add(new { appActive = app.Active, frontmost = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                processId = Environment.ProcessId, nativeKey = view.Window!.IsKeyWindow, actualExternalHost = surface.IsMacOSPopupHost,
                dpiScale = surface.DpiScale });
            if (surface.IsMacOSPopupHost != (kind == "native")) failures.Add("unexpected physical popup host");
            for (int oldButton = 1; oldButton <= 4; oldButton++)
            for (int nextButton = 0; nextButton <= 4; nextButton++)
            foreach (bool sameElement in new[] { false, true })
            {
                if (oldButton == nextButton) continue;
                int old = oldButton, nextKey = nextButton;
                Attempt($"orphan-{old}-{nextKey}-{sameElement}", () =>
                {
                    bool retired = false; int ups = 0; var next = sameElement ? target : same;
                    MouseButtonEventHandler retire = (_, e) =>
                    {
                        if (retired || e.ChangedButton != MouseButton.Left) return; retired = true;
                        oldParent.Children.Remove(target); nextParent.Children.Add(target); owner.UpdateLayout(); surface.UpdateLayout();
                    };
                    RoutedEventHandler up = (_, _) => ups++;
                    target.MouseUp += retire;
                    try
                    {
                        Packet(target, 0, 0, 1); Packet(target, 0, old, 1u | 1u << old); Packet(target, 1, 0, 1u << old);
                        Require(retired && !Slots("_activePointerTargets").Contains((uint)1), "retired route clears its contact");
                        next.PointerUp += up;
                        Packet(next, 0, nextKey, (1u << old) | (1u << nextKey)); Require(next.CaptureMouse(), "new press captures its target"); Contact(next);
                        Packet(next, 1, old, 1u << nextKey); Contact(next);
                        Require(ups == 0 && ReferenceEquals(Mouse.Captured, next), "orphan release preserves capture without PointerUp");
                        Packet(next, 1, nextKey, 0);
                        Require(ups == 1 && !Slots("_activePointerTargets").Contains((uint)1), "new press receives its own terminal event once");
                    }
                    finally { target.MouseUp -= retire; next.PointerUp -= up; }
                });
            }
            Attempt("handled-additional-button", () =>
            {
                MouseButtonEventHandler handled = (_, e) => { if (e.ChangedButton == MouseButton.Right) e.Handled = true; };
                target.MouseDown += handled;
                try { Packet(target, 0, 0, 1); Packet(target, 0, 1, 3); Packet(target, 2, 0, 3); Packet(target, 1, 1, 1); Contact(target); Packet(target, 1, 0, 0); }
                finally { target.MouseDown -= handled; }
            });
            Attempt("canceled-own-contact", () =>
            {
                MouseButtonEventHandler canceled = (_, e) => e.Cancel = true; target.MouseDown += canceled;
                try
                {
                    Packet(target, 0, 0, 1); Packet(target, 2, 0, 1); Packet(target, 1, 0, 0);
                    Require(!Slots("_activePointerTargets").Contains((uint)1) && !Slots("_lastPointerPoints").Contains((uint)1), "canceled own contact is cleaned");
                }
                finally { target.MouseDown -= canceled; }
            });
            Attempt("handled-hover-after-contact", () =>
            {
                Packet(target, 0, 0, 1); Packet(target, 1, 0, 0);
                MouseButtonEventHandler handled = (_, e) => e.Handled = true; target.MouseDown += handled;
                try
                {
                    Packet(target, 0, 0, 1); Packet(target, 2, 0, 1); Packet(target, 1, 0, 0);
                    Require(!Slots("_activePointerTargets").Contains((uint)1) && !Slots("_lastPointerPoints").Contains((uint)1), "handled orphan hover is cleaned");
                }
                finally { target.MouseDown -= handled; }
            });
            status.Text = $"{completed}/35 项通过 · 旧松开事件保留新的拖拽会话"; owner.UpdateLayout();
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, custom, software, kind, completed, expected = 35, passed = completed == 35 && failures.Count == 0,
                nativeSDK = true, physicalInput = false, foregroundRequired = false, observations, scenarios, failures,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(UIElement).Assembly.Location,
                    typeof(WindowMouseReleaseOwnershipChecks).Assembly.Location }, processId = Environment.ProcessId
            }));
            ready.Restart();
        });
        app.Run(); NativeLibrary.Free(library);
        return completed == 35 && failures.Count == 0 ? 0 : 1;
    }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}
