using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Jalium.UI.Controls.Automation.Uia;

// ============================================================================
// Custom SAFEARRAY marshallers for the UIA source-generated interfaces.
//
// The COM source generator categorically rejects UnmanagedType.SafeArray and ships
// no built-in SAFEARRAY collection marshaller, yet several UIA members are SAFEARRAY
// on the wire: IRawElementProviderFragment::GetRuntimeId (SAFEARRAY(VT_I4)),
// ITextRangeProvider::GetBoundingRectangles (SAFEARRAY(VT_R8)), and the VT_UNKNOWN vectors
// ISelectionProvider::GetSelection / ITextProvider::GetSelection (SAFEARRAY of
// IRawElementProviderSimple* / ITextRangeProvider*). These stateless [CustomMarshaller]s build/read
// a real OLE SAFEARRAY so the ABI stays what Narrator/Inspect expect. (Note: GetEmbeddedFragmentRoots
// is SAFEARRAY(VARIANT) on the wire, NOT VT_UNKNOWN, so the provider keeps it null rather than reuse
// SafeArrayProviderMarshaller there.)
//
// Ownership / ref-count rules honored:
//   * As a return value from OUR CCW (managed provider called by native UIA), the
//     SAFEARRAY we allocate is handed to the caller, which destroys it (COM out-param
//     convention). The generated stub does not Free it in that direction.
//   * VT_UNKNOWN vectors are populated via SafeArrayAccessData (NOT SafeArrayPutElement, whose
//     by-reference element convention crashed in practice): the single CCW reference from
//     ConvertToUnmanaged is transferred straight into the slot, and SafeArrayDestroy Releases every
//     stored pointer (FADF_UNKNOWN) — so we neither AddRef again nor Free. Reads borrow the slot
//     pointer; wrapping it in an RCW takes its own reference, so nothing is released on that path.
// ============================================================================

internal static unsafe class UiaOleAut
{
    internal const ushort VT_I4 = 3;
    internal const ushort VT_R8 = 5;
    internal const ushort VT_UNKNOWN = 13;

    [DllImport("oleaut32.dll")]
    internal static extern nint SafeArrayCreateVector(ushort vt, int lLbound, uint cElements);

    [DllImport("oleaut32.dll")]
    internal static extern int SafeArrayDestroy(nint psa);

    [DllImport("oleaut32.dll")]
    internal static extern int SafeArrayAccessData(nint psa, out nint ppvData);

    [DllImport("oleaut32.dll")]
    internal static extern int SafeArrayUnaccessData(nint psa);

    [DllImport("oleaut32.dll")]
    internal static extern int SafeArrayGetLBound(nint psa, uint nDim, out int plLbound);

    [DllImport("oleaut32.dll")]
    internal static extern int SafeArrayGetUBound(nint psa, uint nDim, out int plUbound);
}

/// <summary>Marshals <see cref="int"/>[] &lt;-&gt; SAFEARRAY(VT_I4). Used by GetRuntimeId.</summary>
[CustomMarshaller(typeof(int[]), MarshalMode.Default, typeof(SafeArrayInt32Marshaller))]
internal static unsafe class SafeArrayInt32Marshaller
{
    public static nint ConvertToUnmanaged(int[]? managed)
    {
        if (managed is null) return 0;
        nint psa = UiaOleAut.SafeArrayCreateVector(UiaOleAut.VT_I4, 0, (uint)managed.Length);
        if (psa == 0) throw new OutOfMemoryException();
        if (managed.Length > 0)
        {
            int hr = UiaOleAut.SafeArrayAccessData(psa, out nint pData);
            if (hr < 0) { UiaOleAut.SafeArrayDestroy(psa); Marshal.ThrowExceptionForHR(hr); }
            try { managed.AsSpan().CopyTo(new Span<int>((void*)pData, managed.Length)); }
            finally { UiaOleAut.SafeArrayUnaccessData(psa); }
        }
        return psa;
    }

