using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Displays a Ribbon user interface.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class Ribbon : Selector
{
    private static readonly DependencyPropertyKey IsHostedInRibbonWindowPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsHostedInRibbonWindow), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(false));

    private bool _isSynchronizingTabSelection;
    private readonly ObservableCollection<RibbonContextualTabGroup> _contextualTabGroups = new();

    #region Dependency Properties

    public static readonly DependencyProperty ApplicationMenuProperty =
        DependencyProperty.Register(nameof(ApplicationMenu), typeof(RibbonApplicationMenu), typeof(Ribbon),
            new PropertyMetadata(null, OnApplicationMenuChanged));

    public static readonly DependencyProperty CheckedBackgroundProperty =
        DependencyProperty.Register(nameof(CheckedBackground), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CheckedBorderBrushProperty =
        DependencyProperty.Register(nameof(CheckedBorderBrush), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ContextualTabGroupHeaderTemplateProperty =
        DependencyProperty.Register(nameof(ContextualTabGroupHeaderTemplate), typeof(DataTemplate), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ContextualTabGroupsSourceProperty =
        DependencyProperty.Register(nameof(ContextualTabGroupsSource), typeof(IEnumerable), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ContextualTabGroupStyleProperty =
        DependencyProperty.Register(nameof(ContextualTabGroupStyle), typeof(Style), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FocusedBackgroundProperty =
        DependencyProperty.Register(nameof(FocusedBackground), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FocusedBorderBrushProperty =
        DependencyProperty.Register(nameof(FocusedBorderBrush), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HelpPaneContentProperty =
        DependencyProperty.Register(nameof(HelpPaneContent), typeof(object), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HelpPaneContentTemplateProperty =
        DependencyProperty.Register(nameof(HelpPaneContentTemplate), typeof(DataTemplate), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.Register(nameof(IsCollapsed), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(false, OnIsCollapsedChanged));

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(object), typeof(Ribbon),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsMinimized dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMinimizedProperty =
        DependencyProperty.Register(nameof(IsMinimized), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsHostedInRibbonWindowProperty =
        IsHostedInRibbonWindowPropertyKey.DependencyProperty;

    public static readonly DependencyProperty MouseOverBackgroundProperty =
        DependencyProperty.Register(nameof(MouseOverBackground), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MouseOverBorderBrushProperty =
        DependencyProperty.Register(nameof(MouseOverBorderBrush), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(nameof(PressedBackground), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBorderBrushProperty =
        DependencyProperty.Register(nameof(PressedBorderBrush), typeof(Brush), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty QuickAccessToolBarProperty =
        DependencyProperty.Register(nameof(QuickAccessToolBar), typeof(RibbonQuickAccessToolBar), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowQuickAccessToolBarOnTopProperty =
        DependencyProperty.Register(nameof(ShowQuickAccessToolBarOnTop), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(true));

    public static readonly DependencyProperty TabHeaderStyleProperty =
        DependencyProperty.Register(nameof(TabHeaderStyle), typeof(Style), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TabHeaderTemplateProperty =
        DependencyProperty.Register(nameof(TabHeaderTemplate), typeof(DataTemplate), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TitleTemplateProperty =
        DependencyProperty.Register(nameof(TitleTemplate), typeof(DataTemplate), typeof(Ribbon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty WindowIconVisibilityProperty =
        DependencyProperty.Register(nameof(WindowIconVisibility), typeof(Visibility), typeof(Ribbon),
            new PropertyMetadata(Visibility.Visible));

    #endregion

    /// <summary>
    /// Gets or sets the title of the Ribbon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the ribbon is minimized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMinimized
    {
        get => (bool)GetValue(IsMinimizedProperty)!;
        set => SetValue(IsMinimizedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the drop-down is open when minimized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public RibbonApplicationMenu? ApplicationMenu
    {
        get => (RibbonApplicationMenu?)GetValue(ApplicationMenuProperty);
        set => SetValue(ApplicationMenuProperty, value);
    }

    public Brush? CheckedBackground
    {
        get => (Brush?)GetValue(CheckedBackgroundProperty);
        set => SetValue(CheckedBackgroundProperty, value);
    }

    public Brush? CheckedBorderBrush
    {
        get => (Brush?)GetValue(CheckedBorderBrushProperty);
        set => SetValue(CheckedBorderBrushProperty, value);
    }

    public DataTemplate? ContextualTabGroupHeaderTemplate
    {
        get => (DataTemplate?)GetValue(ContextualTabGroupHeaderTemplateProperty);
        set => SetValue(ContextualTabGroupHeaderTemplateProperty, value);
    }

    public Collection<RibbonContextualTabGroup> ContextualTabGroups => _contextualTabGroups;

    public IEnumerable? ContextualTabGroupsSource
    {
        get => (IEnumerable?)GetValue(ContextualTabGroupsSourceProperty);
        set => SetValue(ContextualTabGroupsSourceProperty, value);
    }

    public Style? ContextualTabGroupStyle
    {
        get => (Style?)GetValue(ContextualTabGroupStyleProperty);
        set => SetValue(ContextualTabGroupStyleProperty, value);
    }

    public Brush? FocusedBackground
    {
        get => (Brush?)GetValue(FocusedBackgroundProperty);
        set => SetValue(FocusedBackgroundProperty, value);
    }

    public Brush? FocusedBorderBrush
    {
        get => (Brush?)GetValue(FocusedBorderBrushProperty);
        set => SetValue(FocusedBorderBrushProperty, value);
    }

    public object? HelpPaneContent
    {
        get => GetValue(HelpPaneContentProperty);
        set => SetValue(HelpPaneContentProperty, value);
    }

    public DataTemplate? HelpPaneContentTemplate
    {
        get => (DataTemplate?)GetValue(HelpPaneContentTemplateProperty);
        set => SetValue(HelpPaneContentTemplateProperty, value);
    }

    public bool IsCollapsed
    {
        get => (bool)(GetValue(IsCollapsedProperty) ?? false);
        set => SetValue(IsCollapsedProperty, value);
    }

    public bool IsHostedInRibbonWindow =>
        (bool)(GetValue(IsHostedInRibbonWindowProperty) ?? false);

    public Brush? MouseOverBackground
    {
        get => (Brush?)GetValue(MouseOverBackgroundProperty);
        set => SetValue(MouseOverBackgroundProperty, value);
    }

    public Brush? MouseOverBorderBrush
    {
        get => (Brush?)GetValue(MouseOverBorderBrushProperty);
        set => SetValue(MouseOverBorderBrushProperty, value);
    }

    public Brush? PressedBackground
    {
        get => (Brush?)GetValue(PressedBackgroundProperty);
        set => SetValue(PressedBackgroundProperty, value);
    }

    public Brush? PressedBorderBrush
    {
        get => (Brush?)GetValue(PressedBorderBrushProperty);
        set => SetValue(PressedBorderBrushProperty, value);
    }

    public RibbonQuickAccessToolBar? QuickAccessToolBar
    {
        get => (RibbonQuickAccessToolBar?)GetValue(QuickAccessToolBarProperty);
        set => SetValue(QuickAccessToolBarProperty, value);
    }

    public bool ShowQuickAccessToolBarOnTop
    {
        get => (bool)(GetValue(ShowQuickAccessToolBarOnTopProperty) ?? true);
        set => SetValue(ShowQuickAccessToolBarOnTopProperty, value);
    }

    public Style? TabHeaderStyle
    {
        get => (Style?)GetValue(TabHeaderStyleProperty);
        set => SetValue(TabHeaderStyleProperty, value);
    }

    public DataTemplate? TabHeaderTemplate
    {
        get => (DataTemplate?)GetValue(TabHeaderTemplateProperty);
        set => SetValue(TabHeaderTemplateProperty, value);
    }

    public DataTemplate? TitleTemplate
    {
        get => (DataTemplate?)GetValue(TitleTemplateProperty);
        set => SetValue(TitleTemplateProperty, value);
    }

    public Visibility WindowIconVisibility
    {
        get => (Visibility)(GetValue(WindowIconVisibilityProperty) ?? Visibility.Visible);
        set => SetValue(WindowIconVisibilityProperty, value);
    }

    public static readonly RoutedEvent CollapsedEvent =
        EventManager.RegisterRoutedEvent(nameof(Collapsed), RoutingStrategy.Direct,
            typeof(RoutedEventHandler), typeof(Ribbon));

    public static readonly RoutedEvent ExpandedEvent =
        EventManager.RegisterRoutedEvent(nameof(Expanded), RoutingStrategy.Direct,
            typeof(RoutedEventHandler), typeof(Ribbon));

    public event RoutedEventHandler Collapsed
    {
        add => AddHandler(CollapsedEvent, value);
        remove => RemoveHandler(CollapsedEvent, value);
    }

    public event RoutedEventHandler Expanded
    {
        add => AddHandler(ExpandedEvent, value);
        remove => RemoveHandler(ExpandedEvent, value);
    }

    protected override DependencyObject GetContainerForItemOverride() => new RibbonTab();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is RibbonTab;

    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is not RibbonTab tab)
        {
            return;
        }

        if (!ReferenceEquals(tab, item) && tab.Header == null)
        {
            tab.Header = item;
        }

        tab.Ribbon = this;
        _isSynchronizingTabSelection = true;
        try
        {
            tab.IsSelected = GetIndexOf(item) == SelectedIndex;
        }
        finally
        {
            _isSynchronizingTabSelection = false;
        }
    }

    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        if (element is RibbonTab tab)
        {
            tab.Ribbon = null;
            if (!ReferenceEquals(tab, item))
            {
                tab.IsSelected = false;
            }
        }

        base.ClearContainerForItem(element, item);
    }

    protected override void UpdateContainerSelection()
    {
        _isSynchronizingTabSelection = true;
        try
        {
            for (var index = 0; index < GetItemCount(); index++)
            {
                if (GetRibbonTabAt(index) is { } tab)
                {
                    tab.IsSelected = index == SelectedIndex;
                }
            }
        }
        finally
        {
            _isSynchronizingTabSelection = false;
        }
    }

    internal void OnTabSelectionChanged(RibbonTab tab, bool isSelected)
    {
        if (_isSynchronizingTabSelection)
        {
            return;
        }

        if (isSelected)
        {
            var item = ItemContainerGenerator.ItemFromContainer(tab);
            SelectedItem = ReferenceEquals(item, DependencyProperty.UnsetValue) ? tab : item;
        }
        else if (ReferenceEquals(GetRibbonTabAt(SelectedIndex), tab))
        {
            SelectedIndex = -1;
        }
    }

    private RibbonTab? GetRibbonTabAt(int index)
    {
        if (index < 0 || index >= GetItemCount())
        {
            return null;
        }

        var item = GetItemAt(index);
        return item as RibbonTab ?? ItemContainerGenerator.ContainerFromIndex(index) as RibbonTab;
    }

    private static void OnApplicationMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RibbonMenuButton oldMenu)
        {
            oldMenu.SetRibbon(null);
        }

        if (d is Ribbon ribbon && e.NewValue is RibbonMenuButton newMenu)
        {
            newMenu.SetRibbon(ribbon);
        }
    }

    private static void OnIsCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Ribbon ribbon)
        {
            var routedEvent = (bool)(e.NewValue ?? false) ? CollapsedEvent : ExpandedEvent;
            ribbon.RaiseEvent(new RoutedEventArgs(routedEvent, ribbon));
        }
    }
}

/// <summary>
/// Represents a tab on a Ribbon control.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonTab : HeaderedItemsControl
{
    private static readonly DependencyPropertyKey ContextualTabGroupPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ContextualTabGroup), typeof(RibbonContextualTabGroup), typeof(RibbonTab),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey RibbonPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Ribbon), typeof(Ribbon), typeof(RibbonTab),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey TabHeaderLeftPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(TabHeaderLeft), typeof(double), typeof(RibbonTab),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey TabHeaderRightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(TabHeaderRight), typeof(double), typeof(RibbonTab),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ContextualTabGroupHeaderProperty =
        DependencyProperty.Register(nameof(ContextualTabGroupHeader), typeof(object), typeof(RibbonTab),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ContextualTabGroupProperty =
        ContextualTabGroupPropertyKey.DependencyProperty;

    public static readonly DependencyProperty GroupSizeReductionOrderProperty =
        DependencyProperty.Register(nameof(GroupSizeReductionOrder), typeof(StringCollection), typeof(RibbonTab),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderStyleProperty =
        DependencyProperty.Register(nameof(HeaderStyle), typeof(Style), typeof(RibbonTab),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(RibbonTab),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly DependencyProperty KeyTipProperty =
        DependencyProperty.Register(nameof(KeyTip), typeof(string), typeof(RibbonTab),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RibbonProperty = RibbonPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TabHeaderLeftProperty = TabHeaderLeftPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TabHeaderRightProperty = TabHeaderRightPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets or sets whether this tab is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    public RibbonContextualTabGroup? ContextualTabGroup
    {
        get => (RibbonContextualTabGroup?)GetValue(ContextualTabGroupProperty);
        internal set => SetValue(ContextualTabGroupPropertyKey, value);
    }

    public object? ContextualTabGroupHeader
    {
        get => GetValue(ContextualTabGroupHeaderProperty);
        set => SetValue(ContextualTabGroupHeaderProperty, value);
    }

    public StringCollection? GroupSizeReductionOrder
    {
        get => (StringCollection?)GetValue(GroupSizeReductionOrderProperty);
        set => SetValue(GroupSizeReductionOrderProperty, value);
    }

    public Style? HeaderStyle
    {
        get => (Style?)GetValue(HeaderStyleProperty);
        set => SetValue(HeaderStyleProperty, value);
    }

    public string? KeyTip
    {
        get => (string?)GetValue(KeyTipProperty);
        set => SetValue(KeyTipProperty, value);
    }

    public Ribbon? Ribbon
    {
        get => (Ribbon?)GetValue(RibbonProperty);
        internal set => SetValue(RibbonPropertyKey, value);
    }

    public double TabHeaderLeft
    {
        get => (double)(GetValue(TabHeaderLeftProperty) ?? 0.0);
        internal set => SetValue(TabHeaderLeftPropertyKey, value);
    }

    public double TabHeaderRight
    {
        get => (double)(GetValue(TabHeaderRightProperty) ?? 0.0);
        internal set => SetValue(TabHeaderRightPropertyKey, value);
    }

    protected override DependencyObject GetContainerForItemOverride() => new RibbonGroup();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is RibbonGroup;

    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is RibbonGroup group && !ReferenceEquals(group, item) && group.Header == null)
        {
            group.Header = item;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!e.Handled && IsEnabled)
        {
            IsSelected = true;
            e.Handled = true;
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RibbonTab tab)
        {
            tab.Ribbon?.OnTabSelectionChanged(tab, (bool)(e.NewValue ?? false));
        }
    }
}

/// <summary>
/// Represents a group of controls within a RibbonTab.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonGroup : HeaderedItemsControl
{
    public static readonly DependencyProperty LargeImageSourceProperty =
        DependencyProperty.Register(nameof(LargeImageSource), typeof(ImageSource), typeof(RibbonGroup),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SmallImageSourceProperty =
        DependencyProperty.Register(nameof(SmallImageSource), typeof(ImageSource), typeof(RibbonGroup),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the small image source for the group.
    /// </summary>
    public ImageSource? SmallImageSource
    {
        get => (ImageSource?)GetValue(SmallImageSourceProperty);
        set => SetValue(SmallImageSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the large image source for the group.
    /// </summary>
    public ImageSource? LargeImageSource
    {
        get => (ImageSource?)GetValue(LargeImageSourceProperty);
        set => SetValue(LargeImageSourceProperty, value);
    }
}
