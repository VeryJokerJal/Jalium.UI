using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A reusable routed-event timeline with replaceable route and node templates.</summary>
[TemplatePart(Name = "PART_FilterTextBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_EventList", Type = typeof(ListBox))]
[TemplateVisualState(Name = "Idle", GroupName = "RecordingStates")]
[TemplateVisualState(Name = "Recording", GroupName = "RecordingStates")]
[TemplateVisualState(Name = "EventsEmpty", GroupName = "EventStates")]
[TemplateVisualState(Name = "EventsAvailable", GroupName = "EventStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsEventsView : Control
{
    private TextBox? _filterBox;
    private NotifyCollectionChangedEventHandler? _sourceChanged;
    static DevToolsEventsView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsEventsView), new PropertyMetadata(typeof(DevToolsEventsView)));
    private static DependencyProperty Property(string name, Type type, object? value = null, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsEventsView), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty EventsSourceProperty = Property(nameof(EventsSource), typeof(IEnumerable), null, OnSourceChanged);
    public static readonly DependencyProperty IsRecordingProperty = Property(nameof(IsRecording), typeof(bool), false, OnStateChanged);
    public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(nameof(FilterText), typeof(string), typeof(DevToolsEventsView), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty EventItemTemplateProperty = Property(nameof(EventItemTemplate), typeof(DataTemplate), null);
    public DataTemplate? EventItemTemplate { get => (DataTemplate?)GetValue(EventItemTemplateProperty)!; set => SetValue(EventItemTemplateProperty, value); }
    public static readonly DependencyProperty NodeItemTemplateProperty = Property(nameof(NodeItemTemplate), typeof(DataTemplate), null);
    public DataTemplate? NodeItemTemplate { get => (DataTemplate?)GetValue(NodeItemTemplateProperty)!; set => SetValue(NodeItemTemplateProperty, value); }
    public static readonly DependencyProperty EventCountProperty = Property(nameof(EventCount), typeof(int), 0);
    public int EventCount { get => (int)GetValue(EventCountProperty)!; set => SetValue(EventCountProperty, value); }
    public static readonly DependencyProperty ToggleRecordingCommandProperty = Property(nameof(ToggleRecordingCommand), typeof(ICommand), null);
    public ICommand? ToggleRecordingCommand { get => (ICommand?)GetValue(ToggleRecordingCommandProperty)!; set => SetValue(ToggleRecordingCommandProperty, value); }
    public static readonly DependencyProperty ResetCommandProperty = Property(nameof(ResetCommand), typeof(ICommand), null);
    public ICommand? ResetCommand { get => (ICommand?)GetValue(ResetCommandProperty)!; set => SetValue(ResetCommandProperty, value); }
    public static readonly DependencyProperty ApplyFilterCommandProperty = Property(nameof(ApplyFilterCommand), typeof(ICommand), null);
    public ICommand? ApplyFilterCommand { get => (ICommand?)GetValue(ApplyFilterCommandProperty)!; set => SetValue(ApplyFilterCommandProperty, value); }
    public static readonly DependencyProperty RevealNodeCommandProperty = Property(nameof(RevealNodeCommand), typeof(ICommand), null);
    public ICommand? RevealNodeCommand { get => (ICommand?)GetValue(RevealNodeCommandProperty)!; set => SetValue(RevealNodeCommandProperty, value); }

    private static readonly DependencyPropertyKey HasEventsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasEvents), typeof(bool), typeof(DevToolsEventsView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasEventsProperty = HasEventsPropertyKey.DependencyProperty;
    public IEnumerable? EventsSource { get => (IEnumerable?)GetValue(EventsSourceProperty); set => SetValue(EventsSourceProperty, value); }
    public bool IsRecording { get => (bool)GetValue(IsRecordingProperty)!; set => SetValue(IsRecordingProperty, value); }
    public string FilterText { get => (string?)GetValue(FilterTextProperty) ?? string.Empty; set => SetValue(FilterTextProperty, value); }
    public bool HasEvents => (bool)GetValue(HasEventsProperty)!;
    public override void OnApplyTemplate()
    {
        if (_filterBox != null) { _filterBox.LostFocus -= OnFilterLostFocus; _filterBox.PreviewKeyDown -= OnFilterKeyDown; }
        base.OnApplyTemplate();
        _filterBox = GetTemplateChild("PART_FilterTextBox") as TextBox;
        if (_filterBox != null) { _filterBox.LostFocus += OnFilterLostFocus; _filterBox.PreviewKeyDown += OnFilterKeyDown; }
        UpdateStates();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }
    private bool IsCurrentFilterPart()
    {
        if (TemplateRootInternal == null) return false;
        for (Visual? current = _filterBox; current != null; current = current.VisualParent) if (ReferenceEquals(current, this)) return true;
        return false;
    }
    private bool CommitFilter()
    {
        if (!IsEnabled || !IsCurrentFilterPart() || ApplyFilterCommand?.CanExecute(FilterText) != true) return false;
        ApplyFilterCommand.Execute(FilterText);
        return true;
    }
    private void OnFilterLostFocus(object sender, RoutedEventArgs e) => CommitFilter();
    private void OnFilterKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && CommitFilter()) e.Handled = true; }
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsEventsView)d;
        if (e.OldValue is INotifyCollectionChanged old && view._sourceChanged != null) old.CollectionChanged -= view._sourceChanged;
        view._sourceChanged = null;
        if (e.NewValue is INotifyCollectionChanged collection)
        {
            var weak = new WeakReference<DevToolsEventsView>(view);
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (sender, _) =>
            {
                if (weak.TryGetTarget(out var owner)) owner.UpdateDataState();
                else if (sender is INotifyCollectionChanged retired) retired.CollectionChanged -= handler;
            };
            collection.CollectionChanged += handler; view._sourceChanged = handler;
        }
        view.UpdateDataState();
    }
    private void UpdateDataState()
    {
        bool any;
        if (EventsSource is ICollection collection) any = collection.Count > 0;
        else if (EventsSource is { } source)
        {
            var iterator = source.GetEnumerator();
            try { any = iterator.MoveNext(); } finally { (iterator as IDisposable)?.Dispose(); }
        }
        else any = false;
        SetValue(HasEventsPropertyKey, any); UpdateStates();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsEventsView)d).UpdateStates();
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, IsRecording ? "Recording" : "Idle", false);
        VisualStateManager.GoToState(this, HasEvents ? "EventsAvailable" : "EventsEmpty", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new EventsPeer(this);
    private sealed class EventsPeer(DevToolsEventsView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsEventsView);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Routed events";
    }
}

