using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Threading;
using Jalium.UI.Automation;
using Jalium.UI.Interop;
using Jalium.UI.Data;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;

int failures = 0;
bool automated = args.Contains("--automated");
bool nativeTitleBar = args.Contains("--native-titlebar");
bool keyboardSmoke = args.Contains("--keyboard-smoke");
bool suspensionProbe = args.Contains("--suspension-probe");
bool constraintsProbe = args.Contains("--constraints-probe");
bool sizeConstraintsProbe = args.Contains("--size-constraints-probe");
bool presentationProbe = args.Contains("--presentation-probe");
bool autosizeProbe = args.Contains("--autosize-probe");
bool minimizeProbe = args.Contains("--minimize-probe");
bool keyboardMenuProbe = args.Contains("--keyboard-menu-probe");
bool hoverProbe = args.Contains("--hover-probe");
bool nativeMenuInputProbe = args.Contains("--native-menu-input-probe");
bool inputDeviceProbe = args.Contains("--input-device-probe");
var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
    { Args = args, DisableDefaults = true, DesktopApplicationId = "org.jalium.LinuxWindowValidation" });
builder.ConfigureApplication(app =>
{
    var window = new Window
    {
        Title = "Jalium · Linux Window", Width = 720, Height = 560,
        MinWidth = 360, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
    };
    bool cancelNextClose = false;
    var root = new DockPanel { Margin = new Thickness(24) };
    var status = new TextBlock { Text = "等待窗口显示", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
    DockPanel.SetDock(status, Dock.Bottom);
    root.Children.Add(status);
    var content = new StackPanel();
    var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    root.Children.Add(scroll);
    content.Children.Add(new TextBlock { Text = "Linux 窗口行为", FontSize = 24, Margin = new Thickness(0, 0, 0, 12) });
    var instructions = new TextBlock
    {
        Text = "拖动标题栏，拖动边缘调整大小，双击标题栏最大化。Tab 切换控件，Alt+Space 打开窗口菜单。",
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20),
    };
    content.Children.Add(instructions);
    if (autosizeProbe)
    {
        instructions.Text = "F4 切换自动尺寸模式，F2 增加内容，F3 移除内容，F6 记录状态。平铺时保持合成器尺寸。";
        instructions.MaxWidth = 640;
    }
    content.Children.Add(new TextBlock { Text = "文本与焦点", Margin = new Thickness(0, 0, 0, 8) });
    var editor = new TextBox { Text = "Linux 中文焦点 · hello", MinHeight = 36, Margin = new Thickness(0, 0, 0, 16) };
    var actionButtons = new List<Button>();
    bool forwardFocus = false, backwardFocus = false;
    AutomationProperties.SetName(editor, "文本与焦点");
    content.Children.Add(editor);
    editor.GotKeyboardFocus += (_, _) =>
    {
        Report("focus: 文本与焦点");
        if (keyboardSmoke && forwardFocus) backwardFocus = true;
    };
    void Report(string text)
    {
        status.Text = text;
        Console.WriteLine($"[window-host] {text}");
    }
    void Check(bool condition, string message)
    {
        if (!condition) failures++;
        Report($"{(condition ? "PASS" : "FAIL")} {message}");
    }
    void AddButton(string label, Action action)
    {
        var button = new Button { Content = label, MinHeight = 36, Margin = new Thickness(0, 0, 0, 8) };
        actionButtons.Add(button);
        button.GotKeyboardFocus += (_, _) => Report($"focus: {label}");
        button.Click += (_, _) => action();
        content.Children.Add(button);
    }
    Window Dialog(Window owner, bool nested)
    {
        var dialog = new Window
        {
            Owner = owner, Title = nested ? "第二层对话框" : "模态对话框", Width = 420, Height = 300,
            MinWidth = 340, MinHeight = 240, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            TitleBarStyle = window.TitleBarStyle,
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "关闭后返回先前窗口与文本焦点", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var dialogEditor = new TextBox { Text = "对话框中的中文文本", MinHeight = 36, Margin = new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetName(dialogEditor, "对话框文本");
        panel.Children.Add(dialogEditor);
        if (!nested)
        {
            var child = new Button { Content = "打开第二层对话框", MinHeight = 36, Margin = new Thickness(0, 0, 0, 8) };
            child.Click += (_, _) => Dialog(dialog, true).ShowDialog();
            panel.Children.Add(child);
        }
        var close = new Button { Content = "完成", IsDefault = true, IsCancel = true, MinHeight = 36 };
        close.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(close);
        dialog.Content = panel;
        return dialog;
    }
    AddButton("正常尺寸 640 × 440", () => { window.WindowState = WindowState.Normal; window.Width = 640; window.Height = 440; });
    AddButton("最大化 / 还原", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
    AddButton("全屏 / 退出全屏", () => window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen);
    AddButton("打开模态对话框", () => { editor.Focus(); Report($"对话框返回：{Dialog(window, false).ShowDialog()}"); });
    AddButton("下次关闭时保留窗口", () => { cancelNextClose = true; Report("下次关闭将取消；再次关闭将退出。"); });
    actionButtons[0].GotKeyboardFocus += (_, _) => forwardFocus = true;
    editor.TextChanged += (_, _) =>
    {
        if (!keyboardSmoke || editor.Text != "Linux 中文焦点 · hello_qa") return;
        Check(forwardFocus && backwardFocus, "Tab 与 Shift+Tab 完成正反向焦点移动");
        Check(true, "原生字符输入保留已有中文并提交一次");
        Report($"finished keyboard failures={failures}");
        // Exercise native callback lifetime when an input callback closes its window.
        window.Close();
    };
    window.Content = root;
    if (autosizeProbe)
    {
        var state = new AutoSizeProbeState();
        var autoBinding = window.SetBinding(Window.SizeToContentProperty,
            new Binding(nameof(AutoSizeProbeState.Mode)) { Source = state, Mode = BindingMode.TwoWay });
        var extraContent = new List<TextBlock>();
        bool IsTiled()
        {
            var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            return platform != null && (bool)platform.GetType().GetProperty("IsTiled")!.GetValue(platform)!;
        }
        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not Key.F2 and not Key.F3 and not Key.F4 and not Key.F6) return;
            if (e.Key == Key.F4)
            {
                var next = window.SizeToContent switch
                {
                    SizeToContent.Manual => SizeToContent.Width,
                    SizeToContent.Width => SizeToContent.Height,
                    SizeToContent.Height => SizeToContent.WidthAndHeight,
                    _ => SizeToContent.Manual,
                };
                window.SetCurrentValue(Window.SizeToContentProperty, next);
                Report($"自动尺寸：{next}");
            }
            else if (e.Key == Key.F2)
            {
                bool tiled = IsTiled();
                double width = window.Width, height = window.Height;
                var extra = new TextBlock
                {
                    Text = "自动尺寸内容：" + new string('宽', 55), MaxWidth = 900,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                };
                extraContent.Add(extra);
                content.Children.Add(extra);
                Report($"已增加内容：{extraContent.Count}");
                if (tiled)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        if (IsTiled())
                            Check(Math.Abs(window.Width - width) < 1 && Math.Abs(window.Height - height) < 1,
                                "平铺内容改变保持合成器尺寸");
                    };
                    timer.Start();
                }
            }
            else if (e.Key == Key.F3 && extraContent.Count > 0)
            {
                var extra = extraContent[^1];
                extraContent.RemoveAt(extraContent.Count - 1);
                content.Children.Remove(extra);
                Report($"已移除内容：{extraContent.Count}");
            }
            else if (e.Key == Key.F6)
            {
                Check(ReferenceEquals(autoBinding, BindingOperations.GetBindingExpressionBase(window, Window.SizeToContentProperty)),
                    "自动尺寸绑定保留");
                Check(state.Mode == window.SizeToContent, "自动尺寸模式同步到绑定源");
                Console.WriteLine($"[window-autosize] mode={window.SizeToContent} tiled={IsTiled()} size={window.Width:F0}x{window.Height:F0} added={extraContent.Count}");
            }
            e.Handled = true;
        };
    }
    if (keyboardMenuProbe) WindowKeyboardMenuProbe.Attach(window, editor, Check);
    if (hoverProbe) WindowHoverProbe.Attach(window, editor, actionButtons, instructions);
    if (nativeMenuInputProbe) WindowNativeMenuInputProbe.Attach(window, editor);
    if (inputDeviceProbe || args.Contains("--seat-global-probe"))
        WindowInputDeviceProbe.Attach(window, editor, actionButtons[0], args.Contains("--seat-global-probe"));
    if (args.Contains("--ime-lifetime-probe"))
        WindowInputLifetimeProbe.Attach(window, editor, content, instructions, Report);
    if (minimizeProbe)
    {
        bool restoring = false;
        WindowState NativeState()
        {
            var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            return (WindowState)platform.GetType().GetMethod("GetState")!.Invoke(platform, null)!;
        }
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F5 || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            if (restoring || window.WindowState == WindowState.Minimized) return;
            restoring = true;
            var previousState = window.WindowState;
            var previousMode = window.SizeToContent;
            var previousBinding = BindingOperations.GetBindingExpressionBase(window, Window.SizeToContentProperty);
            string previousText = editor.Text;
            int phase = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (window.Handle == nint.Zero) { timer.Stop(); restoring = false; return; }
                if (phase++ == 0)
                {
                    Check(NativeState() == WindowState.Minimized, "窗口管理器确认最小化");
                    editor.Text = previousText + " · 最小化还原";
                    window.SetCurrentValue(Window.WindowStateProperty, previousState);
                }
                else if (phase == 2)
                {
                    Check(NativeState() == previousState && editor.Text.EndsWith(" · 最小化还原"),
                        "最小化后恢复原生状态和编辑内容");
                    Check(window.SizeToContent == previousMode && ReferenceEquals(previousBinding,
                        BindingOperations.GetBindingExpressionBase(window, Window.SizeToContentProperty)),
                        "最小化还原保留自动尺寸与绑定");
                    Console.WriteLine($"[window-minimize] restored state={NativeState()} mode={window.SizeToContent}");
                }
                else
                {
                    timer.Stop();
                    editor.Text = previousText;
                    restoring = false;
                    Console.WriteLine($"[window-minimize] finished state={NativeState()}");
                }
            };
            window.SetCurrentValue(Window.WindowStateProperty, WindowState.Minimized);
            timer.Start();
        };
    }
    if (presentationProbe)
    {
        string output = Environment.GetEnvironmentVariable("JALIUM_WINDOW_PRESENTATION_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "jalium-window-presentation");
        Directory.CreateDirectory(output);
        bool pending = false;
        int captureIndex = 0;
        var captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        void ArmReadback()
        {
            if (window.RenderTarget is not { } target) return;
            if (target.RequestReadback() == JaliumResult.Ok) pending = true;
        }
        window.SizeChanged += (_, _) => ArmReadback();
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F12) return;
            ArmReadback();
            e.Handled = true;
        };
        captureTimer.Tick += (_, _) =>
        {
            if (!pending || window.RenderTarget is not { } target ||
                PresentationProbe.Query(target.Handle, 0, 0, out int width, out int height) != 0) return;
            if (width <= 0 || height <= 0) return;
            var pixels = new byte[checked(width * height * 4)];
            if (target.FetchReadback(pixels, checked((uint)width * 4), out width, out height) != JaliumResult.Ok) return;
            pending = false;
            string stem = Path.Combine(output, $"frame-{++captureIndex:D3}-{width}x{height}");
            File.WriteAllBytes(stem + ".bgra", pixels);
            File.WriteAllText(stem + ".json", JsonSerializer.Serialize(new
            {
                width, height, windowWidth = window.Width, windowHeight = window.Height,
                state = window.WindowState.ToString(), utc = DateTime.UtcNow,
            }));
            Console.WriteLine($"[presentation] captured {stem}");
        };
        window.Loaded += (_, _) => { ArmReadback(); captureTimer.Start(); };
        window.Closed += (_, _) => captureTimer.Stop();
    }
    bool HasNegotiatedTitleBar()
    {
        var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        int mode = Convert.ToInt32(platform.GetType().GetProperty("DecorationMode")!.GetValue(platform));
        return mode switch
        {
            1 => window.TitleBar is { ActualHeight: > 0 },
            2 => window.TitleBar == null,
            _ => false,
        };
    }
    if (sizeConstraintsProbe)
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not Key.F2 and not Key.F3) return;
            var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            nint pointer = (nint)platform.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(platform)!;
            float scale = ConstraintsProbe.Scale(pointer);
            bool minimum = e.Key == Key.F3;
            int expectedWidth = minimum ? 600 : 480, expectedHeight = minimum ? 420 : 360;
            int physicalWidth = (int)Math.Ceiling(expectedWidth * scale);
            int physicalHeight = (int)Math.Ceiling(expectedHeight * scale);
            Check(ConstraintsProbe.Bounds(pointer, 0, 0) == 0 &&
                ConstraintsProbe.Limits(pointer, minimum ? physicalWidth : 0, minimum ? physicalHeight : 0,
                    minimum ? 0 : physicalWidth, minimum ? 0 : physicalHeight) == 0,
                "应用无推荐尺寸时的显式尺寸约束");
            uint[] active = [4];
            Check(ConstraintsProbe.Configure(pointer, minimum ? 420 : 720, minimum ? 280 : 560, active, 1) == 0 &&
                ConstraintsProbe.Configure(pointer, 0, 0, active, 1) == 0 && SuspensionProbe.Apply(pointer) == 0,
                "原生客户端自行选择尺寸");
            Check(Math.Abs(window.Width - expectedWidth) <= 1 && Math.Abs(window.Height - expectedHeight) <= 1,
                "显式尺寸约束同步到托管窗口和布局");
            Report(minimum ? "minimum 600 x 420" : "maximum 480 x 360");
            e.Handled = true;
        };
    if (constraintsProbe)
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not Key.F5 and not Key.F6 and not Key.F7 and not Key.F8) return;
            if (e.Key == Key.F5)
            {
                var cursor = (Cursor?)typeof(Mouse).GetProperty("CurrentCursor", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
                Report($"cursor: {cursor?.CursorType}");
            }
            else
            {
                var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                nint pointer = (nint)platform.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(platform)!;
                uint[] states = e.Key == Key.F7 ? [4, 10, 12] : [4];
                if (e.Key == Key.F6)
                    Check(ConstraintsProbe.Bounds(pointer, 380, 360) == 0, "应用逻辑尺寸建议");
                Check(ConstraintsProbe.Configure(pointer, 0, 0, states, (uint)states.Length) == 0 &&
                    SuspensionProbe.Apply(pointer) == 0, "应用边缘约束配置");
                Check(ConstraintsProbe.Edges(pointer) == (e.Key == Key.F7 ? 10u : 15u), "原生边缘约束同步");
                if (e.Key == Key.F6)
                    Check(Math.Abs(window.Width - 380) < 1 && Math.Abs(window.Height - 360) < 1,
                        "推荐尺寸同步到托管布局");
                Report(e.Key == Key.F7 ? "constrained left/top" : e.Key == Key.F6 ? "bounded 380 x 360" : "all edges available");
            }
            e.Handled = true;
        };
    if (suspensionProbe)
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not Key.F9 and not Key.F10) return;
            var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            nint pointer = (nint)platform.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(platform)!;
            bool suspend = e.Key == Key.F9;
            Check(SuspensionProbe.Inject(pointer, suspend ? 1 : 0) == 0 && SuspensionProbe.Apply(pointer) == 0,
                suspend ? "注入暂停配置并应用确认状态" : "注入恢复配置并应用确认状态");
            bool registered = (bool)typeof(Window).GetField("_registeredAsRenderable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Check(registered != suspend && window.Visibility == Visibility.Visible && window.WindowState == WindowState.Normal,
                "渲染可用性与逻辑窗口状态同步");
            if (suspend) editor.Text = "暂停期间更新 · resume";
            Report(suspend ? "suspended with pending text" : "resumed with updated text");
            e.Handled = true;
        };
    window.Closing += (_, eventArgs) =>
    {
        if (cancelNextClose) { cancelNextClose = false; eventArgs.Cancel = true; Report("关闭已取消，窗口和文本保留。"); }
    };
    window.SizeChanged += (_, _) => Report($"尺寸 {window.Width:F0} × {window.Height:F0} · {window.WindowState}");
    window.Loaded += (_, _) =>
    {
        Report("ready");
        if (hoverProbe) editor.Focus();
        if (keyboardSmoke)
        {
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
            Report("keyboard ready");
            return;
        }
        if (!automated) return;
        nint handle = window.Handle;
        var steps = new Queue<Action>();
        steps.Enqueue(() => Check(app.DesktopApplicationId == "org.jalium.LinuxWindowValidation", "应用桌面标识配置生效"));
        if (nativeTitleBar)
        {
            steps.Enqueue(() => Check(window.TitleBarStyle == WindowTitleBarStyle.Native && HasNegotiatedTitleBar(),
                "Native 标题栏按窗口系统能力显示"));
            steps.Enqueue(() => { window.TitleBarStyle = WindowTitleBarStyle.Custom; });
            steps.Enqueue(() => Check(HasNegotiatedTitleBar(), "运行时切换自定义标题栏"));
            steps.Enqueue(() => { window.TitleBarStyle = WindowTitleBarStyle.Native; });
        }
        steps.Enqueue(() => { window.Width = 640; window.Height = 440; });
        steps.Enqueue(() => Check(Math.Abs(window.Width - 640) < 1 && Math.Abs(window.Height - 440) < 1, "运行时更新原生尺寸"));
        steps.Enqueue(() => window.WindowState = WindowState.Maximized);
        steps.Enqueue(() => Check(window.Width > 640, "最大化更新布局尺寸"));
        steps.Enqueue(() => window.WindowState = WindowState.FullScreen);
        steps.Enqueue(() => Check(window.TitleBar == null || window.TitleBar.Visibility == Visibility.Collapsed,
            "全屏隐藏应用标题栏"));
        steps.Enqueue(() => window.WindowState = WindowState.Maximized);
        steps.Enqueue(() => Check(window.WindowState == WindowState.Maximized && window.Width > 640 &&
            (window.TitleBar == null || window.TitleBar.Visibility == Visibility.Visible),
            "从全屏切换到最大化并恢复标题栏"));
        steps.Enqueue(() => window.WindowState = WindowState.Normal);
        steps.Enqueue(() => Check(window.TitleBar == null || window.TitleBar.Visibility == Visibility.Visible,
            "退出全屏恢复应用标题栏"));
        steps.Enqueue(() => Check(Math.Abs(window.Width - 640) < 1 && Math.Abs(window.Height - 440) < 1, "全屏还原正常尺寸"));
        steps.Enqueue(() => { window.Hide(); Check(window.Visibility == Visibility.Hidden, "隐藏同步可见性"); window.Show(); });
        steps.Enqueue(() => Check(window.Handle == handle && window.Visibility == Visibility.Visible, "再次显示复用窗口与内容"));
        if (nativeTitleBar)
            steps.Enqueue(() => Check(window.TitleBarStyle == WindowTitleBarStyle.Native && HasNegotiatedTitleBar(),
                "隐藏后重新显示保留 Native 装饰选择"));
        steps.Enqueue(() => { actionButtons[^1].Focus(); window.Width = 380; window.Height = 360; });
        steps.Enqueue(() =>
        {
            var focusedButton = actionButtons[^1];
            var origin = focusedButton.TransformToAncestor(scroll);
            Check(ReferenceEquals(Keyboard.FocusedElement, focusedButton) && origin.Y >= -1 &&
                origin.Y + focusedButton.ActualHeight <= scroll.ViewportHeight + 1,
                "缩小窗口后保留焦点并显示当前按钮");
            editor.Focus();
        });
        steps.Enqueue(() => Check(editor.Text == "Linux 中文焦点 · hello", "缩放保留编辑内容"));
        steps.Enqueue(() =>
        {
            var dialog = Dialog(window, false);
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            closeTimer.Tick += (_, _) => { closeTimer.Stop(); Check(!window.IsEnabled, "模态期间禁用拥有者"); dialog.DialogResult = true; };
            closeTimer.Start();
            Check(dialog.ShowDialog() == true && window.IsEnabled, "模态关闭恢复拥有者");
        });
        steps.Enqueue(() => Check(ReferenceEquals(Keyboard.FocusedElement, editor), "模态关闭恢复文本焦点"));
        steps.Enqueue(() => { cancelNextClose = true; window.Close(); Check(window.Handle != nint.Zero && editor.Text == "Linux 中文焦点 · hello", "取消关闭保留窗口与编辑内容"); });
        steps.Enqueue(() => { Report($"finished failures={failures}"); window.Close(); });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        bool runningStep = false;
        timer.Tick += (_, _) =>
        {
            if (runningStep) return;
            if (!steps.TryDequeue(out var step)) { timer.Stop(); return; }
            runningStep = true;
            try { step(); }
            catch (Exception error) { failures++; Report($"FAIL {error}"); timer.Stop(); window.Close(); }
            finally { runningStep = false; }
        };
        timer.Start();
    };
    app.MainWindow = window;
});
using var host = builder.Build();
int result = host.Run();
return failures == 0 ? result : 1;

// Available only in explicit native test builds. The opt-in probe drives the
// same native listeners and managed events; it does not claim compositor policy.
static class SuspensionProbe
{
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_suspended_configure")]
    internal static extern int Inject(nint window, int suspended);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_apply_surface_configuration")]
    internal static extern int Apply(nint window);
}

static class ConstraintsProbe
{
    [DllImport("jalium.native.platform", EntryPoint = "jalium_window_set_min_max_size")]
    internal static extern int Limits(nint window, int minWidth, int minHeight, int maxWidth, int maxHeight);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_window_get_dpi_scale")]
    internal static extern float Scale(nint window);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_toplevel_configure")]
    internal static extern int Configure(nint window, int width, int height, uint[] states, uint count);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_test_wayland_inject_configure_bounds")]
    internal static extern int Bounds(nint window, int width, int height);
    [DllImport("jalium.native.platform", EntryPoint = "jalium_linux_window_get_resizable_edges")]
    internal static extern uint Edges(nint window);
}

static class PresentationProbe
{
    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_fetch_readback")]
    internal static extern int Query(nint renderTarget, nint buffer, uint stride, out int width, out int height);
}

sealed class AutoSizeProbeState
{
    public SizeToContent Mode { get; set; }
}
