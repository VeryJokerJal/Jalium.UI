using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A noninteractive diagnostic heading with theme-owned typography.</summary>
public class DevToolsHeading : Control
{
    static DevToolsHeading() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsHeading), new PropertyMetadata(typeof(DevToolsHeading)));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(DevToolsHeading), new PropertyMetadata(string.Empty, (d, e) => ((DevToolsHeading)d).SetValue(DisplayTextPropertyKey, DevToolsUi.Tracked(((string?)e.NewValue ?? string.Empty).ToUpperInvariant()))));
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisplayText), typeof(string), typeof(DevToolsHeading), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;
    public string Text { get => (string?)GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }
    public string DisplayText => (string)GetValue(DisplayTextProperty)!;
    protected override AutomationPeer OnCreateAutomationPeer() => new HeadingPeer(this);
    private sealed class HeadingPeer(DevToolsHeading owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(DevToolsHeading);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.Text;
    }
}

/// <summary>Diagnostic presentation data. FromValue copies previews without retaining the inspected object.</summary>
public class DevToolsDetailItem : DependencyObject
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsDetailItem), new PropertyMetadata(value));
    public static readonly DependencyProperty NameProperty = Property(nameof(Name), typeof(string), string.Empty);
    public static readonly DependencyProperty ValueTextProperty = Property(nameof(ValueText), typeof(string), string.Empty);
    public static readonly DependencyProperty DetailTextProperty = Property(nameof(DetailText), typeof(string), string.Empty);
    public static readonly DependencyProperty ValueKindProperty = Property(nameof(ValueKind), typeof(DevToolsResourceValueKind), DevToolsResourceValueKind.Object);
    public static readonly DependencyProperty SwatchBrushProperty = Property(nameof(SwatchBrush), typeof(Brush), null);
    public string Name { get => (string?)GetValue(NameProperty) ?? string.Empty; set => SetValue(NameProperty, value); }
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public string DetailText { get => (string?)GetValue(DetailTextProperty) ?? string.Empty; set => SetValue(DetailTextProperty, value); }
    public DevToolsResourceValueKind ValueKind { get => (DevToolsResourceValueKind)GetValue(ValueKindProperty)!; set => SetValue(ValueKindProperty, value); }
    public Brush? SwatchBrush { get => (Brush?)GetValue(SwatchBrushProperty); set => SetValue(SwatchBrushProperty, value); }
    public static DevToolsDetailItem FromValue(string name, object? value, string? detail = null)
    {
        var preview = DevToolsResourceSnapshot.Preview(value);
        return new() { Name = name, ValueText = preview.Text, DetailText = detail ?? (preview.Kind == DevToolsResourceValueKind.Style ? preview.Detail : string.Empty), ValueKind = preview.Kind, SwatchBrush = preview.Swatch };
    }
}

