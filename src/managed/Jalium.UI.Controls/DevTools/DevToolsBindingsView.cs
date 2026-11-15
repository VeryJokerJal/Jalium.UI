using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A template-driven binding overview and bounded event timeline.</summary>
[TemplatePart(Name = "PART_RecordButton", Type = typeof(Primitives.ButtonBase))]
[TemplatePart(Name = "PART_BindingsList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_EventsList", Type = typeof(ListBox))]
[TemplateVisualState(Name = "Idle", GroupName = "RecordingStates")]
[TemplateVisualState(Name = "Recording", GroupName = "RecordingStates")]
[TemplateVisualState(Name = "BindingsEmpty", GroupName = "BindingsStates")]
[TemplateVisualState(Name = "BindingsAvailable", GroupName = "BindingsStates")]
[TemplateVisualState(Name = "EventsEmpty", GroupName = "EventStates")]
[TemplateVisualState(Name = "EventsAvailable", GroupName = "EventStates")]
[TemplateVisualState(Name = "NoSelection", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsBindingsView : Control
{
    private NotifyCollectionChangedEventHandler? _bindingsChanged;
    private NotifyCollectionChangedEventHandler? _eventsChanged;
    static DevToolsBindingsView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsBindingsView), new PropertyMetadata(typeof(DevToolsBindingsView)));
    private static DependencyProperty Property(string name, Type type, object? value = null, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsBindingsView), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty BindingsSourceProperty = Property(nameof(BindingsSource), typeof(IEnumerable), null, OnSourceChanged);
    public static readonly DependencyProperty EventsSourceProperty = Property(nameof(EventsSource), typeof(IEnumerable), null, OnSourceChanged);
    public static readonly DependencyProperty SelectedElementNameProperty = Property(nameof(SelectedElementName), typeof(string), string.Empty, OnStateChanged);
    public static readonly DependencyProperty IsRecordingProperty = Property(nameof(IsRecording), typeof(bool), false, OnStateChanged);
    public static readonly DependencyProperty BindingItemTemplateProperty = Property(nameof(BindingItemTemplate), typeof(DataTemplate), null);
    public DataTemplate? BindingItemTemplate { get => (DataTemplate?)GetValue(BindingItemTemplateProperty)!; set => SetValue(BindingItemTemplateProperty, value); }
    public static readonly DependencyProperty EventItemTemplateProperty = Property(nameof(EventItemTemplate), typeof(DataTemplate), null);
    public DataTemplate? EventItemTemplate { get => (DataTemplate?)GetValue(EventItemTemplateProperty)!; set => SetValue(EventItemTemplateProperty, value); }
    public static readonly DependencyProperty EventCountProperty = Property(nameof(EventCount), typeof(int), 0);
    public int EventCount { get => (int)GetValue(EventCountProperty)!; set => SetValue(EventCountProperty, value); }
    public static readonly DependencyProperty ToggleRecordingCommandProperty = Property(nameof(ToggleRecordingCommand), typeof(ICommand), null);
    public ICommand? ToggleRecordingCommand { get => (ICommand?)GetValue(ToggleRecordingCommandProperty)!; set => SetValue(ToggleRecordingCommandProperty, value); }
    public static readonly DependencyProperty ResetCommandProperty = Property(nameof(ResetCommand), typeof(ICommand), null);
    public ICommand? ResetCommand { get => (ICommand?)GetValue(ResetCommandProperty)!; set => SetValue(ResetCommandProperty, value); }
    public static readonly DependencyProperty RevealTargetCommandProperty = Property(nameof(RevealTargetCommand), typeof(ICommand), null);
    public ICommand? RevealTargetCommand { get => (ICommand?)GetValue(RevealTargetCommandProperty)!; set => SetValue(RevealTargetCommandProperty, value); }
    public static readonly DependencyProperty RevealSourceCommandProperty = Property(nameof(RevealSourceCommand), typeof(ICommand), null);
    public ICommand? RevealSourceCommand { get => (ICommand?)GetValue(RevealSourceCommandProperty)!; set => SetValue(RevealSourceCommandProperty, value); }

    private static readonly DependencyPropertyKey HasBindingsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasBindings), typeof(bool), typeof(DevToolsBindingsView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasBindingsProperty = HasBindingsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasEventsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasEvents), typeof(bool), typeof(DevToolsBindingsView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasEventsProperty = HasEventsPropertyKey.DependencyProperty;
    public IEnumerable? BindingsSource { get => (IEnumerable?)GetValue(BindingsSourceProperty); set => SetValue(BindingsSourceProperty, value); }
    public IEnumerable? EventsSource { get => (IEnumerable?)GetValue(EventsSourceProperty); set => SetValue(EventsSourceProperty, value); }
    public string SelectedElementName { get => (string?)GetValue(SelectedElementNameProperty) ?? string.Empty; set => SetValue(SelectedElementNameProperty, value); }
    public bool IsRecording { get => (bool)GetValue(IsRecordingProperty)!; set => SetValue(IsRecordingProperty, value); }
    public bool HasBindings => (bool)GetValue(HasBindingsProperty)!;
    public bool HasEvents => (bool)GetValue(HasEventsProperty)!;
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsBindingsView)d;
        if (e.Property == BindingsSourceProperty) view.Observe(e.OldValue, e.NewValue, ref view._bindingsChanged);
        else view.Observe(e.OldValue, e.NewValue, ref view._eventsChanged);
        view.UpdateDataStates();
    }
    private void Observe(object? previous, object? source, ref NotifyCollectionChangedEventHandler? subscribed)
    {
        if (previous is INotifyCollectionChanged old && subscribed != null) old.CollectionChanged -= subscribed;
        subscribed = null;
        if (source is not INotifyCollectionChanged collection) return;
        var weak = new WeakReference<DevToolsBindingsView>(this);
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
        if (source is ICollection collection) return collection.Count > 0;
        if (source == null) return false;
        var iterator = source.GetEnumerator();
        try { return iterator.MoveNext(); }
        finally { (iterator as IDisposable)?.Dispose(); }
    }
    private void UpdateDataStates()
    {
        SetValue(HasBindingsPropertyKey, Any(BindingsSource));
        SetValue(HasEventsPropertyKey, Any(EventsSource));
        UpdateStates();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsBindingsView)d).UpdateStates();
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, IsRecording ? "Recording" : "Idle", false);
        VisualStateManager.GoToState(this, HasBindings ? "BindingsAvailable" : "BindingsEmpty", false);
        VisualStateManager.GoToState(this, HasEvents ? "EventsAvailable" : "EventsEmpty", false);
        VisualStateManager.GoToState(this, SelectedElementName.Length > 0 ? "Selected" : "NoSelection", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new BindingsPeer(this);
    private sealed class BindingsPeer(DevToolsBindingsView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsBindingsView);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Binding diagnostics";
    }
}