/// <summary>A command-enabled event-route node with strategy and original-source states.</summary>
[TemplateVisualState(Name = "Bubble", GroupName = "StrategyStates")]
[TemplateVisualState(Name = "Tunnel", GroupName = "StrategyStates")]
[TemplateVisualState(Name = "Direct", GroupName = "StrategyStates")]
[TemplateVisualState(Name = "OriginalSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "Ancestor", GroupName = "SourceStates")]
[TemplateVisualState(Name = "Named", GroupName = "NameStates")]
[TemplateVisualState(Name = "Unnamed", GroupName = "NameStates")]
[TemplateVisualState(Name = "Normal", GroupName = "InteractionStates")]
[TemplateVisualState(Name = "Hover", GroupName = "InteractionStates")]
[TemplateVisualState(Name = "Pressed", GroupName = "InteractionStates")]
[TemplateVisualState(Name = "Enabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
public class DevToolsEventNode : Button
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsEventNode), new PropertyMetadata(value));
    public static readonly DependencyProperty TypeNameProperty = Property(nameof(TypeName), typeof(string), "Element");
    public string TypeName { get => (string)GetValue(TypeNameProperty)!; set => SetValue(TypeNameProperty, value); }
    public static readonly DependencyProperty ElementNameProperty = Property(nameof(ElementName), typeof(string), string.Empty);
    public string ElementName { get => (string)GetValue(ElementNameProperty)!; set => SetValue(ElementNameProperty, value); }
    public static readonly DependencyProperty IsOriginalSourceProperty = Property(nameof(IsOriginalSource), typeof(bool), false);
    public bool IsOriginalSource { get => (bool)GetValue(IsOriginalSourceProperty)!; set => SetValue(IsOriginalSourceProperty, value); }

    public static readonly DependencyProperty StrategyProperty = DependencyProperty.Register(nameof(Strategy), typeof(RoutingStrategy), typeof(DevToolsEventNode), new PropertyMetadata(RoutingStrategy.Bubble), static value => value is RoutingStrategy strategy && Enum.IsDefined(strategy));
    public RoutingStrategy Strategy { get => (RoutingStrategy)GetValue(StrategyProperty)!; set => SetValue(StrategyProperty, value); }
    private static readonly DependencyPropertyKey NameTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(NameText), typeof(string), typeof(DevToolsEventNode), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty NameTextProperty = NameTextPropertyKey.DependencyProperty;
    public string NameText => (string)GetValue(NameTextProperty)!;
    static DevToolsEventNode() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsEventNode), new PropertyMetadata(typeof(DevToolsEventNode)));
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ElementNameProperty) SetValue(NameTextPropertyKey, string.IsNullOrEmpty(ElementName) ? "" : "#" + ElementName);
        if (e.Property == ElementNameProperty || e.Property == StrategyProperty || e.Property == IsOriginalSourceProperty ||
            e.Property == IsMouseOverProperty || e.Property == IsPressedProperty || e.Property == IsEnabledProperty || e.Property == IsKeyboardFocusedProperty) UpdateStates();
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, Strategy.ToString(), false);
        VisualStateManager.GoToState(this, IsOriginalSource ? "OriginalSource" : "Ancestor", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(ElementName) ? "Unnamed" : "Named", false);
        VisualStateManager.GoToState(this, IsPressed ? "Pressed" : IsMouseOver ? "Hover" : "Normal", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Enabled" : "Disabled", false);
        VisualStateManager.GoToState(this, IsEnabled && IsKeyboardFocused ? "Focused" : "Unfocused", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new NodePeer(this);
    private sealed class NodePeer(DevToolsEventNode owner) : ButtonAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsEventNode);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name
            : $"Reveal {owner.TypeName}{(string.IsNullOrEmpty(owner.ElementName) ? "" : " #" + owner.ElementName)}{(owner.IsOriginalSource ? " · original source" : "")}";
    }
}

