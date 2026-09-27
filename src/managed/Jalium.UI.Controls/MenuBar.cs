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
public class MenuBar : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.MenuBarAutomationPeer(this);

    private readonly ObservableCollection<MenuBarItem> _items = new();
    private StackPanel? _panel;
    private Border? _cssBorderPainter;

    /// <summary>
    /// Gets the collection of MenuBarItem objects in the MenuBar.
    /// </summary>
    public IList<MenuBarItem> Items => _items;

    internal ObservableCollection<MenuBarItem> ItemCollection => _items;

    /// <summary>
    /// Initializes a new instance of the MenuBar class.
    /// </summary>
    public MenuBar()
    {
        Focusable = true;
        _items.CollectionChanged += OnItemsCollectionChanged;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsurePanel();
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        _panel!.Measure(CssBoxMetrics.InnerSize(availableSize, insets));
        return new Size(
            ControlRenderGeometry.GetAvailableLength(
                _panel.DesiredSize.Width + insets.Left + insets.Right, availableSize.Width),
            ControlRenderGeometry.GetAvailableLength(
                Math.Max(_panel.DesiredSize.Height, 32) + insets.Top + insets.Bottom,
                availableSize.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? finalSize.Width);
        _panel?.Arrange(ControlRenderGeometry.GetContentRect(new Rect(finalSize), insets));
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        base.OnRender(drawingContext);

        var backgroundLayer = GetEffectiveValueLayer(BackgroundProperty);
        var bg = Background;
        if (bg is null && backgroundLayer is not
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
            bg = Brushes.Transparent;
        if (bg is null) return;

        var outer = new Rect(RenderSize);
        var cssRadius = CssBorderRadiusProperties.Get(this);
        if (cssRadius is null && backgroundLayer is not
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
        {
            dc.DrawRoundedRectangle(bg, null, outer, CornerRadius);
            return;
        }

        var radii = cssRadius?.Resolve(RenderSize) ??
            CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize);
        var shape = new CssRoundedRectangleGeometry(outer, radii);
        var (border, padding) = CssBoxMetrics.BackgroundInsets(this,
            CssLayout?.ContainingWidthCache ?? RenderSize.Width);
        if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, bg, dc,
                outer, radii, border, padding,
                brush => dc.DrawGeometry(brush, null, shape)))
            dc.DrawGeometry(bg, null, shape);
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter, drawNative: true);
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => _panel != null ? 1 : 0;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index == 0 && _panel != null) return _panel;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Refreshes the visual representation of items.
    /// </summary>
    public void UpdateItems()
    {
        RefreshPanelChildren();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void EnsurePanel()
    {
        if (_panel != null) return;

        _panel = new StackPanel { Orientation = Orientation.Horizontal };
        AddVisualChild(_panel);
        RefreshPanelChildren();
    }

    private void RefreshPanelChildren()
    {
        if (_panel == null)
            return;

        _panel.Children.Clear();
        foreach (var item in _items)
        {
            item.ParentMenuBar = this;
            _panel.Children.Add(item);
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems.OfType<MenuBarItem>())
            {
                if (ReferenceEquals(oldItem.ParentMenuBar, this))
                {
                    oldItem.ParentMenuBar = null;
                }
            }
        }

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
