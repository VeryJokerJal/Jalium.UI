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

internal static class WindowDockOwnershipChecks
{
    internal const int CaseCount = 8, ScenarioCount = 32;
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= CaseCount) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_DOCK_OWNERSHIP_OUTPUT")!;
        Directory.CreateDirectory(output);
        using var preference = new WindowMotionPreferenceChecks.PreferenceOverride(index % 2 != 0);
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var scene = new Scene(index, output); bool complete = false, stopped = false;
        app.BeginInvokeOnMainThread(() =>
        {
            try { scene.Execute(); } catch (Exception error) { scene.Failures.Add("scene: " + error); }
            finally { try { scene.Cleanup(); } catch (Exception error) { scene.Failures.Add("cleanup: " + error); } }
            complete = true;
        });
        using var finish = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (!complete || stopped) return; stopped = true;
            bool restored = preference.Restore(); if (!restored) scene.Failures.Add("preference getter was not restored");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, passed = scene.Failures.Count == 0 && scene.Scenarios.Count == ScenarioCount,
                scene.Scenarios, scene.Checks, scene.Failures, expected = ScenarioCount,
                software = index >= 4, custom = index % 4 >= 2, reduced = index % 2 != 0,
                processId = Environment.ProcessId, overrideRestored = restored,
                physicalInput = false, foregroundRequired = false, systemPreferencesWritten = false,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location,
                    typeof(WindowDockOwnershipChecks).Assembly.Location }
            }));
            app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        });
        app.Run(); return scene.Failures.Count == 0 && scene.Scenarios.Count == ScenarioCount ? 0 : 1;
    }

    private static bool Same(NSView? first, NSView? second) => first == null ? second == null :
        second != null && (nint)first.Handle != 0 && first.Handle == second.Handle;
    private static void SetView(NSDockTile tile, NSView? view)
    {
        if (view != null) tile.ContentView = view;
        else SetNullableObject(tile.Handle, Selector.GetHandle("setContentView:"), 0);
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetNullableObject(nint receiver, nint selector, nint value);
    private static void Pump(double seconds)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            using var until = NSDate.FromTimeIntervalSinceNow(.01); NSRunLoop.Main.RunUntil(until);
            typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!
                .GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        } while (watch.Elapsed.TotalSeconds < seconds);
    }
    private static void Barrier()
    {
        bool done = false; var app = NSApplication.SharedApplication; var watch = Stopwatch.StartNew();
        app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => done = true)));
        while (!done && watch.Elapsed.TotalSeconds < 5) Pump(.01);
        if (!done) throw new TimeoutException("own AppKit notification barrier");
    }

    private sealed class Scene(int index, string output)
    {
        internal readonly List<object> Scenarios = [];
        internal readonly List<string> Checks = [], Failures = [];
        private readonly NSApplication _app = NSApplication.SharedApplication;
        private readonly NSView? _savedAppView = NSApplication.SharedApplication.DockTile.ContentView;
        private readonly List<Window> _windows = [];
        private Window _window = null!;
        private NSWindow _native = null!;
        private CaptureProbe _probe = null!;
        private TaskbarItemInfo _info = null!;
        private string _base = "";
        private int _capture;
        private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window
            ?? throw new InvalidOperationException("missing physical native window");
        private void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); Checks.Add(message); }
        private void Attempt(string name, Action action)
        {
            int first = Checks.Count; string? error = null;
            try { action(); } catch (Exception failure) { error = failure.ToString(); Failures.Add(_base + ":" + name + ":" + error); }
            Scenarios.Add(new { name = _base + ":" + name, passed = error == null, checks = Checks.Skip(first).ToArray(), error });
            Console.WriteLine(JsonSerializer.Serialize(new { name = _base + ":" + name, passed = error == null, error }));
        }
        private Window Open(bool model = false)
        {
            var probe = new CaptureProbe { Width = 240, Height = 22 };
            var root = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children =
            {
                new TextBlock { Text = "文档任务", FontSize = 26 },
                new TextBlock { Text = "Dock 内容交接 · 中文🙂", FontSize = 18 },
                new TextBox { Text = "窗口内容保持可编辑", Height = 38 }, probe,
                new Button { Content = "继续", Height = 36 }
            } };
            var window = new Window { Title = "Jalium Dock Ownership v312", Width = 640, Height = 420,
                ShowActivated = false, TitleBarStyle = index % 4 >= 2 ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
                Content = root, TaskbarItemInfo = model ? Model() : null };
            _windows.Add(window); window.Show(); window.UpdateLayout(); Barrier(); _probe = probe; return window;
        }
        private static TaskbarItemInfo Model() => new() { Description = "文档任务 · 中文🙂", ProgressState = TaskbarItemProgressState.Normal, ProgressValue = .35 };
        private void Foreign(NSView app, NSView window)
        { _app.DockTile.ContentView = app; _native.DockTile.ContentView = window; }
        private void Preserved(NSView app, NSView window, string message)
        {
            Require(Same(_app.DockTile.ContentView, app), "application surface retained: " + message);
            Require(Same(_native.DockTile.ContentView, window), "window surface retained: " + message);
        }
        private void Refresh() { _info.ProgressValue = _info.ProgressValue == .4 ? .6 : .4; Barrier(); }
        private void None()
        { _info.ProgressState = TaskbarItemProgressState.None; _info.Overlay = null; _info.ThumbnailClipMargin = Thickness.Zero; Barrier(); }
        private void Normal() { _info.ProgressState = TaskbarItemProgressState.Normal; Barrier(); }
        private byte[] Capture(NSView view, string name)
        {
            view.Frame = new CGRect(0, 0, 128, 128);
            using var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds)!;
            byte[] pixels = new byte[checked((int)(bitmap.BytesPerRow * bitmap.PixelsHigh))];
            Marshal.Copy(pixels, 0, bitmap.BitmapData, pixels.Length); view.CacheDisplay(view.Bounds, bitmap);
            Marshal.Copy(bitmap.BitmapData, pixels, 0, pixels.Length);
            string path = Path.Combine(output, $"{_base}-{_capture++:00}-{name}");
            File.WriteAllBytes(path + ".pixels", pixels);
            using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)!;
            File.WriteAllBytes(path + ".png", png.ToArray()); return pixels;
        }
        private void WaitMini(bool value)
        {
            var watch = Stopwatch.StartNew();
            while (_native.IsMiniaturized != value && watch.Elapsed.TotalSeconds < 5) Pump(.02);
            Require(_native.IsMiniaturized == value, "AppKit acknowledges miniaturization state"); Barrier();
        }
        internal void Execute()
        {
            var backend = index >= 4 ? RenderBackend.Software : RenderBackend.Metal;
            var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
            if (backend == RenderBackend.Metal) context.DefaultRenderingEngine = RenderingEngine.Impeller;
            Require(context.Backend == backend, "requested actual rendering backend");
            Controls.Themes.ThemeManager.Initialize(Application.Current!);
            foreach (bool originalPresent in new[] { false, true })
            {
                _base = originalPresent ? "original" : "nil";
                using var originalApp = new ForeignView(.20f, .45f, .75f, "原始应用内容");
                using var originalWindow = new ForeignView(.20f, .45f, .75f, "原始窗口内容");
                using var externalApp = new ForeignView(.78f, .25f, .20f, "外部应用内容");
                using var externalWindow = new ForeignView(.78f, .25f, .20f, "外部窗口内容");
                using var nextApp = new ForeignView(.18f, .65f, .35f, "后续应用内容");
                using var nextWindow = new ForeignView(.18f, .65f, .35f, "后续窗口内容");
                NSView? appBase = originalPresent ? originalApp : null, windowBase = originalPresent ? originalWindow : null;
                SetView(_app.DockTile, appBase); _window = Open(); _native = Native(_window);
                SetView(_native.DockTile, windowBase); _info = Model(); _window.TaskbarItemInfo = _info; Barrier();
                var ownApp = _app.DockTile.ContentView!; var ownWindow = _native.DockTile.ContentView!;
                Attempt("initial-ownership", () =>
                {
                    Require(ownApp.GetType().Name == "TileView" && ownWindow.GetType().Name == "TileView", "initial custom tiles are actually installed");
                    Require(ownApp.AccessibilityLabel == _info.Description, "current task description is exposed"); Capture(ownApp, "initial");
                });
                Attempt("active-takeover", () => { Foreign(externalApp, externalWindow); Refresh(); Preserved(externalApp, externalWindow, "active model update"); });
                Attempt("none-reactivation", () => { None(); Preserved(externalApp, externalWindow, "None"); Normal(); Preserved(externalApp, externalWindow, "progress restarted"); });
                Attempt("overlay-reactivation", () =>
                {
                    None(); var overlay = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
                    _info.Overlay = overlay; Barrier(); Preserved(externalApp, externalWindow, "overlay restarted");
                    overlay.WritePixels(new Int32Rect(0, 0, 4, 4), Enumerable.Repeat((byte)220, 64).ToArray(), 16, 0);
                    Pump(.25); Preserved(externalApp, externalWindow, "mutable overlay changed"); None();
                });
                Attempt("thumbnail-reactivation", () => { None(); _info.ThumbnailClipMargin = new Thickness(2); Barrier(); Preserved(externalApp, externalWindow, "thumbnail restarted"); None(); });
                Attempt("model-replacement", () => { _info = Model(); _window.TaskbarItemInfo = _info; Barrier(); Preserved(externalApp, externalWindow, "replacement model within the same registration"); });
                Attempt("hide-show", () => { _window.Hide(); Barrier(); Preserved(externalApp, externalWindow, "hidden"); _window.Show(); _window.UpdateLayout(); Barrier(); Preserved(externalApp, externalWindow, "shown"); });
                Attempt("taskbar-inclusion", () => { _window.ShowInTaskbar = false; Barrier(); Preserved(externalApp, externalWindow, "excluded"); _window.ShowInTaskbar = true; Barrier(); Preserved(externalApp, externalWindow, "included"); });
                Attempt("minimize-restore", () => { _native.Miniaturize(_native); WaitMini(true); Preserved(externalApp, externalWindow, "minimized"); _native.Deminiaturize(_native); WaitMini(false); Refresh(); Preserved(externalApp, externalWindow, "restored"); });
                Attempt("replacement-external-owner", () =>
                {
                    Foreign(nextApp, nextWindow); None(); Normal(); Preserved(nextApp, nextWindow, "new external owner");
                    var before = Capture(nextApp, "external"); Refresh(); Require(before.AsSpan().SequenceEqual(Capture(nextApp, "external-after")), "foreign rendered pixels remain intact");
                });
                Attempt("return-owned-view", () =>
                {
                    int.TryParse(Environment.GetEnvironmentVariable("JALIUM_DOCK_OWNERSHIP_HOLD_SECONDS"), out int hold); if (hold > 0 && !originalPresent) Pump(Math.Clamp(hold, 0, 30));
                    Foreign(ownApp, ownWindow); Normal(); Refresh();
                    Require(Same(_app.DockTile.ContentView, ownApp) && Same(_native.DockTile.ContentView, ownWindow), "returned owned surfaces resume the same lease");
                    None(); Require(Same(_app.DockTile.ContentView, appBase) && Same(_native.DockTile.ContentView, windowBase), "returned owned surfaces restore their original bases");
                });
                Attempt("release-to-original", () =>
                {
                    Normal(); Foreign(externalApp, externalWindow); Refresh(); None(); SetView(_app.DockTile, appBase); SetView(_native.DockTile, windowBase); Normal();
                    Require(_app.DockTile.ContentView?.GetType().Name == "TileView" && _native.DockTile.ContentView?.GetType().Name == "TileView", "restoring the original surfaces permits reacquisition");
                    None(); Require(Same(_app.DockTile.ContentView, appBase) && Same(_native.DockTile.ContentView, windowBase), "reacquisition retains the current bases");
                });
                Attempt("release-to-nil", () =>
                {
                    Normal(); Foreign(externalApp, externalWindow); Refresh(); None(); SetView(_app.DockTile, null); SetView(_native.DockTile, null); Normal();
                    Require(_app.DockTile.ContentView?.GetType().Name == "TileView" && _native.DockTile.ContentView?.GetType().Name == "TileView", "removing external surfaces permits reacquisition");
                    None(); Require(_app.DockTile.ContentView == null && _native.DockTile.ContentView == null, "nil relinquishment does not restore a stale original surface");
                });
                Attempt("takeover-during-capture", () =>
                {
                    Normal(); bool fired = false, changing = true;
                    _probe.NextRender = () => { if (!NSThread.IsMain) return; Foreign(externalApp, externalWindow); fired = changing; };
                    _probe.InvalidateVisual();
                    _info.ThumbnailClipMargin = new Thickness(3); changing = false; _probe.NextRender = null;
                    Require(fired, $"takeover callback runs inside thumbnail capture ({_probe.ActualWidth}x{_probe.ActualHeight})"); Preserved(externalApp, externalWindow, "capture callback");
                });
                Attempt("multiple-application-owners", () =>
                {
                    Foreign(externalApp, externalWindow); Refresh(); var other = Open(model: true);
                    Require(Same(_app.DockTile.ContentView, externalApp), "another window respects the application takeover");
                    _window.Close(); Barrier(); Require(Same(_app.DockTile.ContentView, externalApp), "closing one owner does not reset the shared takeover");
                    other.TaskbarItemInfo!.ProgressState = TaskbarItemProgressState.None; other.TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal; Barrier();
                    Require(Same(_app.DockTile.ContentView, externalApp), "remaining owner cannot steal the external surface");
                    other.Close(); Barrier(); Require(Same(_app.DockTile.ContentView, externalApp), "last close preserves external content");
                });
                Attempt("new-registration", () =>
                {
                    foreach (var window in _windows) if (window.Handle != 0) window.Close(); Barrier();
                    _app.DockTile.ContentView = nextApp; var fresh = Open(model: true);
                    Require(_app.DockTile.ContentView?.GetType().Name == "TileView", "a new registration obtains a new application lease");
                    fresh.TaskbarItemInfo = null; Barrier(); Require(Same(_app.DockTile.ContentView, nextApp), "clearing the model ends its registration and restores the base");
                    fresh.TaskbarItemInfo = Model(); Barrier(); Require(_app.DockTile.ContentView?.GetType().Name == "TileView", "a model after explicit unregistration starts a new lease");
                    fresh.Close(); Barrier(); Require(Same(_app.DockTile.ContentView, nextApp), "new lease restores its own captured base");
                });
                foreach (var window in _windows) if (window.Handle != 0) window.Close(); Barrier(); SetView(_app.DockTile, _savedAppView);
            }
        }
        internal void Cleanup()
        {
            foreach (var window in _windows.AsEnumerable().Reverse()) if (window.Handle != 0) window.Close(); Barrier(); SetView(_app.DockTile, _savedAppView);
            Require(Same(_app.DockTile.ContentView, _savedAppView), "cleanup restores the original process application surface");
        }
    }

    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System });
    }
    private sealed class CaptureProbe : FrameworkElement
    {
        internal Action? NextRender;
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext); var callback = NextRender; NextRender = null; callback?.Invoke();
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(70, 120, 180)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }
    [Register("JaliumDockOwnershipForeignView")]
    private sealed class ForeignView : NSView
    {
        private readonly NSColor _color;
        internal ForeignView(float red, float green, float blue, string label) : base(new CGRect(0, 0, 128, 128))
        { _color = NSColor.FromRgb(red, green, blue); AccessibilityElement = true; AccessibilityRole = "AXImage"; AccessibilityLabel = label; }
        public override void DrawRect(CGRect dirtyRect) { _color.SetFill(); NSBezierPath.FillRect(Bounds); }
    }
}