/// <summary>A routed-event snapshot. Route nodes are ordered in actual dispatch direction.</summary>
public class DevToolsRoutedEventItem : DependencyObject
{
    private readonly ObservableCollection<DevToolsEventNodeItem> _nodes = new();
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsRoutedEventItem), new PropertyMetadata(value));
    public static readonly DependencyProperty TimeTextProperty = Property(nameof(TimeText), typeof(string), string.Empty);
    public string TimeText { get => (string)GetValue(TimeTextProperty)!; set => SetValue(TimeTextProperty, value); }
    public static readonly DependencyProperty EventNameProperty = Property(nameof(EventName), typeof(string), string.Empty);
    public string EventName { get => (string)GetValue(EventNameProperty)!; set => SetValue(EventNameProperty, value); }
    public static readonly DependencyProperty StrategyProperty = Property(nameof(Strategy), typeof(RoutingStrategy), RoutingStrategy.Direct);
    public RoutingStrategy Strategy { get => (RoutingStrategy)GetValue(StrategyProperty)!; set => SetValue(StrategyProperty, value); }
    public static readonly DependencyProperty HandledProperty = Property(nameof(Handled), typeof(bool), false);
    public bool Handled { get => (bool)GetValue(HandledProperty)!; set => SetValue(HandledProperty, value); }
    public static readonly DependencyProperty AutomationNameProperty = Property(nameof(AutomationName), typeof(string), string.Empty);
    public string AutomationName { get => (string)GetValue(AutomationNameProperty)!; set => SetValue(AutomationNameProperty, value); }

    private static readonly DependencyPropertyKey NodesPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Nodes), typeof(IEnumerable), typeof(DevToolsRoutedEventItem), new PropertyMetadata(null));
    public static readonly DependencyProperty NodesProperty = NodesPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasNodesPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasNodes), typeof(bool), typeof(DevToolsRoutedEventItem), new PropertyMetadata(false));
    public static readonly DependencyProperty HasNodesProperty = HasNodesPropertyKey.DependencyProperty;
    public IEnumerable Nodes => (IEnumerable)GetValue(NodesProperty)!;
    public bool HasNodes => (bool)GetValue(HasNodesProperty)!;
    public RoutedEventDiagnostics.RoutedEventEntry? Source { get; private set; }
    public DevToolsRoutedEventItem() => SetValue(NodesPropertyKey, new ReadOnlyObservableCollection<DevToolsEventNodeItem>(_nodes));
    public void Update(RoutedEventDiagnostics.RoutedEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool changed = !ReferenceEquals(Source, entry);
        Source = entry;
        TimeText = entry.Timestamp.ToString("HH:mm:ss.fff"); EventName = entry.EventName; Strategy = entry.Strategy; Handled = entry.Handled;
        if (changed)
        {
            _nodes.Clear();
            for (int step = 0; step < entry.Path.Count; step++)
            {
                int index = entry.Strategy == RoutingStrategy.Tunnel ? entry.Path.Count - 1 - step : step;
                var node = new DevToolsEventNodeItem();
                node.Update(entry.Path[index], entry.Strategy, index == 0, step < entry.Path.Count - 1);
                _nodes.Add(node);
            }
            SetValue(HasNodesPropertyKey, _nodes.Count > 0);
        }
        else foreach (var node in _nodes) node.RefreshTarget();
        AutomationName = $"{TimeText} · {EventName} · {Strategy}" + (Handled ? " · handled" : "");
    }
}

