using System.Globalization;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Immutable presentation data for an element's diagnostic box diagram.</summary>
public sealed class DevToolsBoxModelMetrics
{
    internal static readonly DevToolsBoxModelMetrics Empty = new(default, default, default, double.NaN, double.NaN);
    static DevToolsBoxModelMetrics()
    {
        Access(nameof(MarginTopText), value => value.MarginTopText); Access(nameof(MarginRightText), value => value.MarginRightText);
        Access(nameof(MarginBottomText), value => value.MarginBottomText); Access(nameof(MarginLeftText), value => value.MarginLeftText);
        Access(nameof(BorderTopText), value => value.BorderTopText); Access(nameof(BorderRightText), value => value.BorderRightText);
        Access(nameof(BorderBottomText), value => value.BorderBottomText); Access(nameof(BorderLeftText), value => value.BorderLeftText);
        Access(nameof(PaddingTopText), value => value.PaddingTopText); Access(nameof(PaddingRightText), value => value.PaddingRightText);
        Access(nameof(PaddingBottomText), value => value.PaddingBottomText); Access(nameof(PaddingLeftText), value => value.PaddingLeftText);
        Access(nameof(MarginText), value => value.MarginText); Access(nameof(BorderText), value => value.BorderText); Access(nameof(PaddingText), value => value.PaddingText);
        Access(nameof(MarginSummary), value => value.MarginSummary); Access(nameof(BorderSummary), value => value.BorderSummary); Access(nameof(PaddingSummary), value => value.PaddingSummary);
        Access(nameof(SizeText), value => value.SizeText); Access(nameof(AutomationSummary), value => value.AutomationSummary);
    }
    private static void Access(string name, Func<DevToolsBoxModelMetrics, object?> getter) =>
        PropertyAccessorRegistry.Register(typeof(DevToolsBoxModelMetrics), name, value => getter((DevToolsBoxModelMetrics)value));

    public DevToolsBoxModelMetrics(Thickness margin, Thickness border, Thickness padding, double width, double height)
    {
        Margin = margin; Border = border; Padding = padding; Width = width; Height = height;
    }
    public Thickness Margin { get; }
    public Thickness Border { get; }
    public Thickness Padding { get; }
    public double Width { get; }
    public double Height { get; }
    public bool HasMeasuredSize => double.IsFinite(Width) && Width >= 0 && double.IsFinite(Height) && Height >= 0;
    public string MarginTopText => Number(Margin.Top);
    public string MarginRightText => Number(Margin.Right);
    public string MarginBottomText => Number(Margin.Bottom);
    public string MarginLeftText => Number(Margin.Left);
    public string BorderTopText => Number(Border.Top);
    public string BorderRightText => Number(Border.Right);
    public string BorderBottomText => Number(Border.Bottom);
    public string BorderLeftText => Number(Border.Left);
    public string PaddingTopText => Number(Padding.Top);
    public string PaddingRightText => Number(Padding.Right);
    public string PaddingBottomText => Number(Padding.Bottom);
    public string PaddingLeftText => Number(Padding.Left);
    public string MarginText => Compact(Margin);
    public string BorderText => Compact(Border);
    public string PaddingText => Compact(Padding);
    public string MarginSummary => Summary("Margin", Margin);
    public string BorderSummary => Summary("Border", Border);
    public string PaddingSummary => Summary("Padding", Padding);
    public string SizeText => HasMeasuredSize ? $"{Number(Width)} × {Number(Height)}" : "No layout";
    public string AutomationSummary => $"Box model; element size {SizeText}; {MarginSummary}; {BorderSummary}; {PaddingSummary}";
    private static string Summary(string name, Thickness value) => $"{name}: left {Number(value.Left)}, top {Number(value.Top)}, right {Number(value.Right)}, bottom {Number(value.Bottom)}";
    private static string Compact(Thickness value)
    {
        if (value.Left == value.Top && value.Left == value.Right && value.Left == value.Bottom) return Number(value.Left);
        if (value.Left == value.Right && value.Top == value.Bottom) return $"{Number(value.Left)}, {Number(value.Top)}";
        return $"{Number(value.Left)}, {Number(value.Top)}, {Number(value.Right)}, {Number(value.Bottom)}";
    }
    private static string Number(double value) => double.IsNaN(value) ? "—" : double.IsPositiveInfinity(value) ? "∞" : double.IsNegativeInfinity(value) ? "−∞" :
        value == 0 ? "0" : value.ToString(Math.Abs(value) >= 1e6 ? "G5" : "0.##", CultureInfo.InvariantCulture);
}

