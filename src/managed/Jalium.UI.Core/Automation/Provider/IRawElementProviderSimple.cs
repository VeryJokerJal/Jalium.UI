using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Automation.Provider;

/// <summary>Represents a raw accessibility provider associated with an automation peer.</summary>
public interface IRawElementProviderSimple
{
}

/// <summary>Exposes a single, stateless action to UI Automation clients.</summary>
/// <remarks>
/// The legacy Jalium provider contracts originally lived directly under
/// <c>Jalium.UI.Automation</c>.  The canonical interfaces live in the WPF-compatible
/// <c>Provider</c> namespace and inherit the legacy contracts so existing platform
/// adapters keep working while new peers expose the correct public type identity.
/// </remarks>
public interface IInvokeProvider : Automation.IInvokeProvider
{
    new void Invoke();
}

public interface IToggleProvider : Automation.IToggleProvider
{
    new Automation.ToggleState ToggleState { get; }
    new void Toggle();
}

public interface IValueProvider : Automation.IValueProvider
{
    new string Value { get; }
    new bool IsReadOnly { get; }
    new void SetValue(string value);
}

public interface IRangeValueProvider : Automation.IRangeValueProvider
{
    new double Value { get; }
    new double Minimum { get; }
    new double Maximum { get; }
    new double SmallChange { get; }
    new double LargeChange { get; }
    new bool IsReadOnly { get; }
    new void SetValue(double value);
}

public interface IExpandCollapseProvider : Automation.IExpandCollapseProvider
{
    new Automation.ExpandCollapseState ExpandCollapseState { get; }
    new void Expand();
    new void Collapse();
}

public interface IScrollProvider : Automation.IScrollProvider
{
    new double HorizontalScrollPercent { get; }
    new double VerticalScrollPercent { get; }
    new double HorizontalViewSize { get; }
    new double VerticalViewSize { get; }
    new bool HorizontallyScrollable { get; }
    new bool VerticallyScrollable { get; }
    new void Scroll(Automation.ScrollAmount horizontalAmount, Automation.ScrollAmount verticalAmount);
    new void SetScrollPercent(double horizontalPercent, double verticalPercent);
}

public interface IScrollItemProvider : Automation.IScrollItemProvider
{
    new void ScrollIntoView();
}

/// <summary>Realizes an item that is currently represented only by a virtualized peer.</summary>
public interface IVirtualizedItemProvider
{
    void Realize();
}

public interface ISelectionProvider
{
    IRawElementProviderSimple[] GetSelection();
    bool CanSelectMultiple { get; }
    bool IsSelectionRequired { get; }
}

public interface ISelectionItemProvider
{
    bool IsSelected { get; }
    IRawElementProviderSimple? SelectionContainer { get; }
    void AddToSelection();
    void RemoveFromSelection();
    void Select();
}

public interface IGridProvider
{
    int RowCount { get; }
    int ColumnCount { get; }
    IRawElementProviderSimple? GetItem(int row, int column);
}

public interface ITableProvider : IGridProvider
{
    Automation.RowOrColumnMajor RowOrColumnMajor { get; }
    IRawElementProviderSimple[] GetColumnHeaders();
    IRawElementProviderSimple[] GetRowHeaders();
}

public interface IItemContainerProvider
{
    IRawElementProviderSimple? FindItemByProperty(
        IRawElementProviderSimple? startAfterProvider,
        int propertyId,
        object? value);
}

public interface IMultipleViewProvider
{
    int CurrentView { get; }
    int[] GetSupportedViews();
    string GetViewName(int viewId);
    void SetCurrentView(int viewId);
}

internal interface IAutomationPeerRawProvider : IRawElementProviderSimple
{
    AutomationPeer Peer { get; }
}

internal sealed class AutomationPeerRawProvider : IAutomationPeerRawProvider
{
    internal AutomationPeerRawProvider(AutomationPeer peer)
    {
        Peer = peer;
    }

    public AutomationPeer Peer { get; }
}
