using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Used within the template of an item control to specify the place in the control's
/// visual tree where the ItemsPanel defined by the ItemsControl is to be added.
/// </summary>
public sealed class ItemsPresenter : FrameworkElement
{
    private Panel? _itemsPanel;
    private ItemsControl? _owner;

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
                _itemsPanel = _owner.ItemsPanel.CreatePanel() as Panel;
            }

            // Default to StackPanel if no template
            _itemsPanel ??= new StackPanel { Orientation = Orientation.Vertical };

            AddVisualChild(_itemsPanel);
        }

        return _itemsPanel;
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
        // Create the panel FIRST so it's available when owner calls RefreshItems
        var panel = EnsureItemsPanel();

        // Try to attach to owner if not already done
        if (_owner == null)
        {
            AttachToOwner();
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
}
