using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.TransparentBackdropDemo.Themes;

namespace Jalium.UI.TransparentBackdropDemo;

/// <summary>A selectable native backdrop demonstration with a replaceable glass-card template.</summary>
[TemplatePart(Name = "PART_Effects", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_ToggleBackground", Type = typeof(Button))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Transparent", GroupName = "BackgroundStates")]
[TemplateVisualState(Name = "Tinted", GroupName = "BackgroundStates")]
public class TransparentBackdropSurface : Control
{
    public sealed record BackdropChoice(string Label, WindowBackdropType Type);
    private static readonly IReadOnlyList<BackdropChoice> Choices = Array.AsReadOnly(new[]
    {
        new BackdropChoice("无 · 纯透明", WindowBackdropType.None), new BackdropChoice("Mica 云母", WindowBackdropType.Mica),
        new BackdropChoice("Mica Alt", WindowBackdropType.MicaAlt), new BackdropChoice("Acrylic 亚克力", WindowBackdropType.Acrylic), new BackdropChoice("Auto 自动", WindowBackdropType.Auto)
    });
    public static readonly DependencyProperty OwnerWindowProperty = DependencyProperty.Register(nameof(OwnerWindow), typeof(Window), typeof(TransparentBackdropSurface), new PropertyMetadata(null, OnOwnerChanged));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(TransparentBackdropSurface), new PropertyMetadata("窗口背景特效"));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(TransparentBackdropSurface), new PropertyMetadata("WindowStyle=None · AllowsTransparency=True · TitleBarStyle=Custom · Background=null"));
    public static readonly DependencyProperty SelectedEffectProperty = DependencyProperty.Register(nameof(SelectedEffect), typeof(WindowBackdropType), typeof(TransparentBackdropSurface), new PropertyMetadata(WindowBackdropType.None, OnPresentationChanged), value => value is WindowBackdropType type && Enum.IsDefined(type));
    public static readonly DependencyProperty IsTintedProperty = DependencyProperty.Register(nameof(IsTinted), typeof(bool), typeof(TransparentBackdropSurface), new PropertyMetadata(false, OnPresentationChanged));
    public static readonly DependencyProperty TintBrushProperty = DependencyProperty.Register(nameof(TintBrush), typeof(Brush), typeof(TransparentBackdropSurface), new PropertyMetadata(null, OnPresentationChanged));
    private static readonly DependencyPropertyKey EffectsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Effects), typeof(IReadOnlyList<BackdropChoice>), typeof(TransparentBackdropSurface), new PropertyMetadata(Choices));
    public static readonly DependencyProperty EffectsProperty = EffectsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(TransparentBackdropSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

    public Window? OwnerWindow { get => (Window?)GetValue(OwnerWindowProperty); set => SetValue(OwnerWindowProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public WindowBackdropType SelectedEffect { get => (WindowBackdropType)GetValue(SelectedEffectProperty)!; set => SetValue(SelectedEffectProperty, value); }
    public bool IsTinted { get => (bool)GetValue(IsTintedProperty)!; set => SetValue(IsTintedProperty, value); }
    public Brush? TintBrush { get => (Brush?)GetValue(TintBrushProperty); set => SetValue(TintBrushProperty, value); }
    public IReadOnlyList<BackdropChoice> Effects => (IReadOnlyList<BackdropChoice>)GetValue(EffectsProperty)!;
    public string StatusText => (string)GetValue(StatusTextProperty)!;

    private ListBox? _effects;
    private Button? _toggle;
    private Window? _window;
    private bool _synchronizing;

    public TransparentBackdropSurface()
    {
        TransparentBackdropDemoTheme.Register(); DefaultStyleKey = typeof(TransparentBackdropSurface);
        Loaded += (_, _) => AttachWindow(OwnerWindow ?? Window.GetWindow(this));
        Unloaded += (_, _) => AttachWindow(null);
        UpdatePresentation();
    }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _effects = GetTemplateChild("PART_Effects") as ListBox;
        if (_effects is not null) _effects.SelectionChanged += OnSelectionChanged;
        _toggle = GetTemplateChild("PART_ToggleBackground") as Button;
        if (_toggle is not null) _toggle.Click += OnToggleBackground;
        UpdatePresentation();
    }
    protected override void OnTemplateChanging() { ReleaseParts(); base.OnTemplateChanging(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    public void ToggleBackground() => SetCurrentValue(IsTintedProperty, !IsTinted);
    private Window? ResolveWindow() => OwnerWindow ?? _window ?? Window.GetWindow(this);
    private static void OnOwnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TransparentBackdropSurface)d).AttachWindow((Window?)e.NewValue);
    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TransparentBackdropSurface)d).UpdatePresentation();
    private void AttachWindow(Window? window)
    {
        if (ReferenceEquals(_window, window)) return;
        if (_window is not null) { _window.KeyDown -= OnKeyDown; _window.Closed -= OnClosed; }
        _window = window;
        if (_window is not null) { _window.KeyDown += OnKeyDown; _window.Closed += OnClosed; UpdatePresentation(); }
    }
    private void OnClosed(object? sender, EventArgs e) => AttachWindow(null);
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || ResolveWindow() is not { } window) return;
        if (e.Key == Key.F11) { window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; e.Handled = true; }
        else if (e.Key == Key.Escape && window.WindowState == WindowState.FullScreen) { SystemCommands.RestoreWindow(window); e.Handled = true; }
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_synchronizing && IsEnabled && _effects?.SelectedItem is BackdropChoice choice) SetCurrentValue(SelectedEffectProperty, choice.Type);
    }
    private void OnToggleBackground(object sender, RoutedEventArgs e) { if (IsEnabled) ToggleBackground(); }
    private void UpdatePresentation()
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            if (_effects is not null) _effects.SetCurrentValue(ListBox.SelectedItemProperty, Choices.First(x => x.Type == SelectedEffect));
            if (ResolveWindow() is { } window)
            {
                window.SetCurrentValue(Window.SystemBackdropProperty, SelectedEffect);
                window.SetCurrentValue(BackgroundProperty, IsTinted ? TintBrush : null);
            }
            var effect = SelectedEffect switch
            {
                WindowBackdropType.None => "无（窗口纯透明，透出桌面）", WindowBackdropType.Mica => "Mica 云母（采样桌面壁纸）",
                WindowBackdropType.MicaAlt => "Mica Alt（云母变体）", WindowBackdropType.Acrylic => "Acrylic 亚克力（模糊背后内容）", _ => "Auto（由 DWM 自动决定）"
            };
            SetValue(StatusTextPropertyKey, $"当前特效：{effect}    ·    窗口背景：{(IsTinted ? "半透明纯色（会盖住材质 / 桌面）" : "null（透出特效 / 桌面）")}");
            UpdateStates();
        }
        finally { _synchronizing = false; }
    }
    private void ReleaseParts()
    {
        if (_effects is not null) _effects.SelectionChanged -= OnSelectionChanged;
        if (_toggle is not null) _toggle.Click -= OnToggleBackground;
        _effects = null; _toggle = null;
    }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsTinted ? "Tinted" : "Transparent", false);
    }
}
