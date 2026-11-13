using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A proportional strip whose segment templates select semantic colors.</summary>
public class DevToolsPerformanceBar : ItemsControl
{
    static DevToolsPerformanceBar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsPerformanceBar), new PropertyMetadata(typeof(DevToolsPerformanceBar)));
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (item is DevToolsPerformanceSegment segment) DevToolsProportionalPanel.SetWeight(element, segment.Value);
    }
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        base.ClearContainerForItem(element, item);
        if (item is DevToolsPerformanceSegment && !ReferenceEquals(element, item)) element.ClearValue(DevToolsProportionalPanel.WeightProperty);
    }
}

/// <summary>A responsive draw-call row that keeps narrow labels and metrics independently readable.</summary>
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Header", GroupName = "RowStates")]
[TemplateVisualState(Name = "Item", GroupName = "RowStates")]
public class DevToolsDrawApiRow : Control
{
    private double _layoutWidth;
    static DevToolsDrawApiRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsDrawApiRow), new PropertyMetadata(typeof(DevToolsDrawApiRow)));
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(DevToolsDrawApiItem), typeof(DevToolsDrawApiRow), new PropertyMetadata(null));
    public static readonly DependencyProperty IsHeaderProperty = DependencyProperty.Register(nameof(IsHeader), typeof(bool), typeof(DevToolsDrawApiRow), new PropertyMetadata(false, OnStateChanged));
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsDrawApiRow), new PropertyMetadata(520d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    public DevToolsDrawApiItem? Item { get => (DevToolsDrawApiItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool IsHeader { get => (bool)GetValue(IsHeaderProperty)!; set => SetValue(IsHeaderProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public DevToolsDrawApiRow() => SizeChanged += (_, e) => { _layoutWidth = e.NewSize.Width; UpdateStates(); };
    protected override Size MeasureOverride(Size availableSize) { _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth; UpdateStates(); return base.MeasureOverride(availableSize); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsDrawApiRow)d).UpdateStates();
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        double width = _layoutWidth > 0 ? _layoutWidth : ActualWidth;
        VisualStateManager.GoToState(this, width > 0 && width <= CompactWidth ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, IsHeader ? "Header" : "Item", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);
    private sealed class RowPeer(DevToolsDrawApiRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.IsHeader ? "Draw API columns" : owner.Item?.AutomationName ?? "Draw API row";
        protected override string GetClassNameCore() => nameof(DevToolsDrawApiRow);
    }
}

/// <summary>A reusable performance dashboard; its host supplies snapshots and rendering-engine commands.</summary>
[TemplatePart(Name = "PART_MetricsList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_ApiList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_MetricsRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_GapRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_ApiRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_MetricsColumn", Type = typeof(ColumnDefinition))]
[TemplatePart(Name = "PART_GapColumn", Type = typeof(ColumnDefinition))]
[TemplatePart(Name = "PART_ApiColumn", Type = typeof(ColumnDefinition))]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "MetricsAvailable", GroupName = "MetricStates")]
[TemplateVisualState(Name = "MetricsEmpty", GroupName = "MetricStates")]
[TemplateVisualState(Name = "ApiAvailable", GroupName = "ApiStates")]
[TemplateVisualState(Name = "ApiEmpty", GroupName = "ApiStates")]
[TemplateVisualState(Name = "Waiting", GroupName = "FrameStates")]
[TemplateVisualState(Name = "Idle", GroupName = "FrameStates")]
[TemplateVisualState(Name = "Active", GroupName = "FrameStates")]
[TemplateVisualState(Name = "Healthy", GroupName = "FrameStates")]
[TemplateVisualState(Name = "Slow", GroupName = "FrameStates")]
[TemplateVisualState(Name = "AutoEngine", GroupName = "EngineStates")]
[TemplateVisualState(Name = "VelloEngine", GroupName = "EngineStates")]
[TemplateVisualState(Name = "ImpellerEngine", GroupName = "EngineStates")]
[TemplateVisualState(Name = "Ready", GroupName = "SwitchStates")]
[TemplateVisualState(Name = "Switching", GroupName = "SwitchStates")]
[TemplateVisualState(Name = "NoError", GroupName = "EngineErrorStates")]
[TemplateVisualState(Name = "Error", GroupName = "EngineErrorStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsPerformanceView : Control
{
    private double _layoutWidth;
    private bool? _compactLayout;
    private RowDefinition[] _layoutRows = [];
    private ColumnDefinition[] _layoutColumns = [];
    private ListBox? _metricsList, _apiList;
    private readonly RelayCommand _autoCommand, _velloCommand, _impellerCommand;
    private EventHandler? _engineChanged;
    private static readonly DevToolsFrameSummary EmptySummary = new(false, false, 0, 0, 0, 0, 0, DevToolsPerformanceTone.Muted);
    static DevToolsPerformanceView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsPerformanceView), new PropertyMetadata(typeof(DevToolsPerformanceView)));
    private static DependencyProperty Property(string name, Type type, object? value, PropertyChangedCallback? changed = null) => DependencyProperty.Register(name, type, typeof(DevToolsPerformanceView), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty MetricsSourceProperty = Property(nameof(MetricsSource), typeof(IEnumerable<DevToolsPerformanceMetric>), null, OnSourceChanged);
    public static readonly DependencyProperty ApiSourceProperty = Property(nameof(ApiSource), typeof(IEnumerable<DevToolsDrawApiItem>), null, OnSourceChanged);
    public static readonly DependencyProperty SamplesSourceProperty = Property(nameof(SamplesSource), typeof(IReadOnlyList<FrameHistory.Sample>), null);
    public static readonly DependencyProperty MetricTemplateProperty = Property(nameof(MetricTemplate), typeof(DataTemplate), null);
    public static readonly DependencyProperty ApiTemplateProperty = Property(nameof(ApiTemplate), typeof(DataTemplate), null);
    public static readonly DependencyProperty FrameSummaryProperty = DependencyProperty.Register(nameof(FrameSummary), typeof(DevToolsFrameSummary), typeof(DevToolsPerformanceView), new PropertyMetadata(EmptySummary, OnStateChanged, (_, value) => value as DevToolsFrameSummary ?? EmptySummary));
    public static readonly DependencyProperty BackendTextProperty = Property(nameof(BackendText), typeof(string), "—");
    public static readonly DependencyProperty ActualEngineTextProperty = Property(nameof(ActualEngineText), typeof(string), "—");
    public static readonly DependencyProperty AdapterTextProperty = Property(nameof(AdapterText), typeof(string), "—");
    public static readonly DependencyProperty HasAdapterWarningProperty = Property(nameof(HasAdapterWarning), typeof(bool), false);
    public static readonly DependencyProperty ApiTimeTextProperty = Property(nameof(ApiTimeText), typeof(string), "—");
    public static readonly DependencyProperty ApiTotalTextProperty = Property(nameof(ApiTotalText), typeof(string), "—");
    public static readonly DependencyProperty IsEngineSwitchEnabledProperty = Property(nameof(IsEngineSwitchEnabled), typeof(bool), true, OnCommandStateChanged);
    public static readonly DependencyProperty EngineCommandProperty = Property(nameof(EngineCommand), typeof(ICommand), null, OnEngineCommandChanged);
    public static readonly DependencyProperty EngineErrorTextProperty = Property(nameof(EngineErrorText), typeof(string), "", OnStateChanged);
    public static readonly DependencyProperty SelectedEngineProperty = DependencyProperty.Register(nameof(SelectedEngine), typeof(RenderingEngine), typeof(DevToolsPerformanceView), new FrameworkPropertyMetadata(RenderingEngine.Auto, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCommandStateChanged), value => value is RenderingEngine engine && Enum.IsDefined(engine));
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsPerformanceView), new PropertyMetadata(780d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    public static readonly DependencyProperty FrameBudgetMsProperty = DependencyProperty.Register(nameof(FrameBudgetMs), typeof(double), typeof(DevToolsPerformanceView), new PropertyMetadata(16.0), value => value is double time && double.IsFinite(time) && time > 0);
    private static DependencyPropertyKey ReadOnly(string name, Type type, object? value) => DependencyProperty.RegisterReadOnly(name, type, typeof(DevToolsPerformanceView), new PropertyMetadata(value));
    private static readonly DependencyPropertyKey HasMetricsPropertyKey = ReadOnly(nameof(HasMetrics), typeof(bool), false);
    public static readonly DependencyProperty HasMetricsProperty = HasMetricsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasApiItemsPropertyKey = ReadOnly(nameof(HasApiItems), typeof(bool), false);
    public static readonly DependencyProperty HasApiItemsProperty = HasApiItemsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsSwitchingPropertyKey = ReadOnly(nameof(IsSwitching), typeof(bool), false);
    public static readonly DependencyProperty IsSwitchingProperty = IsSwitchingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ApiViewportWidthPropertyKey = ReadOnly(nameof(ApiViewportWidth), typeof(double), double.NaN);
    public static readonly DependencyProperty ApiViewportWidthProperty = ApiViewportWidthPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey AutoEngineCommandPropertyKey = ReadOnly(nameof(AutoEngineCommand), typeof(ICommand), null);
    public static readonly DependencyProperty AutoEngineCommandProperty = AutoEngineCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey VelloEngineCommandPropertyKey = ReadOnly(nameof(VelloEngineCommand), typeof(ICommand), null);
    public static readonly DependencyProperty VelloEngineCommandProperty = VelloEngineCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ImpellerEngineCommandPropertyKey = ReadOnly(nameof(ImpellerEngineCommand), typeof(ICommand), null);
    public static readonly DependencyProperty ImpellerEngineCommandProperty = ImpellerEngineCommandPropertyKey.DependencyProperty;

    public IEnumerable<DevToolsPerformanceMetric>? MetricsSource { get => (IEnumerable<DevToolsPerformanceMetric>?)GetValue(MetricsSourceProperty); set => SetValue(MetricsSourceProperty, value); }
    public IEnumerable<DevToolsDrawApiItem>? ApiSource { get => (IEnumerable<DevToolsDrawApiItem>?)GetValue(ApiSourceProperty); set => SetValue(ApiSourceProperty, value); }
    public IReadOnlyList<FrameHistory.Sample>? SamplesSource { get => (IReadOnlyList<FrameHistory.Sample>?)GetValue(SamplesSourceProperty); set => SetValue(SamplesSourceProperty, value); }
    public DataTemplate? MetricTemplate { get => (DataTemplate?)GetValue(MetricTemplateProperty); set => SetValue(MetricTemplateProperty, value); }
    public DataTemplate? ApiTemplate { get => (DataTemplate?)GetValue(ApiTemplateProperty); set => SetValue(ApiTemplateProperty, value); }
    public DevToolsFrameSummary FrameSummary { get => (DevToolsFrameSummary)GetValue(FrameSummaryProperty)!; set => SetValue(FrameSummaryProperty, value); }
    public string BackendText { get => (string?)GetValue(BackendTextProperty) ?? ""; set => SetValue(BackendTextProperty, value); }
    public string ActualEngineText { get => (string?)GetValue(ActualEngineTextProperty) ?? ""; set => SetValue(ActualEngineTextProperty, value); }
    public string AdapterText { get => (string?)GetValue(AdapterTextProperty) ?? ""; set => SetValue(AdapterTextProperty, value); }
    public bool HasAdapterWarning { get => (bool)GetValue(HasAdapterWarningProperty)!; set => SetValue(HasAdapterWarningProperty, value); }
    public string ApiTimeText { get => (string?)GetValue(ApiTimeTextProperty) ?? ""; set => SetValue(ApiTimeTextProperty, value); }
    public string ApiTotalText { get => (string?)GetValue(ApiTotalTextProperty) ?? ""; set => SetValue(ApiTotalTextProperty, value); }
    public bool IsEngineSwitchEnabled { get => (bool)GetValue(IsEngineSwitchEnabledProperty)!; set => SetValue(IsEngineSwitchEnabledProperty, value); }
    public ICommand? EngineCommand { get => (ICommand?)GetValue(EngineCommandProperty); set => SetValue(EngineCommandProperty, value); }
    public string EngineErrorText { get => (string?)GetValue(EngineErrorTextProperty) ?? ""; set => SetValue(EngineErrorTextProperty, value); }
    public RenderingEngine SelectedEngine { get => (RenderingEngine)GetValue(SelectedEngineProperty)!; set => SetValue(SelectedEngineProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public double FrameBudgetMs { get => (double)GetValue(FrameBudgetMsProperty)!; set => SetValue(FrameBudgetMsProperty, value); }
    public bool HasMetrics => (bool)GetValue(HasMetricsProperty)!;
    public bool HasApiItems => (bool)GetValue(HasApiItemsProperty)!;
    public bool IsSwitching => (bool)GetValue(IsSwitchingProperty)!;
    /// <summary>The API item viewport width used by a consumer's pinned column header.</summary>
    public double ApiViewportWidth => (double)GetValue(ApiViewportWidthProperty)!;
    public ICommand AutoEngineCommand => (ICommand)GetValue(AutoEngineCommandProperty)!;
    public ICommand VelloEngineCommand => (ICommand)GetValue(VelloEngineCommandProperty)!;
    public ICommand ImpellerEngineCommand => (ICommand)GetValue(ImpellerEngineCommandProperty)!;

    public DevToolsPerformanceView()
    {
        _autoCommand = new RelayCommand(() => RequestEngine(RenderingEngine.Auto), () => CanRequestEngine(RenderingEngine.Auto));
        _velloCommand = new RelayCommand(() => RequestEngine(RenderingEngine.Vello), () => CanRequestEngine(RenderingEngine.Vello));
        _impellerCommand = new RelayCommand(() => RequestEngine(RenderingEngine.Impeller), () => CanRequestEngine(RenderingEngine.Impeller));
        SetValue(AutoEngineCommandPropertyKey, _autoCommand); SetValue(VelloEngineCommandPropertyKey, _velloCommand); SetValue(ImpellerEngineCommandPropertyKey, _impellerCommand);
        SizeChanged += (_, e) => { _layoutWidth = e.NewSize.Width; UpdateStates(); };
    }
    public bool CanRequestEngine(RenderingEngine engine) => Enum.IsDefined(engine) && IsEnabled && IsEngineSwitchEnabled && !IsSwitching && (EngineCommand?.CanExecute(engine) ?? true);
    public bool RequestEngine(RenderingEngine engine)
    {
        if (!CanRequestEngine(engine)) return false;
        var previous = SelectedEngine; SetValue(IsSwitchingPropertyKey, true); SetCurrentValue(EngineErrorTextProperty, ""); SetCurrentValue(SelectedEngineProperty, engine); UpdateCommands();
        try { EngineCommand?.Execute(engine); return true; }
        catch (Exception error) { SetCurrentValue(SelectedEngineProperty, previous); SetCurrentValue(EngineErrorTextProperty, error.Message); return false; }
        finally { SetValue(IsSwitchingPropertyKey, false); UpdateCommands(); }
    }
    protected override Size MeasureOverride(Size availableSize) { _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth; UpdateStates(); return base.MeasureOverride(availableSize); }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        // Classic bars consume a gutter; overlay bars do not. Use the realized
        // item viewport rather than guessing the platform's scrollbar width.
        double width = _apiList?.ItemsHostInternal?.ActualWidth ?? 0;
        if (width <= 0) width = (_apiList?.VisualParent as FrameworkElement)?.ActualWidth ?? 0;
        SetValue(ApiViewportWidthPropertyKey, width > 0 && double.IsFinite(width) ? width : double.NaN);
        return result;
    }
    public override void OnApplyTemplate()
    {
        DetachParts(); base.OnApplyTemplate();
        _metricsList = GetTemplateChild("PART_MetricsList") as ListBox; _apiList = GetTemplateChild("PART_ApiList") as ListBox;
        _layoutRows = new[] { GetTemplateChild("PART_MetricsRow"), GetTemplateChild("PART_GapRow"), GetTemplateChild("PART_ApiRow") }.OfType<RowDefinition>().ToArray();
        _layoutColumns = new[] { GetTemplateChild("PART_MetricsColumn"), GetTemplateChild("PART_GapColumn"), GetTemplateChild("PART_ApiColumn") }.OfType<ColumnDefinition>().ToArray();
        UpdateDataStates(); UpdateCommands();
    }
    protected override void OnTemplateChanging() { DetachParts(); base.OnTemplateChanging(); }
    private void DetachParts()
    {
        foreach (var list in new[] { _metricsList, _apiList }) if (list != null) { BindingOperations.ClearBinding(list, ItemsControl.ItemsSourceProperty); list.ItemsSource = null; }
        _metricsList = _apiList = null; _layoutRows = []; _layoutColumns = []; _compactLayout = null;
        SetValue(ApiViewportWidthPropertyKey, double.NaN);
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateDataStates();
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsPerformanceView)d;
        if (e.OldValue is INotifyCollectionChanged old) CollectionChangedEventManager.RemoveHandler(old, view.OnCollectionChanged);
        if (e.NewValue is INotifyCollectionChanged current) CollectionChangedEventManager.AddHandler(current, view.OnCollectionChanged);
        view.UpdateDataStates();
    }
    private void UpdateDataStates() { SetValue(HasMetricsPropertyKey, MetricsSource?.Any() == true); SetValue(HasApiItemsPropertyKey, ApiSource?.Any() == true); UpdateStates(); }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsPerformanceView)d).UpdateStates();
    private static void OnCommandStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsPerformanceView)d).UpdateCommands();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == IsEnabledProperty) UpdateCommands(); }
    private static void OnEngineCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsPerformanceView)d;
        if (e.OldValue is ICommand old && view._engineChanged != null) old.CanExecuteChanged -= view._engineChanged;
        view._engineChanged = null;
        if (e.NewValue is ICommand current)
        {
            var weak = new WeakReference<DevToolsPerformanceView>(view); EventHandler? handler = null;
            handler = (sender, _) => { if (weak.TryGetTarget(out var owner)) owner.UpdateCommands(); else if (sender is ICommand retired) retired.CanExecuteChanged -= handler; };
            current.CanExecuteChanged += handler; view._engineChanged = handler;
        }
        view.UpdateCommands();
    }
    private void UpdateCommands() { _autoCommand?.RaiseCanExecuteChanged(); _velloCommand?.RaiseCanExecuteChanged(); _impellerCommand?.RaiseCanExecuteChanged(); UpdateStates(); }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, HasMetrics ? "MetricsAvailable" : "MetricsEmpty", false);
        VisualStateManager.GoToState(this, HasApiItems ? "ApiAvailable" : "ApiEmpty", false);
        var summary = FrameSummary;
        VisualStateManager.GoToState(this, !summary.HasFrames ? "Waiting" : summary.IsIdle ? "Idle" : summary.Tone == DevToolsPerformanceTone.Warning ? "Slow" : summary.Tone == DevToolsPerformanceTone.Success ? "Healthy" : "Active", false);
        VisualStateManager.GoToState(this, SelectedEngine + "Engine", false);
        VisualStateManager.GoToState(this, IsSwitching ? "Switching" : "Ready", false);
        VisualStateManager.GoToState(this, EngineErrorText.Length > 0 ? "Error" : "NoError", false);
        double width = _layoutWidth > 0 ? _layoutWidth : ActualWidth; bool compact = width > 0 && width <= CompactWidth;
        if (_compactLayout is { } previous && previous != compact)
        {
            _compactLayout = compact;
            foreach (var row in _layoutRows) if (BindingOperations.GetBindingExpression(row, RowDefinition.HeightProperty) == null) row.ClearValue(RowDefinition.HeightProperty);
            foreach (var column in _layoutColumns) if (BindingOperations.GetBindingExpression(column, ColumnDefinition.WidthProperty) == null) column.ClearValue(ColumnDefinition.WidthProperty);
        }
        _compactLayout = compact; VisualStateManager.GoToState(this, compact ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ViewPeer(this);
    private sealed class ViewPeer(DevToolsPerformanceView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Rendering performance";
        protected override string GetClassNameCore() => nameof(DevToolsPerformanceView);
    }
}
