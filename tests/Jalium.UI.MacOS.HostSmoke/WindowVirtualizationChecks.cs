using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowVirtualizationChecks
{
    private const int ItemCount = 5000;

    internal static int Run()
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        var failures = new List<string>();
        int passed = 0;
        app.BeginInvokeOnMainThread(() =>
        {
            foreach (bool custom in new[] { false, true })
            foreach (bool self in new[] { false, true })
            foreach (Orientation orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
            {
                Window? owner = null;
                try
                {
                    var rows = Rows(orientation);
                    var viewer = self ? null : Viewer(rows);
                    owner = Owner(custom, viewer ?? (UIElement)rows);
                    Application.Current!.MainWindow = owner;
                    owner.Show(); owner.UpdateLayout();
                    viewer ??= Descendant<ScrollViewer>(rows)!;
                    Require(viewer != null, "self mode did not create a viewer");
                    Require(viewer.CanContentScroll, "viewer did not delegate scrolling");
                    Require(self || ReferenceEquals(rows.ScrollOwner, viewer), "outer viewer was not adopted");
                    Require(!self || rows.ScrollOwner == null, "self mode unexpectedly acquired an outer owner");
                    Require(!self ? Descendant<ScrollViewer>(rows) == null : true, "outer mode created a second viewer");
                    CheckRange(rows, atEnd: false);
                    End(viewer, orientation); owner.UpdateLayout();
                    CheckRange(rows, atEnd: true);
                    Start(viewer, orientation); owner.UpdateLayout();
                    CheckRange(rows, atEnd: false);
                    owner.Width = owner.MinWidth; owner.Height = owner.MinHeight; owner.UpdateLayout();
                    End(viewer, orientation); owner.UpdateLayout();
                    CheckRange(rows, atEnd: true);
                    Start(viewer, orientation); owner.UpdateLayout();
                    CheckRange(rows, atEnd: false);
                    passed++;
                    Console.WriteLine($"PASS virtualized content: custom={custom}; self={self}; orientation={orientation}; lastItemReached=True; resized=True");
                }
                catch (Exception error)
                {
                    failures.Add($"custom={custom}; self={self}; orientation={orientation}: {error.Message}");
                    Console.Error.WriteLine(error);
                }
                finally { owner?.Close(); }
            }
            Save("host-result.json", new { passed, expected = 8, failures });
            app.Terminate(app);
        });
        app.Run();
        return failures.Count == 0 ? 0 : 1;
    }

    internal static int Observe(bool custom)
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        var rows = Rows(Orientation.Vertical);
        var viewer = Viewer(rows);
        AutomationProperties.SetName(viewer, "五千项虚拟化列表");
        var status = Label("", 14);
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(Label("五千项列表", 22));
        header.Children.Add(Label("Tab 进入列表；方向键及 Page Down 滚动。F6 切换首尾，F7 切换独立滚动，F8 缩小窗口。", 14));
        header.Children.Add(status);
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(header);
        var slot = new Border { Child = viewer };
        Grid.SetRow(slot, 1); grid.Children.Add(slot);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var end = Action("切换首尾（F6）");
        var mode = Action("切换独立滚动（F7）");
        var size = Action("缩小窗口（F8）");
        var finish = Action("结束检查");
        actions.Children.Add(end); actions.Children.Add(mode); actions.Children.Add(size); actions.Children.Add(finish);
        Grid.SetRow(actions, 2); grid.Children.Add(actions);
        var owner = Owner(custom, grid); Application.Current!.MainWindow = owner;
        int endVisits = 0, startVisits = 0, modeChanges = 0, keyboardEvents = 0;
        bool self = false, closed = false, failed = false;
        ScrollViewer ActiveViewer() => self ? Descendant<ScrollViewer>(rows)! : viewer;
        void Record()
        {
            var active = ActiveViewer();
            var panel = Descendant<VirtualizingPanel>(rows);
            var visible = panel?.Children.Cast<UIElement>().Select(child => rows.ItemContainerGenerator.IndexFromContainer(child)).ToArray() ?? [];
            status.Text = $"{(self ? "独立滚动" : "外层滚动")} · 已实现 {visible.Length}/{ItemCount} 项 · 偏移 {active.VerticalOffset:0}";
            Save(custom ? "custom-observe.json" : "native-observe.json", new
            {
                custom, self, closed, failed, endVisits, startVisits, modeChanges, keyboardEvents,
                realized = visible, offset = active.VerticalOffset, extent = active.ExtentHeight,
                viewport = active.ViewportHeight, width = owner.Width, height = owner.Height,
                focused = (Keyboard.FocusedElement as Button)?.Content?.ToString(),
            });
            Console.WriteLine($"VIRTUALIZED STATE: custom={custom}; self={self}; realized={visible.Length}; offset={active.VerticalOffset}; range={visible.FirstOrDefault()}..{visible.LastOrDefault()}; closed={closed}; failed={failed}");
        }
        void Guard(Action action)
        {
            try { action(); owner.UpdateLayout(); Record(); }
            catch (Exception error) { failed = true; status.Text = "检查失败：" + error.Message; Console.Error.WriteLine(error); Record(); }
        }
        void ToggleEnd() => Guard(() =>
        {
            var active = ActiveViewer();
            bool atEnd = rows.ItemContainerGenerator.ContainerFromIndex(ItemCount - 1) != null;
            if (atEnd) { Start(active, Orientation.Vertical); startVisits++; }
            else { End(active, Orientation.Vertical); endVisits++; }
            owner.UpdateLayout(); CheckRange(rows, atEnd: !atEnd);
        });
        void ToggleMode() => Guard(() =>
        {
            if (self)
            {
                slot.Child = null; viewer.Content = rows; slot.Child = viewer;
            }
            else
            {
                slot.Child = null; viewer.Content = null; slot.Child = rows;
            }
            self = !self; modeChanges++;
            owner.UpdateLayout(); CheckRange(rows, atEnd: false);
        });
        void Resize() => Guard(() => { owner.Width = owner.MinWidth; owner.Height = owner.MinHeight; });
        end.Click += (_, _) => ToggleEnd(); mode.Click += (_, _) => ToggleMode();
        size.Click += (_, _) => Resize(); finish.Click += (_, _) => owner.Close();
        owner.PreviewKeyDown += (_, e) =>
        {
            keyboardEvents++;
            if (e.Key == Key.F6) { ToggleEnd(); e.Handled = true; }
            else if (e.Key == Key.F7) { ToggleMode(); e.Handled = true; }
            else if (e.Key == Key.F8) { Resize(); e.Handled = true; }
        };
        viewer.ScrollChanged += (_, _) => Record();
        var refresh = new Jalium.UI.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        refresh.Tick += (_, _) => Record();
        owner.Closed += (_, _) => { refresh.Stop(); closed = true; Record(); app.Terminate(app); };
        owner.Show(); owner.UpdateLayout(); end.Focus(); Record();
        refresh.Start();
        app.Run();
        return failed ? 1 : 0;
    }

    private static RazorItemsHost Rows(Orientation orientation)
    {
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            var row = new Button { Height = 36, MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (orientation == Orientation.Horizontal) row.Width = 240;
            row.SetBinding(ContentControl.ContentProperty, new Binding { Path = new PropertyPath("."), StringFormat = "第 {0:D4} 项 · 中文🙂" });
            return row;
        });
        return new RazorItemsHost { Orientation = orientation, ItemTemplate = template, ItemsSource = Enumerable.Range(1, ItemCount) };
    }

    private static ScrollViewer Viewer(RazorItemsHost rows) => new()
    {
        CanContentScroll = true, Content = rows,
        VerticalScrollBarVisibility = rows.Orientation == Orientation.Vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        HorizontalScrollBarVisibility = rows.Orientation == Orientation.Horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        IsScrollInertiaEnabled = false,
    };
    private static void CheckRange(RazorItemsHost rows, bool atEnd)
    {
        var panel = Descendant<VirtualizingPanel>(rows);
        Require(panel != null && panel.Children.Count is > 0 and < 150, "list realized an unbounded number of items");
        int wanted = atEnd ? ItemCount - 1 : 0, absent = atEnd ? 0 : ItemCount - 1;
        Require(rows.ItemContainerGenerator.ContainerFromIndex(wanted) != null, $"item {wanted + 1} was not realized");
        Require(rows.ItemContainerGenerator.ContainerFromIndex(absent) == null, $"item {absent + 1} was not recycled");
    }
    private static T? Descendant<T>(Visual root) where T : Visual
    {
        if (root is T match) return match;
        for (int i = 0; i < root.VisualChildrenCount; i++)
            if (root.GetVisualChild(i) is { } child && Descendant<T>(child) is { } found) return found;
        return null;
    }
    private static void End(ScrollViewer viewer, Orientation orientation)
    { if (orientation == Orientation.Vertical) viewer.ScrollToBottom(); else viewer.ScrollToEnd(); }
    private static void Start(ScrollViewer viewer, Orientation orientation)
    { if (orientation == Orientation.Vertical) viewer.ScrollToTop(); else viewer.ScrollToHome(); }
    private static TextBlock Label(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
    private static Button Action(string text) => new() { Content = text, Height = 38, Margin = new Thickness(0, 0, 8, 6) };
    private static Window Owner(bool custom, UIElement content) => new()
    {
        Title = "Jalium Virtualized Content v158 " + (custom ? "Custom" : "Native"), Width = 640, Height = 700,
        MinWidth = 520, MinHeight = 460, Content = content, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
        Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
    };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Save(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("JALIUM_VIRTUALIZED_CONTENT_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value));
    }
    private static ValidationDelegate Initialize(NSApplication app)
    {
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        return host;
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
