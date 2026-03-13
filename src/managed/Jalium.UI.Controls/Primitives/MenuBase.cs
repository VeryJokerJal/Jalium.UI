using Jalium.UI.Input;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the abstract base class for all menu controls (Menu, ContextMenu).
/// </summary>
public abstract class MenuBase : ItemsControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the ItemContainerStyle dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemContainerStyleProperty =
        DependencyProperty.Register(nameof(ItemContainerStyle), typeof(Style), typeof(MenuBase),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the UsesItemContainerTemplate dependency property.
    /// </summary>
    public static readonly DependencyProperty UsesItemContainerTemplateProperty =
        DependencyProperty.Register(nameof(UsesItemContainerTemplate), typeof(bool), typeof(MenuBase),
            new PropertyMetadata(false));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ItemClick routed event.
    /// </summary>
    public static readonly RoutedEvent ItemClickEvent =
        EventManager.RegisterRoutedEvent(nameof(ItemClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuBase));

    /// <summary>
    /// Occurs when a menu item is clicked.
    /// </summary>
    public event RoutedEventHandler ItemClick
    {
        add => AddHandler(ItemClickEvent, value);
        remove => RemoveHandler(ItemClickEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the style applied to menu item containers.
    /// </summary>
    public Style? ItemContainerStyle
    {
        get => (Style?)GetValue(ItemContainerStyleProperty);
        set => SetValue(ItemContainerStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the menu uses item container templates.
    /// </summary>
    public bool UsesItemContainerTemplate
    {
        get => (bool)GetValue(UsesItemContainerTemplateProperty)!;
        set => SetValue(UsesItemContainerTemplateProperty, value);
    }

    /// <summary>
    /// Gets the currently selected menu item.
    /// </summary>
    public MenuItem? CurrentSelection { get; protected set; }

    #endregion

    #region Private Fields

    private int _currentIndex = -1;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuBase"/> class.
    /// </summary>
    protected MenuBase()
    {
        Focusable = true;
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Item Generation

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        // Default to vertical stack for submenus
        return new StackPanel { Orientation = Orientation.Vertical };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new MenuItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is MenuItem || item is Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is MenuItem menuItem)
        {
            // Apply container style if set
            if (ItemContainerStyle != null)
            {
                // Style would be applied here
            }

            // Set header from string
            if (item is string text)
            {
                menuItem.Header = text;
            }

            // Subscribe to click event
            menuItem.Click += OnMenuItemClick;
        }
    }


    #endregion

    #region Keyboard Navigation

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not KeyEventArgs keyArgs)
            return;

        var itemCount = Items.Count;
        if (itemCount == 0)
            return;

        switch (keyArgs.Key)
        {
            case Key.Up:
                NavigateToItem(_currentIndex - 1);
                e.Handled = true;
                break;

            case Key.Down:
                NavigateToItem(_currentIndex + 1);
                e.Handled = true;
                break;

            case Key.Home:
                NavigateToItem(0);
                e.Handled = true;
                break;

            case Key.End:
                NavigateToItem(itemCount - 1);
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Space:
                if (CurrentSelection != null)
                {
                    ActivateItem(CurrentSelection);
                }
                e.Handled = true;
                break;

            case Key.Escape:
                OnEscapePressed();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Navigates to the item at the specified index.
    /// </summary>
    /// <param name="index">The index to navigate to.</param>
    protected virtual void NavigateToItem(int index)
    {
        var itemCount = Items.Count;
        if (itemCount == 0)
            return;

        // Wrap around
        if (index < 0) index = itemCount - 1;
        if (index >= itemCount) index = 0;

        // Skip separators
        var originalIndex = index;
        while (GetItemAt(index) is Separator)
        {
            index++;
            if (index >= itemCount) index = 0;
            if (index == originalIndex) return; // All items are separators
        }

        _currentIndex = index;
        CurrentSelection = GetItemAt(index) as MenuItem;
        OnCurrentSelectionChanged();
    }

    /// <summary>
    /// Gets the item at the specified index.
    /// </summary>
    protected FrameworkElement? GetItemAt(int index)
    {
        if (index < 0 || index >= Items.Count)
            return null;

        var item = Items[index];
        if (item is FrameworkElement element)
            return element;

        // Item might be data-bound, need to find the container
        if (ItemsHost != null && index < ItemsHost.Children.Count)
            return ItemsHost.Children[index] as FrameworkElement;

        return null;
    }

    /// <summary>
    /// Called when the current selection changes.
    /// </summary>
    protected virtual void OnCurrentSelectionChanged()
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Activates (clicks) the specified menu item.
    /// </summary>
    protected virtual void ActivateItem(MenuItem item)
    {
        if (!item.IsEnabled)
            return;

        if (item.HasItems)
        {
            item.IsSubmenuOpen = true;
        }
        else
        {
            // Simulate a click
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        }
    }

    /// <summary>
    /// Called when the Escape key is pressed.
    /// </summary>
    protected virtual void OnEscapePressed()
    {
        // Close any open submenus
        CurrentSelection = null;
        _currentIndex = -1;
        OnCurrentSelectionChanged();
    }

    #endregion

    #region Event Handlers

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        // Bubble up the click event
        RaiseEvent(new RoutedEventArgs(ItemClickEvent, sender));
    }

    #endregion

    #region Focus Management

    /// <summary>
    /// Focuses the first menu item.
    /// </summary>
    public void FocusFirstItem()
    {
        NavigateToItem(0);
        CurrentSelection?.Focus();
    }

    /// <summary>
    /// Focuses the last menu item.
    /// </summary>
    public void FocusLastItem()
    {
        NavigateToItem(Items.Count - 1);
        CurrentSelection?.Focus();
    }

    #endregion
}
