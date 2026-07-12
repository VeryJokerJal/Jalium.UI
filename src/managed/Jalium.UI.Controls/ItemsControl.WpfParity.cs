using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Data;

namespace Jalium.UI.Controls;

public partial class ItemsControl
{
    private static readonly DependencyPropertyKey AlternationIndexPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "AlternationIndex",
            typeof(int),
            typeof(ItemsControl),
            new PropertyMetadata(0));

    private static readonly DependencyPropertyKey HasItemsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasItems),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsGrouping),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty AlternationCountProperty =
        DependencyProperty.Register(
            nameof(AlternationCount),
            typeof(int),
            typeof(ItemsControl),
            new PropertyMetadata(0, OnAlternationCountPropertyChanged),
            static value => value is int count && count >= 0);

    public static readonly DependencyProperty AlternationIndexProperty =
        AlternationIndexPropertyKey.DependencyProperty;

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(ItemsControl),
            new PropertyMetadata(string.Empty, OnDisplayMemberPathPropertyChanged));

    public static readonly DependencyProperty GroupStyleSelectorProperty =
        DependencyProperty.Register(
            nameof(GroupStyleSelector),
            typeof(GroupStyleSelector),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnGroupStyleSelectorPropertyChanged));

    public static readonly DependencyProperty HasItemsProperty = HasItemsPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsGroupingProperty = IsGroupingPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsTextSearchCaseSensitiveProperty =
        DependencyProperty.Register(
            nameof(IsTextSearchCaseSensitive),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsTextSearchEnabledProperty =
        DependencyProperty.Register(
            nameof(IsTextSearchEnabled),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ItemBindingGroupProperty =
        DependencyProperty.Register(
            nameof(ItemBindingGroup),
            typeof(BindingGroup),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemBindingGroupPropertyChanged));

    public static readonly DependencyProperty ItemContainerStyleProperty =
        DependencyProperty.Register(
            nameof(ItemContainerStyle),
            typeof(Style),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemContainerStylePropertyChanged));

    public static readonly DependencyProperty ItemContainerStyleSelectorProperty =
        DependencyProperty.Register(
            nameof(ItemContainerStyleSelector),
            typeof(StyleSelector),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemContainerStyleSelectorPropertyChanged));

    public static readonly DependencyProperty ItemStringFormatProperty =
        DependencyProperty.Register(
            nameof(ItemStringFormat),
            typeof(string),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemStringFormatPropertyChanged));

    private readonly ObservableCollection<GroupStyle> _groupStyle = new();
    private ICollectionView? _observedGroupingView;
    private DataTemplate? _displayMemberTemplate;
    private int _itemsControlInitializationDepth;
    private bool _refreshItemsWhenInitializationCompletes;

    public int AlternationCount
    {
        get => (int)(GetValue(AlternationCountProperty) ?? 0);
        set => SetValue(AlternationCountProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty) ?? string.Empty;
        set => SetValue(DisplayMemberPathProperty, value ?? string.Empty);
    }

    public ObservableCollection<GroupStyle> GroupStyle => _groupStyle;

    public GroupStyleSelector? GroupStyleSelector
    {
        get => (GroupStyleSelector?)GetValue(GroupStyleSelectorProperty);
        set => SetValue(GroupStyleSelectorProperty, value);
    }

    public bool HasItems => (bool)(GetValue(HasItemsProperty) ?? false);

    public bool IsGrouping => (bool)(GetValue(IsGroupingProperty) ?? false);

    public bool IsTextSearchCaseSensitive
    {
        get => (bool)(GetValue(IsTextSearchCaseSensitiveProperty) ?? false);
        set => SetValue(IsTextSearchCaseSensitiveProperty, value);
    }

    public bool IsTextSearchEnabled
    {
        get => (bool)(GetValue(IsTextSearchEnabledProperty) ?? false);
        set => SetValue(IsTextSearchEnabledProperty, value);
    }

    public BindingGroup? ItemBindingGroup
    {
        get => (BindingGroup?)GetValue(ItemBindingGroupProperty);
        set => SetValue(ItemBindingGroupProperty, value);
    }

    public Style? ItemContainerStyle
    {
        get => (Style?)GetValue(ItemContainerStyleProperty);
        set => SetValue(ItemContainerStyleProperty, value);
    }

    public StyleSelector? ItemContainerStyleSelector
    {
        get => (StyleSelector?)GetValue(ItemContainerStyleSelectorProperty);
        set => SetValue(ItemContainerStyleSelectorProperty, value);
    }

    public string? ItemStringFormat
    {
        get => (string?)GetValue(ItemStringFormatProperty);
        set => SetValue(ItemStringFormatProperty, value);
    }

    public static int GetAlternationIndex(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(AlternationIndexProperty) ?? 0);
    }

    public static DependencyObject? ContainerFromElement(ItemsControl itemsControl, DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        ArgumentNullException.ThrowIfNull(element);

        DependencyObject? current = element;
        while (current != null && !ReferenceEquals(current, itemsControl))
        {
            if (itemsControl._itemContainerGenerator?.IndexFromContainer(current) >= 0)
            {
                return current;
            }

            if (current is Visual visual && visual.VisualParent is Panel panel &&
                ReferenceEquals(GetItemsOwner(panel), itemsControl))
            {
                return current;
            }

            current = current switch
            {
                FrameworkElement frameworkElement => frameworkElement.Parent,
                Visual visualParentSource => visualParentSource.VisualParent,
                _ => null,
            };
        }

        return null;
    }

    public DependencyObject? ContainerFromElement(DependencyObject element) =>
        ContainerFromElement(this, element);

    public override void BeginInit()
    {
        base.BeginInit();
        _itemsControlInitializationDepth++;
    }

    public override void EndInit()
    {
        if (_itemsControlInitializationDepth > 0)
        {
            _itemsControlInitializationDepth--;
        }

        base.EndInit();
        if (_itemsControlInitializationDepth == 0 && _refreshItemsWhenInitializationCompletes)
        {
            _refreshItemsWhenInitializationCompletes = false;
            RefreshItems();
        }
    }

    public bool ShouldSerializeGroupStyle() => _groupStyle.Count > 0;

    public bool ShouldSerializeItems() => ItemsSource == null && Items.Count > 0;

    protected virtual void AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Items.Add(value);
    }

    protected virtual void AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            AddChild(text);
        }
    }

    void Jalium.UI.Markup.IAddChild.AddChild(object value) => AddChild(value);

    void Jalium.UI.Markup.IAddChild.AddText(string text) => AddText(text);

    protected virtual DependencyObject GetContainerForItemOverride() => new ContentPresenter();

    protected virtual void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            throw new InvalidOperationException("An item container must be a FrameworkElement.");
        }

        PrepareContainerForItem(frameworkElement, item);
    }

    protected virtual void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is FrameworkElement frameworkElement)
        {
            ClearContainerForItem(frameworkElement, item);
        }
    }

    protected virtual bool ShouldApplyItemContainerStyle(DependencyObject container, object item)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(item);
        return container is FrameworkElement frameworkElement &&
               ReferenceEquals(frameworkElement.ReadLocalValue(FrameworkElement.StyleProperty), DependencyProperty.UnsetValue);
    }

    protected virtual void OnAlternationCountChanged(int oldAlternationCount, int newAlternationCount)
    {
        UpdateAlternationIndices();
    }

    protected virtual void OnDisplayMemberPathChanged(string oldDisplayMemberPath, string newDisplayMemberPath) =>
        ResetDisplayMemberTemplateAndRefresh();

    protected virtual void OnGroupStyleSelectorChanged(
        GroupStyleSelector? oldGroupStyleSelector,
        GroupStyleSelector? newGroupStyleSelector) => RefreshItems();

    protected virtual void OnItemBindingGroupChanged(
        BindingGroup? oldItemBindingGroup,
        BindingGroup? newItemBindingGroup) => RefreshItems();

    protected virtual void OnItemContainerStyleChanged(
        Style? oldItemContainerStyle,
        Style? newItemContainerStyle) => RefreshItems();

    protected virtual void OnItemContainerStyleSelectorChanged(
        StyleSelector? oldItemContainerStyleSelector,
        StyleSelector? newItemContainerStyleSelector) => RefreshItems();

    protected virtual void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
    }

    protected virtual void OnItemsPanelChanged(
        ItemsPanelTemplate? oldItemsPanel,
        ItemsPanelTemplate? newItemsPanel)
    {
    }

    protected virtual void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
    }

    protected virtual void OnItemStringFormatChanged(string? oldItemStringFormat, string? newItemStringFormat) =>
        ResetDisplayMemberTemplateAndRefresh();

    protected virtual void OnItemTemplateChanged(DataTemplate? oldItemTemplate, DataTemplate? newItemTemplate)
    {
    }

    protected virtual void OnItemTemplateSelectorChanged(
        DataTemplateSelector? oldItemTemplateSelector,
        DataTemplateSelector? newItemTemplateSelector)
    {
    }

    private void InitializeWpfParityState()
    {
        _groupStyle.CollectionChanged += OnGroupStyleCollectionChanged;
        UpdateItemsControlState();
    }

    private void OnGroupStyleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateItemsControlState();
        RefreshItems();
    }

    private void UpdateGroupingViewSubscription(IEnumerable? source)
    {
        if (_observedGroupingView != null)
        {
            _observedGroupingView.GroupDescriptions.CollectionChanged -= OnGroupingDescriptionChanged;
            _observedGroupingView = null;
        }

        if (source != null)
        {
            _observedGroupingView = source as ICollectionView ?? CollectionViewSource.GetDefaultView(source);
            _observedGroupingView.GroupDescriptions.CollectionChanged += OnGroupingDescriptionChanged;
        }
    }

    private void OnGroupingDescriptionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateItemsControlState();
        RefreshItems();
    }

    private DataTemplate? GetDisplayMemberTemplate()
    {
        if (string.IsNullOrEmpty(DisplayMemberPath) && string.IsNullOrEmpty(ItemStringFormat))
        {
            return null;
        }

        return _displayMemberTemplate ??= CreateDisplayMemberTemplate();
    }

    private DataTemplate CreateDisplayMemberTemplate()
    {
        var path = DisplayMemberPath;
        var stringFormat = ItemStringFormat;
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            var text = new TextBlock();
            var binding = string.IsNullOrEmpty(path) ? new Binding() : new Binding(path);
            binding.StringFormat = stringFormat;
            text.SetBinding(TextBlock.TextProperty, binding);
            return text;
        });
        return template;
    }

    private void ResetDisplayMemberTemplateAndRefresh()
    {
        _displayMemberTemplate = null;
        RefreshItems();
    }

    private void UpdateItemsControlState()
    {
        SetValue(HasItemsPropertyKey, GetEffectiveItemCount() > 0);
        SetValue(IsGroupingPropertyKey, GetIsEffectivelyGrouping());
        UpdateAlternationIndices();
    }

    private int GetEffectiveItemCount()
    {
        if (ItemsSource == null)
        {
            return Items.Count;
        }

        if (ItemsSource is ICollection collection)
        {
            return collection.Count;
        }

        var enumerator = ItemsSource.GetEnumerator();
        try
        {
            var count = 0;
            while (enumerator.MoveNext())
            {
                count++;
            }

            return count;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private bool GetIsEffectivelyGrouping()
    {
        if (ItemsSource == null)
        {
            return false;
        }

        ICollectionView view = ItemsSource as ICollectionView ?? CollectionViewSource.GetDefaultView(ItemsSource);
        return view.GroupDescriptions.Count > 0 || view.Groups is { Count: > 0 };
    }

    private Style? SelectContainerStyle(object item, DependencyObject container) =>
        ItemContainerStyleSelector?.SelectStyle(item, container) ?? ItemContainerStyle;

    private void ApplyItemContainerStyleAndBindingGroup(FrameworkElement element, object item)
    {
        var style = SelectContainerStyle(item, element);
        if (style != null && ShouldApplyItemContainerStyle(element, item))
        {
            element.Style = style;
        }

        if (ItemBindingGroup != null &&
            ReferenceEquals(element.ReadLocalValue(FrameworkElement.BindingGroupProperty), DependencyProperty.UnsetValue))
        {
            element.BindingGroup = ItemBindingGroup;
        }

        var index = _itemContainerGenerator?.IndexFromContainer(element) ?? -1;
        if (index < 0 && ItemsHost != null)
        {
            index = ItemsHost.Children.IndexOf(element);
        }

        SetAlternationIndex(element, index);
    }

    private void ClearItemContainerStyleAndBindingGroup(FrameworkElement element, object item)
    {
        var selectedStyle = SelectContainerStyle(item, element);
        if (selectedStyle != null && ReferenceEquals(element.Style, selectedStyle))
        {
            element.ClearValue(FrameworkElement.StyleProperty);
        }

        if (ItemBindingGroup != null && ReferenceEquals(element.BindingGroup, ItemBindingGroup))
        {
            element.ClearValue(FrameworkElement.BindingGroupProperty);
        }

        element.ClearValue(AlternationIndexPropertyKey);
    }

    internal void SetAlternationIndex(DependencyObject container, int index)
    {
        var value = AlternationCount > 0 && index >= 0 ? index % AlternationCount : 0;
        container.SetValue(AlternationIndexPropertyKey, value);
    }

    private void UpdateAlternationIndices()
    {
        var count = GetEffectiveItemCount();
        for (var index = 0; index < count; index++)
        {
            if (_itemContainerGenerator?.ContainerFromIndex(index) is DependencyObject generated)
            {
                SetAlternationIndex(generated, index);
            }
        }

        if (ItemsHost != null)
        {
            for (var index = 0; index < ItemsHost.Children.Count; index++)
            {
                SetAlternationIndex(ItemsHost.Children[index], index);
            }
        }
    }

    private static void OnAlternationCountPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnAlternationCountChanged((int)(e.OldValue ?? 0), (int)(e.NewValue ?? 0));
    }

    private static void OnDisplayMemberPathPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnDisplayMemberPathChanged((string?)e.OldValue ?? string.Empty, (string?)e.NewValue ?? string.Empty);
    }

    private static void OnGroupStyleSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnGroupStyleSelectorChanged((GroupStyleSelector?)e.OldValue, (GroupStyleSelector?)e.NewValue);
    }

    private static void OnItemBindingGroupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemBindingGroupChanged((BindingGroup?)e.OldValue, (BindingGroup?)e.NewValue);
    }

    private static void OnItemContainerStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemContainerStyleChanged((Style?)e.OldValue, (Style?)e.NewValue);
    }

    private static void OnItemContainerStyleSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemContainerStyleSelectorChanged((StyleSelector?)e.OldValue, (StyleSelector?)e.NewValue);
    }

    private static void OnItemStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemStringFormatChanged((string?)e.OldValue, (string?)e.NewValue);
    }
}
