using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Jalium.UI.Controls.Automation.Uia;

// ============================================================================
// UIA Provider Interfaces — source-generated COM ([GeneratedComInterface]).
//
// Why source-gen instead of classic [ComVisible]/[InterfaceType]:
// under NativeAOT there is no built-in COM interop, so a classic CCW cannot be
// built for our managed providers and any UIA marshal throws NotSupportedException
// (see UiaAccessibilityBridge). [GeneratedComInterface] + [GeneratedComClass] emit
// the vtable/marshalling at compile time so a StrategyBasedComWrappers CCW works
// under both JIT and AOT.
//
// ABI RULES honored here (see docs/uia-aot-design):
//   * IRawElementProviderSimple, IRawElementProviderFragment, and
//     IRawElementProviderFragmentRoot are sibling COM interfaces in UIAutomationCore.h.
//     FragmentRoot does not inherit Fragment, and Fragment does not inherit Simple. A
//     provider object may implement all three and UIA uses QueryInterface to switch
//     between them, but every interface vtable starts at its own slot 3.
//   * .NET 10's COM source generator does NOT support C# properties (that shipped in
//     .NET 11), so every UIA property is modelled as an explicit HRESULT-returning
//     get_* METHOD in exact native vtable order. A C# member returning a value T is
//     lowered by the generator to native `HRESULT M(..., T* retVal)`; a `void` member
//     to `HRESULT M(...)`. Do NOT reorder members — COM dispatch is by vtable slot.
//   * VARIANT return  -> [MarshalUsing(typeof(ComVariantMarshaller))] object?
//     IUnknown return -> PreserveSig raw nint* out pointer (see GetPatternProvider)
//     BSTR return     -> [MarshalUsing(typeof(BStrStringMarshaller))] string
//     SAFEARRAY return-> custom [MarshalUsing(typeof(SafeArray*Marshaller))] (source-gen
//                        has no SAFEARRAY support; see UiaSafeArrayMarshallers).
//     Win32 BOOL      -> `int` (0/1) to match UIA's 4-byte BOOL unambiguously.
// ============================================================================

