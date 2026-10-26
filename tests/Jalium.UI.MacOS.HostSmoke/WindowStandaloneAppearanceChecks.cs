#pragma warning disable WPF0001
using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowStandaloneAppearanceChecks
{
    internal const int CaseCount = 8;
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= CaseCount) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_STANDALONE_APPEARANCE_OUTPUT")!;
        Directory.CreateDirectory(output);
        var native = NSApplication.SharedApplication; native.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        var original = native.Appearance;
        using var host = new Delegate(); native.Delegate = host; native.FinishLaunching();
        var scene = new Scene(index, output, native);
        native.BeginInvokeOnMainThread(() =>
        {
            try { scene.Execute(); } catch (Exception error) { scene.Failures.Add("scene: " + error); }
            int.TryParse(Environment.GetEnvironmentVariable("JALIUM_STANDALONE_APPEARANCE_HOLD_SECONDS"), out int hold);
            var end = Stopwatch.StartNew();
            var timer = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
            {
                if (end.Elapsed.TotalSeconds < Math.Clamp(hold, 0, 120)) return;
                try { scene.Cleanup(); native.Appearance = original; }
                catch (Exception error) { scene.Failures.Add("cleanup: " + error); }
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    index, passed = scene.Failures.Count == 0, scene.Checks, scene.Failures, scene.Observed,
                    processId = Environment.ProcessId, foregroundRequired = false, physicalInput = false,
                    nativeImages = NativeImages(), applicationStillNull = Application.Current == null,
                    renderBackend = scene.Backend.ToString(), custom = scene.Custom, initialDark = index >= 4,
                    managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location, typeof(WindowStandaloneAppearanceChecks).Assembly.Location }
                }));
                native.Stop(native);
                using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); native.PostEvent(wake, false);
            });
            // Keep the timer alive until the native loop is stopped.
            _completionTimer = timer;
        });
        native.Run(); _completionTimer?.Invalidate(); _completionTimer = null;
        return scene.Failures.Count == 0 ? 0 : 1;
    }
    private static NSTimer? _completionTimer;
    private sealed class Delegate : NSApplicationDelegate
    {
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => NSApplicationTerminateReply.Cancel;
    }
    private static NSAppearance Appearance(bool dark)
    { using var name = new NSString(dark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua); return NSAppearance.GetAppearance(name)!; }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")] private static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")] private static extern nint ImageName(uint index);
    private static string[] NativeImages() => Enumerable.Range(0, checked((int)ImageCount())).Select(i => Marshal.PtrToStringUTF8(ImageName((uint)i))!).Where(p => p.Contains("libjalium.native.")).ToArray();
    private sealed class Scene(int index, string output, NSApplication native)
    {
        private readonly List<(Window Window, TextBox Editor, string Mode)> _windows = [];
        internal readonly List<string> Checks = [], Failures = [];
        internal readonly List<object> Observed = [];
        internal RenderBackend Backend => index % 4 >= 2 ? RenderBackend.Software : RenderBackend.Metal;
        internal bool Custom => index % 2 != 0;
        private void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); Checks.Add(message); }
        private static void Pump()
        {
            using var until = NSDate.FromTimeIntervalSinceNow(.01); NSRunLoop.Main.RunUntil(until);
            typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!.GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        }
        private static string Expected(string mode, bool dark) => SystemParameters.HighContrast ? "HighContrast" : mode is "Light" or "Dark" ? mode : dark ? "Dark" : "Light";
        private static NSWindow Native(Window w) => Runtime.GetNSObject<NSView>(w.Handle)!.Window!;
        private bool Matches(bool dark) => Application.Current == null && SystemParameters.PrefersDarkColorScheme == dark &&
            ThemeManager.CurrentTheme == (dark ? ThemeVariant.Dark : ThemeVariant.Light) && _windows.All(p =>
            {
                string key = Expected(p.Mode, dark);
                string appearance = key == "HighContrast" ? dark ? NSAppearance.NameAccessibilityHighContrastDarkAqua : NSAppearance.NameAccessibilityHighContrastAqua
                    : key == "Dark" ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua;
                return ReferenceEquals(p.Window.Background, p.Window.Resources.ThemeDictionaries[key]["Pane"]) && Native(p.Window).Appearance?.Name == appearance;
            });
        private void Check(string phase, bool dark)
        {
            native.Appearance = Appearance(dark);
            var wait = Stopwatch.StartNew(); while (!Matches(dark) && wait.Elapsed.TotalSeconds < 3) Pump();
            Observed.Add(new { phase, expectedDark = dark, systemDark = SystemParameters.PrefersDarkColorScheme,
                currentTheme = ThemeManager.CurrentTheme.ToString(), globalKey = ResourceDictionary.CurrentThemeKey,
                windows = _windows.Select(p => new { p.Mode, background = (p.Window.Background as SolidColorBrush)?.Color.ToString(),
                    appearance = Native(p.Window).Appearance?.Name, visible = Native(p.Window).IsVisible }).ToArray() });
            Require(Application.Current == null, phase + ": no managed Application is introduced");
            Require(Matches(dark), phase + ": global palette, window resources and native appearances agree");
            foreach (var (window, editor, mode) in _windows)
            {
                Require(window.RenderTarget?.Backend == Backend && Native(window).IsVisible, phase + ": actual backend and native window for " + mode);
                Require(editor.Text == "ab🙂中文 Miii" && editor.SelectedText == "中文" && editor.SelectionStart == 4 && editor.SelectionLength == 2,
                    phase + ": Unicode text and selection survive for " + mode);
                window.UpdateLayout();
                var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * window.DpiScale), (int)Math.Ceiling(window.ActualHeight * window.DpiScale), window.DpiScale,
                    dc => { dc.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(dc); }, () => window.Handle != 0)
                    ?? throw new InvalidOperationException("native backend readback");
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, phase + "-" + mode + ".png")); png.Save(file);
            }
        }
        internal void Execute()
        {
            Require(Application.Current == null, "cold standalone process has no managed Application");
            var context = RenderContext.GetOrCreateCurrent(Backend, forceReplace: true); if (Backend == RenderBackend.Metal) context.DefaultRenderingEngine = RenderingEngine.Impeller;
            // Standalone hosts explicitly supply their control theme without constructing an Application.
            var controlsTheme = Jalium.UI.Markup.ThemeLoader.LoadGenericTheme() ?? throw new InvalidOperationException("Generic control theme unavailable");
            bool initialDark = index >= 4; native.Appearance = Appearance(initialDark);
            typeof(JaliumMacApplication).Assembly.GetType("Jalium.UI.MacOS.MacOSWindowThemes")!.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            foreach (string mode in new[] { "None", "System", "Light", "Dark" })
            {
                var window = new Window { Title = "Jalium 独立外观 · " + mode, Width = 430, Height = 260,
                    Left = 60 + _windows.Count % 2 * 450, Top = 60 + _windows.Count / 2 * 290,
                    TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
                if (mode != "None") window.ThemeMode = new ThemeMode(mode);
                window.Resources.MergedDictionaries.Add(controlsTheme);
                foreach (string key in new[] { "Light", "Dark", "HighContrast" })
                {
                    bool light = key == "Light";
                    window.Resources.ThemeDictionaries[key] = new ResourceDictionary
                    { ["Pane"] = new SolidColorBrush(light ? Color.FromRgb(243, 243, 243) : key == "Dark" ? Color.FromRgb(32, 32, 32) : Colors.Black),
                      ["Ink"] = new SolidColorBrush(light ? Colors.Black : Colors.White),
                      ["Input"] = new SolidColorBrush(light ? Colors.White : Color.FromRgb(45, 45, 45)) };
                }
                var root = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
                var heading = new TextBlock { Text = "独立窗口：" + mode, FontSize = 24 }; heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); root.Children.Add(heading);
                var description = new TextBlock { Text = "Miii 中文 🙂 随系统外观刷新", FontSize = 18 }; description.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); root.Children.Add(description);
                var editor = new TextBox { Text = "ab🙂中文 Miii", Height = 46 }; editor.SetResourceReference(Control.ForegroundProperty, "Ink"); editor.SetResourceReference(Control.BackgroundProperty, "Input"); root.Children.Add(editor);
                window.Content = root; window.SetResourceReference(Window.BackgroundProperty, "Pane"); _windows.Add((window, editor, mode)); window.Show(); window.UpdateLayout(); editor.Select(4, 2);
            }
            Check("initial", initialDark); Check("changed", !initialDark); Check("restored", initialDark);
        }
        internal void Cleanup()
        {
            foreach (var pair in _windows.AsEnumerable().Reverse()) pair.Window.Close();
            Require(_windows.All(p => p.Window.Handle == 0) && Application.Current == null, "all standalone native windows close without an Application");
        }
    }
}
