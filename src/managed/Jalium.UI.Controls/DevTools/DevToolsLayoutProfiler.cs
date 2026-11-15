using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Controls.DevTools;

/// <summary>The primary layout cost highlighted by the profiler.</summary>
public enum DevToolsLayoutSortMode { Measure, Arrange, Invalidations }

/// <summary>A reusable layout-profiler view. Its host supplies snapshots and commands.</summary>
[TemplatePart(Name = "PART_RecordButton", Type = typeof(Primitives.ButtonBase))]
[TemplatePart(Name = "PART_StatsList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_InvalidationList", Type = typeof(ListBox))]
[TemplateVisualState(Name = "Idle", GroupName = "RecordingStates")]
[TemplateVisualState(Name = "Recording", GroupName = "RecordingStates")]
[TemplateVisualState(Name = "StatsEmpty", GroupName = "StatsStates")]
[TemplateVisualState(Name = "StatsAvailable", GroupName = "StatsStates")]
[TemplateVisualState(Name = "InvalidationsEmpty", GroupName = "InvalidationStates")]
[TemplateVisualState(Name = "InvalidationsAvailable", GroupName = "InvalidationStates")]
[TemplateVisualState(Name = "MeasureSort", GroupName = "SortStates")]
[TemplateVisualState(Name = "ArrangeSort", GroupName = "SortStates")]
[TemplateVisualState(Name = "InvalidationsSort", GroupName = "SortStates")]
[TemplateVisualState(Name = "FooterEmpty", GroupName = "FooterStates")]
[TemplateVisualState(Name = "FooterAvailable", GroupName = "FooterStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsLayoutProfiler : Control
{
    private NotifyCollectionChangedEventHandler? _statsChanged;
    private NotifyCollectionChangedEventHandler? _invalidationsChanged;
    static DevToolsLayoutProfiler() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsLayoutProfiler), new PropertyMetadata(typeof(DevToolsLayoutProfiler)));
    private static DependencyProperty Property(string name, Type type, object? value = null, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsLayoutProfiler), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty StatsSourceProperty = Property(nameof(StatsSource), typeof(IEnumerable), null, OnSourceChanged);
    public static readonly DependencyProperty InvalidationSourceProperty = Property(nameof(InvalidationSource), typeof(IEnumerable), null, OnSourceChanged);
    public static readonly DependencyProperty StatsItemTemplateProperty = Property(nameof(StatsItemTemplate), typeof(DataTemplate), null, null);
    public static readonly DependencyProperty InvalidationItemTemplateProperty = Property(nameof(InvalidationItemTemplate), typeof(DataTemplate), null, null);
    public static readonly DependencyProperty IsRecordingProperty = Property(nameof(IsRecording), typeof(bool), false, OnStateChanged);
    public static readonly DependencyProperty IsStackCaptureEnabledProperty = Property(nameof(IsStackCaptureEnabled), typeof(bool), false, null);
    public static readonly DependencyProperty FooterTextProperty = Property(nameof(FooterText), typeof(string), string.Empty, OnStateChanged);
    public static readonly DependencyProperty InvalidationCountProperty = Property(nameof(InvalidationCount), typeof(int), 0, null);
    public static readonly DependencyProperty ToggleRecordingCommandProperty = Property(nameof(ToggleRecordingCommand), typeof(ICommand), null, null);
    public static readonly DependencyProperty ResetCommandProperty = Property(nameof(ResetCommand), typeof(ICommand), null, null);
    public static readonly DependencyProperty ToggleStackCaptureCommandProperty = Property(nameof(ToggleStackCaptureCommand), typeof(ICommand), null, null);
    public static readonly DependencyProperty CycleSortCommandProperty = Property(nameof(CycleSortCommand), typeof(ICommand), null, null);
    public static readonly DependencyProperty RevealCommandProperty = Property(nameof(RevealCommand), typeof(ICommand), null, null);
    public static readonly DependencyProperty SortModeProperty = DependencyProperty.Register(nameof(SortMode), typeof(DevToolsLayoutSortMode), typeof(DevToolsLayoutProfiler),
        new FrameworkPropertyMetadata(DevToolsLayoutSortMode.Measure, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSortModeChanged),
        static value => value is DevToolsLayoutSortMode mode && Enum.IsDefined(mode));
    private static readonly DependencyPropertyKey HasStatsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasStats), typeof(bool), typeof(DevToolsLayoutProfiler), new PropertyMetadata(false));
    public static readonly DependencyProperty HasStatsProperty = HasStatsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasInvalidationsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasInvalidations), typeof(bool), typeof(DevToolsLayoutProfiler), new PropertyMetadata(false));
    public static readonly DependencyProperty HasInvalidationsProperty = HasInvalidationsPropertyKey.DependencyProperty;
    public static readonly RoutedEvent SortModeChangedEvent = EventManager.RegisterRoutedEvent(nameof(SortModeChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsLayoutProfiler));
    public IEnumerable? StatsSource { get => (IEnumerable?)GetValue(StatsSourceProperty); set => SetValue(StatsSourceProperty, value); }
    public IEnumerable? InvalidationSource { get => (IEnumerable?)GetValue(InvalidationSourceProperty); set => SetValue(InvalidationSourceProperty, value); }
    public DataTemplate? StatsItemTemplate { get => (DataTemplate?)GetValue(StatsItemTemplateProperty); set => SetValue(StatsItemTemplateProperty, value); }
    public DataTemplate? InvalidationItemTemplate { get => (DataTemplate?)GetValue(InvalidationItemTemplateProperty); set => SetValue(InvalidationItemTemplateProperty, value); }
    public bool IsRecording { get => (bool)GetValue(IsRecordingProperty)!; set => SetValue(IsRecordingProperty, value); }
    public bool IsStackCaptureEnabled { get => (bool)GetValue(IsStackCaptureEnabledProperty)!; set => SetValue(IsStackCaptureEnabledProperty, value); }
    public string FooterText { get => (string?)GetValue(FooterTextProperty) ?? string.Empty; set => SetValue(FooterTextProperty, value); }
    public int InvalidationCount { get => (int)GetValue(InvalidationCountProperty)!; set => SetValue(InvalidationCountProperty, value); }
    public ICommand? ToggleRecordingCommand { get => (ICommand?)GetValue(ToggleRecordingCommandProperty); set => SetValue(ToggleRecordingCommandProperty, value); }
    public ICommand? ResetCommand { get => (ICommand?)GetValue(ResetCommandProperty); set => SetValue(ResetCommandProperty, value); }
    public ICommand? ToggleStackCaptureCommand { get => (ICommand?)GetValue(ToggleStackCaptureCommandProperty); set => SetValue(ToggleStackCaptureCommandProperty, value); }
    public ICommand? CycleSortCommand { get => (ICommand?)GetValue(CycleSortCommandProperty); set => SetValue(CycleSortCommandProperty, value); }
    public ICommand? RevealCommand { get => (ICommand?)GetValue(RevealCommandProperty); set => SetValue(RevealCommandProperty, value); }
    public DevToolsLayoutSortMode SortMode { get => (DevToolsLayoutSortMode)GetValue(SortModeProperty)!; set => SetValue(SortModeProperty, value); }
    public bool HasStats => (bool)GetValue(HasStatsProperty)!;
    public bool HasInvalidations => (bool)GetValue(HasInvalidationsProperty)!;
    public event RoutedEventHandler SortModeChanged { add => AddHandler(SortModeChangedEvent, value); remove => RemoveHandler(SortModeChangedEvent, value); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsLayoutProfiler)d;
        if (e.Property == StatsSourceProperty) view.Observe(e.OldValue, e.NewValue, ref view._statsChanged);
        else view.Observe(e.OldValue, e.NewValue, ref view._invalidationsChanged);
        view.UpdateDataStates();
    }
    private void Observe(object? oldSource, object? newSource, ref NotifyCollectionChangedEventHandler? subscribed)
    {
        if (oldSource is INotifyCollectionChanged oldCollection && subscribed != null) oldCollection.CollectionChanged -= subscribed;
        subscribed = null;
        if (newSource is not INotifyCollectionChanged collection) return;
        // A shared diagnostics source must not retain a discarded view.
        var weak = new WeakReference<DevToolsLayoutProfiler>(this);
        NotifyCollectionChangedEventHandler? handler = null;
        handler = (sender, _) =>
        {
            if (weak.TryGetTarget(out var view)) view.UpdateDataStates();
            else if (sender is INotifyCollectionChanged retired) retired.CollectionChanged -= handler;
        };
        collection.CollectionChanged += handler;
        subscribed = handler;
    }
    private static bool Any(IEnumerable? source)
    {
        if (source is ICollection collection) return collection.Count != 0;
        if (source == null) return false;
        var enumerator = source.GetEnumerator();
        try { return enumerator.MoveNext(); }
        finally { (enumerator as IDisposable)?.Dispose(); }
    }
    private void UpdateDataStates()
    {
        SetValue(HasStatsPropertyKey, Any(StatsSource));
        SetValue(HasInvalidationsPropertyKey, Any(InvalidationSource));
        UpdateStates();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsLayoutProfiler)d).UpdateStates();
    private static void OnSortModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsLayoutProfiler)d;
        view.UpdateStates();
        view.RaiseEvent(new RoutedEventArgs(SortModeChangedEvent, view));
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, IsRecording ? "Recording" : "Idle", false);
        VisualStateManager.GoToState(this, HasStats ? "StatsAvailable" : "StatsEmpty", false);
        VisualStateManager.GoToState(this, HasInvalidations ? "InvalidationsAvailable" : "InvalidationsEmpty", false);
        VisualStateManager.GoToState(this, SortMode + "Sort", false);
        VisualStateManager.GoToState(this, FooterText.Length > 0 ? "FooterAvailable" : "FooterEmpty", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ProfilerPeer(this);
    private sealed class ProfilerPeer(DevToolsLayoutProfiler owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsLayoutProfiler);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Layout profiler";
    }
}