    public static int[]? ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0) return null;
        if (UiaOleAut.SafeArrayGetLBound(unmanaged, 1, out int lb) < 0) return null;
        if (UiaOleAut.SafeArrayGetUBound(unmanaged, 1, out int ub) < 0) return null;
        int count = ub - lb + 1;
        if (count <= 0) return [];
        var result = new int[count];
        if (UiaOleAut.SafeArrayAccessData(unmanaged, out nint pData) < 0) return null;
        try { new Span<int>((void*)pData, count).CopyTo(result); }
        finally { UiaOleAut.SafeArrayUnaccessData(unmanaged); }
        return result;
    }

    public static void Free(nint unmanaged)
    {
        if (unmanaged != 0) UiaOleAut.SafeArrayDestroy(unmanaged);
    }
}

/// <summary>
/// Marshals <see cref="double"/>[] &lt;-&gt; SAFEARRAY(VT_R8). Used by
/// <c>ITextRangeProvider.GetBoundingRectangles</c>, whose wire type is a flat SAFEARRAY of doubles
/// laid out as <c>[left, top, width, height, ...]</c> — four per bounding rectangle.
/// </summary>
[CustomMarshaller(typeof(double[]), MarshalMode.Default, typeof(SafeArrayDoubleMarshaller))]
internal static unsafe class SafeArrayDoubleMarshaller
{
    public static nint ConvertToUnmanaged(double[]? managed)
    {
        if (managed is null) return 0;
        nint psa = UiaOleAut.SafeArrayCreateVector(UiaOleAut.VT_R8, 0, (uint)managed.Length);
        if (psa == 0) throw new OutOfMemoryException();
        if (managed.Length > 0)
        {
            int hr = UiaOleAut.SafeArrayAccessData(psa, out nint pData);
            if (hr < 0) { UiaOleAut.SafeArrayDestroy(psa); Marshal.ThrowExceptionForHR(hr); }
            try { managed.AsSpan().CopyTo(new Span<double>((void*)pData, managed.Length)); }
            finally { UiaOleAut.SafeArrayUnaccessData(psa); }
        }
        return psa;
    }

    public static double[]? ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0) return null;
        if (UiaOleAut.SafeArrayGetLBound(unmanaged, 1, out int lb) < 0) return null;
        if (UiaOleAut.SafeArrayGetUBound(unmanaged, 1, out int ub) < 0) return null;
        int count = ub - lb + 1;
        if (count <= 0) return [];
        var result = new double[count];
        if (UiaOleAut.SafeArrayAccessData(unmanaged, out nint pData) < 0) return null;
        try { new Span<double>((void*)pData, count).CopyTo(result); }
        finally { UiaOleAut.SafeArrayUnaccessData(unmanaged); }
        return result;
    }

    public static void Free(nint unmanaged)
    {
        if (unmanaged != 0) UiaOleAut.SafeArrayDestroy(unmanaged);
    }
}

/// <summary>
/// Marshals <see cref="IUiaTextRangeProvider"/>[] &lt;-&gt; SAFEARRAY(VT_UNKNOWN). Used by
/// <c>ITextProvider.GetSelection</c> / <c>GetVisibleRanges</c>, whose wire type is a SAFEARRAY of
/// <c>ITextRangeProvider*</c> (VT_UNKNOWN elements). Element CCWs are built with the same
/// marshalling instance the generated interface stubs use so COM identities are consistent.
/// </summary>
[CustomMarshaller(typeof(IUiaTextRangeProvider[]), MarshalMode.Default, typeof(SafeArrayTextRangeMarshaller))]
internal static unsafe class SafeArrayTextRangeMarshaller
{
    public static nint ConvertToUnmanaged(IUiaTextRangeProvider[]? managed)
    {
        if (managed is null) return 0;
        nint psa = UiaOleAut.SafeArrayCreateVector(UiaOleAut.VT_UNKNOWN, 0, (uint)managed.Length);
        if (psa == 0) throw new OutOfMemoryException();
        if (managed.Length > 0)
        {
            int hr = UiaOleAut.SafeArrayAccessData(psa, out nint pData);
            if (hr < 0) { UiaOleAut.SafeArrayDestroy(psa); Marshal.ThrowExceptionForHR(hr); }
            try
            {
                // Write raw interface pointers straight into the vector (FADF_UNKNOWN). We transfer
                // the single CCW reference from ConvertToUnmanaged into the slot; SafeArrayDestroy
                // Releases each stored pointer, so we neither AddRef again nor Free here.
                var slots = (nint*)pData;
                for (int i = 0; i < managed.Length; i++)
                    slots[i] = managed[i] is null
                        ? 0
                        : (nint)ComInterfaceMarshaller<IUiaTextRangeProvider>.ConvertToUnmanaged(managed[i]);
            }
            finally { UiaOleAut.SafeArrayUnaccessData(psa); }
        }
        return psa;
    }

