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

internal static class WindowAttachedBindingChecks
{
    internal static int Run()
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        int passed = 0;
        var failures = new List<string>();
        app.BeginInvokeOnMainThread(() =>
        {
            foreach (bool custom in new[] { false, true })
                for (int syntax = 0; syntax < 3; syntax++)
                {
                    Window? owner = null;
                    try
                    {
                        var rows = Rows();
                        var source = new Border();
                        var viewer = Viewer(rows, source, syntax);
                        var editor = Editor();
                        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
                        panel.Children.Add(editor); panel.Children.Add(viewer);
                        owner = Owner(custom, panel); Application.Current!.MainWindow = owner;
                        owner.Show(); owner.UpdateLayout();
                        Require(editor.Focus(), "editor did not acquire focus");
                        var responder = app.KeyWindow?.FirstResponder;
                        Require(!viewer.CanContentScroll && rows.ScrollOwner == null, "initial physical mode was lost");
                        ScrollViewer.SetCanContentScroll(source, true); owner.UpdateLayout();
                        Require(viewer.CanContentScroll && ReferenceEquals(rows.ScrollOwner, viewer), "source did not attach the content provider");
                        viewer.LineDown(); owner.UpdateLayout();
                        Require(viewer.VerticalOffset > 0, "content provider did not scroll");
                        viewer.CanContentScroll = false; owner.UpdateLayout();
                        Require(!ScrollViewer.GetCanContentScroll(source) && rows.ScrollOwner == null,
                            "target did not write back or detach the content provider");
                        Require(viewer.VerticalOffset == 0, "mode change retained the previous offset");
                        viewer.LineDown(); owner.UpdateLayout();
                        Require(viewer.VerticalOffset > 0, "physical mode did not scroll");
                        ScrollViewer.SetCanContentScroll(source, true); owner.UpdateLayout();
                        Require(ReferenceEquals(rows.ScrollOwner, viewer) && viewer.VerticalOffset == 0,
                            "returning to content mode did not restore a fresh provider");
                        Require(ReferenceEquals(Keyboard.FocusedElement, editor) && ReferenceEquals(app.KeyWindow?.FirstResponder, responder),
                            "binding mode changes moved keyboard focus");
                        Require(viewer.GetBindingExpression(ScrollViewer.CanContentScrollProperty) != null, "write back removed the binding");
                        Console.WriteLine($"PASS attached binding: custom={custom}; syntax={syntax}; providerRoundTrip=True; retainedFocus=True");
                        passed++;
                    }
                    catch (Exception error)
                    {
                        failures.Add($"custom={custom}; syntax={syntax}: {error.Message}");
                        Console.Error.WriteLine("FAIL attached binding: " + error);
                    }
                    finally { owner?.Close(); }
                }
            Save("host-result.json", new { passed, expected = 6, failures });
            Console.WriteLine($"macOS attached binding host checks: {passed}/6 passed");
            app.Terminate(app);
        });
        app.Run();
        return passed == 6 ? 0 : 1;
    }

    internal static int Observe(bool custom)
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        var source = new Border();
        var rows = Rows();
        var viewer = Viewer(rows, source, 0);
        AutomationProperties.SetName(viewer, "滚动方式检查列表");
        var editor = Editor();
        var status = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "列表滚动方式", FontSize = 22, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = "用 F6 切换滚动方式，用 F7 从列表切换，再继续编辑。两种切换应保持一致。",
            FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White });
        panel.Children.Add(status); panel.Children.Add(editor); panel.Children.Add(viewer);
        var toggle = Button("切换滚动方式（F6）"); panel.Children.Add(toggle);
        var write = Button("从列表切换（F7）"); panel.Children.Add(write);
        var size = Button("切换到最小尺寸"); panel.Children.Add(size);
        var finish = Button("结束检查"); panel.Children.Add(finish);
        var outer = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var owner = Owner(custom, outer); Application.Current!.MainWindow = owner;
        int sourceChanges = 0, targetChanges = 0;
        bool failed = false, closed = false;
        void Record()
        {
            bool enabled = ScrollViewer.GetCanContentScroll(source);
            bool provider = ReferenceEquals(rows.ScrollOwner, viewer);
            if (enabled != viewer.CanContentScroll || enabled != provider) failed = true;
            status.Text = $"当前：{(enabled ? "内容滚动" : "物理滚动")}；列表已同步。切换次数 {sourceChanges + targetChanges}。";
            Save(custom ? "custom-observe.json" : "native-observe.json", new
            {
                custom, sourceChanges, targetChanges, enabled, viewer = viewer.CanContentScroll,
                provider, failed, closed, width = owner.Width, height = owner.Height,
                editor = editor.Text, focused = (Keyboard.FocusedElement as FrameworkElement)?.GetType().Name
            });
            Console.WriteLine($"ATTACHED STATE: custom={custom}; sourceChanges={sourceChanges}; targetChanges={targetChanges}; enabled={enabled}; provider={provider}; failed={failed}; closed={closed}");
        }
        void Change(bool fromTarget)
        {
            try
            {
                if (fromTarget) { viewer.CanContentScroll = !viewer.CanContentScroll; targetChanges++; }
                else { ScrollViewer.SetCanContentScroll(source, !ScrollViewer.GetCanContentScroll(source)); sourceChanges++; }
                owner.UpdateLayout(); Record();
            }
            catch (Exception error) { failed = true; status.Text = "检查失败：" + error.Message; Console.Error.WriteLine(error); }
        }
        toggle.Click += (_, _) => Change(false);
        write.Click += (_, _) => Change(true);
        size.Click += (_, _) => { owner.Width = owner.MinWidth; owner.Height = owner.MinHeight; owner.UpdateLayout(); Record(); };
        finish.Click += (_, _) => owner.Close();
        owner.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.F6 or Key.F7) { Change(e.Key == Key.F7); e.Handled = true; }
        };
        owner.Closed += (_, _) => { closed = true; Record(); app.Terminate(app); };
        owner.Show(); owner.UpdateLayout(); editor.Focus(); Record();
        app.Run();
        return failed ? 1 : 0;
    }

    private static StackPanel Rows()
    {
        var rows = new StackPanel { Spacing = 4 };
        for (int i = 0; i < 80; i++)
            rows.Children.Add(new TextBlock { Text = $"第 {i + 1:00} 项 · 中文内容🙂与横向滚动检查", Width = 800, Height = 36,
                FontSize = 15, Foreground = Brushes.White });
        return rows;
    }

    private static ScrollViewer Viewer(StackPanel rows, DependencyObject source, int syntax)
    {
        var viewer = new ScrollViewer { Content = rows, Height = 180,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var path = syntax switch
        {
            0 => new PropertyPath("(ScrollViewer.CanContentScroll)"),
            1 => new PropertyPath(ScrollViewer.CanContentScrollProperty),
            _ => new PropertyPath("(0)", ScrollViewer.CanContentScrollProperty)
        };
        viewer.SetBinding(ScrollViewer.CanContentScrollProperty, new Binding { Source = source, Path = path, Mode = BindingMode.TwoWay });
        return viewer;
    }

    private static TextBox Editor()
    {
        var editor = new TextBox { Text = "切换后继续编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "滚动方式切换后的编辑框");
        return editor;
    }

    private static Button Button(string text) => new() { Content = text, Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Window Owner(bool custom, UIElement content) => new()
    {
        Title = "Jalium Attached Binding v156 " + (custom ? "Custom" : "Native"), Width = 640, Height = 760,
        MinWidth = 520, MinHeight = 600, Content = content, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
        Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26))
    };
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Save(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("JALIUM_ATTACHED_BINDING_OUTPUT");
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
