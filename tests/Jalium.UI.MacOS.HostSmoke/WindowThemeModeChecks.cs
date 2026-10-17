#pragma warning disable WPF0001
using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ObjCRuntime;
using System.Diagnostics;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowThemeModeChecks
{
    internal static int Run(string argument)
    {
        int[] cases = argument == "--window-themes" ? [0, 1, 2, 3] : [int.Parse(argument.Split('=')[1])];
        if (cases.Any(index => index is < 0 or > 3)) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_WINDOW_THEMES_OUTPUT")
            ?? throw new InvalidOperationException("Set an owned theme output directory.");
        Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        app.Delegate = host;
        app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        ThemeManager.Initialize(Application.Current!);
        var runner = new Runner(app, output, cases);
        using var timer = NSTimer.CreateRepeatingTimer(.04, _ => runner.Tick());
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        app.Run();
        timer.Invalidate();
        runner.Dispose();
        return runner.Passed == cases.Length ? 0 : 1;
    }

    private sealed class Runner(NSApplication app, string output, int[] cases) : IDisposable
    {
        private readonly List<object> _results = [];
        private readonly List<string> _checks = [];
        private readonly Stopwatch _phaseTime = Stopwatch.StartNew();
        private readonly Stopwatch _lifetime = Stopwatch.StartNew();
        private readonly NSAppearance? _originalAppearance = app.Appearance;
        private readonly ThemeMode _originalMode = Application.Current!.ThemeMode;
        private readonly ThemeVariant _originalTheme = ThemeManager.CurrentTheme;
        private int _position;
        private int _phase = -1;
        private Window? _left, _right;
        private TextBox? _editor;
        private Button? _anchor;
        private TextBlock? _caption;
        private Popup? _popup;
        private Border? _popupBody;
        private nint _responder;
        private bool _inPanel;
        private bool _finished;
        internal int Passed { get; private set; }
        private int Index => cases[_position];
        private bool Custom => Index % 2 == 1;
        private int Width => Index < 2 ? 760 : 420;

        internal void Tick()
        {
            if (_finished || _inPanel) return;
            try
            {
                if (_lifetime.Elapsed.TotalSeconds > 120) throw new TimeoutException("Theme host lifetime expired.");
                if (_phase == -1) { BeginCase(); return; }
                switch (_phase)
                {
                    case 0:
                        if (!_left!.IsActive || !_editor!.Focus()) { Await("editor focus"); return; }
                        _editor.Select(2, 2);
                        _responder = app.KeyWindow?.FirstResponder?.Handle ?? 0;
                        Require(_responder != 0, "native editor responder is installed");
                        Verify(_left, "Light"); Verify(_right!, "Dark");
                        Capture(_left, "light"); Capture(_right!, "dark");
                        _left.ThemeMode = ThemeMode.Dark;
                        Next(); break;
                    case 1:
                        Verify(_left!, "Dark"); Verify(_right!, "Dark"); FocusPreserved();
                        _popupBody = new Border { Width = 300, Padding = new Thickness(14), Child = Text("来自当前窗口的弹出说明", 15) };
                        _popupBody.SetResourceReference(Border.BackgroundProperty, "WindowBackground");
                        _popup = new Popup { PlacementTarget = _anchor, Placement = PlacementMode.Bottom,
                            StaysOpen = true, PreferExternalWindow = true, Child = _popupBody, IsOpen = true };
                        Next(); break;
                    case 2:
                        var physical = Window.GetMacOSPhysicalWindow(_popupBody!);
                        if (physical?.Handle == 0 || physical == null || _popupBody!.ActualWidth == 0)
                        { Await("popup host and layout"); return; }
                        Require(physical.IsMacOSPopupHost, "popup uses a detached native host");
                        Require(!Native(physical).IsKeyWindow, "informational popup remains non-key");
                        Verify(physical, "Dark");
                        _left!.ThemeMode = ThemeMode.Light;
                        Next(); break;
                    case 3:
                        Verify(_left!, "Light"); Verify(_right!, "Dark");
                        Verify(Window.GetMacOSPhysicalWindow(_popupBody!)!, "Light");
                        Require(BrushColor(_popupBody!.Background) == Expected("Light", "WindowBackground"), "open popup follows its owner palette");
                        FocusPreserved();
                        _popup!.IsOpen = false;
                        _left!.TitleBarStyle = Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
                        _left.UpdateLayout();
                        Next(); break;
                    case 4:
                        Verify(_left!, "Light"); FocusPreserved();
                        _left!.Hide();
                        nint keyBefore = app.KeyWindow?.Handle ?? 0;
                        _left.ThemeMode = ThemeMode.Dark;
                        Require(!_left.IsVisible && !Native(_left).IsVisible, "changing a hidden theme does not show the window");
                        Require((app.KeyWindow?.Handle ?? 0) == keyBefore, "changing a hidden theme does not select a key window");
                        Verify(_left, "Dark");
                        _left.Show();
                        Next(); break;
                    case 5:
                        if (!_left!.IsActive || !_editor!.Focus()) { Await("reopened editor focus"); return; }
                        Require(_editor.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2,
                            "hide and show retain text and selection");
                        _responder = app.KeyWindow?.FirstResponder?.Handle ?? 0;
                        CancelQuit();
                        _left.ThemeMode = ThemeMode.System;
                        Application.Current!.ThemeMode = ThemeMode.Dark;
                        app.Appearance = Appearance(NSAppearance.NameAqua);
                        Next(); break;
                    case 6:
                        if ((SystemParameters.PrefersDarkColorScheme ? 1u : 2u) != 2) { Await("light appearance notification"); return; }
                        Verify(_left!, "Light"); Verify(_right!, "Dark"); FocusPreserved();
                        Require(ThemeManager.CurrentTheme == ThemeVariant.Dark, "an explicit application theme remains pinned");
                        app.Appearance = Appearance(NSAppearance.NameDarkAqua);
                        Next(); break;
                    case 7:
                        if ((SystemParameters.PrefersDarkColorScheme ? 1u : 2u) != 1) { Await("dark appearance notification"); return; }
                        Verify(_left!, "Dark"); Verify(_right!, "Dark"); FocusPreserved();
                        Application.Current!.ThemeMode = ThemeMode.Light;
                        _left!.ThemeMode = ThemeMode.None;
                        Next(); break;
                    case 8:
                        Verify(_left!, "Light"); Verify(_right!, "Dark"); FocusPreserved();
                        _left!.ThemeMode = ThemeMode.Dark;
                        app.Appearance = Appearance(NSAppearance.NameAqua);
                        Next(); break;
                    case 9:
                        if ((SystemParameters.PrefersDarkColorScheme ? 1u : 2u) != 2) { Await("panel light process appearance"); return; }
                        CheckPanel();
                        Application.Current!.ThemeMode = ThemeMode.System;
                        app.Appearance = Appearance(NSAppearance.NameDarkAqua);
                        Next(); break;
                    case 10:
                        if (!SystemParameters.PrefersDarkColorScheme) { Await("application dark notification"); return; }
                        Require(ThemeManager.CurrentTheme == ThemeVariant.Dark, "a system application theme follows dark appearance");
                        Verify(_left!, "Dark"); Verify(_right!, "Dark");
                        app.Appearance = Appearance(NSAppearance.NameAqua);
                        Next(); break;
                    case 11:
                        if (SystemParameters.PrefersDarkColorScheme) { Await("application light notification"); return; }
                        Require(ThemeManager.CurrentTheme == ThemeVariant.Light, "a system application theme follows light appearance");
                        Verify(_left!, "Dark"); Verify(_right!, "Dark");
                        _left!.UpdateLayout();
                        Capture(_left, "switched");
                        Complete(null); return;
                }
                SaveState();
            }
            catch (Exception error) { Complete(error); }
        }

        private void BeginCase()
        {
            _checks.Clear();
            _left = MakeWindow(ThemeMode.Light, false);
            _right = MakeWindow(ThemeMode.Dark, true);
            Application.Current!.MainWindow = _left;
            _left.Show(); _right.Show(); _left.UpdateLayout(); _right.UpdateLayout();
            _phase = 0; _phaseTime.Restart(); SaveState();
        }

        private Window MakeWindow(ThemeMode mode, bool passive)
        {
            var window = new Window { Title = "Jalium 窗口外观", Width = Width, Height = 520,
                MinWidth = 400, MinHeight = 440, ThemeMode = mode, ShowActivated = !passive,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            window.SetResourceReference(Control.BackgroundProperty, "WindowBackground");
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
            panel.Children.Add(Text("窗口外观", 22));
            panel.Children.Add(Text("分别选择外观，切换后继续编辑。", 15));
            var caption = Text("当前：" + (mode == ThemeMode.Light ? "亮色" : "暗色"), 15);
            window.ResourcesChanged += (_, _) => caption.Text = "当前：" + (window.ThemeMode.Value switch
            {
                "Light" => "亮色", "Dark" => "暗色", "System" => "跟随系统", _ => "跟随应用"
            });
            panel.Children.Add(caption);
            var editor = new TextBox { Text = "中文🙂，继续编辑", Height = 38 };
            AutomationProperties.SetName(editor, "主题编辑框");
            panel.Children.Add(editor);
            var choices = new WrapPanel { ItemWidth = 104, ItemHeight = 44 };
            foreach (var choice in new[] { ("跟随应用", ThemeMode.None), ("跟随系统", ThemeMode.System), ("亮色", ThemeMode.Light), ("暗色", ThemeMode.Dark) })
            {
                var button = new Button { Content = choice.Item1, Width = 100, Height = 40 };
                button.Click += (_, _) => { window.ThemeMode = choice.Item2; caption.Text = "当前：" + choice.Item1; };
                choices.Children.Add(button);
            }
            panel.Children.Add(choices);
            var anchor = new Button { Content = "展开说明", Height = 40 };
            panel.Children.Add(anchor);
            window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            if (!passive) { _editor = editor; _anchor = anchor; _caption = caption; }
            return window;
        }

        private static TextBlock Text(string text, double size)
        {
            var value = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            return value;
        }

        private void Verify(Window window, string requested)
        {
            string key = SystemParameters.HighContrast ? "HighContrast" : requested;
            if (!window.IsMacOSPopupHost)
                Require(BrushColor(window.Background) == Expected(key, "WindowBackground"), "window uses " + key + " background");
            string expectedName = key == "HighContrast"
                ? ((SystemParameters.PrefersDarkColorScheme ? 1u : 2u) == 1 ? NSAppearance.NameAccessibilityHighContrastDarkAqua : NSAppearance.NameAccessibilityHighContrastAqua)
                : key == "Dark" ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua;
            Require(Native(window).Appearance?.Name == expectedName, "native appearance agrees with " + key + " palette");
        }

        private static Color Expected(string theme, string key)
        {
            var seen = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
            return BrushColor(Find(Application.Current!.Resources) as Brush);

            object? Find(ResourceDictionary dictionary)
            {
                if (!seen.Add(dictionary)) return null;
                if (dictionary.TryGetOwnValue(key, out var own)) return own;
                if (dictionary.ThemeDictionaries.TryGetValue(theme, out var variant))
                {
                    var themed = Find(variant);
                    if (themed != null) return themed;
                }
                for (int i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var merged = Find(dictionary.MergedDictionaries[i]);
                    if (merged != null) return merged;
                }
                return null;
            }
        }
        private static Color BrushColor(Brush? brush) => (brush as SolidColorBrush)?.Color ?? throw new InvalidOperationException("Expected a solid semantic brush.");
        private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window ?? throw new InvalidOperationException("Native window is unavailable.");
        private static NSAppearance Appearance(string name)
        { using var value = new NSString(name); return NSAppearance.GetAppearance(value) ?? throw new InvalidOperationException("Appearance is unavailable."); }

        private void FocusPreserved()
        {
            Require(ReferenceEquals(Keyboard.FocusedElement, _editor), "editor keeps logical focus");
            Require(app.KeyWindow?.FirstResponder?.Handle == _responder, "editor keeps native first responder");
            Require(_editor!.Text == "中文🙂，继续编辑" && _editor.SelectionStart == 2 && _editor.SelectionLength == 2, "editor text and selection are unchanged");
        }

        private void CancelQuit()
        {
            static void Cancel(object? sender, System.ComponentModel.CancelEventArgs args) => args.Cancel = true;
            _left!.Closing += Cancel;
            _right!.Closing += Cancel;
            try
            {
                Require(((JaliumMacApplicationDelegate)app.Delegate!).ApplicationShouldTerminate(app) == NSApplicationTerminateReply.Cancel,
                    "a cancelled quit leaves the application running");
                Require(_left.Handle != 0 && _right.Handle != 0, "cancelled quit preserves both windows");
            }
            finally { _left.Closing -= Cancel; _right.Closing -= Cancel; }
        }

        private void CheckPanel()
        {
            _inPanel = true;
            Exception? failure = null;
            bool inspected = false;
            var wait = Stopwatch.StartNew();
            using var timer = NSTimer.CreateRepeatingTimer(.05, _ =>
            {
                var panel = Native(_left!).AttachedSheet as NSSavePanel ?? app.ModalWindow as NSSavePanel;
                if (panel == null)
                {
                    if (wait.Elapsed.TotalSeconds > 12)
                    {
                        failure = new TimeoutException("Owned theme panel was not observable.");
                        app.StopModalWithCode((nint)NSModalResponse.Cancel);
                    }
                    return;
                }
                try
                {
                    Require(panel.EffectiveAppearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]) ==
                        Native(_left!).EffectiveAppearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]),
                        "owned native file panel follows the dark window");
                    inspected = true;
                }
                catch (Exception error) { failure = error; }
                finally { panel.Cancel(panel); }
            });
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
            NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
            try
            {
                string directory = Path.Combine(output, "panel"); Directory.CreateDirectory(directory);
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "窗口外观检查", InitialDirectory = directory };
                Require(dialog.ShowDialog(_left) == false, "theme panel cancellation completes");
                Require(inspected, "native panel appearance was inspected");
                if (failure != null) throw failure;
            }
            finally { timer.Invalidate(); _inPanel = false; }
        }

        private void Capture(Window window, string phase)
        {
            if (ReferenceEquals(window, _left)) _caption!.Text = "当前：" + (phase switch { "light" => "亮色", "dark" => "暗色", _ => "暗色，内容已保留" });
            window.UpdateLayout();
            double dpi = window.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * dpi),
                (int)Math.Ceiling(window.ActualHeight * dpi), dpi, window.Render, () => window.Handle != 0)
                ?? throw new InvalidOperationException("Offscreen rendering failed.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"{Index}-{phase}.png")); encoder.Save(file);
        }
        private void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); _checks.Add(message); }
        private void Await(string message)
        { if (_phaseTime.Elapsed.TotalSeconds > 10) throw new TimeoutException(message); }
        private void Next() { _phase++; _phaseTime.Restart(); }
        private void SaveState() => File.WriteAllText(Path.Combine(output, "state.json"), JsonSerializer.Serialize(new
        { processId = Environment.ProcessId, index = Index, phase = _phase, appActive = app.Active,
            frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
            passed = Passed, checks = _checks.Count, physicalInput = false, desktopScreenshot = false }));

        private void Complete(Exception? error)
        {
            _results.Add(new { index = Index, custom = Custom, width = Width, passed = error == null,
                checks = _checks.ToArray(), failure = error?.ToString(), physicalInput = false, desktopScreenshot = false });
            if (error == null) Passed++;
            Console.WriteLine(JsonSerializer.Serialize(new { index = Index, passed = error == null, checks = _checks.Count, failure = error?.Message }));
            if (_popup != null) _popup.IsOpen = false;
            _left?.Close(); _right?.Close(); _left = _right = null; _popup = null;
            app.Appearance = _originalAppearance;
            Application.Current!.ThemeMode = _originalMode;
            ThemeManager.ApplyTheme(_originalTheme);
            if (++_position < cases.Length) { _phase = -1; return; }
            _finished = true;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { passed = Passed, expected = cases.Length, cases = _results, physicalInput = false, desktopScreenshot = false, offscreenRendering = true }));
            app.Terminate(app);
        }
        public void Dispose()
        {
            if (_popup != null) _popup.IsOpen = false;
            _left?.Close(); _right?.Close();
            app.Appearance = _originalAppearance;
        }
    }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}
