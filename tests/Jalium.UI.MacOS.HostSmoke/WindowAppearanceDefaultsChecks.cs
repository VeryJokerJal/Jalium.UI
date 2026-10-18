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

internal static class WindowAppearanceDefaultsChecks
{
    internal static int Run(string argument)
    {
        int index = int.Parse(argument.Split('=').Last());
        if ((uint)index >= 24) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_APPEARANCE_OUTPUT")
            ?? throw new InvalidOperationException("Set the owned appearance output directory.");
        Directory.CreateDirectory(output);
        var native = NSApplication.SharedApplication; native.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        var original = native.Appearance; native.Appearance = Appearance(false);
        ThemeMode mode = (index / 4) switch { 0 => ThemeMode.None, 1 => ThemeMode.System, 2 or 4 => ThemeMode.Light, _ => ThemeMode.Dark };
        using var host = new DefaultsDelegate(mode); native.Delegate = host; native.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, native)) host.DidFinishLaunching(note);
        native.BeginInvokeOnMainThread(() =>
        {
            var scene = new Scene(index, output, native, host); Exception? failure = null;
            try { scene.Execute(); } catch (Exception error) { failure = error; }
            finally
            {
                try { scene.Cleanup(); } catch (Exception error) { failure ??= error; }
                native.Appearance = original;
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { index, passed = failure == null, checks = scene.Checks, failure = failure?.ToString(), scene.Observed,
                processId = Environment.ProcessId, physicalInput = false, desktopScreenshot = false, offscreenRendering = true }));
            host.AllowQuit = true; native.Terminate(native);
        });
        native.Run(); return 0;
    }

    private sealed class Scene(int index, string output, NSApplication native, DefaultsDelegate host)
    {
        private readonly List<(Window Window, ThemeMode Mode)> _windows = [];
        private readonly ResourceDictionary _expected = new();
        private TextBox? _editor;
        private nint _responder;
        private bool Custom => index % 2 == 1;
        private RenderBackend Backend => index % 4 < 2 ? RenderBackend.Metal : RenderBackend.Software;
        private Window Main => _windows[0].Window;
        internal List<string> Checks { get; } = [];
        internal Dictionary<string, object?> Observed { get; } = [];
        internal void Execute()
        {
            var context = RenderContext.GetOrCreateCurrent(Backend); context.DefaultRenderingEngine = RenderingEngine.Impeller;
            Require(context.Backend == Backend, "actual backend matches the requested case");
            var app = Application.Current!;
            foreach (var dictionary in app.Resources.MergedDictionaries) _expected.MergedDictionaries.Add(dictionary);
            Require(app.ThemeMode == host.Mode, "application starts in the requested mode, including untouched default None");
            foreach (var mode in new[] { ThemeMode.None, ThemeMode.System, ThemeMode.Light, ThemeMode.Dark }) MakeWindow(mode);
            foreach (var pair in _windows.Skip(1)) pair.Window.Show();
            Main.Show(); Focus();
            Observed["initialApplicationMode"] = app.ThemeMode.ToString();
            Observed["initialCurrentTheme"] = ThemeManager.CurrentTheme.ToString();
            Observed["initialSystemDark"] = SystemParameters.PrefersDarkColorScheme;
            Observed["initialInheritedBackground"] = (Main.Background as SolidColorBrush)?.Color.ToString();
            Observed["initialExpectedBackground"] = Expected(SystemParameters.HighContrast ? "HighContrast" : ExpectedTheme(ThemeMode.None)).ToString();
            Capture("initial", includeSystem: false);
            Require(AllMatch(), "first shown windows and application use the current appearance");
            Preserved();
            Switch(true, "dark"); Capture("dark", includeSystem: true);
            Switch(false, "light"); Capture("light", includeSystem: true);
            if (index >= 16)
            {
                app.ThemeMode = ThemeMode.None;
                Require(app.ThemeMode == ThemeMode.None && AllMatch(), "switching a pinned application to None applies the current system appearance");
                Switch(true, "resumed-dark"); Switch(false, "resumed-light");
            }
            Main.ThemeMode = ThemeMode.Light; _windows[0] = (Main, ThemeMode.Light);
            Switch(true, "window-pinned");
            Main.ThemeMode = ThemeMode.None; _windows[0] = (Main, ThemeMode.None);
            Require(AllMatch(), "restoring window None inherits the current application palette");
            Preserved();
            Main.Hide(); Switch(false, "hidden-light");
            Require(!Main.IsVisible && !Native(Main).IsVisible, "appearance changes keep the inherited window hidden");
            Main.Show();
            Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "showing the themed window preserves text and UTF-16 selection");
            Focus(); Require(AllMatch(), "reopened inherited window uses the current palette");
            Preserved();
            Observed["finalApplicationMode"] = app.ThemeMode.ToString();
            Observed["finalCurrentTheme"] = ThemeManager.CurrentTheme.ToString();
            Observed["backend"] = context.Backend.ToString(); Observed["customTitleBar"] = Custom;
            host.Stop();
            Require(Application.Current == null && _windows.All(x => x.Window.Handle == 0), "host shutdown releases the application and all four windows");
            var monitor = typeof(JaliumMacApplication).Assembly.GetType("Jalium.UI.MacOS.MacOSWindowThemes")!;
            Require((int)monitor.GetField("_leases", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! == 0,
                "default theme scenarios release all appearance monitoring leases");
        }
        private void MakeWindow(ThemeMode mode)
        {
            string caption = mode == ThemeMode.None ? "继承应用" : mode == ThemeMode.System ? "跟随系统" : mode == ThemeMode.Light ? "固定亮色" : "固定暗色";
            var window = new Window { Title = "Jalium 默认外观 · " + caption, Width = Custom ? 420 : 760,
                Height = 360, MinWidth = 400, MinHeight = 280, ShowActivated = mode == ThemeMode.None,
                TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            // The inherited window deliberately leaves its ThemeMode unset.
            if (mode != ThemeMode.None) window.ThemeMode = mode;
            window.SetResourceReference(Window.BackgroundProperty, "WindowBackground");
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
            TextBlock Text(string value, double size)
            { var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap }; text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush"); return text; }
            panel.Children.Add(Text("窗口外观与默认主题", 24)); panel.Children.Add(Text(caption + "，切换外观后继续编辑。", 15));
            var editor = new TextBox { Text = "中文🙂，继续编辑", Height = 40 }; panel.Children.Add(editor);
            var action = new Button { Content = "继续编辑", Height = 44 }; action.Click += (_, _) => editor.Focus(); panel.Children.Add(action);
            window.Content = panel; _windows.Add((window, mode));
            if (mode == ThemeMode.None) _editor = editor;
        }
        private string ExpectedTheme(ThemeMode windowMode)
        {
            bool dark = SystemParameters.PrefersDarkColorScheme;
            var mode = windowMode == ThemeMode.None ? Application.Current!.ThemeMode : windowMode;
            return mode == ThemeMode.Light ? "Light" : mode == ThemeMode.Dark ? "Dark" : dark ? "Dark" : "Light";
        }
        private bool AllMatch()
        {
            string application = ExpectedTheme(ThemeMode.None);
            if (ThemeManager.CurrentTheme.ToString() != application) return false;
            foreach (var pair in _windows)
            {
                string expected = SystemParameters.HighContrast ? "HighContrast" : ExpectedTheme(pair.Mode);
                string appearance = expected == "HighContrast"
                    ? SystemParameters.PrefersDarkColorScheme ? NSAppearance.NameAccessibilityHighContrastDarkAqua : NSAppearance.NameAccessibilityHighContrastAqua
                    : expected == "Dark" ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua;
                if (pair.Window.Background is not SolidColorBrush brush || brush.Color != Expected(expected) || Native(pair.Window).Appearance?.Name != appearance) return false;
            }
            return true;
        }
        private void Switch(bool dark, string phase)
        {
            native.Appearance = Appearance(dark);
            var watch = Stopwatch.StartNew();
            while ((SystemParameters.PrefersDarkColorScheme != dark || !AllMatch()) && watch.Elapsed.TotalSeconds < 5)
                NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
            Observed["phase"] = phase; Observed["systemDark"] = SystemParameters.PrefersDarkColorScheme;
            Observed["currentTheme"] = ThemeManager.CurrentTheme.ToString();
            Observed["applicationMode"] = Application.Current!.ThemeMode.ToString();
            Observed["backgrounds"] = _windows.Select(x => (x.Window.Background as SolidColorBrush)?.Color.ToString()).ToArray();
            Require(SystemParameters.PrefersDarkColorScheme == dark && AllMatch(), "all four window scopes and the application match appearance: " + phase);
            if (Main.IsVisible) Preserved();
            else Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2,
                "hidden editor retains its text and UTF-16 selection");
        }
        private void Focus()
        { Main.Activate(); Require(_editor!.Focus(), "inherited editor accepts programmatic focus"); _editor.Select(2, 2); _responder = Native(Main).FirstResponder!.Handle; }
        private void Preserved()
        {
            Require(ReferenceEquals(Keyboard.FocusedElement, _editor), "appearance changes preserve logical editor focus");
            Require(native.KeyWindow?.FirstResponder?.Handle == _responder, "appearance changes preserve the native first responder");
            Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "appearance changes preserve Chinese text and UTF-16 selection");
        }
        private Color Expected(string theme)
        {
            var seen = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
            return (Find(_expected) as SolidColorBrush)?.Color ?? throw new InvalidOperationException("Missing semantic background.");
            object? Find(ResourceDictionary dictionary)
            {
                if (!seen.Add(dictionary)) return null;
                if (dictionary.TryGetOwnValue("WindowBackground", out var own)) return own;
                if (dictionary.ThemeDictionaries.TryGetValue(theme, out var branch) && Find(branch) is { } selected) return selected;
                for (int i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--) if (Find(dictionary.MergedDictionaries[i]) is { } merged) return merged;
                return null;
            }
        }
        private void Capture(string phase, bool includeSystem)
        {
            if (index is not (0 or 3)) return;
            foreach (var pair in _windows.Take(includeSystem ? 2 : 1))
            {
                var window = pair.Window; window.UpdateLayout(); double dpi = window.DpiScale;
                var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * dpi),
                    (int)Math.Ceiling(window.ActualHeight * dpi), dpi,
                    drawing => { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(drawing); }, () => window.Handle != 0)
                    ?? throw new InvalidOperationException("Own backend readback failed.");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, phase + "-" + pair.Mode + ".png")); encoder.Save(file);
            }
        }
        private void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); Checks.Add(message); }
        internal void Cleanup()
        { foreach (var pair in _windows) if (pair.Window.Handle != 0) pair.Window.Close(); host.Stop(); }
    }
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window ?? throw new InvalidOperationException("Native window is unavailable.");
    private static NSAppearance Appearance(bool dark)
    { using var name = new NSString(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua); return NSAppearance.GetAppearance(name)!; }
    private sealed class DefaultsDelegate(ThemeMode mode) : NSApplicationDelegate
    {
        private bool _started; private JaliumApp? _host;
        internal ThemeMode Mode => mode;
        internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification note)
        {
            if (_started) return; _started = true;
            _host = AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build().UseApplication(() =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if (mode != ThemeMode.None) app.ThemeMode = mode;
                return app;
            });
            _host.StartHosted();
        }
        internal void Stop() { var host = _host; _host = null; host?.StopHosted(); }
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
        public override void WillTerminate(NSNotification note) => Stop();
    }
}