// (Fragment/FragmentRoot) must be unsafe too — the COM source generator re-emits this
[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal unsafe partial interface IRawElementProviderSimple
{
    // [PreserveSig] + raw out-pointer instead of the generator's auto-HRESULT lowering, so we
    // can null-guard pRetVal OURSELVES. The CUAS/TSF text-input stack behind an IME (e.g. the
    // Chinese Pinyin IME) attaches as a UIA client the moment a text field takes focus and
    // probes the provider tree; get_ProviderOptions is its first and highest-frequency call.
    // Under that path it can arrive with a NULL pRetVal while the UI thread is inside an
    // input-synchronous message. The auto-lowered stub would blindly do `*pRetVal = value` and
    // raise a first-chance NullReferenceException — caught at the ABI boundary and returned as
    // E_POINTER, non-fatal, but noisy under a debugger and a needless throw/catch per keystroke.
    // Owning the pointer lets us return E_POINTER cleanly with no managed throw. Native vtable
    // slot signature is unchanged: HRESULT get_ProviderOptions(ProviderOptions* pRetVal).
    [PreserveSig]
    unsafe int get_ProviderOptions(ProviderOptions* pRetVal);

    // [PreserveSig] + raw IUnknown** out pointer. The source generator's generic
    // object->IUnknown return path can fault inside ComInterfaceMarshaller<object>
    // when aggressive UIA clients probe unsupported patterns, so the provider owns
    // null handling and typed pattern-wrapper marshalling directly.
    [PreserveSig]
    unsafe int GetPatternProvider(int patternId, nint* pRetVal);

    // Returns a blittable VARIANT struct (ABI-identical to VARIANT*), built by the provider.
    // The object<->VARIANT auto-marshaller requires assembly-wide [DisableRuntimeMarshalling]
    // (SYSLIB1051), not viable here; a plain blittable struct needs no runtime marshalling.
    [PreserveSig]
    unsafe int GetPropertyValue(int propertyId, UiaVariant* pRetVal);

    [PreserveSig]
    unsafe int get_HostRawElementProvider(nint* pRetVal);
}

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    [return: MarshalUsing(typeof(SafeArrayInt32Marshaller))]
    int[]? GetRuntimeId();

    UiaRect get_BoundingRectangle();

    [return: MarshalUsing(typeof(SafeArrayProviderMarshaller))]
    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot? get_FragmentRoot();
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

// ============================================================================
// UIA Pattern Provider Interfaces (IIDs are the real UIA pattern interface IIDs;
// UIA QIs the pattern-wrapper CCW for these). Member order == native vtable order.
// ============================================================================

[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IUiaInvokeProvider
{
    void Invoke();
}

[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IUiaToggleProvider
{
    void Toggle();
    int get_ToggleState();
}

[GeneratedComInterface]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IUiaValueProvider
{
    void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);

    [return: MarshalUsing(typeof(BStrStringMarshaller))]
    string get_Value();

    int get_IsReadOnly();
}

[GeneratedComInterface]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IUiaRangeValueProvider
{
    void SetValue(double value);
    double get_Value();
    int get_IsReadOnly();
    double get_Maximum();
    double get_Minimum();
    double get_LargeChange();
    double get_SmallChange();
}

[GeneratedComInterface]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IUiaExpandCollapseProvider
{
    void Expand();
    void Collapse();
    int get_ExpandCollapseState();
}

[GeneratedComInterface]
[Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
internal partial interface IUiaSelectionProvider
{
    [return: MarshalUsing(typeof(SafeArrayProviderMarshaller))]
    IRawElementProviderSimple[]? GetSelection();

    int get_CanSelectMultiple();
    int get_IsSelectionRequired();
}

[GeneratedComInterface]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface IUiaSelectionItemProvider
{
    void Select();
    void AddToSelection();
    void RemoveFromSelection();
    int get_IsSelected();
    IRawElementProviderSimple? get_SelectionContainer();
}

[GeneratedComInterface]
[Guid("b38b8077-1fc3-42a5-8cae-d40c2215055a")]
internal partial interface IUiaScrollProvider
{
    void Scroll(int horizontalAmount, int verticalAmount);
    void SetScrollPercent(double horizontalPercent, double verticalPercent);
    double get_HorizontalScrollPercent();
    double get_VerticalScrollPercent();
    double get_HorizontalViewSize();
    double get_VerticalViewSize();
    int get_HorizontallyScrollable();
    int get_VerticallyScrollable();
}

[GeneratedComInterface]
[Guid("2360c714-4bf1-4b26-ba65-9b21316127eb")]
internal partial interface IUiaScrollItemProvider
{
    void ScrollIntoView();
}

// ============================================================================
// Text pattern. ITextProvider hands out ITextRangeProvider ranges; UIA clients call
// GetSelection() to discover the currently selected text and GetText() to read it. Member order
// is the native UIA vtable order (each value-returning member is lowered by the source generator
// to HRESULT M(..., T* retVal), each void member to HRESULT M(...)). TextUnit and
// TextPatternRangeEndpoint arguments travel as int (matching the native enums); SupportedTextSelection
// is returned as int, mirroring how ToggleState/ExpandCollapseState are surfaced.
// ============================================================================

[GeneratedComInterface]
[Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface IUiaTextProvider
{
    [return: MarshalUsing(typeof(SafeArrayTextRangeMarshaller))]
    IUiaTextRangeProvider[]? GetSelection();

    [return: MarshalUsing(typeof(SafeArrayTextRangeMarshaller))]
    IUiaTextRangeProvider[]? GetVisibleRanges();

    IUiaTextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement);

    IUiaTextRangeProvider? RangeFromPoint(UiaPoint point);

    IUiaTextRangeProvider? get_DocumentRange();

    int get_SupportedTextSelection();
}

[GeneratedComInterface]
[Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface IUiaTextRangeProvider
{
    IUiaTextRangeProvider? Clone();

    int Compare(IUiaTextRangeProvider range);

    int CompareEndpoints(int endpoint, IUiaTextRangeProvider targetRange, int targetEndpoint);

    void ExpandToEnclosingUnit(int unit);

    IUiaTextRangeProvider? FindAttribute(int attributeId, UiaVariant val, int backward);

    IUiaTextRangeProvider? FindText(
        [MarshalUsing(typeof(BStrStringMarshaller))] string text, int backward, int ignoreCase);

    UiaVariant GetAttributeValue(int attributeId);

    [return: MarshalUsing(typeof(SafeArrayDoubleMarshaller))]
    double[]? GetBoundingRectangles();

    IRawElementProviderSimple? GetEnclosingElement();

    [return: MarshalUsing(typeof(BStrStringMarshaller))]
    string GetText(int maxLength);

    int Move(int unit, int count);

    int MoveEndpointByUnit(int endpoint, int unit, int count);

    void MoveEndpointByRange(int endpoint, IUiaTextRangeProvider targetRange, int targetEndpoint);

    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    void ScrollIntoView(int alignToTop);

    [return: MarshalUsing(typeof(SafeArrayProviderMarshaller))]
    IRawElementProviderSimple[]? GetChildren();
}

// ============================================================================
// UIA Structures
// ============================================================================

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

/// <summary>Blittable UiaPoint (a pair of doubles) passed by value to ITextProvider.RangeFromPoint.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public double X;
    public double Y;
}
