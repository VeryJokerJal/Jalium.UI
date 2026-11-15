using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

/// <summary>The original hover-damage stress sequence, with its row visuals supplied by a theme.</summary>
[TemplatePart(Name = "PART_ScrollViewer", Type = typeof(ScrollViewer))]
[TemplateVisualState(Name = "Running", GroupName = "RunStates")]
[TemplateVisualState(Name = "Settled", GroupName = "RunStates")]
public class HoverProbeSurface : ItemsControl
{
    public sealed record EventRow(int Index) { public string Label => $"EventRow {Index:D2}  LostMouseCapture"; }
    public static readonly DependencyProperty RowCountProperty = DependencyProperty.Register(nameof(RowCount), typeof(int), typeof(HoverProbeSurface), new PropertyMetadata(40, OnRowsChanged), value => value is int count && count > 0 && count <= 10_000);
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(HoverProbeSurface), new PropertyMetadata(22d), value => value is double height && double.IsFinite(height) && height > 0);
    public static readonly DependencyProperty TickLimitProperty = DependencyProperty.Register(nameof(TickLimit), typeof(int), typeof(HoverProbeSurface), new PropertyMetadata(320), value => value is int ticks && ticks > 0);
    public static readonly DependencyProperty IntervalProperty = DependencyProperty.Register(nameof(Interval), typeof(TimeSpan), typeof(HoverProbeSurface), new PropertyMetadata(TimeSpan.FromMilliseconds(33), OnIntervalChanged), value => value is TimeSpan interval && interval > TimeSpan.Zero);
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(HoverProbeSurface), new PropertyMetadata(true, OnRunningChanged));
    private static readonly DependencyPropertyKey PhasePropertyKey = DependencyProperty.RegisterReadOnly(nameof(Phase), typeof(string), typeof(HoverProbeSurface), new PropertyMetadata("RUNNING"));
    public static readonly DependencyProperty PhaseProperty = PhasePropertyKey.DependencyProperty;
    public int RowCount { get => (int)GetValue(RowCountProperty)!; set => SetValue(RowCountProperty, value); }
    public double RowHeight { get => (double)GetValue(RowHeightProperty)!; set => SetValue(RowHeightProperty, value); }
    public int TickLimit { get => (int)GetValue(TickLimitProperty)!; set => SetValue(TickLimitProperty, value); }
    public TimeSpan Interval { get => (TimeSpan)GetValue(IntervalProperty)!; set => SetValue(IntervalProperty, value); }
    public bool IsRunning { get => (bool)GetValue(IsRunningProperty)!; set => SetValue(IsRunningProperty, value); }
    public string Phase => (string)GetValue(PhaseProperty)!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private ScrollViewer? _scrollViewer;
    private int _tick;
    private double _offset, _direction = 3;

    public HoverProbeSurface()
    {
        HoverProbeTheme.Register(); SetRows();
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => UpdateTimer(); Unloaded += (_, _) => _timer.Stop();
        DefaultStyleKey = typeof(HoverProbeSurface);
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        UpdateTimer(); UpdateState();
    }
    protected override void OnTemplateChanging() { _timer.Stop(); _scrollViewer = null; base.OnTemplateChanging(); }
    public void Restart()
    {
        _tick = 0; _offset = 0; _direction = 3;
        SetCurrentValue(IsRunningProperty, true); SetValue(PhasePropertyKey, "RUNNING"); UpdateTimer(); UpdateState();
    }
    public void Stop() => SetCurrentValue(IsRunningProperty, false);
    private void Advance()
    {
        if (_scrollViewer is null) return;
        if (++_tick > TickLimit) { _scrollViewer.ScrollToVerticalOffset(0); Stop(); return; }
        _offset += _direction;
        var maximum = Math.Max(0, RowCount * RowHeight - 400);
        if (_offset > maximum) { _offset = maximum; _direction = -_direction; }
        if (_offset < 0) { _offset = 0; _direction = -_direction; }
        _scrollViewer.ScrollToVerticalOffset(_offset);
    }
    private void SetRows() => SetCurrentValue(ItemsSourceProperty, Enumerable.Range(0, RowCount).Select(index => new EventRow(index)).ToArray());
    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HoverProbeSurface)d).SetRows();
    private static void OnIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HoverProbeSurface)d)._timer.Interval = (TimeSpan)e.NewValue!;
    private static void OnRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (HoverProbeSurface)d;
        surface.SetValue(PhasePropertyKey, surface.IsRunning ? "RUNNING" : "SETTLE"); surface.UpdateTimer(); surface.UpdateState();
    }
    private void UpdateTimer() { if (IsLoaded && IsRunning && _scrollViewer is not null) _timer.Start(); else _timer.Stop(); }
    private void UpdateState() => VisualStateManager.GoToState(this, IsRunning ? "Running" : "Settled", false);
}

/// <summary>Reuses the exact translucent-green enter/leave treatment through visual states.</summary>
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
public class HoverProbeRow : ContentControl
{
    public HoverProbeRow()
    {
        HoverProbeTheme.Register(); DefaultStyleKey = typeof(HoverProbeRow);
        MouseEnter += (_, _) => UpdateState(); MouseLeave += (_, _) => UpdateState();
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    private void UpdateState() => VisualStateManager.GoToState(this, IsMouseOver ? "PointerOver" : "Normal", false);
}
