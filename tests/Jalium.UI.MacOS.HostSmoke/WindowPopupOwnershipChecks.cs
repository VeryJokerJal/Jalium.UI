using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Exercise the real managed Popup -> native SetOwner -> AppKit child-window
// path. Hooks affect this process's retained fixture objects, never input/HID.
internal static class WindowPopupOwnershipChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Body = "Miii中文🙂e\u0301";
    private static readonly string[] Names = ["remove-before", "remove-after", "add-before", "add-after",
        "binding-retarget", "binding-reopen", "native-reopen", "ordinary"];
    private static readonly DependencyProperty[] TextProperties = [Control.FontFamilyProperty, Control.FontSizeProperty,
        Control.FontWeightProperty, Control.FontStyleProperty, Control.FontStretchProperty, Control.ForegroundProperty,
        FrameworkElement.FlowDirectionProperty, FrameworkElement.LanguageProperty];

    internal static int Run(string argument)
    {
        int index = int.Parse(argument[(argument.IndexOf('=') + 1)..]);
        if (index is < 0 or > 3) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_POPUP_OWNER_OUTPUT") ?? throw new InvalidOperationException("Output directory required.");
        Directory.CreateDirectory(output);
        string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var nativePreloadRequests = new[] { "core", "text", "metal", "software", "platform", "media.core", "media", "browser" }
            .Select(name => Path.Combine(directory, "libjalium.native." + name + ".dylib")).ToArray();
        foreach (string path in nativePreloadRequests) _ = NativeLibrary.Load(path);
        bool custom = (index & 1) != 0, software = index >= 2;
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var appDelegate = new ValidationDelegate(); app.Delegate = appDelegate; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) appDelegate.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var anchors = new[] { new TextBlock { Text = "原始位置", Width = 260, Height = 35 },
            new TextBlock { Text = "父窗口 A", Width = 260, Height = 35 }, new TextBlock { Text = "父窗口 B", Width = 260, Height = 35 } };
        var canvas = new Canvas();
        for (int n = 0; n < anchors.Length; ++n) { canvas.Children.Add(anchors[n]); Canvas.SetLeft(anchors[n], n == 2 ? 370 : 25); Canvas.SetTop(anchors[n], n == 0 ? 290 : 55); }
        var owner = new Window { Title = "Jalium Window v303 · 弹窗 owner 重入", Width = 760, Height = 520,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)), Foreground = Brushes.White, Content = canvas };
        Application.Current!.MainWindow = owner; owner.Show(); owner.UpdateLayout();
        bool ordinaryOnly = Environment.GetEnvironmentVariable("JALIUM_POPUP_OWNER_ORDINARY_ONLY") == "1";
        var pixelChecks = new List<object>();
        var checks = new List<string>(); var failures = new List<string>(); var observations = new List<object>(); var scenes = new List<object>();
        Popup? first = null, second = null, popup = null;
        TextBox? editor = null;
        Window? original = null;
        int scenario = ordinaryOnly ? 6 : -1, completed = 0, stage = 0;
        bool finished = false, qualified = true, changed = false, reopenedFocus = false;
        object? lastState = null;
        var elapsed = Stopwatch.StartNew();
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(Names[Math.Max(0, scenario)] + ": " + message);
            checks.Add(message);
        }
        Window Surface() => Window.GetMacOSPhysicalWindow(editor)!;
        NSWindow Native(Window window) => Runtime.GetNSObject<NSView>(window.Handle)!.Window!;
        bool Observe(bool record)
        {
            var surface = editor == null ? null : Window.GetMacOSPhysicalWindow(editor);
            var nativeView = surface is { Handle: not 0 } ? Runtime.GetNSObject<NSView>(surface.Handle) : null;
            int pid = Environment.ProcessId, front = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1;
            bool ready = app.Active && front == pid && surface?.IsActive == true && nativeView != null && app.KeyWindow?.Handle == nativeView.Window?.Handle &&
                app.KeyWindow?.FirstResponder?.Handle == nativeView.Handle;
            if (record)
            {
                qualified &= ready;
                observations.Add(new { scenario, stage, processId = pid, frontmost = front, active = app.Active,
                    keyWindow = (long)(nint)(app.KeyWindow?.Handle ?? default), responder = (long)(nint)(app.KeyWindow?.FirstResponder?.Handle ?? default),
                    ownResponder = (long)(nint)(nativeView?.Handle ?? default), ready });
            }
            return ready;
        }
        Popup Open(UIElement anchor, UIElement child)
        {
            var value = new Popup { Child = child, PlacementTarget = anchor, Placement = PlacementMode.Bottom,
                PreferExternalWindow = true, ShouldConstrainToRootBounds = false, StaysOpen = true, PopupAnimation = PopupAnimation.None };
            value.IsOpen = true; owner.UpdateLayout();
            return value;
        }
        void ClosePopups() { popup?.SetCurrentValue(Popup.IsOpenProperty, false); second?.SetCurrentValue(Popup.IsOpenProperty, false); first?.SetCurrentValue(Popup.IsOpenProperty, false); }
        void Capture(string suffix)
        {
            Window surface = Surface(); surface.UpdateLayout();
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(surface.ActualWidth * surface.DpiScale),
                (int)Math.Ceiling(surface.ActualHeight * surface.DpiScale), surface.DpiScale,
                drawing => { drawing.DrawRectangle(surface.Background, null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)); surface.Render(drawing); },
                () => surface.Handle != 0);
            Require(bitmap is { PixelWidth: > 0, PixelHeight: > 0 }, "nonempty " + suffix + " renderer readback");
            Save(bitmap!, Names[scenario] + "-" + suffix);
            // Independent reference: paint just the selected clusters through
            // the existing brush-aware glyph renderer at a manually resolved
            // viewport origin. This does not query the editor's caret/selection
            // implementation and remains executable against the real parent.
            Rect viewport = (Rect)typeof(TextBox).GetMethod("GetTextViewportRect", Private)!.Invoke(editor, null)!;
            Point origin = editor!.TranslatePoint(viewport.TopLeft, surface);
            string family = editor.FontFamily.GetRenderingSource(editor);
            using var format = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, family,
                (float)editor.FontSize, editor.FontWeight.ToOpenTypeWeight(), editor.FontStyle.ToOpenTypeStyle());
            format.SetNoWrap(true);
            double textWidth = format.MeasureText(Body, 100000, 100000).WidthIncludingTrailingWhitespace;
            var alignment = CssFlowProperties.NativeLineTextAlignment(editor, TextBox.TextAlignmentProperty, isLast: true);
            double shift = alignment == TextAlignment.Right ? viewport.Width - textWidth : alignment == TextAlignment.Center ? (viewport.Width - textWidth) / 2 : 0;
            double lineHeight = Math.Round(format.GetFontMetrics().LineHeight);
            double verticalShift = editor.VerticalContentAlignment switch
            {
                VerticalAlignment.Center => Math.Max(0, (viewport.Height - lineHeight) / 2),
                VerticalAlignment.Bottom => Math.Max(0, viewport.Height - lineHeight),
                _ => 0,
            };
            origin = new Point(Math.Round(origin.X + shift), Math.Round(origin.Y + verticalShift));
            var selected = new FormattedText(Body, family, editor.FontSize) { Foreground = Brushes.White,
                FontWeight = editor.FontWeight.ToOpenTypeWeight(), FontStyle = editor.FontStyle.ToOpenTypeStyle(),
                MaxTextWidth = double.MaxValue, MaxTextHeight = editor.ActualHeight, GlyphRanges = [4, 4], GlyphVisibility = 1 };
            var mask = MacOSWritingToolsBitmapCapture.Capture(bitmap!.PixelWidth, bitmap.PixelHeight, surface.DpiScale,
                drawing => drawing.DrawText(selected, origin), () => surface.Handle != 0);
            Require(mask is { PixelWidth: > 0 }, "selected glyph reference readback " + suffix);
            Save(mask!, Names[scenario] + "-" + suffix + "-mask");
            var glyphBounds = Bounds(mask!, (b, g, r, a) => a >= 96);
            var selectionBounds = Bounds(bitmap, (b, g, r, a) => r > 210 && b > 210 && g < 45 && a > 200);
            Require(glyphBounds is not null, "nonempty selected CJK/emoji reference " + suffix);
            Require(selectionBounds is not null, "nonempty selection-colored pixels " + suffix);
            double tolerance = 3 * surface.DpiScale;
            Require(selectionBounds!.Value.Left <= glyphBounds!.Value.Left + tolerance &&
                selectionBounds.Value.Right >= glyphBounds.Value.Right - tolerance &&
                selectionBounds.Value.Top <= glyphBounds.Value.Top + tolerance &&
                selectionBounds.Value.Bottom >= glyphBounds.Value.Bottom - tolerance,
                "selection pixels cover the independently painted selected glyphs " + suffix);
            Require(format.HitTestTextPosition(Body, 100000, 100000, 4, false, out var referenceCaret) &&
                Math.Abs(editor.TranslatePoint(editor.GetRectFromCharacterIndex(4).TopLeft, surface).X - (origin.X + referenceCaret.CaretX)) <= 1.5,
                "public caret matches the independently positioned glyph edge " + suffix);
            pixelChecks.Add(new { scene = Names[scenario], phase = suffix, glyphBounds, selectionBounds,
                alignment = alignment.ToString(), scale = surface.DpiScale, passed = true });
        }
        void Save(BitmapSource bitmap, string name)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        Rect? Bounds(BitmapSource bitmap, Func<byte, byte, byte, byte, bool> include)
        {
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight, left = width, right = -1, top = height, bottom = -1;
            byte[] pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
            for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x)
            {
                int offset = (y * width + x) * 4;
                if (!include(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3])) continue;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            return right < left ? null : new Rect(left, top, right - left + 1, bottom - top + 1);
        }
        void Next()
        {
            ClosePopups();
            if (++scenario == Names.Length) { Finish(); return; }
            first = Open(anchors[1], new TextBlock { Text = "A · Times · 日本語", Width = 280, Height = 70 });
            second = Open(anchors[2], new TextBlock { Text = "B · Monaco · 中文🙂", Width = 280, Height = 70 });
            var a = Window.GetMacOSPhysicalWindow(first.Child)!; var b = Window.GetMacOSPhysicalWindow(second.Child)!;
            a.FontFamily = new FontFamily("Times"); a.FontSize = 20; a.FontWeight = FontWeights.Bold; a.FontStyle = FontStyles.Italic;
            a.FontStretch = FontStretches.Condensed; a.Foreground = Brushes.Yellow; a.FlowDirection = FlowDirection.RightToLeft; a.Language = XmlLanguage.GetLanguage("ja-JP");
            b.FontFamily = new FontFamily("Monaco"); b.FontSize = 24; b.FontWeight = FontWeights.Normal; b.FontStyle = FontStyles.Normal;
            b.FontStretch = FontStretches.Normal; b.Foreground = Brushes.White; b.FlowDirection = FlowDirection.LeftToRight; b.Language = XmlLanguage.GetLanguage("zh-CN");
            editor = new TextBox { Text = Body, Width = 310, Height = 100, AcceptsReturn = true, Background = new SolidColorBrush(Color.FromRgb(0x24, 0x2a, 0x33)),
                SelectionBrush = new SolidColorBrush(Color.FromRgb(255, 0, 255)), SelectionOpacity = 1 };
            popup = Open(anchors[0], editor); original = Surface(); original.UpdateLayout(); editor.Select(4, 4);
            app.Activate(); original.Activate(); Require(editor.Focus(), "initial editor focus accepted");
            var view = Runtime.GetNSObject<NSView>(original.Handle)!;
            view.Window!.MakeKeyWindow(); view.Window.MakeFirstResponder(view);
            stage = 1; changed = reopenedFocus = false; elapsed.Restart();
        }
        void Reopen()
        {
            changed = true;
            popup!.IsOpen = false; popup.PlacementTarget = anchors[0]; popup.IsOpen = true;
        }
        void Mutate()
        {
            Require(Observe(true), "qualified native focus before reparent");
            Capture("before");
            var initial = Surface(); var a = Window.GetMacOSPhysicalWindow(first!.Child)!;
            var beforeParent = Native(initial).ParentWindow;
            Require(beforeParent == Native(owner), "initial native parent matches the ordinary owner");
            Action retarget = () => { changed = true; popup!.PlacementTarget = second!.Child; };
            if (scenario is 4 or 5)
            {
                initial.PropertyChangedInternal += OnBinding;
                try { popup!.PlacementTarget = first.Child; }
                finally { initial.PropertyChangedInternal -= OnBinding; }
                void OnBinding(DependencyProperty property, object? oldValue, object? value)
                {
                    if (changed || property != Control.FontFamilyProperty) return;
                    if (scenario == 5) Reopen(); else retarget();
                }
            }
            else if (scenario == 7) popup!.PlacementTarget = first.Child;
            else
            {
                bool adding = scenario is 2 or 3;
                using var hook = new ChildWindowHook(adding ? Native(a) : Native(owner), Native(initial), adding,
                    scenario is 1 or 3, scenario == 6 ? Reopen : retarget);
                popup!.PlacementTarget = first.Child;
                Require(hook.Fired && hook.Error is null, "native callback ran without an exception");
                if (hook.Error != null) throw hook.Error;
            }
            Require(scenario == 7 || changed, "requested reentry occurred");
            stage = 2; elapsed.Restart();
        }
        void Verify()
        {
            Require(popup!.IsOpen, "latest popup remains open");
            var surface = Surface(); var expected = scenario is 5 or 6 ? owner :
                Window.GetMacOSPhysicalWindow(scenario == 7 ? first!.Child : second!.Child)!;
            var native = Native(surface); var expectedNative = Native(expected);
            Require(native.ParentWindow == expectedNative, "native parent matches the latest owner");
            Require(expectedNative.ChildWindows.Contains(native), "latest native parent contains the child");
            Require(ReferenceEquals(expected, typeof(Window).GetField("_macOSPopupParent", Private)!.GetValue(surface)), "managed parent matches the latest owner");
            Require(ReferenceEquals(expected, typeof(Popup).GetField("_parentWindow", Private)!.GetValue(popup)), "popup logical parent matches the latest owner");
            Require(ReferenceEquals(expected, typeof(Popup).GetField("_macOSObservedOwner", Private)!.GetValue(popup)), "owner observation follows the latest request");
            Require(expected.ActiveExternalPopups.Contains(popup), "latest owner registers the popup");
            foreach (var candidate in new[] { owner, Window.GetMacOSPhysicalWindow(first!.Child)!, Window.GetMacOSPhysicalWindow(second!.Child)! })
                if (!ReferenceEquals(candidate, expected)) Require(!candidate.ActiveExternalPopups.Contains(popup), "previous owner has no stale popup registration");
            foreach (var property in TextProperties)
            {
                Require(ReferenceEquals(BindingOperations.GetBinding(surface, property)?.Source, expected), "latest binding source for " + property.Name);
                Require(Equals(surface.GetValue(property), expected.GetValue(property)), "latest effective value for " + property.Name);
            }
            Require(editor!.Text == Body && editor.SelectionStart == 4 && editor.SelectionLength == 4 && editor.SelectedText == "中文🙂", "text and complete CJK/emoji selection are retained");
            Require(ReferenceEquals(Keyboard.FocusedElement, editor), "managed editor focus is retained");
            Require((scenario is 5 or 6) == !ReferenceEquals(original, surface), "only reopening replaces the physical host");
            Require(Observe(true), "qualified native focus after reparent");
            Capture("after");
            scenes.Add(new { name = Names[scenario], text = editor.Text, selectedText = editor.SelectedText,
                selection = new[] { editor.SelectionStart, editor.SelectionLength }, expectedParent = (long)(nint)expectedNative.Handle,
                actualParent = (long)(nint)native.ParentWindow!.Handle, sourceFont = expected.FontFamily.Source,
                actualFont = surface.FontFamily.Source, replacedHost = !ReferenceEquals(original, surface) });
            completed++; Next();
        }
        void Finish()
        {
            if (finished) return; finished = true;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
                index, custom, software, completed, expected = ordinaryOnly ? 1 : Names.Length, passed = failures.Count == 0 && completed == (ordinaryOnly ? 1 : Names.Length),
                checks, failures, focus = observations, scenes, pixelChecks, ordinaryOnly, requireForeground = true, foregroundQualified = qualified && observations.Count > 0,
                renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD"), nativePreloadRequests,
                lastState,
                nativePreloadScope = "SDK load preflight; dormant modules are not functionally exercised", physicalInput = false, desktopScreenshot = false,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(UIElement).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location, Assembly.GetExecutingAssembly().Location }
            }));
            ClosePopups(); owner.Close(); app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false);
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(40), _ =>
        {
            if (finished) return;
            try
            {
                Dispatcher.CurrentDispatcher.ProcessQueue();
                if (stage == 0) { Next(); return; }
                owner.UpdateLayout(); Surface().UpdateLayout();
                if (stage == 1) { if (Observe(false)) Mutate(); }
                else
                {
                    var surface = Surface();
                    if (scenario is 5 or 6 && !reopenedFocus)
                    {
                        reopenedFocus = true; surface.Activate(); editor!.Focus();
                        var view = Runtime.GetNSObject<NSView>(surface.Handle)!;
                        view.Window!.MakeKeyWindow(); view.Window.MakeFirstResponder(view);
                    }
                    var expected = scenario is 5 or 6 ? owner : Window.GetMacOSPhysicalWindow(scenario == 7 ? first!.Child : second!.Child)!;
                    var actual = Native(surface);
                    lastState = new { scenario, popupOpen = popup!.IsOpen, expectedParent = (long)(nint)Native(expected).Handle,
                        actualParent = (long)(nint)(actual.ParentWindow?.Handle ?? default), sourceFont = expected.FontFamily.Source,
                        actualFont = surface.FontFamily.Source, text = editor!.Text, selection = new[] { editor.SelectionStart, editor.SelectionLength },
                        frontmost = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1, processId = Environment.ProcessId };
                    if (Native(surface).ParentWindow == Native(expected) && Observe(false)) Verify();
                }
                if (elapsed.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("Owner/focus did not settle in scene " + Names[scenario]);
            }
            catch (Exception error) { failures.Add(error.ToString()); if (observations.Count == 0) qualified = false; Finish(); }
        });
        app.Run(); timer.Invalidate();
        return failures.Count == 0 && completed == Names.Length ? 0 : qualified ? 1 : 3;
    }

    private sealed class ChildWindowHook : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Remove(nint self, nint selector, nint child);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Add(nint self, nint selector, nint child, nint order);
        private readonly nint _method, _original, _target, _child;
        private readonly Delegate _keepAlive;
        private readonly Action _action;
        private readonly bool _after;
        internal bool Fired; internal Exception? Error;
        internal ChildWindowHook(NSWindow target, NSWindow child, bool adding, bool after, Action action)
        {
            _target = target.Handle; _child = child.Handle; _action = action; _after = after;
            nint cls = ObjectClass(_target), selector = ObjCRuntime.Selector.GetHandle(adding ? "addChildWindow:ordered:" : "removeChildWindow:");
            nint inherited = InstanceMethod(cls, selector); _original = Implementation(inherited);
            AddMethod(cls, selector, _original, TypeEncoding(inherited)); _method = InstanceMethod(cls, selector);
            if (adding)
            {
                var original = Marshal.GetDelegateForFunctionPointer<Add>(_original);
                _keepAlive = new Add((self, sel, value, order) => { if (!_after) Fire(self, value); original(self, sel, value, order); if (_after) Fire(self, value); });
            }
            else
            {
                var original = Marshal.GetDelegateForFunctionPointer<Remove>(_original);
                _keepAlive = new Remove((self, sel, value) => { if (!_after) Fire(self, value); original(self, sel, value); if (_after) Fire(self, value); });
            }
            SetImplementation(_method, Marshal.GetFunctionPointerForDelegate(_keepAlive));
        }
        private void Fire(nint target, nint child)
        {
            if (Fired || target != _target || child != _child) return;
            Fired = true;
            try { _action(); } catch (Exception error) { Error = error; }
        }
        public void Dispose() { SetImplementation(_method, _original); GC.KeepAlive(_keepAlive); }
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_getClass")] private static extern nint ObjectClass(nint value);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_getInstanceMethod")] private static extern nint InstanceMethod(nint cls, nint selector);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "method_getImplementation")] private static extern nint Implementation(nint method);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "method_getTypeEncoding")] private static extern nint TypeEncoding(nint method);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addMethod")] private static extern bool AddMethod(nint cls, nint selector, nint method, nint types);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "method_setImplementation")] private static extern nint SetImplementation(nint method, nint implementation);
    }
    [Register("JaliumPopupOwnerValidationV303")]
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}
