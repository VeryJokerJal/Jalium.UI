using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;

namespace Jalium.UI.MacOS;

/// <summary>Real AppKit/Metal host for wheel routing and focus clipping. Held-button
/// packets are injected at the platform boundary; this does not certify physical scrolling.</summary>
internal static class WindowInputBoundaryChecks
{
    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < 2; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-input-boundary-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); process.WaitForExit(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window input boundary host checks: {2 - failed}/2 passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-input-boundary-case=".Length), out int index) || (uint)index >= 2) return 2;
        int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_INPUT_BOUNDARY_OBSERVE_SECONDS"), out int seconds);
        var nativeApp = JaliumMacApplication.Initialize();
        nativeApp.ActivationPolicy = seconds > 0 ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Prohibited;
        nativeApp.FinishLaunching();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var edge = new Button { Content = "边缘按钮 · 中文🙂", Width = 292, Height = 48 };
        var editor = new TextBox { Text = "焦点切换保留中文🙂", Width = 220, Height = 40 };
        var canvas = new Canvas { Width = 620, Height = 440 };
        Canvas.SetTop(edge, 24); canvas.Children.Add(edge);
        Canvas.SetLeft(editor, 24); Canvas.SetTop(editor, 102); canvas.Children.Add(editor);
        var viewer = new ScrollViewer
        {
            Content = canvas, Width = 300, Height = 180,
            IsOverlayScrollBarEnabled = false, IsScrollInertiaEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x25, 0x2b)),
        };
        var title = new TextBlock { Text = "滚动与键盘焦点", FontSize = 24, Foreground = Brushes.White };
        var status = new TextBlock { Text = "用 Tab 和 Shift+Tab 在按钮与输入框之间移动。", FontSize = 15, Foreground = Brushes.LightGray };
        var finish = new Button { Content = "结束验证", Width = 120, Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        foreach (var child in new UIElement[] { title, status, viewer, finish }) panel.Children.Add(child);
        var window = new Window
        {
            Title = "Jalium Window Input Boundaries", Width = 640, Height = 420,
            TitleBarStyle = index == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            ShowActivated = seconds > 0, Content = panel,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
        };
        var application = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool closed = false;
        finish.Click += (_, _) => window.Close();
        window.Closed += (_, _) => closed = true;
        try
        {
            var template = new ControlTemplate(typeof(Control));
            template.SetVisualTree(() => new Border { BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(2), Margin = new Thickness(-2), IsHitTestVisible = false });
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            edge.FocusVisualStyle = editor.FocusVisualStyle = style;
            window.Show(); Pump(window);
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            string before = editor.Text;
            Require(edge.Focus(), "edge button focus failed");
            for (int i = 0; i < 3; i++)
            {
                SendTab(view); Pump(window);
                Require(editor.IsKeyboardFocused && editor.Text == before, "forward Tab changed text or missed the editor");
                SendTab(view, reverse: true); Pump(window);
                Require(edge.IsKeyboardFocused && editor.Text == before, "reverse Tab changed text or missed the edge button");
                VerifyClip();
            }
            // Probe a point inside the native client. A routed handler stops
            // the viewer from scrolling so all button masks share the target.
            var point = edge.TranslatePoint(new Point(40, 20), window);
            MouseWheelEventArgs? wheel = null;
            MouseWheelEventHandler probe = (_, e) => { wheel = e; e.Handled = true; };
            edge.PreviewMouseWheel += probe;
            foreach (uint mask in new uint[] { 1, 2, 4, 8, 16, 31, 0 })
            {
                wheel = null;
                typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [new Controls.Platform.PlatformEvent
                    {
                        Type = Controls.Platform.PlatformEventType.MouseWheel,
                        MouseX = (float)(point.X * window.DpiScale), MouseY = (float)(point.Y * window.DpiScale),
                        MouseButtons = mask, HasMouseButtonStates = true,
                        WheelDeltaY = -.25f, WheelHasPreciseScrollingDeltas = true,
                    }]);
                var received = wheel ?? throw new InvalidOperationException("wheel did not reach the actual window content");
                Require((received.LeftButton == MouseButtonState.Pressed) == ((mask & 1) != 0) &&
                    (received.RightButton == MouseButtonState.Pressed) == ((mask & 2) != 0) &&
                    (received.MiddleButton == MouseButtonState.Pressed) == ((mask & 4) != 0) &&
                    (received.XButton1 == MouseButtonState.Pressed) == ((mask & 8) != 0) &&
                    (received.XButton2 == MouseButtonState.Pressed) == ((mask & 16) != 0), "wheel discarded a button mask");
            }
            edge.PreviewMouseWheel -= probe;
            viewer.ScrollToVerticalOffset(60); Pump(window); VerifyClip();
            viewer.ScrollToVerticalOffset(0); viewer.ScrollToHorizontalOffset(0); Pump(window); VerifyClip();
            Console.WriteLine($"PASS: macOS Window input boundary case {index}; native/custom={window.TitleBarStyle}; client={view.Bounds}; text={editor.Text}");
            if (seconds > 0)
            {
                int tabs = 0;
                window.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Tab) { tabs++; Console.WriteLine($"OBSERVE: Tab {tabs}, modifiers={e.KeyboardModifiers}"); }
                };
                var observation = Stopwatch.StartNew();
                while (!closed && observation.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    Pump(window);
                    if (closed) break;
                    Require(editor.Text == before, "observer keyboard traversal changed text");
                    if (edge.IsKeyboardFocused || editor.IsKeyboardFocused) VerifyClip();
                }
                Console.WriteLine($"OBSERVE COMPLETE: tabs={tabs}; retained text={editor.Text}");
            }
            return 0;

            void VerifyClip()
            {
                var layer = (AdornerLayer)typeof(Window).GetProperty("AdornerLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(window)!;
                var target = editor.IsKeyboardFocused ? (UIElement)editor : edge;
                var ring = layer.GetAdorners(target)?.OfType<FocusVisualAdorner>().Single();
                Require(ring != null, "keyboard focus ring missing");
                var geometry = (Geometry)typeof(FocusVisualAdorner).GetMethod("GetLayoutClip",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(ring, null)!;
                var origin = ring!.TranslatePoint(geometry.Bounds.TopLeft, viewer);
                Require(Math.Abs(origin.X) < .01 && Math.Abs(origin.Y) < .01 &&
                    Math.Abs(geometry.Bounds.Width - viewer.ViewportWidth) < .01 &&
                    Math.Abs(geometry.Bounds.Height - viewer.ViewportHeight) < .01,
                    $"focus ring ignores classic gutter: {geometry.Bounds}; viewport {viewer.ViewportWidth}x{viewer.ViewportHeight}");
            }
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: macOS Window input boundary case {index}: {error}"); return 1; }
        finally { Keyboard.ClearFocus(); window.Close(); application.Shutdown(); }
    }

    private static void SendTab(NSView view, bool reverse = false)
    {
        string text = reverse ? "\u0019" : "\t";
        var flags = reverse ? NSEventModifierMask.ShiftKeyMask : 0;
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1,
            view.Window!.WindowNumber, null, text, text, false, 0x30)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 2,
            view.Window.WindowNumber, null, text, text, false, 0x30)!;
        view.KeyDown(down); view.KeyUp(up);
    }

    private static void Pump(Window window)
    {
        NativeMethods.PlatformPollEvents();
        Dispatcher.GetForCurrentThread().ProcessQueue(); window.UpdateLayout();
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