/// <summary>A template-owned, live box-model diagram with an accessible numeric summary.</summary>
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "SizeKnown", GroupName = "SizeStates")]
[TemplateVisualState(Name = "SizeUnknown", GroupName = "SizeStates")]
[TemplateVisualState(Name = "LegendVisible", GroupName = "LegendStates")]
[TemplateVisualState(Name = "LegendHidden", GroupName = "LegendStates")]
public class DevToolsBoxModel : Control
{
    private double _layoutWidth;
    private double _arrangedWidth = double.NaN;
    static DevToolsBoxModel() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsBoxModel), new PropertyMetadata(typeof(DevToolsBoxModel)));
    private static DependencyProperty Input(string name, Type type, object value) => DependencyProperty.Register(name, type, typeof(DevToolsBoxModel),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));
    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(DevToolsBoxModel), new PropertyMetadata(null));
    public static readonly DependencyProperty ElementMarginProperty = Input(nameof(ElementMargin), typeof(Thickness), default(Thickness));
    public static readonly DependencyProperty ElementBorderThicknessProperty = Input(nameof(ElementBorderThickness), typeof(Thickness), default(Thickness));
    public static readonly DependencyProperty ElementPaddingProperty = Input(nameof(ElementPadding), typeof(Thickness), default(Thickness));
    public static readonly DependencyProperty ElementWidthProperty = Input(nameof(ElementWidth), typeof(double), double.NaN);
    public static readonly DependencyProperty ElementHeightProperty = Input(nameof(ElementHeight), typeof(double), double.NaN);
    public static readonly DependencyProperty MarginAreaBrushProperty = BrushProperty(nameof(MarginAreaBrush));
    public static readonly DependencyProperty BorderAreaBrushProperty = BrushProperty(nameof(BorderAreaBrush));
    public static readonly DependencyProperty PaddingAreaBrushProperty = BrushProperty(nameof(PaddingAreaBrush));
    public static readonly DependencyProperty SizeAreaBrushProperty = BrushProperty(nameof(SizeAreaBrush));
    public static readonly DependencyProperty ShowLegendProperty = DependencyProperty.Register(nameof(ShowLegend), typeof(bool), typeof(DevToolsBoxModel), new PropertyMetadata(true, OnStateChanged));
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsBoxModel), new PropertyMetadata(280d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    private static readonly DependencyPropertyKey MetricsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Metrics), typeof(DevToolsBoxModelMetrics), typeof(DevToolsBoxModel), new PropertyMetadata(DevToolsBoxModelMetrics.Empty));
    public static readonly DependencyProperty MetricsProperty = MetricsPropertyKey.DependencyProperty;
    public Thickness ElementMargin { get => (Thickness)GetValue(ElementMarginProperty)!; set => SetValue(ElementMarginProperty, value); }
    public Thickness ElementBorderThickness { get => (Thickness)GetValue(ElementBorderThicknessProperty)!; set => SetValue(ElementBorderThicknessProperty, value); }
    public Thickness ElementPadding { get => (Thickness)GetValue(ElementPaddingProperty)!; set => SetValue(ElementPaddingProperty, value); }
    public double ElementWidth { get => (double)GetValue(ElementWidthProperty)!; set => SetValue(ElementWidthProperty, value); }
    public double ElementHeight { get => (double)GetValue(ElementHeightProperty)!; set => SetValue(ElementHeightProperty, value); }
    public Brush? MarginAreaBrush { get => (Brush?)GetValue(MarginAreaBrushProperty); set => SetValue(MarginAreaBrushProperty, value); }
    public Brush? BorderAreaBrush { get => (Brush?)GetValue(BorderAreaBrushProperty); set => SetValue(BorderAreaBrushProperty, value); }
    public Brush? PaddingAreaBrush { get => (Brush?)GetValue(PaddingAreaBrushProperty); set => SetValue(PaddingAreaBrushProperty, value); }
    public Brush? SizeAreaBrush { get => (Brush?)GetValue(SizeAreaBrushProperty); set => SetValue(SizeAreaBrushProperty, value); }
    public bool ShowLegend { get => (bool)GetValue(ShowLegendProperty)!; set => SetValue(ShowLegendProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public DevToolsBoxModelMetrics Metrics => (DevToolsBoxModelMetrics)GetValue(MetricsProperty)!;
    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsBoxModel)d;
        view.SetValue(MetricsPropertyKey, new DevToolsBoxModelMetrics(view.ElementMargin, view.ElementBorderThickness, view.ElementPadding, view.ElementWidth, view.ElementHeight));
        view.UpdateStates();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsBoxModel)d).UpdateStates();
    protected override Size MeasureOverride(Size availableSize)
    {
        _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth;
        if (double.IsFinite(_arrangedWidth) && _arrangedWidth > 0) _layoutWidth = Math.Min(_layoutWidth, _arrangedWidth);
        UpdateStates(); return base.MeasureOverride(availableSize);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _arrangedWidth = _layoutWidth = finalSize.Width;
        UpdateStates(); return base.ArrangeOverride(finalSize);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, _layoutWidth > 0 && _layoutWidth <= CompactWidth ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, Metrics.HasMeasuredSize ? "SizeKnown" : "SizeUnknown", false);
        VisualStateManager.GoToState(this, ShowLegend ? "LegendVisible" : "LegendHidden", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new BoxModelPeer(this);
    private sealed class BoxModelPeer(DevToolsBoxModel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsBoxModel);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.Metrics.AutomationSummary;
    }
}
