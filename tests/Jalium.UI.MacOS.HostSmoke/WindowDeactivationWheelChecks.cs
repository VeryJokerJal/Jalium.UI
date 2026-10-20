using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ObjCRuntime;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowDeactivationWheelChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 24) return 2;
        int variant = index % 8;
        bool custom = variant % 2 != 0, same = variant % 4 < 2, software = variant >= 4;
        string kind = new[] { "owner", "overlay", "native" }[index / 8];
        string output = Environment.GetEnvironmentVariable("JALIUM_DEACTIVATION_OUTPUT")!;
        Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var context = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (!software) context.DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var a = new Item("A"); var b = new Item("B");
        var list = new ListBox { Height = 130, SelectionMode = SelectionMode.Multiple };
        list.Items.Add(a); list.Items.Add(b); list.Items.Add(new Item("B")); list.SelectedItems.Add(a); list.SelectedItems.Add(b);
        Border Box(string text) => new() { Width = 170, Height = 70, Focusable = true, Background = Brushes.White,
            Child = new TextBlock { Text = text, Foreground = Brushes.Black, FontSize = 17 } };
        var target = Box("滚轮区域一"); var other = Box("滚轮区域二");
        var area = new Canvas { Height = 78 }; area.Children.Add(target); area.Children.Add(other); Canvas.SetLeft(other, 190);
        var run = new Run("第二段 Miii 中文🙂"); var document = new FlowDocument(new Paragraph(new Run("第一段")));
        document.Blocks.Add(new Paragraph(run));
        var rich = new RichTextBox(document) { Height = 100, FontSize = 18 };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "窗口失活与新滚轮手势", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "重复项替换后，两行 B 都应保持高亮。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(list); panel.Children.Add(area); panel.Children.Add(rich);
        var status = new TextBlock { Text = "等待窗口取得前台", FontSize = 15, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        using var owner = new ProbeWindow { Title = "Jalium Window v294 · 滚轮失活 " + index, Width = custom ? 440 : 760, Height = 640,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = panel };
        Popup? popup = null;
        if (kind != "owner")
        {
            var anchor = new Button { Content = "输入验证区域", Width = 140, Height = 40, HorizontalAlignment = HorizontalAlignment.Right };
            owner.Content = new Border { Padding = new Thickness(20), Child = anchor };
            var surface = new Border { Child = panel, Width = 440, Height = 620,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            surface.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
            surface.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            popup = new Popup { Child = surface, PlacementTarget = anchor, Placement = PlacementMode.Right,
                PreferExternalWindow = kind == "native", ShouldConstrainToRootBounds = kind == "overlay", StaysOpen = true };
        }
        Application.Current!.MainWindow = owner; owner.Show(); owner.UpdateLayout();
        if (popup is not null) { popup.IsOpen = true; owner.UpdateLayout(); }
        Window Physical() => Window.GetMacOSPhysicalWindow(target) ?? owner;
        // makeKey requests a real AppKit transition. resignKey is a notification
        // hook and must not be called directly to initiate that transition.
        using var companion = new NSWindow(new CGRect(820, 220, 300, 180),
            NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false);
        companion.ReleasedWhenClosed = false; companion.Title = "Jalium Window v294 · AppKit 切换对照";
        companion.OrderFront(null);
        var checks = new List<string>(); var failures = new List<string>(); var scenarios = new List<object>(); var focus = new List<object>(); var traces = new List<object>();
        int unavailable = 0; bool done = false; var readiness = System.Diagnostics.Stopwatch.StartNew();
        void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        void Attempt(string name, Action body)
        {
            int first = checks.Count; Exception? error = null;
            try { body(); } catch (Exception ex) { error = ex; failures.Add(name + ": " + ex); }
            scenarios.Add(new { name, passed = error is null, checks = checks.Skip(first).ToArray(), error = error?.ToString() });
        }
        void Capture(string name)
        {
            var source = Physical();
            var image = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(source.ActualWidth * source.DpiScale),
                (int)Math.Ceiling(source.ActualHeight * source.DpiScale), source.DpiScale,
                drawing => { drawing.DrawRectangle(source.Background ?? Brushes.White, null, new Rect(0, 0, source.ActualWidth, source.ActualHeight)); source.Render(drawing); }, () => source.Handle != 0);
            Require(image is not null, "actual backend readback is available");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image!));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        bool Observe(NSView view)
        {
            int frontmost = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1;
            bool qualified = app.Active && frontmost == Environment.ProcessId && view.Window!.IsKeyWindow && view.Window.FirstResponder?.Handle == view.Handle;
            focus.Add(new { qualified, appActive = app.Active, frontmost, processId = Environment.ProcessId,
                nativeKey = view.Window!.IsKeyWindow, responder = view.Window.FirstResponder?.Handle.ToString(), ownResponder = view.Handle.ToString() });
            if (!qualified) unavailable++;
            return qualified;
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(.05, _ =>
        {
            if (done) return;
            var physical = Physical();
            if (kind == "native" && !physical.IsMacOSPopupHost && readiness.Elapsed.TotalSeconds < 15) return;
            var view = physical.Handle == 0 ? null : Runtime.GetNSObject<NSView>(physical.Handle);
            if (view?.Window is not { } native) { if (readiness.Elapsed.TotalSeconds < 15) return; failures.Add("native window was not ready"); done = true; }
            else
            {
                native.MakeKeyAndOrderFront(null); app.ActivateIgnoringOtherApps(true); native.MakeFirstResponder(view);
                if (!(app.Active && NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier == Environment.ProcessId && native.IsKeyWindow))
                { if (readiness.Elapsed.TotalSeconds < 15) return; unavailable++; done = true; }
                else
                {
                    done = true;
                    try
                    {
                        Require(physical.IsMacOSPopupHost == (kind == "native"), "the requested physical window or popup host is installed");
                        Require(Observe(view), "own active/key/responder and system foreground agree");
                        var viewport = new Rect(0, 0, physical.ActualWidth, physical.ActualHeight);
                        foreach (var element in new FrameworkElement[] { list, target, other, rich, status })
                        {
                            var bounds = new Rect(element.TranslatePoint(default, physical), element.RenderSize);
                            Require(viewport.Contains(bounds), "test content stays within its physical viewport: " + element.GetType().Name);
                        }
                        Capture("before");
                        Attempt("duplicate-occurrence-selection", () =>
                        {
                            list.SelectedItems[0] = b; owner.UpdateLayout();
                            Require(list.SelectedItems.Count == 2 && list.GetSelectedOccurrenceIndices().SequenceEqual(new[] { 1, 2 }), "replacement selects both distinct B occurrences");
                            for (int row = 0; row < 3; ++row)
                                Require(list.ItemContainerGenerator.ContainerFromIndex(row) is ListBoxItem container && container.IsSelected == (row != 0), "realized row " + row + " has the committed selection state");
                        });
                        var dispatcher = (WindowInputDispatcher)typeof(Window).GetField("_inputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(physical)!;
                        void Wheel(UIElement element, NSEventPhase phase, bool momentum = false)
                        {
                            var point = element.TranslatePoint(new Point(20, 20), physical);
                            using var e = new SampleWheel { At = view.ConvertPointToView(new CGPoint(point.X, point.Y), null), Direct = momentum ? NSEventPhase.None : phase,
                                Momentum = momentum ? phase : NSEventPhase.None };
                            view.ScrollWheel(e);
                        }
                        foreach (string callback in new[] { "property", "capture", "pressed" }) foreach (bool momentum in new[] { false, true })
                            Attempt(callback + (momentum ? "-momentum" : "-direct"), () =>
                            {
                                native.MakeKeyWindow(); native.MakeFirstResponder(view); Require(Observe(view), "scenario starts with an active own native responder");
                                dispatcher.ActivateMousePressedChain(target); Require(target.CaptureMouse(), "own target captures mouse state");
                                Wheel(target, NSEventPhase.Began);
                                bool restarted = false; int retiredCancels = 0, newCancels = 0, changes = 0; var next = same ? target : other;
                                void Restart()
                                {
                                    if (restarted) return; restarted = true; native.MakeKeyWindow(); native.MakeFirstResponder(view);
                                    Wheel(next, NSEventPhase.Began);
                                    if (momentum) { Wheel(next, NSEventPhase.Ended); Wheel(next, NSEventPhase.Began, true); }
                                }
                                MouseWheelEventHandler cancel = (_, e) => { if (e.Phase == MouseWheelPhase.Cancelled) { if (restarted) newCancels++; else retiredCancels++; } };
                                MouseWheelEventHandler change = (_, e) => { if (e.Phase == MouseWheelPhase.Changed || e.MomentumPhase == MouseWheelPhase.Changed) changes++; };
                                Action<DependencyProperty, object?, object?> property = (p, _, value) => { if (callback == "property" && p == Window.IsActiveProperty && Equals(value, false)) Restart(); };
                                Action<DependencyProperty, object?, object?> pressed = (p, _, value) => { if (callback == "pressed" && p == UIElement.IsPressedProperty && Equals(value, false)) Restart(); };
                                MouseEventHandler capture = (_, _) => { if (callback == "capture") Restart(); };
                                target.PreviewMouseWheel += cancel; if (!same) other.PreviewMouseWheel += cancel; next.MouseWheel += change;
                                physical.PropertyChangedInternal += property; target.PropertyChangedInternal += pressed; target.LostMouseCapture += capture;
                                try
                                {
                                    companion.MakeKeyAndOrderFront(null);
                                    Require(restarted, "native resign-key callback starts a successor gesture");
                                    Require(retiredCancels == 1 && newCancels == 0, "only the retired wheel receives cancellation");
                                    Require(physical.IsActive && native.IsKeyWindow, "callback reactivation supersedes old window deactivation");
                                    Wheel(same ? other : target, NSEventPhase.Changed, momentum);
                                    Require(changes == 1 && newCancels == 0, "successor gesture keeps its target when the SDK cursor position changes");
                                    Require(Observe(view), "scenario ends with its own foreground native responder");
                                    traces.Add(new { callback, momentum, same, restarted, retiredCancels, newCancels, changes });
                                }
                                finally
                                {
                                    physical.PropertyChangedInternal -= property; target.PropertyChangedInternal -= pressed; target.LostMouseCapture -= capture;
                                    target.PreviewMouseWheel -= cancel; if (!same) other.PreviewMouseWheel -= cancel; next.MouseWheel -= change;
                                    target.ReleaseMouseCapture(); Wheel(next, NSEventPhase.Cancelled, momentum); native.MakeKeyWindow(); native.MakeFirstResponder(view);
                                }
                            });
                        Attempt("logical-text-selection", () =>
                        {
                            Require(rich.Focus(), "rich editor regains focus after wheel reentry");
                            rich.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(2)!);
                            Require(rich.Selection.Text == "第二", "selection after a paragraph separator returns the authored source characters");
                            Require(document.GetText() == "第一段\n第二段 Miii 中文🙂\n", "structural and authored text remain intact");
                        });
                        status.Text = failures.Count == 0 ? "新手势、两行高亮与富文本选区检查通过。" : "检查发现问题，请查看记录。";
                        Capture("after");
                    }
                    catch (Exception error) { failures.Add(error.ToString()); }
                }
            }
            if (!done) return;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { index, custom, software, same, kind,
                passed = failures.Count == 0 && unavailable == 0, qualifiedWithoutForeground = failures.Count == 0,
                checks, failures, scenarios, focus, traces, foregroundUnavailable = unavailable, processId = Environment.ProcessId,
                physicalInput = false, desktopScreenshot = false, renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD") }));
            companion.Close(); if (popup is not null) popup.IsOpen = false; owner.Close(); host.AllowQuit = true; app.Terminate(app);
        });
        app.Run(); return failures.Count == 0 && unavailable == 0 ? 0 : 1;
    }
    private sealed record Item(string Text) { public override string ToString() => Text; }
    private sealed class ProbeWindow : Window, IDisposable { public void Dispose() => Close(); }
    [Register("JaliumWindowDeactivationWheelV294")]
    private sealed class SampleWheel : NSEvent
    {
        internal CGPoint At; internal NSEventPhase Direct, Momentum;
        public override NSEventType Type => NSEventType.ScrollWheel;
        public override CGPoint LocationInWindow => At;
        public override NSEventModifierMask ModifierFlags => 0;
        public override NFloat ScrollingDeltaX => 0;
        public override NFloat ScrollingDeltaY => -11;
        public override bool HasPreciseScrollingDeltas => true;
        public override NSEventPhase Phase => Direct;
        public override NSEventPhase MomentumPhase => Momentum;
    }
    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        private bool _started; internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
    }
}
