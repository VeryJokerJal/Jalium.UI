using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Used within the template of an item control to specify the place in the control's
/// visual tree where the ItemsPanel defined by the ItemsControl is to be added.
/// </summary>
public class ItemsPresenter : FrameworkElement, IScrollInfo
{
    private Panel? _itemsPanel;
    private ItemsControl? _owner;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private double _horizontalOffset;
    private double _verticalOffset;
    private ScrollViewer? _scrollOwner;

    private IScrollInfo? CurrentScrollInfo => _itemsPanel as IScrollInfo;

    /// <summary>
    /// Gets the panel that hosts the items.
    /// </summary>
    internal Panel? ItemsPanel => _itemsPanel;

    /// <summary>
    /// Gets or sets the owning ItemsControl.
    /// </summary>
    internal ItemsControl? Owner
    {
        get => _owner;
        set
        {
            if (_owner != value)
            {
                _owner = value;
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsPresenter"/> class.
    /// </summary>
    public ItemsPresenter()
    {
    }

    /// <summary>
    /// Creates or returns the items panel.
    /// </summary>
    private Panel EnsureItemsPanel()
    {
        if (_itemsPanel == null)
        {
            // Try to get panel from owner's ItemsPanel template
            if (_owner?.ItemsPanel != null)
            {
                var created = _owner.ItemsPanel.CreatePanel();
                if (created is Panel panel)
                {
                    _itemsPanel = panel;
                }
                else if (created != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ItemsPresenter: ItemsPanelTemplate produced a {created.GetType().Name} instead of a Panel. Falling back to StackPanel.");
                }
            }

            // Default to StackPanel if no template or template didn't produce a Panel
            _itemsPanel ??= new StackPanel { Orientation = Orientation.Vertical };

            if (_itemsPanel is IScrollInfo scrollInfo)
            {
                scrollInfo.ScrollOwner = _scrollOwner;
                scrollInfo.CanHorizontallyScroll = _canHorizontallyScroll;
                scrollInfo.CanVerticallyScroll = _canVerticallyScroll;
            }

            AddVisualChild(_itemsPanel);
        }

        return _itemsPanel;
    }

    /// <summary>
    /// Discards the current items panel so that the next measure pass
    /// will create a new one from the owner's <see cref="ItemsControl.ItemsPanel"/> template.
    /// </summary>
    internal void InvalidatePanel()
    {
        if (_itemsPanel != null)
        {
            _itemsPanel.Children.Clear();
            RemoveVisualChild(_itemsPanel);
            _itemsPanel = null;
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Called when the template's parent changes to find the owning ItemsControl.
    /// </summary>
    internal void AttachToOwner()
    {
        // Find the ItemsControl ancestor
        var parent = TemplatedParent ?? VisualParent;
        while (parent != null)
        {
            if (parent is ItemsControl itemsControl)
            {
                Owner = itemsControl;
                itemsControl.SetItemsPresenter(this);
                break;
            }
            parent = (parent as FrameworkElement)?.VisualParent ?? (parent as FrameworkElement)?.TemplatedParent;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Attach to owner BEFORE creating the panel so that
        // EnsureItemsPanel can read the owner's ItemsPanel template.
        if (_owner == null)
        {
            AttachToOwner();
        }

        bool panelJustCreated = _itemsPanel == null;
        var panel = EnsureItemsPanel();

        // If the panel was just created after owner attachment, the earlier
        // RefreshItems (from SetItemsPresenter) found no panel and was a no-op.
        // Trigger it again now that the panel exists.
        if (panelJustCreated && _owner != null)
        {
            _owner.RefreshItemsInternal();
        }

        panel.Measure(availableSize);
        return panel.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_itemsPanel != null)
        {
            _itemsPanel.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => _itemsPanel != null ? 1 : 0;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index == 0 && _itemsPanel != null)
            return _itemsPanel;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #region IScrollInfo

    public bool CanHorizontallyScroll
    {
        get => CurrentScrollInfo?.CanHorizontallyScroll ?? _canHorizontallyScroll;
        set
        {
            _canHorizontallyScroll = value;
            if (CurrentScrollInfo != null)
            {
                CurrentScrollInfo.CanHorizontallyScroll = value;
            }
        }
    }

    public bool CanVerticallyScroll
    {
        get => CurrentScrollInfo?.CanVerticallyScroll ?? _canVerticallyScroll;
        set
        {
            _canVerticallyScroll = value;
            if (CurrentScrollInfo != null)
            {
                CurrentScrollInfo.CanVerticallyScroll = value;
            }
        }
    }

    public double ExtentWidth => CurrentScrollInfo?.ExtentWidth ?? (_itemsPanel?.DesiredSize.Width ?? 0);

    public double ExtentHeight => CurrentScrollInfo?.ExtentHeight ?? (_itemsPanel?.DesiredSize.Height ?? 0);

    public double ViewportWidth => CurrentScrollInfo?.ViewportWidth ?? RenderSize.Width;

    public double ViewportHeight => CurrentScrollInfo?.ViewportHeight ?? RenderSize.Height;

    public double HorizontalOffset => CurrentScrollInfo?.HorizontalOffset ?? _horizontalOffset;

    public double VerticalOffset => CurrentScrollInfo?.VerticalOffset ?? _verticalOffset;

    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set
        {
            _scrollOwner = value;
            if (CurrentScrollInfo != null)
            {
                CurrentScrollInfo.ScrollOwner = value;
            }
        }
    }

    public void LineUp()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.LineUp();
        }
    }

    public void LineDown()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.LineDown();
        }
    }

    public void LineLeft()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.LineLeft();
        }
    }

    public void LineRight()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.LineRight();
        }
    }

    public void PageUp()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.PageUp();
        }
    }

    public void PageDown()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.PageDown();
        }
    }

    public void PageLeft()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.PageLeft();
        }
    }

    public void PageRight()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.PageRight();
        }
    }

    public void MouseWheelUp()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.MouseWheelUp();
        }
    }

    public void MouseWheelDown()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.MouseWheelDown();
        }
    }

    public void MouseWheelLeft()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.MouseWheelLeft();
        }
    }

    public void MouseWheelRight()
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.MouseWheelRight();
        }
    }

    public void SetHorizontalOffset(double offset)
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.SetHorizontalOffset(offset);
            return;
        }

        _horizontalOffset = offset;
    }

    public void SetVerticalOffset(double offset)
    {
        if (CurrentScrollInfo != null)
        {
            CurrentScrollInfo.SetVerticalOffset(offset);
            return;
        }

        _verticalOffset = offset;
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (CurrentScrollInfo != null)
        {
            return CurrentScrollInfo.MakeVisible(visual, rectangle);
        }

        return rectangle;
    }

    #endregion
}
