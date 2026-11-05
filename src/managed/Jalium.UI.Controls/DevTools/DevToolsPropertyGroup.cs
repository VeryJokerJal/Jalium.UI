using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;

namespace Jalium.UI.Controls.DevTools;

/// <summary>The standard alternation converter configured for diagnostic row parity.</summary>
public sealed class DevToolsPropertyGroupAlternationConverter : AlternationConverter
{
    public DevToolsPropertyGroupAlternationConverter() { Values.Add(false); Values.Add(true); }
}

/// <summary>A template-owned, expandable diagnostic property category with standard item templates.</summary>
[TemplatePart(Name = "PART_Header", Type = typeof(DevToolsActionButton))]
[TemplateVisualState(Name = "Expanded", GroupName = "ExpansionStates")]
[TemplateVisualState(Name = "Collapsed", GroupName = "ExpansionStates")]
public class DevToolsPropertyGroup : ItemsControl
{
    static DevToolsPropertyGroup() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsPropertyGroup), new PropertyMetadata(typeof(DevToolsPropertyGroup)));
    public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register(nameof(Category), typeof(DevToolsPropertyCategory), typeof(DevToolsPropertyGroup), new PropertyMetadata(DevToolsPropertyCategory.Other, OnInputChanged), value => value is DevToolsPropertyCategory category && Enum.IsDefined(category));
    public static readonly DependencyProperty NameColumnWidthProperty = DependencyProperty.Register(nameof(NameColumnWidth), typeof(double), typeof(DevToolsPropertyGroup), new PropertyMetadata(145d), value => value is double width && double.IsFinite(width) && width >= 0);
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(DevToolsPropertyGroup), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnInputChanged));
    public static readonly DependencyProperty FontDialogCommandProperty = DependencyProperty.Register(nameof(FontDialogCommand), typeof(ICommand), typeof(DevToolsPropertyGroup), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey HeadingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Heading), typeof(string), typeof(DevToolsPropertyGroup), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HeadingProperty = HeadingPropertyKey.DependencyProperty;
    public DevToolsPropertyCategory Category { get => (DevToolsPropertyCategory)GetValue(CategoryProperty)!; set => SetValue(CategoryProperty, value); }
    public double NameColumnWidth { get => (double)GetValue(NameColumnWidthProperty)!; set => SetValue(NameColumnWidthProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty)!; set => SetValue(IsExpandedProperty, value); }
    public ICommand? FontDialogCommand { get => (ICommand?)GetValue(FontDialogCommandProperty); set => SetValue(FontDialogCommandProperty, value); }
    public string Heading => (string)GetValue(HeadingProperty)!;
    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsPropertyGroup)d).Refresh();
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) { base.OnItemsChanged(e); Refresh(); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); Refresh(); }
    private void Refresh()
    {
        SetValue(HeadingPropertyKey, DevToolsUi.Tracked($"{Category} ({Items.Count})".ToUpperInvariant()));
        if (TemplateRootInternal != null) VisualStateManager.GoToState(this, IsExpanded ? "Expanded" : "Collapsed", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new PropertyGroupPeer(this);
    private sealed class PropertyGroupPeer(DevToolsPropertyGroup owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsPropertyGroup);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : $"{owner.Category} ({owner.Items.Count})";
        protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.ExpandCollapse && owner.Items.Count > 0 ? this : base.GetPatternCore(patternInterface);
        public ExpandCollapseState ExpandCollapseState => owner.Items.Count == 0 ? ExpandCollapseState.LeafNode : owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() { CheckEnabled(); owner.IsExpanded = true; }
        public void Collapse() { CheckEnabled(); owner.IsExpanded = false; }
        private void CheckEnabled() { if (!IsEnabled()) throw new InvalidOperationException("Cannot change a disabled property group."); }
    }
}
