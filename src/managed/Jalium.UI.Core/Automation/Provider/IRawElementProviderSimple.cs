using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Text;

namespace Jalium.UI.Automation.Provider;

/// <summary>Specifies the type of UI Automation provider implementation.</summary>
[Flags]
public enum ProviderOptions
{
    ClientSideProvider = 0x1,
    ServerSideProvider = 0x2,
    NonClientAreaProvider = 0x4,
    OverrideProvider = 0x8,
    ProviderOwnsSetFocus = 0x10,
    UseComThreading = 0x20,
}

/// <summary>Specifies the direction in which to navigate the UI Automation tree.</summary>
public enum NavigateDirection
{
    Parent = 0,
    NextSibling = 1,
    PreviousSibling = 2,
    FirstChild = 3,
    LastChild = 4,
}

/// <summary>Exposes the basic UI Automation provider contract.</summary>
public interface IRawElementProviderSimple
{
    ProviderOptions ProviderOptions { get; }

    IRawElementProviderSimple? HostRawElementProvider { get; }

    object? GetPatternProvider(int patternId);

    object? GetPropertyValue(int propertyId);
}

/// <summary>Exposes an element in a UI Automation fragment.</summary>
public interface IRawElementProviderFragment : IRawElementProviderSimple
{
    Rect BoundingRectangle { get; }

    IRawElementProviderFragmentRoot? FragmentRoot { get; }

    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();

    int[]? GetRuntimeId();

    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    void SetFocus();
}

/// <summary>Exposes the root of a UI Automation fragment.</summary>
public interface IRawElementProviderFragmentRoot : IRawElementProviderFragment
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

/// <summary>Exposes a single, stateless action to UI Automation clients.</summary>
public interface IInvokeProvider
{
    void Invoke();
}

/// <summary>Exposes a control that cycles through a set of states.</summary>
public interface IToggleProvider
{
    Automation.ToggleState ToggleState { get; }

    void Toggle();
}

/// <summary>Exposes a string value.</summary>
public interface IValueProvider
{
    string Value { get; }

    bool IsReadOnly { get; }

    void SetValue(string value);
}

/// <summary>Exposes a value constrained to a range.</summary>
public interface IRangeValueProvider
{
    double Value { get; }

    double Minimum { get; }

    double Maximum { get; }

    double SmallChange { get; }

    double LargeChange { get; }

    bool IsReadOnly { get; }

    void SetValue(double value);
}

/// <summary>Exposes movement, resizing, and rotation operations.</summary>
public interface ITransformProvider
{
    bool CanMove { get; }

    bool CanResize { get; }

    bool CanRotate { get; }

    void Move(double x, double y);

    void Resize(double width, double height);

    void Rotate(double degrees);
}

/// <summary>Exposes an element that can expand and collapse.</summary>
public interface IExpandCollapseProvider
{
    Automation.ExpandCollapseState ExpandCollapseState { get; }

    void Expand();

    void Collapse();
}

/// <summary>Exposes a scrollable container.</summary>
public interface IScrollProvider
{
    double HorizontalScrollPercent { get; }

    double VerticalScrollPercent { get; }

    double HorizontalViewSize { get; }

    double VerticalViewSize { get; }

    bool HorizontallyScrollable { get; }

    bool VerticallyScrollable { get; }

    void Scroll(Automation.ScrollAmount horizontalAmount, Automation.ScrollAmount verticalAmount);

    void SetScrollPercent(double horizontalPercent, double verticalPercent);
}

/// <summary>Exposes an item that can be scrolled into view.</summary>
public interface IScrollItemProvider
{
    void ScrollIntoView();
}

/// <summary>Realizes a virtualized item.</summary>
public interface IVirtualizedItemProvider
{
    void Realize();
}

/// <summary>Exposes a selection container.</summary>
public interface ISelectionProvider
{
    IRawElementProviderSimple[] GetSelection();

    bool CanSelectMultiple { get; }

    bool IsSelectionRequired { get; }
}

/// <summary>Exposes an item within a selection container.</summary>
public interface ISelectionItemProvider
{
    bool IsSelected { get; }

    IRawElementProviderSimple? SelectionContainer { get; }

