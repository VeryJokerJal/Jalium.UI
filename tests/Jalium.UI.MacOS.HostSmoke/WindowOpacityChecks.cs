using AppKit;
using Jalium.UI;
using Jalium.UI.Animation;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using ObjCRuntime;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Jalium.UI.MacOS;

/// <summary>Effective dependency-property values must reach real NSWindow alphaValue.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowOpacityChecks
{
    private static readonly string[] Names =
    [
        "CLR opacity setter", "SetValue and ClearValue", "SetCurrentValue and ClearValue",
        "Binding installed before Show", "Binding installed after Show and SetCurrentValue",
        "Style installed before Show", "Style/local precedence and ClearValue",
        "Animation frames and explicit stop", "CLR base value during an active animation",
        "Binding base value during an active animation", "FillBehavior.Stop completion",
        "Hidden window updates and re-show", "Before creation and after close",
        "Derived Window inherits opacity and composition metadata"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < Names.Length * 2; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-opacity-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                failed++;
                Console.Error.WriteLine($"FAIL: opacity case {index} timed out");
            }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window opacity host checks: {Names.Length * 2 - failed}/{Names.Length * 2} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-opacity-case=".Length), out int index) || index < 0 || index >= Names.Length * 2)
            return 2;
        int scenario = index / 2;
        bool allowsTransparency = index % 2 == 1;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Window window = scenario == 13 ? new DerivedWindow() : new Window();
        window.Title = $"Opacity {index}";
        window.Width = 320;
        window.Height = 240;
        window.TitleBarStyle = WindowTitleBarStyle.Native;
        window.ShowActivated = false;
        window.AllowsTransparency = allowsTransparency;
        window.Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc));
        _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var samples = new List<OpacitySample>();
        int failures = 0;
        OpacitySource? source = null;
        if (scenario == 3) Bind(.55);
        if (scenario == 5) SetStyle(.4);
        if (scenario == 12) window.SetValue(UIElement.OpacityProperty, .3);

        try
        {
            window.Show();
            var native = Runtime.GetNSObject<NSView>(window.Handle)?.Window
                ?? throw new InvalidOperationException("Show did not create an NSWindow");
            nint originalHandle = window.Handle;
            var originalFrame = native.Frame;
            var originalRestore = window.RestoreBounds;
            Check("initial", window.Opacity);
            switch (scenario)
            {
                case 0:
                    foreach (double value in new[] { 0.0, .25, .65, 1.0 }) { window.Opacity = value; Check("CLR setter", value); }
                    break;
                case 1:
                    window.SetValue(UIElement.OpacityProperty, .65); Check("SetValue .65", .65);
                    window.SetValue(UIElement.OpacityProperty, .25); Check("SetValue .25", .25);
                    window.ClearValue(UIElement.OpacityProperty); Check("ClearValue", 1);
                    break;
                case 2:
                    window.SetCurrentValue(UIElement.OpacityProperty, .65); Check("SetCurrentValue .65", .65);
                    window.SetCurrentValue(UIElement.OpacityProperty, .4); Check("SetCurrentValue .4", .4);
                    window.ClearValue(UIElement.OpacityProperty); Check("ClearValue", 1);
                    break;
                case 3:
                    source!.Value = .2; Check("binding source .2", .2);
                    source.Value = .8; Check("binding source .8", .8);
                    BindingOperations.ClearBinding(window, UIElement.OpacityProperty); Check("ClearBinding", 1);
                    break;
                case 4:
                    Bind(.6); Check("install binding", .6);
                    var expression = BindingOperations.GetBindingExpression(window, UIElement.OpacityProperty);
                    source!.Value = .3; Check("binding source .3", .3);
                    window.SetCurrentValue(UIElement.OpacityProperty, .45); Check("bound SetCurrentValue", .45);
                    Require(ReferenceEquals(expression, BindingOperations.GetBindingExpression(window, UIElement.OpacityProperty)), "SetCurrentValue replaced the binding");
                    source.Value = .75; Check("binding source .75", .75);
                    BindingOperations.ClearBinding(window, UIElement.OpacityProperty); Check("ClearBinding", 1);
                    break;
                case 5:
                    SetStyle(.7); Check("replace style", .7);
                    window.Style = null; Check("remove style", 1);
                    break;
                case 6:
                    SetStyle(.45); Check("install style", .45);
                    window.Opacity = .8; Check("local over style", .8);
                    SetStyle(.2); Check("style below local", .8);
                    window.ClearValue(UIElement.OpacityProperty); Check("clear local reveals style", .2);
                    window.Style = null; Check("remove style", 1);
                    break;
                case 7:
                    window.Opacity = .9;
                    long varyingStart = StartAnimation(.2, .8);
                    Frame(varyingStart, .25); Check("animation .25s", .35);
                    Frame(varyingStart, .75); Check("animation .75s", .65);
                    StopAnimation(); Check("stop animation", .9);
                    break;
                case 8:
                    window.Opacity = .9;
                    long constantStart = StartAnimation(.4, .4);
                    Frame(constantStart, .25); Check("active animation", .4);
                    window.Opacity = .8; Check("CLR base changes beneath animation", .4);
                    Frame(constantStart, .5); Check("next constant frame", .4);
                    StopAnimation(); Check("stop reveals changed CLR base", .8);
                    break;
                case 9:
                    Bind(.55);
                    var animatedBinding = BindingOperations.GetBindingExpression(window, UIElement.OpacityProperty);
                    long bindingStart = StartAnimation(.4, .4);
                    Frame(bindingStart, .25); Check("animated binding", .4);
                    source!.Value = .8; Check("binding update beneath animation", .4);
                    Require(ReferenceEquals(animatedBinding, BindingOperations.GetBindingExpression(window, UIElement.OpacityProperty)), "animation replaced the binding");
                    StopAnimation(); Check("stop reveals changed binding base", .8);
                    break;
                case 10:
                    window.Opacity = .85;
                    long stopStart = StartAnimation(.1, .7, FillBehavior.Stop);
                    Frame(stopStart, .25); Check("Stop animation active", .25);
                    Frame(stopStart, 2); Check("natural Stop completion", .85);
                    Require(!window.HasAnimation(UIElement.OpacityProperty), "completed Stop animation remained registered");
                    break;
                case 11:
                    window.Hide();
                    window.SetValue(UIElement.OpacityProperty, .35); Check("update while hidden", .35);
                    window.Show(); Check("re-show", .35);
                    window.AllowsTransparency = !allowsTransparency; Check("toggle transparency", .35);
                    window.SetCurrentValue(UIElement.OpacityProperty, .6); Check("update after transparency toggle", .6);
                    window.AllowsTransparency = allowsTransparency; Check("restore transparency", .6);
                    break;
                case 12:
                    window.Close();
                    Require(window.Handle == 0, "Close left a native handle");
                    window.SetValue(UIElement.OpacityProperty, .2);
                    window.Opacity = .5;
                    Require(window.Handle == 0 && window.Opacity == .5, "opacity update recreated the closed window");
                    break;
                case 13:
                    var metadata = UIElement.OpacityProperty.GetMetadata(window.GetType()) as FrameworkPropertyMetadata;
                    Require(metadata is { AffectsRender: true, AffectsCompositionOnly: true } && Equals(metadata.DefaultValue, 1.0),
                        "Window metadata lost the base composition flags or default");
                    window.SetValue(UIElement.OpacityProperty, .25); Check("derived SetValue", .25);
                    window.ClearValue(UIElement.OpacityProperty); Check("derived ClearValue", 1);
                    break;
            }
            if (window.Handle != 0)
            {
                Require(window.Handle == originalHandle && native.Frame == originalFrame && window.RestoreBounds == originalRestore,
                    "opacity updates changed the native handle, frame or RestoreBounds");
                Require(native.IsOpaque == !window.AllowsTransparency, "whole-window alpha changed the background opacity contract");
            }
            Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")}: {Names[scenario]}, AllowsTransparency={allowsTransparency}, samples={samples.Count}, mismatches={failures}");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            failures++;
            Console.Error.WriteLine($"FAIL: {Names[scenario]}, AllowsTransparency={allowsTransparency}: {error}");
            return 1;
        }
        finally
        {
            StopAnimation();
            if (window.Handle != 0) window.Close();
            var directory = Environment.GetEnvironmentVariable("JALIUM_WINDOW_OPACITY_OUTPUT");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, $"opacity-{index:D2}.json"), JsonSerializer.Serialize(new
                {
                    scenario = Names[scenario], allowsTransparency, activationPolicy = "Prohibited",
                    failures, samples
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        void Check(string step, double expected)
        {
            var native = Runtime.GetNSObject<NSView>(window.Handle)?.Window
                ?? throw new InvalidOperationException($"{step}: native window missing");
            double actual = native.AlphaValue;
            bool passed = Math.Abs(window.Opacity - expected) < 1e-6 && Math.Abs(actual - expected) < 1e-6;
            samples.Add(new OpacitySample(step, expected, window.Opacity, actual, passed));
            if (!passed)
            {
                failures++;
                Console.Error.WriteLine($"MISMATCH: {step}, expected={expected:F3}, managed={window.Opacity:F3}, NSWindow.alphaValue={actual:F3}");
            }
        }
        void Bind(double value)
        {
            source = new OpacitySource(value);
            BindingOperations.SetBinding(window, UIElement.OpacityProperty,
                new Binding(nameof(OpacitySource.Value)) { Source = source, Mode = BindingMode.OneWay });
        }
        void SetStyle(double value)
        {
            var style = new Style { TargetType = typeof(Window) };
            style.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = value });
            window.Style = style;
        }
        long StartAnimation(double from, double to, FillBehavior fill = FillBehavior.HoldEnd)
        {
            long timestamp = Stopwatch.GetTimestamp();
            ExceptionDispatchInfo? error = null;
            var subscription = new AnimationTickSubscription(new FrameAction(() =>
            {
                try { window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromSeconds(1), FillBehavior = fill }); }
                catch (Exception exception) { error = ExceptionDispatchInfo.Capture(exception); }
            }), weak: false);
            AnimationManager.Register(subscription);
            AnimationManager.ProcessFrame(timestamp);
            error?.Throw();
            return timestamp;
        }
        void StopAnimation() => window.BeginAnimation(UIElement.OpacityProperty, (AnimationTimeline?)null);
    }

    private static void Frame(long start, double seconds) => AnimationManager.ProcessFrame(start + (long)(seconds * Stopwatch.Frequency));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class DerivedWindow : Window { }
    private sealed record OpacitySample(string Step, double Expected, double ManagedValue, double NativeAlphaValue, bool Passed);
    private sealed class FrameAction(Action action) : IFrameAnimatable
    {
        public bool OnAnimationFrame(long timestamp) { action(); return false; }
    }
    private sealed class OpacitySource(double initialValue) : INotifyPropertyChanged
    {
        private double _value = initialValue;
        public event PropertyChangedEventHandler? PropertyChanged;
        public double Value
        {
            get => _value;
            set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
    }
}
