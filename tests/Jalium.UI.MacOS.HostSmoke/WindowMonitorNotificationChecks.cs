#pragma warning disable WPF0001
using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
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
    internal const int CaseCount = 8, CheckCount = 18;
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
            var elapsed = Stopwatch.StartNew();
            _completion = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
            {
                if (elapsed.Elapsed.TotalSeconds < Math.Clamp(hold, 0, 60)) return;
                try { scene.Cleanup(); } catch (Exception error) { scene.Failures.Add("cleanup: " + error); }
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    index, passed = scene.Failures.Count == 0 && scene.Checks.Count == CheckCount,
                    scene.Checks, scene.Failures, scene.Observed, custom = scene.Custom, cancel = scene.Cancel,
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

        private void Post()
        {
            using var name = new NSString("NSWindowDidChangeScreenNotification");
            NSNotificationCenter.DefaultCenter.PostNotificationName(name, _nativeTarget); Barrier();
            Observed.Add(new { phase = _phase, global = _global, local = _local, survivorLocal = _survivorLocal,
                closeAttempts = _closeAttempts, closed = _closed, targetHandle = (long)_target.Handle,
                targetVisible = _nativeTarget.IsVisible, survivorHandle = (long)_survivor.Handle });
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
            _target.SystemSettingsChanged += (_, _) => _local++;
            _survivor.SystemSettingsChanged += (_, _) => _survivorLocal++;
            _target.Closed += (_, _) => _closed++;
            EventHandler<CancelEventArgs> closing = (_, e) => e.Cancel = Cancel;
            _target.Closing += closing;
            PropertyChangedEventHandler changed = (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName)) return;
                _global++;
                if (_phase == "closing" && _closeAttempts == 0) { _closeAttempts++; _target.Close(); }
            };
            SystemParameters.StaticPropertyChanged += changed;
            try
            {
                _phase = "normal"; Post();
                Require(_global == 1, "one explicit native monitor notification reaches global settings");
                Require(_local == 1 && _survivorLocal == 0, "normal native monitor delivery stays on its target");
                Require(Application.Current == null, "normal monitor delivery does not construct an Application");
                _global = _local = _survivorLocal = 0; _phase = "closing"; Post();
                Require(_closeAttempts == 1, "global monitor subscriber attempts exactly one close");
                Require(_closed == (Cancel ? 0 : 1), "the real close decision is respected");
                Require(_local == (Cancel ? 1 : 0), "only a surviving target receives the local settings event");
                Require(Cancel ? _target.Handle != 0 && _nativeTarget.IsVisible : _target.Handle == 0 && !_nativeTarget.IsVisible,
                    "native target lifetime agrees with the closing decision");
                Require(_survivor.Handle != 0 && Native(_survivor).IsVisible && _survivorLocal == 0,
                    "the other native window remains live without the target notification");
                Require(_editor.Text == "ab🙂中文 Miii" && _editor.SelectedText == "中文" && _editor.SelectionStart == 4 && _editor.SelectionLength == 2,
                    "the surviving editor retains its Unicode text and selection");
                Require(Application.Current == null, "closing decisions preserve standalone hosting");
                _global = _local = _survivorLocal = 0; _phase = "after"; Post();
                Require(_global == (Cancel ? 1 : 0), "retired native delegates stop global publication; canceled windows keep it");
                Require(_local == (Cancel ? 1 : 0) && _survivorLocal == 0, "later native delivery follows the retained target lifetime");
                Require(_closed == (Cancel ? 0 : 1) && _editor.Text == "ab🙂中文 Miii", "later delivery preserves the close result and other editor");
                _status.Text = Cancel ? "取消关闭后仍正常通知" : "关闭后已停止旧通知";
                Capture(_survivor, "after-survivor");
            }
            finally
            {
                SystemParameters.StaticPropertyChanged -= changed;
                _target.Closing -= closing;
            }
        }

        internal void Cleanup()
        {
            foreach (var window in _windows.AsEnumerable().Reverse()) window.Close();
            Require(_windows.All(w => w.Handle == 0) && Application.Current == null, "all native windows close without an Application");
        }
    }
}