/// <summary>A reusable, responsive source-to-target binding card with real command endpoints.</summary>
[TemplatePart(Name = "PART_SourceButton", Type = typeof(Primitives.ButtonBase))]
[TemplatePart(Name = "PART_TargetButton", Type = typeof(Primitives.ButtonBase))]
[TemplateVisualState(Name = "Wide", GroupName = "AdaptiveStates")]
[TemplateVisualState(Name = "Compact", GroupName = "AdaptiveStates")]
[TemplateVisualState(Name = "Active", GroupName = "BindingStates")]
[TemplateVisualState(Name = "Pending", GroupName = "BindingStates")]
[TemplateVisualState(Name = "Inactive", GroupName = "BindingStates")]
[TemplateVisualState(Name = "Failed", GroupName = "BindingStates")]
[TemplateVisualState(Name = "DefaultMode", GroupName = "ModeStates")]
[TemplateVisualState(Name = "OneWayMode", GroupName = "ModeStates")]
[TemplateVisualState(Name = "TwoWayMode", GroupName = "ModeStates")]
[TemplateVisualState(Name = "OneTimeMode", GroupName = "ModeStates")]
[TemplateVisualState(Name = "OneWayToSourceMode", GroupName = "ModeStates")]
[TemplateVisualState(Name = "NoError", GroupName = "ErrorStates")]
[TemplateVisualState(Name = "Error", GroupName = "ErrorStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsBindingFlow : Control
{
    private Action<DependencyProperty, object?, object?>? _itemChanged;
    static DevToolsBindingFlow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsBindingFlow), new PropertyMetadata(typeof(DevToolsBindingFlow)));
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(DevToolsBindingItem), typeof(DevToolsBindingFlow), new PropertyMetadata(null, OnItemChanged));
    public static readonly DependencyProperty RevealTargetCommandProperty = DependencyProperty.Register(nameof(RevealTargetCommand), typeof(ICommand), typeof(DevToolsBindingFlow), new PropertyMetadata(null));
    public static readonly DependencyProperty RevealSourceCommandProperty = DependencyProperty.Register(nameof(RevealSourceCommand), typeof(ICommand), typeof(DevToolsBindingFlow), new PropertyMetadata(null));
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsBindingFlow), new PropertyMetadata(560.0, OnCompactWidthChanged), static value => value is double width && double.IsFinite(width) && width >= 0);
    private static readonly DependencyPropertyKey IsCompactPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsCompact), typeof(bool), typeof(DevToolsBindingFlow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;
    public DevToolsBindingItem? Item { get => (DevToolsBindingItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public ICommand? RevealTargetCommand { get => (ICommand?)GetValue(RevealTargetCommandProperty); set => SetValue(RevealTargetCommandProperty, value); }
    public ICommand? RevealSourceCommand { get => (ICommand?)GetValue(RevealSourceCommandProperty); set => SetValue(RevealSourceCommandProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public bool IsCompact => (bool)GetValue(IsCompactProperty)!;
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateCompact(); UpdateStates(); }
    protected internal override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); UpdateCompact(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }
    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var flow = (DevToolsBindingFlow)d;
        if (e.OldValue is DevToolsBindingItem old && flow._itemChanged != null) old.PropertyChangedInternal -= flow._itemChanged;
        flow._itemChanged = null;
        if (e.NewValue is DevToolsBindingItem item)
        {
            var weak = new WeakReference<DevToolsBindingFlow>(flow);
            Action<DependencyProperty, object?, object?>? handler = null;
            handler = (_, _, _) =>
            {
                if (weak.TryGetTarget(out var owner)) owner.UpdateStates();
                else item.PropertyChangedInternal -= handler;
            };
            flow._itemChanged = handler;
            item.PropertyChangedInternal += handler;
        }
        flow.UpdateStates();
    }
    private static void OnCompactWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsBindingFlow)d).UpdateCompact();
    private void UpdateCompact()
    {
        SetValue(IsCompactPropertyKey, ActualWidth > 0 && ActualWidth < CompactWidth);
        UpdateStates();
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        var status = Item?.Status ?? BindingStatus.Unattached;
        string state = Item?.HasValidationError == true ? "Failed" : status switch
        {
            BindingStatus.Active => "Active",
            BindingStatus.Unattached or BindingStatus.AsyncRequestPending => "Pending",
            BindingStatus.Inactive or BindingStatus.Detached => "Inactive",
            _ => "Failed"
        };
        VisualStateManager.GoToState(this, state, false);
        VisualStateManager.GoToState(this, (Item?.Mode ?? BindingMode.Default) + "Mode", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Item?.LastError) ? "NoError" : "Error", false);
        VisualStateManager.GoToState(this, IsCompact ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FlowPeer(this);
    private sealed class FlowPeer(DevToolsBindingFlow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsBindingFlow);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Item?.AutomationName ?? "Binding flow";
    }
}

