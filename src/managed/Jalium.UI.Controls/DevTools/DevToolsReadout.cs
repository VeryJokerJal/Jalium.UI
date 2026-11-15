using Jalium.UI.Media;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A themeable key/value readout with ordinary text elements in its template.</summary>
public class DevToolsReadout : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DevToolsReadout), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(DevToolsReadout), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(DevToolsReadout), new PropertyMetadata(null));
    static DevToolsReadout() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsReadout), new PropertyMetadata(typeof(DevToolsReadout)));
    public string Label { get => (string?)GetValue(LabelProperty) ?? string.Empty; set => SetValue(LabelProperty, value); }
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public Brush? ValueBrush { get => (Brush?)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
}

/// <summary>A compact fractional progress readout using the platform range and progress contracts.</summary>
public class DevToolsMeter : ProgressBar
{
    public static readonly DependencyProperty LabelProperty = DevToolsReadout.LabelProperty.AddOwner(typeof(DevToolsMeter));
    static DevToolsMeter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsMeter), new PropertyMetadata(typeof(DevToolsMeter)));
        MaximumProperty.OverrideMetadata(typeof(DevToolsMeter), new FrameworkPropertyMetadata(1.0));
    }
    public string Label { get => (string?)GetValue(LabelProperty) ?? string.Empty; set => SetValue(LabelProperty, value); }
    protected override AutomationPeer OnCreateAutomationPeer() => new MeterAutomationPeer(this);
    private sealed class MeterAutomationPeer(DevToolsMeter owner) : ProgressBarAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsMeter);
        protected override string GetNameCore() => owner.Label;
    }
}

/// <summary>A labelled progress meter whose readout and track can be replaced together.</summary>
public class DevToolsMeterRow : Control
{
    public static readonly DependencyProperty LabelProperty = DevToolsReadout.LabelProperty.AddOwner(typeof(DevToolsMeterRow));
    public static readonly DependencyProperty ValueTextProperty = DevToolsReadout.ValueTextProperty.AddOwner(typeof(DevToolsMeterRow));
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(DevToolsMeterRow), new PropertyMetadata(0.0, null,
            static (_, value) => NormalizeFraction(value is double fraction ? fraction : 0)));
    static DevToolsMeterRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsMeterRow), new PropertyMetadata(typeof(DevToolsMeterRow)));
    public string Label { get => (string?)GetValue(LabelProperty) ?? string.Empty; set => SetValue(LabelProperty, value); }
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public double Fraction { get => (double)GetValue(FractionProperty)!; set => SetValue(FractionProperty, value); }
    internal static double NormalizeFraction(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
}

/// <summary>A semantic data tag. Tint layers and typography are supplied by its template.</summary>
public class DevToolsTag : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DevToolsTag), new PropertyMetadata(string.Empty, OnTextChanged));
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(DevToolsTag), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;
    public static readonly DependencyProperty TintOpacityProperty = DependencyProperty.Register(
        nameof(TintOpacity), typeof(double), typeof(DevToolsTag), new PropertyMetadata(0.14, null,
            static (_, value) => DevToolsMeterRow.NormalizeFraction(value is double opacity ? opacity : 0)));
    static DevToolsTag() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsTag), new PropertyMetadata(typeof(DevToolsTag)));
    public string Text { get => (string?)GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }
    public string DisplayText => (string)GetValue(DisplayTextProperty)!;
    public double TintOpacity { get => (double)GetValue(TintOpacityProperty)!; set => SetValue(TintOpacityProperty, value); }
    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DevToolsTag)d).SetValue(DisplayTextPropertyKey, DevToolsUi.Tracked(((string?)e.NewValue ?? string.Empty).ToUpperInvariant()));
}