/// <summary>Mutable presentation data for one layout-cost row, with a weak target reference.</summary>
public class DevToolsLayoutStatsItem : DependencyObject
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsLayoutStatsItem), new PropertyMetadata(value));
    public static readonly DependencyProperty RankProperty = Property(nameof(Rank), typeof(int), 0);
    public static readonly DependencyProperty ElementNameProperty = Property(nameof(ElementName), typeof(string), string.Empty);
    public static readonly DependencyProperty MeasureTextProperty = Property(nameof(MeasureText), typeof(string), string.Empty);
    public static readonly DependencyProperty ArrangeTextProperty = Property(nameof(ArrangeText), typeof(string), string.Empty);
    public static readonly DependencyProperty InvalidationTextProperty = Property(nameof(InvalidationText), typeof(string), string.Empty);
    public static readonly DependencyProperty MeasureFractionProperty = Property(nameof(MeasureFraction), typeof(double), 0.0);
    public static readonly DependencyProperty ArrangeFractionProperty = Property(nameof(ArrangeFraction), typeof(double), 0.0);
    public static readonly DependencyProperty MeasureShareProperty = Property(nameof(MeasureShare), typeof(double), 0.0);
    public static readonly DependencyProperty ArrangeShareProperty = Property(nameof(ArrangeShare), typeof(double), 0.0);
    public static readonly DependencyProperty VisualShareProperty = Property(nameof(VisualShare), typeof(double), 0.0);
    public static readonly DependencyProperty TrailingShareProperty = Property(nameof(TrailingShare), typeof(double), 1.0);
    public static readonly DependencyProperty IsMeasureHighlightedProperty = Property(nameof(IsMeasureHighlighted), typeof(bool), true);
    public static readonly DependencyProperty IsArrangeHighlightedProperty = Property(nameof(IsArrangeHighlighted), typeof(bool), false);
    public static readonly DependencyProperty IsInvalidationHighlightedProperty = Property(nameof(IsInvalidationHighlighted), typeof(bool), false);
    public int Rank { get => (int)GetValue(RankProperty)!; set => SetValue(RankProperty, value); }
    public string ElementName { get => (string?)GetValue(ElementNameProperty) ?? string.Empty; set => SetValue(ElementNameProperty, value); }
    public string MeasureText { get => (string?)GetValue(MeasureTextProperty) ?? string.Empty; set => SetValue(MeasureTextProperty, value); }
    public string ArrangeText { get => (string?)GetValue(ArrangeTextProperty) ?? string.Empty; set => SetValue(ArrangeTextProperty, value); }
    public string InvalidationText { get => (string?)GetValue(InvalidationTextProperty) ?? string.Empty; set => SetValue(InvalidationTextProperty, value); }
    public double MeasureFraction { get => (double)GetValue(MeasureFractionProperty)!; set => SetValue(MeasureFractionProperty, value); }
    public double ArrangeFraction { get => (double)GetValue(ArrangeFractionProperty)!; set => SetValue(ArrangeFractionProperty, value); }
    public double MeasureShare { get => (double)GetValue(MeasureShareProperty)!; set => SetValue(MeasureShareProperty, value); }
    public double ArrangeShare { get => (double)GetValue(ArrangeShareProperty)!; set => SetValue(ArrangeShareProperty, value); }
    public double VisualShare { get => (double)GetValue(VisualShareProperty)!; set => SetValue(VisualShareProperty, value); }
    public double TrailingShare { get => (double)GetValue(TrailingShareProperty)!; set => SetValue(TrailingShareProperty, value); }
    public bool IsMeasureHighlighted { get => (bool)GetValue(IsMeasureHighlightedProperty)!; set => SetValue(IsMeasureHighlightedProperty, value); }
    public bool IsArrangeHighlighted { get => (bool)GetValue(IsArrangeHighlightedProperty)!; set => SetValue(IsArrangeHighlightedProperty, value); }
    public bool IsInvalidationHighlighted { get => (bool)GetValue(IsInvalidationHighlightedProperty)!; set => SetValue(IsInvalidationHighlightedProperty, value); }
    public LayoutDiagnostics.ElementStats? Source { get; private set; }
    public void Update(LayoutDiagnostics.ElementStats source, int rank, DevToolsLayoutSortMode mode, double maximum)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Rank = rank;
        ElementName = string.IsNullOrEmpty(source.ElementName) ? source.TypeName : $"{source.TypeName}  #{source.ElementName}";
        MeasureText = MetricText(source.MeasureCount, source.MeasureTotalMicros, source.MeasureAverageMicros, source.MeasureWorstMicros);
        ArrangeText = MetricText(source.ArrangeCount, source.ArrangeTotalMicros, source.ArrangeAverageMicros, source.ArrangeWorstMicros);
        MeasureFraction = VisibleFraction(Normalize(source.MeasureTotalMicros, maximum));
        ArrangeFraction = VisibleFraction(Normalize(source.ArrangeTotalMicros, maximum));
        IsMeasureHighlighted = mode == DevToolsLayoutSortMode.Measure;
        IsArrangeHighlighted = mode == DevToolsLayoutSortMode.Arrange;
        IsInvalidationHighlighted = mode == DevToolsLayoutSortMode.Invalidations;
        double total = (double)source.InvalidateMeasureCount + source.InvalidateArrangeCount + source.InvalidateVisualCount;
        double fill = mode == DevToolsLayoutSortMode.Invalidations ? Normalize(total, maximum) : 1;
        if (fill <= 0) fill = 1;
        MeasureShare = total <= 0 ? 0 : source.InvalidateMeasureCount / total * fill;
        ArrangeShare = total <= 0 ? 0 : source.InvalidateArrangeCount / total * fill;
        VisualShare = total <= 0 ? 0 : source.InvalidateVisualCount / total * fill;
        TrailingShare = Math.Max(0, 1 - MeasureShare - ArrangeShare - VisualShare);
        InvalidationText = $"M {source.InvalidateMeasureCount}   A {source.InvalidateArrangeCount}   V {source.InvalidateVisualCount}";
    }
    private static double Normalize(double value, double maximum) => double.IsFinite(value) && double.IsFinite(maximum) && maximum > 0 ? Math.Clamp(value / maximum, 0, 1) : 0;
    private static double VisibleFraction(double value) => value > 0 && value < .02 ? .02 : value;
    private static string MetricText(int count, double total, double average, double worst) => count == 0 ? "0 \u00D7   \u2014" : $"{count} \u00D7   tot {total:F0} \u00B5s   avg {average:F1}   worst {worst:F1}";
}

