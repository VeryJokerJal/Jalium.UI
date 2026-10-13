using AppKit;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Jalium.UI.MacOS;

/// <summary>Own-process NSWindow checks; activation is prohibited, not desktop acceptance.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowVisibilityChecks
{
    private static readonly string[] Names =
    [
        "CLR visibility", "SetValue visibility", "SetCurrentValue and ClearValue",
        "Binding changes and removal", "Style/local precedence", "Show preserves binding",
        "Hide preserves binding", "ClearBinding reveals hidden style", "Modal binding Hidden",
        "Modal binding Collapsed", "Modal ClearBinding", "SourceInitialized hide and reuse",
        "Unshown default and asynchronous creation", "Pending show is cancelled by hide",
        "Pending show is cancelled by close", "Explicit Show consumes pending display",
        "Binding before native creation", "Style before native creation",
        "Derived metadata and native synchronization", "Closed windows reject visible requests"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < Names.Length * 2; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-visibility-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(20_000))
            {
                process.Kill(); failed++;
                Console.Error.WriteLine($"FAIL: visibility case {index} timed out");
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window visibility host checks: {Names.Length * 2 - failed}/{Names.Length * 2} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-visibility-case=".Length), out int index) || index < 0 || index >= Names.Length * 2)
            return 2;
        int scenario = index / 2;
        var titleBar = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Window window = scenario == 18 ? new DerivedWindow() : Create("Visibility check");
        window.Width = 320; window.Height = 240; window.TitleBarStyle = titleBar; window.ShowActivated = false;
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dispatcher = Dispatcher.CurrentDispatcher;
        var samples = new List<Sample>();
        VisibilitySource? source = null;
        int initialized = 0, loaded = 0, closed = 0, shown = 0, hiding = 0, failures = 0;
        window.SourceInitialized += (_, _) => initialized++;
        window.Loaded += (_, _) => loaded++;
        window.Closed += (_, _) => closed++;
        window.Shown += (_, _) => shown++;
        window.Hiding += (_, _) => hiding++;
        nint originalHandle = 0;
        NSWindow? native = null;
        CoreGraphics.CGRect frame = default;
        Rect restore = Rect.Empty;
        try
        {
            if (scenario is >= 12 and <= 17)
            {
                Require(window.Handle == 0 && window.Visibility == Visibility.Collapsed && !window.IsLoaded && !window.IsVisible,
                    "an unshown Window must be collapsed, unloaded, and have no native handle");
                if (scenario == 16) Bind(Visibility.Visible);
                else if (scenario == 17) Style(Visibility.Visible);
                else window.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
                Require(window.Handle == 0 && initialized == 0 && loaded == 0, "setting Visibility created the native window synchronously");
                if (scenario == 13) window.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
                if (scenario == 14) window.Close();
                if (scenario == 15) window.Show();
                Pump();
                if (scenario is 13 or 14)
                {
                    Require(window.Handle == 0 && initialized == 0 && loaded == 0, "cancelled display created a native window");
                    Require(closed == (scenario == 14 ? 1 : 0), "cancelled display changed close count");
                    return Pass();
                }
                Attach(); Check("initial property display", Visibility.Visible);
                Require(initialized == 1 && loaded == 1 && shown == 1, "asynchronous display raised lifecycle events more than once");
                if (source != null) { source.Value = Visibility.Hidden; Check("initial binding hide", Visibility.Hidden); }
                else { window.ClearValue(UIElement.VisibilityProperty); if (scenario != 17) Check("clear initial visibility", Visibility.Collapsed); }
                return Pass();
            }

            if (scenario is >= 8 and <= 10)
            {
                Window owner = Create("Visibility owner"), disabled = Create("Disabled peer");
                owner.Show(); disabled.IsEnabled = false; disabled.Show();
                window.Owner = owner;
                Bind(Visibility.Visible);
                bool timedOut = false;
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); window.Close(); };
                window.Shown += (_, _) => dispatcher.BeginInvoke(() =>
                {
                    Require(!owner.IsEnabled && !disabled.IsEnabled, "modal owner or disabled peer was enabled");
                    Attach();
                    if (scenario == 10) BindingOperations.ClearBinding(window, UIElement.VisibilityProperty);
                    else source!.Value = scenario == 8 ? Visibility.Hidden : Visibility.Collapsed;
                });
                try
                {
                    timeout.Start();
                    bool? result = window.ShowDialog();
                    timeout.Stop();
                    Require(!timedOut, "a bound visibility change did not exit the modal loop");
                    Require(result == false && !window.IsModalForCss && window.Handle == originalHandle && closed == 0,
                        "hidden dialog closed, lost its handle, or kept its modal state");
                    Require(owner.IsEnabled && !disabled.IsEnabled, "modal exit did not restore previous enabled states");
                    Check("modal visibility exit", scenario == 8 ? Visibility.Hidden : Visibility.Collapsed);
                }
                finally { timeout.Stop(); window.Close(); owner.Close(); disabled.Close(); }
                return Pass();
            }

            if (scenario is 5 or 6) Bind(Visibility.Hidden);
            if (scenario == 11) window.SourceInitialized += (_, _) => window.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            window.Show(); Attach();
            if (scenario == 11)
            {
                Check("hide during source creation", Visibility.Hidden);
                Require(loaded == 0 && shown == 0, "hidden source initialization continued display");
                window.SetValue(UIElement.VisibilityProperty, Visibility.Visible); Pump();
                Check("property resumes hidden startup", Visibility.Visible);
                Require(initialized == 1 && loaded == 1 && shown == 1, "property display skipped or repeated the startup lifecycle");
                return Pass();
            }
            Check("initial Show", Visibility.Visible);
            switch (scenario)
            {
                case 0:
                    window.Visibility = Visibility.Hidden; Check("CLR Hidden", Visibility.Hidden);
                    window.Visibility = Visibility.Visible; Check("CLR Visible", Visibility.Visible);
                    window.Visibility = Visibility.Collapsed; Check("CLR Collapsed", Visibility.Collapsed);
                    window.Visibility = Visibility.Visible; Check("CLR restored", Visibility.Visible);
                    break;
                case 1:
                    foreach (var value in new[] { Visibility.Hidden, Visibility.Visible, Visibility.Collapsed, Visibility.Visible })
                    { window.SetValue(UIElement.VisibilityProperty, value); Check("SetValue", value); }
                    break;
                case 2:
                    window.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Hidden); Check("current Hidden", Visibility.Hidden);
                    window.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible); Check("current Visible", Visibility.Visible);
                    window.ClearValue(UIElement.VisibilityProperty); Check("clear to unshown default", Visibility.Collapsed);
                    break;
                case 3:
                    Bind(Visibility.Visible);
                    source!.Value = Visibility.Hidden; Check("binding Hidden", Visibility.Hidden);
                    source.Value = Visibility.Visible; Check("binding Visible", Visibility.Visible);
                    source.Value = Visibility.Collapsed; Check("binding Collapsed", Visibility.Collapsed);
                    source.Value = Visibility.Visible; Check("binding restored", Visibility.Visible);
                    BindingOperations.ClearBinding(window, UIElement.VisibilityProperty); Check("clear binding", Visibility.Collapsed);
                    break;
                case 4:
                    Style(Visibility.Hidden);
                    window.ClearValue(UIElement.VisibilityProperty); Check("style Hidden", Visibility.Hidden);
                    Style(Visibility.Visible); Check("style Visible", Visibility.Visible);
                    window.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); Check("local Collapsed", Visibility.Collapsed);
                    window.ClearValue(UIElement.VisibilityProperty); Check("clear local reveals style", Visibility.Visible);
                    window.Style = null; Check("remove style", Visibility.Collapsed);
                    break;
                case 5:
                    Require(BindingOperations.GetBindingExpression(window, UIElement.VisibilityProperty) != null, "Show removed the visibility binding");
                    source!.Value = Visibility.Collapsed; Check("binding after explicit Show", Visibility.Collapsed);
                    source.Value = Visibility.Visible; Check("binding re-show", Visibility.Visible);
                    break;
                case 6:
                    Bind(Visibility.Visible); window.Hide(); Check("explicit Hide", Visibility.Hidden);
                    Require(BindingOperations.GetBindingExpression(window, UIElement.VisibilityProperty) != null, "Hide removed the visibility binding");
                    source!.Value = Visibility.Collapsed; Check("binding after Hide", Visibility.Collapsed);
                    source.Value = Visibility.Visible; Check("binding after Hide re-show", Visibility.Visible);
                    Require(hiding == 1, "Hide event repeated through the property callback");
                    break;
                case 7:
                    Style(Visibility.Hidden); Bind(Visibility.Visible);
                    BindingOperations.ClearBinding(window, UIElement.VisibilityProperty); Check("clear binding to hidden style", Visibility.Hidden);
                    break;
                case 18:
                    Require(Equals(UIElement.VisibilityProperty.GetMetadata(window.GetType()).DefaultValue, Visibility.Collapsed), "derived Window lost the unshown default");
                    window.SetValue(UIElement.VisibilityProperty, Visibility.Hidden); Check("derived Hidden", Visibility.Hidden);
                    window.SetValue(UIElement.VisibilityProperty, Visibility.Visible); Check("derived Visible", Visibility.Visible);
                    break;
                case 19:
                    window.Close();
                    foreach (var writer in new[] { "SetValue", "SetCurrentValue", "CLR setter" })
                    {
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            bool rejected = false;
                            try
                            {
                                if (writer == "SetValue") window.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
                                else if (writer == "SetCurrentValue") window.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
                                else window.Visibility = Visibility.Visible;
                            }
                            catch (InvalidOperationException) { rejected = true; }
                            Pump();
                            Require(rejected && window.Handle == 0 && closed == 1, $"{writer}: a closed window accepted a visible request or was recreated");
                            Require(window.Visibility == Visibility.Collapsed && !window.IsVisible &&
                                window.Content is UIElement closedContent && !closedContent.IsVisible && window.RestoreBounds.IsEmpty,
                                $"{writer}: a rejected request corrupted the closed window state");
                        }
                    }
                    break;
            }
            return Pass();
        }
        catch (Exception error)
        {
            failures++;
            Console.Error.WriteLine($"FAIL: {Names[scenario]}, titleBar={titleBar}: {error}");
            return 1;
        }
        finally
        {
            window.Close();
            string? output = Environment.GetEnvironmentVariable("JALIUM_WINDOW_VISIBILITY_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, $"visibility-{index:00}.json"), JsonSerializer.Serialize(new
                { index, scenario = Names[scenario], titleBar = titleBar.ToString(), activationPolicy = "Prohibited",
                    failures, initialized, loaded, closed, shown, hiding, samples }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        int Pass()
        {
            Console.WriteLine($"PASS: {Names[scenario]}, titleBar={titleBar}, samples={samples.Count}");
            return 0;
        }
        void Bind(Visibility value)
        {
            source = new VisibilitySource { Value = value };
            BindingOperations.SetBinding(window, UIElement.VisibilityProperty, new Binding(nameof(VisibilitySource.Value)) { Source = source });
        }
        void Style(Visibility value)
        {
            var style = new Style(typeof(Window)); style.Setters.Add(new Setter(UIElement.VisibilityProperty, value)); window.Style = style;
        }
        void Pump() { for (int turn = 0; turn < 4; turn++) dispatcher.ProcessQueue(); }
        void Attach()
        {
            native = Runtime.GetNSObject<NSView>(window.Handle)?.Window ?? throw new InvalidOperationException("no NSWindow");
            originalHandle = window.Handle; frame = native.Frame; restore = window.RestoreBounds;
        }
        void Check(string operation, Visibility expected)
        {
            bool visible = expected == Visibility.Visible;
            bool hiddenFlag = (bool)typeof(Window).GetField("_nativeWindowHidden", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            bool matches = window.Visibility == expected && native!.IsVisible == visible && hiddenFlag == !visible;
            samples.Add(new(operation, expected.ToString(), window.Visibility.ToString(), native!.IsVisible, hiddenFlag, matches));
            Require(matches, $"{operation}: expected {expected}, managed {window.Visibility}, native visible {native.IsVisible}, hidden flag {hiddenFlag}");
            Require(window.Handle == originalHandle && native.Frame == frame && window.RestoreBounds == restore,
                $"{operation}: handle, frame or RestoreBounds changed");
        }
    }

    private static Window Create(string title) => new()
    { Title = title, Width = 320, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false,
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)), Content = new TextBox { Text = "隐藏后保留的编辑内容", Margin = new Thickness(16) } };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record Sample(string Operation, string Expected, string Managed, bool NativeVisible, bool NativeHiddenFlag, bool Passed);
    private sealed class DerivedWindow : Window { }
    private sealed class VisibilitySource : INotifyPropertyChanged
    {
        private Visibility _value;
        public Visibility Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
