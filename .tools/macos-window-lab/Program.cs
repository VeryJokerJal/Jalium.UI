
using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Documents;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;
using Jalium.UI.MacOS;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Window = Jalium.UI.Window;
using Orientation = Jalium.UI.Controls.Orientation;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("macos15.0")]

namespace MacOSWindowLab;

internal static class Program
{
    internal static string LogPrefix => "/private/tmp/jalium-macos-window-lab-v"
        + (NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString() ?? "unknown");

    private static void Main(string[] args)
    {
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var beginTracking = NSNotificationCenter.DefaultCenter.AddObserver(NSMenu.DidBeginTrackingNotification, RecordMenuTracking);
        using var endTracking = NSNotificationCenter.DefaultCenter.AddObserver(NSMenu.DidEndTrackingNotification, RecordMenuTracking);
        using var appDelegate = new WindowLabDelegate(args);
        NSApplication.SharedApplication.Delegate = appDelegate;
        try { NSApplication.Main(args); }
        finally
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(beginTracking);
            NSNotificationCenter.DefaultCenter.RemoveObserver(endTracking);
        }
    }

    private static void RecordMenuTracking(NSNotification notification) =>
        RecordMenuInteraction(notification.Name, notification.Object as NSMenu);

    private static void RecordMenuInteraction(string kind, NSMenu? menu) =>
        File.AppendAllText(LogPrefix + "-menu-tracking.jsonl",
            System.Text.Json.JsonSerializer.Serialize(new WindowMenuTrackingSnapshot(
                DateTimeOffset.UtcNow, Environment.ProcessId, kind, menu?.Title,
                NSApplication.SharedApplication.KeyWindow?.Title, NSRunLoop.Current.CurrentMode?.ToString()),
                WindowLabJsonContext.Default.WindowMenuTrackingSnapshot) + "\n");

    internal static void ShowNativeEditingMenu(Window window)
    {
        var menu = NSApplication.SharedApplication.MainMenu?.Items
            .Select(item => item.Submenu).FirstOrDefault(menu => menu?.Title is "Edit" or "编辑");
        var view = ObjCRuntime.Runtime.GetNSObject<NSView>(window.Handle);
        if (menu is null || view is null) return;
        RecordMenuInteraction("EditingPopupRequested", menu);
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            menu.PopUpMenu(null, new CGPoint(24, view.IsFlipped ? 80 : view.Bounds.Height - 80), view);
            RecordMenuInteraction("EditingPopupReturned", menu);
        });
    }

    internal static void ShowNativeWindowMenu(Window window)
    {
        var application = NSApplication.SharedApplication;
        var menu = application.WindowsMenu;
        var view = ObjCRuntime.Runtime.GetNSObject<NSView>(window.Handle);
        if (menu is null || view is null) return;
        RecordMenuInteraction("PopupRequested", menu);
        application.BeginInvokeOnMainThread(() =>
        {
            menu.PopUpMenu(null, new CGPoint(24, view.IsFlipped ? 80 : view.Bounds.Height - 80), view);
            RecordMenuInteraction("PopupReturned", menu);
        });
    }
}

[Register("JaliumWindowLabDelegate")]
internal sealed class WindowLabDelegate(string[] args) : JaliumMacApplicationDelegate
{
    protected override void ConfigureNativeWindowMenu(NSApplication application)
    {
        RecordNativeMenus("before", application);
        base.ConfigureNativeWindowMenu(application);
        if (args.Contains("--window-editing-lab") && !application.MainMenu!.Items
            .Any(item => item.Submenu?.Title is "Edit" or "编辑"))
        {
            var menu = new NSMenu("编辑");
            foreach (var (title, selector, key) in new[]
            {
                ("撤销", "undo:", "z"), ("重做", "redo:", "z"),
                ("剪切", "cut:", "x"), ("复制", "copy:", "c"),
                ("粘贴", "paste:", "v"), ("全选", "selectAll:", "a")
            })
            {
                var item = new NSMenuItem(title, new ObjCRuntime.Selector(selector), key);
                if (selector == "redo:") item.KeyEquivalentModifierMask = NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask;
                menu.AddItem(item);
            }
            application.MainMenu.InsertItem(new NSMenuItem { Submenu = menu }, 1);
        }
        RecordNativeMenus("after", application);
    }

    private static void RecordNativeMenus(string phase, NSApplication application)
    {
        var rows = new List<string> { $"{phase}: windowMenu={application.WindowsMenu?.Title ?? "null"}" };
        foreach (var item in application.MainMenu?.Items ?? [])
        {
            rows.Add($"root: {item.Title}; submenu={item.Submenu?.Title ?? "null"}; items={item.Submenu?.Items.Length ?? 0}");
            foreach (var child in item.Submenu?.Items ?? []) rows.Add($"  {child.Title}; key={child.KeyEquivalent}");
        }
        File.AppendAllLines(Program.LogPrefix + "-native-menu.txt", rows);
    }

    protected override JaliumApp CreateHostedApp()
    {
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings { Args=args, DisableDefaults=true });
        builder.ConfigureApplication(app =>
        {
            ThemeManager.ApplyBrandTheme(new BrandThemeOptions
            {
                Theme = ThemeVariant.Light,
                AccentColor = Color.FromRgb(0x08, 0x91, 0xb2),
                DisplayFontFamily = "PingFang SC",
                BodyFontFamily = "PingFang SC"
            });
            app.Resources["TitleBarCloseButtonName"] = "关闭窗口";
            app.Resources["TitleBarMinimizeButtonName"] = "最小化窗口";
            app.Resources["TitleBarMaximizeButtonName"] = "最大化窗口";
            app.Resources["TitleBarRestoreButtonName"] = "还原窗口";
            if (args.Contains("--window-quit-lab")) app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            app.MainWindow = args.Contains("--window-editing-lab") ? WindowEditingLab.Create(args.Contains("--native-titlebar"), args.Contains("--small-window"))
                : args.Contains("--window-quit-lab") ? WindowQuitLab.Create(args.Contains("--native-titlebar"), args.Contains("--small-window"))
                : args.Contains("--window-drag-lab") ? WindowDragLab.Create(args.Contains("--native-titlebar"), args.Contains("--small-window"))
                : args.Contains("--window-participation-lab") ? WindowLab.CreateParticipationWindow()
                : args.Contains("--window-startup-lab") ? WindowStartupLab.Create(args.Contains("--native-titlebar"), args.Contains("--maximize-initialized"))
                : args.Contains("--window-minimize-lab") ? WindowMinimizeLab.Create(args.Contains("--native-titlebar"))
                : args.Contains("--window-menu-lab") ? WindowMenuLab.Create() : new WindowLab
                {
                    TitleBarStyle = args.Contains("--native-titlebar") ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
                    MinimumModalSize = args.Contains("--small-modal")
                };
            if (args.Contains("--window-focus-trace")) WindowFocusTrace.Attach(app.MainWindow);
        });
        return builder.Build();
    }
    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        var reply = base.ApplicationShouldTerminate(sender);
        if (args.Contains("--window-quit-lab")) WindowQuitLab.RecordQuitDecision(reply);
        return reply;
    }

    public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
    {
        bool result = base.ApplicationShouldHandleReopen(sender, hasVisibleWindows);
        if (args.Contains("--window-quit-lab")) WindowQuitLab.RecordReopen(hasVisibleWindows, result);
        return result;
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => !args.Contains("--window-quit-lab");
}

