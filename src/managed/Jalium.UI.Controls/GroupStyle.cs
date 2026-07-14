using System.ComponentModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines how a group of items should look at each level.
/// </summary>
public class GroupStyle : INotifyPropertyChanged
{
    private Style? _containerStyle;
    private StyleSelector? _containerStyleSelector;
    private DataTemplate? _headerTemplate;
    private DataTemplateSelector? _headerTemplateSelector;
    private string? _headerStringFormat;
    private ItemsPanelTemplate? _panel;
    private bool _hidesIfEmpty;
    private int _alternationCount;

    /// <summary>
    /// Gets the default panel template used to display grouped items.
    /// </summary>
    public static readonly ItemsPanelTemplate DefaultGroupPanel = CreateDefaultGroupPanel();

    /// <summary>
    /// Gets the default GroupStyle.
    /// </summary>
    public static GroupStyle Default { get; } = new();

    /// <summary>
    /// Gets or sets the style that is applied to the GroupItem generated for each item.
    /// </summary>
    public Style? ContainerStyle
    {
        get => _containerStyle;
        set => SetField(ref _containerStyle, value, nameof(ContainerStyle));
    }

    /// <summary>
    /// Gets or sets the style selector for the container style.
    /// </summary>
    public StyleSelector? ContainerStyleSelector
    {
        get => _containerStyleSelector;
        set => SetField(ref _containerStyleSelector, value, nameof(ContainerStyleSelector));
    }

    /// <summary>
    /// Gets or sets the template that is used to display the group header.
    /// </summary>
    public DataTemplate? HeaderTemplate
    {
        get => _headerTemplate;
        set => SetField(ref _headerTemplate, value, nameof(HeaderTemplate));
    }

    /// <summary>
    /// Gets or sets the template selector for the header template.
    /// </summary>
    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => _headerTemplateSelector;
        set => SetField(ref _headerTemplateSelector, value, nameof(HeaderTemplateSelector));
    }

    /// <summary>
    /// Gets or sets a composite string that specifies how to format the header if it is displayed as a string.
    /// </summary>
    public string? HeaderStringFormat
    {
        get => _headerStringFormat;
        set => SetField(ref _headerStringFormat, value, nameof(HeaderStringFormat));
    }

    /// <summary>
    /// Gets or sets the template that is used to display the group panel.
    /// </summary>
    public ItemsPanelTemplate? Panel
    {
        get => _panel;
        set => SetField(ref _panel, value, nameof(Panel));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the items in the group should be displayed
    /// without any visual expansion ("flat").
    /// </summary>
    public bool HidesIfEmpty
    {
        get => _hidesIfEmpty;
        set => SetField(ref _hidesIfEmpty, value, nameof(HidesIfEmpty));
    }

    /// <summary>
    /// Gets or sets a value that affects whether items in the corresponding level of grouping
    /// have alternating appearances.
    /// </summary>
    public int AlternationCount
    {
        get => _alternationCount;
        set => SetField(ref _alternationCount, value, nameof(AlternationCount));
    }

    /// <summary>
    /// Raised when one of this style's properties changes.
    /// </summary>
    protected virtual event PropertyChangedEventHandler? PropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    /// <summary>
    /// Raises the property changed event.
    /// </summary>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PropertyChanged?.Invoke(this, e);
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }

    private static ItemsPanelTemplate CreateDefaultGroupPanel()
    {
        var template = new ItemsPanelTemplate { PanelType = typeof(StackPanel) };
        template.Seal();
        return template;
    }
}

