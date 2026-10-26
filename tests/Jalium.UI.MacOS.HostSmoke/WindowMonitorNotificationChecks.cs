#pragma warning disable WPF0001
using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowMonitorNotificationChecks
{
    internal const int CaseCount = 8, CheckCount = 22;
    private static NSTimer? _completion;

    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= CaseCount) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_MONITOR_NOTIFICATION_OUTPUT")!;
        Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        var scene = new Scene(index, output);
        app.BeginInvokeOnMainThread(() =>
        {
            try { scene.Execute(); } catch (Exception error) { scene.Failures.Add("scene: " + error); }
            int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MONITOR_NOTIFICATION_HOLD_SECONDS"), out int hold);
            var elapsed = Stopwatch.StartNew(); bool completing = false;
            _completion = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
            {
                if (completing || elapsed.Elapsed.TotalSeconds < Math.Clamp(hold, 0, 60)) return;
                completing = true; _completion?.Invalidate();
                try { scene.Cleanup(); } catch (Exception error) { scene.Failures.Add("cleanup: " + error); }
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    index, passed = scene.Failures.Count == 0 && scene.Checks.Count == CheckCount,
                    scene.Checks, scene.Failures, scene.Observed, scene.EventTrace, scene.NativeDeliveries, scene.FrameFailures, custom = scene.Custom, cancel = scene.Cancel,
                    renderBackend = scene.Backend.ToString(), processId = Environment.ProcessId,
                    foregroundRequired = false, physicalInput = false, systemPreferencesWritten = false,
                    applicationStillNull = Application.Current == null, nativeImages = NativeImages(),
                    managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location,
                        typeof(WindowMonitorNotificationChecks).Assembly.Location }
                }));
                app.Stop(app);
                using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
                app.PostEvent(wake, false);
            });
        });
        app.Run(); _completion?.Invalidate(); _completion = null;
        return scene.Failures.Count == 0 && scene.Checks.Count == CheckCount ? 0 : 1;
    }

    private sealed class Delegate : NSApplicationDelegate
    {
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => NSApplicationTerminateReply.Cancel;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")] private static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")] private static extern nint ImageName(uint index);
    private static string[] NativeImages() => Enumerable.Range(0, checked((int)ImageCount()))
        .Select(i => Marshal.PtrToStringUTF8(ImageName((uint)i))!).Where(p => p.Contains("libjalium.native.")).ToArray();

    private static void Barrier()
    {
        bool done = false; var app = NSApplication.SharedApplication; var elapsed = Stopwatch.StartNew();
        app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => done = true)));
        while (!done && elapsed.Elapsed.TotalSeconds < 5)
        {
            using var until = NSDate.FromTimeIntervalSinceNow(.01); NSRunLoop.Main.RunUntil(until);
            typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!
                .GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        }
        if (!done) throw new TimeoutException("own monitor notification barrier");
    }

    private sealed class Scene(int index, string output)
    {
        private readonly List<Window> _windows = [];
        private Window _target = null!, _survivor = null!;
        private TextBox _editor = null!;
        private TextBlock _status = null!;
        private NSWindow _nativeTarget = null!;
        private int _global, _local, _survivorLocal, _closeAttempts, _closed;
        private string _phase = "setup";
        private int _ownGlobal, _ownLocal, _ownOther, _backgroundGlobal, _backgroundLocal, _backgroundOther;
        private int _postDepth, _backgroundDepth, _monitorFrames;
        private Delivery? _delivery;
        private readonly Dictionary<IPlatformWindow, Action<PlatformEvent>> _handlers = [];
        internal readonly List<object> EventTrace = [], NativeDeliveries = [];
        internal readonly List<string> FrameFailures = [];
        private sealed class Delivery(Window window, PlatformEventType type, bool own, bool background)
        {
            internal readonly Window Window = window;
            internal readonly PlatformEventType Type = type;
            internal readonly bool Own = own, Background = background;
            internal int Global, Local, ExpectedLocal = -1;
        }

        internal readonly List<string> Checks = [], Failures = [];
        internal readonly List<object> Observed = [];
        internal bool Custom => index % 2 != 0;
        internal bool Cancel => index >= 4;
        internal RenderBackend Backend => index % 4 >= 2 ? RenderBackend.Software : RenderBackend.Metal;
        private void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Checks.Add(message); }
        private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;

        private Window Open(string title, ResourceDictionary theme, out TextBox editor, out TextBlock status)
        {
            bool dark = SystemParameters.PrefersDarkColorScheme;
            var ink = new SolidColorBrush(dark ? Colors.White : Colors.Black);
            var window = new Window { Title = title, Width = 430, Height = 260, Left = 60 + 450 * _windows.Count, Top = 60,
                Background = new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243)),
                TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            window.Resources.MergedDictionaries.Add(theme);
            var root = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
            root.Children.Add(new TextBlock { Text = "显示参数通知", FontSize = 24, Foreground = ink });
            status = new TextBlock { Text = "回调中的关闭与取消关闭", FontSize = 18, Foreground = ink };
            root.Children.Add(status);
            editor = new TextBox { Text = "ab🙂中文 Miii", Height = 46, Foreground = ink,
                Background = new SolidColorBrush(dark ? Color.FromRgb(45, 45, 45) : Colors.White) };
            root.Children.Add(editor); window.Content = root; _windows.Add(window); window.Show(); window.UpdateLayout(); editor.Select(4, 2);
            return window;
        }

        private void Capture(Window window, string name)
        {
            window.UpdateLayout();
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * window.DpiScale),
                (int)Math.Ceiling(window.ActualHeight * window.DpiScale), window.DpiScale,
                dc => { dc.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(dc); },
                () => window.Handle != 0) ?? throw new InvalidOperationException("actual backend readback");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }

        private string Role(Window? window) => window == _target ? "target" : window == _survivor ? "survivor" : "none";
        private void Trace(string kind)
        {
            EventTrace.Add(new { phase = _phase, kind, window = Role(_delivery?.Window), nativeEvent = _delivery?.Type.ToString(),
                explicitPost = _delivery?.Own ?? false, backgroundPost = _delivery?.Background ?? false,
                global = _global, local = _local, survivorLocal = _survivorLocal });
        }
        private void Watch(Window window)
        {
            var platform = (IPlatformWindow)typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var original = (Action<PlatformEvent>)platform.GetType().GetField("_eventHandler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(platform)!;
            _handlers.Add(platform, original);
            platform.SetEventHandler(evt =>
            {
                if (evt.Type is not (PlatformEventType.MonitorsChanged or PlatformEventType.ScrollBarSettingsChanged)) { original(evt); return; }
                var previous = _delivery;
                var frame = new Delivery(window, evt.Type, _postDepth != 0, _backgroundDepth != 0);
                _delivery = frame;
                try { Trace("native-enter"); original(evt); }
                finally
                {
                    bool valid = frame.Local <= 1;
                    if (frame.Type == PlatformEventType.MonitorsChanged)
                    {
                        _monitorFrames++;
                        valid &= frame.Global == 1 && frame.ExpectedLocal >= 0 && frame.Local == frame.ExpectedLocal;
                    }
                    if (!valid) FrameFailures.Add(_phase + ": invalid native delivery for " + Role(window));
                    NativeDeliveries.Add(new { phase = _phase, window = Role(window), nativeEvent = frame.Type.ToString(),
                        explicitPost = frame.Own, backgroundPost = frame.Background, globals = frame.Global,
                        locals = frame.Local, expectedLocal = frame.ExpectedLocal, liveAfter = window.Handle != 0, valid });
                    Trace("native-exit"); _delivery = previous;
                }
            });
        }
        private void LocalSettings(object? sender, EventArgs args)
        {
            if (sender is not Window window) { FrameFailures.Add("local notification has no Window sender"); return; }
            if (window == _target) _local++; else _survivorLocal++;
            if (_delivery is not { } frame || frame.Window != window) FrameFailures.Add("local notification has no owning native delivery");
            else
            {
                frame.Local++;
                if (window.Handle == 0) FrameFailures.Add("closed window received a local notification");
                if (frame.Own) { if (window == _target) _ownLocal++; else _ownOther++; }
                if (frame.Background) { if (window == _target) _backgroundLocal++; else _backgroundOther++; }
            }
            Trace("local");
        }
        private void Reset()
        {
            _global = _local = _survivorLocal = 0;
            _ownGlobal = _ownLocal = _ownOther = 0;
            _backgroundGlobal = _backgroundLocal = _backgroundOther = 0;
        }
        private void Post()
        {
            using var name = new NSString("NSWindowDidChangeScreenNotification");
            _postDepth++;
            try { NSNotificationCenter.DefaultCenter.PostNotificationName(name, _nativeTarget); }
            finally { _postDepth--; }
            bool targetLive = _target.Handle != 0, otherLive = _survivor.Handle != 0;
            // Real global display notifications can arrive beside a window-specific one.
            // Exercise that interleaving on every run and retain every raw callback.
            using var globalName = new NSString("NSApplicationDidChangeScreenParametersNotification");
            _backgroundDepth++;
            try
            {
                for (int i = 0; i < 2; i++)
                    NSNotificationCenter.DefaultCenter.PostNotificationName(globalName, NSApplication.SharedApplication);
            }
            finally { _backgroundDepth--; }
            Barrier();
            Observed.Add(new { phase = _phase, global = _global, local = _local, survivorLocal = _survivorLocal,
                explicitGlobal = _ownGlobal, explicitLocal = _ownLocal, explicitSurvivorLocal = _ownOther,
                backgroundGlobal = _backgroundGlobal, backgroundLocal = _backgroundLocal, backgroundSurvivorLocal = _backgroundOther,
                closeAttempts = _closeAttempts, closed = _closed, targetHandle = (long)_target.Handle,
                targetVisible = _nativeTarget.IsVisible, survivorHandle = (long)_survivor.Handle });
            Require(_backgroundGlobal == 2 * ((targetLive ? 1 : 0) + (otherLive ? 1 : 0)) &&
                    _backgroundLocal == (targetLive ? 2 : 0) && _backgroundOther == (otherLive ? 2 : 0),
                _phase + ": both global display posts reach every live native window exactly once");
        }

        internal void Execute()
        {
            Require(Application.Current == null, "no managed Application before monitor checks");
            var context = RenderContext.GetOrCreateCurrent(Backend, forceReplace: true);
            if (Backend == RenderBackend.Metal) context.DefaultRenderingEngine = RenderingEngine.Impeller;
            var theme = Jalium.UI.Markup.ThemeLoader.LoadGenericTheme();
            Require(theme != null, "public Generic loader supplies the real control theme");
            _target = Open("Jalium Window v319 · 关闭目标", theme!, out _, out _);
            _survivor = Open("Jalium Window v319 · 保留窗口", theme!, out _editor, out _status);
            _nativeTarget = Native(_target); Barrier();
            Require(_windows.All(w => w.RenderTarget?.Backend == Backend), "both windows use the requested actual backend");
            Require(_windows.All(w => Native(w).IsVisible), "both actual native windows are visible");
            Capture(_target, "initial-target"); Capture(_survivor, "initial-survivor"); Barrier();
            Watch(_target); Watch(_survivor);
            _target.SystemSettingsChanged += LocalSettings;
            _survivor.SystemSettingsChanged += LocalSettings;
            _target.Closed += (_, _) => _closed++;
            EventHandler<CancelEventArgs> closing = (_, e) => e.Cancel = Cancel;
            _target.Closing += closing;
            PropertyChangedEventHandler changed = (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName)) return;
                _global++;
                if (_delivery is { Type: PlatformEventType.MonitorsChanged } frame)
                {
                    frame.Global++;
                    if (frame.Own) _ownGlobal++;
                    if (frame.Background) _backgroundGlobal++;
                    if (frame.Own && frame.Window == _target && _phase == "closing" && _closeAttempts == 0)
                    { _closeAttempts++; _target.Close(); }
                    frame.ExpectedLocal = frame.Window.Handle != 0 ? 1 : 0;
                }
                Trace("global");
            };
            SystemParameters.StaticPropertyChanged += changed;
            try
            {
                _phase = "normal"; Post();
                Require(_ownGlobal == 1, "one explicit native monitor notification reaches global settings");
                Require(_ownLocal == 1 && _ownOther == 0, "normal native monitor delivery stays on its target");
                Require(Application.Current == null, "normal monitor delivery does not construct an Application");
                Reset(); _phase = "closing"; Post();
                Require(_closeAttempts == 1, "global monitor subscriber attempts exactly one close");
                Require(_closed == (Cancel ? 0 : 1), "the real close decision is respected");
                Require(_ownLocal == (Cancel ? 1 : 0), "only a surviving target receives the local settings event");
                Require(Cancel ? _target.Handle != 0 && _nativeTarget.IsVisible : _target.Handle == 0 && !_nativeTarget.IsVisible,
                    "native target lifetime agrees with the closing decision");
                Require(_survivor.Handle != 0 && Native(_survivor).IsVisible && _ownOther == 0,
                    "the other native window remains live without the target notification");
                Require(_editor.Text == "ab🙂中文 Miii" && _editor.SelectedText == "中文" && _editor.SelectionStart == 4 && _editor.SelectionLength == 2,
                    "the surviving editor retains its Unicode text and selection");
                Require(Application.Current == null, "closing decisions preserve standalone hosting");
                Reset(); _phase = "after"; Post();
                Require(_ownGlobal == (Cancel ? 1 : 0), "retired native delegates stop global publication; canceled windows keep it");
                Require(_ownLocal == (Cancel ? 1 : 0) && _ownOther == 0, "later native delivery follows the retained target lifetime");
                Require(_closed == (Cancel ? 0 : 1) && _editor.Text == "ab🙂中文 Miii", "later delivery preserves the close result and other editor");
                Require(FrameFailures.Count == 0 && _monitorFrames >= (Cancel ? 15 : 10),
                    "every native settings delivery retains its owner, global publication and live local notification");
                _status.Text = Cancel ? "取消关闭后仍正常通知" : "关闭后已停止旧通知";
                Capture(_survivor, "after-survivor");
            }
            finally
            {
                SystemParameters.StaticPropertyChanged -= changed;
                _target.Closing -= closing;
                _target.SystemSettingsChanged -= LocalSettings; _survivor.SystemSettingsChanged -= LocalSettings;
                foreach (var pair in _handlers) if (pair.Key.NativeHandle != 0) pair.Key.SetEventHandler(pair.Value);
                _handlers.Clear();
            }
        }

        internal void Cleanup()
        {
            // Native close can pump a nested loop. Exercise that path after retiring the timer.
            using var until = NSDate.FromTimeIntervalSinceNow(.10); NSRunLoop.Main.RunUntil(until);
            foreach (var window in _windows.AsEnumerable().Reverse()) window.Close();
            Require(_windows.All(w => w.Handle == 0) && Application.Current == null, "all native windows close without an Application");
        }
    }
}