/// <summary>A wrapping diagnostic value row with replaceable semantic colors and preview parts.</summary>
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Plain", GroupName = "DetailStates")]
[TemplateVisualState(Name = "Detailed", GroupName = "DetailStates")]
[TemplateVisualState(Name = "Typed", GroupName = "PreviewStates")]
[TemplateVisualState(Name = "Swatch", GroupName = "PreviewStates")]
[TemplateVisualState(Name = "Regular", GroupName = "RowStates")]
[TemplateVisualState(Name = "Alternate", GroupName = "RowStates")]
[TemplateVisualState(Name = "Object", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Null", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Color", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Brush", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Style", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Template", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Thickness", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Number", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Boolean", GroupName = "ValueStates")]
[TemplateVisualState(Name = "String", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Enum", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Reference", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsDetailRow : Control
{
    private double _layoutWidth = double.PositiveInfinity;
    static DevToolsDetailRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsDetailRow), new PropertyMetadata(typeof(DevToolsDetailRow)));
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsDetailRow), new PropertyMetadata(value, OnStateChanged));
    public static readonly DependencyProperty LabelProperty = Property(nameof(Label), typeof(string), string.Empty);
    public static readonly DependencyProperty ValueTextProperty = Property(nameof(ValueText), typeof(string), string.Empty);
    public static readonly DependencyProperty DetailTextProperty = Property(nameof(DetailText), typeof(string), string.Empty);
    public static readonly DependencyProperty ValueKindProperty = DependencyProperty.Register(nameof(ValueKind), typeof(DevToolsResourceValueKind), typeof(DevToolsDetailRow), new PropertyMetadata(DevToolsResourceValueKind.Object, OnStateChanged), value => value is DevToolsResourceValueKind kind && Enum.IsDefined(kind));
    public static readonly DependencyProperty SwatchBrushProperty = Property(nameof(SwatchBrush), typeof(Brush), null);
    public static readonly DependencyProperty IsAlternateProperty = Property(nameof(IsAlternate), typeof(bool), false);
    public static readonly DependencyProperty NameColumnWidthProperty = DependencyProperty.Register(nameof(NameColumnWidth), typeof(double), typeof(DevToolsDetailRow), new PropertyMetadata(145d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    private static readonly DependencyPropertyKey EffectiveNameColumnWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveNameColumnWidth), typeof(double), typeof(DevToolsDetailRow), new PropertyMetadata(145d));
    public static readonly DependencyProperty EffectiveNameColumnWidthProperty = EffectiveNameColumnWidthPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey AvailableContentWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(AvailableContentWidth), typeof(double), typeof(DevToolsDetailRow), new PropertyMetadata(double.PositiveInfinity));
    public static readonly DependencyProperty AvailableContentWidthProperty = AvailableContentWidthPropertyKey.DependencyProperty;
    public string Label { get => (string?)GetValue(LabelProperty) ?? string.Empty; set => SetValue(LabelProperty, value); }
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public string DetailText { get => (string?)GetValue(DetailTextProperty) ?? string.Empty; set => SetValue(DetailTextProperty, value); }
    public DevToolsResourceValueKind ValueKind { get => (DevToolsResourceValueKind)GetValue(ValueKindProperty)!; set => SetValue(ValueKindProperty, value); }
    public Brush? SwatchBrush { get => (Brush?)GetValue(SwatchBrushProperty); set => SetValue(SwatchBrushProperty, value); }
    public bool IsAlternate { get => (bool)GetValue(IsAlternateProperty)!; set => SetValue(IsAlternateProperty, value); }
    public double NameColumnWidth { get => (double)GetValue(NameColumnWidthProperty)!; set => SetValue(NameColumnWidthProperty, value); }
    public double EffectiveNameColumnWidth => (double)GetValue(EffectiveNameColumnWidthProperty)!;
    /// <summary>The finite text budget inside the default template's horizontal padding.</summary>
    public double AvailableContentWidth => (double)GetValue(AvailableContentWidthProperty)!;
    public DevToolsDetailRow() => SizeChanged += (_, e) => { _layoutWidth = e.NewSize.Width; UpdateStates(); };
    protected override Size MeasureOverride(Size availableSize) { _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth > 0 ? ActualWidth : double.PositiveInfinity; UpdateStates(); return base.MeasureOverride(availableSize); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == PaddingProperty) UpdateStates();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsDetailRow)d).UpdateStates();
    private void UpdateStates()
    {
        bool compact = _layoutWidth < NameColumnWidth + 160;
        SetValue(EffectiveNameColumnWidthPropertyKey, compact ? double.NaN : NameColumnWidth);
        // TextBlock keeps an optical ink allowance in DesiredSize. Bound the
        // content box so that allowance does not widen the scrolling extent.
        SetValue(AvailableContentWidthPropertyKey, Math.Max(0, _layoutWidth - Padding.Left - Padding.Right));
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, compact ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, DetailText.Length == 0 ? "Plain" : "Detailed", false);
        VisualStateManager.GoToState(this, SwatchBrush == null ? "Typed" : "Swatch", false);
        VisualStateManager.GoToState(this, IsAlternate ? "Alternate" : "Regular", false);
        VisualStateManager.GoToState(this, ValueKind.ToString(), false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new DetailPeer(this);
    private sealed class DetailPeer(DevToolsDetailRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsDetailRow);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : $"{owner.Label}: {owner.ValueText}" + (owner.DetailText.Length == 0 ? "" : " · " + owner.DetailText);
    }
}