/// <summary>Mutable presentation data for one invalidation-timeline row.</summary>
public class DevToolsLayoutInvalidationItem : DependencyObject
{
    private static DependencyProperty Property(string name) => DependencyProperty.Register(name, typeof(string), typeof(DevToolsLayoutInvalidationItem), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TimeTextProperty = Property(nameof(TimeText));
    public string TimeText { get => (string?)GetValue(TimeTextProperty) ?? string.Empty; set => SetValue(TimeTextProperty, value); }
    public static readonly DependencyProperty KindProperty = Property(nameof(Kind));
    public string Kind { get => (string?)GetValue(KindProperty) ?? string.Empty; set => SetValue(KindProperty, value); }
    public static readonly DependencyProperty ElementNameProperty = Property(nameof(ElementName));
    public string ElementName { get => (string?)GetValue(ElementNameProperty) ?? string.Empty; set => SetValue(ElementNameProperty, value); }
    public static readonly DependencyProperty SourceTextProperty = Property(nameof(SourceText));
    public string SourceText { get => (string?)GetValue(SourceTextProperty) ?? string.Empty; set => SetValue(SourceTextProperty, value); }
    public void Update(LayoutDiagnostics.InvalidationEntry source)
    {
        ArgumentNullException.ThrowIfNull(source);
        TimeText = source.Timestamp.ToString("HH:mm:ss.fff");
        Kind = source.Kind.ToString();
        ElementName = string.IsNullOrEmpty(source.ElementName) ? source.TypeName : $"{source.TypeName}  #{source.ElementName}";
        SourceText = source.StackSummary ?? string.Empty;
    }
}
