using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a container that enables navigation of app content with WinUI-style appearance.
/// </summary>
public class NavigationView : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.NavigationViewAutomationPeer(this);
    }

    #region Fields

    private NavigationViewItem? _selectedItem;
    private bool _isUpdatingSelection;
    private StackPanel? _menuItemsPanel;
    private StackPanel? _footerItemsPanel;
    private Border? _paneContainer;
    private Border? _contentContainer;
    private Border? _paneHeaderHost;
    private Border? _paneFooterHost;
    private Border? _paneFooterRegion;
    private Grid? _rootGrid;
    private ScrollViewer? _paneScrollViewer;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the PaneBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PaneBackgroundProperty =
        DependencyProperty.Register(nameof(PaneBackground), typeof(Brush), typeof(NavigationView),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x4C, 0x3A, 0x3A, 0x3A)), OnPaneBackgroundChanged));

    /// <summary>
    /// Identifies the ContentBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentBackgroundProperty =
        DependencyProperty.Register(nameof(ContentBackground), typeof(Brush), typeof(NavigationView),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(40, 40, 40)), OnContentBackgroundChanged));

    /// <summary>
    /// Identifies the PaneHeader dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PaneHeaderProperty =
        DependencyProperty.Register(nameof(PaneHeader), typeof(UIElement), typeof(NavigationView),
            new PropertyMetadata(null, OnPaneHeaderChanged));

    /// <summary>
    /// Identifies the PaneFooter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PaneFooterProperty =
        DependencyProperty.Register(nameof(PaneFooter), typeof(UIElement), typeof(NavigationView),
            new PropertyMetadata(null, OnPaneFooterChanged));

    /// <summary>
    /// Identifies the IsPaneOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPaneOpenProperty =
        DependencyProperty.Register(nameof(IsPaneOpen), typeof(bool), typeof(NavigationView),
            new PropertyMetadata(true, OnIsPaneOpenChanged));

    /// <summary>
    /// Identifies the PaneDisplayMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PaneDisplayModeProperty =
        DependencyProperty.Register(nameof(PaneDisplayMode), typeof(NavigationViewPaneDisplayMode), typeof(NavigationView),
            new PropertyMetadata(NavigationViewPaneDisplayMode.Left, OnPaneDisplayModeChanged));

    /// <summary>
    /// Identifies the OpenPaneLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OpenPaneLengthProperty =
        DependencyProperty.Register(nameof(OpenPaneLength), typeof(double), typeof(NavigationView),
            new PropertyMetadata(280.0, OnOpenPaneLengthChanged));

    /// <summary>
    /// Identifies the CompactPaneLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty CompactPaneLengthProperty =
        DependencyProperty.Register(nameof(CompactPaneLength), typeof(double), typeof(NavigationView),
            new PropertyMetadata(48.0));

    /// <summary>
    /// Identifies the PaneTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PaneTitleProperty =
        DependencyProperty.Register(nameof(PaneTitle), typeof(string), typeof(NavigationView),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(NavigationView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsSettingsVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSettingsVisibleProperty =
        DependencyProperty.Register(nameof(IsSettingsVisible), typeof(bool), typeof(NavigationView),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsBackButtonVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsBackButtonVisibleProperty =
        DependencyProperty.Register(nameof(IsBackButtonVisible), typeof(NavigationViewBackButtonVisible), typeof(NavigationView),
            new PropertyMetadata(NavigationViewBackButtonVisible.Auto));

    /// <summary>
    /// Identifies the IsBackEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsBackEnabledProperty =
        DependencyProperty.Register(nameof(IsBackEnabled), typeof(bool), typeof(NavigationView),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(NavigationView),
            new PropertyMetadata(null, OnSelectedItemChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the pane background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? PaneBackground
    {
        get => (Brush?)GetValue(PaneBackgroundProperty);
        set => SetValue(PaneBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the content area background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Brush? ContentBackground
    {
        get => (Brush?)GetValue(ContentBackgroundProperty);
        set => SetValue(ContentBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the navigation pane is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPaneOpen
    {
        get => (bool)GetValue(IsPaneOpenProperty)!;
        set => SetValue(IsPaneOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the display mode of the navigation pane.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public NavigationViewPaneDisplayMode PaneDisplayMode
    {
        get => (NavigationViewPaneDisplayMode)GetValue(PaneDisplayModeProperty)!;
        set => SetValue(PaneDisplayModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the navigation pane when open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double OpenPaneLength
    {
        get => (double)GetValue(OpenPaneLengthProperty)!;
        set => SetValue(OpenPaneLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the navigation pane when in compact mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double CompactPaneLength
    {
        get => (double)GetValue(CompactPaneLengthProperty)!;
        set => SetValue(CompactPaneLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the title of the navigation pane.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string PaneTitle
    {
        get => (string)(GetValue(PaneTitleProperty) ?? string.Empty);
        set => SetValue(PaneTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the settings button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSettingsVisible
    {
        get => (bool)GetValue(IsSettingsVisibleProperty)!;
        set => SetValue(IsSettingsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the back button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public NavigationViewBackButtonVisible IsBackButtonVisible
    {
        get => (NavigationViewBackButtonVisible)(GetValue(IsBackButtonVisibleProperty) ?? NavigationViewBackButtonVisible.Auto);
        set => SetValue(IsBackButtonVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the back button is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsBackEnabled
    {
        get => (bool)GetValue(IsBackEnabledProperty)!;
        set => SetValue(IsBackEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected navigation item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets the collection of menu items.
    /// </summary>
    public ObservableCollection<object> MenuItems { get; } = new();

    /// <summary>
    /// Gets the collection of footer menu items.
    /// </summary>
    public ObservableCollection<object> FooterMenuItems { get; } = new();

    /// <summary>
    /// Gets or sets the custom pane header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public UIElement? PaneHeader
    {
        get => (UIElement?)GetValue(PaneHeaderProperty);
        set => SetValue(PaneHeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the custom pane footer content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public UIElement? PaneFooter
    {
        get => (UIElement?)GetValue(PaneFooterProperty);
        set => SetValue(PaneFooterProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationView"/> class.
    /// </summary>
    public NavigationView()
    {
        // NavigationView visuals are template-driven (Navigation.jalxaml).
        UseTemplateContentManagement();

        MenuItems.CollectionChanged += OnMenuItemsCollectionChanged;
        FooterMenuItems.CollectionChanged += OnFooterMenuItemsCollectionChanged;

        // Handle mouse clicks
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnNavigationViewMouseDown));

        // Ensure a template is available even before visual-tree attach (test/runtime parity).
        EnsureDefaultTemplateIfMissing();
    }

    #region Events

    /// <summary>
    /// Occurs when the back button is clicked.
    /// </summary>
    public event EventHandler<NavigationViewBackRequestedEventArgs>? BackRequested;

    /// <summary>
    /// Occurs when the pane is opening.
    /// </summary>
    public event EventHandler? PaneOpening;

    /// <summary>
    /// Occurs when the pane is opened.
    /// </summary>
    public event EventHandler? PaneOpened;

    /// <summary>
    /// Occurs when the pane is closing.
    /// </summary>
    public event EventHandler? PaneClosing;

    /// <summary>
    /// Occurs when the pane is closed.
    /// </summary>
    public event EventHandler? PaneClosed;

    /// <summary>
    /// Occurs when an item is invoked.
    /// </summary>
    public event EventHandler<NavigationViewItemInvokedEventArgs>? ItemInvoked;

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event EventHandler<NavigationViewSelectionChangedEventArgs>? SelectionChanged;

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the content to be displayed in the navigation view.
    /// </summary>
    /// <param name="content">The content to display.</param>
    public void SetContent(UIElement? content)
    {
        Content = content;
    }

    /// <summary>
    /// Updates the navigation view to reflect changes in the MenuItems or FooterMenuItems collections.
    /// This method can be used for forced refreshes; collection changes refresh automatically.
    /// </summary>
    public void UpdateMenuItems()
    {
        EnsureTemplatePartsReady();
        RefreshMenuItems();
        RefreshFooterItems();
    }

    #endregion

    #region Private Methods

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        if (_paneHeaderHost != null)
            _paneHeaderHost.SizeChanged -= OnPaneRegionSizeChanged;
        if (_paneFooterRegion != null)
            _paneFooterRegion.SizeChanged -= OnPaneRegionSizeChanged;

        base.OnApplyTemplate();

        _rootGrid = GetTemplateChild("PART_RootGrid") as Grid;
        _paneContainer = GetTemplateChild("PART_PaneContainer") as Border;
        _contentContainer = GetTemplateChild("PART_ContentContainer") as Border;
        _paneHeaderHost = GetTemplateChild("PART_PaneHeaderHost") as Border;
        _paneFooterHost = GetTemplateChild("PART_PaneFooterHost") as Border;
        _paneFooterRegion = GetTemplateChild("PART_PaneFooterRegion") as Border;
        _menuItemsPanel = GetTemplateChild("PART_MenuItemsHost") as StackPanel;
        _footerItemsPanel = GetTemplateChild("PART_FooterItemsHost") as StackPanel;
        _paneScrollViewer = GetTemplateChild("PART_PaneScrollViewer") as ScrollViewer;

        if (_paneHeaderHost != null)
            _paneHeaderHost.SizeChanged += OnPaneRegionSizeChanged;
        if (_paneFooterRegion != null)
            _paneFooterRegion.SizeChanged += OnPaneRegionSizeChanged;

        UpdatePaneWidth();
        UpdatePaneRegionBackdrop();
        UpdatePaneHeader();
        UpdatePaneFooter();
        RefreshMenuItems();
        RefreshFooterItems();
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        if (VisualParent != null)
        {
            RefreshMenuItems();
            RefreshFooterItems();
            UpdatePaneHeader();
            UpdatePaneFooter();
        }
    }

    private void OnMenuItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshMenuItems();
    }

    private void OnFooterMenuItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFooterItems();
    }

    private void OnNavigationViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.Handled)
            return;

        var clickedItem = FindNavigationItemFromOriginalSource(e.OriginalSource as DependencyObject);
        if (clickedItem == null)
            return;

        HandleItemClicked(clickedItem);
        e.Handled = true;
    }

    private static NavigationViewItem? FindNavigationItemFromOriginalSource(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is NavigationViewItem item)
                return item;

            current = current is Visual visual ? visual.VisualParent : null;
        }

        return null;
    }

    /// <summary>
    /// Handles a click on a NavigationViewItem (called from the item itself).
    /// </summary>
    internal void HandleItemClicked(NavigationViewItem item)
    {
        // Toggle expand for items with children
        if (item.HasUnrealizedChildren)
        {
            item.IsExpanded = !item.IsExpanded;
        }

        SelectItem(item);
    }

    private void SelectItem(NavigationViewItem item)
    {
        if (!item.SelectsOnInvoked)
        {
            item.Invoke();
            ItemInvoked?.Invoke(this, new NavigationViewItemInvokedEventArgs(item));
            return;
        }

        _isUpdatingSelection = true;
        try
        {
            var previousItem = _selectedItem;

            // Deselect previous item
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }

            // Select new item
            _selectedItem = item;
            _selectedItem.IsSelected = true;
            SelectedItem = item;

            // Fire events
            item.Invoke();
            ItemInvoked?.Invoke(this, new NavigationViewItemInvokedEventArgs(item));
            SelectionChanged?.Invoke(this, new NavigationViewSelectionChangedEventArgs(item, previousItem));
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void RefreshMenuItems()
    {
        EnsureTemplatePartsReady();
        if (_menuItemsPanel == null)
            return;

        _menuItemsPanel.Children.Clear();

        foreach (var item in MenuItems)
        {
            if (item is UIElement element)
            {
                AddItemWithChildren(element, _menuItemsPanel, 0);
            }
        }

        UpdatePaneScrollInsets();
        InvalidateMeasure();

        // 用户常见写法:在 Window.ctor → InitializeComponent → AddNavigationItems 里往
        // MenuItems 里 Add,一个 Add 触发一次 RefreshMenuItems。走到这里时 NavigationView
        // 可能已经 measure 过一次(template apply 时 size 被 cache),单靠自身 InvalidateMeasure
        // 在部分 layout 路径上不会让 Pane 内的 ScrollViewer / StackPanel 重新 arrange —
        // 结果 items 在 tree 里但视觉不出来。沿 visual tree 强制下推 invalidate,保证
        // MenuItemsPanel / ScrollViewer / PaneContainer 都会重测。
        InvalidateMeasureUpFrom(_menuItemsPanel);
    }

    private static void InvalidateMeasureUpFrom(UIElement? element)
    {
        while (element != null)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element = element.VisualParent as UIElement;
        }
    }

    private void AddItemWithChildren(UIElement element, Panel container, int indentLevel)
    {
        container.Children.Add(element);

        if (element is NavigationViewItem navItem)
        {
            navItem.IndentLevel = indentLevel;

            // Let items stretch to fill available width (ScrollViewer viewport automatically accounts for scrollbar)
            navItem.HorizontalAlignment = HorizontalAlignment.Stretch;

            // If item has children, add them recursively into the item's template children panel
            if (navItem.HasUnrealizedChildren)
            {
                var childrenPanel = navItem.GetChildrenPanel();
                if (childrenPanel != null)
                {
                    childrenPanel.Children.Clear();

                    // Let children panel stretch as well
                    childrenPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

                    foreach (var child in navItem.MenuItems)
                    {
                        AddItemWithChildren(child, childrenPanel, indentLevel + 1);
                    }
                }
            }

            navItem.RefreshHierarchyVisualState();
        }
    }

    private void RefreshFooterItems()
    {
        EnsureTemplatePartsReady();
        if (_footerItemsPanel == null)
            return;

        _footerItemsPanel.Children.Clear();

        foreach (var item in FooterMenuItems)
        {
            if (item is UIElement element)
            {
                AddItemWithChildren(element, _footerItemsPanel, 0);
            }
        }

        UpdatePaneFooterRegionVisibility();
        UpdatePaneScrollInsets();
        InvalidateMeasure();
    }

    private void UpdatePaneWidth()
    {
        if (_rootGrid != null && _rootGrid.ColumnDefinitions.Count > 0)
        {
            var paneWidth = IsPaneOpen ? OpenPaneLength : CompactPaneLength;
            _rootGrid.ColumnDefinitions[0].Width = new GridLength(paneWidth, GridUnitType.Pixel);
        }
    }

    private void UpdatePaneHeader()
    {
        if (_paneHeaderHost == null)
            return;

        _paneHeaderHost.Child = PaneHeader;
        _paneHeaderHost.Visibility = PaneHeader == null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePaneScrollInsets();
    }

    private void UpdatePaneFooter()
    {
        if (_paneFooterHost == null)
            return;

        _paneFooterHost.Child = PaneFooter;
        _paneFooterHost.Visibility = PaneFooter == null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePaneFooterRegionVisibility();
        UpdatePaneScrollInsets();
    }

    private void OnPaneRegionSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePaneScrollInsets();
    }

    private void UpdatePaneScrollInsets()
    {
        if (_menuItemsPanel == null)
            return;
    }

    private void UpdatePaneFooterRegionVisibility()
    {
        if (_paneFooterRegion == null)
            return;

        var hasFooterItems = _footerItemsPanel != null && _footerItemsPanel.Children.Count > 0;
        var hasPaneFooter = _paneFooterHost != null && _paneFooterHost.Visibility == Visibility.Visible;
        _paneFooterRegion.Visibility = hasFooterItems || hasPaneFooter ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePaneRegionBackdrop()
    {
        if (_paneHeaderHost == null || _paneFooterRegion == null)
            return;

        // Let header/footer be transparent so they inherit the pane background
        // seamlessly instead of applying a separate acrylic layer that causes
        // a visible color mismatch.
        _paneHeaderHost.Background = null;
        _paneHeaderHost.BackdropEffect = null;
        _paneFooterRegion.Background = null;
        _paneFooterRegion.BackdropEffect = null;
    }

    private void EnsureTemplatePartsReady()
    {
        if (_menuItemsPanel != null && _footerItemsPanel != null)
            return;

        EnsureDefaultTemplateIfMissing();
        ApplyTemplate();
    }

    private void EnsureDefaultTemplateIfMissing()
    {
        if (Template != null)
            return;

        var activeStyle = GetEffectiveStyle();
        if (TryGetTemplateFromStyle(activeStyle, out var activeStyleTemplate) && activeStyleTemplate != null)
        {
            Template = activeStyleTemplate;
            return;
        }

        var appResources = Jalium.UI.Application.Current?.Resources;
        if (appResources == null)
            return;

        var themeStyle = FindMergedStyle(appResources.MergedDictionaries, typeof(NavigationView));
        if (themeStyle == null && appResources.TryGetValue(typeof(NavigationView), out var styleObj))
            themeStyle = styleObj as Style;
        if (themeStyle == null)
            return;

        if (!TryGetTemplateFromStyle(themeStyle, out var template) || template == null)
            return;

        // Keep style-driven brushes/typography while restoring core visual structure.
        Template = template;
    }

    private Style? GetEffectiveStyle()
    {
        if (Style != null)
            return Style;

        return TryFindResource(typeof(NavigationView)) as Style;
    }

    private static Style? FindMergedStyle(IList<ResourceDictionary> dictionaries, object key)
    {
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            var dictionary = dictionaries[i];

            if (dictionary.TryGetValue(key, out var styleValue) && styleValue is Style style)
                return style;

            var nested = FindMergedStyle(dictionary.MergedDictionaries, key);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static bool TryGetTemplateFromStyle(Style? style, out ControlTemplate? template)
    {
        while (style != null)
        {
            foreach (var setter in style.Setters)
            {
                if (setter.Property == TemplateProperty && setter.Value is ControlTemplate controlTemplate)
                {
                    template = controlTemplate;
                    return true;
                }
            }

            style = style.BasedOn;
        }

        template = null;
        return false;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsPaneOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            var isOpen = (bool)(e.NewValue ?? true);
            if (isOpen)
            {
                nav.PaneOpening?.Invoke(nav, EventArgs.Empty);
                nav.UpdatePaneWidth();
                nav.PaneOpened?.Invoke(nav, EventArgs.Empty);
            }
            else
            {
                nav.PaneClosing?.Invoke(nav, EventArgs.Empty);
                nav.UpdatePaneWidth();
                nav.PaneClosed?.Invoke(nav, EventArgs.Empty);
            }
        }
    }

    private static void OnPaneDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            nav.InvalidateMeasure();
        }
    }

    private static void OnOpenPaneLengthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            nav.UpdatePaneWidth();
            // Refresh items to update their widths
            nav.RefreshMenuItems();
            nav.RefreshFooterItems();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav && !nav._isUpdatingSelection && e.NewValue is NavigationViewItem item)
        {
            if (nav._selectedItem != item)
            {
                nav.SelectItem(item);
            }
        }
    }

    private static void OnPaneBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            if (nav._paneContainer != null)
            {
                nav._paneContainer.Background = (Brush?)e.NewValue;
            }
            nav.UpdatePaneRegionBackdrop();
        }
    }

    private static void OnContentBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            if (nav._contentContainer != null)
            {
                nav._contentContainer.Background = (Brush?)e.NewValue;
            }
        }
    }

    private static void OnPaneHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            nav.UpdatePaneHeader();
        }
    }

    private static void OnPaneFooterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationView nav)
        {
            nav.UpdatePaneFooter();
        }
    }

    #endregion
}

/// <summary>
/// Specifies how the pane is displayed.
/// </summary>
public enum NavigationViewPaneDisplayMode
{
    /// <summary>
    /// The pane is always displayed at its full width on the left.
    /// </summary>
    Left,

    /// <summary>
    /// The pane is displayed at its compact width on the left.
    /// </summary>
    LeftCompact,

    /// <summary>
    /// The pane is only displayed when needed on the left.
    /// </summary>
    LeftMinimal,

    /// <summary>
    /// The pane is displayed at the top.
    /// </summary>
    Top
}

/// <summary>
/// Specifies the visibility of the back button.
/// </summary>
public enum NavigationViewBackButtonVisible
{
    /// <summary>
    /// The back button visibility is automatically determined.
    /// </summary>
    Auto,

    /// <summary>
    /// The back button is always visible.
    /// </summary>
    Visible,

    /// <summary>
    /// The back button is collapsed and not visible.
    /// </summary>
    Collapsed
}

/// <summary>
/// Provides data for the NavigationView.BackRequested event.
/// </summary>
public sealed class NavigationViewBackRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether the back navigation was handled.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Provides data for the NavigationView.SelectionChanged event.
/// </summary>
public sealed class NavigationViewSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the selected item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public NavigationViewItem? SelectedItem { get; }

    /// <summary>
    /// Gets the previously selected item.
    /// </summary>
    public NavigationViewItem? PreviousSelectedItem { get; }

    /// <summary>
    /// Gets a value indicating whether the selected item is the settings item.
    /// </summary>
    public bool IsSettingsSelected { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewSelectionChangedEventArgs"/> class.
    /// </summary>
    public NavigationViewSelectionChangedEventArgs(NavigationViewItem? selectedItem, NavigationViewItem? previousItem, bool isSettingsSelected = false)
    {
        SelectedItem = selectedItem;
        PreviousSelectedItem = previousItem;
        IsSettingsSelected = isSettingsSelected;
    }
}
