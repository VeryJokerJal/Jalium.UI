using System.Collections.Specialized;
using System.Globalization;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A bounded, template-driven frame timeline whose paths stay in device-independent coordinates.</summary>
[TemplatePart(Name = "PART_Plot", Type = typeof(FrameworkElement))]
[TemplateVisualState(Name = "FramesAvailable", GroupName = "DataStates")]
[TemplateVisualState(Name = "FramesEmpty", GroupName = "DataStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsFrameGraph : Control
{
    private FrameworkElement? _plot;
    static DevToolsFrameGraph() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsFrameGraph), new PropertyMetadata(typeof(DevToolsFrameGraph)));
    public static readonly DependencyProperty SamplesSourceProperty = DependencyProperty.Register(nameof(SamplesSource), typeof(IReadOnlyList<FrameHistory.Sample>), typeof(DevToolsFrameGraph), new PropertyMetadata(null, OnSourceChanged));
    public static readonly DependencyProperty DisplayCapacityProperty = DependencyProperty.Register(nameof(DisplayCapacity), typeof(int), typeof(DevToolsFrameGraph), new PropertyMetadata(FrameHistory.Capacity, OnDataChanged), value => value is int count && count > 0 && count <= 4096);
    public static readonly DependencyProperty FrameBudgetMsProperty = DependencyProperty.Register(nameof(FrameBudgetMs), typeof(double), typeof(DevToolsFrameGraph), new PropertyMetadata(16.0, OnDataChanged), value => value is double time && double.IsFinite(time) && time > 0);
    public static readonly DependencyProperty LayoutBrushProperty = BrushProperty(nameof(LayoutBrush));
    public static readonly DependencyProperty RenderBrushProperty = BrushProperty(nameof(RenderBrush));
    public static readonly DependencyProperty PresentBrushProperty = BrushProperty(nameof(PresentBrush));
    public static readonly DependencyProperty BudgetBrushProperty = BrushProperty(nameof(BudgetBrush));
    public static readonly DependencyProperty GridBrushProperty = BrushProperty(nameof(GridBrush));
    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(DevToolsFrameGraph), new PropertyMetadata(null));
    private static DependencyPropertyKey ReadOnly(string name, Type type, object? value) => DependencyProperty.RegisterReadOnly(name, type, typeof(DevToolsFrameGraph), new PropertyMetadata(value));
    private static readonly DependencyPropertyKey SampleCountPropertyKey = ReadOnly(nameof(SampleCount), typeof(int), 0);
    public static readonly DependencyProperty SampleCountProperty = SampleCountPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey MaximumMillisecondsPropertyKey = ReadOnly(nameof(MaximumMilliseconds), typeof(double), 16.0);
    public static readonly DependencyProperty MaximumMillisecondsProperty = MaximumMillisecondsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ScaleTextPropertyKey = ReadOnly(nameof(ScaleText), typeof(string), "16 ms");
    public static readonly DependencyProperty ScaleTextProperty = ScaleTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey LayoutGeometryPropertyKey = ReadOnly(nameof(LayoutGeometry), typeof(Geometry), null);
    public static readonly DependencyProperty LayoutGeometryProperty = LayoutGeometryPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey RenderGeometryPropertyKey = ReadOnly(nameof(RenderGeometry), typeof(Geometry), null);
    public static readonly DependencyProperty RenderGeometryProperty = RenderGeometryPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PresentGeometryPropertyKey = ReadOnly(nameof(PresentGeometry), typeof(Geometry), null);
    public static readonly DependencyProperty PresentGeometryProperty = PresentGeometryPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey GridGeometryPropertyKey = ReadOnly(nameof(GridGeometry), typeof(Geometry), null);
    public static readonly DependencyProperty GridGeometryProperty = GridGeometryPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey BudgetGeometryPropertyKey = ReadOnly(nameof(BudgetGeometry), typeof(Geometry), null);
    public static readonly DependencyProperty BudgetGeometryProperty = BudgetGeometryPropertyKey.DependencyProperty;

    public IReadOnlyList<FrameHistory.Sample>? SamplesSource { get => (IReadOnlyList<FrameHistory.Sample>?)GetValue(SamplesSourceProperty); set => SetValue(SamplesSourceProperty, value); }
    /// <summary>The fixed number of timeline slots; samples are anchored at the right edge.</summary>
    public int DisplayCapacity { get => (int)GetValue(DisplayCapacityProperty)!; set => SetValue(DisplayCapacityProperty, value); }
    public double FrameBudgetMs { get => (double)GetValue(FrameBudgetMsProperty)!; set => SetValue(FrameBudgetMsProperty, value); }
    public Brush? LayoutBrush { get => (Brush?)GetValue(LayoutBrushProperty); set => SetValue(LayoutBrushProperty, value); }
    public Brush? RenderBrush { get => (Brush?)GetValue(RenderBrushProperty); set => SetValue(RenderBrushProperty, value); }
    public Brush? PresentBrush { get => (Brush?)GetValue(PresentBrushProperty); set => SetValue(PresentBrushProperty, value); }
    public Brush? BudgetBrush { get => (Brush?)GetValue(BudgetBrushProperty); set => SetValue(BudgetBrushProperty, value); }
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public int SampleCount => (int)GetValue(SampleCountProperty)!;
    public double MaximumMilliseconds => (double)GetValue(MaximumMillisecondsProperty)!;
    public string ScaleText => (string)GetValue(ScaleTextProperty)!;
    public Geometry? LayoutGeometry => (Geometry?)GetValue(LayoutGeometryProperty);
    public Geometry? RenderGeometry => (Geometry?)GetValue(RenderGeometryProperty);
    public Geometry? PresentGeometry => (Geometry?)GetValue(PresentGeometryProperty);
    public Geometry? GridGeometry => (Geometry?)GetValue(GridGeometryProperty);
    public Geometry? BudgetGeometry => (Geometry?)GetValue(BudgetGeometryProperty);

    public override void OnApplyTemplate()
    {
        DetachPlot(); base.OnApplyTemplate(); _plot = GetTemplateChild("PART_Plot") as FrameworkElement;
        if (_plot != null) _plot.SizeChanged += OnPlotSizeChanged;
        Rebuild();
    }
    protected override void OnTemplateChanging() { DetachPlot(); base.OnTemplateChanging(); }
    private void DetachPlot() { if (_plot != null) _plot.SizeChanged -= OnPlotSizeChanged; _plot = null; }
    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (DevToolsFrameGraph)d;
        if (e.OldValue is INotifyCollectionChanged old) CollectionChangedEventManager.RemoveHandler(old, graph.OnCollectionChanged);
        if (e.NewValue is INotifyCollectionChanged current) CollectionChangedEventManager.AddHandler(current, graph.OnCollectionChanged);
        graph.Rebuild();
    }
    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsFrameGraph)d).Rebuild();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == IsEnabledProperty) UpdateStates(); }
    private static double Safe(double value) => double.IsFinite(value) && value > 0 ? value : 0;
    private static double Add(double left, double right) => left > double.MaxValue - right ? double.MaxValue : left + right;
    private void Rebuild()
    {
        var source = SamplesSource; int count = Math.Min(source?.Count ?? 0, DisplayCapacity), start = (source?.Count ?? 0) - count;
        SetValue(SampleCountPropertyKey, count); UpdateStates();
        double maximum = FrameBudgetMs;
        for (int i = 0; i < count; i++)
        {
            var sample = source![start + i];
            maximum = Math.Max(maximum, Math.Max(Safe(sample.TotalMs), Add(Add(Safe(sample.LayoutMs), Safe(sample.RenderMs)), Safe(sample.PresentMs))));
        }
        SetValue(MaximumMillisecondsPropertyKey, maximum); SetValue(ScaleTextPropertyKey, maximum.ToString("0.#", CultureInfo.InvariantCulture) + " ms");
        double width = _plot?.ActualWidth ?? 0, height = _plot?.ActualHeight ?? 0;
        if (!(width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height)))
        {
            SetValue(LayoutGeometryPropertyKey, null); SetValue(RenderGeometryPropertyKey, null); SetValue(PresentGeometryPropertyKey, null);
            SetValue(GridGeometryPropertyKey, null); SetValue(BudgetGeometryPropertyKey, null); return;
        }
        var layout = new StreamGeometry(); var render = new StreamGeometry(); var present = new StreamGeometry();
        using (var l = layout.Open()) using (var r = render.Open()) using (var p = present.Open())
        {
            double slotWidth = width / DisplayCapacity;
            for (int i = 0; i < count; i++)
            {
                var sample = source![start + i]; double x = (DisplayCapacity - count + i) * slotWidth, bottom = height;
                double lh = Math.Min(bottom, height * (Safe(sample.LayoutMs) / maximum)); Rectangle(l, x, bottom - lh, slotWidth, lh); bottom -= lh;
                double rh = Math.Min(bottom, height * (Safe(sample.RenderMs) / maximum)); Rectangle(r, x, bottom - rh, slotWidth, rh); bottom -= rh;
                double ph = Math.Min(bottom, height * (Safe(sample.PresentMs) / maximum)); Rectangle(p, x, bottom - ph, slotWidth, ph);
            }
        }
        var grid = new StreamGeometry(); using (var lines = grid.Open()) for (int i = 1; i < 4; i++) Line(lines, 0, height * i / 4, width);
        var budget = new StreamGeometry(); if (count > 0) using (var line = budget.Open()) Line(line, 0, height * (1 - FrameBudgetMs / maximum), width);
        SetValue(LayoutGeometryPropertyKey, layout); SetValue(RenderGeometryPropertyKey, render); SetValue(PresentGeometryPropertyKey, present);
        SetValue(GridGeometryPropertyKey, grid); SetValue(BudgetGeometryPropertyKey, budget);
    }
    private static void Line(StreamGeometryContext context, double x, double y, double width) { context.BeginFigure(new Point(x, y), false, false); context.LineTo(new Point(x + width, y), true, false); }
    private static void Rectangle(StreamGeometryContext context, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        context.BeginFigure(new Point(x, y), true, true); context.LineTo(new Point(x + width, y), true, false);
        context.LineTo(new Point(x + width, y + height), true, false); context.LineTo(new Point(x, y + height), true, false);
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, SampleCount > 0 ? "FramesAvailable" : "FramesEmpty", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FramePeer(this);
    private sealed class FramePeer(DevToolsFrameGraph owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsFrameGraph);
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Frame timings";
        protected override string GetHelpTextCore() => $"{owner.SampleCount} samples; {owner.FrameBudgetMs:0.#} ms budget; {owner.ScaleText} scale";
    }
}