    public static IUiaTextRangeProvider[]? ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0) return null;
        if (UiaOleAut.SafeArrayGetLBound(unmanaged, 1, out int lb) < 0) return null;
        if (UiaOleAut.SafeArrayGetUBound(unmanaged, 1, out int ub) < 0) return null;
        int count = ub - lb + 1;
        if (count <= 0) return [];
        var result = new IUiaTextRangeProvider[count];
        if (UiaOleAut.SafeArrayAccessData(unmanaged, out nint pData) < 0) return null;
        try
        {
            // Each slot is an IUnknown* owned by the SAFEARRAY; wrapping it in an RCW takes its own
            // reference, so read the borrowed pointer without adding or releasing one here.
            var slots = (nint*)pData;
            for (int i = 0; i < count; i++)
                if (slots[i] != 0)
                    result[i] = ComInterfaceMarshaller<IUiaTextRangeProvider>.ConvertToManaged((void*)slots[i])!;
        }
        finally { UiaOleAut.SafeArrayUnaccessData(unmanaged); }
        return result;
    }

    public static void Free(nint unmanaged)
    {
        if (unmanaged != 0) UiaOleAut.SafeArrayDestroy(unmanaged);
    }
}

/// <summary>
/// Marshals <see cref="IRawElementProviderSimple"/>[] &lt;-&gt; SAFEARRAY(VT_UNKNOWN).
/// Used by GetSelection / GetEmbeddedFragmentRoots. Element CCWs are built with the same
/// marshalling instance the generated interface stubs use so COM identities are consistent.
/// </summary>
[CustomMarshaller(typeof(IRawElementProviderSimple[]), MarshalMode.Default, typeof(SafeArrayProviderMarshaller))]
internal static unsafe class SafeArrayProviderMarshaller
{
    public static nint ConvertToUnmanaged(IRawElementProviderSimple[]? managed)
    {
        if (managed is null) return 0;
        nint psa = UiaOleAut.SafeArrayCreateVector(UiaOleAut.VT_UNKNOWN, 0, (uint)managed.Length);
        if (psa == 0) throw new OutOfMemoryException();
        if (managed.Length > 0)
        {
            int hr = UiaOleAut.SafeArrayAccessData(psa, out nint pData);
            if (hr < 0) { UiaOleAut.SafeArrayDestroy(psa); Marshal.ThrowExceptionForHR(hr); }
            try
            {
                // Write raw interface pointers straight into the vector (FADF_UNKNOWN). We transfer
                // the single CCW reference from ConvertToUnmanaged into the slot; SafeArrayDestroy
                // Releases each stored pointer, so we neither AddRef again nor Free here.
                var slots = (nint*)pData;
                for (int i = 0; i < managed.Length; i++)
                    slots[i] = managed[i] is null
                        ? 0
                        : (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(managed[i]);
            }
            finally { UiaOleAut.SafeArrayUnaccessData(psa); }
        }
        return psa;
    }

    public static IRawElementProviderSimple[]? ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0) return null;
        if (UiaOleAut.SafeArrayGetLBound(unmanaged, 1, out int lb) < 0) return null;
        if (UiaOleAut.SafeArrayGetUBound(unmanaged, 1, out int ub) < 0) return null;
        int count = ub - lb + 1;
        if (count <= 0) return [];
        var result = new IRawElementProviderSimple[count];
        if (UiaOleAut.SafeArrayAccessData(unmanaged, out nint pData) < 0) return null;
        try
        {
            // Each slot is an IUnknown* owned by the SAFEARRAY; wrapping it in an RCW takes its own
            // reference, so read the borrowed pointer without adding or releasing one here.
            var slots = (nint*)pData;
            for (int i = 0; i < count; i++)
                if (slots[i] != 0)
                    result[i] = ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToManaged((void*)slots[i])!;
        }
        finally { UiaOleAut.SafeArrayUnaccessData(unmanaged); }
        return result;
    }

    public static void Free(nint unmanaged)
    {
        if (unmanaged != 0) UiaOleAut.SafeArrayDestroy(unmanaged);
    }
}
