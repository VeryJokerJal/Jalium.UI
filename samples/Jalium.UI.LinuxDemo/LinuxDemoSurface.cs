using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.LinuxDemo.Themes;
using Microsoft.Win32;

namespace Jalium.UI.LinuxDemo;

/// <summary>Portable desktop-integration showcase; template parts are optional and replaceable.</summary>
[TemplatePart(Name = "PART_TitleBar", Type = typeof(TitleBar))]
[TemplatePart(Name = "PART_Click", Type = typeof(Button))]
[TemplatePart(Name = "PART_Copy", Type = typeof(Button))]
[TemplatePart(Name = "PART_Paste", Type = typeof(Button))]
[TemplatePart(Name = "PART_OpenFile", Type = typeof(Button))]
[TemplatePart(Name = "PART_Message", Type = typeof(Button))]
[TemplatePart(Name = "PART_About", Type = typeof(MenuItem))]
[TemplatePart(Name = "PART_Quit", Type = typeof(MenuItem))]
[TemplatePart(Name = "PART_Backends", Type = typeof(ComboBox))]
[TemplatePart(Name = "PART_Integrations", Type = typeof(ListBox))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class LinuxDemoSurface : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(LinuxDemoSurface), new PropertyMetadata("Jalium.UI — Linux 桌面示例"));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(LinuxDemoSurface), new PropertyMetadata("在这里输入（支持 XIM / text-input-v3）"));
    public static readonly DependencyProperty SelectedBackendIndexProperty = DependencyProperty.Register(nameof(SelectedBackendIndex), typeof(int), typeof(LinuxDemoSurface), new PropertyMetadata(0, OnBackendChanged));
    public static readonly DependencyProperty FeatureEnabledProperty = DependencyProperty.Register(nameof(FeatureEnabled), typeof(bool), typeof(LinuxDemoSurface), new PropertyMetadata(true, OnFeatureChanged));
    public static readonly DependencyProperty SliderValueProperty = DependencyProperty.Register(nameof(SliderValue), typeof(double), typeof(LinuxDemoSurface), new PropertyMetadata(40d, OnSliderChanged));
    public static readonly DependencyProperty SelectedIntegrationProperty = DependencyProperty.Register(nameof(SelectedIntegration), typeof(string), typeof(LinuxDemoSurface), new PropertyMetadata(null, OnIntegrationChanged));
    public static readonly DependencyProperty RadioGroupNameProperty = DependencyProperty.Register(nameof(RadioGroupName), typeof(string), typeof(LinuxDemoSurface), new PropertyMetadata(""));
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(LinuxDemoSurface), new PropertyMetadata("就绪 — hover, click, right-click, type…"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ClickCountPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ClickCount), typeof(int), typeof(LinuxDemoSurface), new PropertyMetadata(0));
    public static readonly DependencyProperty ClickCountProperty = ClickCountPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey BackendsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Backends), typeof(IReadOnlyList<string>), typeof(LinuxDemoSurface), new PropertyMetadata(Array.AsReadOnly(new[] { "Wayland (xdg-shell)", "X11 (Xlib + EWMH)", "软件渲染 (wl_shm / XPutImage)", "Vulkan (llvmpipe 亦可)" })));
    public static readonly DependencyProperty BackendsProperty = BackendsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IntegrationsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Integrations), typeof(IReadOnlyList<string>), typeof(LinuxDemoSurface), new PropertyMetadata(Array.AsReadOnly(new[] { "输入：xkbcommon / XKB 键盘映射", "剪贴板：文本双向", "拖放：XDND v5 / wl_data_device", "无障碍：AT-SPI2 桥", "通知：org.freedesktop.Notifications", "文件对话框：xdg-desktop-portal" })));
    public static readonly DependencyProperty IntegrationsProperty = IntegrationsPropertyKey.DependencyProperty;

    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Text { get => (string)GetValue(TextProperty)!; set => SetValue(TextProperty, value); }
    public int SelectedBackendIndex { get => (int)GetValue(SelectedBackendIndexProperty)!; set => SetValue(SelectedBackendIndexProperty, value); }
    public bool FeatureEnabled { get => (bool)GetValue(FeatureEnabledProperty)!; set => SetValue(FeatureEnabledProperty, value); }
    public double SliderValue { get => (double)GetValue(SliderValueProperty)!; set => SetValue(SliderValueProperty, value); }
    public string? SelectedIntegration { get => (string?)GetValue(SelectedIntegrationProperty); set => SetValue(SelectedIntegrationProperty, value); }
    public string RadioGroupName { get => (string)GetValue(RadioGroupNameProperty)!; set => SetValue(RadioGroupNameProperty, value); }
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    public int ClickCount => (int)GetValue(ClickCountProperty)!;
    public IReadOnlyList<string> Backends => (IReadOnlyList<string>)GetValue(BackendsProperty)!;
    public IReadOnlyList<string> Integrations => (IReadOnlyList<string>)GetValue(IntegrationsProperty)!;

    private TitleBar? _titleBar;
    private MenuItem? _about, _quit;
    private readonly List<(Button Button, RoutedEventHandler Handler)> _buttons = [];

    public LinuxDemoSurface()
    {
        LinuxDemoTheme.Register(); DefaultStyleKey = typeof(LinuxDemoSurface);
        SetCurrentValue(RadioGroupNameProperty, "LinuxDemo_" + Guid.NewGuid().ToString("N"));
    }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _titleBar = GetTemplateChild("PART_TitleBar") as TitleBar;
        if (_titleBar is not null) { _titleBar.CloseClicked += OnClose; _titleBar.MouseLeftButtonDown += OnDrag; }
        Hook("PART_Click", OnClick); Hook("PART_Copy", OnCopy); Hook("PART_Paste", OnPaste); Hook("PART_OpenFile", OnOpenFile); Hook("PART_Message", OnMessage);
        _about = GetTemplateChild("PART_About") as MenuItem; if (_about is not null) _about.Click += OnAbout;
        _quit = GetTemplateChild("PART_Quit") as MenuItem; if (_quit is not null) _quit.Click += OnQuit;
        UpdateStates();
    }
    protected override void OnTemplateChanging()
    {
        // Selection resets while an ItemsSource detaches. Disconnect its outgoing
        // two-way link before the retiring template can write that reset back.
        if (GetTemplateChild("PART_Backends") is ComboBox combo) combo.ClearBinding(Selector.SelectedIndexProperty);
        if (GetTemplateChild("PART_Integrations") is ListBox list) list.ClearBinding(Selector.SelectedItemProperty);
        ReleaseParts(); base.OnTemplateChanging();
    }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    public void Report(string message) { SetValue(StatusTextPropertyKey, message); Console.WriteLine($"[demo] {message}"); }
    private static void OnBackendChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var surface = (LinuxDemoSurface)d; if (surface.SelectedBackendIndex >= 0 && surface.SelectedBackendIndex < surface.Backends.Count) surface.Report($"选择了：{surface.Backends[surface.SelectedBackendIndex]}"); }
    private static void OnFeatureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LinuxDemoSurface)d).Report((bool)e.NewValue! ? "CheckBox: on" : "CheckBox: off");
    private static void OnSliderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LinuxDemoSurface)d).Report($"Slider: {(double)e.NewValue!:0}");
    private static void OnIntegrationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (e.NewValue is string value) ((LinuxDemoSurface)d).Report($"列表选择：{value}"); }
    private void OnClick(object sender, RoutedEventArgs e) { if (IsEnabled) { SetValue(ClickCountPropertyKey, checked(ClickCount + 1)); Report($"按钮点击 x{ClickCount}"); } }
    private void OnCopy(object sender, RoutedEventArgs e) { if (IsEnabled) { Clipboard.SetText(Text ?? string.Empty); Report("已复制到系统剪贴板（wl_data_device / X11 selection）"); } }
    private void OnPaste(object sender, RoutedEventArgs e) { if (IsEnabled) { var value = Clipboard.GetText(); SetCurrentValue(TextProperty, value); Report(string.IsNullOrEmpty(value) ? "剪贴板为空" : $"粘贴了 {value.Length} 个字符"); } }
    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;
        var dialog = new OpenFileDialog { Title = "选择一个文件", Filter = "文本文件|*.txt;*.md|所有文件|*.*" };
        Report(dialog.ShowDialog() == true ? $"选择了：{dialog.FileName}" : "对话框已取消（或无 portal 服务）");
    }
    private void OnMessage(object sender, RoutedEventArgs e)
    {
        if (IsEnabled) Report($"MessageBox 返回 {MessageBox.Show("这是框架自绘的跨平台 MessageBox。", "Jalium.UI", MessageBoxButton.OKCancel, MessageBoxImage.Information)}");
    }
    private void OnAbout(object sender, RoutedEventArgs e) { if (IsEnabled) Report("ContextMenu → 关于"); }
    private void OnQuit(object sender, RoutedEventArgs e) => OnClose(sender, e);
    private void OnClose(object? sender, EventArgs e) { if (IsEnabled && Window.GetWindow(this) is { } window) SystemCommands.CloseWindow(window); }
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (IsEnabled && !e.Handled && e.ClickCount == 1 && Window.GetWindow(this) is { } window) { window.DragMove(); e.Handled = true; } }
    private void Hook(string name, RoutedEventHandler handler) { if (GetTemplateChild(name) is Button button) { button.Click += handler; _buttons.Add((button, handler)); } }
    private void ReleaseParts()
    {
        if (_titleBar is not null) { _titleBar.CloseClicked -= OnClose; _titleBar.MouseLeftButtonDown -= OnDrag; _titleBar = null; }
        foreach (var (button, handler) in _buttons) button.Click -= handler;
        _buttons.Clear();
        if (_about is not null) _about.Click -= OnAbout;
        if (_quit is not null) _quit.Click -= OnQuit;
        _about = _quit = null;
    }
    private void UpdateStates() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}
