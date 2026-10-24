using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Test-only SDK messages target this process's own NSView; no HID events.
internal static class WindowGestureInterleaveChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendSample(nint view, int boundary, int kind, int phase,
        double magnification, double rotation, double x, double y, double timestamp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConvertPoint(nint source, nint destination, double x, double y, out double outX, out double outY);

    internal static int Run(string argument)
    {
        int index = int.Parse(argument.Split('=').Last()); if ((uint)index >= 48) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_GESTURE_OUTPUT") ?? throw new InvalidOperationException("Missing owned output.");
        Directory.CreateDirectory(output);
        nint helper = NativeLibrary.Load(Environment.GetEnvironmentVariable("JALIUM_GESTURE_TEST_BRIDGE") ?? throw new InvalidOperationException("Missing test-only SDK bridge."));
        var send = Marshal.GetDelegateForFunctionPointer<SendSample>(NativeLibrary.GetExport(helper, "jalium_test_apple_gesture_send"));
        var convert = Marshal.GetDelegateForFunctionPointer<ConvertPoint>(NativeLibrary.GetExport(helper, "jalium_test_apple_gesture_convert_point"));
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new GestureDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(note);
        app.BeginInvokeOnMainThread(() =>
        {
            var scene = new Scene(index, output, send, convert); Exception? error = null;
            try { scene.Execute(); } catch (Exception failure) { error = failure; }
            finally { try { scene.Cleanup(host); } catch (Exception failure) { error ??= failure; } }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { index, passed = error == null, failure = error?.ToString(), checks = scene.Checks, observed = scene.Observed,
                physicalInput = false, desktopScreenshot = false, offscreenRendering = true, pid = Environment.ProcessId }));
            host.AllowQuit = true; app.Terminate(app);
        });
        app.Run(); return 0;
    }

    private sealed class Scene(int index, string output, SendSample send, ConvertPoint convert)
    {
        private int Profile => index % 4;
        private int Configuration => index / 4;
        private bool Custom => Configuration % 6 >= 3;
        private int Placement => Configuration % 3;
        private RenderBackend Backend => Configuration < 6 ? RenderBackend.Metal : RenderBackend.Software;
        private Window? _owner, _source;
        private Popup? _popup;
        private Border _target = null!;
        private Canvas _canvas = null!;
        private TextBox _editor = null!;
        private readonly List<ManipulationDeltaEventArgs> _deltas = [];
        private readonly List<ManipulationCompletedEventArgs> _ends = [];
        private int _starting, _started;
        private Point _expectedPacketOrigin;
        private bool _allOriginsMatch = true;
        private readonly List<object> _originSamples = [];
        private double _scale = 1, _rotation, _initialAngular = double.NaN;
        internal List<string> Checks { get; } = [];
        internal Dictionary<string, object?> Observed { get; } = [];
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private object? Session => _source == null ? null : typeof(Window).GetField("_macOSManipulation", Private)!.GetValue(_source);
        private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window ?? throw new InvalidOperationException("No native view.");

        internal void Execute()
        {
            var context = RenderContext.GetOrCreateCurrent(Backend); context.DefaultRenderingEngine = RenderingEngine.Impeller;
            Require(context.Backend == Backend, "actual backend matches configuration");
            _canvas = new Canvas { Height = 210, ClipToBounds = true };
            _target = new Border { Width = 180, Height = 100, IsManipulationEnabled = true, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(45, 110, 165)), RenderTransformOrigin = new Point(.5, .5),
                Child = new TextBlock { Text = "缩放与旋转 · 中文🙂", FontSize = 18, Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            AutomationProperties.SetName(_target, "缩放与旋转测试区域");
            if (Profile == 1) Manipulation.SetManipulationMode(_target, ManipulationModes.Scale);
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
            TextBlock Text(string text, double size)
            { var value = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; value.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush"); return value; }
            panel.Children.Add(Text("组合手势检查", 24));
            panel.Children.Add(Text(Profile switch { 0 => "仅旋转：先缩放，再旋转。", 1 => "仅缩放：先旋转，再缩放。", 2 => "交错输入后，旋转惯性继续。", _ => "缩放不改变旋转采样间隔。" }, 15));
            panel.Children.Add(_canvas); _editor = new TextBox { Text = "中文🙂，手势后继续编辑", Height = 40 }; panel.Children.Add(_editor); AutomationProperties.SetName(_editor, "手势后继续编辑");
            var action = new Button { Content = "继续编辑", Height = 44 }; action.Click += (_, _) => _editor.Focus(); panel.Children.Add(action);
            _owner = new Window { Title = "Jalium 组合手势 · " + Profile, Width = Custom ? 420 : 760, Height = 520,
                MinWidth = 400, MinHeight = 500, TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
                Content = panel };
            _owner.SetResourceReference(Window.BackgroundProperty, "WindowBackground");
            var anchor = new Border { Width = 80, Height = 32, Child = Text("手势区域", 15) }; _canvas.Children.Add(anchor);
            if (Placement == 0) { _canvas.Children.Add(_target); Canvas.SetLeft(_target, 50); Canvas.SetTop(_target, 50); }
            _owner.Show(); _owner.UpdateLayout();
            Canvas.SetLeft(anchor, Placement == 2 ? Math.Max(0, _canvas.ActualWidth - 45) : 10); Canvas.SetTop(anchor, 5); _owner.UpdateLayout();
            if (Placement != 0)
            {
                var holder = new Canvas { Width = 280, Height = 210, ClipToBounds = true }; holder.Children.Add(_target);
                Canvas.SetLeft(_target, 50); Canvas.SetTop(_target, 50);
                _popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Right, Child = holder, StaysOpen = true,
                    ShouldConstrainToRootBounds = Placement == 1, PreferExternalWindow = Placement == 2 }; _popup.IsOpen = true;
            }
            Wait(() => (_source = Window.GetMacOSPhysicalWindow(_target)) is { Handle: not 0 } && _target.ActualWidth > 0, 10);
            Require(_source != null && _source.Handle != 0 && _target.ActualWidth > 0, "actual subject and popup become ready");
            Require(_source!.IsMacOSPopupHost == (Placement == 2), "requested physical host is installed");
            Require(Native(_source).IsVisible, "subject belongs to a visible AppKit window");
            _owner.Activate(); Require(_editor.Focus(), "editor accepts SDK focus"); _editor.Select(2, 2);
            var responder = Native(_owner).FirstResponder!.Handle;
            Observed["profile"] = Profile; Observed["placement"] = Placement; Observed["custom"] = Custom;
            Observed["backend"] = context.Backend.ToString(); Observed["dpi"] = _source.DpiScale;
            Observed["hostPid"] = Environment.ProcessId; Observed["frontPid"] = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier;
            _target.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, e) =>
            { _starting++; var args = (ManipulationStartingEventArgs)e; args.ManipulationContainer = _canvas; if (Profile == 0) args.Mode = ManipulationModes.Rotate; }));
            _target.AddHandler(UIElement.ManipulationStartedEvent, new RoutedEventHandler((_, _) => _started++));
            _target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
            { var args = (ManipulationDeltaEventArgs)e; _deltas.Add(args);
                if (!args.IsInertial)
                {
                    _originSamples.Add(new { expectedX = _expectedPacketOrigin.X, expectedY = _expectedPacketOrigin.Y, actualX = args.ManipulationOrigin.X, actualY = args.ManipulationOrigin.Y });
                    _allOriginsMatch &= ReferenceEquals(args.ManipulationContainer, _canvas) && Math.Abs(args.ManipulationOrigin.X - _expectedPacketOrigin.X) < .01 && Math.Abs(args.ManipulationOrigin.Y - _expectedPacketOrigin.Y) < .01;
                } _scale *= args.DeltaManipulation.Scale.X; _rotation += args.DeltaManipulation.Rotation;
                _target.RenderTransform = new TransformGroup { Children = { new ScaleTransform(_scale, _scale), new RotateTransform(_rotation) } }; }));
            _target.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, e) => _ends.Add((ManipulationCompletedEventArgs)e)));
            _target.AddHandler(UIElement.ManipulationInertiaStartingEvent, new RoutedEventHandler((_, e) =>
                _initialAngular = ((ManipulationInertiaStartingEventArgs)e).InitialVelocities.AngularVelocity));
            Point Point() => _target.TranslatePoint(new(90, 50), _source);
            var point = Point(); Require(convert(_source.Handle, _owner.Handle, point.X, point.Y, out double x, out double y) == 0, "independent AppKit point conversion");
            var expectedOrigin = _owner.TranslatePoint(new(x, y), _canvas);
            Observed["initialOrigin"] = new { expectedOrigin.X, expectedOrigin.Y };
            void Send(int phase, int kind = 1, double magnification = 0, double rotation = 0, double time = 1, int boundary = 0)
            { var p = Point(); Require(convert(_source.Handle, _owner.Handle, p.X, p.Y, out double px, out double py) == 0, "per-packet AppKit point conversion"); _expectedPacketOrigin = _owner.TranslatePoint(new(px, py), _canvas); Require(send(_source.Handle, boundary, kind, phase, magnification, rotation, p.X, p.Y, time) == 0, "own-view SDK message dispatched"); }
            Send(0, kind: 0, boundary: 1);
            if (Profile < 2)
            {
                Send(1, kind: Profile == 0 ? 1 : 2, magnification: Profile == 0 ? .1 : 0, rotation: Profile == 1 ? 5 : 0);
                bool pending = Session != null && !Manipulation.IsManipulationActive(_target) && _started == 0 && _deltas.Count == 0;
                Observed["pendingAfterExcludedKind"] = pending;
                Send(1, kind: Profile == 0 ? 2 : 1, magnification: Profile == 1 ? .2 : 0, rotation: Profile == 0 ? 12 : 0, time: 1.02);
                Observe("enabled-kind"); Require(_started == 1 && _deltas.Count == 1, "enabled kind joins the original claimed sequence");
                Require(pending && _starting == 1, "excluded kind stays pending without duplicate starting events");
                Require(Math.Abs(_scale - (Profile == 0 ? 1 : 1.2)) < 1e-8 && Math.Abs(_rotation - (Profile == 0 ? -12 : 0)) < 1e-8, "only the selected native component changes the control");
                Send(8, kind: 1, time: 1.04); Send(8, kind: 2, time: 1.04);
                Require(Manipulation.IsManipulationActive(_target) && _ends.Count == 0, "generic sequence owns its per-kind end packets");
                Send(0, kind: 0, boundary: 2, time: 1.06);
            }
            else
            {
                Send(1, kind: 2, rotation: Profile == 2 ? 2 : 0);
                if (Profile == 2)
                {
                    Send(4, kind: 2, rotation: 6, time: 1.02); Require(Math.Abs(_deltas.Last().Velocities.AngularVelocity + .3) < 1e-8, "rotation-only samples provide the velocity control");
                    Send(1, magnification: .1, time: 1.03); Observe("retained-angular");
                    Require(Math.Abs(_deltas.Last().Velocities.AngularVelocity + .3) < 1e-8, "magnification preserves the last angular velocity");
                }
                else
                {
                    Send(1, magnification: .1, time: 1.01); Send(4, kind: 2, rotation: 6, time: 1.02); Observe("angular-clock");
                    Require(Math.Abs(_deltas.Last().Velocities.AngularVelocity + .3) < 1e-8, "rotation intervals exclude magnification packets and include neutral begin");
                    Send(4, magnification: .05, time: 1.03); Send(4, kind: 2, rotation: 8, time: 1.06); Observe("second-angular-clock");
                    Require(Math.Abs(_deltas.Last().Velocities.AngularVelocity + .2) < 1e-8, "second angular interval uses its own timestamp");
                    Send(4, magnification: .03, time: 1.07);
                }
                double expected = Profile == 2 ? -.3 : -.2;
                Manipulation.StartInertia(_target); Observe("inertia-start");
                Require(Math.Abs(_initialAngular - expected) < 1e-8, "real inertia receives the retained angular component");
                Wait(() => _deltas.Any(e => e.IsInertial), 3);
                Require(_deltas.Any(e => e.IsInertial && e.DeltaManipulation.Rotation < 0), "dispatcher produces a nonzero angular inertia frame");
                Manipulation.CompleteManipulation(_target); Send(0, kind: 0, boundary: 2, time: 1.08);
            }
            Require(_starting == 1 && _started == 1 && _ends.Count == 1 && !Manipulation.IsManipulationActive(_target) && Session == null, "sequence completes once and releases managed ownership");
            Require(_deltas.All(e => !e.Manipulators.Any()), "indirect input creates no synthetic touch contacts");
            Observed["packetOrigins"] = _originSamples; Observed["packetOriginsMatch"] = _allOriginsMatch; Require(_allOriginsMatch && _originSamples.Count == _deltas.Count(e => !e.IsInertial) && _originSamples.Count > 0, "logical origin agrees with AppKit cross-window conversion");
            Require(ReferenceEquals(Keyboard.FocusedElement, _editor) && Native(_owner).FirstResponder?.Handle == responder && _editor.Text == "中文🙂，手势后继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "SDK gestures retain editor text selection and logical/native focus");
            Observe("complete"); Capture();
        }
        private static void Wait(Func<bool> ready, double seconds)
        { var timer = Stopwatch.StartNew(); while (!ready() && timer.Elapsed.TotalSeconds < seconds) NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.02)); }
        private void Observe(string phase)
        { Observed["phase"] = phase; Observed["starting"] = _starting; Observed["started"] = _started; Observed["deltas"] = _deltas.Count; Observed["completed"] = _ends.Count; Observed["angular"] = _deltas.LastOrDefault()?.Velocities.AngularVelocity; Observed["initialAngular"] = double.IsFinite(_initialAngular) ? _initialAngular : null; Observed["scale"] = _scale; Observed["rotation"] = _rotation; }
        private void Capture()
        {
            if (Profile != 0 && Profile != 3) return;
            _source!.UpdateLayout(); double dpi = _source.DpiScale;
            Brush background = _owner!.Background ?? new SolidColorBrush(Colors.White);
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(_source.ActualWidth * dpi), (int)Math.Ceiling(_source.ActualHeight * dpi), dpi,
                dc => { dc.DrawRectangle(background, null, new Rect(0, 0, _source.ActualWidth, _source.ActualHeight)); _source.Render(dc); }, () => _source.Handle != 0)
                ?? throw new InvalidOperationException("Own backend readback failed.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, "gesture.png")); encoder.Save(file); Require(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "current backend renders the resulting control tree");
        }
        internal void Cleanup(GestureDelegate host)
        {
            if (_popup != null) _popup.IsOpen = false;
            _owner?.Close(); host.Stop();
            Require((_owner == null || _owner.Handle == 0) && (_source == null || _source.Handle == 0) && Application.Current == null, "shutdown releases native windows and application");
        }
        private void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Checks.Add(name); }
    }
    private sealed class GestureDelegate : NSApplicationDelegate
    {
        private bool _started; private JaliumApp? _host;
        internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_started) return; _started = true; _host = AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build().UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }); _host.StartHosted(); }
        internal void Stop() { var host = _host; _host = null; host?.StopHosted(); }
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
        public override void WillTerminate(NSNotification notification) => Stop();
    }
}
