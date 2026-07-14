using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Automation.Peers;

/// <summary>Base peer for controls that generate item containers.</summary>
public abstract class ItemsControlAutomationPeer : FrameworkElementAutomationPeer, IItemContainerProvider
{
    private readonly Dictionary<object, ItemAutomationPeer> _itemPeers = new(ReferenceEqualityComparer.Instance);

    protected ItemsControlAutomationPeer(ItemsControl owner)
        : base(owner)
    {
    }

    protected internal ItemsControl ItemsOwner => (ItemsControl)Owner;
    protected virtual bool IsVirtualized => false;

    protected abstract ItemAutomationPeer CreateItemAutomationPeer(object item);

    protected internal virtual ItemAutomationPeer FindOrCreateItemAutomationPeer(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_itemPeers.TryGetValue(item, out ItemAutomationPeer? peer))
        {
            peer = CreateItemAutomationPeer(item);
            _itemPeers.Add(item, peer);
        }

        return peer;
    }

    protected override List<AutomationPeer> GetChildrenCore()
    {
        List<AutomationPeer> children = [];
        foreach (object item in ItemsOwner.Items)
        {
            children.Add(FindOrCreateItemAutomationPeer(item));
        }

        return children;
    }

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ItemContainer ? this : base.GetPatternCore(patternInterface);

    IRawElementProviderSimple? IItemContainerProvider.FindItemByProperty(
        IRawElementProviderSimple? startAfterProvider,
        int propertyId,
        object? value)
    {
        AutomationPeer? startAfter = startAfterProvider is null ? null : PeerFromProvider(startAfterProvider);
        bool canReturn = startAfter is null;
        foreach (object item in ItemsOwner.Items)
        {
            ItemAutomationPeer peer = FindOrCreateItemAutomationPeer(item);
            if (!canReturn)
            {
                canReturn = ReferenceEquals(peer, startAfter);
                continue;
            }

            if (value is null || Equals(item, value) || string.Equals(peer.GetName(), value as string, StringComparison.Ordinal))
            {
                return ProviderFromPeer(peer);
            }
        }

        return null;
    }
}

internal sealed class GenericItemsControlAutomationPeer : ItemsControlAutomationPeer
{
    internal GenericItemsControlAutomationPeer(ItemsControl owner)
        : base(owner)
    {
    }

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new(item, this);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
    protected override string GetClassNameCore() => nameof(ItemsControl);
}

/// <summary>Represents an item exposed by an <see cref="ItemsControlAutomationPeer"/>.</summary>
public class ItemAutomationPeer : AutomationPeer
{
    public ItemAutomationPeer(object item, ItemsControlAutomationPeer itemsControlAutomationPeer)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        ItemsControlAutomationPeer = itemsControlAutomationPeer ?? throw new ArgumentNullException(nameof(itemsControlAutomationPeer));
    }

    protected internal ItemAutomationPeer(object item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public object Item { get; }
    protected internal ItemsControlAutomationPeer? ItemsControlAutomationPeer { get; }
    protected new UIElement Owner => ItemContainer ?? throw new InvalidOperationException("The item has no realized UIElement container.");
    protected UIElement? ItemContainer => Item as UIElement ??
        ItemsControlAutomationPeer?.ItemsOwner.ItemContainerGenerator.ContainerFromItem(Item) as UIElement;

    public override object? GetPattern(PatternInterface patternInterface) => GetPatternCore(patternInterface);
    protected virtual object? GetPatternCore(PatternInterface patternInterface) => null;
    protected override DependencyObject? GetAutomationOwnerCore() => ItemContainer;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
    protected override string GetClassNameCore() => ItemContainer?.GetType().Name ?? Item.GetType().Name;
    protected override string GetNameCore() => Item.ToString() ?? string.Empty;
    protected override string GetAutomationIdCore() => string.Empty;
    protected override string GetHelpTextCore() => string.Empty;
    protected override string GetAcceleratorKeyCore() => string.Empty;
    protected override string GetAccessKeyCore() => string.Empty;
    protected override string GetItemStatusCore() => string.Empty;
    protected override string GetItemTypeCore() => string.Empty;
    protected override string GetLocalizedControlTypeCore() => GetAutomationControlTypeCore().ToString();
    protected override Rect GetBoundingRectangleCore() => ItemContainer is UIElement element
        ? element.MapLocalRectToScreen(new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height))
        : Rect.Empty;
    protected override Point GetClickablePointCore()
    {
        Rect bounds = GetBoundingRectangleCore();
        return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
    protected override bool IsEnabledCore() => ItemContainer?.IsEnabled ?? true;
    protected override bool IsKeyboardFocusableCore() => ItemContainer?.Focusable ?? false;
    protected override bool HasKeyboardFocusCore() => ItemContainer?.IsKeyboardFocused ?? false;
    protected override bool IsOffscreenCore() => ItemContainer?.Visibility != Visibility.Visible;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsPasswordCore() => false;
    protected override bool IsRequiredForFormCore() => false;
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override AutomationPeer? GetParentCore() => ItemsControlAutomationPeer;
    protected override List<AutomationPeer> GetChildrenCore() => [];
    protected override void SetFocusCore() => ItemContainer?.Focus();
}

