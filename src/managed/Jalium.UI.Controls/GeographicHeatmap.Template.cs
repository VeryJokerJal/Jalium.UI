using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_HeatmapView", Type = typeof(GeographicHeatmapView))]
[TemplatePart(Name = "PART_Legend", Type = typeof(Border))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "LegendVisible", GroupName = "LegendStates")]
[TemplateVisualState(Name = "LegendHidden", GroupName = "LegendStates")]
public partial class GeographicHeatmap
{
    private GeographicHeatmapView? _heatmapView;
    private bool _refreshingHeatmapPresentation;
    private (Color Color, double Offset)[]? _legendStops;
    private (Size, Thickness, CornerRadius)? _heatmapClipStamp;

    static GeographicHeatmap() => DefaultStyleKeyProperty.OverrideMetadata(typeof(GeographicHeatmap), new PropertyMetadata(typeof(GeographicHeatmap)));

    public static readonly DependencyProperty LegendTitleProperty = DependencyProperty.Register(nameof(LegendTitle), typeof(string), typeof(GeographicHeatmap), new PropertyMetadata("Intensity"));
    public string LegendTitle { get => (string)GetValue(LegendTitleProperty)!; set => SetValue(LegendTitleProperty, value); }
    public static readonly DependencyProperty HighLabelProperty = DependencyProperty.Register(nameof(HighLabel), typeof(string), typeof(GeographicHeatmap), new PropertyMetadata("High"));
    public string HighLabel { get => (string)GetValue(HighLabelProperty)!; set => SetValue(HighLabelProperty, value); }
    public static readonly DependencyProperty MidLabelProperty = DependencyProperty.Register(nameof(MidLabel), typeof(string), typeof(GeographicHeatmap), new PropertyMetadata("Mid"));
    public string MidLabel { get => (string)GetValue(MidLabelProperty)!; set => SetValue(MidLabelProperty, value); }
    public static readonly DependencyProperty LowLabelProperty = DependencyProperty.Register(nameof(LowLabel), typeof(string), typeof(GeographicHeatmap), new PropertyMetadata("Low"));
    public string LowLabel { get => (string)GetValue(LowLabelProperty)!; set => SetValue(LowLabelProperty, value); }