    void AddToSelection();

    void RemoveFromSelection();

    void Select();
}

/// <summary>Exposes a two-dimensional grid.</summary>
public interface IGridProvider
{
    int RowCount { get; }

    int ColumnCount { get; }

    IRawElementProviderSimple? GetItem(int row, int column);
}

/// <summary>Exposes a table and its headers.</summary>
public interface ITableProvider : IGridProvider
{
    Automation.RowOrColumnMajor RowOrColumnMajor { get; }

    IRawElementProviderSimple[] GetColumnHeaders();

    IRawElementProviderSimple[] GetRowHeaders();
}

/// <summary>Locates items in a container.</summary>
public interface IItemContainerProvider
{
    IRawElementProviderSimple? FindItemByProperty(
        IRawElementProviderSimple? startAfter,
        int propertyId,
        object? value);
}

/// <summary>Exposes a control with multiple views.</summary>
public interface IMultipleViewProvider
{
    int CurrentView { get; }

    int[] GetSupportedViews();

    string GetViewName(int viewId);

    void SetCurrentView(int viewId);
}

/// <summary>Exposes text content and text ranges.</summary>
public interface ITextProvider
{
    ITextRangeProvider DocumentRange { get; }

    Automation.SupportedTextSelection SupportedTextSelection { get; }

    ITextRangeProvider[] GetSelection();

    ITextRangeProvider[] GetVisibleRanges();

    ITextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement);

    ITextRangeProvider? RangeFromPoint(Point screenLocation);
}

/// <summary>Exposes a continuous range in a text provider.</summary>
public interface ITextRangeProvider
{
    void AddToSelection();

    ITextRangeProvider Clone();

    bool Compare(ITextRangeProvider range);

    int CompareEndpoints(
        TextPatternRangeEndpoint endpoint,
        ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint);

    void ExpandToEnclosingUnit(TextUnit unit);

    ITextRangeProvider? FindAttribute(int attribute, object? value, bool backward);

    ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase);

    object? GetAttributeValue(int attribute);

    double[] GetBoundingRectangles();

    IRawElementProviderSimple[] GetChildren();

    IRawElementProviderSimple? GetEnclosingElement();

    string GetText(int maxLength);

    int Move(TextUnit unit, int count);

    void MoveEndpointByRange(
        TextPatternRangeEndpoint endpoint,
        ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint);

    int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count);

    void RemoveFromSelection();

    void ScrollIntoView(bool alignToTop);

    void Select();
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

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public object? GetPatternProvider(int patternId) => patternId switch
    {
        10000 => Peer.GetPattern(PatternInterface.Invoke),
        10001 => Peer.GetPattern(PatternInterface.Selection),
        10002 => Peer.GetPattern(PatternInterface.Value),
        10003 => Peer.GetPattern(PatternInterface.RangeValue),
        10004 => Peer.GetPattern(PatternInterface.Scroll),
        10005 => Peer.GetPattern(PatternInterface.ExpandCollapse),
        10006 => Peer.GetPattern(PatternInterface.Grid),
        10007 => Peer.GetPattern(PatternInterface.GridItem),
        10008 => Peer.GetPattern(PatternInterface.MultipleView),
        10009 => Peer.GetPattern(PatternInterface.Window),
        10010 => Peer.GetPattern(PatternInterface.SelectionItem),
        10011 => Peer.GetPattern(PatternInterface.Dock),
        10012 => Peer.GetPattern(PatternInterface.Table),
        10013 => Peer.GetPattern(PatternInterface.TableItem),
        10014 => Peer.GetPattern(PatternInterface.Text),
        10015 => Peer.GetPattern(PatternInterface.Toggle),
        10016 => Peer.GetPattern(PatternInterface.Transform),
        10017 => Peer.GetPattern(PatternInterface.ScrollItem),
        10019 => Peer.GetPattern(PatternInterface.ItemContainer),
        10020 => Peer.GetPattern(PatternInterface.VirtualizedItem),
        10021 => Peer.GetPattern(PatternInterface.SynchronizedInput),
        _ => null,
    };

    public object? GetPropertyValue(int propertyId) => null;
}
