using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ObjCRuntime;
using System.Text.Json;
using System.Diagnostics;

namespace Jalium.UI.MacOS;

internal static class WindowHostedFailureChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 48) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_HOSTED_FAILURE_OUTPUT")
            ?? throw new InvalidOperationException("Set the owned host output directory.");
        Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        app.FinishLaunching();
        app.BeginInvokeOnMainThread(() =>
        {
            var scene = new Scene(index, output);
            Exception? failure = null;
            try { scene.Execute(); }
            catch (Exception error) { failure = error; }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, passed = failure == null, stage = index < 32 ? index / 2 % 8 : index < 36 ? 8 : index < 40 ? 9 : index < 44 ? 10 : 11,
                custom = index % 2 == 1, backend = (index < 32 ? index < 16 : index % 4 < 2) ? "Metal" : "Software",
                checks = scene.Checks, failure = failure?.ToString(), scene.Observed,
                processId = Environment.ProcessId,
                physicalInput = false, desktopScreenshot = false
            }));
            app.Terminate(app);
        });
        app.Run();
        return 0;
    }

    private sealed class Scene(int index, string output)
    {
        private readonly Exception _primary = new InvalidOperationException("primary host callback");
        private readonly Exception _secondary = new InvalidOperationException("secondary cleanup callback");
        private readonly List<Window> _windows = [];
        private readonly Dictionary<Window, int> _closing = [], _closed = [];
        private readonly List<nint> _nativeWindows = [];
        private Application? _application;
        private JaliumApp? _wrapper;
        private TextBox? _editor;
        private int _exitCount;
        private bool _exitCreationRejected;
        private int Stage => index < 32 ? index / 2 % 8 : index < 36 ? 8 : index < 40 ? 9 : index < 44 ? 10 : 11;
        private bool Custom => index % 2 == 1;
        private RenderBackend Backend => (index < 32 ? index < 16 : index % 4 < 2) ? RenderBackend.Metal : RenderBackend.Software;
        internal List<string> Checks { get; } = [];
        internal Dictionary<string, object?> Observed { get; } = [];

        internal void Execute()
        {
            RenderContext.GetOrCreateCurrent(Backend).DefaultRenderingEngine = RenderingEngine.Impeller;
            var host = new Host();
            if (Stage == 2) host.OnStart = () => throw _primary;
            if (Stage == 6) { host.OnStop = () => throw _secondary; host.OnDispose = () => throw _secondary; }
            Action<Application>[] configure = Stage == 1 ? [_ => throw _primary] : [];
            var wrapper = new JaliumApp(host, configure, []).UseApplication(CreateApplication);
            Exception? observedError = null;
            _wrapper = wrapper;
            if (Stage == 10) host.OnStart = () => wrapper.StopHosted(29);
            int expectedExit = Stage is 5 or 6 ? 23 : Stage == 7 ? 41 : Stage == 8 ? 17 : Stage == 9 ? 19 : Stage == 10 ? 29 : Stage == 11 ? 31 : 1;
            try
            {
                wrapper.StartHosted();
                Require(Stage >= 5, "the requested startup failure is observed");
                if (Stage <= 9)
                {
                    Require(_windows.All(window => window.Handle != 0 && window.IsVisible), "all startup windows were shown");
                    Require(_editor!.Text == "中文🙂，回调后继续编辑", "live editor text is available before stop");
                }
                if (Stage == 7)
                {
                    Exception? wrongThreadError = null;
                    var worker = new Thread(() => { try { wrapper.StopHosted(999); } catch (Exception error) { wrongThreadError = error; } });
                    worker.Start(); Require(worker.Join(3000), "cross-thread stop returns without a dispatcher deadlock");
                    Require(wrongThreadError is InvalidOperationException, "cross-thread stop is rejected");
                    Require(host.DisposeCount == 0 && ReferenceEquals(Application.Current, _application) && _windows.All(window => window.Handle != 0),
                        "a rejected cross-thread stop leaves the UI and host running");
                }
                if (Stage == 8)
                {
                    bool invoked = false, servicesLiveInsideRender = false;
                    Exception? renderError = null;
                    ((RenderStopWindow)_windows[0]).StopFromRender = () =>
                    {
                        invoked = true;
                        try
                        {
                            wrapper.StopHosted(17);
                            servicesLiveInsideRender = host.StopCount == 0 && host.DisposeCount == 0 &&
                                ReferenceEquals(Application.Current, _application) && RenderContext.Current != null;
                        }
                        catch (Exception error) { renderError = error; }
                    };
                    _windows[0].InvalidateVisual();
                    _windows[0].ForceRenderFrame();
                    Require(invoked, "host stop was requested inside the actual render callback");
                    Require(renderError == null, "render callback host stop completes without an error");
                    Require(servicesLiveInsideRender, "host and renderer remain alive until the render callback exits");
                }
                else if (Stage == 9)
                {
                    _windows[0].Closing += (_, _) => wrapper.StopHosted(19);
                    _windows[0].Close();
                }
                else if (Stage <= 9) wrapper.StopHosted(7);
            }
            catch (Exception error) { observedError = error; }
            if (Stage == 8)
            {
                var wait = Stopwatch.StartNew();
                while (Application.Current != null && wait.Elapsed.TotalSeconds < 6)
                    NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
            }
            Observed["error"] = observedError?.ToString();
            Observed["currentRetained"] = ReferenceEquals(Application.Current, _application);
            Observed["nativeHandles"] = _windows.Select(window => (long)window.Handle).ToArray();
            Observed["windows"] = _windows.Select(window => new { handle = (long)window.Handle, window.Visibility,
                window.IsVisible, closing = _closing[window], closed = _closed[window] }).ToArray();
            Observed["hostStart"] = host.StartCount;
            Observed["hostStop"] = host.StopCount;
            Observed["hostDispose"] = host.DisposeCount;
            Require(Stage >= 7 ? observedError == null : ReferenceEquals(observedError, _primary), "the original callback error is preserved");
            Require(Application.Current == null, "the failed or stopped application releases Current");
            Require(_application!.MainWindow == null, "the application releases MainWindow");
            Require(_application.Services == null && _application.Configuration == null && _application.HostEnvironment == null,
                "host service references are detached");
            Require(ResourceLookup.ApplicationResourceLookup == null && ResourceLookup.ApplicationResourceLookupWithSource == null,
                "application resource callbacks are detached");
            Require(_windows.All(window => window.Handle == 0 && !window.IsVisible), "shown and unshown windows complete teardown");
            bool oldNativeVisible = false;
            NSApplication.SharedApplication.EnumerateWindows(0, (NSWindow window, ref bool stop) =>
            {
                if (_nativeWindows.Contains(window.Handle) && window.IsVisible) { oldNativeVisible = true; stop = true; }
            });
            Require(!oldNativeVisible, "old native windows are no longer visible");
            Require(_windows.All(window => _closing[window] == 1 && _closed[window] == 1), "Closing and Closed run once despite cancellation");
            Require(_exitCount == (Stage <= 2 || Stage == 10 ? 0 : 1), "Exit only runs for application startup that began");
            Require(Stage is not (5 or 6) || _exitCreationRejected, "Exit cannot create a new window while stopping");
            Require(host.StartCount == (Stage <= 1 ? 0 : 1), "generic host start is attempted once when reached");
            Require(host.StopCount == (Stage <= 1 ? 0 : 1), "a partially started generic host is stopped once");
            Require(host.DisposeCount == 1, "the generic host is disposed once");
            Require(wrapper.StopHosted(99) == expectedExit && wrapper.StopHosted(42) == expectedExit,
                "repeated stop preserves the stored exit result");
            wrapper.Dispose();
            Require(host.DisposeCount == 1, "repeated Dispose does not release the host twice");
            foreach (var window in _windows) Require(Rejects(window.Show), "a stopped window cannot be shown again");
            Recover();
        }

        private Application CreateApplication()
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _application = application;
            var main = MakeWindow(Stage == 4, passive: false);
            var owned = MakeWindow(false, passive: true); owned.Owner = main;
            var independent = MakeWindow(false, passive: true);
            application.MainWindow = main;
            if (Stage == 11) application.StartupUri = new Uri("must-not-load-after-stop.jalxaml", UriKind.Relative);
            application.Startup += (_, _) =>
            {
                if (Stage == 11) { _wrapper!.StopHosted(31); return; }
                if (Stage == 3 || Stage >= 5)
                {
                    foreach (var window in _windows) window.Show();
                    _editor!.Select(2, 2);
                    if (Stage == 3) throw _primary;
                }
            };
            application.Exit += (_, args) =>
            {
                _exitCount++;
                using var gate = new QuitGateDelegate();
                Require(gate.ApplicationShouldTerminate(NSApplication.SharedApplication) == NSApplicationTerminateReply.Cancel,
                    "nested native quit is rejected while the host is stopping");
                Require(!gate.ApplicationShouldHandleReopen(NSApplication.SharedApplication, false),
                    "Dock reopen cannot revive a stopping host");
                gate.MarkStopped();
                var stoppedReply = gate.ApplicationShouldTerminate(NSApplication.SharedApplication);
                Observed["stoppedDelegateQuit"] = stoppedReply.ToString();
                Require(stoppedReply == NSApplicationTerminateReply.Cancel,
                    "a stopped SDK delegate still rejects quit while application cleanup is pending");
                Require(!gate.ApplicationShouldHandleReopen(NSApplication.SharedApplication, false),
                    "a stopped SDK delegate cannot reopen during cleanup");
                if (Stage is 5 or 6)
                {
                    args.ApplicationExitCode = 23;
                    _exitCreationRejected = Rejects(() => new Window().Show());
                    throw _primary;
                }
                if (Stage == 7) args.ApplicationExitCode = 41;
            };
            if (Stage == 0) throw _primary;
            return application;
        }

        private Window MakeWindow(bool throwAfterShow, bool passive)
        {
            Window window = throwAfterShow ? new ThrowingWindow(_primary) : Stage == 8 && !passive ? new RenderStopWindow() : new Window();
            window.Title = "Jalium 宿主恢复";
            window.Width = 520; window.Height = 360; window.MinWidth = 400; window.MinHeight = 300;
            window.ShowActivated = !passive;
            window.TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native;
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "窗口恢复", FontSize = 22 });
            panel.Children.Add(new TextBlock { Text = "中文与 emoji 内容将在恢复后保留。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
            var editor = new TextBox { Text = "中文🙂，回调后继续编辑", Height = 38 };
            AutomationProperties.SetName(editor, "恢复编辑框");
            panel.Children.Add(editor);
            window.Content = panel;
            if (!passive) _editor = editor;
            _windows.Add(window); _closing[window] = _closed[window] = 0;
            window.SourceInitialized += (_, _) =>
            {
                var native = Runtime.GetNSObject<NSView>(window.Handle)?.Window;
                if (native != null) _nativeWindows.Add(native.Handle);
            };
            window.Closing += (_, args) => { _closing[window]++; args.Cancel = true; if (Stage == 7) window.Close(); };
            window.Closed += (_, _) => { _closed[window]++; if (Stage == 6 && ReferenceEquals(window, _windows[0])) throw _secondary; };
            return window;
        }

        private void Recover()
        {
            RenderContext.GetOrCreateCurrent(Backend).DefaultRenderingEngine = RenderingEngine.Impeller;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var host = new Host();
            var wrapper = new JaliumApp(host, [], []).UseApplication(app);
            var editor = new TextBox { Text = "恢复后的中文🙂", Height = 38 };
            var button = new Button { Content = "继续编辑", Height = 40 };
            button.Click += (_, _) => editor.Text += "，可继续";
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "应用已恢复", FontSize = 22 });
            panel.Children.Add(editor); panel.Children.Add(button);
            var window = new Window { Title = "Jalium 宿主恢复", Width = 520, Height = 360,
                TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = panel };
            app.MainWindow = window;
            wrapper.StartHosted(); window.UpdateLayout();
            Require(window.Handle != 0 && window.IsVisible, "a new application shows a real replacement window");
            editor.Focus(); editor.Select(6, 2);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            Require(editor.Text == "恢复后的中文🙂，可继续", "replacement window editing and routed actions still work");
            if (Stage == 7)
            {
                var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * window.DpiScale),
                    (int)Math.Ceiling(window.ActualHeight * window.DpiScale), window.DpiScale,
                    drawing => { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(drawing); },
                    () => window.Handle != 0);
                Require(bitmap != null, "replacement rendering produces actual backend readback");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap!));
                using var file = File.Create(Path.Combine(output, "recovered.png")); encoder.Save(file);
            }
            Require(wrapper.StopHosted(12) == 12 && window.Handle == 0 && Application.Current == null,
                "the replacement host also stops without leftover windows");
            Require(host.StartCount == 1 && host.StopCount == 1 && host.DisposeCount == 1,
                "the replacement host has a complete single lifecycle");
        }

        private static bool Rejects(Action action)
        { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        private void Require(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); Checks.Add(message); }
    }

    private sealed class ThrowingWindow(Exception failure) : Window
    {
        public override void Show() { base.Show(); throw failure; }
    }

    private sealed class RenderStopWindow : Window
    {
        internal Action? StopFromRender;
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            Interlocked.Exchange(ref StopFromRender, null)?.Invoke();
        }
    }

    private sealed class QuitGateDelegate : JaliumMacApplicationDelegate
    {
        internal void MarkStopped() => StopHosted();
        protected override JaliumApp CreateHostedApp() => throw new InvalidOperationException("The quit probe must not start another host.");
    }

    private sealed class Host : IHost
    {
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IHostEnvironment>(new HostEnvironment()).BuildServiceProvider();
        public Action? OnStart, OnStop, OnDispose;
        public int StartCount, StopCount, DisposeCount;
        public IServiceProvider Services => _services;
        public Task StartAsync(CancellationToken token = default) { StartCount++; OnStart?.Invoke(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token = default) { StopCount++; OnStop?.Invoke(); return Task.CompletedTask; }
        public void Dispose() { DisposeCount++; _services.Dispose(); OnDispose?.Invoke(); }
    }

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Jalium hosted lifecycle checks";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
