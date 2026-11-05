namespace Jalium.UI.Controls.DevTools;

/// <summary>The pages in the standard developer-tools workspace.</summary>
public enum DevToolsPage { Inspector, Layout, Events, Bindings, Resources, Performance, Automation, Tools, Repl }

/// <summary>A reusable developer-tools shell. Page content and selection are independent of its tab template.</summary>
[TemplatePart(Name = "PART_Tabs", Type = typeof(TabControl))]
public class DevToolsWorkspace : Control
{
    public static readonly DependencyProperty SelectedPageProperty = DependencyProperty.Register(
        nameof(SelectedPage), typeof(DevToolsPage), typeof(DevToolsWorkspace),
        new FrameworkPropertyMetadata(DevToolsPage.Inspector, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedPageChanged),
        static value => value is DevToolsPage page && Enum.IsDefined(page));
    public static readonly DependencyProperty InspectorContentProperty = ContentProperty(nameof(InspectorContent));
    public static readonly DependencyProperty LayoutContentProperty = ContentProperty(nameof(LayoutContent));
    public static readonly DependencyProperty EventsContentProperty = ContentProperty(nameof(EventsContent));
    public static readonly DependencyProperty BindingsContentProperty = ContentProperty(nameof(BindingsContent));
    public static readonly DependencyProperty ResourcesContentProperty = ContentProperty(nameof(ResourcesContent));
    public static readonly DependencyProperty PerformanceContentProperty = ContentProperty(nameof(PerformanceContent));
    public static readonly DependencyProperty AutomationContentProperty = ContentProperty(nameof(AutomationContent));
    public static readonly DependencyProperty ToolsContentProperty = ContentProperty(nameof(ToolsContent));
    public static readonly DependencyProperty ReplContentProperty = ContentProperty(nameof(ReplContent));
    public static readonly RoutedEvent SelectedPageChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedPageChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsWorkspace));

    private TabControl? _tabs;
    private bool _synchronizing;
    static DevToolsWorkspace() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsWorkspace), new PropertyMetadata(typeof(DevToolsWorkspace)));
    private static DependencyProperty ContentProperty(string name) => DependencyProperty.Register(name, typeof(object), typeof(DevToolsWorkspace), new PropertyMetadata(null));

    public DevToolsPage SelectedPage { get => (DevToolsPage)GetValue(SelectedPageProperty)!; set => SetValue(SelectedPageProperty, value); }
    public object? InspectorContent { get => GetValue(InspectorContentProperty); set => SetValue(InspectorContentProperty, value); }
    public object? LayoutContent { get => GetValue(LayoutContentProperty); set => SetValue(LayoutContentProperty, value); }
    public object? EventsContent { get => GetValue(EventsContentProperty); set => SetValue(EventsContentProperty, value); }
    public object? BindingsContent { get => GetValue(BindingsContentProperty); set => SetValue(BindingsContentProperty, value); }
    public object? ResourcesContent { get => GetValue(ResourcesContentProperty); set => SetValue(ResourcesContentProperty, value); }
    public object? PerformanceContent { get => GetValue(PerformanceContentProperty); set => SetValue(PerformanceContentProperty, value); }
    public object? AutomationContent { get => GetValue(AutomationContentProperty); set => SetValue(AutomationContentProperty, value); }
    public object? ToolsContent { get => GetValue(ToolsContentProperty); set => SetValue(ToolsContentProperty, value); }
    public object? ReplContent { get => GetValue(ReplContentProperty); set => SetValue(ReplContentProperty, value); }
    public event RoutedEventHandler SelectedPageChanged { add => AddHandler(SelectedPageChangedEvent, value); remove => RemoveHandler(SelectedPageChangedEvent, value); }

    public override void OnApplyTemplate()
    {
        DetachTabs();
        base.OnApplyTemplate();
        _tabs = GetTemplateChild("PART_Tabs") as TabControl;
        if (_tabs != null)
        {
            SyncSelection();
            _tabs.SelectionChanged += OnTabSelectionChanged;
        }
    }

    internal override void OnTemplateContentClearing() { DetachTabs(releaseContent: true); base.OnTemplateContentClearing(); }
    private void DetachTabs(bool releaseContent = false)
    {
        var tabs = _tabs;
        _tabs = null;
        if (tabs == null) return;
        tabs.SelectionChanged -= OnTabSelectionChanged;
        // Items authored inside a nested control are not members of the outer
        // template namescope. Release their content bindings explicitly before
        // the next template tries to host the same page elements.
        if (releaseContent)
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                global::Jalium.UI.Data.BindingOperations.ClearBinding(item, ContentControl.ContentProperty);
                item.ClearValue(ContentControl.ContentProperty);
            }
    }
    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || !ReferenceEquals(e.OriginalSource, _tabs) || _tabs is not { SelectedIndex: >= 0 and <= 8 } tabs) return;
        SetCurrentValue(SelectedPageProperty, (DevToolsPage)tabs.SelectedIndex);
    }
    private void SyncSelection()
    {
        if (_tabs == null) return;
        bool previous = _synchronizing;
        _synchronizing = true;
        try { _tabs.SetCurrentValue(TabControl.SelectedIndexProperty, (int)SelectedPage); }
        finally { _synchronizing = previous; }
    }
    private static void OnSelectedPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var workspace = (DevToolsWorkspace)d;
        workspace.SyncSelection();
        workspace.RaiseEvent(new DevToolsPageChangedEventArgs(SelectedPageChangedEvent, workspace,
            (DevToolsPage)e.OldValue!, (DevToolsPage)e.NewValue!));
    }
}

/// <summary>The page transition exposed by <see cref="DevToolsWorkspace"/>.</summary>
public sealed class DevToolsPageChangedEventArgs(RoutedEvent routedEvent, object source, DevToolsPage oldPage, DevToolsPage newPage)
    : RoutedEventArgs(routedEvent, source)
{
    public DevToolsPage OldPage { get; } = oldPage;
    public DevToolsPage NewPage { get; } = newPage;
}
