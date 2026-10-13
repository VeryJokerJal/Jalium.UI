using AppKit;
using System.ComponentModel;
using Jalium.UI.Data;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class WindowPropertyAccessibilityChecks
{
    private static readonly string[] s_names =
    [
        "combined AX geometry updates managed position, size and automatic sizing",
        "position-only AX edits preserve automatic sizing and content",
        "modal owner and hidden window reject AX writes, then remain usable",
        "AX minimize and restore preserve managed maximized state and restore bounds",
        "native restore callback can request another Zoom without replacing managed restore bounds",
        "AX selection preserves native application state and managed activation"
    ];

    internal static int RunAll() => RunCases(0, 10, "property accessibility");

    internal static int RunActivationAll() => RunCases(10, 2, "property activation accessibility");

    private static int RunCases(int firstCase, int count, string group)
    {
        int failed = 0;
        for (int index = firstCase; index < firstCase + count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-property-accessibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill(); failed++;
                Console.Error.WriteLine($"FAIL: Window AX property case {index} timed out");
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window {group} host checks: {count - failed}/{count} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-property-accessibility-case=".Length), out int index)
            || (uint)index >= s_names.Length * 2) return 2;
        bool requiresActivation = index / 2 == 5;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = requiresActivation
            ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Accessory;
        NSApplication.SharedApplication.FinishLaunching();
        NativeMethods.PlatformPollEvents();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var editor = new TextBox { Text = "AX 属性修改后保留中文🙂", Height = 48, Width = 260 };
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(editor);
        var window = new Window
        {
            Title = "窗口属性无障碍验证", Width = 560, Height = 420,
            MinWidth = 360, MinHeight = 200, MaxWidth = 1000, MaxHeight = 800,
            TitleBarStyle = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            Content = panel, ShowActivated = requiresActivation,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var application = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window? dialog = null;
        Window? other = null;
        try
        {
            window.Show(); Pump(window);
            var native = Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
            WindowGeometryBindings? geometry = index / 2 <= 1 ? new(window) : null;
            switch (index / 2)
            {
                case 0:
                {
                    window.SizeToContent = SizeToContent.WidthAndHeight; Pump(window);
                    CGRect before = native.Frame;
                    double width = window.Width, height = window.Height, left = window.Left, top = window.Top;
                    var requested = new CGRect(before.X + 36, before.Y + 18, before.Width + 80, before.Height + 60);
                    native.AccessibilityFrame = requested;
                    Pump(window);
                    Require(native.Frame.Equals(requested), "real NSWindow frame did not change");
                    Require(window.SizeToContent == SizeToContent.Manual, "AX resizing retained SizeToContent");
                    Near(window.Width, width + 80, "managed width"); Near(window.Height, height + 60, "managed height");
                    Near(window.Left, left + 36, "managed left"); Near(window.Top, top - 18 - 60, "managed final top");
                    geometry!.Verify(window);
                    Require(geometry.AutoSize == SizeToContent.Manual, "native resize did not publish manual sizing to the source");
                    int widthWrites = geometry.WidthWrites, heightWrites = geometry.HeightWrites;
                    native.AccessibilityFrame = requested; Pump(window);
                    Require(geometry.WidthWrites == widthWrites && geometry.HeightWrites == heightWrites,
                        "unchanged native geometry duplicated source updates");
                    geometry.Width += 70; geometry.Height += 50; Pump(window);
                    geometry.Verify(window);
                    Near(native.ContentView!.Frame.Width, geometry.Width, "bound source updated native width");
                    Near(native.ContentView.Frame.Height, geometry.Height, "bound source updated native height");
                    break;
                }
                case 1:
                {
                    window.SizeToContent = SizeToContent.WidthAndHeight; Pump(window);
                    CGRect before = native.Frame;
                    double width = window.Width, height = window.Height, left = window.Left, top = window.Top;
                    native.AccessibilityFrame = new CGRect(before.X + 30, before.Y - 20, before.Width, before.Height);
                    Pump(window);
                    Require(window.SizeToContent == SizeToContent.WidthAndHeight, "AX move disabled automatic sizing");
                    Near(window.Width, width, "move preserved width"); Near(window.Height, height, "move preserved height");
                    Near(window.Left, left + 30, "move left"); Near(window.Top, top + 20, "move top");
                    geometry!.Verify(window);
                    Require(geometry.AutoSize == SizeToContent.WidthAndHeight, "native move changed the bound sizing mode");
                    break;
                }
                case 2:
                {
                    CGRect before = native.Frame;
                    var requested = new CGRect(before.X + 30, before.Y + 20, before.Width + 50, before.Height + 40);
                    dialog = new Window { Title = "AX 写入禁用验证", Owner = window, Width = 400, Height = 260, ShowActivated = false };
                    Exception? modalError = null;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        try
                        {
                            Require(!window.IsEnabled && !native.AccessibilityEnabled, "modal owner is enabled");
                            native.AccessibilityFrame = requested; native.AccessibilityMinimized = true;
                            Require(native.Frame.Equals(before) && !native.IsMiniaturized, "AX modified a disabled modal owner");
                        }
                        catch (Exception error) { modalError = error; }
                        finally { dialog.DialogResult = true; }
                    };
                    timer.Start(); dialog.ShowDialog();
                    if (modalError != null) throw modalError;
                    Require(window.IsEnabled && native.AccessibilityEnabled, "owner was not reenabled");
                    window.Hide(); native.AccessibilityFrame = requested; native.AccessibilityMinimized = true;
                    Require(native.Frame.Equals(before) && !native.IsMiniaturized, "AX modified a hidden window");
                    window.Show(); Pump(window); native.AccessibilityFrame = requested; Pump(window);
                    Require(native.Frame.Equals(requested), "AX writes did not recover after hide and disable");
                    break;
                }
                case 3:
                {
                    Rect restore = window.RestoreBounds;
                    window.WindowState = WindowState.Maximized; Pump(window);
                    native.AccessibilityMinimized = true;
                    Require(Wait(window, () => window.WindowState == WindowState.Minimized && native.IsMiniaturized), "AX did not minimize");
                    native.AccessibilityMinimized = false;
                    Require(Wait(window, () => window.WindowState == WindowState.Maximized && !native.IsMiniaturized), "AX restore lost maximized state");
                    Require(window.RestoreBounds == restore, "AX minimize/restore replaced normal restore bounds");
                    window.WindowState = WindowState.Normal; Pump(window);
                    Near(window.Width, restore.Width, "restored width"); Near(window.Height, restore.Height, "restored height");
                    break;
                }
                case 4:
                {
                    Rect restore = window.RestoreBounds;
                    window.WindowState = WindowState.Maximized; Pump(window);
                    bool requested = false;
                    window.StateChanged += (_, _) =>
                    {
                        if (window.WindowState == WindowState.Normal && !requested)
                        {
                            requested = true;
                            native.Zoom(null);
                        }
                    };
                    // Begin with the real AppKit action, so StateChanged runs
                    // inside the native restore notification rather than
                    // after a managed property setter has returned.
                    native.Zoom(null);
                    Require(Wait(window, () => requested && window.WindowState == WindowState.Maximized),
                        "native restore callback lost the newer Zoom request");
                    Require(window.RestoreBounds == restore, "reentrant native Zoom replaced managed restore bounds");
                    window.WindowState = WindowState.Normal; Pump(window);
                    Near(window.Width, restore.Width, "reentrant restored width");
                    Near(window.Height, restore.Height, "reentrant restored height");
                    break;
                }
                case 5:
                {
                    // This group requires an unlocked, active desktop. Keep it
                    // separate from the nonactivating property regressions.
                    window.Activate();
                    Require(Wait(window, () => native.IsMainWindow && native.IsKeyWindow && window.IsActive),
                        "an unlocked desktop that can activate the host is required");
                    int deactivated = 0;
                    window.Deactivated += (_, _) => deactivated++;
                    native.AccessibilityMain = false;
                    native.AccessibilityFocused = false;
                    Pump(window);
                    Require(native.IsMainWindow && native.IsKeyWindow && window.IsActive && deactivated == 0,
                        "AX false revoked native selection or managed activation");
                    Require(native.AccessibilityMain && native.AccessibilityFocused &&
                        NSApplication.SharedApplication.MainWindow == native &&
                        NSApplication.SharedApplication.KeyWindow == native,
                        "AX false changed application selection or detached accessibility state");
                    native.AccessibilityMain = true; native.AccessibilityFocused = true;
                    Pump(window);
                    Require(native.IsMainWindow && native.IsKeyWindow && window.IsActive && deactivated == 0,
                        "AX reselection lost native selection");
                    other = new Window { Title = "窗口选择无障碍验证", Width = 400, Height = 280,
                        TitleBarStyle = window.TitleBarStyle, ShowActivated = false };
                    other.Show(); Pump(window);
                    var otherNative = Runtime.GetNSObject<NSView>(other.Handle)!.Window!;
                    otherNative.AccessibilityMain = true;
                    Require(Wait(window, () => otherNative.IsMainWindow && !native.IsMainWindow),
                        "AX Main did not select the second document window");
                    Require(native.IsKeyWindow && window.IsActive &&
                        NSApplication.SharedApplication.MainWindow == otherNative &&
                        NSApplication.SharedApplication.KeyWindow == native,
                        "AX Main incorrectly changed keyboard focus");
                    native.AccessibilityMain = true;
                    Require(Wait(window, () => native.IsMainWindow && !otherNative.IsMainWindow),
                        "AX Main did not restore the first document window");
                    otherNative.AccessibilityFocused = true;
                    Require(Wait(window, () => otherNative.IsKeyWindow && other.IsActive && !window.IsActive),
                        "AX Focused did not move managed activation to the second window");
                    Require(!native.AccessibilityFocused && NSApplication.SharedApplication.KeyWindow == otherNative,
                        "AX Focused getter or application key window remained stale");
                    native.AccessibilityFocused = true;
                    Require(Wait(window, () => native.IsKeyWindow && native.IsMainWindow && window.IsActive && !other.IsActive),
                        "AX Focused did not return activation to the first window");
                    window.IsEnabled = false; Pump(window);
                    Require(!native.CanBecomeKeyWindow &&
                        (NSApplication.SharedApplication.KeyWindow == native) == native.IsKeyWindow,
                        "disabling an active window left application key selection stale");
                    window.IsEnabled = true; window.Activate(); Pump(window);
                    Require(Wait(window, () => native.IsKeyWindow && window.IsActive && native.FirstResponder?.Handle == window.Handle),
                        "reenabling did not restore the native input view");
                    editor.Focus();
                    Require(editor.IsKeyboardFocused && editor.Text == "AX 属性修改后保留中文🙂",
                        "reenabling lost managed editing focus or content");
                    otherNative.AccessibilityFocused = true;
                    Require(Wait(window, () => otherNative.IsKeyWindow && other.IsActive),
                        "second activation before background reenable failed");
                    window.IsEnabled = false; window.IsEnabled = true; Pump(window);
                    Require(otherNative.IsKeyWindow && other.IsActive && !window.IsActive &&
                        NSApplication.SharedApplication.KeyWindow == otherNative,
                        "background reenable stole native or managed activation");
                    break;
                }
            }
            Require(editor.Text == "AX 属性修改后保留中文🙂", "AX Window edits lost content");
            Console.WriteLine($"PASS {window.TitleBarStyle}: {s_names[index / 2]}");
            if (index == 0 && int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_PROPERTY_OBSERVE_SECONDS"),
                out int observationSeconds) && observationSeconds is >= 1 and <= 60)
            {
                Console.WriteLine($"OBSERVE: PID {Environment.ProcessId}, bound geometry {geometry!.Width} x {geometry.Height}, sizing {geometry.AutoSize}");
                var observation = Stopwatch.StartNew();
                while (observation.Elapsed < TimeSpan.FromSeconds(observationSeconds)) Pump(window);
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL AX property case {index}: {error}"); return 1; }
        finally { dialog?.Close(); other?.Close(); window.Close(); application.Shutdown(); }
    }

    private sealed class WindowGeometryBindings : INotifyPropertyChanged
    {
        private double _width, _height, _left, _top;
        private SizeToContent _autoSize;
        private readonly Dictionary<DependencyProperty, BindingExpressionBase> _expressions = [];
        public int WidthWrites, HeightWrites;
        public event PropertyChangedEventHandler? PropertyChanged;
        public WindowGeometryBindings(Window window)
        {
            _width = window.Width; _height = window.Height; _left = window.Left; _top = window.Top; _autoSize = window.SizeToContent;
            foreach (var (property, path) in new[] { (Window.WidthProperty, nameof(Width)), (Window.HeightProperty, nameof(Height)),
                (Window.LeftProperty, nameof(Left)), (Window.TopProperty, nameof(Top)), (Window.SizeToContentProperty, nameof(AutoSize)) })
                _expressions[property] = window.SetBinding(property, new Binding(path) { Source = this, Mode = BindingMode.TwoWay });
        }
        public double Width { get => _width; set { _width = value; WidthWrites++; Notify(nameof(Width)); } }
        public double Height { get => _height; set { _height = value; HeightWrites++; Notify(nameof(Height)); } }
        public double Left { get => _left; set { _left = value; Notify(nameof(Left)); } }
        public double Top { get => _top; set { _top = value; Notify(nameof(Top)); } }
        public SizeToContent AutoSize { get => _autoSize; set { _autoSize = value; Notify(nameof(AutoSize)); } }
        private void Notify(string property) => PropertyChanged?.Invoke(this, new(property));
        public void Verify(Window window)
        {
            Near(Width, window.Width, "bound width"); Near(Height, window.Height, "bound height");
            Near(Left, window.Left, "bound left"); Near(Top, window.Top, "bound top");
            Require(AutoSize == window.SizeToContent, "bound sizing mode is stale");
            foreach (var (property, expression) in _expressions)
                Require(ReferenceEquals(expression, BindingOperations.GetBindingExpressionBase(window, property)), "geometry binding was replaced");
        }
    }

    private static void Pump(Window window)
    {
        NativeMethods.PlatformPollEvents();
        Dispatcher.GetForCurrentThread().ProcessQueue(); window.UpdateLayout();
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
    }
    private static bool Wait(Window window, Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5)) Pump(window);
        return condition();
    }
    private static void Near(double actual, double expected, string name) =>
        Require(Math.Abs(actual - expected) <= 0.5, $"{name}: expected {expected}, got {actual}");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
