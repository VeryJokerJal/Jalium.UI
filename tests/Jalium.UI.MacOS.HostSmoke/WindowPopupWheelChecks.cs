using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Jalium.UI.MacOS;

// macOS normally keeps Popup in the owner overlay. This fixture explicitly
// creates the independent native bridge, without changing that product policy.
internal static class WindowPopupWheelChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(bool directContent = false)
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        int passed = 0;
        app.BeginInvokeOnMainThread(() =>
        {
            foreach (bool custom in new[] { false, true })
                for (int scenario = 0; scenario < 8; scenario++)
                {
                    Window? owner = null;
                    try
                    {
                        var editor = new TextBox { Text = "弹出窗口后的编辑🙂", Height = 40 };
                        owner = Owner(custom, editor, directContent);
                        Application.Current!.MainWindow = owner;
                        owner.Show(); owner.UpdateLayout(); editor.Focus();
                        using var native = new NativePopup(owner, new Border { Background = Brushes.White }, 320, 240);
                        UIElement target = native.Content;
                        MouseWheelEventArgs? preview = null, bubble = null;
                        PointerPoint? pointer = null;
                        native.Root.PreviewMouseWheel += (_, e) => { if (e.Phase != MouseWheelPhase.Cancelled) preview = e; };
                        target.MouseWheel += (_, e) => bubble = e;
                        target.AddHandler(PointerEvents.PointerWheelChangedEvent,
                            new PointerWheelChangedEventHandler((_, e) => pointer = e.Pointer));
                        var ownerView = Runtime.GetNSObject<NSView>(owner.Handle)!;
                        var responder = ownerView.Window!.FirstResponder;
                        var focus = Keyboard.FocusedElement;
                        Require(native.Native.ParentWindow == ownerView.Window && !native.Native.IsKeyWindow,
                            "popup changed owner or acquired key status on show");
                        using var scroll = new ScrollEvent();
                        if (scenario == 0)
                        {
                            native.Send(PlatformEvent("MouseDown", 0, 0, 0, 50, 50)); // Left press delivered only to the popup.
                            Require(Mouse.LeftButton == MouseButtonState.Pressed, "stale press was not seeded");
                            scroll.Vertical = -12; scroll.Precise = true;
                        }
                        else if (scenario == 1) { scroll.Horizontal = -6; scroll.Precise = false; }
                        else if (scenario == 2)
                        {
                            scroll.Vertical = -.125; scroll.Precise = true;
                            scroll.DirectPhase = NSEventPhase.Ended; scroll.Momentum = NSEventPhase.Began;
                        }
                        else if (scenario == 3)
                            native.Root.PreviewMouseWheel += (_, e) => e.Handled = true;
                        else if (scenario == 4)
                            native.Root.PreviewMouseWheel += (_, e) => { e.Handled = true; e.Cancel = true; };
                        else if (scenario == 5)
                        {
                            native.Send(PlatformEvent("MouseWheel", 31, .125f, -.25f, 50, 50));
                            Require(bubble != null && Mask(bubble) == 31 && pointer != null && PointerMask(pointer) == 31,
                                "complete ABI snapshot lost a button");
                            Console.WriteLine($"PASS {custom}/{scenario}: independent popup ABI button snapshot");
                            passed++; continue;
                        }
                        else if (scenario == 6)
                        {
                            native.Hide(); Require(!native.Native.IsVisible, "native hide left popup visible");
                            native.Dispose();
                            using var reopened = new NativePopup(owner, new Border { Background = Brushes.White }, 320, 240);
                            MouseWheelEventArgs? retried = null;
                            reopened.Content.MouseWheel += (_, e) => retried = e;
                            SendScroll(reopened.View, scroll);
                            Require(retried != null && Mask(retried) == SystemButtons(),
                                "recreated popup retained the previous button state");
                            Require(!native.Native.IsVisible && reopened.Native.IsVisible, "popup recreation retained old window");
                            Console.WriteLine($"PASS {custom}/{scenario}: native hide, dispose, recreate and scroll");
                            passed++; continue;
                        }
                        else
                        {
                            var viewer = List(directContent);
                            using var scrolling = new NativePopup(owner, viewer, 340, 300);
                            scrolling.Visual.UpdateLayout();
                            scrolling.Root.PreviewMouseWheel += (_, e) => Console.WriteLine($"POPUP PROTOCOL: source={e.OriginalSource?.GetType().Name}; x={e.HorizontalDelta}; y={e.VerticalDelta}; precise={e.HasPreciseScrollingDeltas}");
                            scroll.Vertical = -48; scroll.Precise = true;
                            SendScroll(scrolling.View, scroll);
                            Require(viewer.VerticalOffset > 0, "precise popup scroll did not move content");
                            double vertical = viewer.VerticalOffset;
                            scrolling.Visual.UpdateLayout();
                            scroll.Vertical = 0; scroll.Horizontal = -48;
                            SendScroll(scrolling.View, scroll);
                            Console.WriteLine($"POPUP PROTOCOL OFFSET: verticalBefore={vertical}; x={viewer.HorizontalOffset}; y={viewer.VerticalOffset}; extent={viewer.ExtentWidth}x{viewer.ExtentHeight}; viewport={viewer.ViewportWidth}x{viewer.ViewportHeight}");
                            Require(viewer.HorizontalOffset > 0 && viewer.VerticalOffset == vertical,
                                "horizontal popup scroll lost or moved the other axis");
                            Require(ReferenceEquals(focus, Keyboard.FocusedElement), "scrolling moved managed keyboard focus");
                            if (directContent)
                            {
                                var rows = (StackPanel)viewer.Content!;
                                Require(rows.ScrollOwner == null, "physical content still owns the viewer");
                                viewer.CanContentScroll = true; scrolling.Visual.UpdateLayout();
                                Require(rows.ScrollOwner == viewer && viewer.HorizontalOffset == 0 && viewer.VerticalOffset == 0,
                                    "provider enable did not attach or reset old offsets");
                                scroll.Horizontal = 0; scroll.Vertical = -48; SendScroll(scrolling.View, scroll);
                                Require(viewer.VerticalOffset > 0, "enabled provider cannot scroll");
                                viewer.CanContentScroll = false; scrolling.Visual.UpdateLayout();
                                Require(rows.ScrollOwner == null && viewer.HorizontalOffset == 0 && viewer.VerticalOffset == 0,
                                    "physical mode retained provider or its old offsets");
                                SendScroll(scrolling.View, scroll);
                                scroll.Vertical = 0; scroll.Horizontal = -48; SendScroll(scrolling.View, scroll);
                                Require(viewer.HorizontalOffset == 48 && viewer.VerticalOffset == 48,
                                    "reselected physical mode lost a scrolling axis");
                            }
                            viewer.IsDeferredScrollingEnabled = true;
                            var horizontalBar = (Jalium.UI.Controls.Primitives.ScrollBar)typeof(ScrollViewer).GetField("_horizontalScrollBar", Private)!.GetValue(viewer)!;
                            var verticalBar = (Jalium.UI.Controls.Primitives.ScrollBar)typeof(ScrollViewer).GetField("_verticalScrollBar", Private)!.GetValue(viewer)!;
                            new Jalium.UI.Automation.Peers.ScrollBarAutomationPeer(horizontalBar).SetValue(96);
                            new Jalium.UI.Automation.Peers.ScrollBarAutomationPeer(verticalBar).SetValue(84);
                            scrolling.Visual.UpdateLayout();
                            Require(viewer.HorizontalOffset == 96 && viewer.VerticalOffset == 84 &&
                                viewer.ContentHorizontalOffset == 96 && viewer.ContentVerticalOffset == 84,
                                "automation changed only a thumb or deferred its content offset");
                            Require(ReferenceEquals(focus, Keyboard.FocusedElement), "automation scrolling changed keyboard focus");
                            Console.WriteLine($"PASS {custom}/{scenario}: precise two-axis popup ScrollViewer");
                            passed++; continue;
                        }
                        SendScroll(native.View, scroll);
                        Require(preview != null && Mask(preview) == SystemButtons(),
                            "native wheel button snapshot disagreed with AppKit");
                        double scale = scroll.Precise ? 120.0 / 48 : 120.0 / 3;
                        Require(Math.Abs(preview!.HorizontalDelta + scroll.Horizontal * scale) < .00001 &&
                            Math.Abs(preview.VerticalDelta - scroll.Vertical * scale) < .00001,
                            "native popup changed horizontal/vertical deltas");
                        Require(preview.HasPreciseScrollingDeltas == scroll.Precise &&
                            preview.Phase == (MouseWheelPhase)scroll.DirectPhase && preview.MomentumPhase == (MouseWheelPhase)scroll.Momentum,
                            "native popup changed precise or gesture metadata");
                        Require((scenario >= 3 && scenario <= 4) ? bubble == null && pointer == null :
                            bubble != null && pointer != null && Mask(bubble) == Mask(preview) && PointerMask(pointer) == Mask(preview),
                            "preview, bubble or pointer routing changed");
                        Require(ReferenceEquals(focus, Keyboard.FocusedElement) && ownerView.Window.FirstResponder == responder,
                            "native popup wheel moved editor focus or first responder");
                        Console.WriteLine($"PASS {custom}/{scenario}: native view wheel -> platform ABI -> popup routes");
                        passed++;
                    }
                    catch (Exception e) { Console.Error.WriteLine($"FAIL {custom}/{scenario}: {e}"); }
                    finally { owner?.Close(); }
                }
            if (directContent)
            {
                // DisableDefaults keeps the input fixture minimal. Explicitly
                // load the real theme for the separate default-template checks.
                Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
                foreach (bool custom in new[] { false, true })
                    foreach (ItemsControl items in new ItemsControl[] { new ListBox(), new ListView(), new TreeView() })
                    {
                        Window? owner = null;
                        try
                        {
                            items.Width = 320; items.Height = 240;
                            items.ItemsSource = Enumerable.Range(1, 600).Select(i => $"第 {i} 项🙂").ToArray();
                            owner = Owner(custom, items, true); Application.Current!.MainWindow = owner;
                            owner.Show(); owner.UpdateLayout();
                            var viewer = Find<ScrollViewer>(items);
                            Console.WriteLine($"POPUP TEMPLATE: control={items.GetType().Name}; template={items.Template != null}; parentMode={ScrollViewer.GetCanContentScroll(items)}; viewer={viewer != null}; mode={viewer?.CanContentScroll}");
                            Require(viewer != null && viewer.CanContentScroll, "default items template did not enable content scrolling");
                            var presenter = Find<ItemsPresenter>(items)!;
                            Require(presenter.ScrollOwner == viewer, "default items presenter did not attach to viewer");
                            if (items is ListBox)
                            {
                                var panel = Find<VirtualizingStackPanel>(items)!;
                                Require(panel != null && panel.Children.Count < 200, "default list template stopped virtualizing");
                            }
                            ScrollViewer.SetCanContentScroll(items, false); owner.UpdateLayout();
                            Require(!viewer!.CanContentScroll && presenter.ScrollOwner == null,
                                "parent attached property did not disable content scrolling");
                            ScrollViewer.SetCanContentScroll(items, true); owner.UpdateLayout();
                            Require(viewer.CanContentScroll && presenter.ScrollOwner == viewer,
                                "parent attached property did not restore content scrolling");
                            Console.WriteLine($"PASS {custom}/{items.GetType().Name}: default template, provider binding and virtualization");
                            passed++;
                        }
                        catch (Exception e) { Console.Error.WriteLine($"FAIL {custom}/{items.GetType().Name}: {e}"); }
                        finally { owner?.Close(); }
                    }
            }
            NativeMethods.PlatformQuit(0);
        });
        app.Run();
        int expected = directContent ? 22 : 16;
        Console.WriteLine($"macOS popup wheel native host checks: {passed}/{expected} passed");
        return passed == expected ? 0 : 1;
    }

    internal static int Observe(bool custom, bool directContent = false)
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        var status = new TextBlock { Text = "打开列表后滚动，再关闭并继续编辑。", FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var editor = new TextBox { Text = "滚动后继续编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "弹出列表后的编辑框");
        panel.Children.Add(new TextBlock { Text = "弹出列表的滚动", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = directContent ? "滚动两种列表。按 F6 切换滚动方式，按 Escape 返回编辑。" : "分别滚动窗口内列表和独立列表，检查横向、纵向滚动及返回编辑。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(status); panel.Children.Add(editor);
        var overlayButton = Button("打开窗口内列表"); panel.Children.Add(overlayButton);
        var nativeButton = Button("打开独立列表"); panel.Children.Add(nativeButton);
        var mode = Button("切换滚动方式（F6）");
        if (directContent) panel.Children.Add(mode);
        var close = Button("关闭列表并返回编辑框"); panel.Children.Add(close);
        var size = Button("切换到最小尺寸"); panel.Children.Add(size);
        var finish = Button("结束检查"); panel.Children.Add(finish);
        var owner = Owner(custom, panel, directContent);
        Application.Current!.MainWindow = owner;
        NativePopup? independent = null;
        Popup? overlay = null;
        ScrollViewer? currentViewer = null;
        int nativeWheels = 0, overlayWheels = 0, opens = 0, closes = 0;
        bool failed = false, closed = false;
        void CloseLists()
        {
            bool hadList = independent != null || overlay != null;
            independent?.Dispose(); independent = null;
            if (overlay != null) { overlay.IsOpen = false; overlay = null; }
            currentViewer = null;
            if (hadList) closes++;
            editor.Focus();
        }
        void Record(MouseWheelEventArgs e, ScrollViewer viewer, bool native)
        {
            if (e.Phase == MouseWheelPhase.Cancelled) return;
            if (native) nativeWheels++; else overlayWheels++;
            uint expected = SystemButtons();
            if (Mask(e) != expected) failed = true;
            Console.WriteLine($"POPUP WHEEL: native={native}; buttons={Mask(e)}; system={expected}; modifiers={e.KeyboardModifiers}; x={e.HorizontalDelta}; y={e.VerticalDelta}; precise={e.HasPreciseScrollingDeltas}; phase={e.Phase}; momentum={e.MomentumPhase}; focus={FocusedName()}");
            // Read offsets after ScrollViewer's bubbling handler has run.
            app.BeginInvokeOnMainThread(() =>
            {
                if (!ReferenceEquals(currentViewer, viewer)) return;
                status.Text = $"{(native ? "独立" : "窗口内")}列表已滚动；横向 {viewer.HorizontalOffset:0.##}，纵向 {viewer.VerticalOffset:0.##}。";
                Console.WriteLine($"POPUP OFFSET: native={native}; x={viewer.HorizontalOffset}; y={viewer.VerticalOffset}");
            });
        }
        void Open(bool native)
        {
            CloseLists();
            var viewer = List(directContent); currentViewer = viewer;
            if (directContent) viewer.ScrollChanged += (_, _) =>
            {
                if (!ReferenceEquals(currentViewer, viewer)) return;
                status.Text = $"{(native ? "独立" : "窗口内")}列表当前：横向 {viewer.HorizontalOffset:0.##}，纵向 {viewer.VerticalOffset:0.##}。";
                Console.WriteLine($"POPUP METRIC: native={native}; provider={viewer.CanContentScroll}; x={viewer.HorizontalOffset}; y={viewer.VerticalOffset}");
            };
            if (native)
            {
                independent = new NativePopup(owner, viewer, 340, 300);
                independent.Root.PreviewMouseWheel += (_, e) => Record(e, viewer, true);
            }
            else
            {
                viewer.Height = 180;
                overlay = new Popup { Child = viewer, PlacementTarget = overlayButton, Placement = PlacementMode.Bottom,
                    StaysOpen = true, ShouldConstrainToRootBounds = true };
                overlay.IsOpen = true;
                viewer.PreviewMouseWheel += (_, e) => Record(e, viewer, false);
            }
            opens++; status.Text = native ? "独立列表已打开。滚动后关闭，再打开一次。" : "窗口内列表已打开。滚动后按 Escape 返回编辑框。";
            owner.UpdateLayout(); editor.Focus();
            Console.WriteLine($"POPUP OPEN: native={native}; opens={opens}; ownerKey={app.KeyWindow?.Title}; focus={FocusedName()}");
        }
        void Safe(Action action)
        {
            try { action(); }
            catch (Exception e) { failed = true; status.Text = "检查失败：" + e.Message; Console.Error.WriteLine("POPUP FAIL: " + e); }
        }
        overlayButton.Click += (_, _) => Safe(() => Open(false));
        nativeButton.Click += (_, _) => Safe(() => Open(true));
        void SwitchMode()
        {
            if (currentViewer == null) Open(false);
            var viewer = currentViewer!;
            viewer.CanContentScroll = !viewer.CanContentScroll;
            independent?.Visual.UpdateLayout(); owner.UpdateLayout(); editor.Focus();
            status.Text = "滚动方式已切换；列表已回到起点。";
            Console.WriteLine($"POPUP MODE: provider={viewer.CanContentScroll}; x={viewer.HorizontalOffset}; y={viewer.VerticalOffset}; owner={((StackPanel)viewer.Content!).ScrollOwner != null}");
        }
        mode.Click += (_, _) => Safe(SwitchMode);
        close.Click += (_, _) => Safe(CloseLists);
        size.Click += (_, _) => { CloseLists(); owner.Width = 520; owner.Height = directContent ? 590 : 540; owner.UpdateLayout(); };
        finish.Click += (_, _) => owner.Close();
        owner.PreviewKeyDown += (_, e) =>
        {
            if (directContent && e.Key == Key.F6)
            { Safe(SwitchMode); e.Handled = true; }
            if (e.Key == Key.Escape && (independent != null || overlay != null))
            { Safe(CloseLists); status.Text = "列表已关闭，可以继续编辑。"; e.Handled = true; }
        };
        owner.Closing += (_, _) => CloseLists();
        owner.Closed += (_, _) => closed = true;
        owner.Show(); owner.UpdateLayout(); editor.Focus();
        var watch = Stopwatch.StartNew(); string? last = null;
        using var timer = NSTimer.CreateRepeatingTimer(.2, _ =>
        {
            string state = $"active={app.Active}; key={app.KeyWindow?.Title}; focus={FocusedName()}; size={owner.ActualWidth}x{owner.ActualHeight}; text={editor.Text}";
            if (state != last) { last = state; Console.WriteLine("POPUP STATE: " + state); }
            if (!closed && watch.Elapsed.TotalSeconds < 900) return;
            if (!closed) owner.Close();
            NativeMethods.PlatformQuit(0);
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common); NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        app.Run(); timer.Invalidate(); CloseLists();
        Console.WriteLine($"POPUP COMPLETE: nativeWheels={nativeWheels}; overlayWheels={overlayWheels}; opens={opens}; closes={closes}; closed={closed}; failed={failed}; text={editor.Text}");
        return failed ? 1 : 0;
    }

    private static ValidationDelegate Initialize(NSApplication app)
    {
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        return host;
    }
    private static Window Owner(bool custom, UIElement content, bool directContent = false) => new()
    {
        Title = (directContent ? "Jalium Scroll Content v153 " : "Jalium Popup Wheel v152 ") + (custom ? "Custom" : "Native"),
        Width = 680, Height = directContent ? 620 : 570, MinWidth = 520, MinHeight = directContent ? 590 : 540,
        TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
        WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = content,
        Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
    };
    private static Button Button(string text) => new() { Content = text, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 350 };
    private static ScrollViewer List(bool directContent = false)
    {
        var rows = new StackPanel { Width = 980, Spacing = 8, Margin = new Thickness(12) };
        for (int i = 1; i <= 30; i++) rows.Children.Add(new TextBlock
        { Text = $"第 {i:00} 项：中文🙂 Miii é · 滚动列表中的完整内容；关闭和重新打开后仍可操作。横向滚动后能读到末尾编号 {i:00}。", FontSize = 16, Height = 28 });
        // Retain the v152 wrapped fixture; v153 exercises direct IScrollInfo
        // content and mode changes through the same native popup input path.
        var viewer = new ScrollViewer { Width = 340, Height = 300, Content = directContent ? rows : new Border { Child = rows },
            CanContentScroll = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x2a, 0x33)) };
        if (directContent)
        {
            foreach (string axis in new[] { "horizontal", "vertical" })
            {
                var bar = (DependencyObject)typeof(ScrollViewer).GetField("_" + axis + "ScrollBar", Private)!.GetValue(viewer)!;
                AutomationProperties.SetName(bar, axis == "horizontal" ? "横向滚动列表" : "纵向滚动列表");
            }
        }
        return viewer;
    }
    private static T? Find<T>(Visual visual) where T : Visual
    {
        if (visual is T value) return value;
        for (int i = 0; i < visual.VisualChildrenCount; i++)
            if (Find<T>(visual.GetVisualChild(i)!) is { } found) return found;
        return null;
    }
    private static string FocusedName() => Keyboard.FocusedElement is DependencyObject d ? AutomationProperties.GetName(d) : "none";
    private static uint Mask(MouseEventArgs e) => (e.LeftButton == MouseButtonState.Pressed ? 1u : 0) |
        (e.RightButton == MouseButtonState.Pressed ? 2u : 0) | (e.MiddleButton == MouseButtonState.Pressed ? 4u : 0) |
        (e.XButton1 == MouseButtonState.Pressed ? 8u : 0) | (e.XButton2 == MouseButtonState.Pressed ? 16u : 0);
    private static uint PointerMask(PointerPoint p) => (p.Properties.IsLeftButtonPressed ? 1u : 0) |
        (p.Properties.IsRightButtonPressed ? 2u : 0) | (p.Properties.IsMiddleButtonPressed ? 4u : 0) |
        (p.Properties.IsXButton1Pressed ? 8u : 0) | (p.Properties.IsXButton2Pressed ? 16u : 0);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static object PlatformEvent(string type, uint buttons, float dx, float dy, float x, float y)
    {
        Type t = Type.GetType("Jalium.UI.Controls.Platform.PlatformEvent, Jalium.UI.Managed", throwOnError: true)!;
        object packet = Activator.CreateInstance(t)!;
        foreach (var (name, value) in new (string, object)[] { ("MouseButtons", buttons), ("HasMouseButtonStates", true),
            ("WheelDeltaX", dx), ("WheelDeltaY", dy), ("MouseX", x), ("MouseY", y), ("Button", 0) })
            t.GetField(name)!.SetValue(packet, value);
        t.GetField("Type")!.SetValue(packet, Enum.Parse(t.GetField("Type")!.FieldType, type));
        return packet;
    }
    private static uint SystemButtons() => (uint)NSEvent.CurrentPressedMouseButtons & 31;
    private static void SendScroll(NSView view, NSEvent e) => view.ScrollWheel(e);

    private sealed class NativePopup : IDisposable
    {
        private bool _disposed;
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicProperties)]
        private readonly Type _type = Type.GetType("Jalium.UI.Controls.Primitives.PopupWindow, Jalium.UI.Managed", throwOnError: true)!;
        internal FrameworkElement Visual { get; }
        internal FrameworkElement Root { get; }
        internal UIElement Content { get; }
        internal NSView View { get; }
        internal NSWindow Native { get; }
        internal NativePopup(Window owner, UIElement content, int width, int height)
        {
            Content = content;
            var rootType = Type.GetType("Jalium.UI.Controls.Primitives.PopupRoot, Jalium.UI.Managed", throwOnError: true)!;
            Root = (FrameworkElement)Activator.CreateInstance(rootType, Private | BindingFlags.Public, null,
                [new Popup { StaysOpen = true }, content, false], null)!;
            Root.Width = width; Root.Height = height;
            var type = _type;
            Visual = (FrameworkElement)Activator.CreateInstance(type, Private | BindingFlags.Public, null, [owner, Root], null)!;
            object platform = typeof(Window).GetField("_platformWindow", Private)!.GetValue(owner)!;
            object[] origin = [0, 0];
            Type platformType = Type.GetType("Jalium.UI.Controls.Platform.NativePlatformWindow, Jalium.UI.Managed", throwOnError: true)!;
            Require((bool)platformType.GetMethod("TryGetClientOrigin")!.Invoke(platform, origin)!, "owner client origin unavailable");
            type.GetMethod("Show", Private)!.Invoke(Visual,
                [(int)origin[0] + (int)Math.Round((owner.ActualWidth + 16) * owner.DpiScale), (int)origin[1], width, height]);
            Visual.Measure(new Size(width / owner.DpiScale, height / owner.DpiScale));
            Visual.Arrange(new Rect(0, 0, width / owner.DpiScale, height / owner.DpiScale));
            nint handle = (nint)type.GetProperty("Handle", Private)!.GetValue(Visual)!;
            View = Runtime.GetNSObject<NSView>(handle)!;
            Native = View.Window!; Native.Title = "Jalium Popup Wheel v152 List";
        }
        internal void Send(object packet) => _type.GetMethod("OnPlatformEvent", Private)!.Invoke(Visual, [packet]);
        internal void Hide() => _type.GetMethod("Hide", Private)!.Invoke(Visual, null);
        public void Dispose() { if (_disposed) return; _disposed = true; ((IDisposable)Visual).Dispose(); }
    }

    [Register("JaliumPopupWheelEventV152")]
    private sealed class ScrollEvent : NSEvent
    {
        internal double Horizontal, Vertical = -3;
        internal bool Precise;
        internal NSEventPhase DirectPhase = NSEventPhase.None, Momentum = NSEventPhase.None;
        public override NSEventType Type => NSEventType.ScrollWheel;
        public override CGPoint LocationInWindow => new(50, 50);
        public override NSEventModifierMask ModifierFlags => 0;
        public override NFloat ScrollingDeltaX => (NFloat)Horizontal;
        public override NFloat ScrollingDeltaY => (NFloat)Vertical;
        public override bool HasPreciseScrollingDeltas => Precise;
        public override NSEventPhase Phase => DirectPhase;
        public override NSEventPhase MomentumPhase => Momentum;
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
