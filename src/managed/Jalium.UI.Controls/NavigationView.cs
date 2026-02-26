using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a container that enables navigation of app content with WinUI-style appearance.
/// </summary>
public sealed class NavigationView : ContentControl
{
    #region Fields

    private NavigationViewItem? _selectedItem;
    private bool _isUpdatingSelection;
    private readonly StackPanel _menuItemsPanel;
    private readonly StackPanel _footerItemsPanel;
    private readonly Border _paneContainer;
    private readonly Border _contentContainer;
    private readonly Grid _rootGrid;
    private readonly ScrollViewer _paneScrollViewer;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsPaneOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsPaneOpenProperty =
        DependencyProperty.Register(nameof(IsPaneOpen), typeof(bool), typeof(NavigationView),
            new PropertyMetadata(true, OnIsPaneOpenChanged));

    /// <summary>
    /// Identifies the PaneDisplayMode dependency property.
    /// </summary>
    public static readonly DependencyProperty PaneDisplayModeProperty =
        DependencyProperty.Register(nameof(PaneDisplayMode), typeof(NavigationViewPaneDisplayMode), typeof(NavigationView),
            new PropertyMetadata(NavigationViewPaneDisplayMode.Left, OnPaneDisplayModeChanged));

    /// <summary>
    /// Identifies the OpenPaneLength dependency property.
    /// </summary>
    public static readonly DependencyProperty OpenPaneLengthProperty =
        DependencyProperty.Register(nameof(OpenPaneLength), typeof(double), typeof(NavigationView),
            new PropertyMetadata(280.0, OnOpenPaneLengthChanged));

    /// <summary>
    /// Identifies the CompactPaneLength dependency property.
    /// </summary>
    public static readonly DependencyProperty CompactPaneLengthProperty =
        DependencyProperty.Register(nameof(CompactPaneLength), typeof(double), typeof(NavigationView),
            new PropertyMetadata(48.0));