/// <summary>Mutable binding-card data. Inspected source and target visuals are retained weakly.</summary>
public class DevToolsBindingItem : DependencyObject
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsBindingItem), new PropertyMetadata(value));
    public static readonly DependencyProperty TargetNameProperty = Property(nameof(TargetName), typeof(string), string.Empty);
    public string TargetName { get => (string)GetValue(TargetNameProperty)!; set => SetValue(TargetNameProperty, value); }
    public static readonly DependencyProperty TargetPropertyProperty = Property(nameof(TargetProperty), typeof(string), string.Empty);
    public string TargetProperty { get => (string)GetValue(TargetPropertyProperty)!; set => SetValue(TargetPropertyProperty, value); }
    public static readonly DependencyProperty SourceNameProperty = Property(nameof(SourceName), typeof(string), string.Empty);
    public string SourceName { get => (string)GetValue(SourceNameProperty)!; set => SetValue(SourceNameProperty, value); }
    public static readonly DependencyProperty SourcePathProperty = Property(nameof(SourcePath), typeof(string), string.Empty);
    public string SourcePath { get => (string)GetValue(SourcePathProperty)!; set => SetValue(SourcePathProperty, value); }
    public static readonly DependencyProperty SourceFooterProperty = Property(nameof(SourceFooter), typeof(string), "SOURCE");
    public string SourceFooter { get => (string)GetValue(SourceFooterProperty)!; set => SetValue(SourceFooterProperty, value); }
    public static readonly DependencyProperty ModeProperty = Property(nameof(Mode), typeof(BindingMode), BindingMode.Default);
    public BindingMode Mode { get => (BindingMode)GetValue(ModeProperty)!; set => SetValue(ModeProperty, value); }
    public static readonly DependencyProperty StatusProperty = Property(nameof(Status), typeof(BindingStatus), BindingStatus.Unattached);
    public BindingStatus Status { get => (BindingStatus)GetValue(StatusProperty)!; set => SetValue(StatusProperty, value); }
    public static readonly DependencyProperty HasValidationErrorProperty = Property(nameof(HasValidationError), typeof(bool), false);
    public bool HasValidationError { get => (bool)GetValue(HasValidationErrorProperty)!; set => SetValue(HasValidationErrorProperty, value); }
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(DevToolsBindingItem), new PropertyMetadata("Unattached"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    public static readonly DependencyProperty SourceActionNameProperty = Property(nameof(SourceActionName), typeof(string), "Reveal binding source");
    public string SourceActionName { get => (string)GetValue(SourceActionNameProperty)!; set => SetValue(SourceActionNameProperty, value); }
    public static readonly DependencyProperty TargetActionNameProperty = Property(nameof(TargetActionName), typeof(string), "Reveal binding target");
    public string TargetActionName { get => (string)GetValue(TargetActionNameProperty)!; set => SetValue(TargetActionNameProperty, value); }
    public static readonly DependencyProperty TargetCountTextProperty = Property(nameof(TargetCountText), typeof(string), "T 0");
    public string TargetCountText { get => (string)GetValue(TargetCountTextProperty)!; set => SetValue(TargetCountTextProperty, value); }
    public static readonly DependencyProperty SourceCountTextProperty = Property(nameof(SourceCountText), typeof(string), "S 0");
    public string SourceCountText { get => (string)GetValue(SourceCountTextProperty)!; set => SetValue(SourceCountTextProperty, value); }
    public static readonly DependencyProperty ErrorCountTextProperty = Property(nameof(ErrorCountText), typeof(string), "E 0");
    public string ErrorCountText { get => (string)GetValue(ErrorCountTextProperty)!; set => SetValue(ErrorCountTextProperty, value); }
    public static readonly DependencyProperty LastErrorProperty = Property(nameof(LastError), typeof(string), string.Empty);
    public string LastError { get => (string)GetValue(LastErrorProperty)!; set => SetValue(LastErrorProperty, value); }
    public static readonly DependencyProperty CanRevealTargetProperty = Property(nameof(CanRevealTarget), typeof(bool), false);
    public bool CanRevealTarget { get => (bool)GetValue(CanRevealTargetProperty)!; set => SetValue(CanRevealTargetProperty, value); }
    public static readonly DependencyProperty CanRevealSourceProperty = Property(nameof(CanRevealSource), typeof(bool), false);
    public bool CanRevealSource { get => (bool)GetValue(CanRevealSourceProperty)!; set => SetValue(CanRevealSourceProperty, value); }
    public static readonly DependencyProperty AutomationNameProperty = Property(nameof(AutomationName), typeof(string), "Binding flow");
    public string AutomationName { get => (string)GetValue(AutomationNameProperty)!; set => SetValue(AutomationNameProperty, value); }

    public WeakReference<Visual>? TargetReference { get; private set; }
    public WeakReference<Visual>? SourceReference { get; private set; }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == StatusProperty || e.Property == HasValidationErrorProperty)
            SetValue(StatusTextPropertyKey, HasValidationError ? "Validation error" : Status.ToString());
    }
    public void Update(FrameworkElement target, DependencyProperty property, BindingExpressionBase expression)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(property); ArgumentNullException.ThrowIfNull(expression);
        TargetReference = new WeakReference<Visual>(target);
        CanRevealTarget = true;
        TargetName = string.IsNullOrEmpty(target.Name) ? target.GetType().Name : $"{target.GetType().Name}  #{target.Name}";
        TargetProperty = property.Name;
        Status = expression.Status;
        HasValidationError = expression.HasValidationError;
        if (expression is BindingExpression binding)
        {
            var source = binding.ResolvedSource;
            SourceReference = source is Visual visual ? new WeakReference<Visual>(visual) : null;
            CanRevealSource = SourceReference != null;
            SourceName = source is FrameworkElement { Name: { Length: > 0 } sourceName }
                ? $"{source.GetType().Name}  #{sourceName}" : source?.GetType().Name ?? "<unresolved>";
            SourcePath = binding.ParentBinding?.Path?.Path is { Length: > 0 } path ? path : "(no path)";
            Mode = binding.ParentBinding?.Mode ?? BindingMode.Default;
            var trigger = binding.ParentBinding?.UpdateSourceTrigger ?? UpdateSourceTrigger.Default;
            string detail = binding.ParentBinding?.ElementName is { } name ? $"ElementName={name}"
                : binding.ParentBinding?.RelativeSource is { } relative ? $"RelativeSource={relative.Mode}" : string.Empty;
            SourceFooter = "SOURCE" + (trigger == UpdateSourceTrigger.Default ? "" : $" · {trigger}") + (detail.Length == 0 ? "" : $" · {detail}");
        }
        else
        {
            SourceReference = null; CanRevealSource = false;
            SourceName = expression.GetType().Name; SourcePath = "(composite binding)"; SourceFooter = "SOURCE";
            Mode = expression.ParentBindingBase is MultiBinding multi ? multi.Mode : BindingMode.Default;
        }
        var counters = BindingDiagnostics.GetCounters(target, property);
        TargetCountText = $"T {counters?.UpdateTargetCount ?? 0}";
        SourceCountText = $"S {counters?.UpdateSourceCount ?? 0}";
        ErrorCountText = $"E {counters?.ErrorCount ?? 0}";
        LastError = expression.ValidationError?.ErrorContent?.ToString() ?? counters?.LastError ?? string.Empty;
        SourceActionName = $"Reveal source: {SourceName}.{SourcePath}";
        TargetActionName = $"Reveal target: {TargetName}.{TargetProperty}";
        AutomationName = $"{TargetName}.{TargetProperty} · {SourceName}.{SourcePath} · {Mode} · {StatusText}";
    }
}

