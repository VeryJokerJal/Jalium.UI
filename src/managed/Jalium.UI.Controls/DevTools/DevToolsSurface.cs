using Jalium.UI.Media;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Built-in developer-tools surface treatments.</summary>
public enum DevToolsSurfaceKind { Card, AlternateCard, Panel, Toolbar, StatusBar, CornerFrame, Row, AlternateRow, TabShell }

/// <summary>A reusable content surface whose decoration, header and status indicator belong to its theme.</summary>
[TemplateVisualState(Name = "Card", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "AlternateCard", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "Panel", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "Toolbar", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "StatusBar", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "CornerFrame", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "Row", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "AlternateRow", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "TabShell", GroupName = "SurfaceStates")]
[TemplateVisualState(Name = "IndicatorVisible", GroupName = "IndicatorStates")]
[TemplateVisualState(Name = "IndicatorHidden", GroupName = "IndicatorStates")]
public class DevToolsSurface : ContentControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(DevToolsSurfaceKind), typeof(DevToolsSurface), new PropertyMetadata(DevToolsSurfaceKind.Card));
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(DevToolsSurface), new PropertyMetadata(string.Empty, OnHeadingChanged));
    private static readonly DependencyPropertyKey DisplayHeadingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayHeading), typeof(string), typeof(DevToolsSurface), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayHeadingProperty = DisplayHeadingPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IndicatorBrushProperty = DependencyProperty.Register(
        nameof(IndicatorBrush), typeof(Brush), typeof(DevToolsSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty IsIndicatorOnProperty = DependencyProperty.Register(
        nameof(IsIndicatorOn), typeof(bool), typeof(DevToolsSurface), new PropertyMetadata(true));
    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush), typeof(Brush), typeof(DevToolsSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty TickSizeProperty = DependencyProperty.Register(
        nameof(TickSize), typeof(double), typeof(DevToolsSurface), new PropertyMetadata(9.0, null,
            static (_, value) => value is double size && double.IsFinite(size) ? Math.Max(0, size) : 0.0));

    static DevToolsSurface() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsSurface), new PropertyMetadata(typeof(DevToolsSurface)));

    public DevToolsSurface() => UseTemplateContentManagement();

    public DevToolsSurfaceKind Kind { get => (DevToolsSurfaceKind)GetValue(KindProperty)!; set => SetValue(KindProperty, value); }
    public string Heading { get => (string?)GetValue(HeadingProperty) ?? string.Empty; set => SetValue(HeadingProperty, value); }
    public string DisplayHeading => (string)GetValue(DisplayHeadingProperty)!;
    public Brush? IndicatorBrush { get => (Brush?)GetValue(IndicatorBrushProperty); set => SetValue(IndicatorBrushProperty, value); }
    public bool IsIndicatorOn { get => (bool)GetValue(IsIndicatorOnProperty)!; set => SetValue(IsIndicatorOnProperty, value); }
    public Brush? TickBrush { get => (Brush?)GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }
    public double TickSize { get => (double)GetValue(TickSizeProperty)!; set => SetValue(TickSizeProperty, value); }

    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == KindProperty || e.Property == IndicatorBrushProperty) UpdateStates();
    }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, Kind.ToString(), false);
        VisualStateManager.GoToState(this, IndicatorBrush == null ? "IndicatorHidden" : "IndicatorVisible", false);
    }
    private static void OnHeadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DevToolsSurface)d).SetValue(DisplayHeadingPropertyKey, DevToolsUi.Tracked(((string?)e.NewValue ?? string.Empty).ToUpperInvariant()));

    protected override AutomationPeer OnCreateAutomationPeer() => new SurfaceAutomationPeer(this);
    private sealed class SurfaceAutomationPeer(DevToolsSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsSurface);
        protected override string GetNameCore() => owner.Heading.Length > 0 ? owner.Heading : owner.Content as string ?? string.Empty;
    }
}

/// <summary>A decorative status indicator with a template-defined ring and core.</summary>
[TemplateVisualState(Name = "On", GroupName = "IndicatorStates")]
[TemplateVisualState(Name = "Off", GroupName = "IndicatorStates")]
public class DevToolsStatusIndicator : Control
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(DevToolsStatusIndicator), new PropertyMetadata(true));
    static DevToolsStatusIndicator() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsStatusIndicator), new PropertyMetadata(typeof(DevToolsStatusIndicator)));
    public bool IsOn { get => (bool)GetValue(IsOnProperty)!; set => SetValue(IsOnProperty, value); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsOnProperty) UpdateStates();
    }
    private void UpdateStates() => VisualStateManager.GoToState(this, IsOn ? "On" : "Off", false);
}
