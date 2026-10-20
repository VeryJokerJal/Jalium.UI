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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;

namespace Jalium.UI.MacOS;

// Invokes our own AppKit surfaces. No physical Dock interaction is claimed.
internal static class WindowTaskbarChecks
{
    internal static int Run(bool software = false)
    {
        string output = Environment.GetEnvironmentVariable("JALIUM_TASKBAR_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_TASKBAR_OUTPUT is required.");
        Directory.CreateDirectory(output);
        void Progress(string message)
        {
            File.AppendAllText(Path.Combine(output, "progress.log"), DateTime.UtcNow.ToString("O") + " " + message + "\n");
            Console.WriteLine(message);
        }
        Progress("Run entered");
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        Progress("Before FinishLaunching");
        app.Delegate = host; app.FinishLaunching();
        Progress("After FinishLaunching");
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var context = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (!software) context.DefaultRenderingEngine = RenderingEngine.Impeller;
        var checks = new List<string>(); var failures = new List<string>();
        var savedAppView = app.DockTile.ContentView;
        string? savedBadge = app.DockTile.BadgeLabel;
        app.DockTile.BadgeLabel = "既有";
        Window? window = null, other = null;
        NSWindow? native = null;
        TaskbarItemInfo? info = null;
        WriteableBitmap? mutableOverlay = null;
        byte[]? originalOverlayTile = null;
        int completed = 0, phase = 0;
        var watch = Stopwatch.StartNew();
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add($"custom={completed == 1}: {message}");
            Progress("PASS " + message);
        }
        NSView AppTile() => app.DockTile.ContentView ?? throw new InvalidOperationException("application tile missing");
        byte[] Capture(NSView view, string name)
        {
            view.Frame = new CGRect(0, 0, 128, 128);
            using var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
            Require(bitmap != null, "AppKit allocates tile bitmap");
            view.CacheDisplay(view.Bounds, bitmap!);
            using var data = bitmap!.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
            var bytes = data!.ToArray();
            File.WriteAllBytes(Path.Combine(output, $"{completed}-{name}.png"), bytes);
            return bytes;
        }
        Window Create(string title, bool custom) => new()
        {
            Title = title, Width = 640, Height = 460,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 38, 56)), Padding = new Thickness(28),
                Child = new StackPanel { Spacing = 16, Children =
                {
                    new TextBlock { Text = "Dock 与窗口任务", FontSize = 26 },
                    new TextBlock { Text = "正在下载 · 中文🙂", FontSize = 18 },
                    new TextBox { Text = "窗口编辑内容保持可用", Height = 36 },
                    new Button { Content = "继续", Height = 36 },
                } },
            },
        };
        void Start()
        {
            window = Create("Jalium Window v253 · Dock", completed == 1);
            info = new TaskbarItemInfo { Description = "下载文档 · 中文🙂", ProgressState = TaskbarItemProgressState.Normal, ProgressValue = 0.35 };
            window.TaskbarItemInfo = info;
            Application.Current!.MainWindow = window;
            Progress("Before Show");
            window.Show(); window.UpdateLayout();
            Progress("After Show");
            native = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
            native.MakeKeyWindow();
            phase = 0; watch.Restart();
        }
        bool finished = false;
        void Finish()
        {
            other?.Close(); other = null;
            window?.Close(); window = null;
            RestoreContent(app.DockTile, savedAppView);
            app.DockTile.BadgeLabel = savedBadge;
            File.WriteAllText(Path.Combine(output, "host-result.json"), JsonSerializer.Serialize(new
            {
                completed, expected = 2, passed = checks.Count, failures, checks,
                physicalInput = false, backend = software ? "Software" : "Metal", pid = Environment.ProcessId,
                nativeLibraries = NativeLibraries(),
            }));
            finished = true;
            app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ =>
        {
            if (finished) return;
            Progress("Tick phase=" + phase);
            try
            {
                if (phase == 0)
                {
                    if (app.DockTile.ContentView == null || native!.DockTile.ContentView == null)
                    { if (watch.Elapsed.TotalSeconds < 10) return; }
                    Require(native!.MiniWindowTitle == info!.Description, "description reaches native miniwindow title");
                    Require(AppTile().AccessibilityLabel == info.Description, "application tile carries current window description");
                    Require(app.DockTile.BadgeLabel == "既有", "existing application badge is preserved");
                    foreach (var state in new[] { TaskbarItemProgressState.Normal, TaskbarItemProgressState.Indeterminate,
                        TaskbarItemProgressState.Error, TaskbarItemProgressState.Paused })
                    {
                        info.ProgressState = state;
                        Require(!string.IsNullOrEmpty(AppTile().AccessibilityValue?.ToString()), "progress has a semantic value: " + state);
                        Capture(AppTile(), "app-" + state);
                    }
                    var overlay = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
                    var pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 70, 110, 235, 255 }).ToArray();
                    overlay.WritePixels(new Int32Rect(0, 0, 16, 16), pixels, 64, 0);
                    mutableOverlay = overlay;
                    info.Overlay = overlay;
                    info.ThumbnailClipMargin = new Thickness(28, 28, 28, 150);
                    Capture(AppTile(), "app-overlay");
                    Capture(native.DockTile.ContentView!, "window-clipped");
                    int clicks = 0; var command = new TestCommand();
                    var button = new ThumbButtonInfo { Description = "暂停下载", Command = command, ImageSource = overlay };
                    button.Click += (_, _) => { clicks++; info.ProgressValue = 0.6; };
                    info.ThumbButtonInfos!.Add(button);
                    using var menu = host.ApplicationDockMenu(app);
                    Require(menu.Items.Length == 1 && menu.Items[0].Submenu?.Items[0].Title == button.Description,
                        "Dock menu groups the current window command");
                    Require(menu.Items[0].Submenu!.Items[0].Image?.Size.Width == 18, "native command icon retains standard menu size");
                    info.ProgressValue = 0.5;
                    menu.Items[0].Submenu!.PerformActionForItem(0);
                    Require(clicks == 1 && command.Executions == 1, "progress and Click updates do not retire the current command");
                    button.IsEnabled = false;
                    menu.Items[0].Submenu!.PerformActionForItem(0);
                    Require(clicks == 1 && command.Executions == 1, "disabled old menu command cannot execute");
                    button.IsEnabled = true;
                    window!.TaskbarItemInfo = new TaskbarItemInfo();
                    menu.Items[0].Submenu!.PerformActionForItem(0);
                    Require(clicks == 1 && command.Executions == 1, "old native menu cannot execute after model replacement");
                    window.TaskbarItemInfo = info;
                    CheckRoutedDockCommands(window, host, app);
                    other = Create("第二个任务", completed == 1);
                    other.TaskbarItemInfo = new TaskbarItemInfo { Description = "第二个文档", ProgressState = TaskbarItemProgressState.Error, ProgressValue = 0.8 };
                    other.Show(); other.UpdateLayout();
                    Runtime.GetNSObject<NSView>(other.Handle)!.Window!.MakeKeyWindow();
                    phase = 1; watch.Restart(); return;
                }
                if (phase == 1)
                {
                    Require(AppTile().AccessibilityLabel == "第二个文档", "key window owns the application tile");
                    other!.Close(); other = null; native!.MakeKeyWindow();
                    Require(AppTile().AccessibilityLabel == info!.Description, "closing the key task restores the remaining owner");
                    window!.ShowInTaskbar = false;
                    Require(app.DockTile.ContentView?.Handle == savedAppView?.Handle, "excluding the last task restores the application tile");
                    window.ShowInTaskbar = true;
                    native.Miniaturize(native); phase = 2; watch.Restart(); return;
                }
                if (phase == 3)
                {
                    byte[] current = Capture(AppTile(), "app-mutated-overlay");
                    if (current.AsSpan().SequenceEqual(originalOverlayTile))
                    { if (watch.Elapsed.TotalSeconds < 10) return; Require(false, "mutable overlay updates the native tile"); }
                    Require(true, "mutable overlay updates without replacing its model source");
                    var closingProbe = Create("回调内关闭", completed == 1);
                    closingProbe.Show(); closingProbe.UpdateLayout();
                    closingProbe.TaskbarItemInfo = new TaskbarItemInfo
                    { Overlay = new CallbackImage(() => closingProbe.Close()) };
                    Require(closingProbe.Handle == 0 && typeof(Window).GetField("_platformTaskbarIntegration",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(closingProbe) == null,
                        "image callback close retires an in-flight registration");
                    using var external = new NSView(new CGRect(0, 0, 128, 128));
                    app.DockTile.ContentView = external;
                    info!.ProgressValue = 0.9;
                    window!.Close(); window = null;
                    Require(app.DockTile.ContentView?.Handle == external.Handle, "close preserves another component's replacement tile");
                    RestoreContent(app.DockTile, savedAppView);
                    completed++;
                    if (completed == 2) Finish(); else Start();
                    return;
                }
                if (!native!.IsMiniaturized)
                { if (watch.Elapsed.TotalSeconds < 10) return; Require(false, "AppKit acknowledges minimization"); }
                Require(native.DockTile.ContentView != null, "minimized window retains its custom task tile");
                Capture(native.DockTile.ContentView!, "window-minimized");
                native.Deminiaturize(native);
                window!.Hide();
                Require(app.DockTile.ContentView?.Handle == savedAppView?.Handle, "hiding the last task restores the application tile");
                window.Show(); window.UpdateLayout(); native.MakeKeyWindow();
                window.TaskbarItemInfo = null;
                Require(app.DockTile.ContentView?.Handle == savedAppView?.Handle && native.DockTile.ContentView == null,
                    "clearing the task restores application and window content");
                Require(native.MiniWindowTitle == native.Title, "clearing the description restores the current window title");
                window.TaskbarItemInfo = info;
                originalOverlayTile = Capture(AppTile(), "app-original-overlay");
                var changedPixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 230, 150, 40, 255 }).ToArray();
                mutableOverlay!.WritePixels(new Int32Rect(0, 0, 16, 16), changedPixels, 64, 0);
                phase = 3; watch.Restart();
            }
            catch (Exception error)
            {
                Progress("FAIL " + error);
                failures.Add(error.ToString());
                try { Finish(); }
                catch (Exception cleanup)
                {
                    File.WriteAllText(Path.Combine(output, "emergency-failure.txt"), error + "\n" + cleanup);
                    Environment.Exit(2);
                }
            }
        });
        Progress("Before Start"); Start(); Progress("Before Run"); app.Run(); Progress("After Run");
        return failures.Count == 0 && completed == 2 ? 0 : 1;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")]
    private static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")]
    private static extern nint ImageName(uint index);
    private static Dictionary<string, string> NativeLibraries()
    {
        var result = new Dictionary<string, string>();
        for (uint i = 0; i < ImageCount(); i++)
        {
            string? path = Marshal.PtrToStringUTF8(ImageName(i));
            if (path != null && Path.GetFileName(path).StartsWith("libjalium.native.", StringComparison.Ordinal))
                result[Path.GetFileName(path)] = Path.GetFullPath(path);
        }
        return result;
    }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {
            if (_started) return;
            _started = true;
            base.DidFinishLaunching(notification);
        }
        protected override JaliumApp CreateHostedApp() =>
            AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
                .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }

    private static void RestoreContent(NSDockTile tile, NSView? view)
    {
        if (view != null) tile.ContentView = view;
        else SetNullableObject(tile.Handle, Selector.GetHandle("setContentView:"), 0);
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetNullableObject(nint receiver, nint selector, nint value);

    private sealed class CallbackImage : ImageSource
    {
        private Action? _callback;
        internal CallbackImage(Action? callback = null) => _callback = callback;
        public override double Width { get { var callback = _callback; _callback = null; callback?.Invoke(); return 16; } }
        public override double Height => 16;
        public override nint NativeHandle => 0;
        public override ImageMetadata? Metadata => null;
        protected override Freezable CreateInstanceCore() => new CallbackImage();
    }


    private static void CheckRoutedDockCommands(Window window, JaliumMacApplicationDelegate host, NSApplication app)
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        var original = window.TaskbarItemInfo;
        try
        {
            foreach (int phase in new[] { 0, 1 })
            foreach (string mutation in new[] { "replace", "remove", "progress" })
            {
                bool armed = false, changed = false;
                int queries = 0, executions = 0;
                var model = new TaskbarItemInfo();
                var command = new Jalium.UI.Input.RoutedCommand("Dock lifetime", typeof(WindowTaskbarChecks));
                ThumbButtonInfo? button = null;
                void Change()
                {
                    if (!armed || changed) return;
                    changed = true;
                    if (mutation == "replace") window.TaskbarItemInfo = new();
                    else if (mutation == "remove") model.ThumbButtonInfos!.Remove(button!);
                    else model.ProgressValue = .5;
                }
                var binding = new Jalium.UI.Input.CommandBinding(command, (_, _) => executions++, (_, args) =>
                {
                    if (armed && ++queries == 2 && phase == 0) Change();
                    args.CanExecute = true;
                });
                var preview = new Jalium.UI.Input.ExecutedRoutedEventHandler((_, _) => { if (phase == 1) Change(); });
                window.CommandBindings.Add(binding);
                window.AddHandler(Jalium.UI.Input.RoutedCommand.PreviewExecutedEvent, preview);
                try
                {
                    button = new ThumbButtonInfo { Command = command, CommandTarget = window, Description = "Dock lifetime callback" };
                    model.ThumbButtonInfos!.Add(button);
                    window.TaskbarItemInfo = model;
                    using var menu = host.ApplicationDockMenu(app);
                    Require(menu.Items.Length == 1 && menu.Items[0].Submenu?.Items[0].Title == button.Description,
                        "routed Dock menu entry missing");
                    Require(menu.Items[0].Submenu!.Items[0].Enabled, "routed Dock menu entry is disabled before invocation");
                    armed = true;
                    menu.Items[0].Submenu!.PerformActionForItem(0);
                    Require(changed && executions == (mutation == "progress" ? 1 : 0),
                        $"routed Dock action after {mutation} at phase {phase}: changed={changed}, queries={queries}, executions={executions}");
                }
                finally
                {
                    window.RemoveHandler(Jalium.UI.Input.RoutedCommand.PreviewExecutedEvent, preview);
                    window.CommandBindings.Remove(binding);
                }
                Console.WriteLine($"PASS: actual Dock routed lease {phase}/{mutation}");
            }
        }
        finally { window.TaskbarItemInfo = original; }
    }

    private sealed class TestCommand : ICommand
    {
        internal int Executions;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Executions++;
    }
}