/// <summary>Presentation data for one binding event, including the original diagnostic message.</summary>
public class DevToolsBindingEventItem : DependencyObject
{
    private static DependencyProperty Property(string name) => DependencyProperty.Register(name, typeof(string), typeof(DevToolsBindingEventItem), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TimeTextProperty = Property(nameof(TimeText));
    public string TimeText { get => (string?)GetValue(TimeTextProperty) ?? string.Empty; set => SetValue(TimeTextProperty, value); }
    public static readonly DependencyProperty KindProperty = Property(nameof(Kind));
    public string Kind { get => (string?)GetValue(KindProperty) ?? string.Empty; set => SetValue(KindProperty, value); }
    public static readonly DependencyProperty TargetTextProperty = Property(nameof(TargetText));
    public string TargetText { get => (string?)GetValue(TargetTextProperty) ?? string.Empty; set => SetValue(TargetTextProperty, value); }
    public static readonly DependencyProperty SourceTextProperty = Property(nameof(SourceText));
    public string SourceText { get => (string?)GetValue(SourceTextProperty) ?? string.Empty; set => SetValue(SourceTextProperty, value); }
    public static readonly DependencyProperty MessageProperty = Property(nameof(Message));
    public string Message { get => (string?)GetValue(MessageProperty) ?? string.Empty; set => SetValue(MessageProperty, value); }
    public static readonly DependencyProperty AutomationNameProperty = Property(nameof(AutomationName));
    public string AutomationName { get => (string?)GetValue(AutomationNameProperty) ?? string.Empty; set => SetValue(AutomationNameProperty, value); }

    public void Update(BindingDiagnostics.BindingEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        TimeText = entry.Timestamp.ToString("HH:mm:ss.fff");
        Kind = entry.Kind.ToString();
        TargetText = $"{entry.TargetTypeName}.{entry.TargetPropertyName}";
        SourceText = entry.SourceDescription;
        Message = entry.Message ?? string.Empty;
        AutomationName = $"{Kind} · {TargetText} · {SourceText}" + (Message.Length == 0 ? "" : $" · {Message}");
    }
}

