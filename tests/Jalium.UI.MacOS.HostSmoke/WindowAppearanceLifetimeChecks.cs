#pragma warning disable WPF0001
using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowAppearanceLifetimeChecks
{
    internal static int Run(string argument)
    {
        int index = int.Parse(argument.Split('=').Last());
        if (index >= 8) return WindowMotionPreferenceChecks.Run(index - 8);
        if (index is < 0 or >= 8) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_APPEARANCE_OUTPUT")
            ?? throw new InvalidOperationException("Set the owned appearance output directory.");
        Directory.CreateDirectory(output);
        var native = NSApplication.SharedApplication;
        native.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        var original = native.Appearance;
        native.Appearance = Appearance(NSAppearance.NameAqua);
        using var host = new ExternalDelegate(index >= 4);
        native.Delegate = host;
        native.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, native)) host.DidFinishLaunching(note);
        native.BeginInvokeOnMainThread(() =>
        {
            var scene = new Scene(index, output, native, host);
            Exception? failure = null;
            try { scene.Execute(); }
            catch (Exception error) { failure = error; }
            finally
            {
                scene.Cleanup();
                native.Appearance = original;
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { index, passed = failure == null, checks = scene.Checks, failure = failure?.ToString(), scene.Observed,
                processId = Environment.ProcessId, physicalInput = false, desktopScreenshot = false, offscreenRendering = true }));
            host.AllowQuit = true;
            native.Terminate(native);
        });
        native.Run();
        return 0;
    }

    private sealed class Scene(int index, string output, NSApplication native, ExternalDelegate host)
    {
        private readonly List<Window> _windows = [];
        private TextBox? _editor;
        private nint _responder;
        private ResourceDictionary[] _dictionaries = [];
        private ResourceDictionary _expected = new();
        private bool Custom => index % 2 == 1;
        private bool Pinned => index >= 4;
        private RenderBackend Backend => index % 4 < 2 ? RenderBackend.Metal : RenderBackend.Software;
        internal List<string> Checks { get; } = [];
        internal Dictionary<string, object?> Observed { get; } = [];

        internal void Execute()
        {
            RenderContext.GetOrCreateCurrent(Backend).DefaultRenderingEngine = RenderingEngine.Impeller;
            var application = Application.Current!;
            _dictionaries = application.Resources.MergedDictionaries.ToArray();
            foreach (var dictionary in _dictionaries) _expected.MergedDictionaries.Add(dictionary);
            var system = MakeWindow(ThemeMode.System, standalone: false);
            var pinned = MakeWindow(ThemeMode.Dark, standalone: false);
            pinned.ShowActivated = false;
            pinned.Show(); system.Show();
            Focus(system);
            WaitPalette(system, "Light"); Verify(pinned, "Dark");
            Observed["leasesBeforeChanges"] = Leases();
            Switch(system, pinned, dark: true);
            Require(Leases() >= 3, "custom host application and two native windows own appearance leases");
            Capture(system, "host-dark");
            native.Terminate(native);
            Require(host.CancelledQuit == 1 && ReferenceEquals(Application.Current, application), "cancelled native quit keeps the custom host running");
            Require(Leases() >= 3, "cancelled native quit retains appearance monitoring");
            Switch(system, pinned, dark: false);
            Capture(system, "host-light");
            system.Hide();
            native.Appearance = Appearance(NSAppearance.NameDarkAqua);
            WaitPalette(system, "Dark");
            Require(!system.IsVisible && !Native(system).IsVisible, "appearance notifications do not show a hidden window");
            system.Show();
            Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "hidden themed editor retains text and selection before focus is restored");
            Focus(system); Verify(system, "Dark");
            host.Stop();
            Require(Application.Current == null && _windows.All(w => w.Handle == 0), "stopping custom hosting releases application and its windows");
            Require(Leases() == 0 && !HasObserver(), "last hosted owner releases all native appearance observers");

            native.Appearance = Appearance(NSAppearance.NameAqua);
            Pump(.08);
            RenderContext.GetOrCreateCurrent(Backend).DefaultRenderingEngine = RenderingEngine.Impeller;
            var standalone = MakeWindow(ThemeMode.System, standalone: true);
            standalone.Show();
            Observed["standaloneInitialNativeAppearance"] = Native(standalone).Appearance?.Name;
            Observed["standaloneInitialBackground"] = (standalone.Background as SolidColorBrush)?.Color.ToString();
            Observed["standaloneExpectedBackground"] = Expected(SystemParameters.HighContrast ? "HighContrast" : "Light").ToString();
            Observed["standaloneInitialDarkPreference"] = SystemParameters.PrefersDarkColorScheme;
            Require(PaletteMatches(standalone, "Light"), "standalone first shown state uses the current appearance");
            Focus(standalone);
            WaitPalette(standalone, "Light");
            Require(Application.Current == null && Leases() == 1, "a standalone native window owns monitoring without an Application");
            native.Appearance = Appearance(NSAppearance.NameDarkAqua);
            WaitPalette(standalone, "Dark"); Preserved();
            Capture(standalone, "standalone-dark");
            standalone.Hide();
            native.Appearance = Appearance(NSAppearance.NameAqua);
            WaitPalette(standalone, "Light");
            Require(!standalone.IsVisible, "hidden standalone window tracks appearance without reopening");
            standalone.Show();
            Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "hidden standalone editor retains text and selection before focus is restored");
            Focus(standalone);
            Capture(standalone, "standalone-light");
            int notifications = 0;
            System.ComponentModel.PropertyChangedEventHandler listener = (_, _) => notifications++;
            SystemParameters.StaticPropertyChanged += listener;
            try
            {
                native.Appearance = Appearance(NSAppearance.NameDarkAqua);
                standalone.Close();
                Require(Leases() == 0 && !HasObserver(), "last standalone window releases the appearance observers");
                Pump(.2);
                Require(notifications == 0, "queued callbacks are retired when the last appearance owner closes");
            }
            finally { SystemParameters.StaticPropertyChanged -= listener; }

            var primary = new InvalidOperationException("appearance factory rollback");
            using (var failed = AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
                .UseApplication(() => { _ = new Application(); throw primary; }))
            {
                Exception? observed = null;
                try { failed.StartHosted(); } catch (Exception error) { observed = error; }
                Require(ReferenceEquals(observed, primary), "failed custom host startup preserves its original error");
                Require(Application.Current == null && Leases() == 0 && !HasObserver(), "failed custom host startup releases its appearance lease");
            }
            host.Start();
            RenderContext.GetOrCreateCurrent(Backend).DefaultRenderingEngine = RenderingEngine.Impeller;
            var replacement = MakeWindow(ThemeMode.System, standalone: false);
            replacement.Show(); Focus(replacement);
            WaitPalette(replacement, "Dark");
            native.Appearance = Appearance(NSAppearance.NameAqua);
            WaitPalette(replacement, "Light"); Preserved();
            Require(Leases() == 2, "a replacement application and its native window restart appearance monitoring once");
            host.Stop();
            Require(Application.Current == null && Leases() == 0 && !HasObserver(), "replacement shutdown releases all appearance leases");
            Observed["leasesAfterStop"] = Leases();
            Observed["backend"] = Backend.ToString();
            Observed["customTitleBar"] = Custom;
        }

        private Window MakeWindow(ThemeMode mode, bool standalone)
        {
            var window = new Window { Title = "Jalium 外观生命周期", Width = Custom ? 420 : 760, Height = 380,
                MinWidth = 400, MinHeight = 300, ThemeMode = mode,
                TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            if (standalone) foreach (var dictionary in _dictionaries) window.Resources.MergedDictionaries.Add(dictionary);
            window.SetResourceReference(Window.BackgroundProperty, "WindowBackground");
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
            var heading = new TextBlock { Text = standalone ? "独立窗口外观" : "窗口跟随系统外观", FontSize = 22, TextWrapping = TextWrapping.Wrap };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            panel.Children.Add(heading);
            var label = new TextBlock { Text = "切换外观后，文字与选区应保持。", FontSize = 15, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush"); panel.Children.Add(label);
            var editor = new TextBox { Text = "中文🙂，继续编辑", Height = 40 }; panel.Children.Add(editor);
            var action = new Button { Content = "继续编辑", Height = 42 };
            action.Click += (_, _) => editor.Focus();
            panel.Children.Add(action);
            window.Content = panel;
            _editor = editor;
            _windows.Add(window);
            return window;
        }

        private void Focus(Window window)
        {
            _editor = ((StackPanel)window.Content!).Children.OfType<TextBox>().Single();
            window.Activate();
            Until(() => _editor.Focus() && Native(window).FirstResponder != null, "native editor focus");
            _editor.Select(2, 2); _responder = Native(window).FirstResponder!.Handle;
            Require(_responder != 0, "native editor responder is present");
        }
        private void Switch(Window system, Window pinned, bool dark)
        {
            native.Appearance = Appearance(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua);
            WaitPalette(system, dark ? "Dark" : "Light"); Verify(pinned, "Dark"); Preserved();
            Require(ThemeManager.CurrentTheme == (Pinned ? ThemeVariant.Light : dark ? ThemeVariant.Dark : ThemeVariant.Light),
                "application System follows appearance while explicit Light stays pinned");
        }
        private void WaitPalette(Window window, string theme)
        {
            bool dark = theme == "Dark";
            Until(() => SystemParameters.PrefersDarkColorScheme == dark && PaletteMatches(window, theme), "palette " + theme);
            Verify(window, theme);
        }
        private bool PaletteMatches(Window window, string requested)
        {
            string theme = SystemParameters.HighContrast ? "HighContrast" : requested;
            return window.Background is SolidColorBrush brush && brush.Color == Expected(theme) &&
                Native(window).Appearance?.Name == (theme == "HighContrast" ? (SystemParameters.PrefersDarkColorScheme ? NSAppearance.NameAccessibilityHighContrastDarkAqua : NSAppearance.NameAccessibilityHighContrastAqua) : theme == "Dark" ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua);
        }
        private void Verify(Window window, string theme) => Require(PaletteMatches(window, theme), "semantic background and native appearance agree with " + theme);
        private Color Expected(string theme)
        {
            var seen = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
            return (Find(_expected) as SolidColorBrush)?.Color ?? throw new InvalidOperationException("Missing semantic background.");
            object? Find(ResourceDictionary dictionary)
            {
                if (!seen.Add(dictionary)) return null;
                if (dictionary.TryGetOwnValue("WindowBackground", out var own)) return own;
                if (dictionary.ThemeDictionaries.TryGetValue(theme, out var branch) && Find(branch) is { } branchValue) return branchValue;
                for (int i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--) if (Find(dictionary.MergedDictionaries[i]) is { } mergedValue) return mergedValue;
                return null;
            }
        }
        private void Preserved()
        {
            Require(ReferenceEquals(Keyboard.FocusedElement, _editor), "appearance changes preserve logical editing focus");
            Require(native.KeyWindow?.FirstResponder?.Handle == _responder, "appearance changes preserve native first responder");
            Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "appearance changes preserve Chinese text and UTF-16 selection");
        }
        private void Until(Func<bool> condition, string phase)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.Elapsed.TotalSeconds < 10) Pump(.02);
            Observed["lastPhase"] = phase; Observed["appActive"] = native.Active;
            Observed["frontmostPID"] = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier;
            Observed["leases"] = Leases();
            if (!condition()) throw new TimeoutException(phase);
        }
        private void Capture(Window window, string phase)
        {
            if (index is not (0 or 3)) return;
            window.UpdateLayout(); double dpi = window.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * dpi),
                (int)Math.Ceiling(window.ActualHeight * dpi), dpi,
                drawing => { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(drawing); }, () => window.Handle != 0)
                ?? throw new InvalidOperationException("Offscreen rendering failed.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, phase + ".png")); encoder.Save(file);
        }
        internal void Cleanup()
        { foreach (var window in _windows) if (window.Handle != 0) window.Close(); host.Stop(); }
        private void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); Checks.Add(message); }
    }

    private static readonly Type MonitorType = typeof(JaliumMacApplication).Assembly.GetType("Jalium.UI.MacOS.MacOSWindowThemes")!;
    private static int Leases() => (int)MonitorType.GetField("_leases", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static bool HasObserver() => MonitorType.GetField("_appearanceObserver", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) != null;
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window ?? throw new InvalidOperationException("Native window is unavailable.");
    private static NSAppearance Appearance(string name)
    { using var value = new NSString(name); return NSAppearance.GetAppearance(value) ?? throw new InvalidOperationException("Appearance unavailable."); }
    private static void Pump(double seconds) => NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(seconds));

    private sealed class ExternalDelegate(bool pinned) : NSApplicationDelegate
    {
        private bool _launched;
        private JaliumApp? _host;
        internal bool AllowQuit;
        internal int CancelledQuit;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_launched) return; _launched = true; Start(); }
        internal void Start()
        {
            _host = AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
                .UseApplication(() => new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = pinned ? ThemeMode.Light : ThemeMode.System });
            _host.StartHosted();
        }
        internal void Stop() { var host = _host; _host = null; host?.StopHosted(); }
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
        { if (AllowQuit) return NSApplicationTerminateReply.Now; CancelledQuit++; return NSApplicationTerminateReply.Cancel; }
        public override void WillTerminate(NSNotification notification) => Stop();
    }
}
