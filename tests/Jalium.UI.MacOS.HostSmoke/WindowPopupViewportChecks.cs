#pragma warning disable WPF0001
using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowPopupViewportChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or > 7) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_POPUP_VIEWPORT_OUTPUT")
            ?? throw new InvalidOperationException("Missing owned output directory.");
        Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication;
        using var host = new Delegate();
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var context = RenderContext.GetOrCreateCurrent(index >= 4 ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (index < 4) context.DefaultRenderingEngine = RenderingEngine.Impeller;
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var scene = new Scene(index);
        bool complete = false, stopped = false, cleaned = false;
        void Save(string name) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(new
        {
            index, backend = context.Backend.ToString(), custom = index % 4 >= 2, vertical = index % 2 != 0,
            processId = Environment.ProcessId, complete, cleaned, scene.Checks, scene.Scenarios, scene.Failures,
            passed = complete && scene.Failures.Count == 0 && scene.Scenarios.Count == 9,
            physicalInput = false, systemPreferencesWritten = false, foregroundRequired = false,
            appActive = app.Active, frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
            managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location,
                typeof(WindowPopupViewportChecks).Assembly.Location }
        }));
        app.BeginInvokeOnMainThread(() =>
        {
            try
            {
                scene.Execute();
                Save("state.json");
                double hold = double.TryParse(Environment.GetEnvironmentVariable("JALIUM_POPUP_VIEWPORT_HOLD_SECONDS"), out var value) ? value : 0;
                if (hold > 0) Pump(Math.Min(hold, 120));
            }
            catch (Exception error) { scene.Failures.Add(error.ToString()); }
            complete = true;
        });
        using var finish = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (!complete || stopped) return;
            stopped = true;
            try { scene.Cleanup(); cleaned = true; } catch (Exception error) { scene.Failures.Add("cleanup: " + error); }
            Save("result.json"); app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        });
        app.Run();
        return cleaned && scene.Failures.Count == 0 && scene.Scenarios.Count == 9 ? 0 : 1;
    }

    private static void Pump(double seconds)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            using var until = NSDate.FromTimeIntervalSinceNow(.01);
            NSRunLoop.Main.RunUntil(until);
            typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!
                .GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        } while (watch.Elapsed.TotalSeconds < seconds);
    }
    private sealed class ProbeItems : ItemsControl
    {
        private readonly bool _vertical;
        internal ProbeItems(bool vertical, double width = double.NaN, double height = double.NaN)
        {
            _vertical = vertical; Width = width; Height = height;
            ItemsPanel = new ItemsPanelTemplate { PanelType = typeof(VirtualizingWrapPanel) };
            ItemsSource = Enumerable.Range(0, 512).ToArray();
            VirtualizingPanel.SetCacheLength(this, new VirtualizationCacheLength(0));
            VirtualizingPanel.SetCacheLengthUnit(this, VirtualizationCacheLengthUnit.Pixel);
        }
        internal VirtualizingWrapPanel Panel
        {
            get
            {
                var panel = (VirtualizingWrapPanel)ItemsHost!;
                panel.UseAncestorScrollViewer = true;
                panel.Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal;
                panel.ItemWidth = 64; panel.ItemHeight = 36;
                return panel;
            }
        }
        protected override FrameworkElement GetContainerForItem(object item) => new Border
        {
            Width = 64, Height = 36, Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 223, 232)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock { Text = $"项{(int)item:000}", FontSize = 11,
                Foreground = Brushes.Black, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center }
        };
    }
    private sealed class Scene(int index)
    {
        internal readonly List<string> Checks = [], Failures = [];
        internal readonly List<object> Scenarios = [];
        private readonly bool _vertical = index % 2 != 0;
        private Window? _owner;
        private readonly List<Popup> _popups = [];
        private readonly List<(ProbeItems Rows, Window Window)> _surfaces = [];
        private void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Checks.Add(message);
        }
        private double Axis(Window window) => _vertical ? window.ActualWidth : window.ActualHeight;
        private double Viewport(VirtualizingWrapPanel panel) => _vertical ? panel.ViewportWidth : panel.ViewportHeight;
        private (int Minimum, int Maximum) Range(VirtualizingWrapPanel panel)
        {
            double cross = _vertical ? panel.ViewportHeight : panel.ViewportWidth;
            int perRow = (int)Math.Floor(cross / (_vertical ? 36 : 64));
            int minimum = (int)Math.Ceiling(Viewport(panel) / (_vertical ? 64 : 36)) * perRow;
            return (minimum, minimum + 2 * perRow);
        }
        private void Settle()
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 8)
            {
                foreach (var (rows, window) in _surfaces) { _ = rows.Panel; window.UpdateLayout(); }
                Pump(.04);
                bool ready = _surfaces.All(x =>
                {
                    var panel = x.Rows.Panel; var range = Range(panel);
                    return range.Minimum > 0 && panel.Children.Count >= range.Minimum && panel.Children.Count <= range.Maximum &&
                        Math.Abs(Viewport(panel) - Axis(x.Window)) < .1;
                });
                if (ready) return;
            }
            throw new TimeoutException("Native popup viewport and realization did not settle: " +
                string.Join("; ", _surfaces.Select(Describe)));
        }
        private static string Describe((ProbeItems Rows, Window Window) surface)
        {
            var panel = surface.Rows.Panel;
            var viewer = (ScrollViewer?)typeof(VirtualizingWrapPanel).GetField("_ancestorScrollViewer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel);
            var chain = new List<string>();
            for (Visual? current = panel; current != null; current = current.VisualParent)
                chain.Add(current is FrameworkElement element ? $"{current.GetType().Name}:{element.ActualWidth}x{element.ActualHeight}:m={element.IsMeasureValid}:a={element.IsArrangeValid}" : current.GetType().Name);
            return $"{surface.Window.Width}x{surface.Window.Height} actual={surface.Window.ActualWidth}x{surface.Window.ActualHeight} viewport={panel.ViewportWidth}x{panel.ViewportHeight} realized={panel.Children.Count} ancestor-viewer={viewer?.ViewportWidth}x{viewer?.ViewportHeight}; " + string.Join(" > ", chain);
        }
        private void Record(string stage)
        {
            for (int i = 0; i < _surfaces.Count; i++)
            {
                var (rows, window) = _surfaces[i]; var panel = rows.Panel; var range = Range(panel);
                var native = Runtime.GetNSObject<NSView>(window.Handle)?.Window
                    ?? throw new InvalidOperationException("Missing physical NSWindow.");
                Require(window.Handle != 0 && native.IsVisible, stage + "/" + i + " has a visible physical native surface");
                Require(ReferenceEquals(Window.GetMacOSPhysicalWindow(panel), window), stage + "/" + i + " physical presentation identity");
                Require(ReferenceEquals(Window.GetWindow(panel), _owner), stage + "/" + i + " logical owner retained");
                Require(Math.Abs(Viewport(panel) - Axis(window)) < .1, stage + "/" + i + " fallback uses presenting dimensions");
                Require(panel.Children.Count >= range.Minimum && panel.Children.Count <= range.Maximum,
                    stage + "/" + i + " realization remains bounded by this viewport");
                var client = native.ContentView!.Bounds;
                Require(Math.Abs((double)client.Width - window.ActualWidth) < 1 && Math.Abs((double)client.Height - window.ActualHeight) < 1,
                    stage + "/" + i + " AppKit client and framework dimensions agree");
                Scenarios.Add(new { stage, depth = i, width = window.ActualWidth, height = window.ActualHeight,
                    viewportWidth = panel.ViewportWidth, viewportHeight = panel.ViewportHeight,
                    realized = panel.Children.Count, expectedMinimum = range.Minimum, expectedMaximum = range.Maximum,
                    nativeClientWidth = (double)client.Width, nativeClientHeight = (double)client.Height,
                    dpiScale = window.DpiScale, nativeWindow = (long)native.WindowNumber });
            }
        }
        internal void Execute()
        {
            var rows = new ProbeItems(_vertical);
            _owner = new Window { Title = "Jalium Popup Viewport v315", Width = 640, Height = 432,
                Left = 100, Top = 120, Content = rows,
                TitleBarStyle = index % 4 >= 2 ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            Application.Current!.MainWindow = _owner;
            _owner.Show(); _owner.UpdateLayout(); _ = rows.Panel;
            _surfaces.Add((rows, _owner)); Settle();
            var anchor = (UIElement)rows.ItemContainerGenerator.ContainerFromIndex(0)!;
            for (int i = 0; i < 2; i++)
            {
                var child = new ProbeItems(_vertical, i == 0 ? 256 : 192, i == 0 ? 180 : 144);
                var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Right,
                    Child = child, PreferExternalWindow = true, StaysOpen = true, PopupAnimation = PopupAnimation.None };
                _popups.Add(popup); popup.IsOpen = true; _owner.UpdateLayout(); Pump(.05);
                var physical = Window.GetMacOSPhysicalWindow(child) ?? throw new InvalidOperationException("Popup did not create a physical host.");
                Require(!ReferenceEquals(physical, _owner), "external popup uses its own Window");
                _surfaces.Add((child, physical)); Settle();
                anchor = (UIElement)child.ItemContainerGenerator.ContainerFromIndex(0)!;
            }
            Record("initial");
            var popupSizes = _surfaces.Skip(1).Select(x => (x.Window.ActualWidth, x.Window.ActualHeight)).ToArray();
            _owner.Width = 768; _owner.Height = 504; _owner.UpdateLayout(); Settle();
            for (int i = 1; i < _surfaces.Count; i++)
                Require(Math.Abs(_surfaces[i].Window.ActualWidth - popupSizes[i - 1].ActualWidth) < .1 &&
                    Math.Abs(_surfaces[i].Window.ActualHeight - popupSizes[i - 1].ActualHeight) < .1, "owner resize preserves popup " + i + " viewport");
            Record("owner-resized");
            _surfaces[1].Rows.Width = 320; _surfaces[1].Rows.Height = 216;
            _surfaces[1].Rows.InvalidateMeasure(); _owner.UpdateLayout(); Pump(.1); Settle();
            Require(Math.Abs(_surfaces[1].Window.ActualWidth - 320) < .1 && Math.Abs(_surfaces[1].Window.ActualHeight - 216) < .1,
                "popup content resize reaches the real host");
            Record("popup-resized");
        }
        internal void Cleanup()
        {
            for (int i = _popups.Count - 1; i >= 0; i--) _popups[i].IsOpen = false;
            _owner?.Close(); Pump(.1);
            Require(_surfaces.All(x => x.Window.Handle == 0), "all owned native surfaces closed");
        }
    }
    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light });
    }
}
