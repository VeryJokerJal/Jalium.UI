using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Automation.Uia;

// ============================================================================
// UIA Provider Interfaces
//
// Matches WPF's approach: [ComVisible(true)] + [InterfaceType(InterfaceIsIUnknown)]
// WITHOUT [PreserveSig] — CLR auto-handles HRESULT conversion.
// WITHOUT [ComImport] — these are interfaces we IMPLEMENT, not import.
// ============================================================================

[ComVisible(true)]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRawElementProviderSimple
{
    ProviderOptions ProviderOptions { get; }

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    object? GetPropertyValue(int propertyId);

    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComVisible(true)]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRawElementProviderFragment : IRawElementProviderSimple
{
    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    int[] GetRuntimeId();

    UiaRect BoundingRectangle { get; }

    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot FragmentRoot { get; }
}

[ComVisible(true)]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRawElementProviderFragmentRoot : IRawElementProviderFragment
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

// ============================================================================
// UIA Pattern Provider Interfaces
// ============================================================================

[ComVisible(true)]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaInvokeProvider
{
    void Invoke();
}

[ComVisible(true)]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaToggleProvider
{
    void Toggle();
    int ToggleState { get; }
}

[ComVisible(true)]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaValueProvider
{
    void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);
    string Value { get; }
    bool IsReadOnly { get; }
}

[ComVisible(true)]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaRangeValueProvider
{
    void SetValue(double value);
    double Value { get; }
    bool IsReadOnly { get; }
    double Maximum { get; }
    double Minimum { get; }
    double LargeChange { get; }
    double SmallChange { get; }
}

[ComVisible(true)]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaExpandCollapseProvider
{
    void Expand();
    void Collapse();
    int ExpandCollapseState { get; }
}

[ComVisible(true)]
[Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaSelectionProvider
{
    IRawElementProviderSimple[]? GetSelection();
    bool CanSelectMultiple { get; }
    bool IsSelectionRequired { get; }
}

[ComVisible(true)]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaSelectionItemProvider
{
    void Select();
    void AddToSelection();
    void RemoveFromSelection();
    bool IsSelected { get; }
    IRawElementProviderSimple? SelectionContainer { get; }
}

[ComVisible(true)]
[Guid("b38b8077-1fc3-42a5-8cae-d40c2215055a")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaScrollProvider
{
    void Scroll(int horizontalAmount, int verticalAmount);
    void SetScrollPercent(double horizontalPercent, double verticalPercent);
    double HorizontalScrollPercent { get; }
    double VerticalScrollPercent { get; }
    double HorizontalViewSize { get; }
    double VerticalViewSize { get; }
    bool HorizontallyScrollable { get; }
    bool VerticallyScrollable { get; }
}

[ComVisible(true)]
[Guid("2360c714-4bf1-4b26-ba65-9b21316127eb")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUiaScrollItemProvider
{
    void ScrollIntoView();
}

// ============================================================================
// UIA Structures
// ============================================================================

[StructLayout(LayoutKind.Sequential)]
public struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}
