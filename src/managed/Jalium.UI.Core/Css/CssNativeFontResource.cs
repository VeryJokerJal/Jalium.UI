using Jalium.UI.Interop;
using Microsoft.Win32.SafeHandles;

namespace Jalium.UI.Styling;

internal sealed class CssNativeFontResource : SafeHandleZeroOrMinusOneIsInvalid
{
    private static long s_nextFamily;
    internal string Family { get; }
    private CssNativeFontResource(string family, nint pointer) : base(true) { Family = family; SetHandle(pointer); }

    internal static unsafe CssNativeFontResource? Create(byte[] bytes, string? postScriptName = null)
    {
        var family = "JaliumCss" + Interlocked.Increment(ref s_nextFamily).ToString("x16");
        // CoreText formats retain private data directly and resolve this alias
        // through our registry. Renaming is unnecessary and can discard large
        // variable-font name tables or the other faces of a font collection.
        if (!CssFontData.TryPrepare(bytes, family, out var prepared, postScriptName,
                preserveNames: OperatingSystem.IsMacOS())) return null;
        fixed (byte* data = prepared)
        {
            var pointer = NativeMethods.FontResourceRegister(family, (nint)data, (uint)prepared.Length);
            return pointer == 0 ? null : new(family, pointer);
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.FontResourceRelease(handle); return true; }
}
