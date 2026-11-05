using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A retemplatable ruler result; coordinate acquisition belongs to its consumer.</summary>
[TemplateVisualState(Name = "ResultAvailable", GroupName = "ResultStates")]
[TemplateVisualState(Name = "ResultEmpty", GroupName = "ResultStates")]
[TemplateVisualState(Name = "Snapped", GroupName = "SnapStates")]
[TemplateVisualState(Name = "Free", GroupName = "SnapStates")]
public class DevToolsMeasurementView : Control
{
    static DevToolsMeasurementView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsMeasurementView), new PropertyMetadata(typeof(DevToolsMeasurementView)));
    public static readonly DependencyProperty MeasurementProperty = DependencyProperty.Register(nameof(Measurement), typeof(DevToolsRulerMeasurement), typeof(DevToolsMeasurementView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnResultChanged));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(DevToolsMeasurementView), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey HasMeasurementPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasMeasurement), typeof(bool), typeof(DevToolsMeasurementView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasMeasurementProperty = HasMeasurementPropertyKey.DependencyProperty;
    public DevToolsRulerMeasurement? Measurement { get => (DevToolsRulerMeasurement?)GetValue(MeasurementProperty); set => SetValue(MeasurementProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public bool HasMeasurement => (bool)GetValue(HasMeasurementProperty)!;
    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsMeasurementView)d;
        view.SetValue(HasMeasurementPropertyKey, view.Measurement != null); view.UpdateStates();
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, HasMeasurement ? "ResultAvailable" : "ResultEmpty", false);
        VisualStateManager.GoToState(this, Measurement?.IsSnapped == true ? "Snapped" : "Free", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new MeasurementPeer(this);
    private sealed class MeasurementPeer(DevToolsMeasurementView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsMeasurementView);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.Measurement?.AutomationName ?? "Ruler measurement";
    }
}

/// <summary>A color swatch and channel readout with an optional consumer-owned copy command.</summary>
[TemplateVisualState(Name = "ResultAvailable", GroupName = "ResultStates")]
[TemplateVisualState(Name = "ResultEmpty", GroupName = "ResultStates")]
[TemplateVisualState(Name = "CopyAvailable", GroupName = "CopyStates")]
[TemplateVisualState(Name = "CopyUnavailable", GroupName = "CopyStates")]
public class DevToolsColorSampleView : Control
{
    static DevToolsColorSampleView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsColorSampleView), new PropertyMetadata(typeof(DevToolsColorSampleView)));
    public static readonly DependencyProperty SampleProperty = DependencyProperty.Register(nameof(Sample), typeof(DevToolsColorSample), typeof(DevToolsColorSampleView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnSampleChanged));
    public static readonly DependencyProperty CopyCommandProperty = DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand), typeof(DevToolsColorSampleView), new PropertyMetadata(null, OnCopyChanged));
    private static readonly DependencyPropertyKey HasSamplePropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasSample), typeof(bool), typeof(DevToolsColorSampleView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasSampleProperty = HasSamplePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey SwatchBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SwatchBrush), typeof(Brush), typeof(DevToolsColorSampleView), new PropertyMetadata(null));
    public static readonly DependencyProperty SwatchBrushProperty = SwatchBrushPropertyKey.DependencyProperty;
    public DevToolsColorSample? Sample { get => (DevToolsColorSample?)GetValue(SampleProperty); set => SetValue(SampleProperty, value); }
    public ICommand? CopyCommand { get => (ICommand?)GetValue(CopyCommandProperty); set => SetValue(CopyCommandProperty, value); }
    public bool HasSample => (bool)GetValue(HasSampleProperty)!;
    public Brush? SwatchBrush => (Brush?)GetValue(SwatchBrushProperty);
    private static void OnSampleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsColorSampleView)d;
        view.SetValue(HasSamplePropertyKey, view.Sample != null);
        view.SetValue(SwatchBrushPropertyKey, view.Sample is { } sample ? new SolidColorBrush(sample.Color) : null);
        view.UpdateStates();
    }
    private static void OnCopyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsColorSampleView)d).UpdateStates();
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, HasSample ? "ResultAvailable" : "ResultEmpty", false);
        VisualStateManager.GoToState(this, CopyCommand == null ? "CopyUnavailable" : "CopyAvailable", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ColorSamplePeer(this);
    private sealed class ColorSamplePeer(DevToolsColorSampleView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsColorSampleView);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.Sample?.AutomationName ?? "Color sample";
    }
}