    private static readonly DependencyPropertyKey LegendBoundsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(LegendBounds), typeof(Rect), typeof(GeographicHeatmap), new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty LegendBoundsProperty = LegendBoundsPropertyKey.DependencyProperty;
    public Rect LegendBounds => (Rect)GetValue(LegendBoundsProperty)!;
    private static readonly DependencyPropertyKey LegendVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(nameof(LegendVisibility), typeof(Visibility), typeof(GeographicHeatmap), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty LegendVisibilityProperty = LegendVisibilityPropertyKey.DependencyProperty;
    public Visibility LegendVisibility => (Visibility)GetValue(LegendVisibilityProperty)!;
    private static readonly DependencyPropertyKey LegendGradientBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(LegendGradientBrush), typeof(LinearGradientBrush), typeof(GeographicHeatmap), new PropertyMetadata(null));
    public static readonly DependencyProperty LegendGradientBrushProperty = LegendGradientBrushPropertyKey.DependencyProperty;
    public LinearGradientBrush? LegendGradientBrush => (LinearGradientBrush?)GetValue(LegendGradientBrushProperty);
    private static readonly DependencyPropertyKey HeatmapContentClipPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HeatmapContentClip), typeof(Geometry), typeof(GeographicHeatmap), new PropertyMetadata(null));
    public static readonly DependencyProperty HeatmapContentClipProperty = HeatmapContentClipPropertyKey.DependencyProperty;
    public Geometry? HeatmapContentClip => (Geometry?)GetValue(HeatmapContentClipProperty);

    public override void OnApplyTemplate()
    {
        ReleaseHeatmapView();
        base.OnApplyTemplate();
        _heatmapView = GetTemplateChild("PART_HeatmapView") as GeographicHeatmapView;
        _heatmapView?.SetCurrentValue(GeographicHeatmapView.OwnerProperty, this);
        RefreshHeatmapPresentation();
    }

    protected override void OnTemplateChanging()
    {
        ReleaseHeatmapView();
        base.OnTemplateChanging();
    }

    private void ReleaseHeatmapView()
    {
        if (_heatmapView is { } view)
        {
            view.ClearBinding(GeographicHeatmapView.OwnerProperty);
            view.SetCurrentValue(GeographicHeatmapView.OwnerProperty, null);
        }
        _heatmapView = null;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_refreshingHeatmapPresentation && TemplateRootInternal is not null) InvalidateHeatmapView();
    }

    protected internal override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        RefreshHeatmapPresentation();
    }

    private void InvalidateHeatmapView()
    {
        RefreshHeatmapPresentation();
        _heatmapView?.InvalidateVisual();
        InvalidateVisual();
    }

    private void RefreshHeatmapPresentation()
    {
        if (_refreshingHeatmapPresentation) return;
        _refreshingHeatmapPresentation = true;
        try
        {
            var stops = Gradient.Stops;
            var changed = _legendStops is null || _legendStops.Length != stops.Count;
            if (!changed)
                for (var i = 0; i < stops.Count; i++)
                    if (_legendStops![i] != (stops[i].Color, stops[i].Offset)) { changed = true; break; }
            if (changed)
            {
                _legendStops = stops.Select(s => (s.Color, s.Offset)).ToArray();
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
                foreach (var stop in stops) brush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
                brush.Freeze();
                SetValue(LegendGradientBrushPropertyKey, brush);
            }

            var size = RenderSize;
            var width = Math.Min(84, Math.Max(0, size.Width - 20));
            var height = Math.Min(160, Math.Max(0, size.Height - 20));
            var left = LegendPosition is HeatmapLegendPosition.TopLeft or HeatmapLegendPosition.BottomLeft;
            var top = LegendPosition is HeatmapLegendPosition.TopLeft or HeatmapLegendPosition.TopRight;
            SetValue(LegendBoundsPropertyKey, new Rect(left ? Math.Min(10, size.Width) : Math.Max(0, size.Width - width - 10), top ? Math.Min(10, size.Height) : Math.Max(0, size.Height - height - 10), width, height));
            SetValue(LegendVisibilityPropertyKey, ShowLegend ? Visibility.Visible : Visibility.Collapsed);
            if (size.Width > 0 && size.Height > 0 && _heatmapClipStamp != (size, BorderThickness, CornerRadius))
            {
                _heatmapClipStamp = (size, BorderThickness, CornerRadius);
                var rect = ControlRenderGeometry.GetContentRect(new Rect(size), BorderThickness);
                var radii = CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(size).Inset(BorderThickness).Normalize(rect.Size);
                var clip = new PathGeometry();
                clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect, radii));
                clip.Freeze();
                SetValue(HeatmapContentClipPropertyKey, clip);
            }
            if (TemplateRootInternal is not null)
            {
                VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
                VisualStateManager.GoToState(this, ShowLegend ? "LegendVisible" : "LegendHidden", false);
            }
        }
        finally { _refreshingHeatmapPresentation = false; }
    }
}

/// <summary>Renders the geographic intensity image; the owner template supplies chrome and legend.</summary>
public sealed class GeographicHeatmapView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner), typeof(GeographicHeatmap), typeof(GeographicHeatmapView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GeographicHeatmap? Owner { get => (GeographicHeatmap?)GetValue(OwnerProperty); set => SetValue(OwnerProperty, value); }
    public GeographicHeatmapView() => Focusable = false;
    protected override bool ParticipatesInRenderCache => false;
    protected override Size MeasureOverride(Size size) => new(double.IsFinite(size.Width) ? size.Width : 0, double.IsFinite(size.Height) ? size.Height : 0);
    protected override void OnRender(DrawingContext dc) => Owner?.RenderHeatmapContent(dc);
}
