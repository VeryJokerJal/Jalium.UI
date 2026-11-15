using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.BorderlessDemo.Themes;

namespace Jalium.UI.BorderlessDemo;

/// <summary>Reusable window controls with replaceable chrome and optional template parts.</summary>
[TemplatePart(Name = "PART_TitleBar", Type = typeof(TitleBar))]
[TemplatePart(Name = "PART_EnterFullScreen", Type = typeof(Button))]
[TemplatePart(Name = "PART_ExitFullScreen", Type = typeof(Button))]
[TemplatePart(Name = "PART_ToggleWindowStyle", Type = typeof(Button))]
[TemplatePart(Name = "PART_ToggleMaximize", Type = typeof(Button))]
[TemplatePart(Name = "PART_Close", Type = typeof(Button))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Windowed", GroupName = "WindowStates")]
[TemplateVisualState(Name = "Maximized", GroupName = "WindowStates")]
[TemplateVisualState(Name = "FullScreen", GroupName = "WindowStates")]
public class BorderlessDemoSurface : Control
{
    public static readonly DependencyProperty OwnerWindowProperty = DependencyProperty.Register(nameof(OwnerWindow), typeof(Window), typeof(BorderlessDemoSurface), new PropertyMetadata(null, OnOwnerChanged));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(BorderlessDemoSurface), new PropertyMetadata("Jalium.UI BorderlessDemo"));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(BorderlessDemoSurface), new PropertyMetadata("WindowStyle=None · AllowsTransparency=True"));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(BorderlessDemoSurface), new PropertyMetadata("拖动顶部区域可移动窗口；拖动四边可调整大小。"));
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(BorderlessDemoSurface), new PropertyMetadata("当前状态：Normal"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

    public Window? OwnerWindow { get => (Window?)GetValue(OwnerWindowProperty); set => SetValue(OwnerWindowProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty)!; set => SetValue(HintProperty, value); }
    public string StatusText => (string)GetValue(StatusTextProperty)!;

    private Window? _window;
    private TitleBar? _titleBar;
    private Button? _enter, _exit, _style, _maximize, _close;

    public BorderlessDemoSurface()
    {
        BorderlessDemoTheme.Register();
        DefaultStyleKey = typeof(BorderlessDemoSurface);
        Loaded += (_, _) => AttachWindow(OwnerWindow ?? Window.GetWindow(this));
        Unloaded += (_, _) => AttachWindow(null);
    }

    public override void OnApplyTemplate()
    {
        ReleaseParts();
        base.OnApplyTemplate();
        _titleBar = GetTemplateChild("PART_TitleBar") as TitleBar;
        if (_titleBar is not null)
        {
            _titleBar.MinimizeClicked += OnMinimize;
            _titleBar.MaximizeRestoreClicked += OnMaximize;
            _titleBar.CloseClicked += OnClose;
            _titleBar.MouseLeftButtonDown += OnTitleBarMouseDown;
        }
        _enter = Hook("PART_EnterFullScreen", OnEnter);
        _exit = Hook("PART_ExitFullScreen", OnExit);
        _style = Hook("PART_ToggleWindowStyle", OnStyle);
        _maximize = Hook("PART_ToggleMaximize", OnMaximizeClick);
        _close = Hook("PART_Close", OnCloseClick);
        RefreshState();
    }

    protected override void OnTemplateChanging() { ReleaseParts(); base.OnTemplateChanging(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }

    public void EnterFullScreen() { if (ResolveWindow() is { } window) window.WindowState = WindowState.FullScreen; RefreshState(); }
    public void ExitFullScreen() { if (ResolveWindow() is { } window) SystemCommands.RestoreWindow(window); RefreshState(); }
    public void ToggleFullScreen() { if (ResolveWindow() is { } window) { if (window.WindowState == WindowState.FullScreen) ExitFullScreen(); else EnterFullScreen(); } }
    public void ToggleMaximize()
    {
        if (ResolveWindow() is { } window)
        {
            if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
            else SystemCommands.MaximizeWindow(window);
        }
        RefreshState();
    }
    public void ToggleWindowStyle()
    {
        if (ResolveWindow() is { } window)
            window.SetCurrentValue(Window.WindowStyleProperty, window.WindowStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : WindowStyle.None);
        RefreshState();
    }
    public void RefreshState()
    {
        var window = ResolveWindow();
        SetValue(StatusTextPropertyKey, window is null ? "当前状态：Normal" : $"当前状态：WindowState={window.WindowState}, WindowStyle={window.WindowStyle}");
        if (_titleBar is not null) _titleBar.SetCurrentValue(TitleBar.IsMaximizedProperty, window?.WindowState == WindowState.Maximized);
        UpdateStates();
    }

    private Window? ResolveWindow() => OwnerWindow ?? _window ?? Window.GetWindow(this);
    private static void OnOwnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var surface = (BorderlessDemoSurface)d; surface.AttachWindow((Window?)e.NewValue); }
    private void AttachWindow(Window? window)
    {
        if (ReferenceEquals(_window, window)) return;
        if (_window is not null) { _window.StateChanged -= OnWindowStateChanged; _window.KeyDown -= OnWindowKeyDown; _window.Closed -= OnWindowClosed; }
        _window = window;
        if (_window is not null) { _window.StateChanged += OnWindowStateChanged; _window.KeyDown += OnWindowKeyDown; _window.Closed += OnWindowClosed; }
        RefreshState();
    }
    private void OnWindowStateChanged(object? sender, EventArgs e) => RefreshState();
    private void OnWindowClosed(object? sender, EventArgs e) => AttachWindow(null);
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && ResolveWindow()?.WindowState == WindowState.FullScreen) { ExitFullScreen(); e.Handled = true; }
    }
    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsEnabled && !e.Handled && e.ClickCount == 1 && ResolveWindow() is { } window) { window.DragMove(); e.Handled = true; }
    }
    private void OnMinimize(object? sender, EventArgs e) { if (IsEnabled && ResolveWindow() is { } window) SystemCommands.MinimizeWindow(window); }
    private void OnMaximize(object? sender, EventArgs e) { if (IsEnabled) ToggleMaximize(); }
    private void OnClose(object? sender, EventArgs e) { if (IsEnabled && ResolveWindow() is { } window) SystemCommands.CloseWindow(window); }
    private void OnEnter(object sender, RoutedEventArgs e) { if (IsEnabled) EnterFullScreen(); }
    private void OnExit(object sender, RoutedEventArgs e) { if (IsEnabled) ExitFullScreen(); }
    private void OnStyle(object sender, RoutedEventArgs e) { if (IsEnabled) ToggleWindowStyle(); }
    private void OnMaximizeClick(object sender, RoutedEventArgs e) => OnMaximize(sender, e);
    private void OnCloseClick(object sender, RoutedEventArgs e) => OnClose(sender, e);
    private Button? Hook(string name, RoutedEventHandler handler) { var button = GetTemplateChild(name) as Button; if (button is not null) button.Click += handler; return button; }
    private void ReleaseParts()
    {
        if (_titleBar is not null) { _titleBar.MinimizeClicked -= OnMinimize; _titleBar.MaximizeRestoreClicked -= OnMaximize; _titleBar.CloseClicked -= OnClose; _titleBar.MouseLeftButtonDown -= OnTitleBarMouseDown; }
        if (_enter is not null) _enter.Click -= OnEnter;
        if (_exit is not null) _exit.Click -= OnExit;
        if (_style is not null) _style.Click -= OnStyle;
        if (_maximize is not null) _maximize.Click -= OnMaximizeClick;
        if (_close is not null) _close.Click -= OnCloseClick;
        _titleBar = null; _enter = _exit = _style = _maximize = _close = null;
    }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, ResolveWindow()?.WindowState switch { WindowState.FullScreen => "FullScreen", WindowState.Maximized => "Maximized", _ => "Windowed" }, false);
    }
}
