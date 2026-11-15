namespace Jalium.UI.Controls;

/// <summary>Templated render diagnostics. Samples survive template and theme replacement.</summary>
[TemplateVisualState(Name = "Healthy", GroupName = "FrameRateStates")]
[TemplateVisualState(Name = "Fair", GroupName = "FrameRateStates")]
[TemplateVisualState(Name = "Critical", GroupName = "FrameRateStates")]
[TemplateVisualState(Name = "WorstNormal", GroupName = "WorstFrameStates")]
[TemplateVisualState(Name = "WorstSlow", GroupName = "WorstFrameStates")]
[TemplateVisualState(Name = "TotalNormal", GroupName = "FrameTimeStates")]
[TemplateVisualState(Name = "TotalSlow", GroupName = "FrameTimeStates")]
[TemplateVisualState(Name = "FramesNormal", GroupName = "PipelineStates")]
[TemplateVisualState(Name = "FramesFailed", GroupName = "PipelineStates")]
internal sealed class DebugHudOverlay : Control
{
    private const double BarFixedWidth = 380;
    private double _fps, _worstMs, _totalMs;
    private int _beginFails;

    static DebugHudOverlay() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DebugHudOverlay), new PropertyMetadata(typeof(DebugHudOverlay)));

    private static readonly DependencyPropertyKey BarWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(BarWidth), typeof(double), typeof(DebugHudOverlay), new PropertyMetadata(380d));
    public static readonly DependencyProperty BarWidthProperty = BarWidthPropertyKey.DependencyProperty;
    public double BarWidth { get => (double)GetValue(BarWidthProperty)!; private set => SetValue(BarWidthPropertyKey, value); }

    private static readonly DependencyPropertyKey FpsTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FpsText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata("0"));
    public static readonly DependencyProperty FpsTextProperty = FpsTextPropertyKey.DependencyProperty;
    public string FpsText { get => (string)GetValue(FpsTextProperty)!; private set => SetValue(FpsTextPropertyKey, value); }

    private static readonly DependencyPropertyKey RenderPathTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(RenderPathText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata("Full [D3D12 · Impeller]"));
    public static readonly DependencyProperty RenderPathTextProperty = RenderPathTextPropertyKey.DependencyProperty;
    public string RenderPathText { get => (string)GetValue(RenderPathTextProperty)!; private set => SetValue(RenderPathTextPropertyKey, value); }

    private static readonly DependencyPropertyKey WorstFrameTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(WorstFrameText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata("worst 0.0ms"));
    public static readonly DependencyProperty WorstFrameTextProperty = WorstFrameTextPropertyKey.DependencyProperty;
    public string WorstFrameText { get => (string)GetValue(WorstFrameTextProperty)!; private set => SetValue(WorstFrameTextPropertyKey, value); }

    private static readonly DependencyPropertyKey TimingTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TimingText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata("Layout 0.0ms   Render 0.0ms   Present 0.0ms   Total 0.0ms"));
    public static readonly DependencyProperty TimingTextProperty = TimingTextPropertyKey.DependencyProperty;
    public string TimingText { get => (string)GetValue(TimingTextProperty)!; private set => SetValue(TimingTextPropertyKey, value); }

    private static readonly DependencyPropertyKey FramesTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FramesText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata(""));
    public static readonly DependencyProperty FramesTextProperty = FramesTextPropertyKey.DependencyProperty;
    public string FramesText { get => (string)GetValue(FramesTextProperty)!; private set => SetValue(FramesTextPropertyKey, value); }

    private static readonly DependencyPropertyKey DirtyTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DirtyText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata(""));
    public static readonly DependencyProperty DirtyTextProperty = DirtyTextPropertyKey.DependencyProperty;
    public string DirtyText { get => (string)GetValue(DirtyTextProperty)!; private set => SetValue(DirtyTextPropertyKey, value); }

    private static readonly DependencyPropertyKey WindowTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(WindowText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata(""));
    public static readonly DependencyProperty WindowTextProperty = WindowTextPropertyKey.DependencyProperty;
    public string WindowText { get => (string)GetValue(WindowTextProperty)!; private set => SetValue(WindowTextPropertyKey, value); }

    private static readonly DependencyPropertyKey GcTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(GcText), typeof(string), typeof(DebugHudOverlay), new PropertyMetadata(""));
    public static readonly DependencyProperty GcTextProperty = GcTextPropertyKey.DependencyProperty;
    public string GcText { get => (string)GetValue(GcTextProperty)!; private set => SetValue(GcTextPropertyKey, value); }

    private static readonly DependencyPropertyKey LayoutBarWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LayoutBarWidth), typeof(double), typeof(DebugHudOverlay), new PropertyMetadata(1d));
    public static readonly DependencyProperty LayoutBarWidthProperty = LayoutBarWidthPropertyKey.DependencyProperty;
    public double LayoutBarWidth { get => (double)GetValue(LayoutBarWidthProperty)!; private set => SetValue(LayoutBarWidthPropertyKey, value); }

    private static readonly DependencyPropertyKey RenderBarWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(RenderBarWidth), typeof(double), typeof(DebugHudOverlay), new PropertyMetadata(1d));
    public static readonly DependencyProperty RenderBarWidthProperty = RenderBarWidthPropertyKey.DependencyProperty;
    public double RenderBarWidth { get => (double)GetValue(RenderBarWidthProperty)!; private set => SetValue(RenderBarWidthPropertyKey, value); }

    private static readonly DependencyPropertyKey PresentBarWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PresentBarWidth), typeof(double), typeof(DebugHudOverlay), new PropertyMetadata(1d));
    public static readonly DependencyProperty PresentBarWidthProperty = PresentBarWidthPropertyKey.DependencyProperty;
    public double PresentBarWidth { get => (double)GetValue(PresentBarWidthProperty)!; private set => SetValue(PresentBarWidthPropertyKey, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisualStates();
    }

    private void UpdateVisualStates()
    {
        VisualStateManager.GoToState(this, _fps >= 55 ? "Healthy" : _fps >= 30 ? "Fair" : "Critical", false);
        VisualStateManager.GoToState(this, _worstMs > 16 ? "WorstSlow" : "WorstNormal", false);
        VisualStateManager.GoToState(this, _totalMs > 16 ? "TotalSlow" : "TotalNormal", false);
        VisualStateManager.GoToState(this, _beginFails > 0 ? "FramesFailed" : "FramesNormal", false);
    }

    public void Update(
        double fps, double worstMs,
        double layoutMs, double renderMs, double presentMs, double totalMs,
        string renderPath, string backend, string engine,
        int fullFrames, int partialFrames, int skippedFrames, int beginFails,
        int dirtyElements, string dirtyRegion,
        int windowW, int windowH, float dpiScale,
        long gcBytes, int gen0, int gen1, int gen2,
        int promotedFrames = 0, int capacityExceeded = 0,
        int dirtyRectCount = 0, double dirtyCoverageRatio = 0)
    {
        _fps = fps;
        _worstMs = worstMs;
        _totalMs = totalMs;
        _beginFails = beginFails;
        // FPS header
        FpsText = $"{fps:F0}";
        RenderPathText = $"{renderPath}  [{backend} · {engine}]";
        WorstFrameText = $"worst {worstMs:F1}ms";

        // Timing
        TimingText = $"Layout {layoutMs:F1}ms   Render {renderMs:F1}ms   Present {presentMs:F1}ms   Total {totalMs:F1}ms";

        // The window width is logical, matching the template's layout units.
        BarWidth = Math.Min(BarFixedWidth, Math.Max(0, windowW - Padding.TotalWidth - BorderThickness.TotalWidth - Margin.TotalWidth));
        // Bar widths stay proportional and sum to the available width exactly.
        double barTotal = double.IsFinite(totalMs) ? Math.Max(totalMs, 0.01) : 0.01;
        double lW = double.IsFinite(layoutMs) ? Math.Floor((layoutMs / barTotal) * BarWidth) : 0;
        double rW = double.IsFinite(renderMs) ? Math.Floor((renderMs / barTotal) * BarWidth) : 0;
        lW = Math.Clamp(lW, 0, BarWidth);
        rW = Math.Clamp(rW, 0, BarWidth - lW);
        double pW = BarWidth - lW - rW;
        LayoutBarWidth = lW;
        RenderBarWidth = rW;
        PresentBarWidth = pW;

        // Pipeline — now split into two lines so promote / capacity / partial hit
        // are visible at a glance. The original "Frames" line keeps its legacy
        // Full / Partial / Skip / Fail layout; a second line surfaces the new
        // partial-redraw diagnostics.
        int partialAttempts = partialFrames + promotedFrames;
        double partialHitRatio = partialAttempts > 0
            ? (double)partialFrames / partialAttempts
            : 0;
        FramesText = $"Frames  Full={fullFrames}  Partial={partialFrames}  Promoted={promotedFrames}  Skip={skippedFrames}  Fail={beginFails}";
        DirtyText = $"Dirty   {dirtyElements}el  rects={dirtyRectCount}  cov={dirtyCoverageRatio * 100:F1}%  hit={partialHitRatio * 100:F0}%  cap+={capacityExceeded}   {dirtyRegion}";

        // System
        WindowText = $"Window  {windowW}x{windowH}   DPI {dpiScale * 96:F0} ({dpiScale:F2}x)";
        GcText = $"GC      {gcBytes / (1024.0 * 1024.0):F1} MB   Gen {gen0}/{gen1}/{gen2}";
        UpdateVisualStates();
    }
}