/// <summary>
/// Represents a group item container in an ItemsControl.
/// </summary>
public class GroupItem : ContentControl, IContainItemStorage, IHierarchicalVirtualizationAndScrollInfo
{
    private readonly Dictionary<object, Dictionary<DependencyProperty, object?>> _storedItemValues =
        new();
    private HierarchicalVirtualizationConstraints _constraints;
    private HierarchicalVirtualizationHeaderDesiredSizes _headerDesiredSizes;
    private HierarchicalVirtualizationItemDesiredSizes _itemDesiredSizes;
    private bool _mustDisableVirtualization;
    private bool _inBackgroundLayout;
    private UIElement? _headerElement;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.GroupItemAutomationPeer(this);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _headerElement = GetTemplateChild("PART_Header") as UIElement
            ?? GetTemplateChild("HeaderSite") as UIElement
            ?? GetTemplateChild("PART_HeaderContent") as UIElement;
    }

    void IContainItemStorage.StoreItemValue(object item, DependencyProperty dp, object value)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        if (!_storedItemValues.TryGetValue(item, out var values))
        {
            values = new Dictionary<DependencyProperty, object?>();
            _storedItemValues.Add(item, values);
        }

        values[dp] = value;
    }

    object? IContainItemStorage.ReadItemValue(object item, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        return _storedItemValues.TryGetValue(item, out var values) && values.TryGetValue(dp, out var value)
            ? value
            : DependencyProperty.UnsetValue;
    }

    void IContainItemStorage.ClearItemValue(object item, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        if (_storedItemValues.TryGetValue(item, out var values))
        {
            values.Remove(dp);
            if (values.Count == 0)
            {
                _storedItemValues.Remove(item);
            }
        }
    }

    void IContainItemStorage.ClearValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        foreach (var values in _storedItemValues.Values)
        {
            values.Remove(dp);
        }

        foreach (var emptyItem in _storedItemValues.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToArray())
        {
            _storedItemValues.Remove(emptyItem);
        }
    }

    void IContainItemStorage.Clear() => _storedItemValues.Clear();

    HierarchicalVirtualizationConstraints IHierarchicalVirtualizationAndScrollInfo.Constraints
    {
        get => _constraints;
        set => _constraints = value;
    }

    HierarchicalVirtualizationHeaderDesiredSizes IHierarchicalVirtualizationAndScrollInfo.HeaderDesiredSizes =>
        _headerDesiredSizes;

    HierarchicalVirtualizationItemDesiredSizes IHierarchicalVirtualizationAndScrollInfo.ItemDesiredSizes
    {
        get => _itemDesiredSizes;
        set => _itemDesiredSizes = value;
    }

    Panel? IHierarchicalVirtualizationAndScrollInfo.ItemsHost => FindItemsHost(this);

    bool IHierarchicalVirtualizationAndScrollInfo.MustDisableVirtualization
    {
        get => _mustDisableVirtualization;
        set => _mustDisableVirtualization = value;
    }

    bool IHierarchicalVirtualizationAndScrollInfo.InBackgroundLayout
    {
        get => _inBackgroundLayout;
        set => _inBackgroundLayout = value;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        var header = _headerElement ?? FindNamedHeader(this);
        var headerSize = header?.DesiredSize ?? default;
        if (header == null && ContentElement is { } directContent && FindItemsHost(directContent) == null)
        {
            headerSize = directContent.DesiredSize;
        }

        var logicalSize = new Size(headerSize.Width > 0 ? 1 : 0, headerSize.Height > 0 ? 1 : 0);
        _headerDesiredSizes = new HierarchicalVirtualizationHeaderDesiredSizes(logicalSize, headerSize);
        return desired;
    }

    private static UIElement? FindNamedHeader(Visual visual)
    {
        for (var index = 0; index < visual.VisualChildrenCount; index++)
        {
            if (visual.GetVisualChild(index) is not Visual child)
                continue;

            if (child is FrameworkElement { Name: "PART_Header" or "HeaderSite" or "PART_HeaderContent" } header)
                return header;

            if (child is Panel { IsItemsHost: true } or ItemsPresenter)
                continue;

            if (FindNamedHeader(child) is { } match)
                return match;
        }

        return null;
    }

    private static Panel? FindItemsHost(Visual visual)
    {
        if (visual is ItemsPresenter { ItemsPanel: { } presenterPanel })
        {
            return presenterPanel;
        }

        if (visual is ItemsControl { ItemsHostInternal: { } controlPanel })
        {
            return controlPanel;
        }

        if (visual is Panel { IsItemsHost: true } panel)
        {
            return panel;
        }

        for (var index = 0; index < visual.VisualChildrenCount; index++)
        {
            if (visual.GetVisualChild(index) is Visual child && FindItemsHost(child) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}

/// <summary>
/// Selects a group style for a collection-view group and its nesting level.
/// </summary>
public delegate GroupStyle GroupStyleSelector(CollectionViewGroup group, int level);
