using System.Globalization;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A replaceable type declaration and statistics header for an inspected element.</summary>
[TemplateVisualState(Name = "TypeVisible", GroupName = "TypeStates")]
[TemplateVisualState(Name = "TypeHidden", GroupName = "TypeStates")]
[TemplateVisualState(Name = "NameVisible", GroupName = "NameStates")]
[TemplateVisualState(Name = "NameHidden", GroupName = "NameStates")]
[TemplateVisualState(Name = "BaseTypeVisible", GroupName = "BaseTypeStates")]
[TemplateVisualState(Name = "BaseTypeHidden", GroupName = "BaseTypeStates")]
[TemplateVisualState(Name = "StatisticsVisible", GroupName = "StatisticsStates")]
[TemplateVisualState(Name = "StatisticsHidden", GroupName = "StatisticsStates")]
public class DevToolsElementSummary : Control
{
    private double _arrangedWidth = double.NaN;
    static DevToolsElementSummary() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsElementSummary), new PropertyMetadata(typeof(DevToolsElementSummary)));
    private static DependencyProperty Text(string name) => DependencyProperty.Register(name, typeof(string), typeof(DevToolsElementSummary), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));
    private static DependencyProperty Count(string name) => DependencyProperty.Register(name, typeof(int), typeof(DevToolsElementSummary), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged), value => value is int count && count >= 0);
    public static readonly DependencyProperty TypeNameProperty = Text(nameof(TypeName));
    public static readonly DependencyProperty BaseTypeNameProperty = Text(nameof(BaseTypeName));
    public static readonly DependencyProperty ElementNameProperty = Text(nameof(ElementName));
    public static readonly DependencyProperty DepthProperty = Count(nameof(Depth));
    public static readonly DependencyProperty ChildrenCountProperty = Count(nameof(ChildrenCount));
    public static readonly DependencyProperty DescendantCountProperty = Count(nameof(DescendantCount));
    public static readonly DependencyProperty ShowStatisticsProperty = DependencyProperty.Register(nameof(ShowStatistics), typeof(bool), typeof(DevToolsElementSummary), new PropertyMetadata(false, OnInputChanged));
    private static readonly DependencyPropertyKey AvailableTextWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(AvailableTextWidth), typeof(double), typeof(DevToolsElementSummary), new PropertyMetadata(double.PositiveInfinity));
    public static readonly DependencyProperty AvailableTextWidthProperty = AvailableTextWidthPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey NameTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(NameText), typeof(string), typeof(DevToolsElementSummary), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty NameTextProperty = NameTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey BaseTypeTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(BaseTypeText), typeof(string), typeof(DevToolsElementSummary), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty BaseTypeTextProperty = BaseTypeTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey DepthTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DepthText), typeof(string), typeof(DevToolsElementSummary), new PropertyMetadata("0"));
    public static readonly DependencyProperty DepthTextProperty = DepthTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ChildrenTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ChildrenText), typeof(string), typeof(DevToolsElementSummary), new PropertyMetadata("0"));
    public static readonly DependencyProperty ChildrenTextProperty = ChildrenTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey DescendantsTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DescendantsText), typeof(string), typeof(DevToolsElementSummary), new PropertyMetadata("0"));
    public static readonly DependencyProperty DescendantsTextProperty = DescendantsTextPropertyKey.DependencyProperty;
    public string TypeName { get => (string?)GetValue(TypeNameProperty) ?? string.Empty; set => SetValue(TypeNameProperty, value); }
    public string BaseTypeName { get => (string?)GetValue(BaseTypeNameProperty) ?? string.Empty; set => SetValue(BaseTypeNameProperty, value); }
    public string ElementName { get => (string?)GetValue(ElementNameProperty) ?? string.Empty; set => SetValue(ElementNameProperty, value); }
    public int Depth { get => (int)GetValue(DepthProperty)!; set => SetValue(DepthProperty, value); }
    public int ChildrenCount { get => (int)GetValue(ChildrenCountProperty)!; set => SetValue(ChildrenCountProperty, value); }
    public int DescendantCount { get => (int)GetValue(DescendantCountProperty)!; set => SetValue(DescendantCountProperty, value); }
    public bool ShowStatistics { get => (bool)GetValue(ShowStatisticsProperty)!; set => SetValue(ShowStatisticsProperty, value); }
    public double AvailableTextWidth => (double)GetValue(AvailableTextWidthProperty)!;
    public string NameText => (string)GetValue(NameTextProperty)!;
    public string BaseTypeText => (string)GetValue(BaseTypeTextProperty)!;
    public string DepthText => (string)GetValue(DepthTextProperty)!;
    public string ChildrenText => (string)GetValue(ChildrenTextProperty)!;
    public string DescendantsText => (string)GetValue(DescendantsTextProperty)!;
    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var summary = (DevToolsElementSummary)d;
        summary.SetValue(NameTextPropertyKey, string.IsNullOrEmpty(summary.ElementName) ? string.Empty : "#" + summary.ElementName);
        summary.SetValue(BaseTypeTextPropertyKey, string.IsNullOrEmpty(summary.BaseTypeName) ? string.Empty : ": " + summary.BaseTypeName);
        summary.SetValue(DepthTextPropertyKey, summary.Depth.ToString(CultureInfo.InvariantCulture));
        summary.SetValue(ChildrenTextPropertyKey, summary.ChildrenCount.ToString(CultureInfo.InvariantCulture));
        summary.SetValue(DescendantsTextPropertyKey, summary.DescendantCount.ToString(CultureInfo.InvariantCulture));
        summary.UpdateStates();
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, TypeName.Length > 0 ? "TypeVisible" : "TypeHidden", false);
        VisualStateManager.GoToState(this, ElementName.Length > 0 ? "NameVisible" : "NameHidden", false);
        VisualStateManager.GoToState(this, BaseTypeName.Length > 0 ? "BaseTypeVisible" : "BaseTypeHidden", false);
        VisualStateManager.GoToState(this, ShowStatistics ? "StatisticsVisible" : "StatisticsHidden", false);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        if (double.IsFinite(width) && double.IsFinite(_arrangedWidth) && _arrangedWidth > 0) width = Math.Min(width, _arrangedWidth);
        SetValue(AvailableTextWidthPropertyKey, double.IsFinite(width) ? Math.Max(0, width) : double.PositiveInfinity);
        return base.MeasureOverride(availableSize);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _arrangedWidth = finalSize.Width; SetValue(AvailableTextWidthPropertyKey, Math.Max(0, finalSize.Width));
        return base.ArrangeOverride(finalSize);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new SummaryPeer(this);
    private sealed class SummaryPeer(DevToolsElementSummary owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsElementSummary);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name :
            owner.TypeName + (owner.NameText.Length > 0 ? " " + owner.NameText : "") + (owner.BaseTypeName.Length > 0 ? ", base " + owner.BaseTypeName : "") +
            (owner.ShowStatistics ? $"; depth {owner.DepthText}; children {owner.ChildrenText}; descendants {owner.DescendantsText}" : "");
    }
}
