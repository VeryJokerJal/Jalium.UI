using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// A minimal, blittable COM <c>VARIANT</c> covering the value kinds UIA <c>GetPropertyValue</c>
/// and property-changed events produce: VT_EMPTY, VT_I4, VT_BOOL and VT_BSTR.
/// </summary>
/// <remarks>
/// The layout matches <c>sizeof(VARIANT)</c> on every architecture — 24 bytes on x64/arm64,
/// 16 bytes on x86 — via two native-int payload slots that mirror VARIANT's widest union
/// member (BRECORD = <c>{ PVOID pvRecord; IRecordInfo* pRecInfo; }</c>, 16 bytes on 64-bit).
/// Matching the exact size is essential because these are passed BY VALUE to
/// <c>UiaRaiseAutomationPropertyChangedEvent</c>: a fixed 16-byte struct would be an 8-byte
/// over-read on 64-bit. Used instead of <see cref="System.Runtime.InteropServices.Marshalling.ComVariant"/>
/// because the COM source generator refuses ComVariant returns without assembly-wide
/// <c>[DisableRuntimeMarshalling]</c> (SYSLIB1051), which is not viable for this assembly.
/// The caller (UIA) owns any VARIANT we return and calls VariantClear (freeing the BSTR); a
/// VARIANT we pass to UIA by value we <see cref="Clear"/> ourselves after the call.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaVariant
{
    internal const ushort VT_EMPTY = 0;
    internal const ushort VT_I4 = 3;
    internal const ushort VT_BSTR = 8;
    internal const ushort VT_BOOL = 11;
    internal const ushort VT_UNKNOWN = 13;

    // Field ORDER is the ABI. vt@0, three reserved WORDs push the union to VARIANT offset 8 on
    // every arch, _payload is the union, _payloadHigh is the union's second half (BRECORD tail)
    // that makes the natural size equal sizeof(VARIANT). The reserved/tail fields are layout
    // padding only and intentionally never read.
#pragma warning disable CS0169
    /// <summary>VARTYPE discriminator (VARIANT offset 0).</summary>
    public ushort vt;
    private readonly ushort _reserved1;
    private readonly ushort _reserved2;
    private readonly ushort _reserved3;
    private nint _payload;      // union @ offset 8: lVal / boolVal / bstrVal
    private readonly nint _payloadHigh;
#pragma warning restore CS0169

    /// <summary>VT_I4 payload (LONG at VARIANT offset 8).</summary>
    public int lVal
    {
        readonly get => (int)_payload;
        set => _payload = (nint)(uint)value;
    }

    /// <summary>VT_BOOL payload (VARIANT_BOOL at offset 8; VARIANT_TRUE = -1).</summary>
    public short boolVal
    {
        readonly get => (short)_payload;
        set => _payload = (nint)(ushort)value;
    }

    /// <summary>VT_BSTR payload (BSTR pointer at offset 8).</summary>
    public nint bstrVal
    {
        readonly get => _payload;
        set => _payload = value;
    }

    /// <summary>VT_UNKNOWN payload (IUnknown* at offset 8).</summary>
    public nint punkVal
    {
        readonly get => _payload;
        set => _payload = value;
    }

    /// <summary>
    /// Wraps an owned IUnknown pointer as a VT_UNKNOWN VARIANT. Used to return UIA's reserved
    /// "not supported" sentinel from <c>ITextRangeProvider.GetAttributeValue</c>. The one reference
    /// obtained from <c>UiaGetReservedNotSupportedValue</c> is transferred to the caller (UIA),
    /// which releases it via VariantClear — so do NOT AddRef again and do NOT <see cref="Clear"/> it.
    /// </summary>
    internal static UiaVariant FromUnknown(nint punk)
    {
        var v = default(UiaVariant);
        v.vt = VT_UNKNOWN;
        v._payload = punk;
        return v;
    }

    internal static UiaVariant From(object? value)
    {
        var v = default(UiaVariant);
        switch (value)
        {
            case null:
                v.vt = VT_EMPTY;
                break;
            case bool b:
                v.vt = VT_BOOL;
                v.boolVal = (short)(b ? -1 : 0);
                break;
            case int i:
                v.vt = VT_I4;
                v.lVal = i;
                break;
            case string s:
                v.vt = VT_BSTR;
                v.bstrVal = Marshal.StringToBSTR(s);
                break;
            default:
                v.vt = VT_EMPTY;
                break;
        }
        return v;
    }

    /// <summary>Frees an owned payload (BSTR or IUnknown). Call only on VARIANTs this code still owns.</summary>
    internal void Clear()
    {
        if (vt == VT_BSTR && _payload != nint.Zero)
            Marshal.FreeBSTR(_payload);
        else if (vt == VT_UNKNOWN && _payload != nint.Zero)
            Marshal.Release(_payload);
        vt = VT_EMPTY;
        _payload = nint.Zero;
    }
}
