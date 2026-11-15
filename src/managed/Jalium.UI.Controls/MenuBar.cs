using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a specialized container that presents a set of menus in a horizontal row,
/// typically at the top of an app window.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Items))]
[TemplatePart(Name = "PART_Root", Type = typeof(Border))]
[TemplatePart(Name = "PART_ItemsPanel", Type = typeof(Panel))]
public class MenuBar : Control
{
    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.MenuBarAutomationPeer(this);
    private readonly ObservableCollection<MenuBarItem> _items = new();
    private readonly HashSet<MenuBarItem> _ownedItems = new(ReferenceEqualityComparer.Instance);
    private Panel? _panel;
    private static readonly DependencyPropertyKey ItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Items), typeof(IList<MenuBarItem>), typeof(MenuBar), new PropertyMetadata(null));
    public static readonly DependencyProperty ItemsProperty = ItemsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasItems), typeof(bool), typeof(MenuBar), new PropertyMetadata(false));
    public static readonly DependencyProperty HasItemsProperty = HasItemsPropertyKey.DependencyProperty;
    public bool HasItems => (bool)GetValue(HasItemsProperty)!;

    static MenuBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuBar), new PropertyMetadata(typeof(MenuBar)));
        FocusableProperty.OverrideMetadata(typeof(MenuBar), new PropertyMetadata(true));
    }

    /// <summary>
    /// Gets the collection of MenuBarItem objects in the MenuBar.
    /// </summary>
    public IList<MenuBarItem> Items => _items;
    internal ObservableCollection<MenuBarItem> ItemCollection => _items;
    protected internal override System.Collections.IEnumerator LogicalChildren => _items.GetEnumerator();

    /// <summary>
    /// Initializes a new instance of the MenuBar class.
    /// </summary>
    public MenuBar()
    {
        SetValue(ItemsPropertyKey, _items);
        _items.CollectionChanged += OnItemsCollectionChanged;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    public override void OnApplyTemplate()
    {
        ReleaseParts();
        base.OnApplyTemplate();
        _panel = GetTemplateChild("PART_ItemsPanel") as Panel;
        if (_panel != null)
            _panel.PreserveExistingLogicalParents = true;
        RefreshPanelChildren();
        UpdateVisualState();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleaseParts();
        base.OnTemplateContentClearing();
    }

    private void ReleaseParts()
    {
        var old = _panel;
        _panel = null;
        old?.Children.Clear();
    }

    protected override void RenderTemplatedBackground(DrawingContext context)
    {
        if (RenderSize.Width > 0 && RenderSize.Height > 0)
            base.RenderTemplatedBackground(context);
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue)
            CloseAllMenus();
        UpdateVisualState();
    }

    protected override void OnIsKeyboardFocusedChanged(bool focused)
    {
        base.OnIsKeyboardFocusedChanged(focused);
        UpdateVisualState();
    }

    private void UpdateVisualState() => VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsKeyboardFocused ? "Focused" : "Normal", false);
    /// <summary>
    /// Refreshes the visual representation of items.
    /// </summary>
    public void UpdateItems()
    {
        RefreshPanelChildren();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RefreshPanelChildren()
    {
        if (_panel is not { } panel)
            return;
        panel.Children.BeginBatchUpdate();
        try
        {
            for (int index = panel.Children.Count - 1; index >= 0; index--)
                if (panel.Children[index] is not MenuBarItem item || !_items.Contains(item))
                    panel.Children.RemoveAt(index);
            for (int index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], item))
                    continue;
                if (panel.Children.Contains(item))
                    panel.Children.Remove(item);
                item.DetachFromVisualParent();
                panel.Children.Insert(index, item);
            }
        }
        finally
        {
            panel.Children.EndBatchUpdate();
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Menu ownership is a collection contract, including before layout and
        // Reset notifications whose OldItems list is absent.
        foreach (var oldItem in _ownedItems)
            if (!_items.Contains(oldItem))
            {
                if (ReferenceEquals(oldItem.ParentMenuBar, this))
                    oldItem.ParentMenuBar = null;
                RemoveLogicalChild(oldItem);
            }

        _ownedItems.Clear();
        foreach (var item in _items)
        {
            item.ParentMenuBar = this;
            AddLogicalChild(item);
            _ownedItems.Add(item);
        }

        SetValue(HasItemsPropertyKey, _items.Count > 0);
        RefreshPanelChildren();
        InvalidateMeasure();
        InvalidateVisual();
    }

    internal void CloseAllMenus(MenuBarItem? except = null)
    {
        foreach (var item in _items)
        {
            if (item != except)
                item.CloseMenu();
        }
    }

    internal bool IsAnyMenuOpen()
    {
        return _items.Any(item => item.IsMenuOpen);
    }

    internal bool FocusSibling(MenuBarItem currentItem, int direction, bool openMenu)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        var currentIndex = _items.IndexOf(currentItem);
        if (currentIndex < 0)
        {
            return false;
        }

        for (int offset = 1; offset <= _items.Count; offset++)
        {
            var nextIndex = (currentIndex + (direction * offset) + _items.Count) % _items.Count;
            var candidate = _items[nextIndex];
            if (!candidate.IsEnabled || candidate.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (!candidate.Focus())
            {
                continue;
            }

            if (openMenu)
            {
                candidate.OpenMenuAndFocusFirstItem();
            }

            return true;
        }

        return false;
    }

    internal bool FocusBoundaryItem(bool last, bool openMenu)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        if (!last)
        {
            for (int index = 0; index < _items.Count; index++)
            {
                var candidate = _items[index];
                if (!candidate.IsEnabled || candidate.Visibility != Visibility.Visible)
                {
                    continue;
                }

                if (!candidate.Focus())
                {
                    continue;
                }

                if (openMenu)
                {
                    candidate.OpenMenuAndFocusFirstItem();
                }

                return true;
            }

            return false;
        }

        for (int index = _items.Count - 1; index >= 0; index--)
        {
            var candidate = _items[index];
            if (!candidate.IsEnabled || candidate.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (!candidate.Focus())
            {
                continue;
            }

            if (openMenu)
            {
                candidate.OpenMenuAndFocusFirstItem();
            }

            return true;
        }

        return false;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (Keyboard.FocusedElement is MenuBarItem)
        {
            return;
        }

        var handled = e.Key switch
        {
            Key.Left => FocusBoundaryItem(last: true, openMenu: false),
            Key.Right or Key.Down => FocusBoundaryItem(last: false, openMenu: false),
            Key.Home => FocusBoundaryItem(last: false, openMenu: false),
            Key.End => FocusBoundaryItem(last: true, openMenu: false),
            _ => false
        };
        if (handled)
        {
            e.Handled = true;
        }
    }
}
