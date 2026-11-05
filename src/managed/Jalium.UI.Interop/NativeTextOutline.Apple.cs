using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Interop;

internal static partial class NativeTextOutline
{
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private static class AppleKeys
    {
        private static readonly nint Text = NativeLibrary.Load(CoreText);
        private static readonly nint Foundation = NativeLibrary.Load(CoreFoundation);
        internal static readonly nint Font = Marshal.ReadIntPtr(NativeLibrary.GetExport(Text, "kCTFontAttributeName"));
        internal static readonly nint KeyCallbacks = NativeLibrary.GetExport(Foundation, "kCFTypeDictionaryKeyCallBacks");
        internal static readonly nint ValueCallbacks = NativeLibrary.GetExport(Foundation, "kCFTypeDictionaryValueCallBacks");
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct AppleRange(nint Location, nint Length);
    [StructLayout(LayoutKind.Sequential)] private struct ApplePoint { internal double X, Y; }
    [StructLayout(LayoutKind.Sequential)] private unsafe struct ApplePathElement { internal int Type; internal ApplePoint* Points; }
    private sealed class ApplePath
    {
        internal readonly StringBuilder Text = new();
        internal double X, Y, Baseline;
        internal bool Failed;
    }

    private static unsafe bool TryApple(string text, string family, float size, int weight, int style,
        out string path, out float width, out float baseline)
    {
        path = ""; width = baseline = 0;
        nint name = 0, font = 0, selected = 0, content = 0, attributes = 0, attributed = 0, line = 0;
        try
        {
            fixed (char* chars = family) name = CFStringCreateWithCharacters(0, chars, family.Length);
            if (name == 0 || (font = CTFontCreateWithName(name, size, 0)) == 0) return false;
            var traits = (weight >= 600 ? 2u : 0u) | (style != 0 ? 1u : 0u);
            selected = traits == 0 ? 0 : CTFontCreateCopyWithSymbolicTraits(font, size, 0, traits, 3);
            var key = AppleKeys.Font; var value = selected != 0 ? selected : font;
            attributes = CFDictionaryCreate(0, &key, &value, 1, AppleKeys.KeyCallbacks, AppleKeys.ValueCallbacks);
            fixed (char* chars = text) content = CFStringCreateWithCharacters(0, chars, text.Length);
            if (attributes == 0 || content == 0 || (attributed = CFAttributedStringCreate(0, content, attributes)) == 0 ||
                (line = CTLineCreateWithAttributedString(attributed)) == 0) return false;
            var advance = CTLineGetTypographicBounds(line, out var ascent, out _, out _);
            if (!double.IsFinite(advance) || !double.IsFinite(ascent) || advance < 0 || advance > float.MaxValue || ascent > float.MaxValue) return false;
            width = (float)advance; baseline = (float)ascent;
            var state = new ApplePath { Baseline = ascent };
            var handle = GCHandle.Alloc(state);
            try
            {
                var runs = CTLineGetGlyphRuns(line); var count = CFArrayGetCount(runs);
                for (nint r = 0; r < count; r++)
                {
                    var run = CFArrayGetValueAtIndex(runs, r); var glyphCount = CTRunGetGlyphCount(run);
                    if (glyphCount is < 0 or > 16384) return false;
                    var glyphs = new ushort[(int)glyphCount]; var positions = new ApplePoint[(int)glyphCount]; var indices = new nint[(int)glyphCount];
                    fixed (ushort* target = glyphs) CTRunGetGlyphs(run, default, target);
                    fixed (ApplePoint* target = positions) CTRunGetPositions(run, default, target);
                    fixed (nint* target = indices) CTRunGetStringIndices(run, default, target);
                    var runFont = CFDictionaryGetValue(CTRunGetAttributes(run), AppleKeys.Font);
                    if (runFont == 0) return false;
                    for (var i = 0; i < glyphs.Length; i++)
                    {
                        var outline = CTFontCreatePathForGlyph(runFont, glyphs[i], 0);
                        if (outline == 0)
                        {
                            // Spaces and formatting controls have no ink. A
                            // bitmap-only color glyph is not an empty contour.
                            var index = indices[i];
                            if (index < 0 || index >= text.Length || !(char.IsWhiteSpace(text[(int)index]) ||
                                char.GetUnicodeCategory(text, (int)index) is UnicodeCategory.Format or UnicodeCategory.Control)) return false;
                            continue;
                        }
                        try
                        {
                            state.X = positions[i].X; state.Y = positions[i].Y;
                            CGPathApply(outline, (void*)GCHandle.ToIntPtr(handle), &AppendApplePath);
                            if (state.Failed) return false;
                        }
                        finally { CFRelease(outline); }
                    }
                }
                path = state.Text.ToString(); return true;
            }
            finally { handle.Free(); }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return false; }
        finally
        {
            foreach (var owned in new[] { line, attributed, content, attributes, selected, font, name })
                if (owned != 0) CFRelease(owned);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void AppendApplePath(void* info, ApplePathElement* element)
    {
        var state = (ApplePath)GCHandle.FromIntPtr((nint)info).Target!;
        if (state.Failed) return;
        try
        {
            var count = element->Type switch { 0 or 1 => 1, 2 => 2, 3 => 3, 4 => 0, _ => -1 };
            if (count < 0 || state.Text.Length > 8 * 1024 * 1024) { state.Failed = true; return; }
            state.Text.Append(element->Type switch { 0 => 'M', 1 => 'L', 2 => 'Q', 3 => 'C', _ => 'Z' });
            for (var i = 0; i < count; i++)
            {
                var point = element->Points[i]; var x = state.X + point.X; var y = state.Baseline - state.Y - point.Y;
                if (!double.IsFinite(x) || !double.IsFinite(y)) { state.Failed = true; return; }
                state.Text.Append(x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(y.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
            }
        }
        catch (Exception) { state.Failed = true; } // Exceptions must not cross the native applier callback.
    }

    [LibraryImport(CoreFoundation)] private static unsafe partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint count);
    [LibraryImport(CoreFoundation)] private static unsafe partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallbacks, nint valueCallbacks);
    [LibraryImport(CoreFoundation)] private static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(CoreFoundation)] private static partial nint CFAttributedStringCreate(nint allocator, nint text, nint attributes);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetCount(nint array);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(CoreFoundation)] private static partial void CFRelease(nint value);
    [LibraryImport(CoreText)] private static partial nint CTFontCreateWithName(nint name, double size, nint matrix);
    [LibraryImport(CoreText)] private static partial nint CTFontCreateCopyWithSymbolicTraits(nint font, double size, nint matrix, uint traits, uint mask);
    [LibraryImport(CoreText)] private static partial nint CTFontCreatePathForGlyph(nint font, ushort glyph, nint matrix);
    [LibraryImport(CoreText)] private static partial nint CTLineCreateWithAttributedString(nint text);
    [LibraryImport(CoreText)] private static partial double CTLineGetTypographicBounds(nint line, out double ascent, out double descent, out double leading);
    [LibraryImport(CoreText)] private static partial nint CTLineGetGlyphRuns(nint line);
    [LibraryImport(CoreText)] private static partial nint CTRunGetGlyphCount(nint run);
    [LibraryImport(CoreText)] private static partial nint CTRunGetAttributes(nint run);
    [LibraryImport(CoreText)] private static unsafe partial void CTRunGetGlyphs(nint run, AppleRange range, ushort* glyphs);
    [LibraryImport(CoreText)] private static unsafe partial void CTRunGetPositions(nint run, AppleRange range, ApplePoint* positions);
    [LibraryImport(CoreText)] private static unsafe partial void CTRunGetStringIndices(nint run, AppleRange range, nint* indices);
    [LibraryImport(CoreGraphics)] private static unsafe partial void CGPathApply(nint path, void* info, delegate* unmanaged[Cdecl]<void*, ApplePathElement*, void> callback);
}
