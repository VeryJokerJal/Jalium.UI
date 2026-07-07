using System.Runtime.InteropServices;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.Uia;

namespace Jalium.UI.Tests;

/// <summary>
/// Self-consistency tests for the source-generated COM UIA providers. They build a real
/// StrategyBasedComWrappers CCW for a provider and invoke it THROUGH its native vtable —
/// exactly the way UIAutomationCore would — validating QueryInterface across the
/// IRawElementProviderSimple/Fragment/FragmentRoot inheritance chain, the HRESULT method
/// shapes, the blittable <see cref="UiaVariant"/>, the custom SAFEARRAY marshaller, and a
/// pattern-wrapper CCW.
///
/// NOTE: these use the interface definitions in THIS assembly, so they prove the CCW is
/// internally consistent (marshalling round-trips, no crashes, correct slot wiring against
/// our own vtable) — they cannot prove the vtable matches the real UIA ABI. That requires a
/// live client (Narrator / Inspect / a translation tool). See the migration notes.
/// </summary>
public class UiaComWrappersTests
{
    // IUnknown / provider vtable slot indices (0-2 = QueryInterface/AddRef/Release).
    private const int Slot_get_ProviderOptions = 3;
    private const int Slot_GetPatternProvider = 4;
    private const int Slot_GetPropertyValue = 5;
    private const int Slot_GetRuntimeId = 8; // on the IRawElementProviderFragment vtable

    // IUiaTextProvider vtable slots (3..8 after IUnknown 0-2).
    private const int Slot_Text_GetSelection = 3;
    private const int Slot_Text_SupportedTextSelection = 8;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIntOut(nint self, out int retVal);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPropertyValueFn(nint self, int propertyId, out UiaVariant retVal);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPatternProviderFn(nint self, int patternId, out nint retVal);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPtrOut(nint self, out nint retVal);

