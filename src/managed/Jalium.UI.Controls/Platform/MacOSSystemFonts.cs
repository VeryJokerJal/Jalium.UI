using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Platform;

// Core Text's UI roles do not require an NSApplication, an AppKit thread, or a
// renderer. Keep the system-selected size and family instead of guessing them.
internal static class MacOSSystemFonts
{
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly nint s_coreText;
    private static readonly nint s_weightKey;
    static MacOSSystemFonts()
    {
        s_coreText = NativeLibrary.Load(CoreText);
        s_weightKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(s_coreText, "kCTFontWeightTrait"));
    }

    internal static (string Family, double Size, FontStyle Style, FontWeight Weight)? Read(uint role)
    {
        nint font = 0, name = 0, traits = 0;
        try
        {
            font = CTFontCreateUIFontForLanguage(role, 0, 0);
            if (font == 0) return null;
            name = CTFontCopyFamilyName(font);
            long length = name == 0 ? 0 : CFStringGetLength(name);
            double size = CTFontGetSize(font);
            if (length is <= 0 or > 4096 || !double.IsFinite(size) || size <= 0) return null;
            var buffer = new char[(int)length]; CFStringGetCharacters(name, new(0, length), buffer);
            uint symbolic = CTFontGetSymbolicTraits(font);
            traits = CTFontCopyTraits(font);
            var number = traits == 0 ? 0 : CFDictionaryGetValue(traits, s_weightKey);
            double weight = 0;
            if (number != 0) CFNumberGetValue(number, 13, out weight); // kCFNumberDoubleType
            return (new string(buffer), size, (symbolic & 1) != 0 ? FontStyles.Italic : FontStyles.Normal,
                FontWeight.FromOpenTypeWeight(Weight(weight)));
        }
        finally
        {
            if (traits != 0) CFRelease(traits);
            if (name != 0) CFRelease(name);
            if (font != 0) CFRelease(font);
        }
    }

    // Same trait scale used by the native font matcher; system-selected faces
    // can have a weight different from the font file's default OS/2 instance.
    private static int Weight(double trait)
    {
        ReadOnlySpan<double> values = [-1, -.8, -.6, -.4, 0, .23, .3, .4, .56, .62, 1];
        trait = double.IsFinite(trait) ? Math.Clamp(trait, -1, 1) : 0;
        int lower = 0; while (lower < 9 && trait > values[lower + 1]) lower++;
        return Math.Clamp((int)Math.Round(100 * (lower + (trait - values[lower]) /
            (values[lower + 1] - values[lower])), MidpointRounding.AwayFromZero), 1, 1000);
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct Range(long Location, long Length);
    [DllImport(CoreText)] private static extern nint CTFontCreateUIFontForLanguage(uint role, double size, nint language);
    [DllImport(CoreText)] private static extern nint CTFontCopyFamilyName(nint font);
    [DllImport(CoreText)] private static extern double CTFontGetSize(nint font);
    [DllImport(CoreText)] private static extern uint CTFontGetSymbolicTraits(nint font);
    [DllImport(CoreText)] private static extern nint CTFontCopyTraits(nint font);
    [DllImport(CoreFoundation)] private static extern nint CFDictionaryGetValue(nint dictionary, nint key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(nint number, int type, out double value);
    [DllImport(CoreFoundation)] private static extern long CFStringGetLength(nint text);
    [DllImport(CoreFoundation)] private static extern void CFStringGetCharacters(nint text, Range range, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] buffer);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
}
