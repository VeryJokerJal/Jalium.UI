using System.Runtime.InteropServices;
using Jalium.UI.Input;

namespace Jalium.UI.Interop;

/// <summary>Creates Jalium cursors from native cursor handles.</summary>
public static class CursorInteropHelper
{
    /// <summary>
    /// Creates a cursor while keeping the supplied safe handle alive for the
    /// lifetime of the returned object. Ownership remains with the caller.
    /// </summary>
    /// <param name="cursorHandle">A safe handle that refers to a native cursor.</param>
    /// <returns>A cursor backed by <paramref name="cursorHandle"/>.</returns>
    public static Cursor Create(SafeHandle cursorHandle)
    {
        ArgumentNullException.ThrowIfNull(cursorHandle);
        return new Cursor(cursorHandle);
    }
}