    private static T Slot<T>(nint pUnk, int index) where T : Delegate
    {
        nint vtbl = Marshal.ReadIntPtr(pUnk);
        nint fn = Marshal.ReadIntPtr(vtbl, index * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    private static nint QueryInterface(nint pUnk, Guid iid)
    {
        int hr = Marshal.QueryInterface(pUnk, ref iid, out nint p);
        Assert.True(hr == 0, $"QueryInterface({iid}) failed: 0x{hr:X8}");
        Assert.NotEqual(nint.Zero, p);
        return p;
    }

    [Fact]
    public void UiaVariant_MatchesNativeVariantSize()
    {
        // sizeof(VARIANT) is 24 on 64-bit (x64/arm64) and 16 on x86. UiaVariant is passed by
        // value to UiaRaiseAutomationPropertyChangedEvent, so an undersized struct is an OOB read.
        int expected = nint.Size == 8 ? 24 : 16;
        Assert.Equal(expected, Marshal.SizeOf<UiaVariant>());
    }

    [Fact]
    public void Ccw_ExposesProviderInterfaceChain_AndCoreVtableWorks()
    {
        var button = new Button();
        var peer = button.GetAutomationPeer();
        Assert.NotNull(peer);
        var provider = UiaAccessibilityBridge.GetOrCreateProvider(peer!, nint.Zero);

        nint pSimple = UiaComInterop.ProviderToPointer(provider);
        Assert.NotEqual(nint.Zero, pSimple);
        try
        {
            // QueryInterface must succeed for every IID in the inheritance chain.
            Marshal.Release(QueryInterface(pSimple, typeof(IRawElementProviderSimple).GUID));
            nint pFragment = QueryInterface(pSimple, typeof(IRawElementProviderFragment).GUID);
            Marshal.Release(QueryInterface(pSimple, typeof(IRawElementProviderFragmentRoot).GUID));

            // get_ProviderOptions (slot 3) -> ServerSideProvider | UseComThreading.
            int hr = Slot<GetIntOut>(pSimple, Slot_get_ProviderOptions)(pSimple, out int options);
            Assert.Equal(0, hr);
            Assert.Equal((int)(ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading), options);

            // GetPropertyValue (slot 5): ControlType -> VT_I4, ClassName -> VT_BSTR.
            var getProp = Slot<GetPropertyValueFn>(pSimple, Slot_GetPropertyValue);

            hr = getProp(pSimple, UiaConstants.UIA_ControlTypePropertyId, out UiaVariant vType);
            Assert.Equal(0, hr);
            Assert.Equal(UiaVariant.VT_I4, vType.vt);
            Assert.NotEqual(0, vType.lVal);

            hr = getProp(pSimple, UiaConstants.UIA_ClassNamePropertyId, out UiaVariant vClass);
            Assert.Equal(0, hr);
            Assert.Equal(UiaVariant.VT_BSTR, vClass.vt);
            Assert.NotEqual(nint.Zero, vClass.bstrVal);
            Assert.Equal("Button", Marshal.PtrToStringBSTR(vClass.bstrVal));
            vClass.Clear(); // we own the returned VARIANT's BSTR

            // GetRuntimeId (Fragment slot 8) builds a real SAFEARRAY(VT_I4); read it back
            // through the same custom marshaller. Expect [AppendRuntimeId, <hash>].
            hr = Slot<GetPtrOut>(pFragment, Slot_GetRuntimeId)(pFragment, out nint pSafeArray);
            Assert.Equal(0, hr);
            Assert.NotEqual(nint.Zero, pSafeArray);
            int[]? runtimeId = SafeArrayInt32Marshaller.ConvertToManaged(pSafeArray);
            SafeArrayInt32Marshaller.Free(pSafeArray);
            Assert.NotNull(runtimeId);
            Assert.Equal(2, runtimeId!.Length);
            Assert.Equal(provider.GetRuntimeIdArray(), runtimeId);

            Marshal.Release(pFragment);
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pSimple);
        }
    }

    [Fact]
    public void Ccw_GetPatternProvider_ReturnsQueryablePatternWrapper()
    {
        // A Button peer exposes the Invoke pattern.
        var button = new Button { Content = "Hi" };
        var peer = button.GetAutomationPeer();
        Assert.NotNull(peer);
        var provider = UiaAccessibilityBridge.GetOrCreateProvider(peer!, nint.Zero);

        nint pSimple = UiaComInterop.ProviderToPointer(provider);
        try
        {
            int hr = Slot<GetPatternProviderFn>(pSimple, Slot_GetPatternProvider)(
                pSimple, UiaConstants.UIA_InvokePatternId, out nint pPattern);
            Assert.Equal(0, hr);
            Assert.NotEqual(nint.Zero, pPattern);
            try
            {
                // The returned IUnknown must expose the Invoke pattern COM interface.
                Marshal.Release(QueryInterface(pPattern, typeof(IUiaInvokeProvider).GUID));
            }
            finally
            {
                Marshal.Release(pPattern);
            }
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pSimple);
        }
    }

