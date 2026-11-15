using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>Presents an indented tree cell with a replaceable expansion affordance.</summary>
[TemplatePart(Name = "PART_Expander", Type = typeof(ToggleButton))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Branch", GroupName = "NodeStates")]
[TemplateVisualState(Name = "Leaf", GroupName = "NodeStates")]
[TemplateVisualState(Name = "Expanded", GroupName = "ExpansionStates")]
[TemplateVisualState(Name = "Collapsed", GroupName = "ExpansionStates")]
public class TreeDataGridCellPresenter : ContentControl
{
    static TreeDataGridCellPresenter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeDataGridCellPresenter), new PropertyMetadata(typeof(TreeDataGridCellPresenter)));
        FocusableProperty.OverrideMetadata(typeof(TreeDataGridCellPresenter), new PropertyMetadata(false));
    }

    public TreeDataGridCellPresenter() => UseTemplateContentManagement();

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(int), typeof(TreeDataGridCellPresenter), new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.AffectsMeasure, OnIndentChanged, (_, value) => Math.Max(0, (int)(value ?? 0))));
    public static readonly DependencyProperty IndentSizeProperty = DependencyProperty.Register(
        nameof(IndentSize), typeof(double), typeof(TreeDataGridCellPresenter), new FrameworkPropertyMetadata(16.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure, OnIndentChanged, CoerceLength));
    public static readonly DependencyProperty ExpanderSizeProperty = DependencyProperty.Register(
        nameof(ExpanderSize), typeof(double), typeof(TreeDataGridCellPresenter), new FrameworkPropertyMetadata(16.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure, null, CoerceLength));
    public static readonly DependencyProperty ExpanderStyleProperty = DependencyProperty.Register(
        nameof(ExpanderStyle), typeof(Style), typeof(TreeDataGridCellPresenter), new PropertyMetadata(null));
    public static readonly DependencyProperty HasChildrenProperty = DependencyProperty.Register(
        nameof(HasChildren), typeof(bool), typeof(TreeDataGridCellPresenter), new PropertyMetadata(false));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(TreeDataGridCellPresenter), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnExpansionChanged));
    private static readonly DependencyPropertyKey IndentWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IndentWidth), typeof(double), typeof(TreeDataGridCellPresenter), new PropertyMetadata(0.0));
    public static readonly DependencyProperty IndentWidthProperty = IndentWidthPropertyKey.DependencyProperty;

    public int Level { get => (int)GetValue(LevelProperty)!; set => SetValue(LevelProperty, value); }
    public double IndentSize { get => (double)GetValue(IndentSizeProperty)!; set => SetValue(IndentSizeProperty, value); }
    public double ExpanderSize { get => (double)GetValue(ExpanderSizeProperty)!; set => SetValue(ExpanderSizeProperty, value); }
    public Style? ExpanderStyle { get => (Style?)GetValue(ExpanderStyleProperty); set => SetValue(ExpanderStyleProperty, value); }
    public bool HasChildren { get => (bool)GetValue(HasChildrenProperty)!; set => SetValue(HasChildrenProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty)!; set => SetValue(IsExpandedProperty, value); }
    public double IndentWidth => (double)GetValue(IndentWidthProperty)!;

    public static readonly RoutedEvent ExpandedEvent = EventManager.RegisterRoutedEvent(
        nameof(Expanded), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TreeDataGridCellPresenter));
    public static readonly RoutedEvent CollapsedEvent = EventManager.RegisterRoutedEvent(
        nameof(Collapsed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TreeDataGridCellPresenter));
    public event RoutedEventHandler Expanded { add => AddHandler(ExpandedEvent, value); remove => RemoveHandler(ExpandedEvent, value); }
    public event RoutedEventHandler Collapsed { add => AddHandler(CollapsedEvent, value); remove => RemoveHandler(CollapsedEvent, value); }

    internal TreeDataGridNode? Node { get; set; }
    internal bool? InitialExpansionTransition { get; set; }
    private ToggleButton? _expander;
    private bool _synchronizingExpander;

    private static object CoerceLength(DependencyObject d, object? value) =>
        value is double length && double.IsFinite(length) ? Math.Max(0, length) : 0.0;
    private static void OnIndentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (TreeDataGridCellPresenter)d;
        presenter.SetValue(IndentWidthPropertyKey, Math.Min(double.MaxValue, presenter.Level * presenter.IndentSize));
    }
    private static void OnExpansionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (TreeDataGridCellPresenter)d;
        presenter.SyncExpander();
        presenter.UpdateVisualStates(true);
        presenter.RaiseEvent(new RoutedEventArgs(e.NewValue is true ? ExpandedEvent : CollapsedEvent, presenter));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _expander = GetTemplateChild("PART_Expander") as ToggleButton;
        if (_expander != null)
        {
            _expander.Checked += OnExpanderToggled;
            _expander.Unchecked += OnExpanderToggled;
        }
        SyncExpander();
        if (InitialExpansionTransition is { } expanding)
        {
            VisualStateManager.GoToState(this, expanding ? "Collapsed" : "Expanded", false);
            UpdateVisualStates(true);
            InitialExpansionTransition = null;
        }
        else UpdateVisualStates(false);
    }
    protected override void OnTemplateChanging()
    {
        if (_expander != null)
        {
            _expander.Checked -= OnExpanderToggled;
            _expander.Unchecked -= OnExpanderToggled;
        }
        _expander = null;
        base.OnTemplateChanging();
    }
    private void OnExpanderToggled(object sender, RoutedEventArgs e)
    {
        if (!_synchronizingExpander && IsEnabled && HasChildren)
            SetCurrentValue(IsExpandedProperty, _expander?.IsChecked == true);
        else if (!_synchronizingExpander) SyncExpander();
    }
    private void SyncExpander()
    {
        if (_expander == null) return;
        _synchronizingExpander = true;
        try { _expander.SetCurrentValue(ToggleButton.IsCheckedProperty, IsExpanded); }
        finally { _synchronizingExpander = false; }
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == HasChildrenProperty) UpdateVisualStates(false);
    }
    protected virtual void UpdateVisualStates(bool useTransitions)
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, HasChildren ? "Branch" : "Leaf", false);
        VisualStateManager.GoToState(this, IsExpanded ? "Expanded" : "Collapsed", useTransitions);
    }
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() =>
        new Jalium.UI.Automation.Peers.TreeDataGridCellPresenterAutomationPeer(this);
}