    /// <summary>
    /// Identifies the PaneTitle dependency property.
    /// </summary>
    public static readonly DependencyProperty PaneTitleProperty =
        DependencyProperty.Register(nameof(PaneTitle), typeof(string), typeof(NavigationView),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(NavigationView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsSettingsVisible dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSettingsVisibleProperty =
        DependencyProperty.Register(nameof(IsSettingsVisible), typeof(bool), typeof(NavigationView),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsBackButtonVisible dependency property.
    /// </summary>
    public static readonly DependencyProperty IsBackButtonVisibleProperty =
        DependencyProperty.Register(nameof(IsBackButtonVisible), typeof(NavigationViewBackButtonVisible), typeof(NavigationView),
            new PropertyMetadata(NavigationViewBackButtonVisible.Auto));

    /// <summary>
    /// Identifies the IsBackEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsBackEnabledProperty =
        DependencyProperty.Register(nameof(IsBackEnabled), typeof(bool), typeof(NavigationView),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(NavigationView),
            new PropertyMetadata(null, OnSelectedItemChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the navigation pane is open.
    /// </summary>
    public bool IsPaneOpen
    {
        get => (bool)GetValue(IsPaneOpenProperty)!;
        set => SetValue(IsPaneOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the display mode of the navigation pane.
    /// </summary>
    public NavigationViewPaneDisplayMode PaneDisplayMode
    {
        get => (NavigationViewPaneDisplayMode)GetValue(PaneDisplayModeProperty)!;
        set => SetValue(PaneDisplayModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the navigation pane when open.
    /// </summary>
    public double OpenPaneLength
    {
        get => (double)GetValue(OpenPaneLengthProperty)!;
        set => SetValue(OpenPaneLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the navigation pane when in compact mode.
    /// </summary>
    public double CompactPaneLength
    {
        get => (double)GetValue(CompactPaneLengthProperty)!;
        set => SetValue(CompactPaneLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the title of the navigation pane.
    /// </summary>
    public string PaneTitle
    {
        get => (string)(GetValue(PaneTitleProperty) ?? string.Empty);
        set => SetValue(PaneTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the settings button is visible.
    /// </summary>
    public bool IsSettingsVisible
    {
        get => (bool)GetValue(IsSettingsVisibleProperty)!;
        set => SetValue(IsSettingsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the back button.
    /// </summary>
    public NavigationViewBackButtonVisible IsBackButtonVisible
    {
        get => (NavigationViewBackButtonVisible)(GetValue(IsBackButtonVisibleProperty) ?? NavigationViewBackButtonVisible.Auto);
        set => SetValue(IsBackButtonVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the back button is enabled.
    /// </summary>
    public bool IsBackEnabled
    {
        get => (bool)GetValue(IsBackEnabledProperty)!;
        set => SetValue(IsBackEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected navigation item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets the collection of menu items.
    /// </summary>
    public List<object> MenuItems { get; } = new();

    /// <summary>
    /// Gets the collection of footer menu items.
    /// </summary>
    public List<object> FooterMenuItems { get; } = new();

    /// <summary>
    /// Gets or sets the custom pane header content.
    /// </summary>
    public UIElement? PaneHeader { get; set; }

    /// <summary>
    /// Gets or sets the custom pane footer content.
    /// </summary>
    public UIElement? PaneFooter { get; set; }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationView"/> class.
    /// </summary>
    public NavigationView()
    {
        // Create internal layout
        _rootGrid = new Grid();
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OpenPaneLength, GridUnitType.Pixel) });
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Create menu items panel
        _menuItemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Create footer items panel
        _footerItemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // Create scroll viewer for pane content
        var paneContent = new Grid();
        paneContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        paneContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0, GridUnitType.Auto) });

        _paneScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _menuItemsPanel,
            ClipToBounds = false
        };
        Grid.SetRow(_paneScrollViewer, 0);
        paneContent.Children.Add(_paneScrollViewer);

        Grid.SetRow(_footerItemsPanel, 1);
        paneContent.Children.Add(_footerItemsPanel);

        // Create pane container with dark theme
        _paneContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)), // WinUI dark pane background
            Child = paneContent
        };
        Grid.SetColumn(_paneContainer, 0);
        _rootGrid.Children.Add(_paneContainer);

        // Create content container
        _contentContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)) // Slightly lighter content background
        };
        Grid.SetColumn(_contentContainer, 1);
        _rootGrid.Children.Add(_contentContainer);

        Content = _rootGrid;

        // Handle mouse clicks
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnNavigationViewMouseDown));
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
        _contentContainer.Child = content;
    }

    /// <summary>
    /// Updates the navigation view to reflect changes in the MenuItems or FooterMenuItems collections.
    /// Call this method after adding, removing, or modifying items in the collections.
    /// </summary>
    public void UpdateMenuItems()
    {
        RefreshMenuItems();
        RefreshFooterItems();
    }

    #endregion

    #region Private Methods

    private void OnNavigationViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);

            // Find if a menu item was clicked
            var item = FindNavigationItemAtPosition(pos);
            if (item != null)
            {
                HandleItemClicked(item);
                e.Handled = true;
            }
        }
    }

    private NavigationViewItem? FindNavigationItemAtPosition(Point position)
    {
        // Check menu items (including nested children)
        var item = FindItemInCollection(MenuItems, position);
        if (item != null) return item;

        // Check footer items
        item = FindItemInCollection(FooterMenuItems, position);
        return item;
    }

    private NavigationViewItem? FindItemInCollection(List<object> items, Point position)
    {
        foreach (var element in items)
        {
            if (element is NavigationViewItem item)
            {
                // Calculate item's absolute position in NavigationView coordinates
                var absoluteBounds = GetElementAbsoluteBounds(item);
                if (position.X >= absoluteBounds.X && position.X <= absoluteBounds.X + absoluteBounds.Width &&
                    position.Y >= absoluteBounds.Y && position.Y <= absoluteBounds.Y + absoluteBounds.Height)
                {
                    // Check if we hit a child item first
                    if (item.HasUnrealizedChildren && item.IsExpanded)
                    {
                        var childItem = FindItemInList(item.MenuItems, position);
                        if (childItem != null) return childItem;
                    }

                    return item;
                }

                // Also check children even if not directly hitting parent
                if (item.HasUnrealizedChildren && item.IsExpanded)
                {
                    var childItem = FindItemInList(item.MenuItems, position);
                    if (childItem != null) return childItem;
                }
            }
        }

        return null;
    }

    private NavigationViewItem? FindItemInList(List<NavigationViewItem> items, Point position)
    {
        foreach (var item in items)
        {
            var absoluteBounds = GetElementAbsoluteBounds(item);
            if (position.X >= absoluteBounds.X && position.X <= absoluteBounds.X + absoluteBounds.Width &&
                position.Y >= absoluteBounds.Y && position.Y <= absoluteBounds.Y + absoluteBounds.Height)
            {
                // Check nested children first
                if (item.HasUnrealizedChildren && item.IsExpanded)
                {
                    var childItem = FindItemInList(item.MenuItems, position);
                    if (childItem != null) return childItem;
                }
                return item;
            }

            // Also check children even if not directly hitting parent
            if (item.HasUnrealizedChildren && item.IsExpanded)
            {
                var childItem = FindItemInList(item.MenuItems, position);
                if (childItem != null) return childItem;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the absolute bounds of an element relative to this NavigationView.
    /// </summary>
    private Rect GetElementAbsoluteBounds(FrameworkElement element)
    {
        double offsetX = 0;
        double offsetY = 0;

        Visual? current = element;
        while (current != null && current != this)
        {
            if (current is FrameworkElement fe)
            {
                var bounds = fe.VisualBounds;
                offsetX += bounds.X;
                offsetY += bounds.Y;
            }
            current = current.VisualParent;
        }

        var itemBounds = element.VisualBounds;
        return new Rect(offsetX, offsetY, itemBounds.Width, itemBounds.Height);
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
        _menuItemsPanel.Children.Clear();

        foreach (var item in MenuItems)
        {
            if (item is UIElement element)
            {
                AddItemWithChildren(element, _menuItemsPanel, 0);
            }
        }

        InvalidateMeasure();
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
        }
    }

    private void RefreshFooterItems()
    {
        _footerItemsPanel.Children.Clear();

        foreach (var item in FooterMenuItems)
        {
            if (item is UIElement element)
            {
                AddItemWithChildren(element, _footerItemsPanel, 0);
            }
        }

        InvalidateMeasure();
    }

    private void UpdatePaneWidth()
    {
        if (_rootGrid.ColumnDefinitions.Count > 0)
        {
            var paneWidth = IsPaneOpen ? OpenPaneLength : CompactPaneLength;
            _rootGrid.ColumnDefinitions[0].Width = new GridLength(paneWidth, GridUnitType.Pixel);
        }
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