    [Fact]
    public void Ccw_TextPattern_GetSelection_ReturnsSelectedText()
    {
        // A TextBox peer exposes the Text pattern; an external UIA client detects the selected text
        // via GetPatternProvider(Text) -> ITextProvider.GetSelection() -> ITextRangeProvider.GetText().
        var textBox = new TextBox { Text = "Hello World" };
        textBox.Select(6, 5); // selects "World"

        var peer = textBox.GetAutomationPeer();
        Assert.NotNull(peer);
        var provider = UiaAccessibilityBridge.GetOrCreateProvider(peer!, nint.Zero);

        nint pSimple = UiaComInterop.ProviderToPointer(provider);
        try
        {
            int hr = Slot<GetPatternProviderFn>(pSimple, Slot_GetPatternProvider)(
                pSimple, UiaConstants.UIA_TextPatternId, out nint pPattern);
            Assert.Equal(0, hr);
            Assert.NotEqual(nint.Zero, pPattern);

            // The pattern object must QI as the real UIA ITextProvider IID.
            nint pText = QueryInterface(pPattern, typeof(IUiaTextProvider).GUID);
            Marshal.Release(pPattern);
            try
            {
                // get_SupportedTextSelection (slot 8) -> Single.
                hr = Slot<GetIntOut>(pText, Slot_Text_SupportedTextSelection)(pText, out int selKind);
                Assert.Equal(0, hr);
                Assert.Equal((int)SupportedTextSelection.Single, selKind);

                // GetSelection (slot 3) builds a real SAFEARRAY(ITextRangeProvider*) with one range.
                hr = Slot<GetPtrOut>(pText, Slot_Text_GetSelection)(pText, out nint pSelection);
                Assert.Equal(0, hr);
                Assert.NotEqual(nint.Zero, pSelection);
                IUiaTextRangeProvider[]? ranges = SafeArrayTextRangeMarshaller.ConvertToManaged(pSelection);
                SafeArrayTextRangeMarshaller.Free(pSelection);
                Assert.NotNull(ranges);
                Assert.Single(ranges!);

                // The range reads back exactly the selected substring.
                Assert.Equal("World", ranges![0].GetText(-1));
            }
            finally
            {
                Marshal.Release(pText);
            }
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pSimple);
        }
    }

    [Fact]
    public void SafeArrayProviderMarshaller_RoundTripsVtUnknownVector()
    {
        // Directly exercises the VT_UNKNOWN SAFEARRAY path shared by ISelectionProvider.GetSelection
        // and ITextProvider.GetSelection: building the vector from CCWs and reading it back must not
        // crash and must round-trip element identity. (This path is populated via SafeArrayAccessData;
        // the earlier SafeArrayPutElement form access-violated the first time it ran.)
        var p1 = UiaAccessibilityBridge.GetOrCreateProvider(new Button().GetAutomationPeer()!, nint.Zero);
        var p2 = UiaAccessibilityBridge.GetOrCreateProvider(new Button().GetAutomationPeer()!, nint.Zero);

        IRawElementProviderSimple[] src = [p1, p2];
        nint psa = SafeArrayProviderMarshaller.ConvertToUnmanaged(src);
        try
        {
            Assert.NotEqual(nint.Zero, psa);
            IRawElementProviderSimple[]? back = SafeArrayProviderMarshaller.ConvertToManaged(psa);
            Assert.NotNull(back);
            Assert.Equal(2, back!.Length);
            // Our own CCWs unwrap back to the original managed providers, preserving order/identity.
            Assert.Same(p1, back[0]);
            Assert.Same(p2, back[1]);
        }
        finally
        {
            SafeArrayProviderMarshaller.Free(psa);
        }
    }

    [Fact]
    public void TextRange_FindText_Backward_FindsLastOccurrence()
    {
        // Locks in ITextRangeProvider.FindText search direction (used by Narrator "find" and some
        // look-up tools): forward returns the first match, backward the last; a miss returns null.
        var textBox = new TextBox { Text = "ab ab ab" };
        var peer = (Jalium.UI.Controls.Automation.TextBoxAutomationPeer)textBox.GetAutomationPeer()!;
        var provider = UiaAccessibilityBridge.GetOrCreateProvider(peer, nint.Zero);
        var textProvider = new UiaTextProviderWrapper((ITextProvider)peer, provider);

        IUiaTextRangeProvider? doc = textProvider.get_DocumentRange();
        Assert.NotNull(doc);

        IUiaTextRangeProvider? forward = doc!.FindText("ab", backward: 0, ignoreCase: 0);
        IUiaTextRangeProvider? backward = doc.FindText("ab", backward: 1, ignoreCase: 0);
        Assert.NotNull(forward);
        Assert.NotNull(backward);
        Assert.Equal("ab", forward!.GetText(-1));
        Assert.Equal("ab", backward!.GetText(-1));

        // Forward starts at offset 0, backward at offset 6, so forward.Start precedes backward.Start.
        Assert.True(forward.CompareEndpoints(0, backward, 0) < 0);

        // A miss returns null rather than throwing across the COM boundary.
        Assert.Null(doc.FindText("zz", backward: 0, ignoreCase: 0));
    }

    [Theory]
    [InlineData(30031, PatternInterface.Invoke)]
    [InlineData(30037, PatternInterface.Selection)]
    [InlineData(30043, PatternInterface.Value)]
    [InlineData(30033, PatternInterface.RangeValue)]
    [InlineData(30034, PatternInterface.Scroll)]
    [InlineData(30035, PatternInterface.ScrollItem)]
    [InlineData(30028, PatternInterface.ExpandCollapse)]
    [InlineData(30041, PatternInterface.Toggle)]
    [InlineData(30036, PatternInterface.SelectionItem)]
    [InlineData(30040, PatternInterface.Text)]
    public void MapAvailabilityProperty_KeysOnCanonicalSdkPropertyIds(int sdkPropertyId, PatternInterface expected)
    {
        // The IsXxxPatternAvailable ids are the Windows SDK values (UIAutomationClient.h). Passing the
        // LITERAL SDK id here locks both the numeric constant and its pattern mapping in one shot: a
        // swapped constant (e.g. Selection 30037 <-> SelectionItem 30036, or Scroll 30034 <-> ScrollItem
        // 30035) makes the wrong PatternInterface come back and fails this test.
        Assert.Equal(expected, UiaConstants.MapAvailabilityPropertyToPatternInterface(sdkPropertyId));
    }

    [Fact]
    public void MapAvailabilityProperty_NonAvailabilityOrUnwrappableId_ReturnsNull()
    {
        // Not a pattern-availability property at all.
        Assert.Null(UiaConstants.MapAvailabilityPropertyToPatternInterface(UiaConstants.UIA_NamePropertyId));

        // Availability ids for patterns the provider cannot wrap (Dock=30027, MultipleView=30032,
        // Table=30038, Transform=30042) must fall through to null -> VT_EMPTY, never map to some other
        // pattern. This is the guard against the mislabeled-constant class of bug.
        Assert.Null(UiaConstants.MapAvailabilityPropertyToPatternInterface(30027));
        Assert.Null(UiaConstants.MapAvailabilityPropertyToPatternInterface(30032));
        Assert.Null(UiaConstants.MapAvailabilityPropertyToPatternInterface(30038));
        Assert.Null(UiaConstants.MapAvailabilityPropertyToPatternInterface(30042));
    }

    [Fact]
    public void Ccw_GetPropertyValue_ReportsPatternAvailabilityAsVtBool()
    {
        // UIA clients gate on IsXxxPatternAvailable before calling GetPatternProvider, so these must
        // answer VT_BOOL true/false through the real vtable slot — not VT_EMPTY. This is the end-to-end
        // wiring the Codex suggestion asked for (Text pattern reported as available).
        var textProvider = UiaAccessibilityBridge.GetOrCreateProvider(
            new TextBox { Text = "hi" }.GetAutomationPeer()!, nint.Zero);
        var buttonProvider = UiaAccessibilityBridge.GetOrCreateProvider(
            new Button { Content = "hi" }.GetAutomationPeer()!, nint.Zero);

        nint pText = UiaComInterop.ProviderToPointer(textProvider);
        nint pButton = UiaComInterop.ProviderToPointer(buttonProvider);
        try
        {
            var getText = Slot<GetPropertyValueFn>(pText, Slot_GetPropertyValue);
            var getButton = Slot<GetPropertyValueFn>(pButton, Slot_GetPropertyValue);

            // TextBox exposes the Text pattern -> IsTextPatternAvailable == VT_BOOL TRUE.
            Assert.Equal(0, getText(pText, UiaConstants.UIA_IsTextPatternAvailablePropertyId, out UiaVariant vTextAvail));
            Assert.Equal(UiaVariant.VT_BOOL, vTextAvail.vt);
            Assert.Equal((short)-1, vTextAvail.boolVal);

            // Button has no Text pattern -> IsTextPatternAvailable == VT_BOOL FALSE (definitive, not VT_EMPTY).
            Assert.Equal(0, getButton(pButton, UiaConstants.UIA_IsTextPatternAvailablePropertyId, out UiaVariant vButtonText));
            Assert.Equal(UiaVariant.VT_BOOL, vButtonText.vt);
            Assert.Equal((short)0, vButtonText.boolVal);

            // Button exposes the Invoke pattern -> IsInvokePatternAvailable == VT_BOOL TRUE.
            Assert.Equal(0, getButton(pButton, UiaConstants.UIA_IsInvokePatternAvailablePropertyId, out UiaVariant vInvoke));
            Assert.Equal(UiaVariant.VT_BOOL, vInvoke.vt);
            Assert.Equal((short)-1, vInvoke.boolVal);
        }
        finally
        {
            UiaComInterop.FreeProviderPointer(pText);
            UiaComInterop.FreeProviderPointer(pButton);
        }
    }
}
