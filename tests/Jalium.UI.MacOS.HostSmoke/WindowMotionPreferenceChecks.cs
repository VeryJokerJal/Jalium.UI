#pragma warning disable WPF0001
using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using ObjCRuntime;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowMotionPreferenceChecks
{
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type Settings = typeof(Window).Assembly.GetType("Jalium.UI.Controls.Platform.MacOSScrollBarSettings")!;
    private static readonly FieldInfo Cache = Settings.GetField("s_prefersReducedMotion", HiddenStatic)!;
    private static readonly Type Monitor = typeof(JaliumMacApplication).Assembly.GetType("Jalium.UI.MacOS.MacOSWindowThemes")!;
    private static readonly string[] FlagNames = ["ClientAreaAnimation", "ComboBoxAnimation", "ListBoxSmoothScrolling", "MenuAnimation", "MinimizeAnimation", "ToolTipAnimation", "UIEffects"];
    private static readonly HashSet<string> MotionNames = [.. FlagNames, "ComboBoxPopupAnimation", "MenuPopupAnimation", "ToolTipPopupAnimation"];

    internal static int Run(int index)
    {
        if (index is < 0 or >= 16) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_APPEARANCE_OUTPUT") ?? throw new InvalidOperationException("Missing owned output directory.");
        Directory.CreateDirectory(output);
        using var preference = new PreferenceOverride(index % 2 != 0);
        Cache.SetValue(null, index % 2 == 0);
        var native = NSApplication.SharedApplication;
        native.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new MotionDelegate(Mode(index));
        native.Delegate = host;
        native.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, native)) host.DidFinishLaunching(note);
        native.BeginInvokeOnMainThread(() =>
        {
            var scene = new Scene(index, output, native, host, preference);
            try { scene.Execute(); }
            catch (Exception error) { scene.Failures.Add("scene: " + error); }
            finally { try { scene.Cleanup(); } catch (Exception error) { scene.Failures.Add("cleanup: " + error); } }
            bool restored = preference.Restore();
            if (!restored) scene.Failures.Add("process-local AppKit preference override was not restored");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, passed = scene.Failures.Count == 0 && scene.ForegroundUnavailable == 0,
                qualifiedWithoutForeground = scene.Failures.Count == 0,
                scene.Checks, scene.Scenarios, scene.Failures, scene.Observed, scene.Focus,
                scene.ForegroundUnavailable, processId = Environment.ProcessId, appKitPreferenceOverride = true,
                nativeWorkspaceNotifications = true, systemPreferencesWritten = false, overrideRestored = restored,
                physicalInput = false, desktopScreenshot = false, backendReadback = true,
                renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD")
            }));
            host.AllowQuit = true;
            native.Terminate(native);
        });
        native.Run();
        return 0;
    }

    private static ThemeMode Mode(int index) => (index / 4) switch
    { 0 => ThemeMode.None, 1 => ThemeMode.System, 2 => ThemeMode.Light, _ => ThemeMode.Dark };
    private static bool Cached => (bool?)Cache.GetValue(null) ?? throw new InvalidOperationException("Reduced-motion cache was not initialized.");
    private static int Leases => (int)Monitor.GetField("_leases", HiddenStatic)!.GetValue(null)!;
    private static bool Pending => (bool)Monitor.GetField("_pending", HiddenStatic)!.GetValue(null)!;
    private static int OpenWindows => ((IEnumerable<Window>)typeof(Window).GetMethod("SnapshotOpenWindows", HiddenStatic)!.Invoke(null, null)!).Count();
    private static NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)?.Window ?? throw new InvalidOperationException("Missing native window.");
    private static void Pump(double seconds)
    {
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(seconds));
        // Nested SDK waits must also drain AppKit events, as the normal dispatcher does.
        typeof(Window).Assembly.GetType("Jalium.UI.Core.Platform.NativePlatformEventPump")!
            .GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
    }
    private static void Barrier()
    {
        bool done = false;
        var app = NSApplication.SharedApplication;
        app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => app.BeginInvokeOnMainThread(() => done = true)));
        var watch = Stopwatch.StartNew();
        while (!done && watch.Elapsed.TotalSeconds < 5) Pump(.01);
        if (!done) throw new TimeoutException("Main-thread observation barrier.");
        typeof(Css).Assembly.GetType("Jalium.UI.Styling.CssEvaluationScheduler")!
            .GetMethod("FlushIfPending", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [Jalium.UI.Threading.Dispatcher.CurrentDispatcher]);
    }

    private sealed class Scene(int index, string output, NSApplication native, MotionDelegate host, PreferenceOverride preference)
    {
        private readonly List<Window> _windows = [];
        private readonly List<StackPanel> _roots = [];
        private readonly List<string> _events = [];
        private Border? _probe;
        private Border? _authored;
        private object? _staticResourceValue;
        private TextBox? _editor;
        private TextBlock? _status;
        private nint _responder;
        private ResourceDictionary[] _dictionaries = [];
        private int _workspaceNotes;
        private NSObject? _witness;
        internal List<string> Checks { get; } = [];
        internal List<object> Scenarios { get; } = [];
        internal List<string> Failures { get; } = [];
        internal Dictionary<string, object?> Observed { get; } = [];
        internal List<object> Focus { get; } = [];
        internal int ForegroundUnavailable;
        private PropertyChangedEventHandler? _changed;
        private RenderBackend Backend => index % 4 < 2 ? RenderBackend.Metal : RenderBackend.Software;
        private bool Custom => index % 2 != 0;

        internal void Execute()
        {
            RenderContext.GetOrCreateCurrent(Backend, forceReplace: true).DefaultRenderingEngine = RenderingEngine.Impeller;
            _changed = (_, e) => { if (e.PropertyName is { } name && MotionNames.Contains(name)) _events.Add(name); };
            SystemParameters.StaticPropertyChanged += _changed;
            var workspace = NSWorkspace.SharedWorkspace;
            _witness = workspace.NotificationCenter.AddObserver(new NSString("NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification"), _ => _workspaceNotes++);
            _dictionaries = Application.Current!.Resources.MergedDictionaries.ToArray();
            Observed["mode"] = Mode(index).ToString(); Observed["backend"] = Backend.ToString(); Observed["custom"] = Custom;
            Observed["resourceForms"] = new { kinds = new[] { "StaticResource", "DynamicResource", "ThemeResource" }, wpf = index >= 8, namedKey = index % 4 >= 2, actualXamlStrings = true };
            Observed["startupNative"] = preference.Read(); Observed["startupCache"] = Cached;
            Attempt("application-only-owner", () => Require(Leases == 1 && OpenWindows == 0, "the application owns the only appearance lease without a Jalium native window"));
            Attempt("startup-refresh", () => Require(Cached == preference.Read(), "application startup refreshes the stale native motion preference before its first window"));
            var root = MakeRoot();
            Attempt("existing-policy-positive", () => Verify(Cached, root, includeDynamic: true));
            Attempt("windowless-change", () => Transition(root, exactSingleNotification: true));
            Attempt("windowless-unchanged", () =>
            {
                preference.Set(Cached); _events.Clear(); int before = _workspaceNotes; Post(); Barrier();
                Require(_workspaceNotes == before + 1 && !Pending, "the correct workspace center completed the unchanged notification");
                Require(_events.Count == 0, "an unchanged application-only preference does not republish animation keys");
                Verify(Cached, root, includeDynamic: true);
            });
            Attempt("windowless-coalesced", () =>
            {
                bool target = !Cached; preference.Set(!target); _events.Clear(); int before = _workspaceNotes;
                preference.Set(target); Post(); preference.Set(!target); Post(); preference.Set(target); Post(); Barrier();
                Require(_workspaceNotes == before + 3 && !Pending, "all three native workspace notifications reached one completed refresh");
                Verify(target, root, includeDynamic: true); VerifyEvents();
            });
            Attempt("author-values-positive", () =>
            {
                _authored!.Opacity = .7;
                Transition(root, exactSingleNotification: true);
                Require(_authored.Opacity == .7, "author opacity keeps precedence over reduced-motion media rules");
                var app = Application.Current!; bool authoredValue = !Cached; app.Resources[SystemParameters.ClientAreaAnimationKey] = authoredValue;
                try
                {
                    Require(Equals(app.TryFindResource(SystemParameters.ClientAreaAnimationKey), authoredValue), "application resources retain precedence over system animation values");
                    Transition(root, exactSingleNotification: true, applicationOverride: authoredValue);
                }
                finally { app.Resources.Remove(SystemParameters.ClientAreaAnimationKey); }
            });
            Attempt("queued-owner-retirement", () =>
            {
                bool before = Cached; preference.Set(!before); _events.Clear(); Post(); host.Stop(); Barrier();
                Require(Application.Current == null && Leases == 0 && OpenWindows == 0, "stopping a windowless host releases its only appearance lease");
                Require(_events.Count == 0 && Cached == before, "callbacks queued for a retired application cannot publish or refresh its preferences");
            });
            host.Start(); RenderContext.GetOrCreateCurrent(Backend, forceReplace: true).DefaultRenderingEngine = RenderingEngine.Impeller; Barrier();
            root = MakeRoot();
            Attempt("replacement-startup-refresh", () =>
            {
                Require(Leases == 1 && OpenWindows == 0, "a replacement application restarts its sole observer lease");
                Verify(preference.Read(), root, includeDynamic: true);
            });
            var window = MakeWindow(root); window.Show(); Barrier(); FocusEditor(window);
            Attempt("first-window-policy", () => { Require(window.RenderTarget?.Backend == Backend, "first window uses the requested actual backend"); Verify(preference.Read(), root, includeDynamic: true); });
            Capture(window, "first-window");
            Attempt("visible-window-change", () => { Transition(root, exactSingleNotification: false); Preserved(window); });
            Capture(window, "changed-window");
            Attempt("parsed-static-resource-is-a-snapshot", () => Require(Equals(_authored!.Tag, _staticResourceValue), "a parsed x:Static system key in StaticResource keeps its initial value"));
            Attempt("equal-local-value-replaces-resource", () => WithResource(() =>
            {
                object? literal = _probe!.Tag; _probe.Tag = literal; bool reduced = ChangeNativePreference();
                Require(Equals(_probe.Tag, literal), "an equal local SetValue retires the resource before the next native preference change");
                Verify(reduced, root, includeDynamic: false); Preserved(window);
            }));
            Attempt("authored-local-value-replaces-resource", () => WithResource(() =>
            {
                _probe!.Tag = "保留设置🙂"; _status!.Text = "保留当前设置，系统变化不会覆盖。";
                bool reduced = ChangeNativePreference();
                Require(Equals(_probe.Tag, "保留设置🙂"), "a user-authored local value survives the next native preference change");
                Verify(reduced, root, includeDynamic: false); Preserved(window); Capture(window, "literal-window");
            }));
            Attempt("clear-local-value-retires-resource", () => WithResource(() =>
            {
                _probe!.ClearValue(FrameworkElement.TagProperty); bool reduced = ChangeNativePreference();
                Require(_probe.Tag is null, "ClearValue does not allow an old resource to revive after a native preference notification");
                Verify(reduced, root, includeDynamic: false); Preserved(window);
            }));
            Attempt("current-value-keeps-resource", () => WithResource(() =>
            {
                _probe!.SetCurrentValue(FrameworkElement.TagProperty, "暂时值"); bool reduced = ChangeNativePreference();
                Require(Equals(_probe.Tag, !reduced), "SetCurrentValue keeps the expression live for the next native preference change");
                Verify(reduced, root, includeDynamic: true); Preserved(window);
            }));
            window.Close(); Barrier(); root = MakeRoot();
            Attempt("closed-window-application-owner", () =>
            {
                Require(Leases == 1 && OpenWindows == 0, "closing the last window leaves the application observer alive");
                Transition(root, exactSingleNotification: true);
            });
            root = MakeRoot(); var reopened = MakeWindow(root); reopened.Show(); Barrier(); FocusEditor(reopened);
            Attempt("reopened-window-policy", () => { Require(reopened.RenderTarget?.Backend == Backend, "reopened window uses the requested actual backend"); Verify(preference.Read(), root, includeDynamic: true); Preserved(reopened); });
            Capture(reopened, "reopened-window");
            reopened.Close(); Barrier();
            Attempt("subscriber-retirement", () =>
            {
                bool retired = false; int appearanceAfterRetirement = 0;
                var original = native.Appearance;
                string? match = native.EffectiveAppearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]);
                bool expectedDark = match != NSAppearance.NameDarkAqua;
                PropertyChangedEventHandler retire = (_, e) =>
                {
                    if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !retired)
                    {
                        Require(SystemParameters.PrefersDarkColorScheme == expectedDark, "the same completed refresh reads appearance before publishing motion");
                        retired = true; host.Stop();
                    }
                    if (retired && e.PropertyName == nameof(SystemParameters.PrefersDarkColorScheme)) appearanceAfterRetirement++;
                };
                SystemParameters.StaticPropertyChanged += retire;
                try
                {
                    using var name = new NSString(expectedDark ? NSAppearance.NameDarkAqua : NSAppearance.NameAqua);
                    native.Appearance = NSAppearance.GetAppearance(name)!;
                    preference.Set(!Cached); Post(); Barrier();
                    Require(retired && Application.Current == null && Leases == 0, "a motion subscriber can stop the last appearance owner during publication");
                    Require(appearanceAfterRetirement == 0, "the retired refresh does not publish a later appearance change");
                }
                finally { SystemParameters.StaticPropertyChanged -= retire; native.Appearance = original; }
            });
            host.Stop(); Barrier();
            Attempt("final-observer-cleanup", () => Require(Application.Current == null && Leases == 0 && OpenWindows == 0, "all application and window appearance leases are released"));
            Attempt("standalone-motion-preference", () =>
            {
                preference.Set(!Cached);
                RenderContext.GetOrCreateCurrent(Backend, forceReplace: true).DefaultRenderingEngine = RenderingEngine.Impeller;
                root = MakeRoot(); var standalone = MakeWindow(root); standalone.ThemeMode = ThemeMode.System;
                foreach (var dictionary in _dictionaries) standalone.Resources.MergedDictionaries.Add(dictionary);
                standalone.Show(); Barrier(); FocusEditor(standalone);
                Require(Application.Current == null && Leases == 1, "an independent native window owns the only monitor without an Application");
                Require(standalone.RenderTarget?.Backend == Backend, "the independent window uses the requested actual backend");
                Verify(preference.Read(), root, includeDynamic: true);
                Transition(root, exactSingleNotification: false); Preserved(standalone);
                Capture(standalone, "standalone-window");
                standalone.Close(); Barrier();
                Require(Leases == 0 && OpenWindows == 0, "the independent window releases the last observer lease");
            });
            Observed["workspaceNotifications"] = _workspaceNotes; Observed["finalCache"] = Cached;
        }

        private bool ChangeNativePreference()
        {
            bool reduced = !Cached; preference.Set(reduced); Post(); Barrier();
            Require(Cached == reduced && preference.Read() == reduced, "native preference publication completed for the local-expression scenario");
            return reduced;
        }
        private void WithResource(Action action)
        {
            _probe!.SetResourceReference(FrameworkElement.TagProperty, SystemParameters.ClientAreaAnimationKey);
            try { action(); }
            finally
            {
                _probe.SetResourceReference(FrameworkElement.TagProperty, SystemParameters.ClientAreaAnimationKey);
                _status!.Text = "窗口与控件跟随当前动态效果偏好。";
                Require(Equals(_probe.Tag, SystemParameters.ClientAreaAnimation), "an explicitly restored resource resumes following the current preference");
            }
        }
        private void Transition(StackPanel root, bool exactSingleNotification, bool? applicationOverride = null)
        {
            bool target = !Cached; preference.Set(!target); _events.Clear(); int before = _workspaceNotes;
            preference.Set(target); Require(preference.Read() == target, "the native platform getter sees the process-local AppKit preference");
            Post(); Barrier();
            Require(_workspaceNotes == before + 1 && !Pending, "the native workspace notification and queued application refresh completed");
            Verify(target, root, includeDynamic: true, applicationOverride);
            if (exactSingleNotification) VerifyEvents();
        }
        private void VerifyEvents()
        {
            Require(_events.Count == MotionNames.Count && MotionNames.All(n => _events.Count(e => e == n) == 1), "one application-only change publishes every affected animation key exactly once");
        }
        private void Verify(bool reduced, StackPanel root, bool includeDynamic, bool? applicationOverride = null)
        {
            Require(Cached == reduced, "the cached motion preference matches the independently requested native value");
            foreach (string name in FlagNames)
            {
                Require(Equals(typeof(SystemParameters).GetProperty(name)!.GetValue(null), !reduced), "public animation flag " + name);
                object key = typeof(SystemParameters).GetProperty(name + "Key")!.GetValue(null)!;
                bool expected = name == "ClientAreaAnimation" && applicationOverride.HasValue ? applicationOverride.Value : !reduced;
                Require(Equals(Application.Current?.TryFindResource(key) ?? root.TryFindResource(key), expected), "system resource value " + name);
            }
            foreach (string name in new[] { "ComboBoxPopupAnimation", "MenuPopupAnimation", "ToolTipPopupAnimation" })
                Require(Equals(typeof(SystemParameters).GetProperty(name)!.GetValue(null), PopupAnimation.None) == reduced, "popup animation policy " + name);
            var provider = (Func<bool>?)typeof(UIElement).GetProperty("AutomaticTransitionsEnabledProvider", HiddenStatic)!.GetValue(null);
            Require(provider != null && provider() == !reduced, "automatic transition policy follows the native preference");
            Require(_probe!.Opacity == (reduced ? .25 : 1), "existing CSS reduced-motion media queries update in both directions");
            if (includeDynamic) Require(Equals(_probe.Tag, applicationOverride ?? !reduced), "existing dynamic system-key bindings refresh without a window");
            Require(Equals(_editor!.Tag, applicationOverride ?? !reduced), "a parsed ThemeResource system key keeps following preference changes");
        }
        private T ParseResourceElement<T>(string kind) where T : FrameworkElement
        {
            string ns = index >= 8 ? "http://schemas.microsoft.com/winfx/2006/xaml/presentation" : "http://schemas.jalium.com/jalxaml";
            string named = index % 4 >= 2 ? "ResourceKey=" : string.Empty;
            string markup = "<" + typeof(T).Name + " xmlns='" + ns + "' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
                + "xmlns:ui='clr-namespace:Jalium.UI;assembly=Jalium.UI.Controls' Tag='{"
                + kind + " " + named + "{x:Static ui:SystemParameters.ClientAreaAnimationKey}}' />";
            return (T)Jalium.UI.Markup.XamlReader.Parse(markup);
        }
        private StackPanel MakeRoot()
        {
            var root = new StackPanel { Margin = new Thickness(20), Spacing = 14 }; _roots.Add(root);
            var heading = new TextBlock { Text = "系统动态效果偏好", FontSize = 22, TextWrapping = TextWrapping.Wrap };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush"); root.Children.Add(heading);
            _status = new TextBlock { Text = "窗口与控件跟随当前动态效果偏好。", FontSize = 15, TextWrapping = TextWrapping.Wrap };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush"); root.Children.Add(_status);
            _probe = ParseResourceElement<Border>("DynamicResource"); _probe.Height = 20; Css.SetClass(_probe, "motion");
            _probe.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            root.Children.Add(_probe);
            _staticResourceValue = SystemParameters.ClientAreaAnimation;
            _authored = ParseResourceElement<Border>("StaticResource"); _authored.Height = 8; Css.SetClass(_authored, "motion"); root.Children.Add(_authored);
            _editor = ParseResourceElement<TextBox>("ThemeResource"); _editor.Text = "保留中文🙂 Miii"; _editor.Height = 42; root.Children.Add(_editor);
            var button = new Button { Content = "继续编辑", Height = 44 }; button.Click += (_, _) => _editor.Focus(); root.Children.Add(button);
            Css.SetStyleSheet(root, ".motion { opacity: 1; } @media (prefers-reduced-motion: reduce) { .motion { opacity: .25; } }");
            Barrier(); return root;
        }
        private Window MakeWindow(StackPanel root)
        {
            var window = new Window { Title = "Jalium Window Motion " + index, Width = Custom ? 420 : 760, Height = 420, MinWidth = 400, MinHeight = 320,
                TitleBarStyle = Custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = root };
            window.SetResourceReference(Window.BackgroundProperty, "WindowBackground"); _windows.Add(window); return window;
        }
        private void FocusEditor(Window window)
        {
            window.Activate(); _editor!.Focus(); _editor.Select(1, 2); Barrier();
            _responder = Native(window).FirstResponder?.Handle ?? 0;
            ObserveForeground(window);
        }
        private void Preserved(Window window)
        {
            Require(_editor!.Text == "保留中文🙂 Miii" && _editor.SelectionStart == 1 && _editor.SelectionLength == 2, "preference changes retain Unicode text and selection");
            Require(ReferenceEquals(Keyboard.FocusedElement, _editor), "preference changes retain logical editing focus");
            Require(_responder != 0 && Native(window).FirstResponder?.Handle == _responder, "preference changes retain the subject window native editor responder");
            ObserveForeground(window);
        }
        private bool ObserveForeground(Window window)
        {
            bool active = native.Active;
            int? frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier;
            bool available = active && frontmostPID == Environment.ProcessId;
            bool nativeKey = Native(window).IsKeyWindow;
            if (!available) ForegroundUnavailable++;
            Focus.Add(new { available, appActive = active, frontmostPID, nativeKey });
            if (available) Require(nativeKey && native.KeyWindow?.Handle == Native(window).Handle, "foreground preference subject remains the key window");
            return available;
        }
        private void Post()
        {
            using var name = new NSString("NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification");
            var workspace = NSWorkspace.SharedWorkspace; workspace.NotificationCenter.PostNotificationName(name, workspace);
        }
        private void Capture(Window window, string phase)
        {
            if (index is not (0 or 1 or 2 or 3 or 8 or 9 or 12 or 13)) return;
            window.UpdateLayout(); double dpi = window.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * dpi), (int)Math.Ceiling(window.ActualHeight * dpi), dpi,
                drawing => { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(drawing); }, () => window.Handle != 0)
                ?? throw new InvalidOperationException("Backend readback failed.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, phase + ".png")); encoder.Save(file);
        }
        private void Attempt(string name, Action body)
        {
            int start = Checks.Count;
            try { body(); Scenarios.Add(new { name, passed = true, checks = Checks.Skip(start).ToArray(), error = (string?)null }); }
            catch (Exception error) { Failures.Add(name + ": " + error); Scenarios.Add(new { name, passed = false, checks = Checks.Skip(start).ToArray(), error = error.ToString() }); }
        }
        private void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); Checks.Add(message); }
        internal void Cleanup()
        {
            foreach (var window in _windows) if (window.Handle != 0) window.Close();
            foreach (var root in _roots) Css.SetStyleSheets(root, null);
            if (_changed != null) SystemParameters.StaticPropertyChanged -= _changed;
            if (_witness != null) { NSWorkspace.SharedWorkspace.NotificationCenter.RemoveObserver(_witness); _witness.Dispose(); }
            host.Stop();
        }
    }

    private sealed class MotionDelegate(ThemeMode mode) : NSApplicationDelegate
    {
        private bool _started; private JaliumApp? _host; internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification note) { if (_started) return; _started = true; Start(); }
        internal void Start()
        {
            _host = AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build().UseApplication(() =>
            { var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; if (mode != ThemeMode.None) app.ThemeMode = mode; return app; });
            _host.StartHosted();
        }
        internal void Stop() { var host = _host; _host = null; host?.StopHosted(); }
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
        public override void WillTerminate(NSNotification note) => Stop();
    }
    internal sealed class PreferenceOverride : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int One(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetValue(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Zero();
        private readonly nint _library;
        private readonly SetValue _set;
        private readonly Zero _read, _restore;
        private bool _restored;
        internal PreferenceOverride(bool value)
        {
            _library = NativeLibrary.Load(Environment.GetEnvironmentVariable("JALIUM_MOTION_TEST_LIBRARY") ?? throw new InvalidOperationException("Missing process-local test library."));
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            _set = Function<SetValue>("jalium_motion_test_set"); _read = Function<Zero>("jalium_motion_test_read"); _restore = Function<Zero>("jalium_motion_test_restore");
            if (Function<One>("jalium_motion_test_install")(value ? 1 : 0) != 1) throw new InvalidOperationException("Cannot install process-local AppKit override.");
        }
        internal void Set(bool value) => _set(value ? 1 : 0);
        internal bool Read() => _read() != 0;
        internal bool Restore() => _restored || (_restored = _restore() == 1);
        public void Dispose() { Restore(); NativeLibrary.Free(_library); }
    }
}