/// <summary>Presentation data for one weakly referenced visual on an event route.</summary>
public class DevToolsEventNodeItem : DependencyObject
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsEventNodeItem), new PropertyMetadata(value));
    public static readonly DependencyProperty TypeNameProperty = Property(nameof(TypeName), typeof(string), string.Empty);
    public string TypeName { get => (string)GetValue(TypeNameProperty)!; set => SetValue(TypeNameProperty, value); }
    public static readonly DependencyProperty ElementNameProperty = Property(nameof(ElementName), typeof(string), string.Empty);
    public string ElementName { get => (string)GetValue(ElementNameProperty)!; set => SetValue(ElementNameProperty, value); }
    public static readonly DependencyProperty StrategyProperty = Property(nameof(Strategy), typeof(RoutingStrategy), RoutingStrategy.Direct);
    public RoutingStrategy Strategy { get => (RoutingStrategy)GetValue(StrategyProperty)!; set => SetValue(StrategyProperty, value); }
    public static readonly DependencyProperty IsOriginalSourceProperty = Property(nameof(IsOriginalSource), typeof(bool), false);
    public bool IsOriginalSource { get => (bool)GetValue(IsOriginalSourceProperty)!; set => SetValue(IsOriginalSourceProperty, value); }
    public static readonly DependencyProperty HasSuccessorProperty = Property(nameof(HasSuccessor), typeof(bool), false);
    public bool HasSuccessor { get => (bool)GetValue(HasSuccessorProperty)!; set => SetValue(HasSuccessorProperty, value); }
    public static readonly DependencyProperty CanRevealProperty = Property(nameof(CanReveal), typeof(bool), false);
    public bool CanReveal { get => (bool)GetValue(CanRevealProperty)!; set => SetValue(CanRevealProperty, value); }
    public static readonly DependencyProperty AutomationNameProperty = Property(nameof(AutomationName), typeof(string), string.Empty);
    public string AutomationName { get => (string)GetValue(AutomationNameProperty)!; set => SetValue(AutomationNameProperty, value); }

    public WeakReference<Visual>? VisualReference { get; private set; }
    public void Update(RoutedEventDiagnostics.PathNode node, RoutingStrategy strategy, bool isOriginalSource, bool hasSuccessor)
    {
        ArgumentNullException.ThrowIfNull(node);
        VisualReference = node.VisualRef; TypeName = node.TypeName; ElementName = node.ElementName;
        Strategy = strategy; IsOriginalSource = isOriginalSource; HasSuccessor = hasSuccessor; RefreshTarget();
        AutomationName = TypeName + (ElementName.Length == 0 ? "" : " #" + ElementName) + (IsOriginalSource ? " · original source" : "");
    }
    public void RefreshTarget() => CanReveal = VisualReference?.TryGetTarget(out _) == true;
}

