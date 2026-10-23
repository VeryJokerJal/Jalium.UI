#pragma warning disable WPF0001
using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using ObjCRuntime;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowMotionNotificationChecks
{
    private static readonly string[] Names = ["ClientAreaAnimation", "ComboBoxAnimation", "ListBoxSmoothScrolling", "MenuAnimation",
        "MinimizeAnimation", "ToolTipAnimation", "UIEffects", "ComboBoxPopupAnimation", "MenuPopupAnimation", "ToolTipPopupAnimation"];

    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 48) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_MOTION_NOTIFICATION_OUTPUT")!; Directory.CreateDirectory(output);
        using var preference = new WindowMotionPreferenceChecks.PreferenceOverride(index % 2 != 0);
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(Mode(index));
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var scene = new Scene(index, output, preference, host); bool complete = false;
        app.BeginInvokeOnMainThread(() =>
        {
            try { scene.Execute(); } catch (Exception error) { scene.Failures.Add("scene: " + error); }
            complete = true;
        });
        var wait = Stopwatch.StartNew();
        using var timer = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (!complete) return;
            int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MOTION_NOTIFICATION_HOLD_SECONDS"), out int hold);
            if (wait.Elapsed.TotalSeconds < Math.Clamp(hold, 0, 60)) return;
            try { scene.Cleanup(); } catch (Exception error) { scene.Failures.Add("cleanup: " + error); }
            bool restored = preference.Restore(); if (!restored) scene.Failures.Add("process-local getter restoration failed");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, passed = scene.Failures.Count == 0 && scene.Scenarios.Count == 13, scene.Failures, scene.Scenarios, scene.Checks,
                owners = new[] { 0, 1, 3 }[index % 6 / 2], custom = index % 2 != 0, software = index % 12 >= 6,
                mode = Mode(index).ToString(),
                expected = 13, scene.Observed, processId = Environment.ProcessId, appKitPreferenceOverride = true,
                systemPreferencesWritten = false, overrideRestored = restored, foregroundRequired = false, physicalInput = false,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location,
                    typeof(WindowMotionNotificationChecks).Assembly.Location }
            }));
            complete = false; app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false);
        });
        app.Run(); return scene.Failures.Count == 0 && scene.Scenarios.Count == 13 ? 0 : 1;
    }

    private static ThemeMode Mode(int index) => new[] { ThemeMode.None, ThemeMode.System, ThemeMode.Light, ThemeMode.Dark }[index / 12];

    private static void Barrier()
    {
        bool done = false; var app = NSApplication.SharedApplication; var watch = Stopwatch.StartNew();
        app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => done = true)));
        while (!done && watch.Elapsed.TotalSeconds < 5)
        {
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
            typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!.GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        }
        if (!done) throw new TimeoutException("own main-thread notification barrier");
        typeof(Css).Assembly.GetType("Jalium.UI.Styling.CssEvaluationScheduler")!.GetMethod("FlushIfPending", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [Jalium.UI.Threading.Dispatcher.CurrentDispatcher]);
    }

    private sealed class Scene(int index, string output, WindowMotionPreferenceChecks.PreferenceOverride preference, Delegate host)
    {
        private readonly List<Window> _windows = [];
        private readonly List<Popup> _popups = [];
        private readonly List<(StackPanel Root, Border Probe, TextBox Editor)> _subjects = [];
        private readonly List<StackPanel> _roots = [];
        private readonly List<string> _events = [];
        private readonly Dictionary<Window, int> _local = [];
        internal readonly List<object> Scenarios = [];
        internal readonly List<string> Checks = [], Failures = [];
        internal readonly List<object> Observed = [];
        private PropertyChangedEventHandler? _changed;
        private string _phase = "startup";
        private readonly Stopwatch _timeline = Stopwatch.StartNew();
        private RenderBackend Backend => index % 12 >= 6 ? RenderBackend.Software : RenderBackend.Metal;
        private bool Custom => index % 2 != 0;
        private int Owners => new[] { 0, 1, 3 }[index % 6 / 2];
        private static bool Cached => (bool)typeof(MacOSScrollBarSettings).GetProperty("PrefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        private static int Leases => (int)typeof(JaliumMacApplication).Assembly.GetType("Jalium.UI.MacOS.MacOSWindowThemes")!
            .GetField("_leases", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        private void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Checks.Add(message); }
        private void Attempt(string name, Action body)
        {
            _phase = name;
            int first = Checks.Count; string? error = null;
            try { body(); } catch (Exception failure) { error = failure.ToString(); Failures.Add(name + ": " + error); }
            Scenarios.Add(new { name, passed = error == null, checks = Checks.Skip(first).ToArray(), error,
                localNotifications = _local.Select(pair => new { handle = (long)pair.Key.Handle,
                    popup = pair.Key.IsMacOSPopupHost, count = pair.Value }).ToArray() });
        }
        private static void Post()
        {
            using var name = new NSString("NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification");
            NSWorkspace.SharedWorkspace.NotificationCenter.PostNotificationName(name, NSWorkspace.SharedWorkspace);
        }
        private void Reset() { _events.Clear(); foreach (var window in _local.Keys.ToArray()) _local[window] = 0; }
        private void Wave(int waves)
        {
            Require(_events.Count == 10 * waves && Names.All(n => _events.Count(e => e == n) == waves), "one global animation wave per observed preference transition");
        }
        private void Verify()
        {
            Require(Cached == preference.Read(), "cache agrees with the independent process-local AppKit getter");
            foreach (string name in Names.Take(7)) Require(Equals(typeof(SystemParameters).GetProperty(name)!.GetValue(null), !Cached), "public animation policy " + name);
            foreach (var subject in _subjects)
            {
                Require(Equals(subject.Probe.Tag, !Cached), "dynamic system resource follows the latest preference");
                Require(subject.Probe.Opacity == (Cached ? .25 : 1), "existing CSS media query follows the latest preference");
                Require(subject.Editor.Text == "保留中文🙂 Miii" && subject.Editor.SelectionStart == 1 && subject.Editor.SelectionLength == 2,
                    "programmatic Unicode text and selection remain intact");
            }
        }
        private StackPanel Root()
        {
            var root = new StackPanel { Margin = new Thickness(18), Spacing = 12 }; _roots.Add(root);
            root.Children.Add(new TextBlock { Text = "系统动态效果", FontSize = 22 });
            root.Children.Add(new TextBlock { Text = "窗口与弹窗共享一次偏好通知。", TextWrapping = TextWrapping.Wrap });
            var probe = new Border { Height = 18, CornerRadius = new CornerRadius(4) }; Css.SetClass(probe, "motion");
            probe.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            probe.SetResourceReference(FrameworkElement.TagProperty, SystemParameters.ClientAreaAnimationKey); root.Children.Add(probe);
            var editor = new TextBox { Text = "保留中文🙂 Miii", Height = 42 }; editor.Select(1, 2); root.Children.Add(editor);
            Css.SetStyleSheet(root, ".motion { opacity: 1; } @media (prefers-reduced-motion: reduce) { .motion { opacity: .25; } }");
            _subjects.Add((root, probe, editor)); Barrier(); return root;
        }
        private Window Open(bool alternate = false)
        {
            var window = new Window { Title = "Jalium Window v308 · 动态效果", Width = 620, Height = 380, Content = Root(),
                TitleBarStyle = Custom ^ alternate ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            window.SetResourceReference(Window.BackgroundProperty, "WindowBackground"); _windows.Add(window); window.Show(); window.UpdateLayout();
            Watch(window); return window;
        }
        private void Watch(Window window)
        {
            if (_local.ContainsKey(window)) return; _local[window] = 0;
            window.SystemSettingsChanged += (_, _) =>
            {
                _local[window]++;
                Observed.Add(new { phase = _phase, milliseconds = _timeline.Elapsed.TotalMilliseconds,
                    localSettingsWindow = (long)window.Handle, popup = window.IsMacOSPopupHost, count = _local[window] });
            };
        }
        private void CloseAll()
        {
            foreach (var popup in _popups.AsEnumerable().Reverse()) popup.IsOpen = false;
            foreach (var window in _windows) if (window.Handle != 0) window.Close();
            _local.Clear(); _subjects.Clear(); Barrier();
        }
        internal void Execute()
        {
            var context = RenderContext.GetOrCreateCurrent(Backend, forceReplace: true);
            if (Backend == RenderBackend.Metal) context.DefaultRenderingEngine = RenderingEngine.Impeller;
            Controls.Themes.ThemeManager.Initialize(Application.Current!);
            _changed = (_, e) => { if (e.PropertyName is { } name && Names.Contains(name)) _events.Add(name); };
            SystemParameters.StaticPropertyChanged += _changed;
            Attempt("application-only-startup", () => { Require(Leases == 1 && Window.SnapshotOpenWindows().Count() == 0, "application owns the only appearance lease"); Require(Application.Current!.ThemeMode == Mode(index), "requested application theme mode"); Verify(); });
            if (Owners == 0) Root();
            for (int i = 0; i < Owners; i++) Open(i % 2 != 0);
            if (Owners == 3)
            {
                foreach (bool external in new[] { false, true })
                {
                    var anchor = _subjects[0].Editor; var child = Root(); child.Width = 280; child.Height = 230;
                    var popup = new Popup { Child = child, PlacementTarget = anchor, Placement = PlacementMode.Right, StaysOpen = true,
                        PreferExternalWindow = external, ShouldConstrainToRootBounds = !external };
                    _popups.Add(popup); popup.IsOpen = true; _windows[0].UpdateLayout();
                    var physical = Window.GetMacOSPhysicalWindow(child)!; Require(physical.IsMacOSPopupHost == external, "requested actual popup host"); Watch(physical);
                }
            }
            Barrier(); Verify();
            Attempt("unchanged-workspace", () => { Reset(); Post(); Barrier(); Wave(0); Verify(); });
            Attempt("changed-workspace", () =>
            {
                Reset(); preference.Set(!Cached); Post(); Barrier(); Wave(1); Verify();
                Require(_local.All(p => p.Value == 1), "every live physical window keeps its own local settings callback");
            });
            Attempt("scroller-style-only", () =>
            {
                Reset(); using var name = new NSString("NSPreferredScrollerStyleDidChangeNotification"); NSNotificationCenter.DefaultCenter.PostNotificationName(name, null);
                Barrier(); Wave(0); Verify();
            });
            Attempt("read-before-publication", () =>
            {
                Reset(); preference.Set(!Cached); Require(MacOSScrollBarSettings.RefreshPreferences(), "another reader observes the transition before delivery");
                Require(_events.Count == 0, "reading preferences does not itself publish resources"); Post(); Barrier(); Wave(1); Verify();
            });
            Attempt("same-value-reentry", () =>
            {
                bool entered = false; PropertyChangedEventHandler reenter = (_, e) =>
                { if (!entered && e.PropertyName == Names[0]) { entered = true; Post(); Barrier(); } };
                SystemParameters.StaticPropertyChanged += reenter;
                try { Reset(); preference.Set(!Cached); Post(); Barrier(); Require(entered, "subscriber reenters the actual notification center"); Wave(1); Verify(); }
                finally { SystemParameters.StaticPropertyChanged -= reenter; }
            });
            Attempt("new-value-reentry", () =>
            {
                bool entered = false; PropertyChangedEventHandler replace = (_, e) =>
                { if (!entered && e.PropertyName == Names[0]) { entered = true; preference.Set(!Cached); Post(); Barrier(); } };
                SystemParameters.StaticPropertyChanged += replace;
                try
                {
                    Reset(); preference.Set(!Cached); Post(); Barrier(); Require(entered, "subscriber publishes a newer native preference");
                    Require(_events.Count == 11 && _events.Count(n => n == Names[0]) == 2 && Names.Skip(1).All(n => _events.Count(e => e == n) == 1),
                        "newer preference completes once and retires the old wave before its remaining property events"); Verify();
                }
                finally { SystemParameters.StaticPropertyChanged -= replace; }
            });
            Attempt("new-value-resource-reentry", () =>
            {
                bool entered = false;
                Action<DependencyProperty, object?, object?> replace = (property, _, _) =>
                {
                    if (!entered && property == FrameworkElement.TagProperty)
                    { entered = true; preference.Set(!Cached); Post(); Barrier(); }
                };
                _subjects[0].Probe.PropertyChangedInternal += replace;
                try
                {
                    Reset(); preference.Set(!Cached); Post(); Barrier(); Require(entered, "resource subscriber publishes a newer native preference");
                    Wave(1); Verify();
                }
                finally { _subjects[0].Probe.PropertyChangedInternal -= replace; }
            });
            Attempt("notification-burst", () =>
            {
                Reset(); bool target = !Cached;
                preference.Set(target); Post(); preference.Set(!target); Post(); preference.Set(target); Post(); Barrier();
                Wave(Owners == 0 ? 1 : 3); Verify();
            });
            Attempt("last-window-closed", () => { CloseAll(); Root(); Reset(); Require(Leases == 1, "application observer survives its last window"); preference.Set(!Cached); Post(); Barrier(); Wave(1); Verify(); });
            Attempt("retired-observer", () => { Reset(); bool previous = Cached; preference.Set(!previous); Post(); host.Stop(); Barrier(); Wave(0); Require(Application.Current == null && Leases == 0 && Cached == previous, "retired application callbacks cannot refresh or publish"); });
            _subjects.Clear(); host.Start(); var replacementContext = RenderContext.GetOrCreateCurrent(Backend, forceReplace: true); if (Backend == RenderBackend.Metal) replacementContext.DefaultRenderingEngine = RenderingEngine.Impeller; Controls.Themes.ThemeManager.Initialize(Application.Current!); Barrier();
            Attempt("replacement-owner", () => { Require(Leases == 1, "replacement application starts its sole lease"); Root(); Verify(); });
            Attempt("first-or-reopened-window", () =>
            {
                var window = Open(); Barrier(); Verify(); Require(window.RenderTarget?.Backend == Backend, "final window renders with the requested native backend");
                Observed.Add(new { appActive = NSApplication.SharedApplication.Active, frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                    processId = Environment.ProcessId, key = Runtime.GetNSObject<NSView>(window.Handle)!.Window!.IsKeyWindow, owners = Owners });
                var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * window.DpiScale), (int)Math.Ceiling(window.ActualHeight * window.DpiScale),
                    window.DpiScale, drawing => { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(drawing); }, () => window.Handle != 0)
                    ?? throw new InvalidOperationException("native backend readback");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var image = File.Create(Path.Combine(output, "window.png")); encoder.Save(image);
            });
        }
        internal void Cleanup()
        {
            CloseAll(); foreach (var root in _roots) Css.SetStyleSheets(root, null);
            if (_changed != null) SystemParameters.StaticPropertyChanged -= _changed;
            host.Stop();
        }
    }

    private sealed class Delegate(ThemeMode mode) : NSApplicationDelegate
    {
        private JaliumApp? _host;
        public override void DidFinishLaunching(NSNotification notification) { if (_host == null) Start(); }
        internal void Start() => (_host = AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build().UseApplication(() =>
        { var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; if (mode != ThemeMode.None) app.ThemeMode = mode; return app; })).StartHosted();
        internal void Stop() { var host = _host; _host = null; host?.StopHosted(); }
    }
}
