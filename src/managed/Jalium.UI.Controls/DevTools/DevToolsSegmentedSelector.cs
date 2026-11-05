using System.Collections.Specialized;
using Jalium.UI.Input;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A compact single-selection list using standard selection and accessibility contracts.</summary>
public class DevToolsSegmentedSelector : ListBox
{
    private readonly Dictionary<DevToolsSegmentItem, Action> _callbacks = new();
    private bool _suppressCallback;

    static DevToolsSegmentedSelector() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsSegmentedSelector), new PropertyMetadata(typeof(DevToolsSegmentedSelector)));

    public DevToolsSegmentedSelector()
    {
        SelectionChanged += OnSegmentSelectionChanged;
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnSegmentKeyDown));
    }

    /// <summary>Adds a glyph option. Its full label is used for the tooltip and accessible name.</summary>
    public DevToolsSegmentedSelector AddSegment(string glyph, string? label, Action onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        var item = new DevToolsSegmentItem { Content = glyph };
        if (label != null)
        {
            global::Jalium.UI.Automation.AutomationProperties.SetName(item, label);
            ToolTipService.SetToolTip(item, label);
        }
        _callbacks.Add(item, onSelect);
        Items.Add(item);
        return this;
    }

    /// <summary>Synchronizes the selected option without calling its action.</summary>
    public void SetSelectedSilent(int index)
    {
        bool previous = _suppressCallback;
        _suppressCallback = true;
        try { SetCurrentValue(SelectedIndexProperty, index); }
        finally { _suppressCallback = previous; }
    }

    protected override FrameworkElement GetContainerForItem(object item) => new DevToolsSegmentItem();

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        foreach (var item in _callbacks.Keys.Where(item => !Items.Contains(item)).ToArray())
            _callbacks.Remove(item);
    }

    private void OnSegmentSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (!_suppressCallback && ReferenceEquals(e.OriginalSource, this) &&
            SelectedItem is DevToolsSegmentItem item && _callbacks.TryGetValue(item, out var callback))
            callback();
    }

    private void OnSegmentKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || e.Key is not (Key.Left or Key.Right)) return;
        int direction = e.Key == Key.Right ? 1 : -1;
        if (FlowDirection == FlowDirection.RightToLeft) direction = -direction;
        int next = SelectedIndex < 0 ? (direction > 0 ? 0 : Items.Count - 1) : SelectedIndex + direction;
        while (next >= 0 && next < Items.Count)
        {
            if (Items[next] is not UIElement element || element.IsEnabled && element.Visibility == Visibility.Visible)
            {
                SetCurrentValue(SelectedIndexProperty, next);
                UpdateLayout();
                (ItemContainerGenerator.ContainerFromIndex(next) as UIElement)?.Focus();
                break;
            }
            next += direction;
        }
        e.Handled = true;
    }
}

/// <summary>The templated option container for <see cref="DevToolsSegmentedSelector"/>.</summary>
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "MouseOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
public class DevToolsSegmentItem : ListBoxItem
{
    static DevToolsSegmentItem() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsSegmentItem), new PropertyMetadata(typeof(DevToolsSegmentItem)));

    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsSelectedProperty || e.Property == IsMouseOverProperty ||
            e.Property == IsEnabledProperty || e.Property == IsKeyboardFocusedProperty)
            UpdateStates();
    }

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "MouseOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsKeyboardFocused && IsEnabled ? "Focused" : "Unfocused", false);
    }
}
