using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

/// <summary>Preserves the effect repro's transform and layout-damage animation as control behavior.</summary>
[TemplatePart(Name = "PART_MovingBlock", Type = typeof(Border))]
[TemplateVisualState(Name = "Playing", GroupName = "PlaybackStates")]
[TemplateVisualState(Name = "Paused", GroupName = "PlaybackStates")]
public class EffectReproSurface : Control
{
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(EffectReproSurface), new PropertyMetadata(true, OnPlayingChanged));
    public static readonly DependencyProperty ElapsedSecondsProperty = DependencyProperty.Register(nameof(ElapsedSeconds), typeof(double), typeof(EffectReproSurface), new PropertyMetadata(0d, OnTimeChanged), value => value is double time && double.IsFinite(time) && time >= 0);
    private static readonly DependencyPropertyKey GlowTransformPropertyKey = DependencyProperty.RegisterReadOnly(nameof(GlowTransform), typeof(TransformGroup), typeof(EffectReproSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty GlowTransformProperty = GlowTransformPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ShadowTransformPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShadowTransform), typeof(TransformGroup), typeof(EffectReproSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty ShadowTransformProperty = ShadowTransformPropertyKey.DependencyProperty;
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty)!; set => SetValue(IsPlayingProperty, value); }
    public double ElapsedSeconds { get => (double)GetValue(ElapsedSecondsProperty)!; set => SetValue(ElapsedSecondsProperty, value); }
    public TransformGroup GlowTransform => (TransformGroup)GetValue(GlowTransformProperty)!;
    public TransformGroup ShadowTransform => (TransformGroup)GetValue(ShadowTransformProperty)!;
    private readonly ScaleTransform _glowScale = new(), _shadowScale = new();
    private readonly TranslateTransform _glowTranslation = new(), _shadowTranslation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Border? _movingBlock;

    public EffectReproSurface()
    {
        EffectReproTheme.Register();
        var glow = new TransformGroup(); glow.Children.Add(_glowScale); glow.Children.Add(_glowTranslation); SetValue(GlowTransformPropertyKey, glow);
        var shadow = new TransformGroup(); shadow.Children.Add(_shadowScale); shadow.Children.Add(_shadowTranslation); SetValue(ShadowTransformPropertyKey, shadow);
        _timer.Tick += (_, _) => SetCurrentValue(ElapsedSecondsProperty, ElapsedSeconds + 0.016);
        Loaded += (_, _) => UpdateTimer(); Unloaded += (_, _) => _timer.Stop();
        DefaultStyleKey = typeof(EffectReproSurface);
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); _movingBlock = GetTemplateChild("PART_MovingBlock") as Border;
        UpdateAnimation(); UpdateTimer(); UpdateState();
    }
    protected override void OnTemplateChanging() { _timer.Stop(); _movingBlock = null; base.OnTemplateChanging(); }
    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((EffectReproSurface)d).UpdateAnimation();
    private static void OnPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var surface = (EffectReproSurface)d; surface.UpdateTimer(); surface.UpdateState(); }
    private void UpdateAnimation()
    {
        double progress = Math.Min(1, (ElapsedSeconds % 2.4) / 1.2);
        double ease = 1 - Math.Pow(1 - progress, 3);
        _glowTranslation.Y = _shadowTranslation.Y = 46 * (1 - ease);
        _glowScale.ScaleX = _glowScale.ScaleY = _shadowScale.ScaleX = _shadowScale.ScaleY = 0.88 + 0.12 * ease;
        // Keep the original Canvas.Left layout change: this case exercises dirty
        // regions beside a static glow, rather than animating a compositor transform.
        if (_movingBlock is not null) Canvas.SetLeft(_movingBlock, 240 + 240 * ((Math.Sin(ElapsedSeconds * 2.2) + 1) * 0.5));
    }
    private void UpdateTimer() { if (IsLoaded && IsPlaying) _timer.Start(); else _timer.Stop(); }
    private void UpdateState() => VisualStateManager.GoToState(this, IsPlaying ? "Playing" : "Paused", false);
}
