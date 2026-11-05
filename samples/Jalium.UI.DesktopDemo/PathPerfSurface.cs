using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

/// <summary>Real path/cache workload with data-driven glyphs and a template-owned layout.</summary>
[TemplateVisualState(Name = "Playing", GroupName = "PlaybackStates")]
[TemplateVisualState(Name = "Paused", GroupName = "PlaybackStates")]
public class PathPerfSurface : ItemsControl
{
    public sealed record Glyph(Geometry PathData, Brush Fill);
    private static readonly string[] s_iconData =
    {
        // play_circle (single figure 圆 + 内嵌三角)
        "M12 2 C6.48 2 2 6.48 2 12 C2 17.52 6.48 22 12 22 C17.52 22 22 17.52 22 12 C22 6.48 17.52 2 12 2 Z M10 16.5 V7.5 L16 12 Z",
        // home (mixed line + line)
        "M10 20 V14 H14 V20 H19 V12 H22 L12 3 L2 12 H5 V20 H10 Z",
        // settings (多 figure：外齿轮 + 内圆)
        "M19.43 12.98 C19.47 12.66 19.5 12.34 19.5 12 C19.5 11.66 19.47 11.34 19.43 11.02 L21.54 9.37 C21.73 9.22 21.78 8.95 21.66 8.73 L19.66 5.27 C19.54 5.05 19.27 4.97 19.05 5.05 L16.56 6.05 C16.04 5.66 15.48 5.32 14.87 5.07 L14.49 2.42 C14.46 2.18 14.25 2 14 2 H10 C9.75 2 9.54 2.18 9.51 2.42 L9.13 5.07 C8.52 5.32 7.96 5.66 7.44 6.05 L4.95 5.05 C4.73 4.97 4.46 5.05 4.34 5.27 L2.34 8.73 C2.22 8.95 2.27 9.22 2.46 9.37 L4.57 11.02 C4.53 11.34 4.5 11.67 4.5 12 C4.5 12.33 4.53 12.66 4.57 12.98 L2.46 14.63 C2.27 14.78 2.22 15.05 2.34 15.27 L4.34 18.73 C4.46 18.95 4.73 19.03 4.95 18.95 L7.44 17.95 C7.96 18.34 8.52 18.68 9.13 18.93 L9.51 21.58 C9.54 21.82 9.75 22 10 22 H14 C14.25 22 14.46 21.82 14.49 21.58 L14.87 18.93 C15.48 18.68 16.04 18.34 16.56 17.95 L19.05 18.95 C19.27 19.03 19.54 18.95 19.66 18.73 L21.66 15.27 C21.78 15.05 21.73 14.78 21.54 14.63 L19.43 12.98 Z M12 15.5 C10.07 15.5 8.5 13.93 8.5 12 C8.5 10.07 10.07 8.5 12 8.5 C13.93 8.5 15.5 10.07 15.5 12 C15.5 13.93 13.93 15.5 12 15.5 Z",
        // account_circle (多 figure：外圆 + 内人形复合 path)
        "M12 2 C6.48 2 2 6.48 2 12 C2 17.52 6.48 22 12 22 C17.52 22 22 17.52 22 12 C22 6.48 17.52 2 12 2 Z M12 5 C13.66 5 15 6.34 15 8 C15 9.66 13.66 11 12 11 C10.34 11 9 9.66 9 8 C9 6.34 10.34 5 12 5 Z M12 19.2 C9.5 19.2 7.29 17.92 6 15.98 C6.03 13.99 10 12.9 12 12.9 C13.99 12.9 17.97 13.99 18 15.98 C16.71 17.92 14.5 19.2 12 19.2 Z",
        // menu (3 short lines — 简单 path 作 baseline)
        "M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 17 H21 V19 H3 Z",
    };

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(PathPerfSurface), new PropertyMetadata(true, OnPlayingChanged));
    public static readonly DependencyProperty ElapsedSecondsProperty = DependencyProperty.Register(nameof(ElapsedSeconds), typeof(double), typeof(PathPerfSurface), new PropertyMetadata(0d, OnScaleChanged), value => value is double time && double.IsFinite(time) && time >= 0);
    public static readonly DependencyProperty MinimumScaleProperty = DependencyProperty.Register(nameof(MinimumScale), typeof(double), typeof(PathPerfSurface), new PropertyMetadata(0.5d, OnScaleChanged), value => value is double scale && double.IsFinite(scale) && scale > 0);
    public static readonly DependencyProperty MaximumScaleProperty = DependencyProperty.Register(nameof(MaximumScale), typeof(double), typeof(PathPerfSurface), new PropertyMetadata(4d, OnScaleChanged), value => value is double scale && double.IsFinite(scale) && scale > 0);
    public static readonly DependencyProperty CycleSecondsProperty = DependencyProperty.Register(nameof(CycleSeconds), typeof(double), typeof(PathPerfSurface), new PropertyMetadata(6d, OnScaleChanged), value => value is double period && double.IsFinite(period) && period > 0);
    private static readonly DependencyPropertyKey ScaleTransformPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ScaleTransform), typeof(ScaleTransform), typeof(PathPerfSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty ScaleTransformProperty = ScaleTransformPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CurrentScalePropertyKey = DependencyProperty.RegisterReadOnly(nameof(CurrentScale), typeof(double), typeof(PathPerfSurface), new PropertyMetadata(1d));
    public static readonly DependencyProperty CurrentScaleProperty = CurrentScalePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(PathPerfSurface), new PropertyMetadata("Scale 0.5× → 4.0× 来回缓动。打开 DevTools (F3) Perf tab 可以看 path 指标。"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty)!; set => SetValue(IsPlayingProperty, value); }
    public double ElapsedSeconds { get => (double)GetValue(ElapsedSecondsProperty)!; set => SetValue(ElapsedSecondsProperty, value); }
    public double MinimumScale { get => (double)GetValue(MinimumScaleProperty)!; set => SetValue(MinimumScaleProperty, value); }
    public double MaximumScale { get => (double)GetValue(MaximumScaleProperty)!; set => SetValue(MaximumScaleProperty, value); }
    public double CycleSeconds { get => (double)GetValue(CycleSecondsProperty)!; set => SetValue(CycleSecondsProperty, value); }
    public ScaleTransform ScaleTransform => (ScaleTransform)GetValue(ScaleTransformProperty)!;
    public double CurrentScale => (double)GetValue(CurrentScaleProperty)!;
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private double _playbackOffset;
    public PathPerfSurface()
    {
        PathPerfTheme.Register(); SetValue(ScaleTransformPropertyKey, new ScaleTransform());
        SetCurrentValue(ItemsSourceProperty, Enumerable.Range(0, 60).Select(index => new Glyph(Geometry.Parse(s_iconData[index % s_iconData.Length]), new SolidColorBrush(Color.FromRgb((byte)(120 + (index * 7) % 100), (byte)(140 + (index * 11) % 80), (byte)(180 + (index * 13) % 60))))).ToArray());
        _timer.Tick += (_, _) => { SetCurrentValue(ElapsedSecondsProperty, _playbackOffset + _clock.Elapsed.TotalSeconds); UpdateStatistics(); };
        Loaded += (_, _) => { RenderDiagnostics.ApiStatsEnabled = true; UpdateTimer(); };
        Unloaded += (_, _) => { _timer.Stop(); _clock.Stop(); };
        DefaultStyleKey = typeof(PathPerfSurface);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateScale(); UpdateTimer(); UpdateState(); }
    protected override void OnTemplateChanging() { _timer.Stop(); _clock.Stop(); base.OnTemplateChanging(); }
    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PathPerfSurface)d).UpdateScale();
    private static void OnPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var surface = (PathPerfSurface)d; surface.UpdateTimer(); surface.UpdateState(); }
    private void UpdateScale()
    {
        var value = MinimumScale + (Math.Max(MinimumScale, MaximumScale) - MinimumScale) * ((Math.Sin(ElapsedSeconds * (2 * Math.PI) / CycleSeconds) + 1) * 0.5);
        SetValue(CurrentScalePropertyKey, value);
        if (GetValue(ScaleTransformProperty) is ScaleTransform transform) transform.ScaleX = transform.ScaleY = value;
    }
    private void UpdateStatistics()
    {
        if (RenderDiagnostics.LatestPathCacheStats is not { } stats) return;
        long total = stats.FillHits + stats.FillMisses, geometryTotal = stats.GeometryHits + stats.GeometryMisses;
        double fill = total == 0 ? 0 : stats.FillHits * 100d / total, geometry = geometryTotal == 0 ? 0 : stats.GeometryHits * 100d / geometryTotal;
        SetValue(StatusTextPropertyKey, $"Scale {CurrentScale:F2}×   Fill {fill:F0}%   Geom {geometry:F0}%   Flatten {stats.FlattenNs / 1_000_000d:F1}ms   verts {stats.FlattenOutputVerts}");
    }
    private void UpdateTimer()
    {
        if (IsLoaded && IsPlaying) { if (!_timer.IsEnabled) { _playbackOffset = ElapsedSeconds; _clock.Restart(); _timer.Start(); } }
        else { _timer.Stop(); _clock.Stop(); }
    }
    private void UpdateState() => VisualStateManager.GoToState(this, IsPlaying ? "Playing" : "Paused", false);
}
