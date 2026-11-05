using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A virtualizable, indented selection item with template-owned expansion chrome.</summary>
[TemplatePart(Name = "PART_Expander", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_RevealContent", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Chevron", Type = typeof(UIElement))]
[TemplateVisualState(Name = "Branch", GroupName = "NodeStates")]
[TemplateVisualState(Name = "Leaf", GroupName = "NodeStates")]
[TemplateVisualState(Name = "Expanded", GroupName = "ExpansionStates")]
[TemplateVisualState(Name = "Collapsed", GroupName = "ExpansionStates")]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "MouseOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
public class DevToolsTreeItem : ListBoxItem
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(int), typeof(DevToolsTreeItem), new PropertyMetadata(0, OnIndentChanged, (_, v) => Math.Max(0, (int)(v ?? 0))));
    public static readonly DependencyProperty IndentSizeProperty = DependencyProperty.Register(
        nameof(IndentSize), typeof(double), typeof(DevToolsTreeItem), new PropertyMetadata(14.0, OnIndentChanged, CoerceLength));
    private static readonly DependencyPropertyKey IndentWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IndentWidth), typeof(double), typeof(DevToolsTreeItem), new PropertyMetadata(0.0));
    public static readonly DependencyProperty IndentWidthProperty = IndentWidthPropertyKey.DependencyProperty;
    public static readonly DependencyProperty HasChildrenProperty = DependencyProperty.Register(
        nameof(HasChildren), typeof(bool), typeof(DevToolsTreeItem), new PropertyMetadata(false));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(DevToolsTreeItem), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnExpansionChanged));
    public static readonly DependencyProperty ChevronAngleProperty = DependencyProperty.Register(
        nameof(ChevronAngle), typeof(double), typeof(DevToolsTreeItem), new PropertyMetadata(-90.0, null,
            (_, v) => v is double angle && double.IsFinite(angle) ? angle : -90.0));
    public static readonly DependencyProperty RevealProgressProperty = DependencyProperty.Register(
        nameof(RevealProgress), typeof(double), typeof(DevToolsTreeItem), new PropertyMetadata(1.0,
            (d, _) => ((DevToolsTreeItem)d).ApplyReveal(), (_, v) => v is double value && !double.IsNaN(value) ? Math.Clamp(value, 0, 1) : 1.0));
    public static readonly RoutedEvent ExpansionChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ExpansionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsTreeItem));

    private ButtonBase? _expander;
    private UIElement? _chevron;
    private FrameworkElement? _revealContent;
    private Transform? _originalTransform;
    private TranslateTransform? _revealTransform;
    private double _lastRevealY = double.NaN;

    static DevToolsTreeItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsTreeItem), new PropertyMetadata(typeof(DevToolsTreeItem)));
    public DevToolsTreeItem() => AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnExpansionKey));
    public int Level { get => (int)GetValue(LevelProperty)!; set => SetValue(LevelProperty, value); }
    public double IndentSize { get => (double)GetValue(IndentSizeProperty)!; set => SetValue(IndentSizeProperty, value); }
    public double IndentWidth => (double)GetValue(IndentWidthProperty)!;
    public bool HasChildren { get => (bool)GetValue(HasChildrenProperty)!; set => SetValue(HasChildrenProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty)!; set => SetValue(IsExpandedProperty, value); }
    /// <summary>The current glyph angle, allowing a virtualized tree's animation driver to supply intermediate values.</summary>
    public double ChevronAngle { get => (double)GetValue(ChevronAngleProperty)!; set => SetValue(ChevronAngleProperty, value); }
    /// <summary>Render-only slide into the row's clipped slot; leaves the virtualized row's size unchanged.</summary>
    public double RevealProgress { get => (double)GetValue(RevealProgressProperty)!; set => SetValue(RevealProgressProperty, value); }
    public event RoutedEventHandler ExpansionChanged { add => AddHandler(ExpansionChangedEvent, value); remove => RemoveHandler(ExpansionChangedEvent, value); }

    private static object CoerceLength(DependencyObject d, object? v) => v is double length && double.IsFinite(length) ? Math.Max(0, length) : 0.0;
    private static void OnIndentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (DevToolsTreeItem)d;
        item.SetValue(IndentWidthPropertyKey, Math.Min(double.MaxValue, item.Level * item.IndentSize));
    }
    private static void OnExpansionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (DevToolsTreeItem)d;
        item.SetCurrentValue(ChevronAngleProperty, item.IsExpanded ? 0.0 : -90.0);
        item.RaiseEvent(new RoutedEventArgs(ExpansionChangedEvent, item));
    }
    public override void OnApplyTemplate()
    {
        DetachParts();
        base.OnApplyTemplate();
        _expander = GetTemplateChild("PART_Expander") as ButtonBase;
        if (_expander != null) _expander.Click += OnExpanderClick;
        _revealContent = GetTemplateChild("PART_RevealContent") as FrameworkElement;
        _chevron = GetTemplateChild("PART_Chevron") as UIElement;
        _originalTransform = _revealContent?.RenderTransform;
        ApplyChevronAngle();
        UpdateStates();
        ApplyReveal();
    }
    protected override void OnTemplateChanging() { DetachParts(); base.OnTemplateChanging(); }
    private void DetachParts()
    {
        if (_expander != null) _expander.Click -= OnExpanderClick;
        _expander = null;
        _chevron = null;
        if (_revealContent != null && ReferenceEquals(_revealContent.RenderTransform, _revealTransform))
            _revealContent.RenderTransform = _originalTransform;
        _revealContent = null;
        _originalTransform = null;
        _lastRevealY = double.NaN;
    }
    private void OnExpanderClick(object sender, RoutedEventArgs e)
    {
        if (IsEnabled && HasChildren) SetCurrentValue(IsExpandedProperty, !IsExpanded);
        e.Handled = true;
    }
    private void OnExpansionKey(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || !HasChildren || e.Key is not (Key.Left or Key.Right)) return;
        bool expand = e.Key == (FlowDirection == FlowDirection.RightToLeft ? Key.Left : Key.Right);
        if (expand != IsExpanded) SetCurrentValue(IsExpandedProperty, expand);
        e.Handled = true;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ChevronAngleProperty) ApplyChevronAngle();
        if (e.Property == HasChildrenProperty || e.Property == IsExpandedProperty || e.Property == IsSelectedProperty ||
            e.Property == IsMouseOverProperty || e.Property == IsEnabledProperty || e.Property == IsKeyboardFocusedProperty) UpdateStates();
    }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, HasChildren ? "Branch" : "Leaf", false);
        VisualStateManager.GoToState(this, IsExpanded ? "Expanded" : "Collapsed", false);
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "MouseOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsKeyboardFocused && IsEnabled ? "Focused" : "Unfocused", false);
    }
    private void ApplyChevronAngle()
    {
        if (_chevron?.RenderTransform is RotateTransform transform)
            transform.SetCurrentValue(RotateTransform.AngleProperty, ChevronAngle);
    }
    protected internal override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); ApplyReveal(); }
    private void ApplyReveal()
    {
        if (_revealContent == null) return;
        if (RevealProgress >= 1)
        {
            if (ReferenceEquals(_revealContent.RenderTransform, _revealTransform) && _revealTransform != null)
            {
                _revealContent.RenderTransform = _originalTransform;
                _lastRevealY = double.NaN;
                InvalidateComposition();
            }
            return;
        }
        double height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height > 0 ? DesiredSize.Height : 28.0;
        double y = -(1 - RevealProgress) * height;
        if (y == _lastRevealY && ReferenceEquals(_revealContent.RenderTransform, _revealTransform)) return;
        var transform = _revealTransform ??= new TranslateTransform();
        transform.Y = y;
        _lastRevealY = y;
        _revealContent.RenderTransform = transform;
        InvalidateComposition();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new TreeItemPeer(this);
    private sealed class TreeItemPeer(DevToolsTreeItem owner) : ListBoxItemAutomationPeer(owner), IExpandCollapseProvider
    {
        protected override string GetClassNameCore() => nameof(DevToolsTreeItem);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(pattern);
        public ExpandCollapseState ExpandCollapseState => !owner.HasChildren ? ExpandCollapseState.LeafNode : owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => ChangeExpansion(true);
        public void Collapse() => ChangeExpansion(false);
        private void ChangeExpansion(bool expanded)
        {
            if (!IsEnabled() || !owner.HasChildren) throw new InvalidOperationException("The tree item cannot change expansion.");
            owner.SetCurrentValue(IsExpandedProperty, expanded);
        }
    }
}