internal sealed class WindowLab : Window
{
    private static SolidColorBrush Ink => new(Color.FromRgb(0x16,0x32,0x42));
    private readonly TextBlock _status = new() { FontSize=18, Height=82, Foreground=Ink, TextWrapping=TextWrapping.Wrap };
    private readonly TextBlock _history = new() { FontSize=14, Height=170, Foreground=Ink, TextWrapping=TextWrapping.Wrap };
    private readonly StackPanel _body;
    private readonly List<string> _events = [];
    private readonly string _log = Program.LogPrefix + "-events.jsonl";
    private Window? _modalWindow;
    private Window? _independentWindow;
    private int _sequence;
    internal bool MinimumModalSize { get; init; }

    internal WindowLab()
    {
        Loaded += (_, _) => WindowInputTrace.Attach(this);
        Directory.CreateDirectory(Path.GetDirectoryName(_log)!);
        Title="Window 行为验证";
        Width=760; Height=860; MinWidth=540; MinHeight=430;
        Background = new SolidColorBrush(Color.FromRgb(0xf5,0xfa,0xfc));
        _body = new StackPanel { Width=680, Spacing=18, Margin=new Thickness(32) };
        _body.Children.Add(new TextBlock { Text="macOS Window", FontSize=30, Foreground=Ink });
        _body.Children.Add(new TextBlock
        {
            Text="系统布局与内容自适应",
            FontSize=20, Foreground=Ink
        });
        _body.Children.Add(new TextBlock
        {
            Text="检查内容自适应、系统布局，以及多窗口模态与取消退出。",
            TextWrapping=TextWrapping.Wrap, FontSize=15, Foreground=Ink
        });
        _body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xe5,0xf4,0xf8)),
            Padding=new Thickness(16), CornerRadius=new CornerRadius(8), Child=_status
        });
        AddButtons(("启用内容自适应", () => { WindowState=WindowState.Normal; SizeToContent=SizeToContent.WidthAndHeight; Record("自动尺寸"); }),
            ("程序设为 840×700", () => { Width=840; Height=700; Record("程序尺寸"); }),
            ("窗口参与验证", ShowParticipationWindow));
        AddButtons(("托管最大化", () => WindowState=WindowState.Maximized),
            ("托管还原", () => WindowState=WindowState.Normal),
            ("新建第二窗口", ShowSecondWindow));
        AddButtons(("打开模态窗口", ShowModalWindow), ("隐藏主窗口", () => { Record("隐藏"); Hide(); }),
            ("新建独立窗口", ShowIndependentWindow), ("CSS 按钮验证", ShowCssButtonWindow),
            ("换行导航验证", ShowWrappedNavigationWindow),
            ("按词与选词验证", ShowWordNavigationWindow), ("字体回退验证", ShowFontWindow),
            ("透明与背景验证", () => WindowAppearanceLab.Show(this)),
            ("标题栏验证", () => WindowCaptionLab.Show(this)),
            ("窗口属性验证", () => WindowPropertyLab.Show(this)),
            ("窗口菜单验证", () => WindowMenuLab.Create().Show()),
            ("初始定位验证", () => WindowStartupLab.Create().Show()),
            ("最小化队列验证", () => WindowMinimizeLab.Create().Show()),
            ("显示与隐藏验证", () => WindowVisibilityLab.Show(this)));
        _body.Children.Add(new TextBlock { Text="最近窗口事件", FontSize=16, Foreground=Ink });
        _body.Children.Add(_history);
        Content=_body;
        Loaded += (_,_) => Record("显示");
        SizeChanged += (_,_) => Record("尺寸");
        StateChanged += (_,_) => Record("状态");
        Activated += (_,_) => Record("激活");
        Deactivated += (_,_) => Record("失活");
        Closed += (_,_) => Record("关闭");
        Record("创建");
    }

    private void AddButtons(params (string label, Action action)[] actions)
    {
        var row=new WrapPanel { Orientation=Orientation.Horizontal };
        foreach (var (label,action) in actions)
        {
            var button=new Button { Content=label, MinHeight=42, Margin=new Thickness(0,0,10,10), Padding=new Thickness(14,8,14,8) };
            button.Click += (_,_) => action();
            row.Children.Add(button);
        }
        _body.Children.Add(row);
    }

    private void ShowWrappedNavigationWindow()
    {
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "换行与光标", FontSize = 24, Foreground = Ink });
        body.Children.Add(new TextBlock { Text = "点击中间一行，按 ⌘← / ⌘→ 到当前行两端；加入 ⇧ 扩展选区。调整窗口宽度，再检查光标和输入法候选位置。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ink });
        var status = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Ink };
        var plain = new TextBox { Text = "中文换行测试：在这一段中检查视觉行。alpha beta gamma delta epsilon zeta eta theta iota kappa。Emoji 👩‍👩‍👧‍👦 和组合字符 e\u0301 应保持完整。",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 18, Height = 180,
            Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        Jalium.UI.Automation.AutomationProperties.SetName(plain, "多行文字导航");
        plain.SelectionChanged += (_, _) => status.Text = $"光标 {plain.CaretIndex} · 选区 {plain.SelectionStart} + {plain.SelectionLength}";
        body.Children.Add(plain);
        body.Children.Add(status);
        body.Children.Add(new TextBlock { Text = "富文本段落", FontSize = 18, Foreground = Ink });
        body.Children.Add(new TextBlock { Text = "在富文本中点击、选择并输入。长段落应在 Run 内换行；混合字号应共用基线。缩窄窗口后，再检查 ⌘← / ⌘→、↑ / ↓ 和中文候选位置。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ink });
        var rich = new RichTextBox { Height = 210, Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        Jalium.UI.Automation.AutomationProperties.SetName(rich, "富文本段落导航");
        void SetRichSample(int kind)
        {
            var paragraph = new Paragraph();
            if (kind == 0)
                paragraph.Inlines.Add(new Run("中文长段落在同一个 Run 内换行。alpha beta gamma delta epsilon zeta eta theta iota kappa。Emoji 👩‍👩‍👧‍👦 与组合字符 e\u0301 应完整显示，选区和输入光标也应停在同一行。") { FontSize = 20 });
            else if (kind == 1)
            {
                paragraph.Inlines.Add(new Run("小字号和常规文字应自然接续，") { FontSize = 18, Foreground = Ink });
                paragraph.Inlines.Add(new Run("较大的蓝色粗体文字跨行后保留样式，中文 👩‍👩‍👧‍👦 ")
                    { FontSize = 28, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x55, 0x9b)) });
                paragraph.Inlines.Add(new Run("最后一段恢复常规字号。调整宽度后没有重复或漏字。") { FontSize = 18, Foreground = Ink });
            }
            else
                paragraph.Inlines.Add(new Run("abc אבגדה 123 xyz العربية — 混合方向文字。אבגדה וזחטי כלמנס עפרצק שתאבג דהוזח טיכלמ נסעפצ") { FontSize = 20 });
            var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica", Foreground = Ink };
            document.Blocks.Add(paragraph); rich.Document = document; rich.Focus();
        }
        var samples = new WrapPanel();
        foreach (var (label, kind) in new[] { ("长 Run", 0), ("混合样式", 1), ("双向文字", 2) })
        {
            var button = new Button { Content = label, MinHeight = 42, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => SetRichSample(kind); samples.Children.Add(button);
        }
        body.Children.Add(samples); body.Children.Add(rich); SetRichSample(0);
        var child = new Window { Title = "视觉行导航验证", Width = 440, Height = 740, MinWidth = 180,
            MinHeight = 430, Owner = this, Content = new ScrollViewer { Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = Background,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        child.Show(); plain.Focus();
    }

    private void ShowWordNavigationWindow()
    {
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "按词移动与选词", FontSize = 24, Foreground = Ink });
        body.Children.Add(new TextBlock { Text = "按 ⌥← / ⌥→ 移动到词边界，加入 ⇧ 扩展选区；⌥Delete 删除前一个词，⌘Z 撤销。双击选词后继续拖动。调整窗口宽度，检查混合方向文字换行后的移动；也可切换富文本方向与字号。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ink });
        var plain = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontSize = 18, Height = 140, Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        var rich = new RichTextBox { Height = 170, Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        Jalium.UI.Automation.AutomationProperties.SetName(plain, "普通文本按词导航");
        Jalium.UI.Automation.AutomationProperties.SetName(rich, "富文本按词导航");
        var status = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Ink };
        int richDirection = -1;
        bool mixedSize = false;
        void LoadRich(string text)
        {
            var document = new FlowDocument { FontFamily = "Helvetica", FontSize = 20 };
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            if (richDirection is 0 or 1) paragraph.FlowDirection = richDirection == 1 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            int split = System.Globalization.StringInfo.ParseCombiningCharacters(text)
                .FirstOrDefault(index => index >= 4, text.Length);
            paragraph.Inlines.Add(new Run(text[..split]) { FontSize = 20 });
            paragraph.Inlines.Add(new Run(text[split..]) { FontSize = mixedSize ? 32 : 20 });
            document.Blocks.Add(paragraph); rich.Document = document;
        }
        plain.SelectionChanged += (_, _) => status.Text = $"光标 {plain.CaretIndex} · 选区 {plain.SelectionStart} + {plain.SelectionLength}";
        var samples = new WrapPanel();
        foreach (var (label, text) in new[]
        {
            ("中文", "中文输入窗口行为验证"), ("日文", "日本語の編集動作"),
            ("泰文", "ภาษาไทยยินดีต้อนรับ"), ("英文与 Emoji", "don't stop foo_bar 12.34 · one 👩‍👩‍👧‍👦 two"),
            ("双向文字", "שלום עולם مرحبا بالعالم · left אבג right"),
            ("双向换行", "abc אבגדה 123 xyz العربية 中文 words tail abc אבגדה words tail"),
            ("极长不换行", string.Concat(Enumerable.Repeat("MMMM ", 3000)) + "abc אבגדה 123 xyz tail")
        })
        {
            var button = new Button { Content = label, MinHeight = 42, Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { plain.TextWrapping = label == "极长不换行" ? TextWrapping.NoWrap : TextWrapping.Wrap; plain.Text = text; LoadRich(label == "极长不换行" ? "שלום עולם abc def" : text); plain.Focus(); };
            samples.Children.Add(button);
        }
        body.Children.Add(samples);
        body.Children.Add(new TextBlock { Text = "普通文本", FontSize = 18, Foreground = Ink });
        body.Children.Add(plain); body.Children.Add(status);
        body.Children.Add(new TextBlock { Text = "富文本", FontSize = 18, Foreground = Ink });
        var modes = new WrapPanel();
        foreach (var (label, direction) in new[] { ("自动方向", -1), ("从左至右", 0), ("从右至左", 1), ("继承从左至右", 2) })
        {
            var button = new Button { Content = label, MinHeight = 42, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { richDirection = direction; if (direction == 2) body.FlowDirection = FlowDirection.LeftToRight; else body.ClearValue(FrameworkElement.FlowDirectionProperty); LoadRich(direction == 2 ? "שלום עולם abc def" : plain.Text); rich.Focus(); };
            modes.Children.Add(button);
        }
        var sizes = new Button { Content = "混合字号", MinHeight = 42, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
        sizes.Click += (_, _) => { mixedSize = !mixedSize; sizes.Content = mixedSize ? "统一字号" : "混合字号"; LoadRich(plain.Text); rich.Focus(); };
        modes.Children.Add(sizes); body.Children.Add(modes); body.Children.Add(rich);
        plain.Text = "中文输入窗口行为验证"; LoadRich(plain.Text);
        var child = new Window { Title = "按词导航验证", Width = 600, Height = 710, MinWidth = 240, MinHeight = 430,
            Owner = this, Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = Background,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        child.Show(); plain.Focus();
    }

    private void ShowFontWindow()
    {
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "字体、字重与光标", FontSize = 24, Foreground = Ink });
        body.Children.Add(new TextBlock { Text = "切换字体、字重和斜体，检查两处文本的字宽、光标与选区。切换回退顺序，比较 אבג 的字形和宽度。使用 ⌥← / ⌥→ 按词移动，按住 ⇧ 扩展选区；调整窗口宽度，检查换行。富文本粘贴后按 ⌘Z 撤销、⇧⌘Z 重做，检查文字、格式和原选区。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ink });
        const string sample = "MMMM iii1 abc אבג xyz tail · 中文输入";
        var plain = new TextBox { Text = sample, FontSize = 20, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, Height = 120, Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        var run = new Run(sample) { FontSize = 20 };
        var document = new FlowDocument { FontFamily = "Helvetica", FontSize = 20, Foreground = Ink };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        var rich = new RichTextBox(document) { Height = 170, Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        Css.SetStyleSheet(body, "@font-face {font-family: WindowLabMono; src: url('file:///System/Library/Fonts/Supplemental/Andale%20Mono.ttf'); font-display: swap;}" +
            "@font-face {font-family: WindowLabVariable; src: url('file:///System/Library/Fonts/SFNS.ttf'); font-weight:100 900; font-width:30% 150%; font-display: swap;}" +
            "@font-face {font-family: WindowLabCollection; src: url('file:///System/Library/Fonts/Avenir%20Next.ttc'); font-weight:100 900; font-display: swap;}" +
            "@font-face {font-family: WindowLabHelvetica; src: url('file:///System/Library/Fonts/HelveticaNeue.ttc'); font-weight:100 900; font-width:75% 100%; font-display:swap;}");
        var status = new TextBlock { FontSize = 14, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
        Window? fontWindow = null;
        var windowWidths = new WrapPanel();
        foreach (var (label, width) in new[] { ("窄窗口 320", 320d), ("标准窗口 600", 600d) })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) =>
            {
                if (fontWindow is null) return;
                fontWindow.Width = width;
                status.Text = $"窗口宽度：{fontWindow.Width:0} DIP。检查按钮换行、文本换行和选区。";
                plain.Focus();
            };
            windowWidths.Children.Add(button);
        }
        body.Children.Add(windowWidths);
        var buttons = new WrapPanel();
        foreach (var (label, family) in new[] { ("系统字体", "SF Pro"), ("具名字体", "Avenir Next"), ("等宽字体", "Menlo"),
            ("首项缺失", "MissingWindowLab43, Menlo"), ("带引号的列表", "\"Missing, Font42\", 'Menlo'"),
            ("私有等宽字体", "WindowLabMono, Helvetica"), ("私有可变字体", "WindowLabVariable, Helvetica"), ("私有字体集合", "WindowLabCollection, Helvetica") })
        {
            var button = new Button { Content = label, MinHeight = 42, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) =>
            {
                plain.ClearValue(TextElement.FontFamilyProperty); Css.SetStyle(plain, $"font-family: {family}");
                run.ClearValue(TextElement.FontFamilyProperty); Css.SetStyle(run, $"font-family: {family}");
                status.Text = $"当前：{label} · {family}"; plain.Focus();
            };
            buttons.Children.Add(button);
        }
        body.Children.Add(buttons);
        var cascades = new WrapPanel();
        foreach (var (label, family) in new[] {
            ("Arial 优先回退", "Avenir Next, Arial Hebrew, New Peninim MT"),
            ("Peninim 优先回退", "Avenir Next, New Peninim MT, Arial Hebrew") })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => {
                plain.FontFamily = new FontFamily(family); run.FontFamily = new FontFamily(family);
                status.Text = $"当前：{label} · {family}"; plain.Focus();
            };
            cascades.Children.Add(button);
        }
        body.Children.Add(cascades);
        var cssCascades = new WrapPanel();
        foreach (var (label, family) in new[] { ("CSS Arial 回退", "'Avenir Next', 'Arial Hebrew', 'New Peninim MT'"),
            ("CSS Peninim 回退", "'Avenir Next', 'New Peninim MT', 'Arial Hebrew'") })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => {
                plain.ClearValue(TextElement.FontFamilyProperty); run.ClearValue(TextElement.FontFamilyProperty);
                Css.SetStyle(plain, "font-family:" + family); Css.SetStyle(run, "font-family:" + family);
                status.Text = $"当前：{label} · 比较 אבג 的字形、光标和选区。"; plain.Focus();
            };
            cssCascades.Children.Add(button);
        }
        var subset = new Button { Content = "局部字体加载", MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
        subset.Click += (_, _) => {
            Css.GetStyleSheets(plain).Clear();
            Css.GetStyleSheets(plain).Add(CssStyleSheet.Parse(
                "@font-face {font-family:WindowLabSubset43;src:url('hebrew.ttc');font-weight:100 900;unicode-range:U+590-5FF;font-display:block;}",
                "Window Lab subset", new Uri("https://window-lab-font.invalid/"), new LabSubsetResolver()));
            plain.ClearValue(TextElement.FontFamilyProperty); run.ClearValue(TextElement.FontFamilyProperty);
            const string family = "font-family:'Avenir Next',WindowLabSubset43,'New Peninim MT'";
            Css.SetStyle(plain, family); Css.SetStyle(run, family);
            status.Text = "观察局部加载：拉丁文字保持显示，אבג 的字形稍后出现；检查加载前后的光标与选区。"; plain.Focus();
        };
        cssCascades.Children.Add(subset); body.Children.Add(cssCascades);
        body.Children.Add(new TextBlock { Text = "字重", FontSize = 15, Foreground = Ink });
        var weights = new WrapPanel();
        for (int value = 100; value <= 900; value += 100)
        {
            int weight = value;
            var button = new Button { Content = weight.ToString(), MinHeight = 44, MinWidth = 52,
                Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 8, 10, 8) };
            Jalium.UI.Automation.AutomationProperties.SetName(button, $"字重 {weight}");
            button.Click += (_, _) => { plain.FontWeight = FontWeight.FromOpenTypeWeight(weight); run.FontWeight = plain.FontWeight;
                status.Text = $"字重已切换为 {weight}，检查两处文本和光标。"; plain.Focus(); };
            weights.Children.Add(button);
        }
        body.Children.Add(weights);
        body.Children.Add(new TextBlock { Text = "字宽", FontSize = 15, Foreground = Ink });
        var widths = new WrapPanel();
        foreach (var (label, stretch) in new[] { ("窄体 75%", FontStretches.Condensed), ("标准 100%", FontStretches.Normal), ("宽体 125%", FontStretches.Expanded) })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { plain.FontStretch = stretch; run.FontStretch = stretch;
                status.Text = $"字宽已切换为{label}，比较两处文本的字形、换行、光标和选区。"; plain.Focus(); };
            widths.Children.Add(button);
        }
        var exactWidth = new Button { Content = "SF 精确字宽 90.25%", MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
        exactWidth.Click += (_, _) => {
            plain.ClearValue(TextElement.FontStretchProperty); run.ClearValue(TextElement.FontStretchProperty);
            plain.ClearValue(TextElement.FontFamilyProperty); run.ClearValue(TextElement.FontFamilyProperty);
            Css.SetStyle(plain, "font-family:SF Pro;font-width:90.25%"); Css.SetStyle(run, "font-family:SF Pro;font-width:90.25%");
            status.Text = "当前：SF 精确字宽 90.25%，检查百分比宽度、光标与选区。"; plain.Focus();
        };
        widths.Children.Add(exactWidth); body.Children.Add(widths);
        body.Children.Add(new TextBlock { Text = "标准字宽的字重匹配", FontSize = 15, Foreground = Ink });
        var matching = new WrapPanel();
        foreach (var (label, family, weight) in new[] { ("Helvetica 900", "Helvetica Neue", 900),
            ("Helvetica 450", "Helvetica Neue", 450), ("Helvetica 501", "Helvetica Neue", 501),
            ("私有 Helvetica 900", "WindowLabHelvetica", 900) })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => {
                plain.ClearValue(TextElement.FontFamilyProperty); run.ClearValue(TextElement.FontFamilyProperty);
                plain.ClearValue(TextElement.FontStretchProperty); run.ClearValue(TextElement.FontStretchProperty);
                plain.ClearValue(TextElement.FontWeightProperty); run.ClearValue(TextElement.FontWeightProperty);
                Css.SetStyle(plain, $"font-family:'{family}';font-weight:{weight};font-width:100%");
                Css.SetStyle(run, $"font-family:'{family}';font-weight:{weight};font-width:100%");
                status.Text = $"当前：{label} · 标准字宽。检查字形、光标和选区；再切换窄体进行比较。"; plain.Focus();
            };
            matching.Children.Add(button);
        }
        body.Children.Add(matching);
        var styles = new WrapPanel();
        foreach (var (label, value) in new[] { ("普通", 0), ("斜体", 1), ("倾斜", 2) })
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { plain.FontStyle = FontStyle.FromOpenTypeStyle(value); run.FontStyle = plain.FontStyle;
                status.Text = $"样式已切换为{label}，检查两处文本和选区。"; plain.Focus(); };
            styles.Children.Add(button);
        }
        body.Children.Add(styles); body.Children.Add(status);
        body.Children.Add(new TextBlock { Text = "普通文本", FontSize = 18, Foreground = Ink }); body.Children.Add(plain);
        body.Children.Add(new TextBlock { Text = "富文本", FontSize = 18, Foreground = Ink }); body.Children.Add(rich);
        Jalium.UI.Automation.AutomationProperties.SetName(plain, "字体回退普通文本");
        Jalium.UI.Automation.AutomationProperties.SetName(rich, "字体回退富文本");
        Css.SetStyle(plain, "font-family: MissingWindowLab43, Menlo"); Css.SetStyle(run, "font-family: MissingWindowLab43, Menlo");
        status.Text = "当前：首项缺失 · 应使用 Menlo 等宽字体";
        var child = new Window { Title = "字体回退验证", Width = 600, Height = 650, MinWidth = 300, MinHeight = 430,
            Owner = this, Background = Background, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        fontWindow = child;
        child.Show(); plain.Focus();
    }

    private sealed class LabSubsetResolver : ICssResourceResolver
    {
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            return new(uri, new MemoryStream(await File.ReadAllBytesAsync("/System/Library/Fonts/ArialHB.ttc", cancellationToken).ConfigureAwait(false)), "font/collection");
        }
    }

    private void ShowSecondWindow()
    {
        var editor = new TextBox { Text="第二窗口焦点", Margin=new Thickness(24), Height=52 };
        var child=new Window
        {
            Title="第二窗口 · 焦点验证", Width=600, Height=300,
            Owner=this, Content=editor, Background=Background,
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        child.Activated += (_,_) => Record("第二窗口激活");
        child.Closed += (_,_) => Record("第二窗口关闭");
        child.Show();
        editor.Focus();
    }

    private void ShowParticipationWindow()
    {
        CreateParticipationWindow(this).Show();
    }

    internal static Window CreateParticipationWindow(Window? owner = null)
    {
        var editor = new TextBox { Text="全屏与还原后继续编辑", MinHeight=44 };
        Jalium.UI.Automation.AutomationProperties.SetName(editor, "窗口参与验证编辑框");
        var status = new TextBlock { FontSize=17, MinHeight=116, Foreground=Ink, TextWrapping=TextWrapping.Wrap };
        var body = new StackPanel { Spacing=16, Margin=new Thickness(24) };
        body.Children.Add(new TextBlock { Text="窗口菜单与全屏", FontSize=30, Foreground=Ink });
        body.Children.Add(new TextBlock
        {
            Text="全屏时切换窗口菜单显示，画面尺寸应保持不变。退出全屏后应恢复原来的窗口尺寸和编辑内容。",
            FontSize=15, Foreground=Ink, TextWrapping=TextWrapping.Wrap
        });
        body.Children.Add(new Border
        {
            Background=new SolidColorBrush(Color.FromRgb(0xe5,0xf4,0xf8)),
            Padding=new Thickness(16), CornerRadius=new CornerRadius(8), Child=status
        });
        body.Children.Add(new TextBlock { Text="编辑内容", FontSize=15, Foreground=Ink });
        body.Children.Add(editor);
        var row = new WrapPanel { Orientation=Orientation.Horizontal };
        var visibility = new Button { Content="从窗口菜单隐藏", MinHeight=44, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,12,12) };
        var fullscreen = new Button { Content="进入全屏", MinHeight=44, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,12,12) };
        var minimum = new Button { Content="最小尺寸", MinHeight=44, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,12,12) };
        var titlebar = new Button { Content="原生标题栏", MinHeight=44, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,12,12) };
        var close = new Button { Content="关闭验证窗口", IsCancel=true, MinHeight=44, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,0,12) };
        row.Children.Add(visibility); row.Children.Add(fullscreen); row.Children.Add(minimum); row.Children.Add(titlebar); row.Children.Add(close);
        body.Children.Add(row);
        var window = new Window
        {
            Title="窗口参与 · 全屏验证", Width=680, Height=600, MinWidth=520, MinHeight=580,
            Owner=owner, Content=new ScrollViewer
            {
                Content=body, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
            }, Background=new SolidColorBrush(Color.FromRgb(0xf5,0xfa,0xfc)),
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        void Refresh(string kind)
        {
            var bounds=window.RestoreBounds;
            var native=window.Handle == 0 ? null : ObjCRuntime.Runtime.GetNSObject<NSView>(window.Handle)?.Window;
            bool nativeFullScreen=native is not null && (native.StyleMask & NSWindowStyle.FullScreenWindow) != 0;
            bool nativeContentFocused=native?.FirstResponder?.Handle == window.Handle;
            bool nativeResponderIsWindow=native is not null && native.FirstResponder?.Handle == native.Handle;
            string restored=bounds.IsEmpty ? "尚未显示" : $"{bounds.Width:0.##} × {bounds.Height:0.##}";
            status.Text=$"窗口菜单：{(window.ShowInTaskbar ? "显示" : "隐藏")}\n托管状态：{window.WindowState}\n原生全屏：{(nativeFullScreen ? "是" : "否")} · 标题栏：{window.TitleBarStyle}\n客户区 DIP：{window.Width:0.##} × {window.Height:0.##}\n还原 DIP：{restored}\n输入响应：{(nativeContentFocused ? "渲染视图" : nativeResponderIsWindow ? "窗口" : "其他")}";
            visibility.Content=window.ShowInTaskbar ? "从窗口菜单隐藏" : "在窗口菜单显示";
            fullscreen.Content=window.WindowState == WindowState.FullScreen ? "退出全屏" : "进入全屏";
            minimum.IsEnabled=window.WindowState == WindowState.Normal && !nativeFullScreen;
            titlebar.IsEnabled=!nativeFullScreen && window.WindowState != WindowState.FullScreen;
            titlebar.Content=window.TitleBarStyle == WindowTitleBarStyle.Custom ? "原生标题栏" : "自定义标题栏";
            var snapshot=new WindowParticipationSnapshot(Environment.ProcessId, kind, window.WindowState.ToString(),
                window.TitleBarStyle.ToString(), nativeFullScreen, native?.IsKeyWindow == true, native?.IsMainWindow == true, window.ShowInTaskbar,
                nativeContentFocused, nativeResponderIsWindow,
                window.Width, window.Height, bounds.IsEmpty ? null : bounds.Width, bounds.IsEmpty ? null : bounds.Height,
                editor.Text, editor.IsKeyboardFocused, DateTimeOffset.UtcNow);
            File.AppendAllText(Program.LogPrefix + "-participation.jsonl",
                System.Text.Json.JsonSerializer.Serialize(snapshot, WindowLabJsonContext.Default.WindowParticipationSnapshot)+Environment.NewLine);
        }
        visibility.Click += (_,_) => { window.ShowInTaskbar=!window.ShowInTaskbar; editor.Focus(); Refresh("切换菜单显示"); };
        fullscreen.Click += (_,_) =>
        {
            window.WindowState=window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            editor.Focus(); Refresh("请求全屏切换");
        };
        minimum.Click += (_,_) =>
        {
            bool restore=window.Width <= window.MinWidth && window.Height <= window.MinHeight;
            window.Width=restore ? 680 : window.MinWidth; window.Height=restore ? 600 : window.MinHeight;
            minimum.Content=restore ? "最小尺寸" : "恢复尺寸"; editor.Focus(); Refresh("切换尺寸");
        };
        titlebar.Click += (_,_) =>
        {
            window.TitleBarStyle=window.TitleBarStyle == WindowTitleBarStyle.Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
            editor.Focus(); Refresh("切换标题栏");
        };
        close.Click += (_,_) => window.Close();
        window.Loaded += (_,_) => { editor.Focus(); Refresh("显示"); };
        window.SizeChanged += (_,_) => Refresh("尺寸");
        window.StateChanged += (_,_) =>
        {
            Refresh("状态");
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                if (window.Handle != 0) Refresh("状态稳定");
            });
        };
        window.Activated += (_,_) => Refresh("激活");
        editor.TextChanged += (_,_) => Refresh("编辑");
        var transitions=new List<NSObject>();
        window.Loaded += (_,_) =>
        {
            var native=ObjCRuntime.Runtime.GetNSObject<NSView>(window.Handle)?.Window;
            foreach (var name in new[] { NSWindow.WillEnterFullScreenNotification, NSWindow.DidEnterFullScreenNotification,
                NSWindow.WillExitFullScreenNotification, NSWindow.DidExitFullScreenNotification })
                transitions.Add(NSNotificationCenter.DefaultCenter.AddObserver(name, _ => Refresh(name.ToString()), native));
        };
        window.Closed += (_,_) =>
        {
            foreach (var token in transitions) { NSNotificationCenter.DefaultCenter.RemoveObserver(token); token.Dispose(); }
            Refresh("关闭");
        };
        Refresh("创建");
        return window;
    }

    private void ShowCssButtonWindow()
    {
        var editor = new TextBox { Text="保持编辑焦点，按 Enter 或 Escape", MinHeight=42 };
        Jalium.UI.Automation.AutomationProperties.SetName(editor, "CSS 验证编辑框");
        var status = new TextBlock { FontSize=15, Height=78, Foreground=Ink, TextWrapping=TextWrapping.Wrap };
        var body = new StackPanel { Spacing=14, Margin=new Thickness(24) };
        body.Children.Add(new TextBlock { Text="默认与取消按钮的可见性", FontSize=24, Foreground=Ink });
        body.Children.Add(new TextBlock { Text="切换模式后，Enter/Escape 应只调用期望按钮。隐藏或退出中的按钮调用次数应始终为 0。",
            FontSize=15, Foreground=Ink, TextWrapping=TextWrapping.Wrap });
        body.Children.Add(editor); body.Children.Add(status);
        Button ActionButton(string label, bool cancelAction = false) => new()
        {
            Content=label, IsDefault=!cancelAction, IsCancel=cancelAction,
            MinHeight=42, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,12,0)
        };
        var inheritedDefault = ActionButton("后代确认"); var inheritedCancel = ActionButton("后代取消", true);
        var fallbackDefault = ActionButton("可见确认"); var fallbackCancel = ActionButton("可见取消", true);
        var hiddenBox = new StackPanel { Orientation=Orientation.Horizontal };
        hiddenBox.Children.Add(inheritedDefault); hiddenBox.Children.Add(inheritedCancel);
        var flexHost = new StackPanel(); flexHost.Children.Add(hiddenBox); Css.SetStyle(flexHost, "display: flex");
        var fallback = new StackPanel { Orientation=Orientation.Horizontal };
        fallback.Children.Add(fallbackDefault); fallback.Children.Add(fallbackCancel);
        var actions = new StackPanel { Spacing=8 }; actions.Children.Add(flexHost); actions.Children.Add(fallback); body.Children.Add(actions);
        var dialog = new Window { Title="CSS 可见性 · Window 按钮", Width=660, Height=600, MinWidth=540, MinHeight=580,
            Owner=this, Content=body, Background=Background, WindowStartupLocation=WindowStartupLocation.CenterOwner };
        int mode=0, confirms=0, cancels=0, hiddenCalls=0;
        string[] modes=["CSS 隐藏", "显式显示后代", "原生折叠", "display:none", "折叠 flex 项目", "退出动画（60 秒）"];
        void RefreshStatus() => status.Text=$"模式：{modes[mode]}\n期望按钮：{(mode == 1 ? "后代确认 / 后代取消" : "可见确认 / 可见取消")}\n确认 {confirms} 次，取消 {cancels} 次，隐藏按钮 {hiddenCalls} 次";
        void RecordAction(bool cancelAction, bool fromDescendant)
        {
            if (fromDescendant && mode != 1) hiddenCalls++;
            if (cancelAction) cancels++; else confirms++;
            RefreshStatus(); Record($"CSS {modes[mode]} {(cancelAction ? "取消" : "确认")}，隐藏调用 {hiddenCalls}");
        }
        inheritedDefault.Click += (_,_) => RecordAction(false, true); inheritedCancel.Click += (_,_) => RecordAction(true, true);
        fallbackDefault.Click += (_,_) => RecordAction(false, false); fallbackCancel.Click += (_,_) => RecordAction(true, false);
        void ApplyMode()
        {
            hiddenBox.ClearValue(UIElement.VisibilityProperty);
            // Reset the authored display before applying the next mode;
            // active display presentation must still honor CSS visibility.
            Css.SetStyle(hiddenBox, "display:block; visibility:visible; transition:none");
            Css.SetStyle(hiddenBox, mode == 3 ? "display:none" : mode == 4 ? "visibility:collapse" : mode == 5
                ? "display:block; visibility:visible; transition:display 60s allow-discrete" : "visibility:hidden");
            Css.SetStyle(inheritedDefault, mode == 0 ? string.Empty : "visibility:visible");
            Css.SetStyle(inheritedCancel, mode == 0 ? string.Empty : "visibility:visible");
            if (mode == 5) Css.SetStyle(hiddenBox, "display:none; visibility:visible; transition:display 60s allow-discrete");
            if (mode == 2) hiddenBox.Visibility=Visibility.Collapsed;
            fallback.Visibility=mode == 1 ? Visibility.Collapsed : Visibility.Visible;
            RefreshStatus(); editor.Focus();
        }
        var controls = new WrapPanel { Orientation=Orientation.Horizontal };
        var next = new Button { Content="切换验证模式", MinHeight=42, Padding=new Thickness(16,8,16,8), Margin=new Thickness(0,0,12,0) };
        var close = new Button { Content="关闭验证窗口", MinHeight=42, Padding=new Thickness(16,8,16,8) };
        var resize = new Button { Content="最小尺寸", MinHeight=42, Padding=new Thickness(16,8,16,8), Margin=new Thickness(12,0,0,0) };
        resize.Click += (_,_) =>
        {
            bool restore = dialog.Width <= dialog.MinWidth && dialog.Height <= dialog.MinHeight;
            dialog.Width=restore ? 660 : dialog.MinWidth; dialog.Height=restore ? 600 : dialog.MinHeight;
            resize.Content=restore ? "最小尺寸" : "恢复尺寸";
            dialog.UpdateLayout();
            Record($"CSS 客户区 {dialog.ActualWidth:F0}×{dialog.ActualHeight:F0}");
        };
        var role = new Button { Content="对话框", MinHeight=42, Padding=new Thickness(10,8,10,8), Margin=new Thickness(12,0,0,0) };
        role.Click += (_,_) =>
        {
            bool wasDialog = Jalium.UI.Automation.AutomationProperties.GetIsDialog(dialog);
            Jalium.UI.Automation.AutomationProperties.SetIsDialog(dialog, !wasDialog);
            role.Content=wasDialog ? "对话框" : "普通窗口";
            Record($"CSS 对话框标记 {!wasDialog}");
        };
        next.Click += (_,_) => { mode=(mode+1)%modes.Length; ApplyMode(); Record($"CSS 切换 {modes[mode]}"); };
        close.Click += (_,_) => dialog.Close(); controls.Children.Add(next); controls.Children.Add(close); controls.Children.Add(resize); controls.Children.Add(role); body.Children.Add(controls);
        ApplyMode(); dialog.Loaded += (_,_) => editor.Focus(); dialog.Show(); Record("CSS 验证窗口显示");
    }

    private void ShowIndependentWindow()
    {
        if (_independentWindow is { Handle: not 0 } existing) { existing.Activate(); return; }
        var editor = new TextBox { Text="独立窗口仍需遵守应用模态", Margin=new Thickness(24), Height=52 };
        var window = new Window
        {
            Title="独立窗口 · 应用模态验证", Width=600, Height=300,
            Content=editor, Background=Background, WindowStartupLocation=WindowStartupLocation.CenterScreen
        };
        _independentWindow=window;
        window.Activated += (_,_) => Record("独立窗口激活");
        window.Closed += (_,_) => { _independentWindow=null; Record("独立窗口关闭"); };
        window.Show();
        editor.Focus();
    }

    private void ShowModalWindow()
    {
        if (_modalWindow is { Handle: not 0 } existing)
        {
            var repeatedResult=existing.ShowDialog();
            Record($"重开模态结果 {repeatedResult?.ToString() ?? "null"}，主窗口可用 {IsEnabled}");
            return;
        }
        var editor = new TextBox { Text="模态窗口焦点", MinHeight=42 };
        Jalium.UI.Automation.AutomationProperties.SetName(editor, "模态窗口编辑器");
        var status = new TextBlock { Text="首次关闭会取消；隐藏后可再次打开同一窗口。", TextWrapping=TextWrapping.Wrap, FontSize=15, Foreground=Ink };
        var body = new StackPanel { Spacing=16, Margin=new Thickness(24) };
        body.Children.Add(new TextBlock { Text="模态关闭与结果", FontSize=24, Foreground=Ink });
        body.Children.Add(status);
        body.Children.Add(editor);
        var confirm = new Button { Content="确认", IsDefault=true, MinHeight=42, Padding=new Thickness(16,8,16,8) };
        var cancel = new Button { Content="取消", IsCancel=true, MinHeight=42, Margin=new Thickness(12,0,0,0), Padding=new Thickness(16,8,16,8) };
        var hide = new Button { Content="隐藏", MinHeight=42, Margin=new Thickness(12,0,0,0), Padding=new Thickness(16,8,16,8) };
        var nested = new Button { Content="打开内层模态", MinHeight=42, Margin=new Thickness(12,0,0,0), Padding=new Thickness(16,8,16,8) };
        var row = new WrapPanel { Orientation=Orientation.Horizontal };
        row.Children.Add(confirm); row.Children.Add(cancel); row.Children.Add(hide); row.Children.Add(nested); body.Children.Add(row);
        var nativeMenu = new Button { Content="打开原生 Window 菜单", MinHeight=42, Padding=new Thickness(16,8,16,8) };
        body.Children.Add(nativeMenu);
        var dialog = new Window
        {
            Title="模态窗口 · 关闭验证", Width=MinimumModalSize ? 500 : 640,
            Height=MinimumModalSize ? 380 : 400, MinWidth=500, MinHeight=380,
            Owner=this, Content=body, Background=Background, TitleBarStyle=this.TitleBarStyle,
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        bool cancelNextClose=true;
        nativeMenu.Click += (_,_) => { dialog.Activate(); editor.Focus(); Program.ShowNativeWindowMenu(dialog); };
        _modalWindow=dialog;
        dialog.Closed += (_,_) => _modalWindow=null;
        dialog.Loaded += (_,_) => editor.Focus();
        dialog.Closing += (_,args) =>
        {
            if (!cancelNextClose) return;
            cancelNextClose=false;
            args.Cancel=true;
            status.Text="已取消首次关闭，模态窗口仍可编辑；再次确认会关闭。";
            Record("模态关闭取消");
        };
        confirm.Click += (_,_) => dialog.DialogResult=true;
        cancel.Click += (_,_) => dialog.DialogResult=false;
        hide.Click += (_,_) => dialog.Hide();
        nested.Click += (_,_) => ShowNestedModalWindow(dialog);
        var result=dialog.ShowDialog();
        Record($"模态结果 {result?.ToString() ?? "null"}，主窗口可用 {IsEnabled}");
    }

    private void ShowNestedModalWindow(Window owner)
    {
        var editor = new TextBox { Text="内层模态窗口", MinHeight=42 };
        var body = new StackPanel { Spacing=16, Margin=new Thickness(24) };
        body.Children.Add(new TextBlock { Text="嵌套模态与恢复", FontSize=24, Foreground=Ink });
        body.Children.Add(new TextBlock
        {
            Text="关闭后应恢复外层模态窗口，其余窗口继续保持不可操作。",
            TextWrapping=TextWrapping.Wrap, FontSize=15, Foreground=Ink
        });
        body.Children.Add(editor);
        var confirm = new Button { Content="确认", IsDefault=true, MinHeight=42, Padding=new Thickness(16,8,16,8) };
        var cancel = new Button { Content="取消", IsCancel=true, MinHeight=42, Margin=new Thickness(12,0,0,0), Padding=new Thickness(16,8,16,8) };
        var row = new WrapPanel { Orientation=Orientation.Horizontal };
        row.Children.Add(confirm); row.Children.Add(cancel); body.Children.Add(row);
        var dialog = new Window
        {
            Title="内层模态窗口", Width=600, Height=370, MinWidth=440, MinHeight=330,
            Owner=owner, Content=body, Background=Background, WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        dialog.Loaded += (_,_) => editor.Focus();
        confirm.Click += (_,_) => dialog.DialogResult=true;
        cancel.Click += (_,_) => dialog.DialogResult=false;
        Record("打开内层模态");
        var result=dialog.ShowDialog();
        Record($"内层模态结果 {result?.ToString() ?? "null"}，外层可用 {owner.IsEnabled}，主窗口可用 {IsEnabled}");
    }

    private void Record(string kind)
    {
        var bounds=RestoreBounds;
        string text=$"{++_sequence:00} {kind}  {WindowState}  {SizeToContent}  {Width:0.##} × {Height:0.##}";
        _events.Add(text);
        string restoreText = bounds.IsEmpty ? "尚未显示" : $"{bounds.Width:0.##} × {bounds.Height:0.##}";
        _status.Text=string.Join(Environment.NewLine,
            $"状态：{WindowState}    自适应：{SizeToContent}",
            $"客户区 DIP：{Width:0.##} × {Height:0.##}",
            $"还原 DIP：{restoreText}");
        _history.Text=string.Join(Environment.NewLine,_events.TakeLast(6));
        var snapshot = new WindowSnapshot(_sequence, kind, WindowState.ToString(), SizeToContent.ToString(),
            Width, Height, bounds.IsEmpty ? null : bounds.Width, bounds.IsEmpty ? null : bounds.Height, DateTimeOffset.UtcNow,
            Environment.ProcessId, TitleBarStyle.ToString());
        File.AppendAllText(_log, System.Text.Json.JsonSerializer.Serialize(snapshot,
            WindowLabJsonContext.Default.WindowSnapshot)+Environment.NewLine);
    }
}

internal sealed record WindowSnapshot(int sequence, string kind, string state, string sizeToContent,
    double width, double height, double? restoreWidth, double? restoreHeight, DateTimeOffset utc, int pid, string titleBar);

internal sealed record WindowMenuTrackingSnapshot(DateTimeOffset at, int pid, string kind,
    string? menu, string? keyWindow, string? runLoopMode);

internal sealed record WindowParticipationSnapshot(int pid, string kind, string state, string titleBar,
    bool nativeFullScreen, bool nativeKey, bool nativeMain, bool showInTaskbar,
    bool nativeContentFocused, bool nativeResponderIsWindow,
    double width, double height, double? restoreWidth, double? restoreHeight, string text, bool editorFocused, DateTimeOffset utc);

[System.Text.Json.Serialization.JsonSerializable(typeof(WindowSnapshot))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WindowParticipationSnapshot))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WindowAppearanceSnapshot))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WindowQuitSnapshot))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WindowMenuTrackingSnapshot))]
internal partial class WindowLabJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