/// <summary>Base peer for selectors.</summary>
public abstract class SelectorAutomationPeer : ItemsControlAutomationPeer,
    Jalium.UI.Automation.Provider.ISelectionProvider
{
    protected SelectorAutomationPeer(Selector owner)
        : base(owner)
    {
    }

    protected Selector SelectorOwner => (Selector)Owner;
    public virtual bool CanSelectMultiple => false;
    public virtual bool IsSelectionRequired => false;

    public virtual AutomationPeer[] GetSelection()
    {
        object? selected = SelectorOwner.SelectedItem;
        return selected is null ? [] : [FindOrCreateItemAutomationPeer(selected)];
    }

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);

    Jalium.UI.Automation.Provider.IRawElementProviderSimple[] Jalium.UI.Automation.Provider.ISelectionProvider.GetSelection() =>
        GetSelection().Select(peer => ProviderFromPeer(peer)!).ToArray();
    bool Jalium.UI.Automation.Provider.ISelectionProvider.CanSelectMultiple => CanSelectMultiple;
    bool Jalium.UI.Automation.Provider.ISelectionProvider.IsSelectionRequired => IsSelectionRequired;
}

/// <summary>Base peer for selectable items.</summary>
public class SelectorItemAutomationPeer : ItemAutomationPeer,
    Jalium.UI.Automation.Provider.ISelectionItemProvider,
    Jalium.UI.Automation.Provider.IVirtualizedItemProvider
{
    public SelectorItemAutomationPeer(object item, SelectorAutomationPeer selectorAutomationPeer)
        : base(item, selectorAutomationPeer)
    {
        SelectorAutomationPeer = selectorAutomationPeer;
    }

    protected internal SelectorItemAutomationPeer(UIElement container)
        : base(container)
    {
    }

    protected SelectorAutomationPeer? SelectorAutomationPeer { get; }
    public virtual bool IsSelected => ItemContainer is DependencyObject container && Selector.GetIsSelected(container);
    public virtual AutomationPeer SelectionContainer => SelectorAutomationPeer ?? ItemsControlAutomationPeer!;
    public virtual void Select() => SetSelected(true);
    public virtual void AddToSelection() => SetSelected(true);
    public virtual void RemoveFromSelection() => SetSelected(false);
    public virtual void Realize()
    {
        if (ItemContainer is UIElement realized)
        {
            LogicalTreeHelper.BringIntoView(realized);
            return;
        }

        ItemsControl? owner = ItemsControlAutomationPeer?.ItemsOwner;
        if (owner is ListBox listBox)
        {
            listBox.ScrollIntoView(Item);
            return;
        }

        int index = owner?.Items.IndexOf(Item) ?? -1;
        if (index >= 0 && owner?.ItemsHostInternal is VirtualizingPanel panel)
        {
            panel.BringIndexIntoViewPublic(index);
        }
    }
    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);

    private void SetSelected(bool value)
    {
        if (ItemContainer is DependencyObject container)
        {
            Selector.SetIsSelected(container, value);
        }
    }

    bool Jalium.UI.Automation.Provider.ISelectionItemProvider.IsSelected => IsSelected;
    Jalium.UI.Automation.Provider.IRawElementProviderSimple? Jalium.UI.Automation.Provider.ISelectionItemProvider.SelectionContainer =>
        SelectionContainer is null ? null : ProviderFromPeer(SelectionContainer);
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.Select() => Select();
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.AddToSelection() => AddToSelection();
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.RemoveFromSelection() => RemoveFromSelection();
    void Jalium.UI.Automation.Provider.IVirtualizedItemProvider.Realize() => Realize();
}

/// <summary>Base peer for range controls.</summary>
public class RangeBaseAutomationPeer : FrameworkElementAutomationPeer, Jalium.UI.Automation.Provider.IRangeValueProvider
{
    protected RangeBaseAutomationPeer(RangeBase owner)
        : base(owner)
    {
    }

    protected RangeBase RangeOwner => (RangeBase)Owner;
    public virtual double Value => RangeOwner.Value;
    public virtual double Minimum => RangeOwner.Minimum;
    public virtual double Maximum => RangeOwner.Maximum;
    public virtual double SmallChange => RangeOwner.SmallChange;
    public virtual double LargeChange => RangeOwner.LargeChange;
    public virtual bool IsReadOnly => !RangeOwner.IsEnabled;
    public virtual void SetValue(double value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("The range control is read-only.");
        }
        RangeOwner.Value = value;
    }
    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);
}

/// <summary>Base peer for text controls.</summary>
public abstract class TextAutomationPeer : FrameworkElementAutomationPeer
{
    protected TextAutomationPeer(FrameworkElement owner)
        : base(owner)
    {
    }

    /// <summary>Raises the active text-position automation event.</summary>
    public virtual void RaiseActiveTextPositionChangedEvent(
        Jalium.UI.Documents.TextPointer rangeStart,
        Jalium.UI.Documents.TextPointer rangeEnd)
    {
        ArgumentNullException.ThrowIfNull(rangeStart);
        ArgumentNullException.ThrowIfNull(rangeEnd);
        RaiseAutomationEvent(AutomationEvents.ActiveTextPositionChanged);
    }
}

internal sealed class GenericButtonBaseAutomationPeer : ButtonBaseAutomationPeer
{
    internal GenericButtonBaseAutomationPeer(ButtonBase owner)
        : base(owner)
    {
    }
}