/// <summary>A reusable, expandable detail list with an optional command action.</summary>
[TemplatePart(Name = "PART_Header", Type = typeof(DevToolsActionButton))]
[TemplatePart(Name = "PART_Action", Type = typeof(DevToolsActionButton))]
[TemplateVisualState(Name = "Expanded", GroupName = "ExpansionStates")]
[TemplateVisualState(Name = "Collapsed", GroupName = "ExpansionStates")]
[TemplateVisualState(Name = "ActionVisible", GroupName = "ActionStates")]
[TemplateVisualState(Name = "ActionHidden", GroupName = "ActionStates")]
[TemplateVisualState(Name = "DetailsAvailable", GroupName = "DataStates")]
[TemplateVisualState(Name = "DetailsEmpty", GroupName = "DataStates")]
public class DevToolsDetailSection : ItemsControl
{
    static DevToolsDetailSection() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsDetailSection), new PropertyMetadata(typeof(DevToolsDetailSection)));
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsDetailSection), new PropertyMetadata(value, OnStateChanged));
    public static readonly DependencyProperty TitleProperty = Property(nameof(Title), typeof(string), string.Empty);
    public static readonly DependencyProperty ShowCountProperty = Property(nameof(ShowCount), typeof(bool), false);
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(DevToolsDetailSection), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged));
    public static readonly DependencyProperty ActionLabelProperty = Property(nameof(ActionLabel), typeof(string), string.Empty);
    public static readonly DependencyProperty ActionCommandProperty = Property(nameof(ActionCommand), typeof(ICommand), null);
    public static readonly DependencyProperty ActionCommandParameterProperty = Property(nameof(ActionCommandParameter), typeof(object), null);
    private static readonly DependencyPropertyKey HeadingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Heading), typeof(string), typeof(DevToolsDetailSection), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HeadingProperty = HeadingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasDetailsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasDetails), typeof(bool), typeof(DevToolsDetailSection), new PropertyMetadata(false));
    public static readonly DependencyProperty HasDetailsProperty = HasDetailsPropertyKey.DependencyProperty;
    public string Title { get => (string?)GetValue(TitleProperty) ?? string.Empty; set => SetValue(TitleProperty, value); }
    public bool ShowCount { get => (bool)GetValue(ShowCountProperty)!; set => SetValue(ShowCountProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty)!; set => SetValue(IsExpandedProperty, value); }
    public string ActionLabel { get => (string?)GetValue(ActionLabelProperty) ?? string.Empty; set => SetValue(ActionLabelProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public object? ActionCommandParameter { get => GetValue(ActionCommandParameterProperty); set => SetValue(ActionCommandParameterProperty, value); }
    public string Heading => (string)GetValue(HeadingProperty)!;
    public bool HasDetails => (bool)GetValue(HasDetailsProperty)!;
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsDetailSection)d).UpdateStates();
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) { base.OnItemsChanged(e); UpdateStates(); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    private void UpdateStates()
    {
        SetValue(HeadingPropertyKey, DevToolsUi.Tracked((Title + (ShowCount ? $" ({Items.Count})" : "")).ToUpperInvariant()));
        bool actionVisible = ActionLabel.Length > 0 && ActionCommand != null;
        SetValue(HasDetailsPropertyKey, Items.Count > 0 || actionVisible);
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, IsExpanded ? "Expanded" : "Collapsed", false);
        VisualStateManager.GoToState(this, actionVisible ? "ActionVisible" : "ActionHidden", false);
        VisualStateManager.GoToState(this, HasDetails ? "DetailsAvailable" : "DetailsEmpty", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new SectionPeer(this);
    private sealed class SectionPeer(DevToolsDetailSection owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsDetailSection);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.Title + (owner.ShowCount ? $" ({owner.Items.Count})" : "");
        protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.ExpandCollapse && owner.HasDetails ? this : base.GetPatternCore(patternInterface);
        public ExpandCollapseState ExpandCollapseState => !owner.HasDetails ? ExpandCollapseState.LeafNode : owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() { CheckEnabled(); owner.IsExpanded = true; }
        public void Collapse() { CheckEnabled(); owner.IsExpanded = false; }
        private void CheckEnabled() { if (!IsEnabled()) throw new InvalidOperationException("Cannot change a disabled detail section."); }
    }
}

/// <summary>A template-owned, selectable XAML viewer with native text and scrolling semantics.</summary>
[TemplatePart(Name = "PART_Editor", Type = typeof(TextBox))]
public class DevToolsXamlView : Control
{
    static DevToolsXamlView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsXamlView), new PropertyMetadata(typeof(DevToolsXamlView)));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(DevToolsXamlView), new PropertyMetadata(string.Empty));
    public string Text { get => (string?)GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }
}
