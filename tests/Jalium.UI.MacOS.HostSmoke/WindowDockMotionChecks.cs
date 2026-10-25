#pragma warning disable WPF0001
using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Shell;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Real, process-local AppKit timers, notifications and drawing. No physical input.
internal static class WindowDockMotionChecks
{
    internal const int CaseCount = 8;
    internal const int ScenarioCount = 16;
    private const BindingFlags HiddenStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo TimerField = typeof(JaliumMacApplication).Assembly
        .GetType("Jalium.UI.MacOS.MacOSTaskbar")!.GetField("_timer", HiddenStatic)!;
    private static NSTimer? Timer => (NSTimer?)TimerField.GetValue(null);

    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= CaseCount) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_DOCK_MOTION_OUTPUT")
            ?? throw new InvalidOperationException("Missing owned output directory.");
        Directory.CreateDirectory(output);
        using var preference = new WindowMotionPreferenceChecks.PreferenceOverride(index % 2 != 0);
        using var display = new DisplayProbe();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate();
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var scene = new Scene(index, output, preference, display);
        bool complete = false, stopped = false;
        app.BeginInvokeOnMainThread(() =>
        {
            try { scene.Execute(); }
            catch (Exception error) { scene.Failures.Add("scene: " + error); }
            complete = true;
        });
        using var finish = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (!complete || stopped) return;
            stopped = true;
            try { scene.Cleanup(); } catch (Exception error) { scene.Failures.Add("cleanup: " + error); }
            bool restored = preference.Restore(), displayRestored = display.Restore();
            if (!restored || !displayRestored) scene.Failures.Add("process-local AppKit overrides were not restored");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, passed = scene.Failures.Count == 0 && scene.Scenarios.Count == ScenarioCount,
                scene.Scenarios, scene.Checks, scene.Failures, scene.WindowTransitions, scene.DisplayObservations, scene.AnimationObservations,
                miniaturizationObserversRestored = scene.ObserversRestored, expected = ScenarioCount,
                software = index >= 4, custom = index % 4 >= 2, initiallyReduced = index % 2 != 0,
                processId = Environment.ProcessId, overrideRestored = restored, displayProbeRestored = displayRestored,
                physicalInput = false, systemPreferencesWritten = false, foregroundRequired = false,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location,
                    typeof(WindowDockMotionChecks).Assembly.Location }
            }));
            app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        });
        app.Run();
        return scene.Failures.Count == 0 && scene.Scenarios.Count == ScenarioCount ? 0 : 1;
    }

    private static void Pump(double seconds)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            using var until = NSDate.FromTimeIntervalSinceNow(.01);
            NSRunLoop.Main.RunUntil(until);
            typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!
                .GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        } while (watch.Elapsed.TotalSeconds < seconds);
    }

    private static void Barrier()
    {
        bool done = false;
        var app = NSApplication.SharedApplication;
        app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => done = true)));
        var watch = Stopwatch.StartNew();
        while (!done && watch.Elapsed.TotalSeconds < 5) Pump(.01);
        if (!done) throw new TimeoutException("AppKit main-thread notification barrier.");
    }

    private sealed class Scene(int index, string output, WindowMotionPreferenceChecks.PreferenceOverride preference, DisplayProbe display)
    {
        internal readonly List<object> Scenarios = [];
        internal readonly List<string> Checks = [], Failures = [];
        private readonly NSApplication _app = NSApplication.SharedApplication;
        private readonly List<Window> _windows = [];
        private Window _window = null!;
        private NSWindow _native = null!;
        private Border _content = null!;
        private TaskbarItemInfo _info = null!;
        private NSView? _originalAppView;
        private int _capture;
        private readonly List<NSObject> _stateObservers = [];
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private int _miniCompletions, _restoreCompletions, _awaitedMini, _awaitedRestore;
        private string _phase = "opening", _observedState = string.Empty;
        internal readonly List<object> WindowTransitions = [], DisplayObservations = [], AnimationObservations = [];
        internal bool ObserversRestored => _stateObservers.Count == 0;

        private void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Checks.Add(message);
        }
        private void Attempt(string name, Action action)
        {
            _phase = name;
            int first = Checks.Count; string? error = null;
            try { action(); } catch (Exception failure) { error = failure.ToString(); Failures.Add(name + ": " + error); }
            Scenarios.Add(new { name, passed = error == null, checks = Checks.Skip(first).ToArray(), error });
            Console.WriteLine(JsonSerializer.Serialize(new { name, passed = error == null, error }));
        }
        private void Reduced(bool value)
        {
            preference.Set(value);
            using var name = new NSString("NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification");
            NSWorkspace.SharedWorkspace.NotificationCenter.PostNotificationName(name, NSWorkspace.SharedWorkspace);
            Barrier();
            Require(preference.Read() == value, "independent AppKit getter follows the process-local preference");
        }
        private void TimerState(bool expected, string message) => Require((Timer is { IsValid: true }) == expected &&
            (expected || Timer == null), message);
        private NSView AppView => _app.DockTile.ContentView ?? throw new InvalidOperationException("Missing application tile.");
        private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window
            ?? throw new InvalidOperationException("Missing physical native window.");
        private Window Open()
        {
            var content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 38, 56)), Padding = new Thickness(28),
                Child = new StackPanel { Spacing = 16, Children =
                {
                    new TextBlock { Text = "下载文档", FontSize = 26 },
                    new TextBlock { Text = "正在下载 · 中文🙂", FontSize = 18 },
                    new TextBox { Text = "窗口编辑内容保持可用", Height = 38 },
                    new Button { Content = "继续", Height = 36 }
                } }
            };
            var window = new Window
            {
                Title = "Jalium Dock Motion v311", Width = 640, Height = 460, Content = content,
                ShowActivated = false, TitleBarStyle = index % 4 >= 2 ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
                TaskbarItemInfo = new TaskbarItemInfo { Description = "下载文档 · 中文🙂", ProgressState = TaskbarItemProgressState.Indeterminate }
            };
            _windows.Add(window); window.Show(); window.UpdateLayout(); Barrier();
            var watch = Stopwatch.StartNew();
            while ((_app.DockTile.ContentView == null || Native(window).DockTile.ContentView == null) && watch.Elapsed.TotalSeconds < 5)
                Pump(.02);
            _content = content;
            return window;
        }
        private byte[] Capture(NSView view, string name)
        {
            view.Frame = new CGRect(0, 0, 128, 128);
            using var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds)
                ?? throw new InvalidOperationException("Missing AppKit tile bitmap.");
            var data = new byte[checked((int)(bitmap.BytesPerRow * bitmap.PixelsHigh))];
            Marshal.Copy(data, 0, bitmap.BitmapData, data.Length);
            view.CacheDisplay(view.Bounds, bitmap);
            Marshal.Copy(bitmap.BitmapData, data, 0, data.Length);
            string prefix = Path.Combine(output, $"{_capture++:00}-{name}");
            File.WriteAllBytes(prefix + ".pixels", data);
            using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)!;
            File.WriteAllBytes(prefix + ".png", png.ToArray());
            return data;
        }
        private void StableApplication(string label, double wait = .35)
        {
            byte[] before = Capture(AppView, label + "-before");
            ulong displays = display.Count(_app.DockTile.Handle);
            Pump(wait);
            byte[] after = Capture(AppView, label + "-after");
            ulong afterDisplays = display.Count(_app.DockTile.Handle);
            DisplayObservations.Add(new { label, milliseconds = _watch.Elapsed.TotalMilliseconds,
                beforeDisplays = displays, afterDisplays, miniCompletions = _miniCompletions,
                restoreCompletions = _restoreCompletions, nativeMiniaturized = _native.IsMiniaturized,
                nativeVisible = _native.IsVisible, nativeKey = _native.IsKeyWindow, managedState = _window.WindowState.ToString() });
            Require(before.AsSpan().SequenceEqual(after), "reduced indeterminate tile keeps identical pixels: " + label);
            Require(afterDisplays == displays, "no repeated application Dock display: " + label +
                "; before=" + displays + "; after=" + afterDisplays + "; completed=" + _miniCompletions + "/" + _restoreCompletions);
        }
        private void AnimatedApplication(string label)
        {
            byte[] before = Capture(AppView, label + "-before");
            ulong displays = display.Count(_app.DockTile.Handle);
            bool changed = false;
            // A manual readback advances the drawn phase even before the timer fires.
            // Observe both signals within the existing four-pump budget.
            for (int i = 0; i < 4 && (!changed || display.Count(_app.DockTile.Handle) <= displays); i++)
            {
                Pump(.16);
                changed = !before.AsSpan().SequenceEqual(Capture(AppView, label + "-after"));
                AnimationObservations.Add(new { label, milliseconds = _watch.Elapsed.TotalMilliseconds,
                    animationPoll = i + 1, changed, displaysBefore = displays,
                    displaysAfter = display.Count(_app.DockTile.Handle) });
            }
            Require(changed, "enabled indeterminate tile advances its rendered phase");
            Require(display.Count(_app.DockTile.Handle) > displays, "real timer requests application Dock display");
        }
        private void ObserveWindowCompletion()
        {
            Window observedWindow = _window;
            NSWindow observedNative = _native;
            void Observe(NSString name, Action? completed = null) => _stateObservers.Add(
                NSNotificationCenter.DefaultCenter.AddObserver(name, _ =>
                {
                    if (observedWindow.Handle == 0) return;
                    completed?.Invoke();
                    string state = $"{observedNative.IsMiniaturized}/{observedNative.IsVisible}/{observedNative.IsKeyWindow}/{observedWindow.WindowState}";
                    if (name == NSWindow.DidUpdateNotification && state == _observedState) return;
                    _observedState = state;
                    WindowTransitions.Add(new { name = name.ToString(), phase = _phase,
                        milliseconds = _watch.Elapsed.TotalMilliseconds, nativeMiniaturized = observedNative.IsMiniaturized,
                        nativeVisible = observedNative.IsVisible, nativeKey = observedNative.IsKeyWindow,
                        managedState = observedWindow.WindowState.ToString(), applicationDisplays = display.Count(_app.DockTile.Handle),
                        miniCompletions = _miniCompletions, restoreCompletions = _restoreCompletions });
                }, observedNative));
            Observe(NSWindow.DidMiniaturizeNotification, () => _miniCompletions++);
            Observe(NSWindow.DidDeminiaturizeNotification, () => _restoreCompletions++);
            Observe(NSWindow.DidBecomeKeyNotification);
            Observe(NSWindow.DidResignKeyNotification);
            Observe(NSWindow.DidUpdateNotification);
        }
        private void WaitMiniaturized(bool expected)
        {
            int completion = expected ? ++_awaitedMini : ++_awaitedRestore;
            bool Complete() => _native.IsMiniaturized == expected &&
                (expected ? _miniCompletions : _restoreCompletions) >= completion &&
                _window.WindowState == (expected ? WindowState.Minimized : WindowState.Normal);
            var watch = Stopwatch.StartNew();
            while (!Complete() && watch.Elapsed.TotalSeconds < 5) Pump(.03);
            Require(Complete(), "AppKit completion notification, native and managed miniaturization states agree");
            Barrier();
        }
        internal void Execute()
        {
            var context = RenderContext.GetOrCreateCurrent(index >= 4 ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
            if (index < 4) context.DefaultRenderingEngine = RenderingEngine.Impeller;
            Require(context.Backend == (index >= 4 ? RenderBackend.Software : RenderBackend.Metal), "requested actual rendering backend");
            Controls.Themes.ThemeManager.Initialize(Application.Current!);
            _originalAppView = _app.DockTile.ContentView;
            _window = Open(); _native = Native(_window); _info = _window.TaskbarItemInfo!;
            ObserveWindowCompletion();
            Attempt("initial-policy", () =>
            {
                Require(AppView.AccessibilityLabel == _info.Description && !string.IsNullOrWhiteSpace(AppView.AccessibilityValue?.ToString()), "task description and indeterminate semantic value remain available");
                TimerState(index % 2 == 0, "initial preference controls the real Dock timer");
            });
            Attempt("animated-progress", () => { Reduced(false); TimerState(true, "animation has a live timer"); AnimatedApplication("animated"); });
            Attempt("stop-for-reduced-motion", () =>
            {
                var previous = Timer; Reduced(true); TimerState(false, "reduced motion retires the animation-only timer");
                Require(previous == null || previous.Handle == (nint)0 || !previous.IsValid, "retired NSTimer is invalid or disposed");
                StableApplication("reduced");
            });
            Attempt("unchanged-reduced-notification", () =>
            {
                Reduced(true); TimerState(false, "unchanged notification does not recreate a static timer"); StableApplication("unchanged");
            });
            Attempt("resume-animation", () =>
            {
                Reduced(false); TimerState(true, "disabling reduced motion resumes animation");
                var timer = Timer; Reduced(false); Require(ReferenceEquals(timer, Timer), "unchanged enabled preference reuses the timer"); AnimatedApplication("resumed");
            });
            Attempt("mutable-overlay-with-reduced-motion", () =>
            {
                Reduced(true);
                var overlay = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);
                void Pixels(byte channel) => overlay.WritePixels(new Int32Rect(0, 0, 8, 8),
                    Enumerable.Range(0, 64).SelectMany(_ => new byte[] { channel, 90, (byte)(255 - channel), 255 }).ToArray(), 32, 0);
                Pixels(30); _info.Overlay = overlay; Barrier(); TimerState(true, "mutable overlay retains content polling");
                StableApplication("unchanged-overlay");
                byte[] before = Capture(AppView, "overlay-before-change"); Pixels(210); Pump(.35);
                Require(!before.AsSpan().SequenceEqual(Capture(AppView, "overlay-after-change")), "mutable overlay content still reaches the Dock");
            });
            Attempt("remove-overlay", () =>
            {
                Reduced(true); _info.Overlay = null; Barrier(); TimerState(false, "removing the final polling reason stops the timer"); StableApplication("overlay-removed");
            });
            Attempt("static-progress-states", () =>
            {
                Reduced(false);
                foreach (var state in new[] { TaskbarItemProgressState.None, TaskbarItemProgressState.Normal, TaskbarItemProgressState.Error, TaskbarItemProgressState.Paused })
                { _info.ProgressState = state; Barrier(); TimerState(false, "static progress has no animation timer: " + state); }
                _info.ProgressState = TaskbarItemProgressState.Indeterminate;
            });
            Attempt("hide-and-show", () =>
            {
                Reduced(false); _info.ProgressState = TaskbarItemProgressState.Indeterminate;
                _window.Hide(); Barrier(); TimerState(false, "hidden task cannot keep animation alive");
                _window.Show(); _window.UpdateLayout(); Barrier(); TimerState(true, "showing the eligible task restores animation");
            });
            Attempt("taskbar-inclusion", () =>
            {
                Reduced(false); _window.ShowInTaskbar = false; Barrier(); TimerState(false, "excluded task cannot keep animation alive");
                _window.ShowInTaskbar = true; Barrier(); TimerState(true, "including the task restores animation");
            });
            Attempt("minimized-description-only", () =>
            {
                Reduced(true); _info.Overlay = null; _info.ThumbnailClipMargin = Thickness.Zero;
                _info.ProgressState = TaskbarItemProgressState.None;
                _native.Miniaturize(_native); WaitMiniaturized(true);
                TimerState(false, "description-only minimized task has no thumbnail polling");
            });
            Attempt("minimized-thumbnail-content", () =>
            {
                Reduced(true); _info.ThumbnailClipMargin = new Thickness(1); Barrier();
                TimerState(true, "custom minimized thumbnail retains content polling");
                var tile = _native.DockTile.ContentView ?? throw new InvalidOperationException("Missing custom minimized tile.");
                byte[] before = Capture(tile, "thumbnail-before");
                _content.Background = new SolidColorBrush(Color.FromRgb(42, 110, 72)); Pump(1.3);
                Require(!before.AsSpan().SequenceEqual(Capture(tile, "thumbnail-after")), "minimized custom thumbnail still refreshes its content");
            });
            Attempt("static-progress-during-thumbnail-polling", () =>
            {
                Reduced(true); _info.ProgressState = TaskbarItemProgressState.Indeterminate; Barrier();
                TimerState(true, "required thumbnail polling survives reduced motion"); StableApplication("thumbnail-polling");
            });
            Attempt("deminiaturized-static-progress", () =>
            {
                Reduced(true); _native.Deminiaturize(_native); WaitMiniaturized(false);
                _info.ThumbnailClipMargin = Thickness.Zero; Barrier();
                TimerState(false, "deminiaturization retires the last content polling reason"); StableApplication("restored");
            });
            Attempt("multiple-window-owners", () =>
            {
                Reduced(false); Window? other = null;
                try
                {
                    var timer = Timer; other = Open();
                    Require(ReferenceEquals(timer, Timer), "multiple windows share one animation timer");
                    _window.Hide(); Barrier(); TimerState(true, "remaining eligible task keeps animation alive");
                    other.Close(); other = null; Barrier(); TimerState(false, "closing the last eligible owner stops animation");
                }
                finally { other?.Close(); _window.Show(); _window.UpdateLayout(); Barrier(); }
                Reduced(true); TimerState(false, "reduced motion applies to all surviving owners");
            });
            Attempt("close-and-reopen", () =>
            {
                int.TryParse(Environment.GetEnvironmentVariable("JALIUM_DOCK_MOTION_HOLD_SECONDS"), out int hold);
                if (hold > 0) Pump(Math.Clamp(hold, 0, 30));
                Reduced(false); var timer = Timer; _window.Close(); Barrier(); TimerState(false, "closing the last window releases its timer");
                Require(timer == null || timer.Handle == (nint)0 || !timer.IsValid, "closed owner's NSTimer cannot fire");
                Reduced(true); Reduced(false); TimerState(false, "preference notifications cannot revive a closed owner");
                _window = Open(); _native = Native(_window); _info = _window.TaskbarItemInfo!;
                TimerState(true, "newly opened owner obtains a current timer"); _window.Close(); Barrier();
                TimerState(false, "reopened owner releases its timer on close");
            });
        }
        internal void Cleanup()
        {
            foreach (var token in _stateObservers) { NSNotificationCenter.DefaultCenter.RemoveObserver(token); token.Dispose(); }
            _stateObservers.Clear();
            foreach (var window in _windows.AsEnumerable().Reverse()) if (window.Handle != 0) window.Close();
            Barrier();
            Require(Timer == null, "all owned windows leave no Dock timer");
            Require(_app.DockTile.ContentView?.Handle == _originalAppView?.Handle, "cleanup restores the original application tile");
        }
    }

    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System });
    }

    private sealed class DisplayProbe : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Zero();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong Read(nint handle);
        private readonly nint _library;
        private readonly Zero _restore;
        private readonly Read _count;
        private bool _restored;
        internal DisplayProbe()
        {
            _library = NativeLibrary.Load(Environment.GetEnvironmentVariable("JALIUM_MOTION_TEST_LIBRARY")!);
            T Function<T>(string name) where T : System.Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            _restore = Function<Zero>("jalium_dock_display_restore"); _count = Function<Read>("jalium_dock_display_count");
            if (Function<Zero>("jalium_dock_display_install")() != 1) throw new InvalidOperationException("Cannot observe process-local Dock display calls.");
        }
        internal ulong Count(nint handle) => _count(handle);
        internal bool Restore() => _restored || (_restored = _restore() == 1);
        public void Dispose() { Restore(); NativeLibrary.Free(_library); }
    }
}
